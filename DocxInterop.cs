using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public static class DocxInterop
{
    public static void Save(string path, FlowDocument document)
    {
        using var word=WordprocessingDocument.Create(path,DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main=word.AddMainDocumentPart();
        main.Document=new W.Document(new W.Body());
        var body=main.Document.Body ?? main.Document.AppendChild(new W.Body());

        foreach(var block in document.Blocks)
        {
            if(block is Paragraph p) body.Append(ToWordParagraph(p));
            else if(block is Table t) body.Append(ToWordTable(t));
        }

        main.Document.Save();
    }

    public static FlowDocument Load(string path)
    {
        using var word=WordprocessingDocument.Open(path,false);
        var result=new FlowDocument();
        var body=word.MainDocumentPart?.Document.Body;
        if(body==null)return result;

        foreach(var child in body.ChildElements)
        {
            if(child is W.Paragraph p) result.Blocks.Add(FromWordParagraph(p));
            else if(child is W.Table t) result.Blocks.Add(FromWordTable(t));
        }
        return result;
    }

    static W.Paragraph ToWordParagraph(Paragraph p)
    {
        var wp=new W.Paragraph();
        var pp=new W.ParagraphProperties();

        if(p.BreakPageBefore) pp.Append(new W.PageBreakBefore());
        pp.Justification=new W.Justification{Val=p.TextAlignment switch
        {
            TextAlignment.Center=>W.JustificationValues.Center,
            TextAlignment.Right=>W.JustificationValues.Right,
            TextAlignment.Justify=>W.JustificationValues.Both,
            _=>W.JustificationValues.Left
        }};

        wp.Append(pp);
        AppendInlines(wp,p.Inlines);
        return wp;
    }

    static void AppendInlines(W.Paragraph target, InlineCollection inlines)
    {
        foreach(var inline in inlines)
        {
            if(inline is Run run) target.Append(ToWordRun(run));
            else if(inline is LineBreak) target.Append(new W.Run(new W.Break()));
            else if(inline is Span span) AppendInlines(target,span.Inlines);
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

    static W.Table ToWordTable(Table table)
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
                    if(block is Paragraph p)wc.Append(ToWordParagraph(p));
                if(!wc.Elements<W.Paragraph>().Any())wc.Append(new W.Paragraph());
                wr.Append(wc);
            }
            wt.Append(wr);
        }
        return wt;
    }

    static Paragraph FromWordParagraph(W.Paragraph p)
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
                if(item is W.Text txt) result.Inlines.Add(FromWordRun(r,txt.Text));
                else if(item is W.Break) result.Inlines.Add(new LineBreak());
            }
        }
        return result;
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

    static Table FromWordTable(W.Table source)
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
                foreach(var p in cell.Elements<W.Paragraph>())tc.Blocks.Add(FromWordParagraph(p));
                if(tc.Blocks.Count==0)tc.Blocks.Add(new Paragraph());
                tr.Cells.Add(tc);
            }
        }
        return t;
    }

    static void ApplyImportedStyle(Paragraph p,string style)
    {
        switch(style.ToLowerInvariant())
        {
            case "heading1": p.FontSize=24;p.FontWeight=FontWeights.Bold;break;
            case "heading2": p.FontSize=20;p.FontWeight=FontWeights.Bold;break;
            case "heading3": p.FontSize=17;p.FontWeight=FontWeights.SemiBold;break;
            case "quote": p.FontStyle=FontStyles.Italic;p.Margin=new Thickness(36,8,36,8);break;
        }
    }
}
