using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public static class DocxInterop
{
    public static void Save(string path, FlowDocument document)
    {
        using var word=WordprocessingDocument.Create(path,DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main=word.AddMainDocumentPart();
        main.Document=new W.Document(new W.Body());
        var body=main.Document.Body ?? main.Document.AppendChild(new W.Body());
        uint imageId=1;

        foreach(var block in document.Blocks)
        {
            if(block is Paragraph p) body.Append(ToWordParagraph(main,p,ref imageId));
            else if(block is Table t) body.Append(ToWordTable(main,t,ref imageId));
        }

        main.Document.Save();
    }

    public static FlowDocument Load(string path)
    {
        using var word=WordprocessingDocument.Open(path,false);
        var result=new FlowDocument();
        var main=word.MainDocumentPart;
        var body=main?.Document?.Body;
        if(main==null || body==null)return result;

        foreach(var child in body.ChildElements)
        {
            if(child is W.Paragraph p) result.Blocks.Add(FromWordParagraph(main,p));
            else if(child is W.Table t) result.Blocks.Add(FromWordTable(main,t));
        }
        return result;
    }

    static W.Paragraph ToWordParagraph(MainDocumentPart main,Paragraph p,ref uint imageId)
    {
        var wp=new W.Paragraph();
        var pp=new W.ParagraphProperties();

        var style=DetectStyleId(p);
        if(style!=null)pp.ParagraphStyleId=new W.ParagraphStyleId{Val=style};
        if(p.BreakPageBefore)pp.Append(new W.PageBreakBefore());
        pp.Justification=new W.Justification{Val=p.TextAlignment switch
        {
            TextAlignment.Center=>W.JustificationValues.Center,
            TextAlignment.Right=>W.JustificationValues.Right,
            TextAlignment.Justify=>W.JustificationValues.Both,
            _=>W.JustificationValues.Left
        }};

        wp.Append(pp);
        AppendInlines(main,wp,p.Inlines,ref imageId);
        return wp;
    }

    static void AppendInlines(MainDocumentPart main,W.Paragraph target,InlineCollection inlines,ref uint imageId)
    {
        foreach(var inline in inlines)
        {
            if(inline is Run run)target.Append(ToWordRun(run));
            else if(inline is LineBreak)target.Append(new W.Run(new W.Break()));
            else if(inline is Span span)AppendInlines(main,target,span.Inlines,ref imageId);
            else if(inline is InlineUIContainer ui && ui.Child is Image img && img.Source is BitmapSource bitmap)
                target.Append(ToWordImageRun(main,bitmap,img,ref imageId));
        }
    }

    static W.Run ToWordRun(Run run)
    {
        var props=new W.RunProperties();
        if(run.FontWeight.ToOpenTypeWeight()>=600)props.Bold=new W.Bold();
        if(run.FontStyle==FontStyles.Italic)props.Italic=new W.Italic();
        if(run.TextDecorations==TextDecorations.Underline)props.Underline=new W.Underline{Val=W.UnderlineValues.Single};
        if(!string.IsNullOrWhiteSpace(run.FontFamily?.Source))
            props.RunFonts=new W.RunFonts{Ascii=run.FontFamily.Source,HighAnsi=run.FontFamily.Source};
        if(!double.IsNaN(run.FontSize) && run.FontSize>0)
            props.FontSize=new W.FontSize{Val=((int)Math.Round(run.FontSize*72/96*2)).ToString()};

        return new W.Run(props,new W.Text(run.Text??""){Space=DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve});
    }

    static W.Run ToWordImageRun(MainDocumentPart main,BitmapSource bitmap,Image image,ref uint imageId)
    {
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms=new MemoryStream();
        encoder.Save(ms);
        ms.Position=0;

        var part=main.AddImagePart(ImagePartType.Png);
        part.FeedData(ms);
        var relId=main.GetIdOfPart(part);

        var widthDip=!double.IsNaN(image.Width)&&image.Width>0?image.Width:(image.ActualWidth>0?image.ActualWidth:bitmap.PixelWidth*96.0/Math.Max(1,bitmap.DpiX));
        var heightDip=!double.IsNaN(image.Height)&&image.Height>0?image.Height:(image.ActualHeight>0?image.ActualHeight:bitmap.PixelHeight*96.0/Math.Max(1,bitmap.DpiY));
        var cx=(long)Math.Max(1,widthDip*9525);
        var cy=(long)Math.Max(1,heightDip*9525);
        var id=imageId++;

        var drawing=new W.Drawing(
            new DW.Inline(
                new DW.Extent{Cx=cx,Cy=cy},
                new DW.EffectExtent{LeftEdge=0L,TopEdge=0L,RightEdge=0L,BottomEdge=0L},
                new DW.DocProperties{Id=id,Name=$"CtrlZed Picture {id}"},
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks{NoChangeAspect=true}),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties{Id=id,Name=$"CtrlZed Picture {id}.png"},
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip{Embed=relId},
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset{X=0L,Y=0L},
                                    new A.Extents{Cx=cx,Cy=cy}),
                                new A.PresetGeometry(new A.AdjustValueList()){Preset=A.ShapeTypeValues.Rectangle})
                        )
                    ){Uri="http://schemas.openxmlformats.org/drawingml/2006/picture"}
                )
            )
            {
                DistanceFromTop=0U,DistanceFromBottom=0U,DistanceFromLeft=0U,DistanceFromRight=0U
            });
        return new W.Run(drawing);
    }

    static W.Table ToWordTable(MainDocumentPart main,Table table,ref uint imageId)
    {
        var wt=new W.Table();
        var borders=new W.TableBorders(
            new W.TopBorder{Val=W.BorderValues.Single,Size=4},
            new W.LeftBorder{Val=W.BorderValues.Single,Size=4},
            new W.BottomBorder{Val=W.BorderValues.Single,Size=4},
            new W.RightBorder{Val=W.BorderValues.Single,Size=4},
            new W.InsideHorizontalBorder{Val=W.BorderValues.Single,Size=2},
            new W.InsideVerticalBorder{Val=W.BorderValues.Single,Size=2});
        wt.AppendChild(new W.TableProperties(borders));

        foreach(var group in table.RowGroups)
        foreach(var row in group.Rows)
        {
            var wr=new W.TableRow();
            foreach(var cell in row.Cells)
            {
                var wc=new W.TableCell();
                foreach(var block in cell.Blocks)
                    if(block is Paragraph p)wc.Append(ToWordParagraph(main,p,ref imageId));
                if(!wc.Elements<W.Paragraph>().Any())wc.Append(new W.Paragraph());
                wr.Append(wc);
            }
            wt.Append(wr);
        }
        return wt;
    }

    static Paragraph FromWordParagraph(MainDocumentPart main,W.Paragraph p)
    {
        var result=new Paragraph();
        var pp=p.ParagraphProperties;
        if(pp?.PageBreakBefore!=null)result.BreakPageBefore=true;
        var justification=pp?.Justification?.Val?.Value;
        if(justification!=null)
        {
            if(justification==W.JustificationValues.Center)result.TextAlignment=TextAlignment.Center;
            else if(justification==W.JustificationValues.Right)result.TextAlignment=TextAlignment.Right;
            else if(justification==W.JustificationValues.Both)result.TextAlignment=TextAlignment.Justify;
            else result.TextAlignment=TextAlignment.Left;
        }

        var style=pp?.ParagraphStyleId?.Val?.Value;
        if(style!=null)ApplyImportedStyle(result,style);

        foreach(var r in p.Elements<W.Run>())
        {
            foreach(var item in r.ChildElements)
            {
                if(item is W.Text txt)result.Inlines.Add(FromWordRun(r,txt.Text));
                else if(item is W.Break)result.Inlines.Add(new LineBreak());
                else if(item is W.Drawing drawing)
                {
                    var image=FromWordDrawing(main,drawing);
                    if(image!=null)result.Inlines.Add(new InlineUIContainer(image));
                }
            }
        }
        return result;
    }

    static Image? FromWordDrawing(MainDocumentPart main,W.Drawing drawing)
    {
        var blip=drawing.Descendants<A.Blip>().FirstOrDefault();
        var relId=blip?.Embed?.Value;
        if(string.IsNullOrWhiteSpace(relId))return null;
        if(main.GetPartById(relId) is not ImagePart part)return null;

        BitmapImage bitmap;
        using(var stream=part.GetStream())
        {
            bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.StreamSource=stream;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        var extent=drawing.Descendants<DW.Extent>().FirstOrDefault();
        var image=new Image{Source=bitmap,Stretch=Stretch.Uniform,Margin=new Thickness(4)};
        if(extent?.Cx?.Value is long cx && cx>0)image.Width=cx/9525.0;
        if(extent?.Cy?.Value is long cy && cy>0)image.Height=cy/9525.0;
        return image;
    }

    static Run FromWordRun(W.Run source,string text)
    {
        var r=new Run(text);
        var rp=source.RunProperties;
        if(rp?.Bold!=null)r.FontWeight=FontWeights.Bold;
        if(rp?.Italic!=null)r.FontStyle=FontStyles.Italic;
        var underline=rp?.Underline?.Val?.Value;
        if(underline!=null && underline!=W.UnderlineValues.None)r.TextDecorations=TextDecorations.Underline;
        var font=rp?.RunFonts?.Ascii?.Value??rp?.RunFonts?.HighAnsi?.Value;
        if(!string.IsNullOrWhiteSpace(font))r.FontFamily=new FontFamily(font);
        if(double.TryParse(rp?.FontSize?.Val?.Value,out var halfPoints))r.FontSize=halfPoints/2*96/72;
        return r;
    }

    static Table FromWordTable(MainDocumentPart main,W.Table source)
    {
        var t=new Table{CellSpacing=0,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Margin=new Thickness(0,8,0,8)};
        var rows=source.Elements<W.TableRow>().ToList();
        var maxCols=rows.Select(r=>r.Elements<W.TableCell>().Count()).DefaultIfEmpty(1).Max();
        for(int i=0;i<maxCols;i++)t.Columns.Add(new TableColumn());
        var group=new TableRowGroup();t.RowGroups.Add(group);

        foreach(var row in rows)
        {
            var tr=new TableRow();group.Rows.Add(tr);
            foreach(var cell in row.Elements<W.TableCell>())
            {
                var tc=new TableCell{BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0.5),Padding=new Thickness(5)};
                foreach(var p in cell.Elements<W.Paragraph>())tc.Blocks.Add(FromWordParagraph(main,p));
                if(tc.Blocks.Count==0)tc.Blocks.Add(new Paragraph());
                tr.Cells.Add(tc);
            }
        }
        return t;
    }

    static string? DetectStyleId(Paragraph p)
    {
        var range=new TextRange(p.ContentStart,p.ContentEnd);
        var sizeValue=range.GetPropertyValue(TextElement.FontSizeProperty);
        var weightValue=range.GetPropertyValue(TextElement.FontWeightProperty);
        var size=sizeValue is double d?d:0;
        var weight=weightValue is FontWeight fw?fw.ToOpenTypeWeight():0;
        if(size>=23 && weight>=600)return "Heading1";
        if(size>=19 && weight>=600)return "Heading2";
        if(size>=17 && weight>=550)return "Heading3";
        return null;
    }

    static void ApplyImportedStyle(Paragraph p,string style)
    {
        switch(style.ToLowerInvariant())
        {
            case "heading1":p.FontSize=24;p.FontWeight=FontWeights.Bold;break;
            case "heading2":p.FontSize=20;p.FontWeight=FontWeights.Bold;break;
            case "heading3":p.FontSize=17;p.FontWeight=FontWeights.SemiBold;break;
            case "quote":p.FontStyle=FontStyles.Italic;p.Margin=new Thickness(36,8,36,8);break;
        }
    }
}
