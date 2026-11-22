using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ICSharpCode.AvalonEdit.Highlighting;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public static class SelfTest
{
    static void VerifyFollowingParagraphStyle(string originalRole,string followingRole)
    {
        var run=new Run("Styled text");
        var original=new Paragraph(new Span(run))
        {
            FontWeight=FontWeights.Bold,FontSize=24,
            Tag=MainWindow.ParagraphStyleTagPrefix+originalRole
        };
        var document=new FlowDocument(original);
        var editor=new RichTextBox(document);
        editor.CaretPosition=run.ContentStart.GetPositionAtOffset(2)!;
        if(MainWindow.ParagraphEndingAtSelection(editor)!=null)
            throw new InvalidOperationException("A split inside a paragraph would change its following style.");

        editor.CaretPosition=run.ContentEnd;
        if(MainWindow.ParagraphEndingAtSelection(editor)!=original)
            throw new InvalidOperationException("Return did not recognise the end of a styled paragraph.");
        // The built-in Return command requires an active, keyboard-focused editor.
        // Use WPF's paragraph split directly in the headless test.
        var next=editor.CaretPosition.InsertParagraphBreak();
        editor.CaretPosition=next;
        var added=next.Paragraph;
        if(added==null || added==original)
            throw new InvalidOperationException("The style test did not create a following paragraph.");
        var format=StyleSetStore.Defaults()["Default"].Styles[followingRole];
        MainWindow.ApplyFollowingStyle(editor,added,followingRole,format);
        if(!Equals(added.Tag,MainWindow.ParagraphStyleTagPrefix+followingRole) ||
           !Equals(editor.Selection.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Normal) ||
           !Equals(editor.Selection.GetPropertyValue(TextElement.FontSizeProperty),format.SizePt*96/72) ||
           added.FontWeight!=FontWeights.Normal || added.FontSize!=format.SizePt*96/72 ||
           original.FontWeight!=FontWeights.Bold || original.FontSize!=24)
            throw new InvalidOperationException($"The following paragraph did not apply {followingRole} after {originalRole}.");
    }

    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"CtrlZedSelfTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Filename and palette checks");
            FileNameSelfTest.Run();
            if(NativeColourDialog.InitialColourRef("#FF0000")!=0x0000FF ||
               NativeColourDialog.InitialColourRef("#0000FF")!=0xFF0000 ||
               NativeColourDialog.ColourRefToHex(0x00CC66)!="#66CC00")
                throw new InvalidOperationException("The native colour palette swapped RGB colour channels.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Icon and editor highlighting");
            if(Branding.CreateIconImageSource()==null)return 26;
            if(HighlightingManager.Instance.GetDefinition("VBNET")==null)return 35;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Local spelling");
            const string simpleText="This is complete. ";
            if(!ContextualSpellingEngine.TryFindCompletedSentence(simpleText,simpleText.Length,out var simpleSentence) ||
               simpleSentence.Text!="This is complete.")return 36;
            const string abbreviationText="Dr. Smith arrived. ";
            if(!ContextualSpellingEngine.TryFindCompletedSentence(abbreviationText,abbreviationText.Length,out var abbreviationSentence) ||
               abbreviationSentence.Text!="Dr. Smith arrived.")return 37;
            const string unfinishedAbbreviation="I spoke to Dr. ";
            if(ContextualSpellingEngine.TryFindCompletedSentence(unfinishedAbbreviation,unfinishedAbbreviation.Length,out _))return 38;
            const string decimalText="Version 3.2 is stable. ";
            if(!ContextualSpellingEngine.TryFindCompletedSentence(decimalText,decimalText.Length,out var decimalSentence) ||
               decimalSentence.Text!="Version 3.2 is stable.")return 39;
            if(ContextualSpellingEngine.ChooseHighConfidenceCorrection("adress",new[]{"address","across"})!="address")return 40;
            if(ContextualSpellingEngine.ChooseHighConfidenceCorrection("Adress",new[]{"Address"})!=null)return 41;
            if(ContextualSpellingEngine.ParseAiChoice("address",new[]{"address","across"})!="address")return 42;
            if(ContextualSpellingEngine.ParseAiChoice("Perhaps address",new[]{"address","across"})!=null)return 43;
            var spellingPrompt=ContextualSpellingEngine.BuildAiChoicePrompt("Send it to this adress.","adress",new[]{"address","across"},"Formal letter");
            if(!spellingPrompt.Contains("Return exactly") || !spellingPrompt.Contains("Send it to this adress."))return 44;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Chart renderer");
            var chart=AiResultRenderer.TryRenderChart("TITLE: Test movements\nFTSE 100|1.2\nDAX|-0.8\nNikkei 225|2.1");
            if(chart==null||chart.PixelWidth<400)return 27;
            var prompt=AiResultRenderer.MacroPrompt("Insert today's date",".czm",SafeMacroLanguage.Reference,"");
            if(!prompt.Contains("INSERT_FIELD")||!prompt.Contains("Insert today's date"))return 28;
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Compiled CSharp macro");
            var csharpEditor=new RichTextBox();
            CompiledMacroHost.RunAsync(
@"using System.Windows.Controls;
public static class TestMacro {
    public static void Run(RichTextBox Editor) { Editor.Tag = ""CSharpMacroOK""; }
}",".cs",csharpEditor,Application.Current?.MainWindow??new Window()).GetAwaiter().GetResult();
            if(!string.Equals(csharpEditor.Tag?.ToString(),"CSharpMacroOK",StringComparison.Ordinal))return 30;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Compiled Visual Basic macro");
            var vbEditor=new RichTextBox();
            var vbSecretName="VBMacroSecret_"+Guid.NewGuid().ToString("N");
            CtrlZedSecrets.Set(vbSecretName,"ok");
            var vbSource=$@"Option Explicit On
Imports System.Windows.Controls
Public Module TestMacro
    Public Sub Run(Editor As RichTextBox)
        Dim s As String = CtrlZedSecrets.Get(""{vbSecretName}"")
        ' Set the WPF selection text.
        Editor.Selection.Text = ""Set text with VB.NET""
        Editor.Tag = ""VBMacroOK"" & vbCrLf & s
    End Sub
End Module
";
            var vbDiagnostics=CompiledMacroHost.Check(vbSource,".vb");
            if(vbDiagnostics.Any(d=>d.StartsWith("Error ",StringComparison.Ordinal) ||
                                    d.StartsWith("Language preflight:",StringComparison.Ordinal)))
                throw new InvalidOperationException(string.Join(Environment.NewLine,vbDiagnostics));
            CompiledMacroHost.RunAsync(vbSource,".vb",vbEditor,Application.Current?.MainWindow??new Window()).GetAwaiter().GetResult();
            var vbText=new TextRange(vbEditor.Document.ContentStart,vbEditor.Document.ContentEnd).Text.Trim();
            if(vbText!="Set text with VB.NET" ||
               !string.Equals(vbEditor.Tag?.ToString(),"VBMacroOK"+Environment.NewLine+"ok",StringComparison.Ordinal))return 31;
            CtrlZedSecrets.Remove(vbSecretName);

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Secret storage");
            var secretName="SelfTest_"+Guid.NewGuid().ToString("N");
            CtrlZedSecrets.Set(secretName,"secret-value");
            if(!CtrlZedSecrets.Exists(secretName) || CtrlZedSecrets.Get(secretName)!="secret-value")return 32;
            CtrlZedSecrets.Remove(secretName);
            if(CtrlZedSecrets.Exists(secretName))return 33;

            if(string.IsNullOrWhiteSpace(CtrlZedAi.GetModel("Gemini")))return 34;
            var profile=AiDirectService.GetProfile("ChatGPT");
            if(string.IsNullOrWhiteSpace(profile.Model))return 29;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Following paragraph styles");
            var legacyStyle=System.Text.Json.JsonSerializer.Deserialize<StyleSetStore.TextStyle>("{}");
            if(legacyStyle?.FollowingStyle!="Normal")
                throw new InvalidOperationException("An older style set did not default its following style to Normal.");

            VerifyFollowingParagraphStyle("Heading 1","Normal");
            VerifyFollowingParagraphStyle("Quote","Caption");

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Document fixture");
            var doc=new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("CtrlZed self test")){FontSize=24,FontWeight=System.Windows.FontWeights.Bold});
            doc.Blocks.Add(new Paragraph(new Run("Find across ordinary text.")));

            var pixels=new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,255,255,255,255};
            var bitmap=new WriteableBitmap(2,2,96,96,PixelFormats.Bgra32,null);
            bitmap.WritePixels(new System.Windows.Int32Rect(0,0,2,2),pixels,8,0);
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(new Image{Source=bitmap,Width=32,Height=32})));

            var nestedPicture=new InlineUIContainer(new Image{Source=bitmap,Width=32,Height=32});
            var pictureParagraph=new Paragraph(new Span(new Span(nestedPicture))){TextAlignment=TextAlignment.Center};
            if(!ReferenceEquals(MainWindow.GetPictureParagraph(nestedPicture),pictureParagraph))
                throw new InvalidOperationException("The picture properties cannot resolve the alignment of an inline picture inside a formatted span.");

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

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: RTF roundtrip");
            var rtf=Path.Combine(root,"test.rtf");
            using(var fs=File.Create(rtf))new TextRange(doc.ContentStart,doc.ContentEnd).Save(fs,System.Windows.DataFormats.Rtf);
            var rtfDoc=new FlowDocument();
            using(var fs=File.OpenRead(rtf))new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Load(fs,System.Windows.DataFormats.Rtf);
            if(!new TextRange(rtfDoc.ContentStart,rtfDoc.ContentEnd).Text.Contains("CtrlZed self test"))return 11;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: DOCX export");
            var docx=Path.Combine(root,"test.docx");
            DocxInterop.Save(docx,doc,"Test {page} of {pages}","{filename}",794,1123,new System.Windows.Thickness(72),"Test Title","Test Author","Test Subject","alpha, beta");
            if(!File.Exists(docx)||new FileInfo(docx).Length<500)return 12;
            using(var zip=ZipFile.OpenRead(docx))
                if(zip.GetEntry("word/document.xml")==null)return 13;

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: DOCX schema");
            using(var validationDoc=WordprocessingDocument.Open(docx,false))
            {
                var validationErrors=new OpenXmlValidator().Validate(validationDoc).Take(1).ToList();
                if(validationErrors.Count>0)
                {
                    try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),validationErrors[0].Description??"Open XML validation error");}catch{}
                    return 17;
                }
            }

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: DOCX import");
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

            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core: Document protection");
            TestDocumentProtection();
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

    static void TestDocumentProtection()
    {
        using var password = TestPassword("Protected editor test 194#");
        using var wrongPassword = TestPassword("Incorrect password");
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var contents = zip.CreateEntry("document.rtf").Open();
            contents.Write("CtrlZed body"u8);
        }
        using var encrypted = new MemoryStream();
        using var encryptedAgain = new MemoryStream();
        CzdProtection.Encrypt(encrypted, archive.ToArray(), password);
        CzdProtection.Encrypt(encryptedAgain, archive.ToArray(), password);
        if (encrypted.ToArray().SequenceEqual(encryptedAgain.ToArray()))
            throw new InvalidOperationException("Protected saves reused encryption parameters.");

        encrypted.Position = 0;
        archive.Position = 0;
        if (!CzdProtection.IsProtected(encrypted) || CzdProtection.IsProtected(archive) || encrypted.Position != 0)
            throw new InvalidOperationException("Protected and legacy CtrlZed files were not distinguished.");
        var decrypted = CzdProtection.Decrypt(encrypted, password);
        if (!decrypted.SequenceEqual(archive.ToArray()))
            throw new InvalidOperationException("The protected archive payload changed on round trip.");

        AssertAuthenticationFails(encrypted.ToArray(), wrongPassword);
        var tampered = encrypted.ToArray();
        tampered[^1] ^= 1;
        AssertAuthenticationFails(tampered, password);

        static SecureString TestPassword(string value)
        {
            var secure = new SecureString();
            foreach (var character in value) secure.AppendChar(character);
            secure.MakeReadOnly();
            return secure;
        }

        static void AssertAuthenticationFails(byte[] bytes, SecureString password)
        {
            using var input = new MemoryStream(bytes, writable: false);
            try { CzdProtection.Decrypt(input, password); }
            catch (AuthenticationTagMismatchException) { return; }
            throw new InvalidOperationException("Protected archive accepted an incorrect password or changed ciphertext.");
        }
    }

}
