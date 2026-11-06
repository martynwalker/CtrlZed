using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public sealed class DocxLoadResult
{
    public FlowDocument Document { get; init; } = new();
    public string Header { get; init; } = "";
    public string Footer { get; init; } = "";
    public double PageWidth { get; init; }
    public double PageHeight { get; init; }
    public Thickness? Margins { get; init; }
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Keywords { get; init; } = "";
}

public static class DocxInterop
{
    sealed record NumberingInfo(int NumberId,int Level,TextMarkerStyle MarkerStyle);

    public static void Save(string path,FlowDocument document)
        =>Save(path,document,"","",794,1123,new Thickness(72),"","","","");

    public static void Save(
        string path,FlowDocument document,string header,string footer,
        double pageWidth,double pageHeight,Thickness margins,
        string title,string author,string subject,string keywords)
    {
        using var word=WordprocessingDocument.Create(path,WordprocessingDocumentType.Document);
        var main=word.AddMainDocumentPart();
        main.Document=new W.Document(new W.Body());
        var body=main.Document.Body ?? main.Document.AppendChild(new W.Body());

        word.PackageProperties.Title=title??"";
        word.PackageProperties.Creator=author??"";
        word.PackageProperties.Subject=subject??"";
        word.PackageProperties.Keywords=keywords??"";

        EnsureStyles(main);
        EnsureNumbering(main);
        EnsureDocumentSettings(main);

        uint imageId=1;
        AppendBlocks(main,body,document.Blocks,ref imageId);

        var section=new W.SectionProperties();
        if(!string.IsNullOrWhiteSpace(header))
        {
            var part=main.AddNewPart<HeaderPart>();
            part.Header=new W.Header(BuildHeaderFooterParagraph(header));
            part.Header.Save();
            section.Append(new W.HeaderReference
            {
                Type=W.HeaderFooterValues.Default,
                Id=main.GetIdOfPart(part)
            });
        }
        if(!string.IsNullOrWhiteSpace(footer))
        {
            var part=main.AddNewPart<FooterPart>();
            part.Footer=new W.Footer(BuildHeaderFooterParagraph(footer));
            part.Footer.Save();
            section.Append(new W.FooterReference
            {
                Type=W.HeaderFooterValues.Default,
                Id=main.GetIdOfPart(part)
            });
        }

        section.Append(new W.PageSize
        {
            Width=(UInt32Value)(uint)Math.Max(1,Math.Round(pageWidth*15)),
            Height=(UInt32Value)(uint)Math.Max(1,Math.Round(pageHeight*15))
        });
        section.Append(new W.PageMargin
        {
            Top=(Int32Value)(int)Math.Round(margins.Top*15),
            Right=(UInt32Value)(uint)Math.Max(0,Math.Round(margins.Right*15)),
            Bottom=(Int32Value)(int)Math.Round(margins.Bottom*15),
            Left=(UInt32Value)(uint)Math.Max(0,Math.Round(margins.Left*15)),
            Header=(UInt32Value)360U,
            Footer=(UInt32Value)360U,
            Gutter=(UInt32Value)0U
        });

        body.Append(section);
        main.Document.Save();
    }

    public static FlowDocument Load(string path)=>LoadWithLayout(path).Document;

    public static DocxLoadResult LoadWithLayout(string path)
    {
        using var word=WordprocessingDocument.Open(path,false);
        var result=new FlowDocument();
        var main=word.MainDocumentPart;
        var body=main?.Document?.Body;
        if(main==null || body==null)return new DocxLoadResult{Document=result};

        var children=body.ChildElements.ToList();
        for(var i=0;i<children.Count;i++)
        {
            if(children[i] is W.Paragraph paragraph)
            {
                var info=GetNumberingInfo(main,paragraph);
                if(info==null)
                {
                    result.Blocks.Add(FromWordParagraph(main,paragraph));
                    continue;
                }

                var list=new List{MarkerStyle=info.MarkerStyle};
                while(i<children.Count && children[i] is W.Paragraph listParagraph)
                {
                    var current=GetNumberingInfo(main,listParagraph);
                    if(current==null || current.NumberId!=info.NumberId || current.Level!=info.Level)break;
                    var item=new ListItem();
                    item.Blocks.Add(FromWordParagraph(main,listParagraph));
                    list.ListItems.Add(item);
                    i++;
                }
                i--;
                result.Blocks.Add(list);
            }
            else if(children[i] is W.Table table)result.Blocks.Add(FromWordTable(main,table));
        }

        var section=body.Descendants<W.SectionProperties>().LastOrDefault()
                    ??body.Elements<W.SectionProperties>().LastOrDefault();
        string header="",footer="";
        double width=0,height=0;
        Thickness? margins=null;

        if(section!=null)
        {
            var headerReference=section.Elements<W.HeaderReference>()
                .FirstOrDefault(x=>x.Type?.Value==W.HeaderFooterValues.Default)
                ??section.Elements<W.HeaderReference>().FirstOrDefault();
            if(headerReference?.Id?.Value is string headerId &&
               main.GetPartById(headerId) is HeaderPart headerPart &&
               headerPart.Header!=null)
                header=ReadHeaderFooter(headerPart.Header);

            var footerReference=section.Elements<W.FooterReference>()
                .FirstOrDefault(x=>x.Type?.Value==W.HeaderFooterValues.Default)
                ??section.Elements<W.FooterReference>().FirstOrDefault();
            if(footerReference?.Id?.Value is string footerId &&
               main.GetPartById(footerId) is FooterPart footerPart &&
               footerPart.Footer!=null)
                footer=ReadHeaderFooter(footerPart.Footer);

            var size=section.GetFirstChild<W.PageSize>();
            if(size?.Width?.Value is uint w && w>0)width=w/15.0;
            if(size?.Height?.Value is uint h && h>0)height=h/15.0;

            var pageMargin=section.GetFirstChild<W.PageMargin>();
            if(pageMargin!=null)
            {
                margins=new Thickness(
                    (pageMargin.Left?.Value??1440U)/15.0,
                    (pageMargin.Top?.Value??1440)/15.0,
                    (pageMargin.Right?.Value??1440U)/15.0,
                    (pageMargin.Bottom?.Value??1440)/15.0);
            }
        }

        return new DocxLoadResult
        {
            Document=result,
            Header=header,
            Footer=footer,
            PageWidth=width,
            PageHeight=height,
            Margins=margins,
            Title=word.PackageProperties.Title??"",
            Author=word.PackageProperties.Creator??"",
            Subject=word.PackageProperties.Subject??"",
            Keywords=word.PackageProperties.Keywords??""
        };
    }

