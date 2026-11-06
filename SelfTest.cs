using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ICSharpCode.AvalonEdit.Highlighting;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public static class SelfTest
{
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"CtrlZedSelfTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if(Branding.CreateIconImageSource()==null)return 26;
            if(HighlightingManager.Instance.GetDefinition("VBNET")==null)return 35;

            if(!ContextualSpellingEngine.TryFindCompletedSentence("This is complete. ",17,out var simpleSentence) ||
               simpleSentence.Text!="This is complete.")return 36;
            if(!ContextualSpellingEngine.TryFindCompletedSentence("Dr. Smith arrived. ",19,out var abbreviationSentence) ||
               abbreviationSentence.Text!="Dr. Smith arrived.")return 37;
            if(ContextualSpellingEngine.TryFindCompletedSentence("I spoke to Dr. ",15,out _))return 38;
            if(!ContextualSpellingEngine.TryFindCompletedSentence("Version 3.2 is stable. ",23,out var decimalSentence) ||
               decimalSentence.Text!="Version 3.2 is stable.")return 39;
            if(ContextualSpellingEngine.ChooseHighConfidenceCorrection("adress",new[]{"address","across"})!="address")return 40;
            if(ContextualSpellingEngine.ChooseHighConfidenceCorrection("Adress",new[]{"Address"})!=null)return 41;

            var chart=AiResultRenderer.TryRenderChart("TITLE: Test movements\nFTSE 100|1.2\nDAX|-0.8\nNikkei 225|2.1");
            if(chart==null||chart.PixelWidth<400)return 27;
            var prompt=AiResultRenderer.MacroPrompt("Insert today's date",".czm",SafeMacroLanguage.Reference,"");
            if(!prompt.Contains("INSERT_FIELD")||!prompt.Contains("Insert today's date"))return 28;
            var csharpEditor=new RichTextBox();
            CompiledMacroHost.RunAsync(
@"using System.Windows.Controls;
public static class TestMacro {
    public static void Run(RichTextBox Editor) { Editor.Tag = ""CSharpMacroOK""; }
}",".cs",csharpEditor,Application.Current?.MainWindow??new Window()).GetAwaiter().GetResult();
            if(!string.Equals(csharpEditor.Tag?.ToString(),"CSharpMacroOK",StringComparison.Ordinal))return 30;

            var vbEditor=new RichTextBox();
            var vbSecretName="VBMacroSecret_"+Guid.NewGuid().ToString("N");
            CtrlZedSecrets.Set(vbSecretName,"ok");
            CompiledMacroHost.RunAsync(
$@"Imports System.Windows.Controls
Public Module TestMacro
    Public Sub Run(Editor As RichTextBox)
        Dim s As String = CtrlZedSecrets.Get(""{vbSecretName}"")
        Editor.Tag = ""VBMacroOK"" & vbCrLf & s
    End Sub
End Module
",".vb",vbEditor,Application.Current?.MainWindow??new Window()).GetAwaiter().GetResult();
            if(!string.Equals(vbEditor.Tag?.ToString(),"VBMacroOK"+Environment.NewLine+"ok",StringComparison.Ordinal))return 31;
            CtrlZedSecrets.Remove(vbSecretName);

            var secretName="SelfTest_"+Guid.NewGuid().ToString("N");
            CtrlZedSecrets.Set(secretName,"secret-value");
            if(!CtrlZedSecrets.Exists(secretName) || CtrlZedSecrets.Get(secretName)!="secret-value")return 32;
            CtrlZedSecrets.Remove(secretName);
            if(CtrlZedSecrets.Exists(secretName))return 33;

            if(string.IsNullOrWhiteSpace(CtrlZedAi.GetModel("Gemini")))return 34;
            var profile=AiDirectService.GetProfile("ChatGPT");
            if(string.IsNullOrWhiteSpace(profile.Model))return 29;

            var doc=new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("CtrlZed self test")){FontSize=24,FontWeight=System.Windows.FontWeights.Bold});
            doc.Blocks.Add(new Paragraph(new Run("Find across ordinary text.")));

            var pixels=new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255};
            var bitmap=new WriteableBitmap(2,2,96,96,PixelFormats.Bgra32,null);
            bitmap.WritePixels(new System.Windows.Int32Rect(0,0,2,2),pixels,8,0);
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(new Image{Source=bitmap,Width=32,Height=32})));

            var table=new Table();
            table.Columns.Add(new TableColumn());table.Columns.Add(new TableColumn());
            var group=new TableRowGroup();table.RowGroups.Add(group);
            var row=new TableRow();group.Rows.Add(row);
            row.Cells.Add(new TableCell(new Paragraph(new Run("A"))));
            row.Cells.Add(new TableCell(new Paragraph(new Run("B"))));
            doc.Blocks.Add(table);

            var bulletList=new List{MarkerStyle=TextMarkerStyle.Disc};
            bulletList.ListItems.Add(new ListItem(new Paragraph(new Run("Bullet one"))));
            bulletList.ListItems.Add(new ListItem(new Paragraph(new Run("Bullet two"))));
            doc.Blocks.Add(bulletList);

            var numberedList=new List{MarkerStyle=TextMarkerStyle.Decimal};
            numberedList.ListItems.Add(new ListItem(new Paragraph(new Run("Number one"))));
            numberedList.ListItems.Add(new ListItem(new Paragraph(new Run("Number two"))));
            doc.Blocks.Add(numberedList);

            var rtf=Path.Combine(root,"test.rtf");
            using(var fs=File.Create(rtf))new TextRange(doc.ContentStart,doc.ContentEnd).Save(fs,System.Windows.DataFormats.Rtf);
            var rtfDoc=new FlowDocument();
            using(var fs=File.OpenRead(rtf))new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Load(fs,System.Windows.DataFormats.Rtf);
            if(!new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Text.Contains("CtrlZed self test"))return 11;

            var docx=Path.Combine(root,"test.docx");
            DocxInterop.Save(docx,doc,"Test {page} of {pages}","{filename}",794,1123,new System.Windows.Thickness(72),"Test Title","Test Author","Test Subject","alpha, beta");
            if(!File.Exists(docx)||new FileInfo(docx).Length<500)return 12;
            using(var zip=ZipFile.OpenRead(docx))
                if(zip.GetEntry("word/document.xml")==null)return 13;

            using(var validationDoc=WordprocessingDocument.Open(docx,false))
            {
                var validationErrors=new OpenXmlValidator().Validate(validationDoc).Take(1).ToList();
                if(validationErrors.Count>0)
                {
                    try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),validationErrors[0].Description??"Open XML validation error");}catch{}
                    return 17;
                }
            }

            var layout=DocxInterop.LoadWithLayout(docx);
            var imported=layout.Document;
            if(!layout.Header.Contains("{page}")||!layout.Header.Contains("{pages}"))return 18;
            if(!layout.Footer.Contains("{filename}"))return 19;
            if(Math.Abs(layout.PageWidth-794)>2||Math.Abs(layout.PageHeight-1123)>2)return 20;
            if(layout.Margins==null||Math.Abs(layout.Margins.Value.Left-72)>2)return 21;
            if(layout.Title!="Test Title"||layout.Author!="Test Author"||layout.Subject!="Test Subject")return 22;
            if(!layout.Keywords.Contains("alpha"))return 23;
            var lists=imported.Blocks.OfType<List>().ToList();
            if(lists.Count<2)return 24;
            if(lists[0].MarkerStyle!=TextMarkerStyle.Disc||lists[1].MarkerStyle!=TextMarkerStyle.Decimal)return 25;
            var text=new TextRange(imported.ContentStart,imported.ContentEnd).Text;
            if(!text.Contains("CtrlZed self test")||!text.Contains("A")||!text.Contains("B"))return 14;
            if(!imported.Blocks.OfType<Table>().Any())return 15;
            var importedImage=imported.Blocks.OfType<Paragraph>()
                .SelectMany(p=>p.Inlines.OfType<InlineUIContainer>())
                .Any(ui=>ui.Child is Image);
            if(!importedImage)return 16;

            return 0;
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());}catch{}
            return 99;
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{}
        }
    }
}
