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

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(PageContentHeight < 100 || ActualWidth < 100 || ActualHeight < 100)return;

        var pages=Math.Max(1,(int)Math.Ceiling(ActualHeight/PageContentHeight));
        var edgePen=new Pen(new SolidColorBrush(Color.FromArgb(150,125,125,125)),1);
        var gapBrush=new SolidColorBrush(Color.FromArgb(210,218,218,218));
        var labelBrush=new SolidColorBrush(Color.FromRgb(105,105,105));

        for(int i=0;i<pages;i++)
        {
            var top=i*PageContentHeight;
            var bottom=(i+1)*PageContentHeight;

            var headerY=i==0?top+4:top+(PageGap/2)+5;
            DrawText(dc,Expand?.Invoke(Header,i+1,pages)??Header,10,headerY,ActualWidth-20,TextAlignment.Center,Brushes.Gray);

            if(bottom<=ActualHeight+1)
            {
                var half=PageGap/2;
                var gapTop=Math.Max(top,bottom-half);
                var gapBottom=Math.Min(ActualHeight,bottom+half);
                var gapHeight=Math.Max(0,gapBottom-gapTop);

                DrawText(dc,Expand?.Invoke(Footer,i+1,pages)??Footer,10,Math.Max(top+28,gapTop-22),ActualWidth-20,TextAlignment.Center,Brushes.Gray);

                dc.DrawLine(edgePen,new Point(0,gapTop),new Point(ActualWidth,gapTop));
                if(gapHeight>0)
                    dc.DrawRectangle(gapBrush,null,new Rect(0,gapTop,ActualWidth,gapHeight));
                dc.DrawLine(edgePen,new Point(0,gapBottom),new Point(ActualWidth,gapBottom));

                DrawText(dc,$"End of page {i+1}",10,gapTop+4,120,TextAlignment.Left,labelBrush);
            }
        }
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
