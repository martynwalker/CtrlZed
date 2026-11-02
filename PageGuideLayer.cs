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
        var labelBrush=new SolidColorBrush(Color.FromRgb(105,105,105));

        for(int i=0;i<pages;i++)
        {
            var top=i*PageContentHeight;
            var bottom=(i+1)*PageContentHeight;

            var headerY=i==0?top+4:top+(PageGap/2)+5;
            DrawTriplet(dc,Expand?.Invoke(Header,i+1,pages)??Header,10,headerY,ActualWidth-20,Brushes.Gray);

            if(bottom<=ActualHeight+1)
            {
                var lineY=bottom;
                DrawTriplet(dc,Expand?.Invoke(Footer,i+1,pages)??Footer,10,Math.Max(top+28,lineY-22),ActualWidth-20,Brushes.Gray);

                var pagePen=new Pen(new SolidColorBrush(Color.FromArgb(135,125,125,125)),1){DashStyle=DashStyles.Dash};
                dc.DrawLine(pagePen,new Point(0,lineY),new Point(ActualWidth,lineY));
                dc.DrawRectangle(Brushes.White,null,new Rect(Math.Max(0,ActualWidth-64),lineY-9,60,18));
                DrawText(dc,$"Page {i+1}",Math.Max(0,ActualWidth-62),Math.Max(0,lineY-8),56,TextAlignment.Right,labelBrush);
            }
        }

        var manualPen=new Pen(new SolidColorBrush(Color.FromRgb(115,135,160)),1){DashStyle=DashStyles.Dot};
        foreach(var y in ManualBreakY.Where(y=>y>16 && y<ActualHeight-16))
        {
            dc.DrawLine(manualPen,new Point(0,y),new Point(ActualWidth,y));
            dc.DrawRectangle(Brushes.White,null,new Rect(4,y-9,112,18));
            DrawText(dc,"Manual page break",8,y-8,104,TextAlignment.Left,new SolidColorBrush(Color.FromRgb(85,105,130)));
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
