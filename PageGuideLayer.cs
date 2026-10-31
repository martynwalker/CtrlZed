using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CtrlZed;

public sealed class PageGuideLayer : FrameworkElement
{
    public double PageContentHeight { get; set; } = 979;
    public string Header { get; set; } = "";
    public string Footer { get; set; } = "";
    public Func<string,int,int,string>? Expand { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(PageContentHeight < 100 || ActualWidth < 100 || ActualHeight < 100)return;

        var pages=Math.Max(1,(int)Math.Ceiling(ActualHeight/PageContentHeight));
        var linePen=new Pen(new SolidColorBrush(Color.FromArgb(120,130,130,130)),1)
        {
            DashStyle=DashStyles.Dash
        };

        for(int i=0;i<pages;i++)
        {
            var top=i*PageContentHeight;
            var bottom=(i+1)*PageContentHeight;

            DrawText(dc,Expand?.Invoke(Header,i+1,pages)??Header,10,top+4,ActualWidth-20,TextAlignment.Center,Brushes.Gray);

            if(bottom<=ActualHeight+1)
            {
                DrawText(dc,Expand?.Invoke(Footer,i+1,pages)??Footer,10,Math.Max(top+28,bottom-24),ActualWidth-20,TextAlignment.Center,Brushes.Gray);
                dc.DrawLine(linePen,new Point(0,bottom),new Point(ActualWidth,bottom));
                DrawText(dc,$"End of page {i+1}",10,bottom-17,110,TextAlignment.Left,Brushes.DarkGray);
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
