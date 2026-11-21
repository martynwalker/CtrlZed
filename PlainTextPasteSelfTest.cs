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
        const string rtf="{\\rtf1\\ansi{\\fonttbl{\\f0 Times New Roman;}}{\\colortbl;\\red255\\green0\\blue0;}\\f0\\fs48\\b\\i\\ul\\cf1 RICH SOURCE\\ul0\\i0\\b0}";
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

        // Exercise the physical ordinary-paste chord, not just Editor.Paste:
        // selecting the rich payload is insufficient if its formatting is lost.
        run=PreparePlainPasteDocument("");
        Editor.CaretPosition=run.ContentStart;
        PressPasteKeyChord();
        AssertRichPasteFormat("RICH SOURCE","Times New Roman",32,Colors.Red,"RTF Ctrl+V");

        // A real WPF copy advertises its native rich formats. Ordinary paste
        // must retain those formats as well as the external RTF route.
        var copiedRun=new Run("Copied rich text")
        {
            FontFamily=new FontFamily("Courier New"),FontSize=20,
            FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,
            Foreground=Brushes.DarkRed,TextDecorations=TextDecorations.Underline
        };
        Editor.Document=new FlowDocument(new Paragraph(copiedRun));
        Editor.Selection.Select(copiedRun.ContentStart,copiedRun.ContentEnd);
        Editor.Copy();
        PreparePlainPasteDocument("");
        PressPasteKeyChord();
        AssertRichPasteFormat("Copied rich text","Courier New",20,Colors.DarkRed,"WPF copy/Ctrl+V");

        // Browsers commonly supply HTML plus plain text without RTF or WPF
        // formats. Use an actual byte-offset CF_HTML header, not a rich-text
        // surrogate, so this is checked through the normal paste command.
        const string browserText="BROWSER SOURCE";
        var browserHtml=CreatePlainPasteCfHtml("<p><a href=\"https://source.example/article\"><span style=\"font-family: Georgia; font-size: 18pt; color: #1E4D7B; font-weight: bold; font-style: italic; text-decoration: underline\">BROWSER SOURCE</span></a></p>");
        var browser=new DataObject();
        browser.SetData(DataFormats.UnicodeText,browserText,false);
        browser.SetData(DataFormats.Html,browserHtml,false);
        Clipboard.SetDataObject(browser,true);
        PreparePlainPasteDocument("");
        PressPasteKeyChord();
        AssertRichPasteFormat(browserText,"Georgia",24,Color.FromRgb(0x1E,0x4D,0x7B),"browser HTML Ctrl+V");
        var browserLinks=EnumeratePlainPasteLinks(Editor.Document).ToArray();
        if(browserLinks.Length!=1 || browserLinks[0].NavigateUri?.AbsoluteUri!="https://source.example/article" ||
           new TextRange(browserLinks[0].ContentStart,browserLinks[0].ContentEnd).Text!=browserText)
            throw new InvalidOperationException("Ordinary browser paste lost the source hyperlink.");
        AssertBrowserClipboardUnchanged(browserHtml,browserText);

        PreparePlainPasteDocument("");
        PressPasteKeyChord(unformatted:true);
        if(PlainPasteDocumentText()!=browserText+"\n" || EnumeratePlainPasteLinks(Editor.Document).Any())
            throw new InvalidOperationException("Ctrl+Shift+V imported browser HTML or hyperlink behaviour.");
        AssertPlainPasteDestinationFormat();
        AssertBrowserClipboardUnchanged(browserHtml,browserText);
        CheckBrowserHtmlEditing(browserHtml,browserText);

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
        var linkTexts=links.Select(item=>new TextRange(item.ContentStart,item.ContentEnd).Text).ToArray();
        // The whole Hyperlink range includes structural Span boundaries and
        // may aggregate FontWeight to UnsetValue. Verify the actual nonempty
        // text runs, whose effective weight determines the visible text.
        var linkRuns=links.Select(item=>EnumeratePlainPasteRuns(item.Inlines)
            .Where(run=>run.Text.Length>0).ToArray()).ToArray();
        if(links.Length!=2 || links.Any(item=>item.NavigateUri?.Host!="destination.example") ||
           !linkTexts.SequenceEqual(new[]{"linked"," words"}) ||
           linkRuns.Any(runs=>runs.Length==0 || runs.Any(run=>run.FontWeight!=FontWeights.Bold)))
        {
            var details=string.Join(" | ",links.Select(item=>$"URI={item.NavigateUri}; Text=[{new TextRange(item.ContentStart,item.ContentEnd).Text}]; Runs={string.Join(", ",EnumeratePlainPasteRuns(item.Inlines).Select(run=>$"[{run.Text}]/{run.FontWeight}"))}"));
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

    void CheckBrowserHtmlEditing(string browserHtml,string browserText)
    {
        // Retain the normal rich paste selection-replacement transaction: the
        // surrounding text survives and Undo/Redo applies to the complete paste.
        var original=PreparePlainPasteDocument("Before replace after");
        Editor.Selection.Select(original.ContentStart.GetPositionAtOffset(7)!,original.ContentStart.GetPositionAtOffset(14)!);
        var before=PlainPasteDocumentText();
        PressPasteKeyChord();
        AssertBrowserReplacement();
        Editor.Undo();
        if(PlainPasteDocumentText()!=before || EnumeratePlainPasteLinks(Editor.Document).Any())
            throw new InvalidOperationException("Browser HTML selection replacement was not one Undo operation.");
        Editor.Redo();
        AssertBrowserReplacement();
        AssertBrowserClipboardUnchanged(browserHtml,browserText);

        void AssertBrowserReplacement()
        {
            var links=EnumeratePlainPasteLinks(Editor.Document).ToArray();
            var runs=links.SelectMany(link=>EnumeratePlainPasteRuns(link.Inlines)).Where(run=>run.Text.Length>0).ToArray();
            if(PlainPasteDocumentText().Replace("\n","")!="Before "+browserText+" after" ||
               links.Length!=1 || links[0].NavigateUri?.AbsoluteUri!="https://source.example/article" ||
               new TextRange(links[0].ContentStart,links[0].ContentEnd).Text!=browserText || runs.Length==0 ||
               runs.Any(run=>!HasPlainPasteRichFormat(run,"Georgia",24,Color.FromRgb(0x1E,0x4D,0x7B))))
                throw new InvalidOperationException("Browser HTML replacement or Redo lost source formatting, its link, or surrounding text.");
        }

        // The byte offsets must be measured in UTF-8, and a copied fragment
        // still inherits formatting from ancestors outside StartFragment.
        const string selected="Été — café 😀";
        var unicodeHtml=CreatePlainPasteCfHtml("<span>"+selected+"</span>",
            "<div style=\"font-family: Georgia; font-size: 18pt; color: #1E4D7B; font-weight: bold; font-style: italic; text-decoration: underline\">Hors sélection 🚤 ",
            " Après sélection é</div>");
        SetBrowserClipboard(unicodeHtml,selected);
        PreparePlainPasteDocument("");
        PressPasteKeyChord();
        AssertRichPasteFormat(selected,"Georgia",24,Color.FromRgb(0x1E,0x4D,0x7B),"UTF-8 HTML fragment and inherited formatting");
        AssertBrowserClipboardUnchanged(unicodeHtml,selected);

        // Valid CSS can contain layout values that WPF does not accept.
        // Ignore/bound those values without discarding supported rich text.
        const string boundedText="Bounded layout";
        var boundedHtml=CreatePlainPasteCfHtml("<p style=\"margin-top: -2px; margin-bottom: -5pt; line-height: 0.00001px; font-family: Georgia; font-size: 18pt; color: #1E4D7B; font-weight: bold; font-style: italic; text-decoration: underline\">Bounded layout</p>");
        SetBrowserClipboard(boundedHtml,boundedText);
        PreparePlainPasteDocument("");
        PressPasteKeyChord();
        AssertRichPasteFormat(boundedText,"Georgia",24,Color.FromRgb(0x1E,0x4D,0x7B),"HTML with unsupported layout values");
        foreach(var paragraph in EnumerateDocumentParagraphs())
        {
            var margin=paragraph.Margin;
            var sides=new[]{margin.Left,margin.Top,margin.Right,margin.Bottom};
            if(sides.Any(value=>!double.IsNaN(value) && (!double.IsFinite(value) || value<0)) ||
               (!double.IsNaN(paragraph.LineHeight) && (!double.IsFinite(paragraph.LineHeight) || paragraph.LineHeight<=0)))
                throw new InvalidOperationException("Browser HTML retained an invalid paragraph margin or zero line height.");
        }
        AssertBrowserClipboardUnchanged(boundedHtml,boundedText);

        // Heading/paragraph boundaries and a soft break have different
        // semantics. Keep them distinct while retaining HTML emphasis tags.
        const string structured="<h2>Selected heading</h2><p><strong>Bold words</strong><br><em>Italic words</em></p>";
        const string structuredText="Selected heading\r\nBold words\r\nItalic words";
        var structuredHtml=CreatePlainPasteCfHtml(structured);
        SetBrowserClipboard(structuredHtml,structuredText);
        PreparePlainPasteDocument("");
        PressPasteKeyChord();
        var paragraphs=EnumerateDocumentParagraphs().Where(paragraph=>
            !string.IsNullOrWhiteSpace(new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text)).ToArray();
        if(paragraphs.Length!=2 ||
           new TextRange(paragraphs[0].ContentStart,paragraphs[0].ContentEnd).Text.TrimEnd('\r','\n')!="Selected heading" ||
           new TextRange(paragraphs[1].ContentStart,paragraphs[1].ContentEnd).Text.Replace("\r\n","\n").TrimEnd('\n')!="Bold words\nItalic words")
            throw new InvalidOperationException("Browser HTML lost heading/paragraph boundaries or the br line break. Text=["+PlainPasteDocumentText()+"].");
        var headingRuns=EnumeratePlainPasteRuns(paragraphs[0].Inlines).Where(run=>run.Text.Length>0).ToArray();
        var bodyRuns=EnumeratePlainPasteRuns(paragraphs[1].Inlines).Where(run=>run.Text.Length>0).ToArray();
        if(headingRuns.Length==0 || bodyRuns.Length==0 || headingRuns.Any(run=>run.FontWeight!=FontWeights.Bold) ||
           headingRuns.Min(run=>run.FontSize)<=bodyRuns.Max(run=>run.FontSize) ||
           !bodyRuns.Any(run=>run.Text=="Bold words" && run.FontWeight==FontWeights.Bold && run.FontStyle==FontStyles.Normal) ||
           !bodyRuns.Any(run=>run.Text=="Italic words" && run.FontStyle==FontStyles.Italic && run.FontWeight==FontWeights.Normal))
            throw new InvalidOperationException("Browser HTML lost heading emphasis/size or leaked strong/em formatting between text spans.");
        AssertBrowserClipboardUnchanged(structuredHtml,structuredText);
    }

    static void SetBrowserClipboard(string html,string text)
    {
        var data=new DataObject();
        data.SetData(DataFormats.Html,html,false);
        data.SetData(DataFormats.UnicodeText,text,false);
        Clipboard.SetDataObject(data,true);
    }

    static void PressPasteKeyChord(bool unformatted=false)
    {
        if(unformatted)
            SendKeys(new[]{KeyInput(0x11),KeyInput(0x10),KeyInput(0x56),KeyInput(0x56,true),KeyInput(0x10,true),KeyInput(0x11,true)});
        else
            SendKeys(new[]{KeyInput(0x11),KeyInput(0x56),KeyInput(0x56,true),KeyInput(0x11,true)});
    }

    void AssertRichPasteFormat(string text,string font,double size,Color colour,string context)
    {
        var runs=EnumerateDocumentParagraphs().SelectMany(paragraph=>EnumeratePlainPasteRuns(paragraph.Inlines))
            .Where(run=>run.Text.Length>0).ToArray();
        if(PlainPasteDocumentText().TrimEnd('\n')!=text || runs.Length==0 ||
           runs.Any(run=>!HasPlainPasteRichFormat(run,font,size,colour)))
        {
            var details=string.Join(" | ",runs.Select(run=>$"[{run.Text}] Font={run.FontFamily.Source}; Size={run.FontSize}; Weight={run.FontWeight}; Style={run.FontStyle}; Colour={run.Foreground}; Underline={HasPlainPasteUnderline(run)}"));
            throw new InvalidOperationException($"{context} lost source text or formatting. Text=[{PlainPasteDocumentText()}]. Runs: {details}");
        }
    }

    static bool HasPlainPasteRichFormat(Run run,string font,double size,Color colour)
        =>run.FontWeight==FontWeights.Bold && run.FontStyle==FontStyles.Italic &&
            Math.Abs(run.FontSize-size)<=0.01 && string.Equals(run.FontFamily.Source,font,StringComparison.OrdinalIgnoreCase) &&
            (run.Foreground as SolidColorBrush)?.Color==colour && HasPlainPasteUnderline(run);

    static bool HasPlainPasteUnderline(Run run)
    {
        // Underlining may be declared on a containing Span or Hyperlink; its
        // presence on the visible run alone is not a reliable rendering check.
        for(TextElement? element=run;element!=null;element=element.Parent as TextElement)
            if(element is Inline inline && inline.TextDecorations?.Any(decoration=>decoration.Location==TextDecorationLocation.Underline)==true)
                return true;
        return false;
    }

    static void AssertBrowserClipboardUnchanged(string html,string text)
    {
        if(Clipboard.GetData(DataFormats.Html) as string!=html || Clipboard.GetText(TextDataFormat.UnicodeText)!=text)
            throw new InvalidOperationException("Pasting browser HTML changed the global clipboard payload.");
    }

    static string CreatePlainPasteCfHtml(string fragment,string beforeFragment="",string afterFragment="")
    {
        var prefix="<html><head><meta charset=\"utf-8\"></head><body>"+beforeFragment+"<!--StartFragment-->";
        var suffix="<!--EndFragment-->"+afterFragment+"</body></html>";
        const string template="Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var headerLength=System.Text.Encoding.UTF8.GetByteCount(string.Format(System.Globalization.CultureInfo.InvariantCulture,template,0,0,0,0));
        var startFragment=headerLength+System.Text.Encoding.UTF8.GetByteCount(prefix);
        var endFragment=startFragment+System.Text.Encoding.UTF8.GetByteCount(fragment);
        var endHtml=endFragment+System.Text.Encoding.UTF8.GetByteCount(suffix);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,template,headerLength,endHtml,startFragment,endFragment)+prefix+fragment+suffix;
    }

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

    static IEnumerable<Run> EnumeratePlainPasteRuns(InlineCollection items)
    {
        foreach(var inline in items)
        {
            if(inline is Run run)yield return run;
            else if(inline is Span span)
                foreach(var nested in EnumeratePlainPasteRuns(span.Inlines))yield return nested;
        }
    }
}
