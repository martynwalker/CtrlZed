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
            DrawTriplet(dc,Expand?.Invoke(Header,i+1,pages)??Header,10,headerY,ActualWidth-20,Brushes.Gray);

            if(bottom<=ActualHeight+1)
            {
                var half=PageGap/2;
                var gapTop=Math.Max(top,bottom-half);
                var gapBottom=Math.Min(ActualHeight,bottom+half);
                var gapHeight=Math.Max(0,gapBottom-gapTop);

                DrawTriplet(dc,Expand?.Invoke(Footer,i+1,pages)??Footer,10,Math.Max(top+28,gapTop-22),ActualWidth-20,Brushes.Gray);

                dc.DrawLine(edgePen,new Point(0,gapTop),new Point(ActualWidth,gapTop));
                if(gapHeight>0)dc.DrawRectangle(gapBrush,null,new Rect(0,gapTop,ActualWidth,gapHeight));
                dc.DrawLine(edgePen,new Point(0,gapBottom),new Point(ActualWidth,gapBottom));

                DrawText(dc,$"End of page {i+1}",10,gapTop+4,120,TextAlignment.Left,labelBrush);
            }
        }

        var manualBrush=new SolidColorBrush(Color.FromArgb(225,232,238,245));
        var manualPen=new Pen(new SolidColorBrush(Color.FromRgb(115,135,160)),1){DashStyle=DashStyles.Dash};
        foreach(var y in ManualBreakY.Where(y=>y>16 && y<ActualHeight-16))
        {
            var top=y-10;
            dc.DrawRectangle(manualBrush,null,new Rect(0,top,ActualWidth,20));
            dc.DrawLine(manualPen,new Point(0,y),new Point(ActualWidth,y));
            DrawText(dc,"Manual page break",10,top+2,140,TextAlignment.Left,new SolidColorBrush(Color.FromRgb(85,105,130)));
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
