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
        var pixels=new byte[8*12*4];
        for(var index=0;index<pixels.Length;index+=4)
        {
            pixels[index]=90;pixels[index+1]=40;pixels[index+2]=180;pixels[index+3]=255;
        }
        var bitmap=BitmapSource.Create(8,12,96,96,PixelFormats.Bgra32,null,pixels,32);
        bitmap.Freeze();
        var inlineImage=new Image{Source=bitmap,Width=24,Height=18};
        var floatingImage=new Image{Source=bitmap,Width=80,Height=120};
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
        // Keep the portrait picture inside the page body, extending beyond
        // the short text segment before the next explicit page break.
        floatingImagePlacements.Single().OffsetX=20;
        floatingImagePlacements.Single().OffsetY=8;
        RefreshFloatingImagePlacements();FreshPagedLayout();
        VerifyPagedGeometry("initial fixture");
        RequirePaged(Beta46DisplayedPageCount==3,"The three forced pages did not produce three editable pages.");
        var mappedSecond=document.ContentStart.GetPositionAtOffset(beta35PaginationMap!.PageStartOffsets[1],LogicalDirection.Forward)
            ?.GetInsertionPosition(LogicalDirection.Forward);
        RequirePaged(mappedSecond?.Paragraph==second,"The page map did not resolve a nested-span page start back to the canonical paragraph.");
        var imagesBefore=CaptureFloatingImagePlacements();
        RequirePaged(imagesBefore.Count==1,"The floating-picture fixture was not registered.");
        report.AppendLine($"Fixture: {Beta46DisplayedPageCount} pages, inline and floating pictures, exact line height and explicit page breaks.");
        VerifyFloatingPageOffset(floatingImage,report);
        SavePagedLayoutDebugImage(report);
        VerifyTallFloatingPicturePixels(floatingImage,report);

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
        // No explicit scrolling after the keys: keyboard movement itself must
        // bring the projected caret on the next sheet into the outer viewport.
        var caretRect=PagedEditor.GetProjectedCharacterRect(Editor.CaretPosition);
        var caretInViewport=caretRect.IsEmpty?new Point(double.NaN,double.NaN):
            PagedEditor.TranslatePoint(caretRect.TopLeft,scroll);
        RequirePaged(!caretRect.IsEmpty && double.IsFinite(caretInViewport.Y) &&
            caretInViewport.Y>=-2 && caretInViewport.Y+caretRect.Height<=scroll.ViewportHeight+2 &&
            caretInViewport.X>=-2 && caretInViewport.X<=scroll.ViewportWidth+2,
            $"Physical cross-page keyboard movement left the caret outside the viewport: caret={caretRect}, viewportPoint={caretInViewport}, viewport={scroll.ViewportWidth}x{scroll.ViewportHeight}.");
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

        VerifyPagedBrowsingAndNavigation(floatingImage,report);

        // Width and margin changes must invalidate the displayed papers as
        // well as the preview paginator; zoom changes visual scale only.
        pageWidth=816;pageHeight=1056;pageMargins=new Thickness(60,54,66,54);
        ApplyPage();FreshPagedLayout();VerifyPagedGeometry("changed paper and margins");
        var beforeZoom=PagedDocumentFingerprint();
        zoomPercent=75;ApplyZoom();RefreshBeta46PagedEditingLayout();PumpInput();
        RequirePaged(PagedDocumentFingerprint()==beforeZoom,"Zoom changed the document or picture model.");
        VerifyPagedGeometry("zoomed paper");
        report.AppendLine("Paper size, margins and zoom preserved print-page agreement and document content.");

        VerifyReturnAfterThumbnailNavigation(report);
    }

    void VerifyReturnAfterThumbnailNavigation(StringBuilder report)
    {
        var pictureSource=floatingImagePlacements.FirstOrDefault()?.RuntimeImage?.Source;
        var failures=new List<string>();
        foreach(var forcedBreaks in new[]{false,true})
        {
            var name=forcedBreaks?"explicit page breaks":"automatic page boundaries";
            ClearFloatingImagePlacements();
            NavigationPane.Visibility=Visibility.Visible;
            NavigationTabs.SelectedItem=NavigationPagesTab;
            pageWidth=794;pageHeight=1123;pageMargins=new Thickness(72);
            zoomPercent=100;ApplyZoom();
            var document=new FlowDocument();
            var rowCount=forcedBreaks?75:120;
            var rows=new List<Paragraph>();
            for(var index=0;index<rowCount;index++)
            {
                var row=new Paragraph(new Run($"Return navigation row {index:D3}"))
                {
                    FontFamily=new FontFamily("Calibri"),FontSize=16,
                    Margin=new Thickness(0),LineHeight=20,
                    LineStackingStrategy=LineStackingStrategy.BlockLineHeight,
                    KeepTogether=false,KeepWithNext=false,
                    BreakPageBefore=forcedBreaks && index is 25 or 50,
                    Tag=ParagraphStyleTagPrefix+"Normal"
                };
                document.Blocks.Add(row);rows.Add(row);
            }
            Image? picture=null;
            if(pictureSource!=null)
            {
                picture=new Image{Source=pictureSource,Width=32,Height=48};
                rows[forcedBreaks?35:70].Inlines.Add(new InlineUIContainer(picture));
            }
            Editor.Document=document;ApplyPage();
            Editor.CaretPosition=rows[0].ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            Editor.Focus();FreshPagedLayout();
            if(picture!=null)
            {
                SetPicturePlacement(picture,PicturePlacementMode.InFrontOfText);
                var placement=floatingImagePlacements.Single();
                placement.OffsetX=260;placement.OffsetY=8;
                RefreshFloatingImagePlacements();FreshPagedLayout();
            }
            UpdatePageNavigationDemand();
            WaitForPagedCondition(()=>Beta46PaginationReady && PageNavigationList.Items.Count==Beta46DisplayedPageCount &&
                PageNavigationList.Items.Count>=3,"The Return/navigation fixture did not produce current page thumbnails.");
            var initialPages=Beta46DisplayedPageCount;
            var originalText=PagedDocumentText();
            var originalParagraphs=EnumerateDocumentParagraphs().Count();
            var originalPageBreaks=EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore);
            ClickReturnNavigationThumbnail(2);
            RequirePaged(Editor.CaretPosition.Paragraph!=rows[0],"The Return fixture did not navigate away from its first page.");

            // Keep the normal idle worker from replacing the old map while
            // physical Return input exercises its live provisional geometry.
            // Geometry/caret dispatcher work continues normally on every key.
            TextChangedEventHandler holdIdlePagination=(_,__)=>beta35PaginationTimer?.Stop();
            Editor.TextChanged+=holdIdlePagination;
            var maximumPending=Beta46DisplayedPageCount;
            try
            {
                for(var count=1;count<=5;count++)
                {
                    SendKeys(new[]{KeyInput(0x0D),KeyInput(0x0D,true)});
                    Editor.UpdateLayout();PagedEditor.UpdateLayout();PumpInput();
                    maximumPending=Math.Max(maximumPending,Beta46DisplayedPageCount);
                    var caret=Editor.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                    var source=caret.IsEmpty?new Point(double.NaN,double.NaN):Editor.TranslatePoint(caret.TopLeft,EditorSurfaceGrid);
                    report.AppendLine($"Return navigation {name}, key {count}: displayed={Beta46DisplayedPageCount}, thumbnails={PageNavigationList.Items.Count}, map={beta35PaginationMap?.PageCount}, pending={beta46PaginationPending}, sourceHeight={EditorSurfaceGrid.ActualHeight:0.##}/{EditorSurfaceGrid.DesiredSize.Height:0.##}, editorOffset={Editor.VerticalOffset:0.##}, caretY={caret.Top:0.##}, sourceY={source.Y:0.##}, starts=[{string.Join(',',PagedEditor.SourcePageStarts.Select(value=>value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)))}], breakParagraphs={EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)}.");
                }
            }
            finally {Editor.TextChanged-=holdIdlePagination;}
            var preview=CreatePreviewPaginator();preview.ComputePageCount();
            var expectedPages=preview.PageCount;
            var insertedParagraphs=EnumerateDocumentParagraphs().Count()-originalParagraphs;
            var actualPageBreaks=EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore);
            if(insertedParagraphs!=5)
                failures.Add($"{name}: five Returns inserted {insertedParagraphs} paragraphs.");
            if(actualPageBreaks!=originalPageBreaks)
                failures.Add($"{name}: five ordinary Returns duplicated deliberate page breaks: {originalPageBreaks} before, {actualPageBreaks} after.");
            if(maximumPending>Math.Max(initialPages,expectedPages))
                failures.Add($"{name}: five Returns temporarily displayed {maximumPending} pages, but the actual document has {expectedPages} (initially {initialPages}).");
            if(expectedPages!=initialPages)
                failures.Add($"{name}: five short paragraph breaks unexpectedly changed print pages from {initialPages} to {expectedPages} despite spare page space.");

            // Let the normal worker publish its current map and thumbnails.
            ScheduleBeta35PaginationMap(100);
            WaitForPagedCondition(()=>Beta46PaginationReady && PageNavigationList.Items.Count==expectedPages,
                $"Return navigation {name}: the page view and thumbnails did not settle together.");
            if(Beta46DisplayedPageCount!=expectedPages)
                failures.Add($"{name}: settled editing/print counts disagree: {Beta46DisplayedPageCount}/{expectedPages}.");
            var settledCount=Beta46DisplayedPageCount;
            var settledExtent=PagedEditor.ActualHeight;
            var textAfterReturns=PagedDocumentText();
            ClickReturnNavigationThumbnail(1);
            ClickReturnNavigationThumbnail(Math.Min(2,expectedPages));
            PumpInput();
            if(Beta46DisplayedPageCount!=settledCount || Math.Abs(PagedEditor.ActualHeight-settledExtent)>1 ||
                PagedDocumentText()!=textAfterReturns)
                failures.Add($"{name}: navigating again changed the page extent or source after Return.");

            for(var undo=0;undo<10 && PagedDocumentText()!=originalText && Editor.CanUndo;undo++)Editor.Undo();
            if(PagedDocumentText()!=originalText)
                failures.Add($"{name}: five Returns could not be undone exactly after page navigation.");
            if(EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)!=originalPageBreaks)
                failures.Add($"{name}: Undo did not restore the original deliberate page-break count.");
            FreshPagedLayout();
            if(forcedBreaks)VerifyManualPageBreakCommandPaths(report);
            report.AppendLine($"Return navigation {name}: initial={initialPages}, maximumPending={maximumPending}, finalPrint={expectedPages}, finalDisplayed={settledCount}; five physical Returns and navigation preserved canonical paragraph insertion/Undo.");
        }
        NavigationPane.Visibility=Visibility.Collapsed;
        UpdatePageNavigationDemand();
        RequirePaged(failures.Count==0,string.Join(Environment.NewLine,failures));
        report.AppendLine("Five physical Returns after thumbnail navigation produced no phantom pages; live, settled, thumbnail and print counts remained consistent, and Undo restored the original text.");
    }

    void VerifyManualPageBreakCommandPaths(StringBuilder report)
    {
        foreach(var path in new[]{"WPF paragraph command","safe macro"})
        {
            var original=FindPagedParagraph("Return navigation row 025");
            var before=PagedDocumentText();
            var paragraphs=EnumerateDocumentParagraphs().Count();
            var breaks=EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore);
            Editor.CaretPosition=original.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            Editor.Focus();PumpInput();
            if(path=="safe macro")AwaitPagedTask(RunSafeMacroAsync("NEW_PARAGRAPH"));
            else EditingCommands.EnterParagraphBreak.Execute(null,Editor);
            RequirePaged(EnumerateDocumentParagraphs().Count()==paragraphs+1 &&
                EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)==breaks,
                $"The {path} path duplicated a manual page boundary when inserting a paragraph.");
            for(var undo=0;undo<5 && PagedDocumentText()!=before && Editor.CanUndo;undo++)Editor.Undo();
            RequirePaged(PagedDocumentText()==before &&
                EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)==breaks,
                $"Undo did not restore the manual boundary after the {path} path.");
        }

        var set=ActiveParagraphStyleSet;
        var name="Return break rule "+Guid.NewGuid().ToString("N");
        var style=StyleSetStore.CreateStyle(name,ResolveStyle("Normal"));
        style.BreakPageBefore=true;style.FollowingStyle=name;
        set.Styles.Add(name,style);
        var paragraph=FindPagedParagraph("Return navigation row 025");
        var tag=paragraph.Tag;
        var source=PagedDocumentText();
        var beforeBreaks=EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore);
        try
        {
            paragraph.Tag=ParagraphStyleTag(name,style);
            Editor.CaretPosition=paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            Editor.Focus();PumpInput();
            EditingCommands.EnterParagraphBreak.Execute(null,Editor);
            var content=Editor.CaretPosition.Paragraph;
            RequirePaged(ResolveStyle(name)?.BreakPageBefore==true && content?.BreakPageBefore==true &&
                content.PreviousBlock is Paragraph {BreakPageBefore:true} &&
                EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)==beforeBreaks+1,
                "An intentional page-break-before style lost its repeatable boundary rule.");
        }
        finally
        {
            for(var undo=0;undo<5 && PagedDocumentText()!=source && Editor.CanUndo;undo++)Editor.Undo();
            FindPagedParagraph("Return navigation row 025").Tag=tag;
            set.Styles.Remove(name);
        }
        RequirePaged(PagedDocumentText()==source &&
            EnumerateDocumentParagraphs().Count(row=>row.BreakPageBefore)==beforeBreaks,
            "Undo did not restore the paragraph after checking the intentional page-break style.");
        FreshPagedLayout();
        report.AppendLine("WPF paragraph commands and safe macros preserved a single manual boundary; intentional page-break styles retained their repeatable rule, with Undo restoring each source.");
    }

    void ClickReturnNavigationThumbnail(int pageNumber)
    {
        var page=PageNavigationList.Items.Cast<NavigationPage>().Single(item=>item.Number==pageNumber);
        PageNavigationList.ScrollIntoView(page);UpdateLayout();PumpInput();
        var container=PageNavigationList.ItemContainerGenerator.ContainerFromItem(page) as ListBoxItem
            ??throw new InvalidOperationException("The Return fixture page thumbnail has no item container.");
        var point=container.PointToScreen(new Point(container.ActualWidth/2,Math.Min(50,container.ActualHeight/2)));
        SetPagedCursor((int)Math.Round(point.X),(int)Math.Round(point.Y));
        SendKeys(new[]
        {
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}
        });
        PumpInput();
    }

    void VerifyPagedBrowsingAndNavigation(Image floatingImage,StringBuilder report)
    {
        var scroll=FindVisualAncestor<ScrollViewer>(PagedEditor)
            ??throw new InvalidOperationException("The browse fixture has no outer scroll viewer.");
        RequirePaged(Beta46DisplayedPageCount>=4,"The browse fixture needs several text/picture pages.");
        NavigationPane.Visibility=Visibility.Visible;
        NavigationTabs.SelectedItem=NavigationPagesTab;
        UpdatePageNavigationDemand();
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
            PageNavigationList.Items.Count==Beta46DisplayedPageCount &&
            PageNavigationList.Items.Cast<NavigationPage>().All(item=>item.Thumbnail!=null),
            "Opening Pages did not produce current thumbnails.");
        var beforeText=PagedNavigationFingerprint();

        var first=FindPagedParagraph("FIRST PAGE");
        Editor.CaretPosition=first.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();PagedEditor.ActivatePageForCaret();PagedEditor.ScrollCaretIntoView();PumpInput();
        TypeInput(" Browse refresh sentinel");
        var sourceCaret=Editor.CaretPosition.GetOffsetToPosition(Editor.Document.ContentEnd);
        var initialOffset=scroll.VerticalOffset;
        // Use Windows mouse-wheel input over the editor while the changed
        // document is still waiting for its normal idle pagination worker.
        var wheelPoint=scroll.PointToScreen(new Point(scroll.ViewportWidth/2,Math.Min(200,scroll.ViewportHeight/2)));
        SetPagedCursor((int)Math.Round(wheelPoint.X),(int)Math.Round(wheelPoint.Y));
        SendKeys(new[]{new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput
        {
            Data=unchecked((uint)-2400),Flags=0x0800
        }}}});
        var wheelOffset=scroll.VerticalOffset;
        RequirePaged(wheelOffset>initialOffset+40,
            $"Mouse-wheel browsing did not leave the focused caret's viewport: before={initialOffset}, after={wheelOffset}.");
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
            PagedNavigationFingerprint()!=beforeText,"Typing did not refresh the visible page thumbnails.");
        for(var pass=0;pass<4;pass++)PumpInput();
        RequirePaged(scroll.VerticalOffset>=wheelOffset-4 &&
            Editor.CaretPosition.GetOffsetToPosition(Editor.Document.ContentEnd)==sourceCaret,
            $"Idle pagination pulled wheel browsing back to the unchanged caret: wheel={wheelOffset}, settled={scroll.VerticalOffset}.");

        // Dragging the outer scrollbar uses the same physical offset API. A
        // later native bring-into-view request or geometry refresh must not
        // turn a read-only browse into a jump back to the editing page.
        var lastPaper=Beta46PageBounds(Beta46DisplayedPageCount-1);
        var lastPoint=PagedEditor.TranslatePoint(lastPaper.TopLeft,scroll);
        scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+lastPoint.Y-16));
        UpdateLayout();PumpInput();
        var scrollbarOffset=scroll.VerticalOffset;
        RequirePaged(scrollbarOffset>wheelOffset+40,"The scrollbar fixture did not reach a later paper.");
        Editor.BringIntoView();
        FreshPagedLayout();
        for(var pass=0;pass<4;pass++)PumpInput();
        RequirePaged(Math.Abs(scroll.VerticalOffset-scrollbarOffset)<4 &&
            Editor.CaretPosition.GetOffsetToPosition(Editor.Document.ContentEnd)==sourceCaret,
            $"Layout/bring-into-view requests overrode scrollbar browsing: selected={scrollbarOffset}, actual={scroll.VerticalOffset}.");

        var beforePicture=PagedNavigationFingerprint();
        var placement=floatingImagePlacements.Single(item=>ReferenceEquals(item.RuntimeImage,floatingImage));
        var originalX=placement.OffsetX;
        try
        {
            placement.OffsetX=originalX+80;
            RefreshFloatingImagePlacements();
            WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
                PagedNavigationFingerprint()!=beforePicture,
                "Moving a floating picture did not refresh its page thumbnail without a text edit.");
            RequirePaged(Math.Abs(scroll.VerticalOffset-scrollbarOffset)<4,
                "Refreshing a picture thumbnail pulled the document back to the caret page.");
        }
        finally
        {
            placement.OffsetX=originalX;
            RefreshFloatingImagePlacements();
        }
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
            PagedNavigationFingerprint()==beforePicture,"Restoring the picture did not restore its thumbnail.");
        VerifyPagedThumbnailPicture(floatingImage,"in front of text");
        try
        {
            SetPicturePlacement(floatingImage,PicturePlacementMode.BehindText);
            placement.OffsetX=originalX+40;
            RefreshFloatingImagePlacements();
            WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
                PagedNavigationFingerprint()!=beforePicture,"The behind-text picture thumbnail did not refresh.");
            VerifyPagedThumbnailPicture(floatingImage,"behind text");
        }
        finally
        {
            SetPicturePlacement(floatingImage,PicturePlacementMode.InFrontOfText);
            placement.OffsetX=originalX;
            RefreshFloatingImagePlacements();
        }
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady &&
            PagedNavigationFingerprint()==beforePicture,"Restoring the front picture did not restore its thumbnail.");

        TypeInput(" Resume editing");
        var resumedCaret=PagedEditor.GetProjectedCharacterRect(Editor.CaretPosition);
        var resumedPoint=resumedCaret.IsEmpty?new Point(double.NaN,double.NaN):
            PagedEditor.TranslatePoint(resumedCaret.TopLeft,scroll);
        RequirePaged(PagedEditor.ActivePageIndex==0 && !resumedCaret.IsEmpty && resumedPoint.Y>=-2 &&
            resumedPoint.Y+resumedCaret.Height<=scroll.ViewportHeight+2,
            "Typing after browsing did not reveal the original insertion point again.");
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady,
            "Pagination did not settle after resuming typing.");

        var last=PageNavigationList.Items.Cast<NavigationPage>().Last();
        // A current thumbnail remains a navigation target while the next idle
        // map is pending; stale content must not make the list unresponsive.
        ScheduleBeta35PaginationMap(850);
        RequirePaged(beta35PaginationDirty && PageNavigationList.IsEnabled,
            "Scheduling an idle map disabled useful existing page thumbnails.");
        PageNavigationList.ScrollIntoView(last);UpdateLayout();PumpInput();
        var container=PageNavigationList.ItemContainerGenerator.ContainerFromItem(last) as ListBoxItem
            ??throw new InvalidOperationException("The final page thumbnail has no visible item container.");
        var thumbnailPoint=container.PointToScreen(new Point(container.ActualWidth/2,Math.Min(50,container.ActualHeight/2)));
        SetPagedCursor((int)Math.Round(thumbnailPoint.X),(int)Math.Round(thumbnailPoint.Y));
        SendKeys(new[]
        {
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}
        });
        for(var pass=0;pass<4;pass++)PumpInput();
        var caret=PagedEditor.GetProjectedCharacterRect(Editor.CaretPosition);
        var inViewport=caret.IsEmpty?new Point(double.NaN,double.NaN):PagedEditor.TranslatePoint(caret.TopLeft,scroll);
        RequirePaged(PageNavigationList.SelectedItem is NavigationPage selected && selected.Number==last.Number &&
            Beta46PageForPointer(Editor.CaretPosition)==last.Number-1 &&
            PagedEditor.ActivePageIndex==last.Number-1 && !caret.IsEmpty &&
            inViewport.Y>=-2 && inViewport.Y+caret.Height<=scroll.ViewportHeight+2,
            $"Clicking the final thumbnail did not select and display that page: requested={last.Number}, active={PagedEditor.ActivePageIndex+1}, caret={inViewport}.");
        WaitForPagedCondition(()=>PageNavigationList.IsEnabled && Beta46PaginationReady,
            "The pending page map did not settle after thumbnail navigation.");
        last=PageNavigationList.Items.Cast<NavigationPage>().Last();
        PageNavigationList.ScrollIntoView(last);UpdateLayout();PumpInput();
        scroll.ScrollToVerticalOffset(0);UpdateLayout();PumpInput();
        container=PageNavigationList.ItemContainerGenerator.ContainerFromItem(last) as ListBoxItem
            ??throw new InvalidOperationException("The selected thumbnail container was lost during its refresh.");
        thumbnailPoint=container.PointToScreen(new Point(container.ActualWidth/2,Math.Min(50,container.ActualHeight/2)));
        SetPagedCursor((int)Math.Round(thumbnailPoint.X),(int)Math.Round(thumbnailPoint.Y));
        SendKeys(new[]
        {
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},
            new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}
        });
        caret=PagedEditor.GetProjectedCharacterRect(Editor.CaretPosition);
        inViewport=caret.IsEmpty?new Point(double.NaN,double.NaN):PagedEditor.TranslatePoint(caret.TopLeft,scroll);
        RequirePaged(PageNavigationList.SelectedItem is NavigationPage repeated && repeated.Number==last.Number &&
            !caret.IsEmpty && inViewport.Y>=-2 && inViewport.Y+caret.Height<=scroll.ViewportHeight+2,
            "Clicking the already-selected thumbnail did not return to its page after manual browsing.");
        report.AppendLine("Wheel/scrollbar browsing survived idle pagination and native layout requests; typing revealed its caret again. Text and both floating-picture layers refreshed thumbnails; pending and repeated physical thumbnail clicks navigated correctly.");
        NavigationPane.Visibility=Visibility.Collapsed;
        UpdatePageNavigationDemand();
        FreshPagedLayout();
    }

    void VerifyPagedThumbnailPicture(Image image,string mode)
    {
        var picture=CreateFloatingPageImages(new Size(pageWidth,pageHeight)).Single();
        var source=PageNavigationList.Items.Cast<NavigationPage>().Single(item=>item.Number==picture.PageNumber+1).Thumbnail
            ??throw new InvalidOperationException("The picture page thumbnail is missing.");
        var bitmap=source.Format==PixelFormats.Pbgra32?source:
            new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        var stride=bitmap.PixelWidth*4;
        var pixels=new byte[stride*bitmap.PixelHeight];
        bitmap.CopyPixels(pixels,stride,0);
        var xScale=bitmap.PixelWidth/pageWidth;var yScale=bitmap.PixelHeight/pageHeight;
        var left=Math.Clamp((int)Math.Ceiling(picture.Bounds.Left*xScale)+1,0,bitmap.PixelWidth);
        var right=Math.Clamp((int)Math.Floor(picture.Bounds.Right*xScale)-1,0,bitmap.PixelWidth);
        var top=Math.Clamp((int)Math.Ceiling(picture.Bounds.Top*yScale)+1,0,bitmap.PixelHeight);
        var bottom=Math.Clamp((int)Math.Floor(picture.Bounds.Bottom*yScale)-1,0,bitmap.PixelHeight);
        var colourCount=0;
        for(var y=top;y<bottom;y++)
            for(var x=left;x<right;x++)
            {
                var pixel=y*stride+x*4;
                if(Math.Abs(pixels[pixel]-90)<=16 && Math.Abs(pixels[pixel+1]-40)<=16 &&
                   Math.Abs(pixels[pixel+2]-180)<=16 && pixels[pixel+3]>=220)colourCount++;
            }
        RequirePaged(image.Source!=null && colourCount>=8,
            $"The {mode} floating picture is absent from its page thumbnail: coloured pixels={colourCount}.");
    }

    string PagedNavigationFingerprint()
    {
        using var bytes=new MemoryStream();
        using var writer=new BinaryWriter(bytes,Encoding.UTF8,true);
        foreach(var item in PageNavigationList.Items.Cast<NavigationPage>())
        {
            writer.Write(item.Number);
            if(item.Thumbnail is not BitmapSource source){writer.Write(0);continue;}
            var bitmap=source.Format==PixelFormats.Pbgra32?source:
                new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
            writer.Write(bitmap.PixelWidth);writer.Write(bitmap.PixelHeight);
            var stride=bitmap.PixelWidth*4;
            var pixels=new byte[stride*bitmap.PixelHeight];
            bitmap.CopyPixels(pixels,stride,0);writer.Write(pixels);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    static void WaitForPagedCondition(Func<bool> condition,string message)
    {
        var watch=Stopwatch.StartNew();
        while(!condition())
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException(message);
            PumpInput();
        }
    }

    static void SetPagedCursor(int x,int y)
    {
        if(!SetPagedCursorPosition(x,y))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not position test mouse input.");
    }

    void VerifyFloatingPageOffset(Image image,StringBuilder report)
    {
        var placement=floatingImagePlacements.Single();
        var original=Beta46FloatingPictureBounds(image);
        var width=placement.Width;var height=placement.Height;
        var x=placement.OffsetX;var y=placement.OffsetY;
        RequirePaged(!original.IsEmpty,"The floating portrait has no initial provider bounds.");
        try
        {
            placement.PageOffset=1;
            RefreshBeta46PagedPictures();
            var moved=Beta46FloatingPictureBounds(image);
            var nextPaper=Beta46PageBounds(2);
            var expectedShift=nextPaper.Top-Beta46PageBounds(1).Top;
            var printed=CreateFloatingPageImages(new Size(pageWidth,pageHeight)).Single();
            RequirePaged(!moved.IsEmpty && moved.Top>=nextPaper.Top && moved.Bottom<=nextPaper.Bottom &&
                Math.Abs(moved.Top-original.Top-expectedShift)<1 && Math.Abs(moved.Left-original.Left)<1 &&
                printed.PageNumber==2 && printed.Bounds.Size==original.Size,
                "A floating picture's page offset did not move its provider and print placement to page three.");
            RequirePaged(placement.Width==width && placement.Height==height && placement.OffsetX==x && placement.OffsetY==y,
                "Changing a picture's page offset changed its stored size or body coordinates.");
            var restored=JsonSerializer.Deserialize<CzdSettings>(JsonSerializer.Serialize(CaptureDocumentSettings()));
            RequirePaged(restored?.FloatingImages.Count==1 && restored.FloatingImages[0].PageOffset==1,
                "The floating picture's page offset was lost by native settings JSON round-trip.");
        }
        finally
        {
            placement.PageOffset=0;
            RefreshBeta46PagedPictures();
        }
        RequirePaged(Beta46FloatingPictureBounds(image)==original,"Resetting a picture's page offset did not restore its original provider bounds.");
        report.AppendLine("A persisted picture page offset moved the portrait to page three for both editing and printing without changing its size/body offsets.");
    }

    void VerifyTallFloatingPicturePixels(Image image,StringBuilder report)
    {
        RequirePaged(GetPicturePlacement(image)==PicturePlacementMode.InFrontOfText,"The portrait fixture lost its floating placement mode.");
        var printed=CreateFloatingPageImages(new Size(pageWidth,pageHeight)).Single();
        RequirePaged(printed.PageNumber==1 && printed.Bounds.Width==80 && printed.Bounds.Height==120,
            "The floating portrait did not retain its full printed size on the anchor page.");
        var paper=Beta46PageBounds(printed.PageNumber);
        // Printing routes the image by its logical source anchor. A collapsed
        // anchor's forward character rectangle can lie on the following line,
        // so do not derive the expected page by geometric Y alone.
        var expected=printed.Bounds;

        // Sample the rendered papers rather than accepting the control's bounds:
        // the old source-segment clip retained those bounds but cut the pixels.
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen())
        {
            var brush=new VisualBrush(PagedEditor)
            {
                ViewboxUnits=BrushMappingMode.Absolute,Viewbox=paper,
                Stretch=Stretch.Fill,AutoLayoutContent=false
            };
            dc.DrawRectangle(brush,null,new Rect(new Point(0,0),paper.Size));
        }
        var bitmap=RenderPagedDebugVisual(drawing,paper.Size);
        SavePagedDebugBitmap(bitmap,"CtrlZed46Page2.png");
        var preview=CreatePreviewPaginator();
        var printedPage=preview.GetPage(1);
        var printDrawing=new DrawingVisual();
        using(var dc=printDrawing.RenderOpen())
            dc.DrawRectangle(new VisualBrush(printedPage.Visual){Stretch=Stretch.Fill,AutoLayoutContent=false},null,
                new Rect(new Point(0,0),paper.Size));
        var printBitmap=RenderPagedDebugVisual(printDrawing,paper.Size);
        var shownPixels=CountPagedPortraitPixels(bitmap,expected);
        var printPixels=CountPagedPortraitPixels(printBitmap,printed.Bounds);
        report.AppendLine($"Portrait pixels at {expected}: editable={shownPixels}, printed={printPixels}; expected height={expected.Height}.");
        RequirePaged(shownPixels>=expected.Height-8 && printPixels>=expected.Height-8 && Math.Abs(shownPixels-printPixels)<=4,
            $"The 120-DIP portrait was clipped by a text page break: editable purple height={shownPixels}, printed={printPixels}.");
    }

    static int CountPagedPortraitPixels(BitmapSource bitmap,Rect expected)
    {
        var stride=bitmap.PixelWidth*4;
        var pixels=new byte[stride*bitmap.PixelHeight];
        bitmap.CopyPixels(pixels,stride,0);
        var x=Math.Clamp((int)Math.Round(expected.Left+expected.Width/2),0,bitmap.PixelWidth-1);
        var first=Math.Clamp((int)Math.Ceiling(expected.Top)+2,0,bitmap.PixelHeight);
        var end=Math.Clamp((int)Math.Floor(expected.Bottom)-2,0,bitmap.PixelHeight);
        var count=0;
        for(var y=first;y<end;y++)
        {
            var index=y*stride+x*4;
            if(Math.Abs(pixels[index]-90)<=12 && Math.Abs(pixels[index+1]-40)<=12 &&
               Math.Abs(pixels[index+2]-180)<=12 && pixels[index+3]>=220)count++;
        }
        return count;
    }

    void SavePagedLayoutDebugImage(StringBuilder report)
    {
        var oldZoom=zoomPercent;
        var scroll=FindVisualAncestor<ScrollViewer>(PagedEditor);
        var oldOffset=scroll?.VerticalOffset??0;
        try
        {
            zoomPercent=50;ApplyZoom();RefreshBeta46PagedEditingLayout();UpdateLayout();PumpInput();
            if(scroll!=null)
            {
                var top=PagedEditor.TranslatePoint(Beta46PageBounds(1).TopLeft,scroll);
                scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+top.Y-8));
                UpdateLayout();PumpInput();
            }
            SavePagedDebugBitmap(RenderPagedDebugVisual(this,new Size(ActualWidth,ActualHeight)),"CtrlZed46Layout.png");
            report.AppendLine("Layout viewport saved at 50% zoom: "+Path.Combine(Path.GetTempPath(),"CtrlZed46Layout.png"));
        }
        finally
        {
            zoomPercent=oldZoom;ApplyZoom();RefreshBeta46PagedEditingLayout();UpdateLayout();
            scroll?.ScrollToVerticalOffset(oldOffset);PumpInput();
        }
    }

    static RenderTargetBitmap RenderPagedDebugVisual(Visual visual,Size size)
    {
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(size.Width)),
            Math.Max(1,(int)Math.Ceiling(size.Height)),96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }

    static void SavePagedDebugBitmap(BitmapSource bitmap,string name)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(Path.GetTempPath(),name));encoder.Save(output);
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
