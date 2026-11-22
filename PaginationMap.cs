using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CtrlZed;

public sealed record PaginationMapResult(
    string CacheKey,
    int PageCount,
    IReadOnlyList<int> PageStartOffsets,
    DateTime CreatedUtc)
{
    public IReadOnlyList<BitmapSource?> Thumbnails { get; init; }=Array.Empty<BitmapSource?>();
    // Safe paper-body origins. Precise glyph rectangles require a hosted WPF
    // text view and must not be queried from an unhosted worker paginator.
    public IReadOnlyList<double> PageFirstContentY { get; init; }=Array.Empty<double>();
}

public static class PaginationMapBuilder
{
    public static string CreateCacheKey(
        byte[] rtf,double pageWidth,double pageHeight,Thickness margins,
        PaginationLayoutSnapshot? layout=null)
    {
        var contentHash=Convert.ToHexString(SHA256.HashData(rtf));
        var dimensions=string.Create(
            CultureInfo.InvariantCulture,
            $"{pageWidth:F3}|{pageHeight:F3}|{margins.Left:F3}|{margins.Top:F3}|{margins.Right:F3}|{margins.Bottom:F3}");
        return contentHash+"|"+dimensions+(layout==null?"":"|"+layout.CacheDiscriminator);
    }

    public static Task<PaginationMapResult> BuildAsync(
        byte[] rtf,double pageWidth,double pageHeight,Thickness margins,
        string cacheKey,CancellationToken cancellationToken,bool includeThumbnails=false,
        PaginationLayoutSnapshot? layout=null)
    {
        var completion=new TaskCompletionSource<PaginationMapResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread=new Thread(() =>
        {
            Dispatcher? dispatcher=null;
            PaginationMapResult? result=null;
            Exception? failure=null;
            var cancelled=false;
            try
            {
                dispatcher=Dispatcher.CurrentDispatcher;
                result=BuildCore(
                    rtf,pageWidth,pageHeight,margins,cacheKey,cancellationToken,includeThumbnails,layout);
            }
            catch(OperationCanceledException)
            {
                cancelled=true;
            }
            catch(Exception ex)
            {
                failure=ex;
            }
            finally
            {
                // WPF owns native PTS contexts through this thread's
                // Dispatcher. ShutdownFinished disposes them on their owner
                // thread; simply exiting the STA leaves that cleanup to later
                // finalizers while other document work may already be running.
                try
                {
                    if(dispatcher!=null && !dispatcher.HasShutdownFinished)dispatcher.InvokeShutdown();
                }
                catch(Exception ex) { failure??=ex; }
            }

            // Return only frozen bitmaps and immutable data, and only after
            // the worker's native pagination resources have been released.
            if(failure!=null)completion.TrySetException(failure);
            else if(cancelled)completion.TrySetCanceled(cancellationToken);
            else completion.TrySetResult(result!);
        })
        {
            IsBackground=true,
            Name="CtrlZed pagination map"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static PaginationMapResult BuildCore(
        byte[] rtf,double pageWidth,double pageHeight,Thickness margins,
        string cacheKey,CancellationToken cancellationToken,bool includeThumbnails=false,
        PaginationLayoutSnapshot? layout=null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document=new FlowDocument();
        using(var stream=new MemoryStream(rtf,false))
            new TextRange(document.ContentStart,document.ContentEnd)
                .Load(stream,DataFormats.Rtf);

        layout?.Apply(document);
        var mapOffset=layout?.CreatePageOffsetMapper(document);

        document.PageWidth=Math.Max(240,pageWidth);
        document.PageHeight=Math.Max(320,pageHeight);
        document.PagePadding=margins;
        document.ColumnWidth=double.PositiveInfinity;
        document.ColumnGap=0;

        var paginator=((IDocumentPaginatorSource)document).DocumentPaginator;
        var dynamicPaginator=paginator as DynamicDocumentPaginator;
        if(dynamicPaginator!=null)dynamicPaginator.IsBackgroundPaginationEnabled=false;
        paginator.PageSize=new Size(document.PageWidth,document.PageHeight);
        paginator.ComputePageCount();
        cancellationToken.ThrowIfCancellationRequested();

        var pageCount=Math.Max(1,paginator.PageCount);
        var totalOffset=layout?.SourceSymbolLength??Math.Max(0,
            document.ContentStart.GetOffsetToPosition(document.ContentEnd));
        var starts=new List<int>(pageCount){0};
        var firstContentY=Enumerable.Repeat(margins.Top,pageCount).ToArray();

        var thumbnails=includeThumbnails?new BitmapSource?[pageCount]:Array.Empty<BitmapSource?>();
        for(var pageNumber=includeThumbnails?0:1;pageNumber<pageCount;pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page=dynamicPaginator!=null || includeThumbnails?paginator.GetPage(pageNumber):DocumentPage.Missing;
            try
            {
                if(includeThumbnails && page!=DocumentPage.Missing && page.Visual!=null)
                    thumbnails[pageNumber]=CreateThumbnail(page);
                if(pageNumber==0)continue;

                var offset=-1;
                if(dynamicPaginator!=null && page!=DocumentPage.Missing &&
                   dynamicPaginator.GetPagePosition(page) is TextPointer pointer)
                {
                    offset=mapOffset?.Invoke(pointer)??document.ContentStart.GetOffsetToPosition(pointer);
                }
                if(offset<0)
                    offset=(int)Math.Round(totalOffset*pageNumber/(double)pageCount);
                offset=Math.Clamp(offset,starts[^1],totalOffset);
                starts.Add(offset);
            }
            finally
            {
                // Nothing in the result references a live DocumentPage or
                // visual. Dispose on the same STA which created its PTS page.
                if(page!=DocumentPage.Missing)page.Dispose();
            }
        }

        return new PaginationMapResult(cacheKey,pageCount,starts,DateTime.UtcNow)
        {
            Thumbnails=thumbnails,
            PageFirstContentY=Array.AsReadOnly(firstContentY)
        };
    }

    static BitmapSource CreateThumbnail(DocumentPage page)
    {
        var scale=Math.Min(136/page.Size.Width,180/page.Size.Height);
        var width=Math.Max(1,(int)Math.Round(page.Size.Width*scale));
        var height=Math.Max(1,(int)Math.Round(page.Size.Height*scale));
        var visual=new DrawingVisual();
        var area=new Rect(0,0,width,height);
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White,null,area);
            var brush=new VisualBrush(page.Visual)
            {
                ViewboxUnits=BrushMappingMode.Absolute,
                Viewbox=new Rect(page.Size),
                Stretch=Stretch.Uniform,
                AutoLayoutContent=false
            };
            dc.DrawRectangle(brush,null,area);
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

public partial class MainWindow
{
    bool beta35PaginationInstalled;
    DispatcherTimer? beta35PaginationTimer;
    CancellationTokenSource? beta35PaginationCancellation;
    PaginationMapResult? beta35PaginationMap;
    FlowDocument? beta35PaginationDocument;
    bool beta35PaginationDirty=true;
    string beta35PaginationCacheKey="";
    int beta35PaginationGeneration;
    TextBlock? beta35PageStatus;
    bool beta35PageStatusQueued;
    DispatcherOperation? beta35PendingNavigationScroll;
    PaginationMapResult? beta35DeferredPaginationMap;
    FlowDocument? beta35DeferredPaginationDocument;

    internal void InstallBeta35PaginationMap()
    {
        if(beta35PaginationInstalled)return;
        beta35PaginationInstalled=true;

        beta35PaginationTimer=new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval=TimeSpan.FromMilliseconds(850)
        };
        beta35PaginationTimer.Tick+=async (_,__)=>
        {
            if(IsMenuInteractionActive)return;
            beta35PaginationTimer.Stop();
            if(beta35DeferredPaginationMap is { } map &&
               ReferenceEquals(beta35DeferredPaginationDocument,Editor.Document))
            {
                beta35DeferredPaginationMap=null;
                beta35DeferredPaginationDocument=null;
                CompleteBeta35PaginationMap(map,Editor.Document);
                return;
            }
            await RebuildBeta35PaginationMapAsync();
        };

        PreviewKeyDown+=(_,e)=>
        {
            if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.G)
            {
                e.Handled=true;
                ShowBeta35GoToPageDialog();
            }
        };

        Editor.TextChanged+=(_,__)=>ScheduleBeta35PaginationMap();
        Editor.SelectionChanged+=(_,__)=>QueueBeta35PageStatusUpdate();
        PageBorder.SizeChanged+=(_,__)=>ScheduleBeta35PaginationMap();
        Closed+=(_,__)=>
        {
            beta35PaginationTimer.Stop();
            beta35PaginationCancellation?.Cancel();
            beta35DeferredPaginationMap=null;
            beta35DeferredPaginationDocument=null;
        };

        var statusBar=FindBeta35Descendant<StatusBar>(this);
        if(statusBar!=null)
        {
            statusBar.Items.Add(new Separator());
            beta35PageStatus=new TextBlock
            {
                Text="Page 1 of 1",
                Cursor=System.Windows.Input.Cursors.Hand,
                ToolTip="Click to go to a page",
                Background=Brushes.Transparent,
                TextDecorations=TextDecorations.Underline
            };
            beta35PageStatus.MouseLeftButtonUp+=(_,e)=>
            {
                e.Handled=true;
                ShowBeta35GoToPageDialog();
            };
            statusBar.Items.Add(new StatusBarItem{Content=beta35PageStatus});
        }

        ScheduleBeta35PaginationMap(120);
    }

    internal void OnBeta35ViewModeChanged()
    {
        SetBeta35PageSurfaceVisibility();
        if(!beta35PaginationInstalled)return;
        if(CurrentEditorViewMode==EditorViewMode.Paged || PageThumbnailsRequested)
            ScheduleBeta35PaginationMap(120);
        else
        {
            beta35PaginationTimer?.Stop();
            beta35PaginationCancellation?.Cancel();
        }
        UpdateBeta35PageStatus();
    }

    void ScheduleBeta35PaginationMap(int delayMilliseconds=850)
    {
        if(!beta35PaginationInstalled || beta35PaginationTimer==null)return;

        beta35PaginationDirty=true;
        beta35DeferredPaginationMap=null;
        beta35DeferredPaginationDocument=null;
        if(!ReferenceEquals(beta35PaginationDocument,Editor.Document))
        {
            beta35PaginationMap=null;
            beta35PaginationDocument=null;
            beta35PaginationCacheKey="";
        }
        UpdatePageNavigation();
        OnBeta46PaginationPending();
        if(CurrentEditorViewMode!=EditorViewMode.Paged && !PageThumbnailsRequested)return;

        beta35PaginationCancellation?.Cancel();
        beta35PaginationGeneration++;
        beta35PaginationTimer.Stop();
        beta35PaginationTimer.Interval=TimeSpan.FromMilliseconds(
            Math.Clamp(delayMilliseconds,100,2500));
        beta35PaginationTimer.Start();
        if(beta35PageStatus!=null && CurrentEditorViewMode==EditorViewMode.Paged &&
           beta35PageStatus.Text!="Page map updating…")beta35PageStatus.Text="Page map updating…";
    }

    async Task RebuildBeta35PaginationMapAsync()
    {
        var includeThumbnails=PageThumbnailsRequested;
        if(CurrentEditorViewMode!=EditorViewMode.Paged && !includeThumbnails)return;
        var document=Editor.Document;

        byte[] rtf;
        PaginationLayoutSnapshot layout;
        try
        {
            layout=PaginationLayoutSnapshot.Capture(document);
            using var stream=new MemoryStream();
            new TextRange(document.ContentStart,document.ContentEnd)
                .Save(stream,DataFormats.Rtf);
            rtf=stream.ToArray();
        }
        catch(Exception ex)
        {
            ReportPageNavigationFailure();
            if(beta35PageStatus!=null && CurrentEditorViewMode==EditorViewMode.Paged)beta35PageStatus.Text="Page map unavailable";
            Status.Text="Pagination snapshot failed: "+ex.Message;
            return;
        }

        var key=PaginationMapBuilder.CreateCacheKey(
            rtf,pageWidth,pageHeight,pageMargins,layout);
        if(beta35PaginationMap!=null && key==beta35PaginationCacheKey &&
           (!includeThumbnails || beta35PaginationMap.Thumbnails.Count==beta35PaginationMap.PageCount))
        {
            CompleteBeta35PaginationMap(beta35PaginationMap,document);
            return;
        }

        beta35PaginationCancellation?.Cancel();
        beta35PaginationCancellation?.Dispose();
        beta35PaginationCancellation=new CancellationTokenSource();
        var token=beta35PaginationCancellation.Token;
        var generation=++beta35PaginationGeneration;

        try
        {
            var map=await PaginationMapBuilder.BuildAsync(
                rtf,pageWidth,pageHeight,pageMargins,key,token,includeThumbnails,layout);
            if(token.IsCancellationRequested || generation!=beta35PaginationGeneration ||
               !ReferenceEquals(document,Editor.Document))return;

            beta35PaginationCacheKey=key;
            CompleteBeta35PaginationMap(map,document);
        }
        catch(OperationCanceledException){ }
        catch(Exception ex)
        {
            if(token.IsCancellationRequested || generation!=beta35PaginationGeneration ||
               !ReferenceEquals(document,Editor.Document))return;
            ReportPageNavigationFailure();
            if(beta35PageStatus!=null && CurrentEditorViewMode==EditorViewMode.Paged)beta35PageStatus.Text="Page map unavailable";
            Status.Text="Pagination map failed: "+ex.Message;
        }
    }

    void CompleteBeta35PaginationMap(PaginationMapResult map,FlowDocument document)
    {
        if(IsMenuInteractionActive)
        {
            beta35DeferredPaginationMap=map;
            beta35DeferredPaginationDocument=document;
            beta35PaginationTimer?.Start();
            return;
        }
        beta35PaginationMap=map;
        beta35PaginationDocument=document;
        beta35PaginationDirty=false;
        if(CurrentEditorViewMode==EditorViewMode.Paged)ApplyBeta35PaginationMap(map);
        UpdatePageNavigation();
        OnBeta46PaginationMapApplied(map);
    }

    void ApplyBeta35PaginationMap(PaginationMapResult map)
    {
        var yStarts=new List<double>{0};
        foreach(var offset in map.PageStartOffsets.Skip(1))
        {
            var pointer=Editor.Document.ContentStart.GetPositionAtOffset(
                Math.Max(0,offset),LogicalDirection.Forward);
            if(pointer==null)continue;
            var rect=pointer.GetCharacterRect(LogicalDirection.Forward);
            if(rect.IsEmpty || !double.IsFinite(rect.Top))continue;
            var y=Math.Max(0,rect.Top);
            if(y>yStarts[^1]+1)yStarts.Add(y);
        }

        PageGuides.CachedPageCount=map.PageCount;
        PageGuides.CachedPageStartsY=yStarts;
        PageGuides.UseCachedPaginationMap=true;
        PageGuides.InvalidateVisual();
        UpdateBeta35PageSurface(map.PageCount,yStarts);
        UpdateDocumentStats();
        UpdateBeta35PageStatus();
    }

    void QueueBeta35PageStatusUpdate()
    {
        if(beta35PageStatusQueued)return;
        beta35PageStatusQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            beta35PageStatusQueued=false;
            UpdateBeta35PageStatus();
        }));
    }

    void UpdateBeta35PageStatus()
    {
        if(beta35PageStatus==null)return;
        var map=beta35PaginationMap;
        var text=CurrentEditorViewMode!=EditorViewMode.Paged
            ?"Continuous view"
            :map==null?"Page map updating…":$"Page {CurrentBeta35PageNumber(map)} of {map.PageCount}";
        if(beta35PageStatus.Text!=text)beta35PageStatus.Text=text;
    }

    int CurrentBeta35PageNumber(PaginationMapResult map)
    {
        var caretOffset=Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
        var page=1;
        for(var i=1;i<map.PageStartOffsets.Count;i++)
        {
            if(caretOffset<map.PageStartOffsets[i])break;
            page=i+1;
        }
        return Math.Clamp(page,1,map.PageCount);
    }

    void ShowBeta35GoToPageDialog()
    {
        if(CurrentEditorViewMode!=EditorViewMode.Paged)
        {
            MessageBox.Show(this,"Switch to Paged Editing before navigating by page.",
                "Go to Page",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var map=beta35PaginationMap;
        if(map==null)
        {
            MessageBox.Show(this,"The page map is still updating. Try again in a moment.",
                "Go to Page",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var w=new Window
        {
            Title="Go to Page",
            Owner=this,
            Width=360,
            Height=160,
            ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new StackPanel{Margin=new Thickness(14)};
        root.Children.Add(new TextBlock
        {
            Text=$"Enter a page number from 1 to {map.PageCount}:",
            Margin=new Thickness(0,0,0,8)
        });
        var input=new TextBox
        {
            Text=CurrentBeta35PageNumber(map).ToString(),
            Width=90,
            HorizontalAlignment=HorizontalAlignment.Left,
            Margin=new Thickness(0,0,0,12)
        };
        root.Children.Add(input);
        var buttons=new StackPanel
        {
            Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right
        };
        var go=new Button{Content="Go",Width=76,IsDefault=true,Margin=new Thickness(4)};
        var cancel=new Button{Content="Cancel",Width=76,IsCancel=true,Margin=new Thickness(4)};
        TextPointer? destination=null;
        go.Click+=(_,__)=>
        {
            if(!int.TryParse(input.Text,out var requested) ||
               requested<1 || requested>map.PageCount)
            {
                MessageBox.Show(w,$"Enter a whole number from 1 to {map.PageCount}.",
                    "Go to Page",MessageBoxButton.OK,MessageBoxImage.Warning);
                input.Focus();
                input.SelectAll();
                return;
            }

            var target=FindBeta35PageStart(map,requested);
            if(target==null)
            {
                MessageBox.Show(w,"CtrlZed could not locate the start of that page.",
                    "Go to Page",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }

            destination=target;
            w.DialogResult=true;
        };
        cancel.Click+=(_,__)=>w.DialogResult=false;
        buttons.Children.Add(go);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        w.Content=root;
        w.Loaded+=(_,__)=>{input.Focus();input.SelectAll();};
        if(w.ShowDialog()==true && destination!=null)NavigateToBeta35TextPointer(destination);
    }

    void GoToPage_Click(object sender,RoutedEventArgs e)
        =>ShowBeta35GoToPageDialog();

    TextPointer? FindBeta35PageStart(PaginationMapResult map,int pageNumber)
    {
        if(!ReferenceEquals(beta35PaginationDocument,Editor.Document) ||
           pageNumber<1 || pageNumber>map.PageStartOffsets.Count)return null;
        var start=Editor.Document.ContentStart;
        var length=start.GetOffsetToPosition(Editor.Document.ContentEnd);
        return start.GetPositionAtOffset(Math.Clamp(map.PageStartOffsets[pageNumber-1],0,length),LogicalDirection.Forward);
    }

    void NavigateToBeta35TextPointer(TextPointer target)
    {
        Editor.Focus();
        Editor.Selection.Select(target,target);
        var document=Editor.Document;
        var caret=Editor.CaretPosition;
        beta35PendingNavigationScroll?.Abort();
        beta35PendingNavigationScroll=Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            beta35PendingNavigationScroll=null;
            if(ReferenceEquals(document,Editor.Document) && caret.CompareTo(Editor.CaretPosition)==0)
                ScrollBeta35TextPointerIntoView(caret);
        }));
    }

    void ScrollBeta35TextPointerIntoView(TextPointer target)
    {
        if(ScrollBeta46TextPointerIntoView(target))return;
        try
        {
            var rect=target.GetCharacterRect(LogicalDirection.Forward);
            if(rect.IsEmpty || !double.IsFinite(rect.Top))return;

            var scroll=FindVisualAncestor<ScrollViewer>(PageBorder);
            if(scroll==null)
            {
                Editor.ScrollToVerticalOffset(Math.Max(0,rect.Top));
                return;
            }

            var point=Editor.TranslatePoint(new Point(0,rect.Top),scroll);
            scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+point.Y));
        }
        catch { }
    }

    static T? FindBeta35Descendant<T>(DependencyObject root) where T:DependencyObject
    {
        var count=VisualTreeHelper.GetChildrenCount(root);
        for(var i=0;i<count;i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match)return match;
            var nested=FindBeta35Descendant<T>(child);
            if(nested!=null)return nested;
        }
        return null;
    }
}

internal static class Beta35PaginationBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallBeta35PaginationMap),DispatcherPriority.ApplicationIdle);
    }
}

