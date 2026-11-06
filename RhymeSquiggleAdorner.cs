using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public sealed record RhymeMarkedRange(TextPointer Start,TextPointer End);

public sealed class RhymeSquiggleAdorner : Adorner
{
    static readonly Pen RhymePen=CreatePen(Color.FromRgb(28,145,78));
    IReadOnlyList<RhymeMarkedRange> ranges=Array.Empty<RhymeMarkedRange>();
    Geometry? cachedGeometry;
    double cachedWidth=-1;

    public RhymeSquiggleAdorner(UIElement adornedElement):base(adornedElement)
    {
        IsHitTestVisible=false;
        SnapsToDevicePixels=true;
        ClipToBounds=true;
    }

    public void SetRanges(IReadOnlyList<RhymeMarkedRange> value)
    {
        ranges=value??Array.Empty<RhymeMarkedRange>();
        cachedGeometry=null;
        cachedWidth=-1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if(cachedGeometry==null || Math.Abs(cachedWidth-ActualWidth)>0.5)
        {
            cachedWidth=ActualWidth;
            cachedGeometry=BuildGeometry();
        }
        if(cachedGeometry!=null)drawingContext.DrawGeometry(null,RhymePen,cachedGeometry);
    }

    Geometry BuildGeometry()
    {
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            foreach(var range in ranges)
            {
                try{AppendRange(context,range.Start,range.End);}
                catch(InvalidOperationException){ }
                catch(ArgumentException){ }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    void AppendRange(StreamGeometryContext context,TextPointer start,TextPointer end)
    {
        if(start.CompareTo(end)>=0)return;
        var first=start.GetCharacterRect(LogicalDirection.Forward);
        var last=end.GetCharacterRect(LogicalDirection.Backward);
        if(first.IsEmpty || last.IsEmpty)return;

        var lineHeight=Math.Max(8,first.Height);
        var sameLine=Math.Abs(first.Top-last.Top)<lineHeight*0.6;
        var y=first.Bottom-0.5;
        var x1=Math.Max(0,first.Left);
        if(sameLine)
        {
            var x2=Math.Max(x1+4,last.Right>last.Left?last.Right:last.Left);
            AppendWave(context,x1,x2,y);
            return;
        }

        var width=ActualWidth>12?ActualWidth:AdornedElement.RenderSize.Width;
        AppendWave(context,x1,Math.Max(x1+4,width-6),y);
        var endY=last.Bottom-0.5;
        AppendWave(context,4,Math.Max(8,last.Right>last.Left?last.Right:last.Left),endY);
    }

    static void AppendWave(StreamGeometryContext context,double x1,double x2,double y)
    {
        if(double.IsNaN(x1)||double.IsNaN(x2)||double.IsNaN(y)||x2<=x1)return;
        context.BeginFigure(new Point(x1,y),false,false);
        var up=true;
        for(var x=x1+2.2;x<x2;x+=2.2)
        {
            context.LineTo(new Point(x,y+(up?-1.25:1.25)),true,false);
            up=!up;
        }
        context.LineTo(new Point(x2,y),true,false);
    }

    static Pen CreatePen(Color colour)
    {
        var brush=new SolidColorBrush(colour);
        brush.Freeze();
        var pen=new Pen(brush,0.9);
        pen.Freeze();
        return pen;
    }
}
