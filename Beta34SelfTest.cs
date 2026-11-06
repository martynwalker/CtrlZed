using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using W=DocumentFormat.OpenXml.Wordprocessing;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public static class Beta34SelfTest
{
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"CtrlZedBeta34_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalogueErrors=CtrlZedKeyboardCatalogue.Validate();
            if(catalogueErrors.Count>0)
            {
                File.WriteAllLines(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),catalogueErrors);
                return 60;
            }
            var wordShortcuts=CtrlZedKeyboardCatalogue.All
                .Where(x=>!string.IsNullOrWhiteSpace(x.Word))
                .ToDictionary(x=>x.Command,x=>x.Word,StringComparer.OrdinalIgnoreCase);
            if(!wordShortcuts.TryGetValue("Next spelling error",out var spelling) || spelling!="F7")return 61;
            if(!wordShortcuts.TryGetValue("Contextual thesaurus",out var thesaurus) || thesaurus!="Shift+F7")return 62;

            var document=new FlowDocument();
            var paragraph=new Paragraph
            {
                Margin=new Thickness(24,12,18,15),
                TextIndent=10,
                KeepTogether=true,
                KeepWithNext=true,
                LineHeight=22
            };
            var coloured=new Run("Colour")
            {
                Foreground=new SolidColorBrush(Color.FromRgb(178,34,34)),
                Background=new SolidColorBrush(Color.FromRgb(255,245,200)),
                FontFamily=new FontFamily("Calibri"),
                FontSize=16,
                TextDecorations=TextDecorations.Strikethrough
            };
            var raised=new Run("2"){BaselineAlignment=BaselineAlignment.Superscript,FontSize=12};
            paragraph.Inlines.Add(coloured);
            paragraph.Inlines.Add(new Run(" H"));
            paragraph.Inlines.Add(raised);
            paragraph.Inlines.Add(new Run("O"));
            document.Blocks.Add(paragraph);
            document.Blocks.Add(new Paragraph(new Run("Heading round trip"))
            {
                FontSize=24,FontWeight=FontWeights.Bold,KeepWithNext=true
            });

            var list=new List{MarkerStyle=TextMarkerStyle.LowerLatin};
            list.ListItems.Add(new ListItem(new Paragraph(new Run("alpha item"))));
            list.ListItems.Add(new ListItem(new Paragraph(new Run("beta item"))));
            document.Blocks.Add(list);

            var table=new Table{CellSpacing=0};
            table.Columns.Add(new TableColumn{Width=new GridLength(110)});
            table.Columns.Add(new TableColumn{Width=new GridLength(130)});
            var group=new TableRowGroup();table.RowGroups.Add(group);
            var row=new TableRow();group.Rows.Add(row);
            row.Cells.Add(new TableCell(new Paragraph(new Run("spanning cell")))
            {
                ColumnSpan=2,
                Background=new SolidColorBrush(Color.FromRgb(220,235,250))
            });
            document.Blocks.Add(table);

            var path=Path.Combine(root,"beta34.docx");
            DocxInterop.Save(path,document,"Header {page}","Footer {pages}",794,1123,new Thickness(72),"Beta 34","CtrlZed","Round trip","docx, formatting");
            if(!File.Exists(path) || new FileInfo(path).Length<1000)return 63;

            using(var word=WordprocessingDocument.Open(path,false))
            {
                var error=new OpenXmlValidator().Validate(word).FirstOrDefault();
                if(error!=null)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),error.Description??"Open XML validation error");
                    return 64;
                }
                var styleIds=word.MainDocumentPart?.StyleDefinitionsPart?.Styles?
                    .Elements<W.Style>().Select(x=>x.StyleId?.Value).Where(x=>x!=null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(styleIds==null || !styleIds.Contains("Heading1") || !styleIds.Contains("Quote") || !styleIds.Contains("Caption"))return 65;
            }

            var loaded=DocxInterop.LoadWithLayout(path);
            var imported=loaded.Document;
            var first=imported.Blocks.OfType<Paragraph>()
                .FirstOrDefault(x=>new TextRange(x.ContentStart,x.ContentEnd).Text.Contains("Colour",StringComparison.Ordinal));
            if(first==null)return 66;
            if(Math.Abs(first.Margin.Left-24)>1 || Math.Abs(first.Margin.Top-12)>1 ||
               Math.Abs(first.Margin.Right-18)>1 || Math.Abs(first.Margin.Bottom-15)>1)return 67;
            if(Math.Abs(first.TextIndent-10)>1 || !first.KeepTogether || !first.KeepWithNext)return 68;
            if(double.IsNaN(first.LineHeight) || Math.Abs(first.LineHeight-22)>1)return 69;

            var colourRun=first.Inlines.OfType<Run>().FirstOrDefault(x=>x.Text=="Colour");
            if(colourRun?.Foreground is not SolidColorBrush foreground || foreground.Color.R!=178 || foreground.Color.G!=34 || foreground.Color.B!=34)return 70;
            if(colourRun.Background is not SolidColorBrush background || background.Color.R!=255 || background.Color.G!=245 || background.Color.B!=200)return 71;
            if(colourRun.TextDecorations?.Any(x=>x.Location==TextDecorationLocation.Strikethrough)!=true)return 72;
            var super=first.Inlines.OfType<Run>().FirstOrDefault(x=>x.Text=="2");
            if(super?.BaselineAlignment!=BaselineAlignment.Superscript)return 73;

            var importedList=imported.Blocks.OfType<List>().FirstOrDefault();
            if(importedList==null || importedList.MarkerStyle!=TextMarkerStyle.LowerLatin || importedList.ListItems.Count!=2)return 74;
            var importedTable=imported.Blocks.OfType<Table>().FirstOrDefault();
            var importedCell=importedTable?.RowGroups.FirstOrDefault()?.Rows.FirstOrDefault()?.Cells.FirstOrDefault();
            if(importedCell==null || importedCell.ColumnSpan!=2)return 75;
            if(importedCell.Background is not SolidColorBrush cellBackground || cellBackground.Color.B!=250)return 76;

            return 0;
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());}catch{ }
            return 79;
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{ }
        }
    }
}
