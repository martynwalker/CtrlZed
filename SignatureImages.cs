using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace CtrlZed;

/// <summary>The SVG master and the readable bitmap used by existing rich-text formats.</summary>
public sealed record SignatureAsset(BitmapImage Bitmap,DrawingImage? Vector,string? Svg,double Width,double Height);

/// <summary>Loads ordinary images and the outlined SVG subset used by signatures.</summary>
public static class SignatureImages
{
    public const int MaximumBytes=10*1024*1024;
    const int MaximumSvgCharacters=2*1024*1024;
    const int MaximumElements=10000;
    const int MaximumPathCharacters=1536*1024;
    const string SvgNamespace="http://www.w3.org/2000/svg";
    static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
    static readonly Regex Numbers=new(@"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(200));
    static readonly Regex TransformFunctions=new(@"([A-Za-z]+)\s*\(([^)]*)\)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(200));
    static readonly HashSet<string> StyleNames=new(StringComparer.OrdinalIgnoreCase)
    {
        "fill","stroke","color","fill-rule","fill-opacity","stroke-opacity","opacity",
        "stroke-width","stroke-linecap","stroke-linejoin","stroke-miterlimit",
        "stroke-dasharray","stroke-dashoffset","display","visibility"
    };

    sealed record Style(Color Colour,Color? Fill,Color? Stroke,bool FillCurrentColour,bool StrokeCurrentColour,double StrokeWidth,FillRule Rule,
        double FillOpacity,double StrokeOpacity,PenLineCap Cap,PenLineJoin Join,double Miter,
        double[] Dashes,double DashOffset,bool Visible,double LocalOpacity)
    {
        public static Style Default=>new(Colors.Black,Colors.Black,null,false,false,1,FillRule.Nonzero,
            1,1,PenLineCap.Flat,PenLineJoin.Miter,4,Array.Empty<double>(),0,true,1);
    }

    public static bool IsSvg(byte[] bytes)
    {
        if(bytes==null || bytes.Length==0)return false;
        var offset=bytes.Length>=3 && bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf?3:0;
        while(offset<bytes.Length && bytes[offset] is 9 or 10 or 13 or 32)offset++;
        var xml=offset<bytes.Length && bytes[offset]=='<' ||
            bytes.Length>=2 && (bytes[0]==0xff && bytes[1]==0xfe || bytes[0]==0xfe && bytes[1]==0xff || bytes[0]==0 && bytes[1]=='<');
        if(!xml)return false;
        try
        {
            using var stream=new MemoryStream(bytes,false);
            using var reader=XmlReader.Create(stream,ReaderSettings());
            reader.MoveToContent();
            return reader.LocalName=="svg";
        }
        catch(XmlException)
        {
            // An XML-like upload still goes through the strict SVG parser so
            // invalid declarations cannot be mistaken for a raster image.
            return true;
        }
    }

    public static SignatureAsset Load(byte[] bytes)
    {
        ValidateBytes(bytes);
        if(IsSvg(bytes))
        {
            using var stream=new MemoryStream(bytes,false);
            using var reader=XmlReader.Create(stream,ReaderSettings());
            return LoadSvgDocument(XDocument.Load(reader,LoadOptions.None));
        }
        using var header=new MemoryStream(bytes,false);
        var decoder=BitmapDecoder.Create(header,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
        var frame=decoder.Frames[0];
        var width=frame.PixelWidth;var height=frame.PixelHeight;
        if(width<=0 || height<=0 || (long)width*height>32000000)
            throw new InvalidDataException("Choose a signature image with no more than 32 million pixels.");
        using var raster=new MemoryStream(bytes,false);
        var bitmap=new BitmapImage();
        bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=raster;
        bitmap.EndInit();bitmap.Freeze();
        return new SignatureAsset(bitmap,null,null,width,height);
    }

    public static SignatureAsset FromSvg(string svg)
    {
        if(string.IsNullOrWhiteSpace(svg) || svg.Length>MaximumSvgCharacters)
            throw new InvalidDataException("Choose an SVG signature smaller than 2 MB.");
        using var text=new StringReader(svg);
        using var reader=XmlReader.Create(text,ReaderSettings());
        return LoadSvgDocument(XDocument.Load(reader,LoadOptions.None));
    }

    static XmlReaderSettings ReaderSettings()=>new()
    {
        DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,
        MaxCharactersInDocument=MaximumSvgCharacters,MaxCharactersFromEntities=0,
        IgnoreComments=true,IgnoreProcessingInstructions=false
    };

    static void ValidateBytes(byte[] bytes)
    {
        if(bytes==null || bytes.Length==0 || bytes.Length>MaximumBytes)
            throw new InvalidDataException("Choose an image file smaller than 10 MB.");
    }

    static SignatureAsset LoadSvgDocument(XDocument document)
    {
        var root=document.Root;
        if(root==null || root.Name.LocalName!="svg" || root.Name.NamespaceName is not "" and not SvgNamespace)
            throw new InvalidDataException("The selected file is not an SVG signature.");
        if(root.Attribute("transform")!=null)
            throw new InvalidDataException("Save the SVG signature with root transforms applied to its paths before importing it.");
        var elements=document.Descendants().ToArray();
        if(elements.Length>MaximumElements || elements.Any(element=>element.Ancestors().Count()>32))
            throw new InvalidDataException("The SVG signature is too complex. Simplify its paths before importing it.");
        if(document.DescendantNodes().OfType<XProcessingInstruction>().Any())
            throw new InvalidDataException("Use an outlined SVG signature with inline colours, without linked style sheets.");
        if(elements.Any(element=>element.Name.LocalName is "script" or "foreignObject" or "image" or "use" or
            "style" or "animate" or "animateTransform" or "animateMotion" or "set" ||
            element.Attributes().Any(attribute=>attribute.Name.LocalName.Equals("href",StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName=="class" && !string.IsNullOrWhiteSpace(attribute.Value) ||
                attribute.Name.LocalName.StartsWith("on",StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Use an outlined SVG signature with inline colours, without linked images, scripts or animation.");
        var viewBox=ParseViewBox((string?)root.Attribute("viewBox"));
        var width=RootLength((string?)root.Attribute("width"),viewBox?.Width??300);
        var height=RootLength((string?)root.Attribute("height"),viewBox?.Height??150);
        if(width<=0 || height<=0 || width>100000 || height>100000)
            throw new InvalidDataException("The SVG signature has invalid dimensions.");
        viewBox??=new Rect(0,0,width,height);
        if(viewBox.Value.Width<=0 || viewBox.Value.Height<=0)
            throw new InvalidDataException("The SVG viewBox must have a positive width and height.");

        var canonical=new XElement(XName.Get("svg",SvgNamespace),
            new XAttribute("width",Number(width)),new XAttribute("height",Number(height)),
            new XAttribute("viewBox",string.Join(" ",new[]{viewBox.Value.X,viewBox.Value.Y,viewBox.Value.Width,viewBox.Value.Height}.Select(Number))));
        var alignment=(string?)root.Attribute("preserveAspectRatio")??"xMidYMid meet";
        var fit=ViewBoxTransform(viewBox.Value,width,height,alignment);
        canonical.SetAttributeValue("preserveAspectRatio",alignment.Trim());
        var count=0;
        var content=ReadElement(root,Style.Default,canonical,true,ref count);
        if(count==0)throw new InvalidDataException("The SVG signature contains no visible paths or shapes.");
        var fitted=new DrawingGroup{Transform=new MatrixTransform(fit)};
        fitted.Children.Add(content);
        var drawing=new DrawingGroup{ClipGeometry=new RectangleGeometry(new Rect(0,0,width,height))};
        // Transparent paper fixes the image's intrinsic dimensions without
        // adding a visible background or cropping strokes to their own bounds.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,width,height))));
        drawing.Children.Add(fitted);drawing.Freeze();
        var vector=new DrawingImage(drawing);vector.Freeze();
        var svg=canonical.ToString(SaveOptions.DisableFormatting);
        if(svg.Length>MaximumSvgCharacters)
            throw new InvalidDataException("The SVG signature is too complex. Simplify its paths before importing it.");
        var bitmap=RenderBitmap(vector,width,height);
        return new SignatureAsset(bitmap,vector,svg,width,height);
    }

    static DrawingGroup ReadElement(XElement element,Style inherited,XElement canonical,bool root,ref int visibleCount)
    {
        var kind=element.Name.LocalName;
        if(element.Name.NamespaceName is not "" and not SvgNamespace)
            throw new InvalidDataException("The SVG contains an unsupported drawing element. Convert the signature to paths before importing it.");
        ValidateAttributes(element,kind,root);
        var (style,opacity,display)=ReadStyle(element,inherited);
        var result=new DrawingGroup{Opacity=opacity};
        WriteStyle(canonical,style,opacity);
        if(element.Attribute("transform") is { } transform)
        {
            var matrix=ParseTransform(transform.Value);
            result.Transform=new MatrixTransform(matrix);
            canonical.SetAttributeValue("transform",MatrixText(matrix));
        }
        if(!display || opacity<=0)
        {
            canonical.SetAttributeValue("display","none");
            return result;
        }
        if(root || kind=="g")
        {
            foreach(var child in element.Elements())
            {
                if(child.Name.LocalName is "title" or "desc" or "metadata" or "defs")continue;
                if(child.Name.LocalName is not "g" and not "path" and not "rect" and not "circle" and not "ellipse" and not "line" and not "polyline" and not "polygon")
                    throw new InvalidDataException($"SVG {child.Name.LocalName} is not supported for signatures. Convert text and effects to paths first.");
                var clean=new XElement(XName.Get(child.Name.LocalName,SvgNamespace));
                canonical.Add(clean);
                result.Children.Add(ReadElement(child,style,clean,false,ref visibleCount));
            }
            return result;
        }

        Geometry geometry;
        switch(kind)
        {
            case "path":
                var data=((string?)element.Attribute("d")??"").Trim();
                if(data.Length==0)return result;
                if(data.Length>MaximumPathCharacters || data.Any(character=>char.IsLetter(character) && !"MmLlHhVvCcSsQqTtAaZzEe".Contains(character)))
                    throw new InvalidDataException("The SVG path is invalid or too complex.");
                ValidateNumericValues(data);
                try {geometry=Geometry.Parse(data).Clone();}
                catch(FormatException ex){throw new InvalidDataException("The SVG path cannot be read. Save it as ordinary SVG paths and try again.",ex);}
                canonical.SetAttributeValue("d",data);
                break;
            case "rect":
                var x=AttributeLength(element,"x",0);var y=AttributeLength(element,"y",0);
                var rw=AttributeLength(element,"width",0);var rh=AttributeLength(element,"height",0);
                var rx=AttributeLength(element,"rx",element.Attribute("ry") is { } ryValue?Length(ryValue.Value):0);
                var ry=AttributeLength(element,"ry",rx);
                if(rw<0 || rh<0 || rx<0 || ry<0)throw new InvalidDataException("SVG rectangle dimensions cannot be negative.");
                geometry=new RectangleGeometry(new Rect(x,y,rw,rh),Math.Min(rx,rw/2),Math.Min(ry,rh/2));
                Attributes(canonical,("x",x),("y",y),("width",rw),("height",rh),("rx",rx),("ry",ry));
                break;
            case "circle":
            case "ellipse":
                var cx=AttributeLength(element,"cx",0);var cy=AttributeLength(element,"cy",0);
                var radiusX=AttributeLength(element,kind=="circle"?"r":"rx",0);
                var radiusY=kind=="circle"?radiusX:AttributeLength(element,"ry",0);
                if(radiusX<0 || radiusY<0)throw new InvalidDataException("SVG circle dimensions cannot be negative.");
                geometry=new EllipseGeometry(new Point(cx,cy),radiusX,radiusY);
                Attributes(canonical,("cx",cx),("cy",cy));
                if(kind=="circle")Attributes(canonical,("r",radiusX));else Attributes(canonical,("rx",radiusX),("ry",radiusY));
                break;
            case "line":
                var x1=AttributeLength(element,"x1",0);var y1=AttributeLength(element,"y1",0);
                var x2=AttributeLength(element,"x2",0);var y2=AttributeLength(element,"y2",0);
                geometry=new LineGeometry(new Point(x1,y1),new Point(x2,y2));
                Attributes(canonical,("x1",x1),("y1",y1),("x2",x2),("y2",y2));
                break;
            case "polygon":
            case "polyline":
                var points=NumberList((string?)element.Attribute("points")??"");
                if(points.Length<4 || points.Length%2!=0)throw new InvalidDataException("An SVG polygon has invalid points.");
                var stream=new StreamGeometry{FillRule=style.Rule};
                using(var context=stream.Open())
                {
                    context.BeginFigure(new Point(points[0],points[1]),true,kind=="polygon");
                    for(var index=2;index<points.Length;index+=2)context.LineTo(new Point(points[index],points[index+1]),true,false);
                }
                geometry=stream;
                canonical.SetAttributeValue("points",string.Join(" ",points.Select(Number)));
                break;
            default:throw new InvalidDataException("The SVG contains an unsupported shape.");
        }
        if(geometry is PathGeometry path)path.FillRule=style.Rule;
        if(geometry is StreamGeometry streamGeometry)streamGeometry.FillRule=style.Rule;
        geometry.Freeze();
        if(style.Visible)
        {
            var brush=Paint(style.Fill,style.FillOpacity);
            var stroke=Paint(style.Stroke,style.StrokeOpacity);
            Pen? pen=null;
            if(stroke!=null && style.StrokeWidth>0)
            {
                pen=new Pen(stroke,style.StrokeWidth)
                {
                    StartLineCap=style.Cap,EndLineCap=style.Cap,DashCap=style.Cap,
                    LineJoin=style.Join,MiterLimit=style.Miter
                };
                if(style.Dashes.Length>0 && style.Dashes.Any(value=>value>0))
                    pen.DashStyle=new DashStyle(style.Dashes.Select(value=>value/style.StrokeWidth),style.DashOffset/style.StrokeWidth);
                pen.Freeze();
            }
            if(brush!=null || pen!=null)
            {
                result.Children.Add(new GeometryDrawing(brush,pen,geometry));
                visibleCount++;
            }
        }
        return result;
    }

    static void ValidateAttributes(XElement element,string kind,bool root)
    {
        var shape=kind switch
        {
            "path"=>new[]{"d"},"rect"=>new[]{"x","y","width","height","rx","ry"},
            "circle"=>new[]{"cx","cy","r"},"ellipse"=>new[]{"cx","cy","rx","ry"},
            "line"=>new[]{"x1","y1","x2","y2"},"polygon" or "polyline"=>new[]{"points"},
            _=>Array.Empty<string>()
        };
        foreach(var attribute in element.Attributes())
        {
            var name=attribute.Name.LocalName;
            if(attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length>0 &&
                attribute.Name.NamespaceName is not SvgNamespace)continue;
            if(StyleNames.Contains(name) || shape.Contains(name) || name is "style" or "transform" or "id" or "class" or "role" ||
               name.StartsWith("data-",StringComparison.Ordinal) || name.StartsWith("aria-",StringComparison.Ordinal) ||
               root && (name is "width" or "height" or "viewBox" or "preserveAspectRatio" or "version" or "x" or "y"))continue;
            throw new InvalidDataException($"SVG attribute '{name}' is not supported. Convert text and effects to ordinary paths first.");
        }
    }

    static (Style Style,double Opacity,bool Display) ReadStyle(XElement element,Style inherited)
    {
        var values=element.Attributes().Where(attribute=>attribute.Name.NamespaceName.Length==0 && StyleNames.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute=>attribute.Name.LocalName,attribute=>attribute.Value,StringComparer.OrdinalIgnoreCase);
        if(element.Attribute("style") is { } inline)
            foreach(var part in inline.Value.Split(';',StringSplitOptions.RemoveEmptyEntries))
            {
                var split=part.IndexOf(':');
                if(split<1)throw new InvalidDataException("The SVG has an invalid style declaration.");
                var name=part[..split].Trim();var value=part[(split+1)..].Trim();
                if(!StyleNames.Contains(name))throw new InvalidDataException($"SVG style '{name}' is not supported for signatures.");
                values[name]=value;
            }
        string? Value(string name)=>values.TryGetValue(name,out var value) && !value.Trim().Equals("inherit",StringComparison.OrdinalIgnoreCase)?value.Trim():null;
        var colour=Value("color") is { } colourValue && !colourValue.Equals("currentColor",StringComparison.OrdinalIgnoreCase)?Colour(colourValue):inherited.Colour;
        var fillCurrent=Value("fill") is { } fillToken?fillToken.Equals("currentColor",StringComparison.OrdinalIgnoreCase):inherited.FillCurrentColour;
        var strokeCurrent=Value("stroke") is { } strokeToken?strokeToken.Equals("currentColor",StringComparison.OrdinalIgnoreCase):inherited.StrokeCurrentColour;
        Color? fill=Value("fill") is { } fillValue?ColourOrNone(fillValue,colour):fillCurrent?colour:inherited.Fill;
        Color? stroke=Value("stroke") is { } strokeValue?ColourOrNone(strokeValue,colour):strokeCurrent?colour:inherited.Stroke;
        var width=Value("stroke-width") is { } strokeWidth?Length(strokeWidth):inherited.StrokeWidth;
        if(width<0)throw new InvalidDataException("SVG stroke width cannot be negative.");
        var rule=Value("fill-rule") switch {null=>inherited.Rule,"evenodd"=>FillRule.EvenOdd,"nonzero"=>FillRule.Nonzero,_=>throw new InvalidDataException("The SVG fill rule is invalid.")};
        var fillOpacity=Value("fill-opacity") is { } f?Ratio(f):inherited.FillOpacity;
        var strokeOpacity=Value("stroke-opacity") is { } s?Ratio(s):inherited.StrokeOpacity;
        var opacity=values.TryGetValue("opacity",out var opacityValue) && opacityValue.Trim().Equals("inherit",StringComparison.OrdinalIgnoreCase)?
            inherited.LocalOpacity:Value("opacity") is { } o?Ratio(o):1;
        var cap=Value("stroke-linecap") switch {null=>inherited.Cap,"butt"=>PenLineCap.Flat,"round"=>PenLineCap.Round,"square"=>PenLineCap.Square,_=>throw new InvalidDataException("The SVG line cap is unsupported.")};
        var join=Value("stroke-linejoin") switch {null=>inherited.Join,"miter"=>PenLineJoin.Miter,"round"=>PenLineJoin.Round,"bevel"=>PenLineJoin.Bevel,_=>throw new InvalidDataException("The SVG line join is unsupported.")};
        var miter=Value("stroke-miterlimit") is { } m?Scalar(m):inherited.Miter;
        if(miter<1)throw new InvalidDataException("The SVG miter limit must be at least one.");
        var dashes=Value("stroke-dasharray") is { } d?d.Equals("none",StringComparison.OrdinalIgnoreCase)?Array.Empty<double>():NumberList(d):inherited.Dashes;
        if(dashes.Length>128 || dashes.Any(value=>value<0))throw new InvalidDataException("The SVG dash pattern is invalid.");
        if(dashes.Length%2!=0)dashes=dashes.Concat(dashes).ToArray();
        var dashOffset=Value("stroke-dashoffset") is { } offset?Length(offset):inherited.DashOffset;
        var display=Value("display") is not "none";
        var visible=Value("visibility") switch {"hidden" or "collapse"=>false,"visible"=>true,null=>inherited.Visible,_=>throw new InvalidDataException("The SVG visibility value is invalid.")};
        return (new Style(colour,fill,stroke,fillCurrent,strokeCurrent,width,rule,fillOpacity,strokeOpacity,cap,join,miter,dashes,dashOffset,visible,opacity),opacity,display);
    }

    static void WriteStyle(XElement target,Style style,double opacity)
    {
        target.SetAttributeValue("fill",style.Fill is { } fill?Rgb(fill):"none");
        target.SetAttributeValue("stroke",style.Stroke is { } stroke?Rgb(stroke):"none");
        target.SetAttributeValue("fill-rule",style.Rule==FillRule.Nonzero?"nonzero":"evenodd");
        target.SetAttributeValue("fill-opacity",Number(style.FillOpacity*(style.Fill?.A??255)/255.0));
        target.SetAttributeValue("stroke-opacity",Number(style.StrokeOpacity*(style.Stroke?.A??255)/255.0));
        target.SetAttributeValue("stroke-width",Number(style.StrokeWidth));
        target.SetAttributeValue("stroke-linecap",style.Cap==PenLineCap.Flat?"butt":style.Cap==PenLineCap.Round?"round":"square");
        target.SetAttributeValue("stroke-linejoin",style.Join==PenLineJoin.Miter?"miter":style.Join==PenLineJoin.Round?"round":"bevel");
        target.SetAttributeValue("stroke-miterlimit",Number(style.Miter));
        if(style.Dashes.Length>0)target.SetAttributeValue("stroke-dasharray",string.Join(" ",style.Dashes.Select(Number)));
        if(style.DashOffset!=0)target.SetAttributeValue("stroke-dashoffset",Number(style.DashOffset));
        if(opacity!=1)target.SetAttributeValue("opacity",Number(opacity));
        target.SetAttributeValue("visibility",style.Visible?"visible":"hidden");
    }

    static SolidColorBrush? Paint(Color? colour,double opacity)
    {
        if(colour is not { } value || opacity<=0 || value.A==0)return null;
        value.A=(byte)Math.Round(value.A*Math.Clamp(opacity,0,1));
        var brush=new SolidColorBrush(value);brush.Freeze();return brush;
    }

    static Color? ColourOrNone(string value,Color current)=>value.Equals("none",StringComparison.OrdinalIgnoreCase)?null:
        value.Equals("currentColor",StringComparison.OrdinalIgnoreCase)?current:Colour(value);

    static Color Colour(string value)
    {
        value=value.Trim();
        if(value.StartsWith('#'))
        {
            var hex=value[1..];
            if(hex.Length is 3 or 4)hex=string.Concat(hex.Select(character=>new string(character,2)));
            if(hex.Length is not 6 and not 8 || hex.Any(character=>!Uri.IsHexDigit(character)))
                throw new InvalidDataException("The SVG colour is invalid.");
            var red=byte.Parse(hex[..2],NumberStyles.HexNumber,Invariant);
            var green=byte.Parse(hex.Substring(2,2),NumberStyles.HexNumber,Invariant);
            var blue=byte.Parse(hex.Substring(4,2),NumberStyles.HexNumber,Invariant);
            var alpha=hex.Length==8?byte.Parse(hex.Substring(6,2),NumberStyles.HexNumber,Invariant):(byte)255;
            return Color.FromArgb(alpha,red,green,blue);
        }
        if(value.StartsWith("rgb(",StringComparison.OrdinalIgnoreCase) || value.StartsWith("rgba(",StringComparison.OrdinalIgnoreCase))
        {
            var open=value.IndexOf('(');
            if(!value.EndsWith(')'))throw new InvalidDataException("The SVG colour is invalid.");
            var parts=value[(open+1)..^1].Split(',',StringSplitOptions.TrimEntries);
            var alpha=value.StartsWith("rgba",StringComparison.OrdinalIgnoreCase);
            if(parts.Length!=(alpha?4:3))throw new InvalidDataException("The SVG colour is invalid.");
            byte Channel(string part)=>(byte)Math.Round(Math.Clamp(part.EndsWith('%')?Scalar(part[..^1])*255/100:Scalar(part),0,255));
            return Color.FromArgb(alpha?(byte)Math.Round(Ratio(parts[3])*255):(byte)255,Channel(parts[0]),Channel(parts[1]),Channel(parts[2]));
        }
        try{return (Color)ColorConverter.ConvertFromString(value)!;}
        catch(Exception ex) when(ex is FormatException or NotSupportedException)
        {throw new InvalidDataException("The SVG colour is unsupported. Use a solid ink colour.",ex);}
    }

    static Rect? ParseViewBox(string? value)
    {
        if(value==null)return null;
        var values=NumberList(value);
        if(values.Length!=4 || values[2]<=0 || values[3]<=0)throw new InvalidDataException("The SVG viewBox is invalid.");
        return new Rect(values[0],values[1],values[2],values[3]);
    }

    static Matrix ViewBoxTransform(Rect viewBox,double width,double height,string text)
    {
        var parts=text.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(parts.FirstOrDefault()=="defer")parts=parts.Skip(1).ToArray();
        var align=parts.FirstOrDefault()??"xMidYMid";
        var mode=parts.ElementAtOrDefault(1)??"meet";
        if(parts.Length>2 || mode is not "meet" and not "slice")throw new InvalidDataException("The SVG aspect-ratio rule is unsupported.");
        var sx=width/viewBox.Width;var sy=height/viewBox.Height;
        double dx=0,dy=0;
        if(align!="none")
        {
            if(!new[]{"xMinYMin","xMidYMin","xMaxYMin","xMinYMid","xMidYMid","xMaxYMid","xMinYMax","xMidYMax","xMaxYMax"}.Contains(align))
                throw new InvalidDataException("The SVG aspect-ratio alignment is unsupported.");
            sx=sy=mode=="slice"?Math.Max(sx,sy):Math.Min(sx,sy);
            dx=(width-viewBox.Width*sx)*(align.StartsWith("xMid",StringComparison.Ordinal)?0.5:align.StartsWith("xMax",StringComparison.Ordinal)?1:0);
            dy=(height-viewBox.Height*sy)*(align.EndsWith("YMid",StringComparison.Ordinal)?0.5:align.EndsWith("YMax",StringComparison.Ordinal)?1:0);
        }
        return new Matrix(sx,0,0,sy,dx-viewBox.X*sx,dy-viewBox.Y*sy);
    }

    static Matrix ParseTransform(string value)
    {
        var result=Matrix.Identity;var position=0;
        foreach(Match match in TransformFunctions.Matches(value))
        {
            if(!Separators(value[position..match.Index]))throw new InvalidDataException("The SVG transform is invalid.");
            var args=NumberList(match.Groups[2].Value);
            var matrix=Matrix.Identity;
            switch(match.Groups[1].Value)
            {
                case "matrix" when args.Length==6:matrix=new Matrix(args[0],args[1],args[2],args[3],args[4],args[5]);break;
                case "translate" when args.Length is 1 or 2:matrix.Translate(args[0],args.Length==2?args[1]:0);break;
                case "scale" when args.Length is 1 or 2:matrix.Scale(args[0],args.Length==2?args[1]:args[0]);break;
                case "rotate" when args.Length is 1 or 3:
                    if(args.Length==1)matrix.Rotate(args[0]);else matrix.RotateAt(args[0],args[1],args[2]);break;
                case "skewX" when args.Length==1:matrix.M21=Math.Tan(args[0]*Math.PI/180);break;
                case "skewY" when args.Length==1:matrix.M12=Math.Tan(args[0]*Math.PI/180);break;
                default:throw new InvalidDataException("The SVG transform is unsupported.");
            }
            // SVG's transform list applies its rightmost operation first.
            result.Prepend(matrix);
            if(!new[]{result.M11,result.M12,result.M21,result.M22,result.OffsetX,result.OffsetY}.All(number=>double.IsFinite(number) && Math.Abs(number)<=10000000))
                throw new InvalidDataException("The SVG transform is invalid.");
            position=match.Index+match.Length;
        }
        if(!Separators(value[position..]))throw new InvalidDataException("The SVG transform is invalid.");
        return result;
    }

    static string MatrixText(Matrix matrix)=>"matrix("+string.Join(" ",new[]{matrix.M11,matrix.M12,matrix.M21,matrix.M22,matrix.OffsetX,matrix.OffsetY}.Select(Number))+")";
    static string Rgb(Color value)=>$"#{value.R:X2}{value.G:X2}{value.B:X2}";
    static string Number(double value)=>value.ToString("R",Invariant);
    static void Attributes(XElement element,params (string Name,double Value)[] attributes)
    {foreach(var (name,value) in attributes)element.SetAttributeValue(name,Number(value));}
    static double AttributeLength(XElement element,string name,double fallback)
        =>element.Attribute(name) is { } value?Length(value.Value):fallback;
    static double RootLength(string? value,double fallback)=>value==null || value.Trim().EndsWith('%')?fallback:Length(value);
    static double Ratio(string value)=>Math.Clamp(value.Trim().EndsWith('%')?Scalar(value.Trim()[..^1])/100:Scalar(value),0,1);
    static double Length(string value)
    {
        value=value.Trim();
        var match=Numbers.Match(value);
        if(!match.Success || match.Index!=0)throw new InvalidDataException("The SVG length is invalid.");
        var suffix=value[match.Length..].Trim();
        var scale=suffix switch {"" or "px"=>1,"mm"=>96/25.4,"cm"=>96/2.54,"in"=>96,"pt"=>96/72.0,"pc"=>16,_=>throw new InvalidDataException("The SVG length unit is unsupported.")};
        return Checked(Scalar(match.Value)*scale);
    }
    static double Scalar(string value)
    {
        if(!double.TryParse(value,NumberStyles.Float,Invariant,out var number))throw new InvalidDataException("The SVG contains an invalid number.");
        return Checked(number);
    }
    static double Checked(double value)=>double.IsFinite(value) && Math.Abs(value)<=10000000?value:
        throw new InvalidDataException("The SVG contains an out-of-range coordinate.");
    static double[] NumberList(string value)
    {
        var values=new List<double>();var position=0;
        foreach(Match match in Numbers.Matches(value))
        {
            if(!Separators(value[position..match.Index]))throw new InvalidDataException("The SVG has an invalid number list.");
            values.Add(Scalar(match.Value));position=match.Index+match.Length;
            if(values.Count>100000)throw new InvalidDataException("The SVG has too many coordinates.");
        }
        if(!Separators(value[position..]))throw new InvalidDataException("The SVG has an invalid number list.");
        return values.ToArray();
    }
    static bool Separators(string value)=>value.All(character=>char.IsWhiteSpace(character) || character==',');
    static void ValidateNumericValues(string data)
    {
        var count=0;
        foreach(Match match in Numbers.Matches(data))
        {
            if(++count>200000)throw new InvalidDataException("The SVG path has too many coordinates.");
            _ = Scalar(match.Value);
        }
    }

    static BitmapImage RenderBitmap(DrawingImage vector,double width,double height)
    {
        var scale=1536/Math.Max(width,height);
        var pixelWidth=Math.Max(1,(int)Math.Round(width*scale));
        var pixelHeight=Math.Max(1,(int)Math.Round(height*scale));
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())dc.DrawImage(vector,new Rect(0,0,pixelWidth,pixelHeight));
        var target=new RenderTargetBitmap(pixelWidth,pixelHeight,96,96,PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream=new MemoryStream();encoder.Save(stream);stream.Position=0;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
        return bitmap;
    }
}
