using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public sealed record PagedFloatingPicture(ImageSource Source,Rect Bounds,PicturePlacementMode Mode,bool Selected=false);

/// <summary>
/// Projects one live editing surface onto separate paper sheets. The actual
/// RichTextBox remains over the active sheet; the other sheets sample the same
/// live visual. No document content or paragraph formatting is used as spacing.
/// </summary>
[ContentProperty(nameof(EditorSurface))]
public sealed class PagedEditorHost : Panel
{
    public static readonly DependencyProperty EditorSurfaceProperty=DependencyProperty.Register(
        nameof(EditorSurface),typeof(UIElement),typeof(PagedEditorHost),new FrameworkPropertyMetadata(null,SurfaceChanged));
    public static readonly DependencyProperty EditorProperty=DependencyProperty.Register(
        nameof(Editor),typeof(RichTextBox),typeof(PagedEditorHost),new FrameworkPropertyMetadata(null,EditorChanged));
    public static readonly DependencyProperty IsPagedProperty=DependencyProperty.Register(
        nameof(IsPaged),typeof(bool),typeof(PagedEditorHost),new FrameworkPropertyMetadata(true,GeometryChanged));
    public static readonly DependencyProperty IsPaginationPendingProperty=DependencyProperty.Register(
        nameof(IsPaginationPending),typeof(bool),typeof(PagedEditorHost),new FrameworkPropertyMetadata(true,GeometryChanged));
    public static readonly DependencyProperty PaperSizeProperty=DependencyProperty.Register(
        nameof(PaperSize),typeof(Size),typeof(PagedEditorHost),new FrameworkPropertyMetadata(new Size(794,1123),GeometryChanged));
    public static readonly DependencyProperty PageMarginsProperty=DependencyProperty.Register(
        nameof(PageMargins),typeof(Thickness),typeof(PagedEditorHost),new FrameworkPropertyMetadata(new Thickness(72),GeometryChanged));
    public static readonly DependencyProperty PageGapProperty=DependencyProperty.Register(
        nameof(PageGap),typeof(double),typeof(PagedEditorHost),new FrameworkPropertyMetadata(24d,GeometryChanged));

    public UIElement? EditorSurface {get=>(UIElement?)GetValue(EditorSurfaceProperty);set=>SetValue(EditorSurfaceProperty,value);}
    public RichTextBox? Editor {get=>(RichTextBox?)GetValue(EditorProperty);set=>SetValue(EditorProperty,value);}
    public bool IsPaged {get=>(bool)GetValue(IsPagedProperty);set=>SetValue(IsPagedProperty,value);}
    public bool IsPaginationPending {get=>(bool)GetValue(IsPaginationPendingProperty);set=>SetValue(IsPaginationPendingProperty,value);}
    public Size PaperSize {get=>(Size)GetValue(PaperSizeProperty);set=>SetValue(PaperSizeProperty,value);}
    public Thickness PageMargins {get=>(Thickness)GetValue(PageMarginsProperty);set=>SetValue(PageMarginsProperty,value);}
    public double PageGap {get=>(double)GetValue(PageGapProperty);set=>SetValue(PageGapProperty,value);}
    public int ActivePageIndex=>activePage;
    public int PageCount=>IsPaged?effectiveStarts.Count:1;
    public IReadOnlyList<double> SourcePageStarts=>effectiveStarts;
    public Func<int,int,(string Header,string Footer)>? HeaderFooterProvider {get;set;}
    public Func<int,IReadOnlyList<PagedFloatingPicture>>? FloatingPicturesProvider {get;set;}
    public event EventHandler? ActivePageChanged;
    public event EventHandler<MouseButtonEventArgs>? ActivatedPageMouseDown;

    readonly NativeViewport viewport=new(){ClipToBounds=true};
    readonly ProjectionLayer behindPictures;
    readonly ProjectionLayer projectedText;
    readonly ProjectionLayer frontPictures;
    readonly List<double> starts=new(){0};
    readonly List<double> effectiveStarts=new(){0};
    readonly Dictionary<int,VisualBrush> brushes=new();
    readonly Dictionary<double,double> measuredLineSplits=new();
    ScrollViewer? outerScroll;
    DispatcherOperation? queuedCaret;
    bool revealCaretPending;
    int activePage;
    bool changingGeometry;
    bool manualDrag;
    TextPointer? dragAnchor;
    FlowDocument? dragDocument;
    Rect nativeBounds;

