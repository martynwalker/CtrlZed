using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

/// <summary>Traces a single-colour signature as closed SVG contours.</summary>
public static class SignatureTrace
{
    const int MaximumDimension=1600;
    const int MaximumBoundaryEdges=240000;
    const int MaximumOutputPoints=60000;
    const int MaximumContours=4096;
    const int MaximumDistanceChecks=8000000;
    const double SimplificationTolerance=0.35;

    readonly record struct Vertex(int X,int Y);

    /// <summary>
    /// Keeps dark ink on white paper, or light ink on a dark background, and
    /// removes the surrounding background. The caller supplies a frozen image
    /// when tracing on a worker thread. All returned data is ordinary UTF-8 SVG.
    /// </summary>
    public static byte[] Trace(BitmapSource source,int threshold=170,bool lightInk=false,string colour="#000000")
    {
        ArgumentNullException.ThrowIfNull(source);
        if(threshold is <0 or >255)throw new ArgumentOutOfRangeException(nameof(threshold));
        colour=NormaliseColour(colour);
        if(source.PixelWidth<1 || source.PixelHeight<1)
            throw new InvalidDataException("The signature image has no readable pixels.");

        BitmapSource working=source;
        var scale=Math.Min(1,MaximumDimension/(double)Math.Max(source.PixelWidth,source.PixelHeight));
        if(scale<1)
        {
            var resized=new TransformedBitmap(source,new ScaleTransform(scale,scale));
            resized.Freeze();
            working=resized;
        }
        if(working.Format!=PixelFormats.Bgra32)
        {
            var converted=new FormatConvertedBitmap(working,PixelFormats.Bgra32,null,0);
            converted.Freeze();
            working=converted;
        }
        var width=working.PixelWidth;
        var height=working.PixelHeight;
        var stride=checked(width*4);
        var pixels=new byte[checked(stride*height)];
        working.CopyPixels(pixels,stride,0);

        var mask=new bool[checked(width*height)];
        var minimumX=width;var minimumY=height;var maximumX=-1;var maximumY=-1;
        for(var y=0;y<height;y++)
        {
            for(var x=0;x<width;x++)
            {
                var index=y*stride+x*4;
                var alpha=pixels[index+3];
                if(alpha<16)continue;
                var luminance=(pixels[index+2]*299+pixels[index+1]*587+pixels[index]*114+500)/1000;
                // Transparent black must not become ink. Composite dark-ink
                // samples on white and light-ink samples on black first.
                var value=lightInk?(luminance*alpha+127)/255:
                    (luminance*alpha+255*(255-alpha)+127)/255;
                if(lightInk?value<threshold:value>threshold)continue;
                mask[y*width+x]=true;
                minimumX=Math.Min(minimumX,x);minimumY=Math.Min(minimumY,y);
                maximumX=Math.Max(maximumX,x);maximumY=Math.Max(maximumY,y);
            }
        }
        if(maximumX<minimumX || maximumY<minimumY)
            throw new InvalidDataException("No signature strokes were found. Adjust the threshold or choose Light ink.");

        var croppedWidth=maximumX-minimumX+1;
        var croppedHeight=maximumY-minimumY+1;
        var vertexStride=croppedWidth+1;
        var edges=new Dictionary<int,byte>();
        var seeds=new List<int>();

        bool IsInk(int x,int y)=>x>=0 && y>=0 && x<width && y<height && mask[y*width+x];
        void AddEdge(int x,int y,int direction)
        {
            if(seeds.Count>=MaximumBoundaryEdges)
                throw TooComplex();
            var vertex=y*vertexStride+x;
            edges.TryGetValue(vertex,out var directions);
            edges[vertex]=(byte)(directions|(1<<direction));
            seeds.Add(vertex*4+direction);
        }

        // Direct each boundary with ink on its right. Shared interior pixel
        // edges are omitted, producing outlines rather than pixel rectangles.
        for(var y=minimumY;y<=maximumY;y++)
        {
            for(var x=minimumX;x<=maximumX;x++)
            {
                if(!mask[y*width+x])continue;
                var localX=x-minimumX;var localY=y-minimumY;
                if(!IsInk(x,y-1))AddEdge(localX,localY,0);          // east
                if(!IsInk(x+1,y))AddEdge(localX+1,localY,1);        // south
                if(!IsInk(x,y+1))AddEdge(localX+1,localY+1,2);      // west
                if(!IsInk(x-1,y))AddEdge(localX,localY+1,3);        // north
            }
        }

        var path=new StringBuilder();
        var contours=0;var outputPoints=0;var distanceChecks=0;
        foreach(var seed in seeds)
        {
            var start=seed/4;var direction=seed%4;
            if(!edges.TryGetValue(start,out var initial) || (initial&(1<<direction))==0)continue;
            if(++contours>MaximumContours)throw TooComplex();
            var vertices=new List<Vertex>();
            var current=start;
            do
            {
                vertices.Add(new Vertex(current%vertexStride,current/vertexStride));
                if(vertices.Count>MaximumBoundaryEdges || !edges.TryGetValue(current,out var available) ||
                   (available&(1<<direction))==0)
                    throw new InvalidDataException("The signature outline could not be traced. Try another threshold.");
                available=(byte)(available&~(1<<direction));
                if(available==0)edges.Remove(current);else edges[current]=available;
                current+=direction switch {0=>1,1=>vertexStride,2=>-1,_=>-vertexStride};
                if(current==start)break;
                if(!edges.TryGetValue(current,out var next))
                    throw new InvalidDataException("The signature outline could not be closed. Try another threshold.");
                // At diagonally touching pixels, follow the right-hand edge
                // of this component instead of crossing into the other loop.
                direction=NextDirection(next,direction);
            }
            while(true);

            var outline=RemoveCollinear(vertices);
            if(outline.Count<3)continue;
            outline=SimplifyClosed(outline,ref distanceChecks);
            outputPoints+=outline.Count;
            if(outputPoints>MaximumOutputPoints)throw TooComplex();
            if(path.Length>0)path.Append(' ');
            path.Append('M');AppendVertex(path,outline[0]);
            for(var index=1;index<outline.Count;index++)
            {
                path.Append('L');AppendVertex(path,outline[index]);
            }
            path.Append('Z');
        }
        if(path.Length==0)
            throw new InvalidDataException("No closed signature strokes were found. Adjust the threshold.");

        // One even-odd path preserves enclosed background holes independently
        // of winding direction and retains small disconnected signature dots.
        var svg=string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{croppedWidth}\" height=\"{croppedHeight}\" viewBox=\"0 0 {croppedWidth} {croppedHeight}\"><path fill=\"{colour}\" fill-rule=\"evenodd\" d=\"{path}\"/></svg>");
        return Encoding.UTF8.GetBytes(svg);
    }

    static int NextDirection(byte available,int previous)
    {
        var right=(previous+1)%4;
        if((available&(1<<right))!=0)return right;
        if((available&(1<<previous))!=0)return previous;
        var left=(previous+3)%4;
        if((available&(1<<left))!=0)return left;
        var reverse=(previous+2)%4;
        if((available&(1<<reverse))!=0)return reverse;
        throw new InvalidDataException("The signature outline contains an open contour.");
    }

    static List<Vertex> RemoveCollinear(List<Vertex> vertices)
    {
        var result=new List<Vertex>();
        for(var index=0;index<vertices.Count;index++)
        {
            var previous=vertices[(index+vertices.Count-1)%vertices.Count];
            var current=vertices[index];
            var next=vertices[(index+1)%vertices.Count];
            var ax=current.X-previous.X;var ay=current.Y-previous.Y;
            var bx=next.X-current.X;var by=next.Y-current.Y;
            if(ax*by-ay*bx==0 && ax*bx+ay*by>0)continue;
            result.Add(current);
        }
        return result;
    }

    static List<Vertex> SimplifyClosed(List<Vertex> vertices,ref int distanceChecks)
    {
        if(vertices.Count<5 || distanceChecks>=MaximumDistanceChecks)return vertices;
        var split=1;long farthest=-1;
        for(var index=1;index<vertices.Count;index++)
        {
            var dx=vertices[index].X-vertices[0].X;var dy=vertices[index].Y-vertices[0].Y;
            var distance=(long)dx*dx+(long)dy*dy;
            if(distance>farthest){farthest=distance;split=index;}
        }
        var first=SimplifyOpen(vertices.GetRange(0,split+1),ref distanceChecks);
        var secondPoints=vertices.GetRange(split,vertices.Count-split);
        secondPoints.Add(vertices[0]);
        var second=SimplifyOpen(secondPoints,ref distanceChecks);
        first.RemoveAt(first.Count-1);
        second.RemoveAt(second.Count-1);
        first.AddRange(second);
        return first.Count>=3?first:vertices;
    }

    static List<Vertex> SimplifyOpen(List<Vertex> vertices,ref int distanceChecks)
    {
        if(vertices.Count<3 || distanceChecks>=MaximumDistanceChecks)return vertices;
        var keep=new bool[vertices.Count];
        keep[0]=true;keep[^1]=true;
        var ranges=new Stack<(int First,int Last)>();
        ranges.Push((0,vertices.Count-1));
        while(ranges.Count>0)
        {
            var range=ranges.Pop();
            var maximum=SimplificationTolerance*SimplificationTolerance;
            var selected=-1;
            for(var index=range.First+1;index<range.Last;index++)
            {
                if(++distanceChecks>MaximumDistanceChecks)return vertices;
                var distance=DistanceSquared(vertices[index],vertices[range.First],vertices[range.Last]);
                if(distance>maximum){maximum=distance;selected=index;}
            }
            if(selected<0)continue;
            keep[selected]=true;
            if(selected>range.First+1)ranges.Push((range.First,selected));
            if(selected<range.Last-1)ranges.Push((selected,range.Last));
        }
        var result=new List<Vertex>();
        for(var index=0;index<vertices.Count;index++)if(keep[index])result.Add(vertices[index]);
        return result;
    }

    static double DistanceSquared(Vertex point,Vertex first,Vertex last)
    {
        var dx=last.X-first.X;var dy=last.Y-first.Y;
        var length=(double)dx*dx+(double)dy*dy;
        var amount=length==0?0:Math.Clamp(((point.X-first.X)*(double)dx+(point.Y-first.Y)*(double)dy)/length,0,1);
        var x=point.X-first.X-amount*dx;var y=point.Y-first.Y-amount*dy;
        return x*x+y*y;
    }

    static void AppendVertex(StringBuilder text,Vertex vertex)
        =>text.Append(vertex.X.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(vertex.Y.ToString(CultureInfo.InvariantCulture));

    static string NormaliseColour(string colour)
    {
        if(colour==null || colour.Length!=7 || colour[0]!='#' || colour.Skip(1).Any(value=>!Uri.IsHexDigit(value)))
            throw new ArgumentException("Choose an ink colour in #RRGGBB format.",nameof(colour));
        return colour.ToUpperInvariant();
    }

    static InvalidDataException TooComplex()
        =>new("The image contains too much detail to trace as a signature. Crop it to the signature or adjust the threshold.");
}
