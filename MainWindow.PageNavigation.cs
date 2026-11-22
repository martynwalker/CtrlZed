using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    sealed record NavigationPage(int Number,BitmapSource? Thumbnail,TextPointer? Anchor)
    {
        public string Label=>$"Page {Number}";
        public double Width=>Thumbnail?.PixelWidth??127;
        public double Height=>Thumbnail?.PixelHeight??180;
    }

    PaginationMapResult? displayedNavigationMap;
    FlowDocument? displayedNavigationDocument;
    string displayedNavigationDecorationKey="";
    bool updatingPageNavigation;
    DispatcherOperation? queuedPageNavigationRefresh;

    bool PageThumbnailsRequested=>NavigationPane?.Visibility==Visibility.Visible &&
        NavigationPagesTab!=null && NavigationTabs?.SelectedItem==NavigationPagesTab;

    void NavigationTabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ReferenceEquals(e.Source,NavigationTabs) || !IsLoaded)return;
        if(!PageThumbnailsRequested && NavigationPane.Visibility==Visibility.Visible)RefreshNavigation();
        UpdatePageNavigationDemand();
    }

    void UpdatePageNavigationDemand()
    {
        if(!PageThumbnailsRequested)
        {
            if(CurrentEditorViewMode==EditorViewMode.Continuous)
            {
                beta35PaginationTimer?.Stop();
                beta35PaginationCancellation?.Cancel();
            }
            return;
        }

        UpdatePageNavigation();
        if(beta35PaginationDirty || beta35PaginationMap==null ||
           beta35PaginationMap.Thumbnails.Count!=beta35PaginationMap.PageCount)
            ScheduleBeta35PaginationMap(120);
    }

    void QueuePageNavigationRefresh()
    {
        if(!PageThumbnailsRequested || queuedPageNavigationRefresh is {Status:DispatcherOperationStatus.Pending})return;
        queuedPageNavigationRefresh=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            queuedPageNavigationRefresh=null;
            if(IsLoaded && !IsMenuInteractionActive)UpdatePageNavigation();
        }));
    }

    void UpdatePageNavigation()
    {
        if(PageNavigationList==null)return;
        if(!ReferenceEquals(displayedNavigationDocument,Editor.Document))
        {
            displayedNavigationDocument=Editor.Document;
            displayedNavigationMap=null;
            displayedNavigationDecorationKey="";
            PageNavigationList.ItemsSource=null;
        }
        if(!PageThumbnailsRequested)return;

        var map=beta35PaginationMap;
        var ready=!beta35PaginationDirty && map!=null &&
            ReferenceEquals(beta35PaginationDocument,Editor.Document) && map.Thumbnails.Count==map.PageCount;
        // Keep the last useful page list interactive during idle pagination.
        // Its live anchors follow edits; disabling it on every layout update
        // made a page click appear to be ignored while pictures were settling.
        PageNavigationList.IsEnabled=ready || PageNavigationList.Items.Count>0;
        if(!ready)
        {
            const string updating="Updating page thumbnails…";
            if(PageNavigationStatus.Text!=updating)PageNavigationStatus.Text=updating;
            PageNavigationStatus.Visibility=Visibility.Visible;
            return;
        }
        if(Beta46PagedPicturesActive && beta46PagedGeometryDirty)return;
        PageNavigationStatus.Visibility=Visibility.Collapsed;
        var pictures=CreateFloatingPageImages(new Size(pageWidth,pageHeight));
        var labels=Enumerable.Range(1,map!.PageCount).Select(page=>(
            Header:ExpandFields(headerText,page,map.PageCount),Footer:ExpandFields(footerText,page,map.PageCount))).ToArray();
        var key=new StringBuilder();
        foreach(var picture in pictures)
            key.Append(picture.PageNumber).Append('|').Append((int)picture.Mode).Append('|')
                .Append(RuntimeHelpers.GetHashCode(picture.Source)).Append('|')
                .Append(picture.Bounds.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach(var label in labels)key.Append(label.Header.Length).Append(':').Append(label.Header)
            .Append(label.Footer.Length).Append(':').Append(label.Footer);
        var decorationKey=key.ToString();
        if(ReferenceEquals(displayedNavigationMap,map) && displayedNavigationDecorationKey==decorationKey)return;

        var selected=PageNavigationList.SelectedIndex;
        updatingPageNavigation=true;
        try
        {
            displayedNavigationMap=map;
            displayedNavigationDecorationKey=decorationKey;
            PageNavigationList.ItemsSource=map.Thumbnails.Select((bitmap,index)=>new NavigationPage(index+1,
                ComposeNavigationThumbnail(bitmap,index,pictures,labels[index]),FindBeta35PageStart(map,index+1))).ToList();
            // Restoring a selection after a refresh is not a navigation action.
            PageNavigationList.SelectedIndex=Math.Min(selected,PageNavigationList.Items.Count-1);
        }
        finally {updatingPageNavigation=false;}
    }

    BitmapSource? ComposeNavigationThumbnail(BitmapSource? text,int page,IReadOnlyList<FloatingPageImage> pictures,
        (string Header,string Footer) labels)
    {
        if(text==null)return null;
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(text.PixelWidth/pageWidth,text.PixelHeight/pageHeight));
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,pageWidth,pageHeight));
            var body=new Rect(pageMargins.Left,pageMargins.Top,
                Math.Max(1,pageWidth-pageMargins.Left-pageMargins.Right),Math.Max(1,pageHeight-pageMargins.Top-pageMargins.Bottom));
            dc.PushClip(new RectangleGeometry(body));
            foreach(var picture in pictures.Where(item=>item.PageNumber==page && item.Mode==PicturePlacementMode.BehindText))
                dc.DrawImage(picture.Source,picture.Bounds);
            dc.Pop();
            dc.DrawImage(text,new Rect(0,0,pageWidth,pageHeight));
            dc.PushClip(new RectangleGeometry(body));
            foreach(var picture in pictures.Where(item=>item.PageNumber==page && item.Mode==PicturePlacementMode.InFrontOfText))
                dc.DrawImage(picture.Source,picture.Bounds);
            dc.Pop();
            DrawNavigationTriplet(dc,labels.Header,18);
            DrawNavigationTriplet(dc,labels.Footer,Math.Max(18,pageHeight-38));
            dc.Pop();
        }
        var bitmap=new RenderTargetBitmap(text.PixelWidth,text.PixelHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    void DrawNavigationTriplet(DrawingContext dc,string text,double y)
    {
        var parts=(text??"").Split('\t');
        var width=Math.Max(20,pageWidth-84);
        for(var index=0;index<(parts.Length<2?1:3);index++)
        {
            var value=parts.ElementAtOrDefault(index);
            if(string.IsNullOrWhiteSpace(value))continue;
            var third=parts.Length<2?width:width/3;
            var formatted=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface("Calibri"),10*96/72,Brushes.DimGray,1)
            {MaxTextWidth=third,TextAlignment=parts.Length<2?TextAlignment.Center:
                index==0?TextAlignment.Left:index==1?TextAlignment.Center:TextAlignment.Right,
                Trimming=TextTrimming.CharacterEllipsis};
            dc.DrawText(formatted,new Point(42+third*index,y));
        }
    }

    void ReportPageNavigationFailure()
    {
        if(!PageThumbnailsRequested)return;
        PageNavigationStatus.Text="Page thumbnails unavailable. Reopen the Pages tab to retry.";
        PageNavigationStatus.Visibility=Visibility.Visible;
    }

    void PageNavigationList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!updatingPageNavigation && PageNavigationList.SelectedItem is NavigationPage page)NavigateFromThumbnail(page);
    }

    void PageNavigationList_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is not DependencyObject source ||
           ItemsControl.ContainerFromElement(PageNavigationList,source) is not ListBoxItem { IsSelected:true, DataContext:NavigationPage page })return;
        NavigateFromThumbnail(page);
        e.Handled=true;
    }

    void NavigateFromThumbnail(NavigationPage page)
    {
        if(!PageNavigationList.IsEnabled || !ReferenceEquals(displayedNavigationDocument,Editor.Document))return;
        var target=page.Anchor;
        if(NavigateBeta46Page(page.Number-1,target))return;
        if(target!=null)NavigateToBeta35TextPointer(target);
    }
}
