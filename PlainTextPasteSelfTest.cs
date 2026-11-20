using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    internal static int RunPlainTextPasteSelfTest()
    {
        MainWindow? window=null;
        IDataObject? previousClipboard=null;
        try
        {
            previousClipboard=Clipboard.GetDataObject();
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow();
            window.Show();
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.CheckPlainTextPaste();
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());
            return 123;
        }
        finally
        {
            try
            {
                if(previousClipboard!=null)Clipboard.SetDataObject(previousClipboard,true);
                else Clipboard.Clear();
            }
            catch { }
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();
                window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();
                window.beta35PaginationTimer?.Stop();
                window.beta35PaginationCancellation?.Cancel();
                window.Close();
            }
        }
    }

    void CheckPlainTextPaste()
    {
        languageSettings.GrammarAndStyleSuggestions=false;
        SpellCheck.SetIsEnabled(Editor,false);
        const string plain="Pasted plain\r\nsecond line";
        const string rtf="{\\rtf1\\ansi{\\fonttbl{\\f0 Times New Roman;}}{\\colortbl;\\red255\\green0\\blue0;}\\f0\\fs48\\b\\cf1 RICH SOURCE\\b0}";
        var rich=new DataObject();
        rich.SetData(DataFormats.UnicodeText,plain,false);
        rich.SetData(DataFormats.Rtf,rtf,false);
        rich.SetData(DataFormats.Html,"<a href=\"https://source.example\"><b>RICH SOURCE</b></a>",false);
        Clipboard.SetDataObject(rich,true);

        var run=PreparePlainPasteDocument("Before replace after");
        Editor.Selection.Select(run.ContentStart.GetPositionAtOffset(7)!,run.ContentStart.GetPositionAtOffset(14)!);
        var before=PlainPasteDocumentText();
        // Exercise the actual built-in chord, including its precedence over
        // user-defined macro shortcuts. Other cases call the same command.
        SendKeys(new[]{KeyInput(0x11),KeyInput(0x10),KeyInput(0x56),KeyInput(0x56,true),KeyInput(0x10,true),KeyInput(0x11,true)});
        if(PlainPasteDocumentText()!="Before Pasted plain\nsecond line after\n")
            throw new InvalidOperationException("Ctrl+Shift+V did not replace the selection with clipboard plain text and preserved line breaks.");
        AssertPlainPasteDestinationFormat();
        if(EnumeratePlainPasteLinks(Editor.Document).Any() || EnumerateImageContainers(Editor.Document).Any())
            throw new InvalidOperationException("Plain paste imported rich clipboard objects or hyperlinks.");
        if(Clipboard.GetText(TextDataFormat.UnicodeText)!=plain || Clipboard.GetData(DataFormats.Rtf) as string!=rtf)
            throw new InvalidOperationException("Plain paste changed the system clipboard contents.");
        Editor.Undo();
        if(PlainPasteDocumentText()!=before)
            throw new InvalidOperationException("Plain paste and selection replacement were not one undo operation.");

        // Ordinary paste must retain its existing rich-content route.
        run=PreparePlainPasteDocument("");
        Editor.CaretPosition=run.ContentStart;
        Editor.Paste();
        if(!PlainPasteDocumentText().Contains("RICH SOURCE",StringComparison.Ordinal))
            throw new InvalidOperationException("Ordinary paste no longer selected the rich clipboard payload.");

        run=PreparePlainPasteDocument("Keep selected text");
        Editor.Selection.Select(run.ContentStart,run.ContentEnd);
        before=PlainPasteDocumentText();
        var pixels=new byte[]{20,80,160,255};
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,pixels,4);
        Clipboard.SetImage(bitmap);
        if(PasteUnformattedText() || PlainPasteDocumentText()!=before || Editor.Selection.IsEmpty)
            throw new InvalidOperationException("An image-only clipboard removed the selection or inserted rich content.");
        Clipboard.Clear();
        if(PasteUnformattedText() || PlainPasteDocumentText()!=before || Editor.Selection.IsEmpty)
            throw new InvalidOperationException("An empty clipboard removed the selection.");

        Clipboard.SetText(plain,TextDataFormat.UnicodeText);
        Editor.IsReadOnly=true;
        if(PasteUnformattedText() || PlainPasteDocumentText()!=before)
            throw new InvalidOperationException("Plain paste changed a read-only document.");
        Editor.IsReadOnly=false;

        // Pasting into an existing hyperlink must preserve line breaks and
        // retain the untouched destination link text on each side.
        var linkedRun=new Run("linked words");
        var link=new Hyperlink(new Span(new Bold(linkedRun))){NavigateUri=new Uri("https://destination.example")};
        var paragraph=new Paragraph();
        paragraph.Inlines.Add(new Run("Before "));
        paragraph.Inlines.Add(link);
        paragraph.Inlines.Add(new Run(" after"));
        Editor.Document=new FlowDocument(paragraph);
        Editor.CaretPosition=linkedRun.ContentStart.GetPositionAtOffset(6)!;
        Editor.Focus();
        PumpInput();
        before=PlainPasteDocumentText();
        if(!PasteUnformattedText() || PlainPasteDocumentText()!="Before linkedPasted plain\nsecond line words after\n")
            throw new InvalidOperationException("Plain paste lost multiline text at a hyperlink boundary.");
        var links=EnumeratePlainPasteLinks(Editor.Document).ToArray();
        if(links.Length!=2 || links.Any(item=>item.NavigateUri?.Host!="destination.example") ||
           links.Any(item=>new TextRange(item.ContentStart,item.ContentEnd).Text.Contains("Pasted",StringComparison.Ordinal)) ||
           links.Any(item=>!Equals(new TextRange(item.ContentStart,item.ContentEnd).GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold)))
        {
            var details=string.Join(" | ",links.Select(item=>$"URI={item.NavigateUri}; Text=[{new TextRange(item.ContentStart,item.ContentEnd).Text}]; Weight={new TextRange(item.ContentStart,item.ContentEnd).GetPropertyValue(TextElement.FontWeightProperty)}"));
            using var markup=new MemoryStream();
            new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(markup,DataFormats.Xaml);
            throw new InvalidOperationException($"Plain paste imported link behaviour or changed the untouched destination links. Count={links.Length}; {details}. Fixture XAML: {System.Text.Encoding.UTF8.GetString(markup.ToArray())}");
        }
        Editor.Undo();
        if(PlainPasteDocumentText()!=before || EnumeratePlainPasteLinks(Editor.Document).Count()!=1)
            throw new InvalidOperationException("Hyperlink-boundary paste could not be undone as one operation.");
    }

    Run PreparePlainPasteDocument(string text)
    {
        var run=new Run(text)
        {
            FontFamily=new FontFamily("Arial"),FontSize=14,FontWeight=FontWeights.Normal,
            FontStyle=FontStyles.Normal,Foreground=Brushes.DarkSlateGray,TextDecorations=null
        };
        var paragraph=new Paragraph(run)
        {
            FontFamily=run.FontFamily,FontSize=run.FontSize,FontWeight=run.FontWeight,
            FontStyle=run.FontStyle,Foreground=run.Foreground,Tag=ParagraphStyleTagPrefix+"Normal"
        };
        Editor.Document=new FlowDocument(paragraph);
        Editor.CaretPosition=run.ContentStart;
        Editor.Focus();
        PumpInput();
        return run;
    }

    string PlainPasteDocumentText()
        =>new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text.Replace("\r\n","\n");

    void AssertPlainPasteDestinationFormat()
    {
        foreach(var paragraph in EnumerateDocumentParagraphs())
        {
            var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
            if(!Equals(range.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Normal) ||
               !Equals(range.GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Normal) ||
               !Equals(range.GetPropertyValue(TextElement.FontSizeProperty),14.0) ||
               (range.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source!="Arial" ||
               (range.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color!=Colors.DarkSlateGray ||
               (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations && decorations.Count>0))
                throw new InvalidOperationException("Plain paste retained source formatting or lost the destination typing format.");
        }
    }

    static IEnumerable<Hyperlink> EnumeratePlainPasteLinks(FlowDocument document)
    {
        static IEnumerable<Hyperlink> Inlines(InlineCollection items)
        {
            foreach(var inline in items)
            {
                if(inline is Hyperlink link)yield return link;
                if(inline is Span span)
                    foreach(var nested in Inlines(span.Inlines))yield return nested;
            }
        }
        foreach(var paragraph in document.Blocks.OfType<Paragraph>())
            foreach(var link in Inlines(paragraph.Inlines))yield return link;
    }
}
