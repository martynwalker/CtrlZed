using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public sealed record GrammarMarkedRange(TextPointer Start,TextPointer End,GrammarFindingKind Kind);

public sealed class GrammarSquiggleAdorner : Adorner
{
    static readonly Pen GrammarPen=CreatePen(Color.FromRgb(35,105,190));
    IReadOnlyList<GrammarMarkedRange> ranges=Array.Empty<GrammarMarkedRange>();

    public GrammarSquiggleAdorner(UIElement adornedElement):base(adornedElement)
    {
        IsHitTestVisible=false;
        SnapsToDevicePixels=true;
    }

    public void SetRanges(IReadOnlyList<GrammarMarkedRange> value)
    {
        ranges=value??Array.Empty<GrammarMarkedRange>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        foreach(var range in ranges)
        {
            try{DrawRange(drawingContext,range.Start,range.End);}
            catch(InvalidOperationException){ }
            catch(ArgumentException){ }
        }
    }

    void DrawRange(DrawingContext dc,TextPointer start,TextPointer end)
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
            DrawWave(dc,x1,x2,y);
            return;
        }

        DrawWave(dc,x1,Math.Max(x1+4,ActualWidth-6),y);
        var endY=last.Bottom-0.5;
        DrawWave(dc,4,Math.Max(8,last.Right>last.Left?last.Right:last.Left),endY);
    }

    static void DrawWave(DrawingContext dc,double x1,double x2,double y)
    {
        if(double.IsNaN(x1)||double.IsNaN(x2)||double.IsNaN(y)||x2<=x1)return;
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            context.BeginFigure(new Point(x1,y),false,false);
            var up=true;
            for(var x=x1+2.2;x<x2;x+=2.2)
            {
                context.LineTo(new Point(x, y+(up?-1.25:1.25)),true,false);
                up=!up;
            }
            context.LineTo(new Point(x2,y),true,false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null,GrammarPen,geometry);
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