    static void EnsureDocumentSettings(MainDocumentPart main)
    {
        var part=main.DocumentSettingsPart??main.AddNewPart<DocumentSettingsPart>();
        part.Settings=new W.Settings(new W.UpdateFieldsOnOpen{Val=true});
        part.Settings.Save();
    }

    static void EnsureStyles(MainDocumentPart main)
    {
        var part=main.StyleDefinitionsPart??main.AddNewPart<StyleDefinitionsPart>();
        if(part.Styles!=null && part.Styles.Elements<W.Style>().Any())return;

        var styles=new W.Styles();
        styles.Append(CreateParagraphStyle("Normal","Normal",null,"Normal",false,22,false,false,0,120,0,0));
        styles.Append(CreateParagraphStyle("Heading1","heading 1","Normal","Normal",true,32,true,false,240,120,0,0));
        styles.Append(CreateParagraphStyle("Heading2","heading 2","Normal","Normal",true,28,true,false,200,100,0,0));
        styles.Append(CreateParagraphStyle("Heading3","heading 3","Normal","Normal",true,24,true,false,160,80,0,0));
        styles.Append(CreateParagraphStyle("Quote","Quote","Normal","Normal",false,22,false,true,120,120,540,540));
        styles.Append(CreateParagraphStyle("Caption","Caption","Normal","Normal",false,18,false,true,40,120,0,0));
        part.Styles=styles;
        part.Styles.Save();
    }

    static W.Style CreateParagraphStyle(
        string id,string name,string? basedOn,string next,bool primary,
        int halfPoints,bool bold,bool italic,int before,int after,int left,int right)
    {
        var style=new W.Style{Type=W.StyleValues.Paragraph,StyleId=id};
        style.Append(new W.StyleName{Val=name});
        if(!string.IsNullOrWhiteSpace(basedOn))style.Append(new W.BasedOn{Val=basedOn});
        style.Append(new W.NextParagraphStyle{Val=next});
        if(primary)style.Append(new W.PrimaryStyle());

        var paragraphProperties=new W.StyleParagraphProperties();
        if(id.StartsWith("Heading",StringComparison.Ordinal))
        {
            paragraphProperties.Append(new W.KeepNext());
            paragraphProperties.Append(new W.KeepLines());
        }
        paragraphProperties.Append(new W.SpacingBetweenLines
        {
            Before=before.ToString(),After=after.ToString()
        });
        if(left>0 || right>0)
            paragraphProperties.Append(new W.Indentation{Left=left.ToString(),Right=right.ToString()});
        if(id.StartsWith("Heading",StringComparison.Ordinal) && int.TryParse(id[^1..],out var headingLevel))
            paragraphProperties.Append(new W.OutlineLevel{Val=headingLevel-1});
        style.Append(paragraphProperties);

        var runProperties=new W.StyleRunProperties();
        if(bold)runProperties.Append(new W.Bold());
        if(italic)runProperties.Append(new W.Italic());
        runProperties.Append(new W.FontSize{Val=halfPoints.ToString()});
        style.Append(runProperties);
        return style;
    }

    static W.Paragraph BuildHeaderFooterParagraph(string template)
    {
        var paragraph=new W.Paragraph();
        paragraph.Append(new W.ParagraphProperties(
            new W.Justification{Val=W.JustificationValues.Center}));
        var tokens=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{page}"]="PAGE",
            ["{pages}"]="NUMPAGES",
            ["{filename}"]="FILENAME",
            ["{date}"]="DATE \\@ \"dd MMMM yyyy\"",
            ["{time}"]="TIME \\@ \"HH:mm\""
        };

