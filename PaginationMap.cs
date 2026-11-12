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
using System.Windows.Threading;

namespace CtrlZed;

public sealed record PaginationMapResult(
    string CacheKey,
    int PageCount,
    IReadOnlyList<int> PageStartOffsets,
    DateTime CreatedUtc);

public static class PaginationMapBuilder
{
    public static string CreateCacheKey(
        byte[] rtf,double pageWidth,double pageHeight,Thickness margins)
    {
        var contentHash=Convert.ToHexString(SHA256.HashData(rtf));
        var layout=string.Create(
            CultureInfo.InvariantCulture,
            $"{pageWidth:F3}|{pageHeight:F3}|{margins.Left:F3}|{margins.Top:F3}|{margins.Right:F3}|{margins.Bottom:F3}");
        return contentHash+"|"+layout;
    }

    public static Task<PaginationMapResult> BuildAsync(
        byte[] rtf,double pageWidth,double pageHeight,Thickness margins,
        string cacheKey,CancellationToken cancellationToken)
    {
        var completion=new TaskCompletionSource<PaginationMapResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread=new Thread(() =>
        {
            try
            {
                completion.TrySetResult(BuildCore(
                    rtf,pageWidth,pageHeight,margins,cacheKey,cancellationToken));
            }
            catch(OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch(Exception ex)
            {
                completion.TrySetException(ex);
            }
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
        string cacheKey,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document=new FlowDocument();
        using(var stream=new MemoryStream(rtf,false))
            new TextRange(document.ContentStart,document.ContentEnd)
                .Load(stream,DataFormats.Rtf);

        document.PageWidth=Math.Max(240,pageWidth);
        document.PageHeight=Math.Max(320,pageHeight);
        document.PagePadding=margins;
        document.ColumnWidth=double.PositiveInfinity;
        document.ColumnGap=0;

        var paginator=((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.PageSize=new Size(document.PageWidth,document.PageHeight);
        paginator.ComputePageCount();
        cancellationToken.ThrowIfCancellationRequested();

        var pageCount=Math.Max(1,paginator.PageCount);
        var totalOffset=Math.Max(0,
            document.ContentStart.GetOffsetToPosition(document.ContentEnd));
        var starts=new List<int>(pageCount){0};
        var dynamicPaginator=paginator as DynamicDocumentPaginator;

        for(var pageNumber=1;pageNumber<pageCount;pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset=-1;
            if(dynamicPaginator!=null)
            {
                var page=paginator.GetPage(pageNumber);
                if(page!=DocumentPage.Missing &&
                   dynamicPaginator.GetPagePosition(page) is TextPointer pointer)
                    offset=document.ContentStart.GetOffsetToPosition(pointer);
            }

            if(offset<0)
                offset=(int)Math.Round(totalOffset*pageNumber/(double)pageCount);
            offset=Math.Clamp(offset,starts[^1],totalOffset);
            starts.Add(offset);
        }

        return new PaginationMapResult(
            cacheKey,pageCount,starts,DateTime.UtcNow);
    }
}

public partial class MainWindow
{
    bool beta35PaginationInstalled;
    DispatcherTimer? beta35PaginationTimer;
    CancellationTokenSource? beta35PaginationCancellation;
    PaginationMapResult? beta35PaginationMap;
    string beta35PaginationCacheKey="";
    int beta35PaginationGeneration;
    TextBlock? beta35PageStatus;

    internal void InstallBeta35PaginationMap()
    {
        if(beta35PaginationInstalled)return;
        beta35PaginationInstalled=true;

        beta35PaginationTimer=new DispatcherTimer
        {
            Interval=TimeSpan.FromMilliseconds(850)
        };
        beta35PaginationTimer.Tick+=async (_,__)=>
        {
            beta35PaginationTimer.Stop();
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
        Editor.SelectionChanged+=(_,__)=>UpdateBeta35PageStatus();
        PageBorder.SizeChanged+=(_,__)=>ScheduleBeta35PaginationMap();

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
        if(CurrentEditorViewMode==EditorViewMode.Paged)
            ScheduleBeta35PaginationMap(120);
        else
        {
            beta35PaginationTimer?.Stop();
            beta35PaginationCancellation?.Cancel();
            if(beta35PageStatus!=null)beta35PageStatus.Text="Continuous view";
        }
    }

    void ScheduleBeta35PaginationMap(int delayMilliseconds=850)
    {
        if(!beta35PaginationInstalled || beta35PaginationTimer==null ||
           CurrentEditorViewMode!=EditorViewMode.Paged)return;

        beta35PaginationCancellation?.Cancel();
        beta35PaginationGeneration++;
        beta35PaginationTimer.Stop();
        beta35PaginationTimer.Interval=TimeSpan.FromMilliseconds(
            Math.Clamp(delayMilliseconds,100,2500));
        beta35PaginationTimer.Start();
        if(beta35PageStatus!=null)beta35PageStatus.Text="Page map updating…";
    }

    async Task RebuildBeta35PaginationMapAsync()
    {
        if(CurrentEditorViewMode!=EditorViewMode.Paged)return;

        byte[] rtf;
        try
        {
            using var stream=new MemoryStream();
            new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd)
                .Save(stream,DataFormats.Rtf);
            rtf=stream.ToArray();
        }
        catch(Exception ex)
        {
            if(beta35PageStatus!=null)beta35PageStatus.Text="Page map unavailable";
            Status.Text="Pagination snapshot failed: "+ex.Message;
            return;
        }

        var key=PaginationMapBuilder.CreateCacheKey(
            rtf,pageWidth,pageHeight,pageMargins);
        if(beta35PaginationMap!=null && key==beta35PaginationCacheKey)
        {
            ApplyBeta35PaginationMap(beta35PaginationMap);
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
                rtf,pageWidth,pageHeight,pageMargins,key,token);
            if(token.IsCancellationRequested || generation!=beta35PaginationGeneration ||
               CurrentEditorViewMode!=EditorViewMode.Paged)return;

            beta35PaginationMap=map;
            beta35PaginationCacheKey=key;
            ApplyBeta35PaginationMap(map);
        }
        catch(OperationCanceledException){ }
        catch(Exception ex)
        {
            if(beta35PageStatus!=null)beta35PageStatus.Text="Page map unavailable";
            Status.Text="Pagination map failed: "+ex.Message;
        }
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

    void UpdateBeta35PageStatus()
    {
        if(beta35PageStatus==null)return;
        if(CurrentEditorViewMode!=EditorViewMode.Paged)
        {
            beta35PageStatus.Text="Continuous view";
            return;
        }
        var map=beta35PaginationMap;
        if(map==null)
        {
            beta35PageStatus.Text="Page map updating…";
            return;
        }

        beta35PageStatus.Text=$"Page {CurrentBeta35PageNumber(map)} of {map.PageCount}";
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

            var documentLength=Editor.Document.ContentStart.GetOffsetToPosition(
                Editor.Document.ContentEnd);
            var offset=Math.Clamp(map.PageStartOffsets[requested-1],0,documentLength);
            var target=Editor.Document.ContentStart.GetPositionAtOffset(
                offset,LogicalDirection.Forward);
            if(target==null)
            {
                MessageBox.Show(w,"CtrlZed could not locate the start of that page.",
                    "Go to Page",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }

            Editor.Selection.Select(target,target);
            Editor.CaretPosition=target;
            Editor.Focus();
            ScrollBeta35TextPointerIntoView(target);
            UpdateBeta35PageStatus();
            w.DialogResult=true;
        };
        cancel.Click+=(_,__)=>w.DialogResult=false;
        buttons.Children.Add(go);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        w.Content=root;
        w.Loaded+=(_,__)=>{input.Focus();input.SelectAll();};
        w.ShowDialog();
    }

    void GoToPage_Click(object sender,RoutedEventArgs e)
        =>ShowBeta35GoToPageDialog();

    void ScrollBeta35TextPointerIntoView(TextPointer target)
    {
        try
        {
            target.BringIntoView();
            Editor.UpdateLayout();
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
