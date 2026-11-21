using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CtrlZed;

public sealed class PageGuideLayer : FrameworkElement
{
    public double PageContentHeight { get; set; } = 979;
    public double PageGap { get; set; } = 24;
    public string Header { get; set; } = "";
    public string Footer { get; set; } = "";
    public Func<string,int,int,string>? Expand { get; set; }
    public IReadOnlyList<double> ManualBreakY { get; set; } = Array.Empty<double>();
    public bool UseCachedPaginationMap { get; set; }
    public int CachedPageCount { get; set; }
    public IReadOnlyList<double> CachedPageStartsY { get; set; } = Array.Empty<double>();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(PageContentHeight < 100 || ActualWidth < 100 || ActualHeight < 100)return;

        if(UseCachedPaginationMap && CachedPageCount>0)
            DrawCachedPages(dc);
        else
            DrawLegacyGuides(dc);

        DrawManualBreaks(dc);
    }

    void DrawCachedPages(DrawingContext dc)
    {
        const double sideGutter=52;
        var leftEdge=Math.Max(2,sideGutter-5);
        var rightEdge=Math.Min(ActualWidth-2,ActualWidth-sideGutter+5);
        var edgePen=new Pen(new SolidColorBrush(Color.FromArgb(150,160,157,147)),1);
        var breakPen=new Pen(new SolidColorBrush(Color.FromArgb(170,145,142,132)),1)
        {
            DashStyle=DashStyles.Dash
        };
        var gutterBrush=new SolidColorBrush(Color.FromRgb(239,236,226));
        var labelBrush=new SolidColorBrush(Color.FromRgb(88,88,84));

        var starts=CachedPageStartsY
            .Where(double.IsFinite)
            .Select(y=>Math.Clamp(y,0,ActualHeight))
            .OrderBy(y=>y)
            .DistinctBy(y=>Math.Round(y,1))
            .ToList();
        if(starts.Count==0 || starts[0]>1)starts.Insert(0,0);

        var fallback=Math.Max(160,PageContentHeight);
        while(starts.Count<CachedPageCount)
        {
            var next=Math.Min(ActualHeight,starts[^1]+fallback);
            if(next<=starts[^1]+1)break;
            starts.Add(next);
        }

        for(var i=0;i<CachedPageCount;i++)
        {
            var top=i<starts.Count?starts[i]:Math.Min(ActualHeight,i*fallback);
            var bottom=i+1<starts.Count?starts[i+1]:ActualHeight;
            if(bottom<top)bottom=top;

            dc.DrawLine(edgePen,new Point(leftEdge,top),new Point(leftEdge,bottom));
            dc.DrawLine(edgePen,new Point(rightEdge,top),new Point(rightEdge,bottom));
            dc.DrawLine(edgePen,new Point(0,top),new Point(Math.Max(0,leftEdge-7),top));
            dc.DrawLine(edgePen,new Point(Math.Min(ActualWidth,rightEdge+7),top),new Point(ActualWidth,top));

            var labelY=Math.Min(Math.Max(top+5,0),Math.Max(0,ActualHeight-20));
            var labelX=Math.Max(0,ActualWidth-sideGutter+7);
            dc.DrawRectangle(Brushes.White,null,new Rect(labelX,labelY,sideGutter-9,18));
            DrawText(dc,$"{i+1}/{CachedPageCount}",labelX+2,labelY+1,sideGutter-13,TextAlignment.Right,labelBrush);

            if(i<CachedPageCount-1 && bottom>1 && bottom<ActualHeight-1)
            {
                dc.DrawRectangle(gutterBrush,null,new Rect(0,bottom-5,Math.Max(0,leftEdge-7),10));
                dc.DrawRectangle(gutterBrush,null,new Rect(Math.Min(ActualWidth,rightEdge+7),bottom-5,
                    Math.Max(0,ActualWidth-rightEdge-7),10));
                dc.DrawLine(breakPen,new Point(0,bottom),new Point(Math.Max(0,leftEdge-7),bottom));
                dc.DrawLine(breakPen,new Point(Math.Min(ActualWidth,rightEdge+7),bottom),new Point(ActualWidth,bottom));
            }
        }
    }

    void DrawLegacyGuides(DrawingContext dc)
    {
        var stride=Math.Max(PageContentHeight+PageGap,PageContentHeight);
        var pages=Math.Max(1,(int)Math.Ceiling((ActualHeight+PageGap)/stride));
        var labelBrush=new SolidColorBrush(Color.FromRgb(105,105,105));

        for(int i=0;i<pages;i++)
        {
            var top=i*stride;
            var bottom=top+PageContentHeight;

            const double sideGutter=52;
            var contentX=sideGutter;
            var contentWidth=Math.Max(20,ActualWidth-sideGutter*2);
            var headerY=top+4;
            DrawTriplet(dc,Expand?.Invoke(Header,i+1,pages)??Header,contentX,headerY,contentWidth,Brushes.Gray);

            if(bottom<=ActualHeight+1)
            {
                var lineY=bottom;
                DrawTriplet(dc,Expand?.Invoke(Footer,i+1,pages)??Footer,contentX,Math.Max(top+28,lineY-22),contentWidth,Brushes.Gray);

                var pagePen=new Pen(new SolidColorBrush(Color.FromArgb(145,125,125,125)),1){DashStyle=DashStyles.Dash};
                dc.DrawLine(pagePen,new Point(0,lineY),new Point(Math.Max(0,sideGutter-8),lineY));
                dc.DrawLine(pagePen,new Point(Math.Min(ActualWidth,ActualWidth-sideGutter+8),lineY),new Point(ActualWidth,lineY));

                var nextBodyY=lineY+PageGap;
                if(i<pages-1 && nextBodyY<ActualHeight)
                {
                    dc.DrawLine(pagePen,new Point(0,nextBodyY),new Point(Math.Max(0,sideGutter-8),nextBodyY));
                    dc.DrawLine(pagePen,new Point(Math.Min(ActualWidth,ActualWidth-sideGutter+8),nextBodyY),new Point(ActualWidth,nextBodyY));
                }

                var labelX=Math.Max(0,ActualWidth-sideGutter+6);
                var labelY=lineY+Math.Max(0,(PageGap-18)/2);
                dc.DrawRectangle(Brushes.White,null,new Rect(labelX,labelY,sideGutter-8,18));
                DrawText(dc,$"Page {i+1}",labelX+2,labelY+1,sideGutter-12,TextAlignment.Right,labelBrush);
            }
        }
    }

    void DrawManualBreaks(DrawingContext dc)
    {
        var manualPen=new Pen(new SolidColorBrush(Color.FromRgb(115,135,160)),1){DashStyle=DashStyles.Dot};
        foreach(var y in ManualBreakY.Where(y=>y>16 && y<ActualHeight-16))
        {
            const double sideGutter=52;
            dc.DrawLine(manualPen,new Point(0,y),new Point(Math.Max(0,sideGutter-8),y));
            dc.DrawLine(manualPen,new Point(Math.Min(ActualWidth,ActualWidth-sideGutter+8),y),new Point(ActualWidth,y));
            dc.DrawRectangle(Brushes.White,null,new Rect(2,y-9,sideGutter-4,18));
            DrawText(dc,"Break",5,y-8,sideGutter-10,TextAlignment.Left,new SolidColorBrush(Color.FromRgb(85,105,130)));
        }
    }

    static void DrawTriplet(DrawingContext dc,string text,double x,double y,double width,Brush brush)
    {
        var parts=(text??"").Split('\t');
        if(parts.Length<2){DrawText(dc,text,x,y,width,TextAlignment.Center,brush);return;}
        var third=width/3;
        DrawText(dc,parts.ElementAtOrDefault(0)??"",x,y,third,TextAlignment.Left,brush);
        DrawText(dc,parts.ElementAtOrDefault(1)??"",x+third,y,third,TextAlignment.Center,brush);
        DrawText(dc,parts.ElementAtOrDefault(2)??"",x+third*2,y,third,TextAlignment.Right,brush);
    }

    static void DrawText(DrawingContext dc,string text,double x,double y,double width,TextAlignment alignment,Brush brush)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        var formatted=new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Calibri"),
            9*96/72,
            brush,
            1.0)
        {
            MaxTextWidth=Math.Max(20,width),
            TextAlignment=alignment,
            Trimming=TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted,new Point(x,y));
    }
}
