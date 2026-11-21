using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    bool beta46PagedEditingInstalled;
    bool beta46UpdatingPagedGeometry;
    bool beta46PagedGeometryDirty=true;
    bool beta46PaginationPending=true;
    FlowDocument? beta46AnchorDocument;
    readonly List<TextPointer> beta46PageAnchors=new();
    readonly List<double> beta46PageLeading=new();
    DispatcherTimer? beta46PagedGeometryTimer;

    internal void InitializeBeta46PagedEditing()
    {
        if(beta46PagedEditingInstalled || PagedEditor==null)return;
        beta46PagedEditingInstalled=true;
        PagedEditor.Editor=Editor;
        PagedEditor.HeaderFooterProvider=(page,total)=>(ExpandFields(headerText,page,total),ExpandFields(footerText,page,total));
        PagedEditor.ActivatedPageMouseDown+=(_,e)=>
        {
            // The viewport has already moved under the pointer. Existing image
            // and context-menu handlers use coordinate hit tests, so forwarding
            // the original event preserves click count and native mouse capture.
            if(e.ChangedButton==MouseButton.Left)Editor_PreviewMouseLeftButtonDown(Editor,e);
            else if(e.ChangedButton==MouseButton.Right)
            {
                // The window-level grammar preview ran before this inactive
                // page moved under the pointer. Re-evaluate its existing menu
                // path now that mouse hit testing sees the native editor.
                Mouse.Synchronize();
                GrammarPreviewMouseDown(this,e);
                if(!e.Handled)Editor_PreviewMouseRightButtonDown(Editor,e);
            }
        };
        beta46PagedGeometryTimer=new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval=TimeSpan.FromMilliseconds(80)
        };
        beta46PagedGeometryTimer.Tick+=(_,__)=>
        {
            if(IsMenuInteractionActive)return;
            beta46PagedGeometryTimer.Stop();
            UpdateBeta46PagedGeometry();
        };
        Editor.TextChanged+=(_,__)=>
        {
            beta46PaginationPending=true;
            PagedEditor.IsPaginationPending=true;
            EnsureBeta46AnchorDocument();
            QueueBeta46PagedGeometry();
        };
        Editor.SelectionChanged+=(_,__)=>
        {
            if(!beta46PagedEditingInstalled || !PagedEditor.IsPaged || beta46UpdatingPagedGeometry)return;
            EnsureBeta46AnchorDocument();
            if(beta46PaginationPending)
            {
                // Source height can grow continuously while the idle worker is
                // cancelled by further typing. The host keeps provisional
                // page slices live until an authoritative map arrives.
                PagedEditor.ActivatePageForCaret();
                QueueBeta46PagedGeometry();
            }
            else
                PagedEditor.SetActivePage(Beta46PageForPointer(Editor.CaretPosition));
        };
        Editor.SizeChanged+=(_,__)=>
        {
            if(!beta46UpdatingPagedGeometry)QueueBeta46PagedGeometry();
        };
        PagedEditor.ActivePageChanged+=(_,__)=>UpdateBeta35PageStatus();
        Closed+=(_,__)=>beta46PagedGeometryTimer?.Stop();
        RefreshBeta46PagedEditingLayout();
    }

    internal void RefreshBeta46PagedEditingLayout()
    {
        if(!beta46PagedEditingInstalled || PagedEditor==null)return;
        // Paper and body margins belong to the projected host in both modes.
        // The outer border supplies only centring and the zoom transform.
        PageBorder.Padding=new Thickness(0);
        PageBorder.MinHeight=0;
        PageBorder.Background=Brushes.Transparent;
        PageBorder.BorderThickness=new Thickness(0);
        var paper=new Size(Math.Max(240,pageWidth),Math.Max(320,pageHeight));
        var paged=CurrentEditorViewMode==EditorViewMode.Paged;
        var geometryChanged=PagedEditor.PaperSize!=paper || PagedEditor.PageMargins!=pageMargins ||
            PagedEditor.IsPaged!=paged || !ReferenceEquals(beta46AnchorDocument,Editor.Document);
        PagedEditor.PaperSize=paper;
        PagedEditor.PageMargins=pageMargins;
        PagedEditor.IsPaged=paged;
        PagedEditor.IsPaginationPending=beta46PaginationPending;
        PageGuides.Visibility=Visibility.Collapsed;
        if(beta35PageSurfaceLayer!=null)beta35PageSurfaceLayer.Visibility=Visibility.Collapsed;
        EnsureBeta46AnchorDocument();
        PagedEditor.InvalidateVisual();
        if(geometryChanged)QueueBeta46PagedGeometry();
    }

    void EnsureBeta46AnchorDocument()
    {
        if(ReferenceEquals(beta46AnchorDocument,Editor.Document))return;
        beta46AnchorDocument=Editor.Document;
        beta46PageAnchors.Clear();
        beta46PageLeading.Clear();
        beta46PageLeading.Add(0);
        beta46PageAnchors.Add(Editor.Document.ContentStart.GetPositionAtOffset(0,LogicalDirection.Forward)!);
        beta46PaginationPending=true;
        PagedEditor.IsPaginationPending=true;
        PagedEditor.SetPageStarts(new[]{0d});
    }

    internal void OnBeta46PaginationPending()
    {
        if(!beta46PagedEditingInstalled)return;
        EnsureBeta46AnchorDocument();
        beta46PaginationPending=true;
        PagedEditor.IsPaginationPending=true;
        QueueBeta46PagedGeometry();
    }

    internal void OnBeta46PaginationMapApplied(PaginationMapResult map)
    {
        if(!beta46PagedEditingInstalled || !ReferenceEquals(beta35PaginationDocument,Editor.Document))return;
        beta46AnchorDocument=Editor.Document;
        beta46PageAnchors.Clear();
        beta46PageLeading.Clear();
        var start=Editor.Document.ContentStart;
        var length=start.GetOffsetToPosition(Editor.Document.ContentEnd);
        for(var page=0;page<map.PageStartOffsets.Count;page++)
        {
            var pointer=start.GetPositionAtOffset(Math.Clamp(map.PageStartOffsets[page],0,length),LogicalDirection.Forward);
            if(pointer==null)continue;
            beta46PageAnchors.Add(pointer);
            var firstY=page<map.PageFirstContentY.Count?map.PageFirstContentY[page]:pageMargins.Top;
            beta46PageLeading.Add(double.IsFinite(firstY)?Math.Max(0,firstY-pageMargins.Top):0);
        }
        if(beta46PageAnchors.Count==0)beta46PageAnchors.Add(start.GetPositionAtOffset(0,LogicalDirection.Forward)!);
        beta46PaginationPending=false;
        PagedEditor.IsPaginationPending=false;
        QueueBeta46PagedGeometry();
    }

    void QueueBeta46PagedGeometry()
    {
        if(!beta46PagedEditingInstalled || beta46PagedGeometryTimer==null)return;
        beta46PagedGeometryDirty=true;
        // Throttle rather than debounce: sustained typing must still move the
        // live caret across page edges while snapshot pagination waits for idle.
        if(!beta46PagedGeometryTimer.IsEnabled)beta46PagedGeometryTimer.Start();
    }

    void UpdateBeta46PagedGeometry()
    {
        if(!beta46PagedEditingInstalled || beta46UpdatingPagedGeometry || IsMenuInteractionActive)return;
        beta46UpdatingPagedGeometry=true;
        try
        {
            EnsureBeta46AnchorDocument();
            Editor.UpdateLayout();
            var starts=new List<double>{0};
            for(var i=1;i<beta46PageAnchors.Count;i++)
            {
                var pointer=beta46PageAnchors[i].GetInsertionPosition(LogicalDirection.Forward)??beta46PageAnchors[i];
                var rect=pointer.GetCharacterRect(LogicalDirection.Forward);
                if(rect.IsEmpty || !double.IsFinite(rect.Top))
                {
                    // Keep the last usable projection until WPF has measured
                    // the newly changed document, rather than losing pages.
                    QueueBeta46PagedGeometry();
                    return;
                }
                var leading=i<beta46PageLeading.Count?beta46PageLeading[i]:0;
                starts.Add(Math.Max(starts[^1],rect.Top-leading));
            }
            if(!beta46PaginationPending)
                PageGuides.CachedPageStartsY=starts.ToArray();
            PagedEditor.SetPageStarts(starts);
            PagedEditor.IsPaginationPending=beta46PaginationPending;
            if(PagedEditor.IsPaged)
            {
                if(beta46PaginationPending)PagedEditor.ActivatePageForCaret();
                else PagedEditor.SetActivePage(Beta46PageForPointer(Editor.CaretPosition));
            }
            beta46PagedGeometryDirty=false;
            if(beta46PaginationPending && PagedEditor.IsPaged)RequestBeta46BoundaryPagination();
        }
        catch(InvalidOperationException)
        {
            QueueBeta46PagedGeometry();
        }
        finally { beta46UpdatingPagedGeometry=false; }
    }

    int Beta46PageForPointer(TextPointer pointer)
    {
        if(!ReferenceEquals(beta46AnchorDocument,Editor.Document))return 0;
        var page=0;
        for(var i=1;i<beta46PageAnchors.Count;i++)
        {
            if(pointer.CompareTo(beta46PageAnchors[i])<0)break;
            page=i;
        }
        return page;
    }

    void RequestBeta46BoundaryPagination()
    {
        if(beta35PaginationTimer==null || !beta35PaginationTimer.IsEnabled)return;
        var rect=PagedEditor.GetProjectedCharacterRect(Editor.CaretPosition);
        var paper=PagedEditor.GetPageBounds(PagedEditor.ActivePageIndex);
        if(rect.IsEmpty || paper.IsEmpty)return;
        var top=paper.Top+pageMargins.Top;
        var bottom=paper.Bottom-pageMargins.Bottom;
        var threshold=Math.Max(48,rect.Height*3);
        if(rect.Top-top>threshold && bottom-rect.Bottom>threshold)return;
        // Do not recursively schedule/cancel a worker from its own map hook.
        // Only shorten the already waiting idle timer at a page edge.
        if(beta35PaginationTimer.Interval>TimeSpan.FromMilliseconds(100))
            beta35PaginationTimer.Interval=TimeSpan.FromMilliseconds(100);
    }

    internal bool ScrollBeta46TextPointerIntoView(TextPointer target)
    {
        if(!beta46PagedEditingInstalled || !PagedEditor.IsPaged)return false;
        if(IsMenuInteractionActive)return true;
        EnsureBeta46AnchorDocument();
        if(beta46PagedGeometryDirty)UpdateBeta46PagedGeometry();
        if(beta46PaginationPending)PagedEditor.ActivatePageForCaret();
        else PagedEditor.SetActivePage(Beta46PageForPointer(target));
        var rect=PagedEditor.GetProjectedCharacterRect(target);
        if(rect.IsEmpty)return true;
        var scroll=FindVisualAncestor<ScrollViewer>(PageBorder);
        if(scroll==null)return true;
        var point=PagedEditor.TranslatePoint(rect.TopLeft,scroll);
        var lower=point.Y+rect.Height;
        if(point.Y<0)scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+point.Y-12));
        else if(lower>scroll.ViewportHeight)
            scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+lower-scroll.ViewportHeight+12));
        return true;
    }

    internal int Beta46DisplayedPageCount=>PagedEditor?.PageCount??1;
    internal Rect Beta46PageBounds(int zeroBasedPage)=>PagedEditor.GetPageBounds(zeroBasedPage);
    internal bool Beta46PaginationReady=>beta46PagedEditingInstalled && !beta46PagedGeometryDirty &&
        !beta46PaginationPending && !beta35PaginationDirty &&
        ReferenceEquals(beta35PaginationDocument,Editor.Document);

    internal async Task RebuildBeta46PaginationForTestAsync()
    {
        for(var attempt=0;attempt<3;attempt++)
        {
            Editor.UpdateLayout();
            PagedEditor.UpdateLayout();
            await RebuildBeta35PaginationMapAsync();
            UpdateBeta46PagedGeometry();
            PagedEditor.UpdateLayout();
            if(Beta46PaginationReady)break;
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        }
    }
}