        var index=0;
        while(index<template.Length)
        {
            var match=tokens
                .Select(pair=>(Index:template.IndexOf(pair.Key,index,StringComparison.OrdinalIgnoreCase),Token:pair.Key,Field:pair.Value))
                .Where(x=>x.Index>=0)
                .OrderBy(x=>x.Index)
                .FirstOrDefault();
            if(match.Token==null)
            {
                AppendPlainText(paragraph,template[index..]);
                break;
            }
            if(match.Index>index)AppendPlainText(paragraph,template[index..match.Index]);
            AppendField(paragraph,match.Field,match.Token);
            index=match.Index+match.Token.Length;
        }
        return paragraph;
    }

    static void AppendPlainText(W.Paragraph paragraph,string text)
    {
        if(text.Length==0)return;
        var buffer=new StringBuilder();
        void Flush()
        {
            if(buffer.Length==0)return;
            paragraph.Append(new W.Run(new W.Text(buffer.ToString()){Space=SpaceProcessingModeValues.Preserve}));
            buffer.Clear();
        }
        foreach(var character in text)
        {
            if(character=='\t')
            {
                Flush();
                paragraph.Append(new W.Run(new W.TabChar()));
            }
            else if(character is '\r' or '\n')
            {
                if(character=='\n')continue;
                Flush();
                paragraph.Append(new W.Run(new W.Break()));
            }
            else buffer.Append(character);
        }
        Flush();
    }

    static void AppendField(W.Paragraph paragraph,string instruction,string fallback)
    {
        paragraph.Append(new W.Run(new W.FieldChar{FieldCharType=W.FieldCharValues.Begin}));
        paragraph.Append(new W.Run(new W.FieldCode(" "+instruction+" "){Space=SpaceProcessingModeValues.Preserve}));
        paragraph.Append(new W.Run(new W.FieldChar{FieldCharType=W.FieldCharValues.Separate}));
        paragraph.Append(new W.Run(new W.Text(fallback)));
        paragraph.Append(new W.Run(new W.FieldChar{FieldCharType=W.FieldCharValues.End}));
    }

    static string ReadHeaderFooter(OpenXmlElement root)
    {
        var result=new StringBuilder();
        var inField=false;
        var fieldCode="";
        foreach(var element in root.Descendants())
        {
            if(element is W.FieldChar fieldCharacter)
            {
                var type=fieldCharacter.FieldCharType?.Value;
                if(type==W.FieldCharValues.Begin)
                {
                    inField=true;
                    fieldCode="";
                }
                else if(type==W.FieldCharValues.End)
                {
                    var code=fieldCode.Trim().ToUpperInvariant();
                    if(code.StartsWith("NUMPAGES"))result.Append("{pages}");
                    else if(code.StartsWith("PAGE"))result.Append("{page}");
                    else if(code.StartsWith("FILENAME"))result.Append("{filename}");
                    else if(code.StartsWith("DATE"))result.Append("{date}");
                    else if(code.StartsWith("TIME"))result.Append("{time}");
                    inField=false;
                    fieldCode="";
                }
            }
            else if(element is W.FieldCode code && inField)fieldCode+=code.Text;
            else if(element is W.Text text && !inField)result.Append(text.Text);
            else if(element is W.TabChar && !inField)result.Append('\t');
            else if(element is W.Break && !inField)result.AppendLine();
        }
        return result.ToString().TrimEnd('\r','\n');
    }

    static void AppendBlocks(
        MainDocumentPart main,OpenXmlCompositeElement target,IEnumerable<Block> blocks,ref uint imageId)
    {
        foreach(var block in blocks)
        {
            switch(block)
            {
                case Paragraph paragraph:
                    target.Append(ToWordParagraph(main,paragraph,ref imageId));
                    break;
                case Table table:
                    target.Append(ToWordTable(main,table,ref imageId));
                    break;
                case List list:
                    AppendList(main,target,list,ref imageId);
                    break;
                case Section section:
                    AppendBlocks(main,target,section.Blocks,ref imageId);
                    break;
            }
        }
    }

    static W.Paragraph ToWordParagraph(MainDocumentPart main,Paragraph paragraph,ref uint imageId)
    {
        var wordParagraph=new W.Paragraph();
        var properties=new W.ParagraphProperties();
        var style=DetectStyleId(paragraph);
        if(style!=null)properties.Append(new W.ParagraphStyleId{Val=style});
        if(paragraph.KeepWithNext)properties.Append(new W.KeepNext());
        if(paragraph.KeepTogether)properties.Append(new W.KeepLines());
        if(paragraph.BreakPageBefore)properties.Append(new W.PageBreakBefore());

        var spacing=new W.SpacingBetweenLines();
        var hasSpacing=false;
        if(Math.Abs(paragraph.Margin.Top)>0.01)
        {
            spacing.Before=Twips(paragraph.Margin.Top);
            hasSpacing=true;
        }
        if(Math.Abs(paragraph.Margin.Bottom)>0.01)
        {
            spacing.After=Twips(paragraph.Margin.Bottom);
            hasSpacing=true;
        }
        if(!double.IsNaN(paragraph.LineHeight) && paragraph.LineHeight>0)
        {
            spacing.Line=Twips(paragraph.LineHeight);
            spacing.LineRule=W.LineSpacingRuleValues.AtLeast;
            hasSpacing=true;
        }
        if(hasSpacing)properties.Append(spacing);

        var indentation=new W.Indentation();
        var hasIndentation=false;
        if(Math.Abs(paragraph.Margin.Left)>0.01)
        {
            indentation.Left=Twips(paragraph.Margin.Left);
            hasIndentation=true;
        }
        if(Math.Abs(paragraph.Margin.Right)>0.01)
        {
            indentation.Right=Twips(paragraph.Margin.Right);
            hasIndentation=true;
        }
        if(Math.Abs(paragraph.TextIndent)>0.01)
        {
            if(paragraph.TextIndent>0)indentation.FirstLine=Twips(paragraph.TextIndent);
            else indentation.Hanging=Twips(-paragraph.TextIndent);
            hasIndentation=true;
        }
        if(hasIndentation)properties.Append(indentation);

        var alignment=W.JustificationValues.Left;
        if(paragraph.TextAlignment==TextAlignment.Center)alignment=W.JustificationValues.Center;
        else if(paragraph.TextAlignment==TextAlignment.Right)alignment=W.JustificationValues.Right;
        else if(paragraph.TextAlignment==TextAlignment.Justify)alignment=W.JustificationValues.Both;
        properties.Append(new W.Justification{Val=alignment});

        wordParagraph.Append(properties);
        AppendInlines(main,wordParagraph,paragraph.Inlines,ref imageId);
        return wordParagraph;
    }

    static string Twips(double dip)=>Math.Max(0,(int)Math.Round(dip*15)).ToString();

    static void EnsureNumbering(MainDocumentPart main)
    {
        var part=main.NumberingDefinitionsPart??main.AddNewPart<NumberingDefinitionsPart>();
        if(part.Numbering!=null)return;

        var definitions=new (int Id,W.NumberFormatValues Format,string LevelText)[]
        {
            (1,W.NumberFormatValues.Bullet,"•"),
            (2,W.NumberFormatValues.Decimal,"%1."),
            (3,W.NumberFormatValues.LowerLetter,"%1."),
            (4,W.NumberFormatValues.UpperLetter,"%1."),
            (5,W.NumberFormatValues.LowerRoman,"%1."),
            (6,W.NumberFormatValues.UpperRoman,"%1.")
        };
        var numbering=new W.Numbering();
        foreach(var definition in definitions)
        {
            var abstractNumber=new W.AbstractNum{AbstractNumberId=definition.Id};
            abstractNumber.Append(new W.MultiLevelType{Val=W.MultiLevelValues.SingleLevel});
            abstractNumber.Append(new W.Level(
                new W.StartNumberingValue{Val=1},
                new W.NumberingFormat{Val=definition.Format},
                new W.LevelText{Val=definition.LevelText},
                new W.LevelJustification{Val=W.LevelJustificationValues.Left},
                new W.PreviousParagraphProperties(new W.Indentation{Left="720",Hanging="360"}))
            {LevelIndex=0});
            numbering.Append(abstractNumber);
        }
        foreach(var definition in definitions)
            numbering.Append(new W.NumberingInstance(new W.AbstractNumId{Val=definition.Id}){NumberID=definition.Id});
        part.Numbering=numbering;
        part.Numbering.Save();
    }

    static NumberingInfo? GetNumberingInfo(MainDocumentPart main,W.Paragraph paragraph)
    {
        var properties=paragraph.ParagraphProperties?.NumberingProperties;
        var numberId=properties?.NumberingId?.Val?.Value;
        if(numberId==null)return null;
        var level=properties?.NumberingLevelReference?.Val?.Value??0;
        var numbering=main.NumberingDefinitionsPart?.Numbering;
        if(numbering==null)return new NumberingInfo(numberId.Value,level,TextMarkerStyle.Disc);

        var instance=numbering.Elements<W.NumberingInstance>()
            .FirstOrDefault(x=>x.NumberID?.Value==numberId.Value);
        var abstractId=instance?.AbstractNumId?.Val?.Value;
        var abstractNumber=numbering.Elements<W.AbstractNum>()
            .FirstOrDefault(x=>x.AbstractNumberId?.Value==abstractId);
        var levelDefinition=abstractNumber?.Elements<W.Level>()
            .FirstOrDefault(x=>x.LevelIndex?.Value==level)
            ??abstractNumber?.Elements<W.Level>().FirstOrDefault();
        var format=levelDefinition?.NumberingFormat?.Val?.Value;
        var marker=TextMarkerStyle.Disc;
        if(format==W.NumberFormatValues.Decimal)marker=TextMarkerStyle.Decimal;
        else if(format==W.NumberFormatValues.LowerLetter)marker=TextMarkerStyle.LowerLatin;
        else if(format==W.NumberFormatValues.UpperLetter)marker=TextMarkerStyle.UpperLatin;
        else if(format==W.NumberFormatValues.LowerRoman)marker=TextMarkerStyle.LowerRoman;
        else if(format==W.NumberFormatValues.UpperRoman)marker=TextMarkerStyle.UpperRoman;
        return new NumberingInfo(numberId.Value,level,marker);
    }

    static int NumberIdFor(TextMarkerStyle style)=>style switch
    {
        TextMarkerStyle.Decimal=>2,
        TextMarkerStyle.LowerLatin=>3,
        TextMarkerStyle.UpperLatin=>4,
        TextMarkerStyle.LowerRoman=>5,
        TextMarkerStyle.UpperRoman=>6,
        _=>1
    };

    static void AppendList(
        MainDocumentPart main,OpenXmlCompositeElement target,List list,ref uint imageId)
    {
        var numberId=NumberIdFor(list.MarkerStyle);
        foreach(var item in list.ListItems)
        foreach(var block in item.Blocks)
        {
            if(block is Paragraph paragraph)
            {
                var wordParagraph=ToWordParagraph(main,paragraph,ref imageId);
                wordParagraph.ParagraphProperties??=new W.ParagraphProperties();
                wordParagraph.ParagraphProperties.NumberingProperties=new W.NumberingProperties(
                    new W.NumberingLevelReference{Val=0},
                    new W.NumberingId{Val=numberId});
                target.Append(wordParagraph);
            }
            else if(block is Table table)target.Append(ToWordTable(main,table,ref imageId));
            else if(block is Section section)AppendBlocks(main,target,section.Blocks,ref imageId);
        }
    }

    static void AppendInlines(
        MainDocumentPart main,W.Paragraph target,InlineCollection inlines,ref uint imageId)
    {
        foreach(var inline in inlines)
        {
            if(inline is Run run)target.Append(ToWordRun(run));
            else if(inline is LineBreak)target.Append(new W.Run(new W.Break()));
            else if(inline is Span span)AppendInlines(main,target,span.Inlines,ref imageId);
            else if(inline is InlineUIContainer container &&
                    container.Child is Image image &&
                    image.Source is BitmapSource bitmap)
                target.Append(ToWordImageRun(main,bitmap,image,ref imageId));
        }
    }

    static W.Run ToWordRun(Run run)
    {
        var properties=new W.RunProperties();
        if(!string.IsNullOrWhiteSpace(run.FontFamily?.Source))
            properties.Append(new W.RunFonts{Ascii=run.FontFamily.Source,HighAnsi=run.FontFamily.Source});
        if(run.FontWeight.ToOpenTypeWeight()>=600)properties.Append(new W.Bold());
        if(run.FontStyle==FontStyles.Italic)properties.Append(new W.Italic());
        if(HasDecoration(run.TextDecorations,TextDecorationLocation.Strikethrough))properties.Append(new W.Strike());
        if(run.Foreground is SolidColorBrush foreground && foreground.Color.A>0)
            properties.Append(new W.Color{Val=Rgb(foreground.Color)});
        if(!double.IsNaN(run.FontSize) && run.FontSize>0)
            properties.Append(new W.FontSize{Val=((int)Math.Round(run.FontSize*72/96*2)).ToString()});
        if(HasDecoration(run.TextDecorations,TextDecorationLocation.Underline))
            properties.Append(new W.Underline{Val=W.UnderlineValues.Single});
        if(run.Background is SolidColorBrush background && background.Color.A>0)
            properties.Append(new W.Shading
            {
                Val=W.ShadingPatternValues.Clear,
                Color="auto",
                Fill=Rgb(background.Color)
            });
        if(run.BaselineAlignment==BaselineAlignment.Superscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Superscript});
        else if(run.BaselineAlignment==BaselineAlignment.Subscript)
            properties.Append(new W.VerticalTextAlignment{Val=W.VerticalPositionValues.Subscript});
        if(!string.IsNullOrWhiteSpace(run.Language?.IetfLanguageTag))
            properties.Append(new W.Languages{Val=run.Language.IetfLanguageTag});
        return new W.Run(properties,new W.Text(run.Text??""){Space=SpaceProcessingModeValues.Preserve});
    }

    static bool HasDecoration(TextDecorationCollection? collection,TextDecorationLocation location)
        =>collection?.Any(x=>x.Location==location)==true;

    static string Rgb(System.Windows.Media.Color colour)
        =>$"{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    static W.Run ToWordImageRun(
        MainDocumentPart main,BitmapSource bitmap,Image image,ref uint imageId)
    {
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();
        encoder.Save(stream);
        stream.Position=0;

        var part=main.AddImagePart(ImagePartType.Png);
        part.FeedData(stream);
        var relationshipId=main.GetIdOfPart(part);
        var widthDip=!double.IsNaN(image.Width)&&image.Width>0
            ?image.Width
            :(image.ActualWidth>0?image.ActualWidth:bitmap.PixelWidth*96.0/Math.Max(1,bitmap.DpiX));
        var heightDip=!double.IsNaN(image.Height)&&image.Height>0
            ?image.Height
            :(image.ActualHeight>0?image.ActualHeight:bitmap.PixelHeight*96.0/Math.Max(1,bitmap.DpiY));
        var cx=(long)Math.Max(1,widthDip*9525);
        var cy=(long)Math.Max(1,heightDip*9525);
        var id=imageId++;

        var nonVisual=new PIC.NonVisualPictureProperties(
            new PIC.NonVisualDrawingProperties{Id=id,Name=$"CtrlZed Picture {id}.png"},
            new PIC.NonVisualPictureDrawingProperties());
        var blipFill=new PIC.BlipFill(
            new A.Blip{Embed=relationshipId},
            new A.Stretch(new A.FillRectangle()));
        var shapeProperties=new PIC.ShapeProperties(
            new A.Transform2D(
                new A.Offset{X=0L,Y=0L},
                new A.Extents{Cx=cx,Cy=cy}),
            new A.PresetGeometry(new A.AdjustValueList()){Preset=A.ShapeTypeValues.Rectangle});
        var picture=new PIC.Picture(nonVisual,blipFill,shapeProperties);
        var graphicData=new A.GraphicData(picture)
        {
            Uri="http://schemas.openxmlformats.org/drawingml/2006/picture"
        };
        var graphic=new A.Graphic(graphicData);
        var inline=new DW.Inline(
            new DW.Extent{Cx=cx,Cy=cy},
            new DW.EffectExtent{LeftEdge=0L,TopEdge=0L,RightEdge=0L,BottomEdge=0L},
            new DW.DocProperties
            {
                Id=id,
                Name=$"CtrlZed Picture {id}",
                Description=image.ToolTip?.ToString()??""
            },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks{NoChangeAspect=true}),
            graphic)
        {
            DistanceFromTop=0U,
            DistanceFromBottom=0U,
            DistanceFromLeft=0U,
            DistanceFromRight=0U
        };
        return new W.Run(new W.Drawing(inline));
    }

    static W.Table ToWordTable(MainDocumentPart main,Table table,ref uint imageId)
    {
        var wordTable=new W.Table();
        wordTable.AppendChild(new W.TableProperties(
            new W.TableBorders(
                new W.TopBorder{Val=W.BorderValues.Single,Size=4},
                new W.LeftBorder{Val=W.BorderValues.Single,Size=4},
                new W.BottomBorder{Val=W.BorderValues.Single,Size=4},
                new W.RightBorder{Val=W.BorderValues.Single,Size=4},
                new W.InsideHorizontalBorder{Val=W.BorderValues.Single,Size=2},
                new W.InsideVerticalBorder{Val=W.BorderValues.Single,Size=2})));

        var grid=new W.TableGrid();
        foreach(var column in table.Columns)
        {
            var gridColumn=new W.GridColumn();
            if(column.Width.IsAbsolute && column.Width.Value>0)gridColumn.Width=Twips(column.Width.Value);
            grid.Append(gridColumn);
        }
        wordTable.Append(grid);

        foreach(var group in table.RowGroups)
        foreach(var row in group.Rows)
        {
            var wordRow=new W.TableRow();
            foreach(var cell in row.Cells)
            {
                var wordCell=new W.TableCell();
                var cellProperties=new W.TableCellProperties();
                if(cell.ColumnSpan>1)cellProperties.Append(new W.GridSpan{Val=cell.ColumnSpan});
                if(cell.Background is SolidColorBrush background && background.Color.A>0)
                    cellProperties.Append(new W.Shading
                    {
                        Val=W.ShadingPatternValues.Clear,
                        Color="auto",
                        Fill=Rgb(background.Color)
                    });
                wordCell.Append(cellProperties);
                AppendBlocks(main,wordCell,cell.Blocks,ref imageId);
                if(!wordCell.Elements<W.Paragraph>().Any())wordCell.Append(new W.Paragraph());
                wordRow.Append(wordCell);
            }
            wordTable.Append(wordRow);
        }
        return wordTable;
    }

    static Paragraph FromWordParagraph(MainDocumentPart main,W.Paragraph source)
    {
        var result=new Paragraph();
        var style=source.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if(!string.IsNullOrWhiteSpace(style))ApplyImportedStyle(main,result,style);
        ApplyParagraphProperties(result,source.ParagraphProperties);
        AppendParagraphContent(main,result,source);
        return result;
    }

    static void AppendParagraphContent(MainDocumentPart main,Paragraph target,OpenXmlElement parent)
    {
        foreach(var child in parent.ChildElements)
        {
            if(child is W.ParagraphProperties)continue;
            if(child is W.Run run)
            {
                foreach(var item in run.ChildElements)
                {
                    if(item is W.Text text)target.Inlines.Add(FromWordRun(run,text.Text));
                    else if(item is W.TabChar)target.Inlines.Add(FromWordRun(run,"\t"));
                    else if(item is W.Break || item.LocalName=="cr")target.Inlines.Add(new LineBreak());
                    else if(item.LocalName=="noBreakHyphen")target.Inlines.Add(FromWordRun(run,"‑"));
                    else if(item.LocalName=="softHyphen")target.Inlines.Add(FromWordRun(run,"\u00AD"));
                    else if(item is W.Drawing drawing)
                    {
                        var image=FromWordDrawing(main,drawing);
                        if(image!=null)target.Inlines.Add(new InlineUIContainer(image));
                    }
                }
            }
            else if(child is OpenXmlCompositeElement composite)
                AppendParagraphContent(main,target,composite);
        }
    }

    static Image? FromWordDrawing(MainDocumentPart main,W.Drawing drawing)
    {
        var relationshipId=drawing.Descendants<A.Blip>().FirstOrDefault()?.Embed?.Value;
        if(string.IsNullOrWhiteSpace(relationshipId))return null;
        if(main.GetPartById(relationshipId) is not ImagePart part)return null;

        BitmapImage bitmap;
        using(var stream=part.GetStream())
        {
            bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.StreamSource=stream;
            bitmap.EndInit();
        }
        var extent=drawing.Descendants<DW.Extent>().FirstOrDefault();
        var image=new Image{Source=bitmap,Stretch=Stretch.Uniform,Margin=new Thickness(4)};
        if(extent?.Cx?.Value is long cx && cx>0)image.Width=cx/9525.0;
        if(extent?.Cy?.Value is long cy && cy>0)image.Height=cy/9525.0;
        var description=drawing.Descendants<DW.DocProperties>().FirstOrDefault()?.Description?.Value;
        if(!string.IsNullOrWhiteSpace(description))image.ToolTip=description;
        return image;
    }

    static Run FromWordRun(W.Run source,string text)
    {
        var result=new Run(text);
        ApplyTextProperties(result,source.RunProperties);
        return result;
    }

    static void ApplyTextProperties(TextElement target,OpenXmlElement? properties)
    {
        if(properties==null)return;
        var bold=properties.GetFirstChild<W.Bold>();
        if(bold!=null && (bold.Val?.Value??true))target.FontWeight=FontWeights.Bold;
        var italic=properties.GetFirstChild<W.Italic>();
        if(italic!=null && (italic.Val?.Value??true))target.FontStyle=FontStyles.Italic;

        if(target is Inline inline)
        {
            var decorations=new TextDecorationCollection();
            var underline=properties.GetFirstChild<W.Underline>()?.Val?.Value;
            if(underline!=null && underline!=W.UnderlineValues.None)
                decorations.Add(TextDecorations.Underline[0]);
            var strike=properties.GetFirstChild<W.Strike>();
            if(strike!=null && (strike.Val?.Value??true))
                decorations.Add(TextDecorations.Strikethrough[0]);
            if(decorations.Count>0)inline.TextDecorations=decorations;
        }

        var fonts=properties.GetFirstChild<W.RunFonts>();
        var font=fonts?.Ascii?.Value??fonts?.HighAnsi?.Value;
        if(!string.IsNullOrWhiteSpace(font))target.FontFamily=new FontFamily(font);
        var size=properties.GetFirstChild<W.FontSize>()?.Val?.Value;
        if(double.TryParse(size,out var halfPoints) && halfPoints>0)
            target.FontSize=halfPoints/2*96/72;

        var colour=properties.GetFirstChild<W.Color>()?.Val?.Value;
        if(TryBrush(colour,out var foreground))target.Foreground=foreground;
        var fill=properties.GetFirstChild<W.Shading>()?.Fill?.Value;
        if(TryBrush(fill,out var background))target.Background=background;

        if(target is Inline baselineInline)
        {
            var vertical=properties.GetFirstChild<W.VerticalTextAlignment>()?.Val?.Value;
            if(vertical==W.VerticalPositionValues.Superscript)
                baselineInline.BaselineAlignment=BaselineAlignment.Superscript;
            else if(vertical==W.VerticalPositionValues.Subscript)
                baselineInline.BaselineAlignment=BaselineAlignment.Subscript;
        }
        var language=properties.GetFirstChild<W.Languages>()?.Val?.Value;
        if(!string.IsNullOrWhiteSpace(language))
        {
            try{target.Language=System.Windows.Markup.XmlLanguage.GetLanguage(language);}
            catch{ }
        }
    }

    static bool TryBrush(string? value,out SolidColorBrush brush)
    {
        brush=Brushes.Transparent;
        if(string.IsNullOrWhiteSpace(value) ||
           string.Equals(value,"auto",StringComparison.OrdinalIgnoreCase) ||
           value.Length!=6)return false;
        try
        {
            var colour=(System.Windows.Media.Color)ColorConverter.ConvertFromString("#"+value)!;
            brush=new SolidColorBrush(colour);
            brush.Freeze();
            return true;
        }
        catch{return false;}
    }

    static void ApplyParagraphProperties(Paragraph paragraph,OpenXmlElement? properties)
    {
        if(properties==null)return;
        var justification=properties.GetFirstChild<W.Justification>()?.Val?.Value;
        if(justification==W.JustificationValues.Center)paragraph.TextAlignment=TextAlignment.Center;
        else if(justification==W.JustificationValues.Right)paragraph.TextAlignment=TextAlignment.Right;
        else if(justification==W.JustificationValues.Both)paragraph.TextAlignment=TextAlignment.Justify;
        var pageBreak=properties.GetFirstChild<W.PageBreakBefore>();
        if(pageBreak!=null && (pageBreak.Val?.Value??true))paragraph.BreakPageBefore=true;
        var keepLines=properties.GetFirstChild<W.KeepLines>();
        if(keepLines!=null && (keepLines.Val?.Value??true))paragraph.KeepTogether=true;
        var keepNext=properties.GetFirstChild<W.KeepNext>();
        if(keepNext!=null && (keepNext.Val?.Value??true))paragraph.KeepWithNext=true;

        var margin=paragraph.Margin;
        var spacing=properties.GetFirstChild<W.SpacingBetweenLines>();
        if(spacing!=null)
        {
            margin=new Thickness(
                margin.Left,
                TryTwips(spacing.Before?.Value,margin.Top),
                margin.Right,
                TryTwips(spacing.After?.Value,margin.Bottom));
            if(double.TryParse(spacing.Line?.Value,out var line) && line>0)
                paragraph.LineHeight=line/15.0;
        }
        var indentation=properties.GetFirstChild<W.Indentation>();
        if(indentation!=null)
        {
            margin=new Thickness(
                TryTwips(indentation.Left?.Value,margin.Left),
                margin.Top,
                TryTwips(indentation.Right?.Value,margin.Right),
                margin.Bottom);
            if(double.TryParse(indentation.FirstLine?.Value,out var first))paragraph.TextIndent=first/15.0;
            else if(double.TryParse(indentation.Hanging?.Value,out var hanging))paragraph.TextIndent=-hanging/15.0;
        }
        paragraph.Margin=margin;
    }

    static double TryTwips(string? value,double fallback)
        =>double.TryParse(value,out var twips)?twips/15.0:fallback;

    static Table FromWordTable(MainDocumentPart main,W.Table source)
    {
        var table=new Table
        {
            CellSpacing=0,
            BorderBrush=Brushes.Gray,
            BorderThickness=new Thickness(1),
            Margin=new Thickness(0,8,0,8)
        };
        var rows=source.Elements<W.TableRow>().ToList();
        var maxColumns=rows.Select(row=>row.Elements<W.TableCell>()
                .Sum(cell=>(int)(cell.TableCellProperties?.GetFirstChild<W.GridSpan>()?.Val?.Value??1)))
            .DefaultIfEmpty(1).Max();
        for(var i=0;i<maxColumns;i++)table.Columns.Add(new TableColumn());

        var gridWidths=source.GetFirstChild<W.TableGrid>()?.Elements<W.GridColumn>().ToList();
        if(gridWidths!=null)
        {
            for(var i=0;i<Math.Min(gridWidths.Count,table.Columns.Count);i++)
                if(double.TryParse(gridWidths[i].Width?.Value,out var twips) && twips>0)
                    table.Columns[i].Width=new GridLength(twips/15.0);
        }

        var group=new TableRowGroup();
        table.RowGroups.Add(group);
        foreach(var row in rows)
        {
            var tableRow=new TableRow();
            group.Rows.Add(tableRow);
            foreach(var cell in row.Elements<W.TableCell>())
            {
                var properties=cell.TableCellProperties;
                var tableCell=new TableCell
                {
                    BorderBrush=Brushes.LightGray,
                    BorderThickness=new Thickness(0.5),
                    Padding=new Thickness(5),
                    ColumnSpan=(int)(properties?.GetFirstChild<W.GridSpan>()?.Val?.Value??1)
                };
                var fill=properties?.GetFirstChild<W.Shading>()?.Fill?.Value;
                if(TryBrush(fill,out var background))tableCell.Background=background;
                foreach(var paragraph in cell.Elements<W.Paragraph>())
                    tableCell.Blocks.Add(FromWordParagraph(main,paragraph));
                foreach(var nestedTable in cell.Elements<W.Table>())
                    tableCell.Blocks.Add(FromWordTable(main,nestedTable));
                if(tableCell.Blocks.Count==0)tableCell.Blocks.Add(new Paragraph());
                tableRow.Cells.Add(tableCell);
            }
        }
        return table;
    }

    static string? DetectStyleId(Paragraph paragraph)
    {
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        var sizeValue=range.GetPropertyValue(TextElement.FontSizeProperty);
        var weightValue=range.GetPropertyValue(TextElement.FontWeightProperty);
        var styleValue=range.GetPropertyValue(TextElement.FontStyleProperty);
        var size=sizeValue is double sizeDip?sizeDip:0;
        var weight=weightValue is FontWeight fontWeight?fontWeight.ToOpenTypeWeight():0;
        var italic=styleValue is FontStyle fontStyle && fontStyle==FontStyles.Italic;
        if(size>=23 && weight>=600)return "Heading1";
        if(size>=19 && weight>=600)return "Heading2";
        if(size>=17 && weight>=550)return "Heading3";
        if(italic && paragraph.Margin.Left>=24 && paragraph.Margin.Right>=24)return "Quote";
        if(italic && size>0 && size<=15 && paragraph.Margin.Bottom>=5)return "Caption";
        return null;
    }

    static void ApplyImportedStyle(MainDocumentPart main,Paragraph paragraph,string styleId)
    {
        var styles=main.StyleDefinitionsPart?.Styles;
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Apply(string id)
        {
            if(styles==null || !visited.Add(id))return;
            var style=styles.Elements<W.Style>()
                .FirstOrDefault(x=>string.Equals(x.StyleId?.Value,id,StringComparison.OrdinalIgnoreCase));
            if(style==null)return;
            var baseId=style.BasedOn?.Val?.Value;
            if(!string.IsNullOrWhiteSpace(baseId))Apply(baseId);
            ApplyParagraphProperties(paragraph,style.StyleParagraphProperties);
            ApplyTextProperties(paragraph,style.StyleRunProperties);
        }
        Apply(styleId);
        if(visited.Count==0)ApplyBuiltInStyle(paragraph,styleId);
    }

    static void ApplyBuiltInStyle(Paragraph paragraph,string style)
    {
        switch(style.ToLowerInvariant())
        {
            case "heading1":
                paragraph.FontSize=24;
                paragraph.FontWeight=FontWeights.Bold;
                paragraph.KeepWithNext=true;
                break;
            case "heading2":
                paragraph.FontSize=20;
                paragraph.FontWeight=FontWeights.Bold;
                paragraph.KeepWithNext=true;
                break;
            case "heading3":
                paragraph.FontSize=17;
                paragraph.FontWeight=FontWeights.SemiBold;
                paragraph.KeepWithNext=true;
                break;
            case "quote":
                paragraph.FontStyle=FontStyles.Italic;
                paragraph.Margin=new Thickness(36,8,36,8);
                break;
            case "caption":
                paragraph.FontStyle=FontStyles.Italic;
                paragraph.FontSize=12;
                paragraph.Foreground=Brushes.DimGray;
                paragraph.Margin=new Thickness(0,3,0,8);
                break;
        }
    }
}
