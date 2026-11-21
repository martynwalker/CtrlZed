using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    internal static int RunPagedEditingSelfTest()
    {
        MainWindow? window=null;
        var report=new StringBuilder();
        try
        {
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Maximized};
            window.Show();window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.CheckPagedEditing(report);
            report.AppendLine("PASS: editable pages, print pagination, physical input, Undo, document fidelity and picture anchors.");
            return 0;
        }
        catch(Exception error)
        {
            report.AppendLine(error.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),error.ToString());
            return 124;
        }
        finally
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed46PagedEditing.txt"),report.ToString());
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();window.grammarAiCancellation?.Cancel();
                window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.Close();
            }
        }
    }

    void CheckPagedEditing(StringBuilder report)
    {
        recoveryTimer.Stop();
        languageSettings.GrammarAndStyleSuggestions=false;
        languageSettings.PoetryRhymeSuggestions=false;
        grammarReviewTimer?.Stop();
        SpellCheck.SetIsEnabled(Editor,false);
        profile=KeyboardProfile.Word;
        NavigationPane.Visibility=Visibility.Collapsed;
        InitializeBeta46PagedEditing();
        ApplyBeta35ViewMode(EditorViewMode.Paged,false);
        pageWidth=794;pageHeight=1123;pageMargins=new Thickness(72);
        zoomPercent=100;ApplyZoom();
        ClearFloatingImagePlacements();

        Paragraph Body(string text,bool newPage=false)=>new(new Run(text))
        {
            FontFamily=new FontFamily("Calibri"),FontSize=16,
            Margin=new Thickness(0),LineHeight=20,
            LineStackingStrategy=LineStackingStrategy.BlockLineHeight,
            BreakPageBefore=newPage,Tag=ParagraphStyleTagPrefix+"Normal"
        };
        var first=Body("FIRST PAGE");
        var firstBody=Body("First page body");
        var second=Body("SECOND PAGE",true);
        second.KeepWithNext=true;
        second.Inlines.Clear();
        second.Inlines.Add(new Span(new Bold(new Run("SECOND PAGE"))));
        var anchorParagraph=Body("Picture anchor");
        var third=Body("THIRD PAGE",true);
        var pixels=new byte[8*8*4];
        for(var index=0;index<pixels.Length;index+=4)
        {
            pixels[index]=90;pixels[index+1]=40;pixels[index+2]=180;pixels[index+3]=255;
        }
        var bitmap=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,pixels,32);
        bitmap.Freeze();
        var inlineImage=new Image{Source=bitmap,Width=24,Height=18};
        var floatingImage=new Image{Source=bitmap,Width=40,Height=20};
        first.Inlines.Add(new Run(" "));
        first.Inlines.Add(new InlineUIContainer(inlineImage));
        anchorParagraph.Inlines.Add(new InlineUIContainer(floatingImage));
        var document=new FlowDocument();
        foreach(var paragraph in new[]{first,firstBody,second,anchorParagraph,third})document.Blocks.Add(paragraph);
        Editor.Document=document;ApplyPage();
        Editor.CaretPosition=first.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        Editor.Focus();PumpInput();
        FreshPagedLayout();
        SetPicturePlacement(floatingImage,PicturePlacementMode.InFrontOfText);
        RefreshFloatingImagePlacements();FreshPagedLayout();
        VerifyPagedGeometry("initial fixture");
        RequirePaged(Beta46DisplayedPageCount==3,"The three forced pages did not produce three editable pages.");
        var mappedSecond=document.ContentStart.GetPositionAtOffset(beta35PaginationMap!.PageStartOffsets[1],LogicalDirection.Forward)
            ?.GetInsertionPosition(LogicalDirection.Forward);
        RequirePaged(mappedSecond?.Paragraph==second,"The page map did not resolve a nested-span page start back to the canonical paragraph.");
        var imagesBefore=CaptureFloatingImagePlacements();
        RequirePaged(imagesBefore.Count==1,"The floating-picture fixture was not registered.");
        report.AppendLine($"Fixture: {Beta46DisplayedPageCount} pages, inline and floating pictures, exact line height and explicit page breaks.");

        // A view change must neither rewrite the body nor replace the public
        // RichTextBox/FlowDocument macro target or its existing Undo history.
        Editor.CaretPosition=firstBody.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();
        var originalText=PagedDocumentText();
        TypeInput(" Undo sentinel");
        var editedText=PagedDocumentText();
        RequirePaged(Editor.CanUndo,"Typing did not establish the Undo-history fixture.");
        FreshPagedLayout();
        var fingerprint=PagedDocumentFingerprint();
        dirty=false;
        ApplyBeta35ViewMode(EditorViewMode.Continuous,false);
        RefreshBeta46PagedEditingLayout();PumpInput();
        ApplyBeta35ViewMode(EditorViewMode.Paged,false);FreshPagedLayout();
        RequirePaged(ReferenceEquals(document,Editor.Document),"Changing view replaced the canonical macro document.");
        RequirePaged(!dirty,"Changing view marked the document dirty.");
        RequirePaged(PagedDocumentFingerprint()==fingerprint,"Changing view or repaginating changed document formatting, styles or picture settings.");
        RequirePaged(Editor.CanUndo,"Changing view cleared the existing Undo history.");
        Editor.Undo();
        RequirePaged(PagedDocumentText()==originalText,"View changes added an Undo operation or changed the original typing undo.");
        Editor.Redo();
        RequirePaged(PagedDocumentText()==editedText,"Redo stopped restoring the typed text after a view change.");
        Editor.Undo();FreshPagedLayout();
        report.AppendLine("Paged/continuous switching preserved the canonical document, source/settings fingerprint, dirty state and Undo/Redo.");

        // Scroll to page two without moving the caret there first. Clicking a
        // known page margin/first-line location must itself find page-two text.
        first=FindPagedParagraph("FIRST PAGE");
        second=FindPagedParagraph("SECOND PAGE");
        Editor.CaretPosition=first.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        Editor.Focus();PumpInput();
        var page=Beta46PageBounds(1);
        var scroll=FindVisualAncestor<ScrollViewer>(PagedEditor)
            ??throw new InvalidOperationException("The paged surface has no outer scroll viewer.");
        var top=PagedEditor.TranslatePoint(new Point(page.Left,page.Top),scroll);
        scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+top.Y-16));
        UpdateLayout();PumpInput();
        var click=new Point(page.Left+pageMargins.Left+3,page.Top+pageMargins.Top+8);
        var screen=PagedEditor.PointToScreen(click);
        if(!SetPagedCursorPosition((int)Math.Round(screen.X),(int)Math.Round(screen.Y)))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not position the page-click fixture.");
        SendKeys(new[]
        {
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}
        });
        TypeInput("X");
        RequirePaged(new TextRange(second.ContentStart,second.ContentEnd).Text.TrimEnd('\r','\n')=="XSECOND PAGE",
            "A physical click on page two typed into the wrong page or insertion point. "+PagedDocumentText());
        RequirePaged(new TextRange(first.ContentStart,first.ContentEnd).Text.StartsWith("FIRST PAGE",StringComparison.Ordinal),
            "Clicking page two changed page one.");
        Editor.Undo();FreshPagedLayout();
        RequirePaged(PagedDocumentText()==originalText,"Undo did not reverse physical page-two typing as one edit.");
        report.AppendLine("Physical page-two click and typing reached the correct canonical paragraph and remained undoable.");

        // Use keyboard selection over the page break: it remains one native
        // selection even when the selected text lies on two different papers.
        firstBody=FindPagedParagraph("First page body");
        Editor.CaretPosition=firstBody.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();PagedEditor.ActivatePageForCaret();PagedEditor.ScrollCaretIntoView();PumpInput();
        SendKeys(new[]{KeyInput(0x10),KeyInput(0x27),KeyInput(0x27,true),KeyInput(0x27),KeyInput(0x27,true),KeyInput(0x10,true)});
        RequirePaged(Editor.Selection.Text.Contains('\n') && Editor.Selection.Text.Contains('S'),
            "Physical Shift+Right did not select across the page boundary: ["+Editor.Selection.Text+"].");
        var beforeDelete=PagedDocumentText();
        SendKeys(new[]{KeyInput(0x2E),KeyInput(0x2E,true)});
        var deleted=PagedDocumentText();
        RequirePaged(deleted!=beforeDelete,"Deleting a cross-page selection did not edit the source.");
        FreshPagedLayout();VerifyPagedGeometry("after cross-page deletion");
        Editor.Undo();FreshPagedLayout();
        RequirePaged(PagedDocumentText()==beforeDelete,"Cross-page Delete could not be undone exactly.");
        Editor.Redo();
        RequirePaged(PagedDocumentText()==deleted,"Cross-page Delete could not be redone exactly.");
        Editor.Undo();FreshPagedLayout();
        report.AppendLine("One native selection crossed a page break; Delete, repagination and Undo/Redo preserved exact source text.");

        // Safe macro paragraph insertion must still address the canonical
        // RichTextBox while the final page is being edited.
        third=FindPagedParagraph("THIRD PAGE");
        Editor.CaretPosition=third.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();PagedEditor.ActivatePageForCaret();
        var beforeMacro=PagedDocumentText();
        AwaitPagedTask(RunSafeMacroAsync("NEW_PARAGRAPH"));
        TypeInput("Macro body");
        RequirePaged(PagedDocumentText().Contains("THIRD PAGE\r\nMacro body",StringComparison.Ordinal),
            "The safe macro did not insert into the final page's canonical document.");
        for(var undo=0;undo<3 && PagedDocumentText()!=beforeMacro && Editor.CanUndo;undo++)Editor.Undo();
        RequirePaged(PagedDocumentText()==beforeMacro,"Macro insertion and typing could not be undone after page activation.");
        FreshPagedLayout();

        // Adding content before an anchored picture changes its page number,
        // while the stored dimensions and offsets remain model coordinates.
        second=FindPagedParagraph("SECOND PAGE");
        var pageBefore=CreateFloatingPageImages(new Size(pageWidth,pageHeight)).Single().PageNumber;
        Editor.BeginChange();
        try
        {
            for(var index=0;index<55;index++)document.Blocks.InsertBefore(second,Body("Added reflow paragraph"));
        }
        finally { Editor.EndChange(); }
        FreshPagedLayout();VerifyPagedGeometry("after inserted content");
        var pageAfter=CreateFloatingPageImages(new Size(pageWidth,pageHeight)).Single().PageNumber;
        var imagesAfter=CaptureFloatingImagePlacements();
        RequirePaged(pageAfter>pageBefore,"The anchored picture did not follow its paragraph onto a later page.");
        RequirePaged(imagesAfter.Count==1 && imagesAfter[0].Width==imagesBefore[0].Width &&
            imagesAfter[0].Height==imagesBefore[0].Height && imagesAfter[0].OffsetX==imagesBefore[0].OffsetX &&
            imagesAfter[0].OffsetY==imagesBefore[0].OffsetY && inlineImage.Width==24 && inlineImage.Height==18,
            "Repagination changed picture dimensions or stored anchor offsets.");
        RequirePaged(ReferenceEquals(document,Editor.Document),"Editing replaced the macro's canonical document.");
        report.AppendLine($"Inserted content moved the floating picture from page {pageBefore+1} to {pageAfter+1} without changing stored picture sizes/offsets; macro insertion retained its target.");

        // Width and margin changes must invalidate the displayed papers as
        // well as the preview paginator; zoom changes visual scale only.
        pageWidth=816;pageHeight=1056;pageMargins=new Thickness(60,54,66,54);
        ApplyPage();FreshPagedLayout();VerifyPagedGeometry("changed paper and margins");
        var beforeZoom=PagedDocumentFingerprint();
        zoomPercent=75;ApplyZoom();RefreshBeta46PagedEditingLayout();PumpInput();
        RequirePaged(PagedDocumentFingerprint()==beforeZoom,"Zoom changed the document or picture model.");
        VerifyPagedGeometry("zoomed paper");
        report.AppendLine("Paper size, margins and zoom preserved print-page agreement and document content.");
    }

    void FreshPagedLayout()
    {
        AwaitPagedTask(RebuildBeta46PaginationForTestAsync());
        var watch=Stopwatch.StartNew();
        while(!Beta46PaginationReady)
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(12))throw new InvalidOperationException("Editable pagination did not settle.");
            PumpInput();
        }
        RefreshBeta46PagedEditingLayout();UpdateLayout();PumpInput();
    }

    static void AwaitPagedTask(Task task)
    {
        var watch=Stopwatch.StartNew();
        while(!task.IsCompleted)
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Paged-editing operation timed out.");
            PumpInput();
        }
        task.GetAwaiter().GetResult();
    }

    void VerifyPagedGeometry(string context)
    {
        var preview=CreatePreviewPaginator();
        preview.ComputePageCount();
        RequirePaged(PagedEditor.IsPaged && Beta46DisplayedPageCount==preview.PageCount,
            $"Editable/preview page counts disagree {context}: {Beta46DisplayedPageCount}/{preview.PageCount}.");
        Rect previous=Rect.Empty;
        for(var index=0;index<Beta46DisplayedPageCount;index++)
        {
            var bounds=Beta46PageBounds(index);
            RequirePaged(!bounds.IsEmpty && Math.Abs(bounds.Width-pageWidth)<0.1 && Math.Abs(bounds.Height-pageHeight)<0.1,
                "Editable paper dimensions disagree with Page Setup "+context+".");
            RequirePaged(index==0 || bounds.Top>=previous.Bottom+8,
                "Editable pages overlap or lack a physical page gap "+context+".");
            previous=bounds;
        }
    }

    string PagedDocumentText()=>new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text;

    string PagedDocumentFingerprint()
    {
        using var stream=new MemoryStream();
        new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(stream,DataFormats.Rtf);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))+"|"+JsonSerializer.Serialize(CaptureDocumentSettings());
    }

    Paragraph FindPagedParagraph(string text)=>EnumerateDocumentParagraphs().First(paragraph=>
        new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text.StartsWith(text,StringComparison.Ordinal));

    static void RequirePaged(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll",EntryPoint="SetCursorPos",SetLastError=true)]
    static extern bool SetPagedCursorPosition(int x,int y);
}
