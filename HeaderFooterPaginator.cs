using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public sealed record FloatingPageImage(int PageNumber,ImageSource Source,Rect Bounds);

public sealed class HeaderFooterPaginator : DocumentPaginator
{
    readonly DocumentPaginator inner;
    readonly string header;
    readonly string footer;
    readonly Func<string,int,int,string> expand;
    readonly IReadOnlyList<FloatingPageImage> floatingImages;

    public HeaderFooterPaginator(
        DocumentPaginator inner,string header,string footer,
        Func<string,int,int,string> expand,
        IEnumerable<FloatingPageImage>? floatingImages=null)
    {
        this.inner=inner;
        this.header=header??"";
        this.footer=footer;
        this.expand=expand;
        this.floatingImages=(floatingImages??Array.Empty<FloatingPageImage>()).ToList();
    }

    public override bool IsPageCountValid => inner.IsPageCountValid;
    public override int PageCount => inner.PageCount;
    public override Size PageSize { get => inner.PageSize; set => inner.PageSize=value; }
    public override IDocumentPaginatorSource Source => inner.Source;

    public override void ComputePageCount() => inner.ComputePageCount();

    public override DocumentPage GetPage(int pageNumber)
    {
        var page=inner.GetPage(pageNumber);
        if(page==DocumentPage.Missing)return page;

        var visual=new DrawingVisual();
        using var dc=visual.RenderOpen();

        foreach(var image in floatingImages.Where(x=>x.PageNumber==pageNumber))
            dc.DrawImage(image.Source,image.Bounds);

        if(page.Visual!=null)
        {
            var brush=new VisualBrush(page.Visual){Stretch=Stretch.Fill};
            dc.DrawRectangle(brush,null,new Rect(new Point(0,0),page.Size));
        }

        var total=Math.Max(1,PageCount);
        DrawTriplet(dc,expand(header,pageNumber+1,total),42,18,page.Size.Width-84);
        DrawTriplet(dc,expand(footer,pageNumber+1,total),42,Math.Max(18,page.Size.Height-38),page.Size.Width-84);

        return new DocumentPage(visual,page.Size,page.BleedBox,page.ContentBox);
    }

    static void DrawTriplet(DrawingContext dc,string text,double x,double y,double width)
    {
        var parts=(text??"").Split('\t');
        if(parts.Length<2){DrawText(dc,text,x,y,width,TextAlignment.Center);return;}
        var third=width/3;
        DrawText(dc,parts.ElementAtOrDefault(0)??"",x,y,third,TextAlignment.Left);
        DrawText(dc,parts.ElementAtOrDefault(1)??"",x+third,y,third,TextAlignment.Center);
        DrawText(dc,parts.ElementAtOrDefault(2)??"",x+third*2,y,third,TextAlignment.Right);
    }

    static void DrawText(DrawingContext dc,string text,double x,double y,double width,TextAlignment alignment)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        var formatted=new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Calibri"),
            10*96/72,
            Brushes.DimGray,
            1.0)
        {
            MaxTextWidth=Math.Max(20,width),
            TextAlignment=alignment,
            Trimming=TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted,new Point(x,y));
    }
}
