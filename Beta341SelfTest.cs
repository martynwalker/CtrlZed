using DocumentFormat.OpenXml.Packaging;
using W=DocumentFormat.OpenXml.Wordprocessing;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public static class Beta341SelfTest
{
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"CtrlZedBeta341_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var errors=CtrlZedKeyboardCatalogue.Validate();
            if(errors.Count>0)
            {
                File.WriteAllLines(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),errors);
                return 80;
            }

            var wordPerfect=CtrlZedKeyboardCatalogue.All
                .Select(x=>x.WordPerfect)
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(wordPerfect.Count<15 || !wordPerfect.Contains("F4") ||
               !wordPerfect.Contains("Shift+F10") || !wordPerfect.Contains("Ctrl+F2"))return 81;

            var wordStar=CtrlZedKeyboardCatalogue.All
                .Select(x=>x.WordStar)
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(wordStar.Count<20 || !wordStar.Contains("Ctrl+Q, S") ||
               !wordStar.Contains("Ctrl+K, R") || !wordStar.Contains("Ctrl+Y"))return 82;

            var csharpPrompt=AiResultRenderer.MacroPrompt("Use ChatGPT to edit the selection.",".cs","","","ChatGPT");
            var vbPrompt=AiResultRenderer.MacroPrompt("Use Gemini to edit the selection.",".vb","","","Gemini");
            if(!csharpPrompt.Contains("AI_API_KEY",StringComparison.Ordinal) ||
               !vbPrompt.Contains("AI_API_KEY",StringComparison.Ordinal))return 83;

            var csharpSource="public static class Macro { public static void Run(System.Windows.Controls.RichTextBox Editor) { var key=CtrlZedSecrets.Get(\"ChatGPTKey\"); } }";
            var embeddedCsharp=AiResultRenderer.EmbedMacroPrompt(csharpSource,".cs","Create the macro.","ChatGPT");
            if(!embeddedCsharp.Contains("public const string AI_API_KEY = \"ChatGPTKey\";",StringComparison.Ordinal) ||
               !embeddedCsharp.Contains("CtrlZedSecrets.Get(AI_API_KEY)",StringComparison.Ordinal))return 84;

            var vbSource="Public Module Macro\r\nPublic Sub Run(Editor As System.Windows.Controls.RichTextBox)\r\nDim key=CtrlZedSecrets.Get(\"GeminiKey\")\r\nEnd Sub\r\nEnd Module";
            var embeddedVb=AiResultRenderer.EmbedMacroPrompt(vbSource,".vb","Create the macro.","Gemini");
            if(!embeddedVb.Contains("Public Const AI_API_KEY As String = \"GeminiKey\"",StringComparison.Ordinal) ||
               !embeddedVb.Contains("CtrlZedSecrets.Get(AI_API_KEY)",StringComparison.Ordinal))return 85;

            var document=new FlowDocument();
            var shadedParagraph=new Paragraph
            {
                Background=new SolidColorBrush(Color.FromRgb(224,236,246)),
                Margin=new Thickness(0,5,0,7)
            };
            shadedParagraph.Inlines.Add(new Run("Shaded paragraph. "));
            var highlighted=new Span(new Run("Highlighted words")){Background=Brushes.Yellow};
            shadedParagraph.Inlines.Add(highlighted);
            document.Blocks.Add(shadedParagraph);

            var path=Path.Combine(root,"beta341.docx");
            DocxInterop.Save(path,document,"","",794,1123,new Thickness(72),"Beta 34.1","CtrlZed","Highlight round trip","docx");
            if(!File.Exists(path) || new FileInfo(path).Length<1000)return 86;

            DocxLoadResult loadedWhileShared;
            using(var wordLikeHandle=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))
            {
                loadedWhileShared=DocxInterop.LoadWithLayout(path);
            }
            if(loadedWhileShared.Document.Blocks.OfType<Paragraph>().FirstOrDefault()==null)return 87;

            using(var word=WordprocessingDocument.Open(path,false))
            {
                var paragraph=word.MainDocumentPart?.Document?.Body?.Elements<W.Paragraph>().FirstOrDefault();
                if(paragraph?.ParagraphProperties?.GetFirstChild<W.Shading>()?.Fill?.Value!="E0ECF6")return 88;
                var highlight=paragraph.Descendants<W.Highlight>().FirstOrDefault()?.Val?.Value;
                if(highlight==null || highlight.ToString()!="yellow")return 89;
            }

            var imported=DocxInterop.LoadWithLayout(path).Document;
            var importedParagraph=imported.Blocks.OfType<Paragraph>().FirstOrDefault();
            if(importedParagraph?.Background is not SolidColorBrush paragraphBackground ||
               paragraphBackground.Color.R!=224 || paragraphBackground.Color.G!=236 || paragraphBackground.Color.B!=246)return 90;
            var importedHighlight=importedParagraph.Inlines.OfType<Run>()
                .FirstOrDefault(x=>x.Text.Contains("Highlighted",StringComparison.Ordinal));
            if(importedHighlight?.Background is not SolidColorBrush highlightBackground ||
               highlightBackground.Color.R<240 || highlightBackground.Color.G<220)return 91;

            return 0;
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());}catch{ }
            return 99;
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{ }
        }
    }
}
