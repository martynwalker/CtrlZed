using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using UglyToad.PdfPig;

namespace CtrlZed;

public static class ImportService
{
    public static FlowDocument Load(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Import path is empty.",nameof(path));
        if(!File.Exists(path))throw new FileNotFoundException("Import file not found.",path);

        var ext=Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".docx"=>DocxInterop.Load(path),
            ".rtf"=>LoadRtf(path),
            ".txt"=>LoadText(File.ReadAllText(path)),
            ".md" or ".markdown"=>LoadMarkdown(File.ReadAllText(path)),
            ".html" or ".htm"=>LoadHtml(File.ReadAllText(path)),
            ".pdf"=>LoadPdf(path),
            _=>throw new NotSupportedException($"CtrlZed cannot import '{ext}' files yet.")
        };
    }

    static FlowDocument LoadRtf(string path)
    {
        var doc=new FlowDocument();
        using var stream=File.OpenRead(path);
        new TextRange(doc.ContentStart,doc.ContentEnd).Load(stream,DataFormats.Rtf);
        return doc;
    }

    static FlowDocument LoadText(string text)
    {
        var doc=new FlowDocument();
        foreach(var raw in Normalise(text).Split('\n'))
            doc.Blocks.Add(new Paragraph(new Run(raw)));
        if(doc.Blocks.Count==0)doc.Blocks.Add(new Paragraph());
        return doc;
    }

    static FlowDocument LoadMarkdown(string markdown)
    {
        var doc=new FlowDocument();
        var lines=Normalise(markdown).Split('\n');
        foreach(var raw in lines)
        {
            var line=raw.TrimEnd();
            if(string.IsNullOrWhiteSpace(line))
            {
                doc.Blocks.Add(new Paragraph());
                continue;
            }

            if(line.StartsWith("### ")){doc.Blocks.Add(Heading(line[4..],17));continue;}
            if(line.StartsWith("## ")){doc.Blocks.Add(Heading(line[3..],20));continue;}
            if(line.StartsWith("# ")){doc.Blocks.Add(Heading(line[2..],24));continue;}

            if(Regex.IsMatch(line,@"^\s*[-*+]\s+"))
            {
                var list=new List{MarkerStyle=TextMarkerStyle.Disc};
                var itemText=Regex.Replace(line,@"^\s*[-*+]\s+","");
                list.ListItems.Add(new ListItem(new Paragraph(new Run(StripInlineMarkdown(itemText)))));
                doc.Blocks.Add(list);
                continue;
            }

            if(Regex.IsMatch(line,@"^\s*\d+[\.)]\s+"))
            {
                var list=new List{MarkerStyle=TextMarkerStyle.Decimal};
                var itemText=Regex.Replace(line,@"^\s*\d+[\.)]\s+","");
                list.ListItems.Add(new ListItem(new Paragraph(new Run(StripInlineMarkdown(itemText)))));
                doc.Blocks.Add(list);
                continue;
            }

            doc.Blocks.Add(new Paragraph(new Run(StripInlineMarkdown(line))));
        }
        if(doc.Blocks.Count==0)doc.Blocks.Add(new Paragraph());
        return doc;
    }

    static Paragraph Heading(string text,double size)=>new(new Run(StripInlineMarkdown(text)))
    {
        FontSize=size,FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,5)
    };

    static string StripInlineMarkdown(string text)
    {
        text=Regex.Replace(text,@"!\[([^\]]*)\]\([^\)]*\)","$1");
        text=Regex.Replace(text,@"\[([^\]]+)\]\([^\)]+\)","$1");
        text=Regex.Replace(text,@"(\*\*|__)(.*?)\1","$2");
        text=Regex.Replace(text,@"(\*|_)(.*?)\1","$2");
        text=Regex.Replace(text,@"\x60([^\x60]+)\x60","$1");
        return text;
    }

    static FlowDocument LoadHtml(string html)
    {
        html=Regex.Replace(html,@"(?is)<(script|style).*?>.*?</\1>","");
        html=Regex.Replace(html,@"(?i)<br\s*/?>","\n");
        html=Regex.Replace(html,@"(?i)</(p|div|h[1-6]|li|tr)>","\n");
        html=Regex.Replace(html,@"(?s)<[^>]+>","");
        return LoadText(WebUtility.HtmlDecode(html));
    }

    static FlowDocument LoadPdf(string path)
    {
        try
        {
            var doc=new FlowDocument();
            using var pdf=PdfDocument.Open(path);
            for(var i=1;i<=pdf.NumberOfPages;i++)
            {
                var page=pdf.GetPage(i);
                var text=page.Text??"";
                var p=new Paragraph(new Run(text));
                if(i>1)p.BreakPageBefore=true;
                doc.Blocks.Add(p);
            }
            if(doc.Blocks.Count==0)doc.Blocks.Add(new Paragraph());
            return doc;
        }
        catch(Exception ex)
        {
            throw new InvalidOperationException(
                "CtrlZed could not import this PDF. It may be encrypted, protected, damaged, or use content that cannot be extracted as text.",
                ex);
        }
    }

    static string Normalise(string text)=>(text??"").Replace("\r\n","\n").Replace('\r','\n');
}