    public PagedEditorHost()
    {
        behindPictures=new ProjectionLayer(this,0);
        projectedText=new ProjectionLayer(this,1);
        frontPictures=new ProjectionLayer(this,2);
        Children.Add(behindPictures);
        Children.Add(projectedText);
        Children.Add(viewport);
        Children.Add(frontPictures);
        Focusable=false;
        AddHandler(RequestBringIntoViewEvent,new RequestBringIntoViewEventHandler(BringIntoViewRequested),true);
        Loaded+=(_,__)=>
        {
            DetachOuterScroll();
            outerScroll=FindOuterScroll();
            if(outerScroll!=null)
            {
                outerScroll.ScrollChanged+=OuterScrollChanged;
                outerScroll.PreviewMouseWheel+=OuterScrollPreviewMouseWheel;
                outerScroll.PreviewMouseDown+=OuterScrollPreviewMouseDown;
            }
            QueueCaretRefresh();
        };
        Unloaded+=(_,__)=>
        {
            queuedCaret?.Abort();queuedCaret=null;revealCaretPending=false;EndManualDrag();
            DetachOuterScroll();
            outerScroll=null;
        };
    }

    static void SurfaceChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)
    {
        var host=(PagedEditorHost)sender;
        host.viewport.Child=null;
        host.viewport.Child=e.NewValue as UIElement;
        host.brushes.Clear();
        host.measuredLineSplits.Clear();
        host.InvalidateMeasure();host.InvalidateVisual();
    }

    static void EditorChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)
    {
        var host=(PagedEditorHost)sender;
        host.measuredLineSplits.Clear();
        if(e.OldValue is RichTextBox old)
        {
            old.TextChanged-=host.EditorTextChanged;
            old.SelectionChanged-=host.EditorSelectionChanged;
            old.PreviewKeyDown-=host.EditorPreviewKeyDown;
        }
        if(e.NewValue is RichTextBox editor)
        {
            editor.TextChanged+=host.EditorTextChanged;
            editor.SelectionChanged+=host.EditorSelectionChanged;
            editor.PreviewKeyDown+=host.EditorPreviewKeyDown;
        }
        host.QueueCaretRefresh();
    }

    static void GeometryChanged(DependencyObject sender,DependencyPropertyChangedEventArgs e)
    {
        var host=(PagedEditorHost)sender;
        host.measuredLineSplits.Clear();
        host.RebuildEffectiveStarts(false);
        host.InvalidateMeasure();host.InvalidateVisual();host.QueueCaretRefresh();
    }

    double WidthOfPaper=>double.IsFinite(PaperSize.Width)?Math.Max(240,PaperSize.Width):794;
    double HeightOfPaper=>double.IsFinite(PaperSize.Height)?Math.Max(320,PaperSize.Height):1123;
    double Gap=>double.IsFinite(PageGap)?Math.Clamp(PageGap,0,200):24;
    Thickness Margins=>new(Safe(PageMargins.Left),Safe(PageMargins.Top),Safe(PageMargins.Right),Safe(PageMargins.Bottom));
    static double Safe(double value)=>double.IsFinite(value)?Math.Max(0,value):0;
    double BodyWidth=>Math.Max(80,WidthOfPaper-Margins.Left-Margins.Right);
    double BodyHeight=>Math.Max(80,HeightOfPaper-Margins.Top-Margins.Bottom);
    double NativeHeight=>Math.Max(0,viewport.NativeSize.Height);

    public void SetPageStarts(IReadOnlyList<double> sourceStarts)
    {
        var next=sourceStarts.Where(y=>double.IsFinite(y)&&y>=0).OrderBy(y=>y)
            .DistinctBy(y=>Math.Round(y,2)).ToList();
        if(next.Count==0 || next[0]>0.01)next.Insert(0,0);
        else next[0]=0;
        if(Same(starts,next))return;
        starts.Clear();starts.AddRange(next);
        measuredLineSplits.Clear();
        RebuildEffectiveStarts(true);
        QueueCaretRefresh();
    }

    static bool Same(IReadOnlyList<double> left,IReadOnlyList<double> right)
        =>left.Count==right.Count && !left.Where((value,index)=>Math.Abs(value-right[index])>0.1).Any();

    void RebuildEffectiveStarts(bool useLines)
    {
        if(changingGeometry)return;
        changingGeometry=true;
        try
        {
            var next=new List<double>{0};
            var supplied=starts.Where(y=>y>0.1 && (NativeHeight<1 || y<NativeHeight)).ToArray();
            foreach(var end in supplied.Append(NativeHeight))
            {
                if(IsPaged && IsPaginationPending)
                {
                    var guard=0;
                    while(end-next[^1]>BodyHeight+1 && guard++<20000)
                    {
                        var target=next[^1]+BodyHeight;
                        var key=Math.Round(target,2);
                        var split=target;
                        if(useLines)
                        {
                            split=LineStartAtOrBefore(target);
                            measuredLineSplits[key]=split;
                        }
                        else if(measuredLineSplits.TryGetValue(key,out var measured))split=measured;
                        if(split<=next[^1]+1)split=target;
                        if(split>=end-0.1)break;
                        next.Add(split);
                    }
                }
                if(end!=NativeHeight && end>next[^1]+0.1)next.Add(end);
            }
            if(Same(effectiveStarts,next))return;
            effectiveStarts.Clear();effectiveStarts.AddRange(next);
            brushes.Clear();
            activePage=Math.Clamp(activePage,0,effectiveStarts.Count-1);
            InvalidateMeasure();InvalidateArrange();InvalidateVisual();
        }
        finally {changingGeometry=false;}
    }

    double LineStartAtOrBefore(double sourceY)
    {
        if(Editor==null || EditorSurface==null || !Editor.IsArrangeValid)return sourceY;
        try
        {
            var point=EditorSurface.TranslatePoint(new Point(1,sourceY),Editor);
            var pointer=Editor.GetPositionFromPoint(point,true)?.GetLineStartPosition(0);
            if(pointer==null)return sourceY;
            var rect=SourceCharacterRect(pointer);
            if(!rect.IsEmpty && rect.Top>sourceY+0.1 && pointer.GetLineStartPosition(-1) is { } previous)
                rect=SourceCharacterRect(previous);
            return rect.IsEmpty || !double.IsFinite(rect.Top)?sourceY:rect.Top;
        }
        catch(InvalidOperationException){return sourceY;}
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        viewport.Measure(new Size(BodyWidth,double.PositiveInfinity));
        RebuildEffectiveStarts(false);
        var height=IsPaged?PageCount*HeightOfPaper+(PageCount-1)*Gap:
            Math.Max(HeightOfPaper,NativeHeight+Margins.Top+Margins.Bottom);
        var layerSize=new Size(WidthOfPaper,height);
        behindPictures.Measure(layerSize);projectedText.Measure(layerSize);frontPictures.Measure(layerSize);
        return new Size(WidthOfPaper,height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var margins=Margins;
        var page=IsPaged?activePage:0;
        var start=IsPaged?effectiveStarts[page]:0;
        var height=IsPaged?SegmentHeight(page):Math.Max(BodyHeight,NativeHeight);
        nativeBounds=new Rect(margins.Left,(IsPaged?page*(HeightOfPaper+Gap):0)+margins.Top,BodyWidth,height);
        viewport.SourceStartY=start;
        viewport.Arrange(nativeBounds);
        var layerBounds=new Rect(finalSize);
        behindPictures.Arrange(layerBounds);projectedText.Arrange(layerBounds);frontPictures.Arrange(layerBounds);
        return finalSize;
    }

    double SegmentHeight(int page)
        =>page+1<effectiveStarts.Count?Math.Clamp(effectiveStarts[page+1]-effectiveStarts[page],0,BodyHeight):BodyHeight;

    public Rect GetPageBounds(int pageIndex)
    {
        if(pageIndex<0 || pageIndex>=PageCount)return Rect.Empty;
        return new Rect(0,IsPaged?pageIndex*(HeightOfPaper+Gap):0,WidthOfPaper,
            IsPaged?HeightOfPaper:Math.Max(HeightOfPaper,NativeHeight+Margins.Top+Margins.Bottom));
    }

    public void SetActivePage(int pageIndex)
    {
        var next=Math.Clamp(pageIndex,0,Math.Max(0,PageCount-1));
        if(activePage==next)return;
        activePage=next;
        InvalidateArrange();InvalidateVisual();
        ActivePageChanged?.Invoke(this,EventArgs.Empty);
    }

    int PageForSourceY(double y)
    {
        var low=0;var high=effectiveStarts.Count-1;
        while(low<high)
        {
            var middle=(low+high+1)/2;
            if(y+0.1>=effectiveStarts[middle])low=middle;else high=middle-1;
        }
        return low;
    }

    int PageForProjectedY(double y)=>Math.Clamp((int)Math.Floor(Math.Max(0,y)/(HeightOfPaper+Gap)),0,PageCount-1);

    public int GetPageIndex(Point projected)=>IsPaged?PageForProjectedY(projected.Y):0;

    public Point GetPageBodyPoint(Point projected)
    {
        var page=GetPageIndex(projected);
        return new Point(projected.X-Margins.Left,
            projected.Y-(IsPaged?page*(HeightOfPaper+Gap):0)-Margins.Top);
    }

    public void InvalidateFloatingPictures()
    {
        behindPictures.InvalidateVisual();frontPictures.InvalidateVisual();
    }

    public Point SourceToProjected(Point source)
    {
        var page=IsPaged?PageForSourceY(source.Y):0;
        return new Point(source.X+Margins.Left,source.Y-(IsPaged?effectiveStarts[page]:0)+
            (IsPaged?page*(HeightOfPaper+Gap):0)+Margins.Top);
    }

    public Point ProjectedToSource(Point projected)
    {
        var page=IsPaged?PageForProjectedY(projected.Y):0;
        var y=projected.Y-(IsPaged?page*(HeightOfPaper+Gap):0)-Margins.Top;
        if(IsPaged)y=Math.Clamp(y,0,Math.Max(0,SegmentHeight(page)-0.1));
        return new Point(Math.Clamp(projected.X-Margins.Left,0,BodyWidth),y+(IsPaged?effectiveStarts[page]:0));
    }

    Rect SourceCharacterRect(TextPointer pointer)
    {
        if(Editor==null || EditorSurface==null)return Rect.Empty;
        var rect=pointer.GetCharacterRect(LogicalDirection.Forward);
        if(rect.IsEmpty)return rect;
        var origin=Editor.TranslatePoint(rect.TopLeft,EditorSurface);
        return new Rect(origin,rect.Size);
    }

    public Rect GetProjectedCharacterRect(TextPointer pointer)
    {
        try
        {
            var rect=SourceCharacterRect(pointer);
            return rect.IsEmpty?rect:new Rect(SourceToProjected(rect.TopLeft),rect.Size);
        }
        catch(InvalidOperationException){return Rect.Empty;}
    }

    public void ActivatePageForCaret()
    {
        if(Editor==null || !IsPaged || Editor.IsMouseCaptured && Mouse.LeftButton==MouseButtonState.Pressed)return;
        RebuildEffectiveStarts(true);
        try
        {
            var rect=SourceCharacterRect(Editor.CaretPosition);
            if(!rect.IsEmpty)SetActivePage(PageForSourceY(rect.Top));
        }
        catch(InvalidOperationException){ }
    }

    public void ScrollCaretIntoView()
    {
        if(Editor==null || Editor.IsMouseCaptured && Mouse.LeftButton==MouseButtonState.Pressed)return;
        ActivatePageForCaret();
        var rect=GetProjectedCharacterRect(Editor.CaretPosition);
        var scroll=outerScroll??=FindOuterScroll();
        if(scroll==null || rect.IsEmpty)return;
        try
        {
            var top=TranslatePoint(rect.TopLeft,scroll);
            var bottom=TranslatePoint(rect.BottomRight,scroll);
            if(top.Y<8)scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+top.Y-8));
            else if(bottom.Y>scroll.ViewportHeight-8)
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset+bottom.Y-scroll.ViewportHeight+8);
        }
        catch(InvalidOperationException){ }
    }

    void EditorTextChanged(object sender,TextChangedEventArgs e)
    {
        measuredLineSplits.Clear();
        InvalidateMeasure();InvalidateVisual();QueueCaretRefresh(true);
    }

    void EditorSelectionChanged(object sender,RoutedEventArgs e)=>QueueCaretRefresh(true);

    void EditorPreviewKeyDown(object sender,KeyEventArgs e)
    {
        // A navigation key at the document edge can leave the selection
        // unchanged. It must still return to the caret after manual browsing.
        if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or
           Key.PageUp or Key.PageDown or Key.Back or Key.Delete or Key.Enter or Key.Tab)
            QueueCaretRefresh(true);
    }

    void QueueCaretRefresh(bool revealCaret=false)
    {
        revealCaretPending|=revealCaret;
        if(queuedCaret is {Status:DispatcherOperationStatus.Pending})return;
        queuedCaret=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            queuedCaret=null;
            var reveal=revealCaretPending;
            revealCaretPending=false;
            if(!IsLoaded || Editor==null)return;
            ActivatePageForCaret();
            if(reveal && IsPaged && Editor.IsKeyboardFocusWithin && Mouse.LeftButton!=MouseButtonState.Pressed)
                ScrollCaretIntoView();
            InvalidateVisual();
        }));
    }

    void BringIntoViewRequested(object sender,RequestBringIntoViewEventArgs e)
    {
        if(!IsPaged || Editor==null)return;
        // WPF also raises this when layout or scrolling moves the live
        // RichTextBox viewport. Those requests must not override browsing.
        // Text, selection and keyboard events explicitly request caret reveal.
        e.Handled=true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if(!IsPaged || Editor==null || EditorSurface==null || e.Handled)return;
        var point=e.GetPosition(this);
        var page=PageForProjectedY(point.Y);
        if(page==activePage && nativeBounds.Contains(point))return;
        SetActivePage(page);UpdateLayout();
        ActivatedPageMouseDown?.Invoke(this,e);
        if(e.Handled)return;
        var pointer=PointerAt(point);
        if(pointer==null)return;
        var anchor=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?SelectionAnchor():pointer;
        Editor.Focus();Editor.Selection.Select(anchor,pointer);
        dragAnchor=anchor;dragDocument=Editor.Document;
        manualDrag=Editor.CaptureMouse();
        e.Handled=true;
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        if(!IsPaged || Editor==null || EditorSurface==null || e.Handled)return;
        var point=e.GetPosition(this);
        var page=PageForProjectedY(point.Y);
        if(page==activePage && nativeBounds.Contains(point))return;
        SetActivePage(page);UpdateLayout();
        ActivatedPageMouseDown?.Invoke(this,e);
        if(e.Handled)return;
        if(PointerAt(point) is { } pointer)
        {
            if(Editor.Selection.IsEmpty)Editor.CaretPosition=pointer;
            Editor.Focus();
        }
        e.Handled=true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if(!IsPaged || Editor==null || e.LeftButton!=MouseButtonState.Pressed || !Editor.IsMouseCaptured)return;
        var point=e.GetPosition(this);
        SetActivePage(PageForProjectedY(point.Y));
        if(!IsArrangeValid)UpdateLayout();
        if(!manualDrag)return;
        if(!ReferenceEquals(dragDocument,Editor.Document) || dragAnchor==null){EndManualDrag();return;}
        if(PointerAt(point) is { } pointer)Editor.Selection.Select(dragAnchor,pointer);
        e.Handled=true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if(!manualDrag)return;
        EndManualDrag();e.Handled=true;
    }

    TextPointer SelectionAnchor()
    {
        var selection=Editor!.Selection;
        return Editor.CaretPosition.CompareTo(selection.Start)==0?selection.End:selection.Start;
    }

    TextPointer? PointerAt(Point projected)
    {
        if(Editor==null || EditorSurface==null)return null;
        try{return Editor.GetPositionFromPoint(EditorSurface.TranslatePoint(ProjectedToSource(projected),Editor),true);}
        catch(InvalidOperationException){return null;}
    }

    void EndManualDrag()
    {
        if(manualDrag && Editor?.IsMouseCaptured==true)Editor.ReleaseMouseCapture();
        manualDrag=false;dragAnchor=null;dragDocument=null;
    }

    ScrollViewer? FindOuterScroll()
    {
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is ScrollViewer scroll)return scroll;
        return null;
    }

    void DetachOuterScroll()
    {
        if(outerScroll==null)return;
        outerScroll.ScrollChanged-=OuterScrollChanged;
        outerScroll.PreviewMouseWheel-=OuterScrollPreviewMouseWheel;
        outerScroll.PreviewMouseDown-=OuterScrollPreviewMouseDown;
    }

    void OuterScrollPreviewMouseWheel(object sender,MouseWheelEventArgs e)
    {
        revealCaretPending=false;
        if(!IsPaged || e.Handled || outerScroll==null ||
           (Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0)return;
        // The bottomless RichTextBox still has an internal ScrollViewer even
        // with its scrollbars disabled. Forward the unmodified wheel directly
        // to the outer viewer before that child can consume it. Its normal
        // wheel handling honours Windows' line count and page-scroll setting.
        // Raise only the bubbling event, so this preview handler runs once.
        e.Handled=true;
        outerScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta)
        {
            RoutedEvent=Mouse.MouseWheelEvent,
            Source=outerScroll
        });
    }

    void OuterScrollPreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        // Scrollbar input is outside this panel's own routed-event path.
        // Cancel a pending edit reveal when the user chooses to browse first.
        for(var source=e.OriginalSource as DependencyObject;source!=null && source!=outerScroll;
            source=source is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(source):null)
            if(source is ScrollBar){revealCaretPending=false;break;}
    }

    void OuterScrollChanged(object sender,ScrollChangedEventArgs e)
    {
        if(ReferenceEquals(e.OriginalSource,outerScroll))InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(ActualWidth<1 || ActualHeight<1)return;
        dc.DrawRectangle(IsPaged?BackgroundBrush:Brushes.Transparent,null,new Rect(RenderSize));
        var range=VisiblePages();
        for(var page=range.First;page<=range.Last;page++)DrawPage(dc,page);
        // The native viewport is a later visual child. Keep the two picture
        // planes and the inactive text plane independent of its text clip.
        behindPictures.InvalidateVisual();projectedText.InvalidateVisual();frontPictures.InvalidateVisual();
    }

    (int First,int Last) VisiblePages()
    {
        var first=0;var last=PageCount-1;
        var scroll=outerScroll??=FindOuterScroll();
        if(IsPaged && scroll!=null)
        {
            try
            {
                var top=scroll.TranslatePoint(new Point(0,0),this).Y;
                var bottom=scroll.TranslatePoint(new Point(0,scroll.ViewportHeight),this).Y;
                first=Math.Max(0,PageForProjectedY(top)-1);
                last=Math.Min(PageCount-1,PageForProjectedY(bottom)+1);
            }
            catch(InvalidOperationException){ }
        }
        return (first,last);
    }

    static readonly Brush BackgroundBrush=MakeBrush(Color.FromRgb(239,236,226));
    static readonly Pen PaperPen=MakePen(Color.FromRgb(190,188,181));
    static Brush MakeBrush(Color colour){var value=new SolidColorBrush(colour);value.Freeze();return value;}
    static Pen MakePen(Color colour){var value=new Pen(MakeBrush(colour),1);value.Freeze();return value;}

    void DrawPage(DrawingContext dc,int page)
    {
        var bounds=GetPageBounds(page);
        dc.DrawRectangle(Brushes.White,IsPaged?PaperPen:null,bounds);
        if(HeaderFooterProvider is { } provider)
        {
            var labels=provider(page+1,PageCount);
            DrawTriplet(dc,labels.Header,42,bounds.Top+18,WidthOfPaper-84);
            DrawTriplet(dc,labels.Footer,42,bounds.Bottom-38,WidthOfPaper-84);
        }
    }

    void DrawProjectedText(DrawingContext dc,int page)
    {
        if(EditorSurface!=null && IsPaged && page!=activePage)
        {
            var bounds=GetPageBounds(page);
            var height=SegmentHeight(page);
            if(height>0)
            {
                if(!brushes.TryGetValue(page,out var brush))
                    brushes[page]=brush=new VisualBrush(EditorSurface){ViewboxUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill,AutoLayoutContent=false};
                brush.Viewbox=new Rect(0,effectiveStarts[page],BodyWidth,height);
                dc.DrawRectangle(brush,null,new Rect(Margins.Left,bounds.Top+Margins.Top,BodyWidth,height));
            }
        }
    }

    void DrawPicturePlane(DrawingContext dc,int page,bool front)
    {
        if(!IsPaged || FloatingPicturesProvider==null)return;
        var bounds=GetPageBounds(page);
        var origin=new Point(Margins.Left,bounds.Top+Margins.Top);
        dc.PushClip(new RectangleGeometry(new Rect(origin,new Size(BodyWidth,BodyHeight))));
        foreach(var picture in FloatingPicturesProvider(page))
        {
            var local=picture.Bounds;
            if(local.IsEmpty || !double.IsFinite(local.Left) || !double.IsFinite(local.Top) ||
               !double.IsFinite(local.Width) || !double.IsFinite(local.Height) || local.Width<=0 || local.Height<=0)continue;
            var target=new Rect(origin.X+local.Left,origin.Y+local.Top,local.Width,local.Height);
            if(picture.Mode==(front?PicturePlacementMode.InFrontOfText:PicturePlacementMode.BehindText))
                dc.DrawImage(picture.Source,target);
            if(front && picture.Selected)
            {
                dc.DrawRectangle(null,SelectionPen,target);
                dc.DrawRectangle(Brushes.White,SelectionPen,new Rect(target.Right-5,target.Bottom-5,10,10));
            }
        }
        dc.Pop();
    }

    static readonly Pen SelectionPen=MakePen(Color.FromRgb(70,130,180));

    void DrawProjectionLayer(DrawingContext dc,int plane)
    {
        if(!IsPaged)return;
        var range=VisiblePages();
        for(var page=range.First;page<=range.Last;page++)
            if(plane==1)DrawProjectedText(dc,page);else DrawPicturePlane(dc,page,plane==2);
    }

    static void DrawTriplet(DrawingContext dc,string? text,double x,double y,double width)
    {
        var parts=(text??"").Split('\t');
        if(parts.Length<2){DrawText(dc,text,x,y,width,TextAlignment.Center);return;}
        for(var i=0;i<3;i++)DrawText(dc,parts.ElementAtOrDefault(i)??"",x+width/3*i,y,width/3,
            i==0?TextAlignment.Left:i==1?TextAlignment.Center:TextAlignment.Right);
    }

    static void DrawText(DrawingContext dc,string? text,double x,double y,double width,TextAlignment alignment)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        var formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
            new Typeface("Calibri"),10*96/72,Brushes.DimGray,1)
        {MaxTextWidth=Math.Max(20,width),TextAlignment=alignment,Trimming=TextTrimming.CharacterEllipsis};
        dc.DrawText(formatted,new Point(x,y));
    }

    sealed class NativeViewport : AdornerDecorator
    {
        double sourceStartY;
        public double SourceStartY
        {
            get=>sourceStartY;
            set
            {
                if(Math.Abs(sourceStartY-value)<0.01)return;
                sourceStartY=value;
                InvalidateArrange();
            }
        }
        public Size NativeSize {get;private set;}
        protected override Size MeasureOverride(Size availableSize)
        {
            NativeSize=base.MeasureOverride(new Size(availableSize.Width,double.PositiveInfinity));
            // The host owns the full document extent. This child viewport must
            // request only its page slot, otherwise ArrangeCore may enlarge it
            // to the complete bottomless document before applying our clip.
            return new Size(NativeSize.Width,0);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(0,-SourceStartY,finalSize.Width,Math.Max(NativeSize.Height,SourceStartY+finalSize.Height)));
            AdornerLayer.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    sealed class ProjectionLayer : FrameworkElement
    {
        readonly PagedEditorHost owner;
        readonly int plane;
        public ProjectionLayer(PagedEditorHost owner,int plane)
        {
            this.owner=owner;this.plane=plane;IsHitTestVisible=false;
        }
        protected override void OnRender(DrawingContext dc)=>owner.DrawProjectionLayer(dc,plane);
    }
}

