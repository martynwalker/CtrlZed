using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    sealed record NavigationPage(int Number,BitmapSource? Thumbnail)
    {
        public string Label=>$"Page {Number}";
        public double Width=>Thumbnail?.PixelWidth??127;
        public double Height=>Thumbnail?.PixelHeight??180;
    }

    PaginationMapResult? displayedNavigationMap;
    FlowDocument? displayedNavigationDocument;

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
        if(!PageNavigationList.IsEnabled)ScheduleBeta35PaginationMap(120);
    }

    void UpdatePageNavigation()
    {
        if(PageNavigationList==null)return;
        if(!ReferenceEquals(displayedNavigationDocument,Editor.Document))
        {
            displayedNavigationDocument=Editor.Document;
            displayedNavigationMap=null;
            PageNavigationList.ItemsSource=null;
        }
        if(!PageThumbnailsRequested)return;

        var map=beta35PaginationMap;
        var ready=!beta35PaginationDirty && map!=null &&
            ReferenceEquals(beta35PaginationDocument,Editor.Document) && map.Thumbnails.Count==map.PageCount;
        PageNavigationList.IsEnabled=ready;
        if(!ready)
        {
            const string updating="Updating page thumbnails…";
            if(PageNavigationStatus.Text!=updating)PageNavigationStatus.Text=updating;
            PageNavigationStatus.Visibility=Visibility.Visible;
            return;
        }
        PageNavigationStatus.Visibility=Visibility.Collapsed;
        if(ReferenceEquals(displayedNavigationMap,map))return;

        displayedNavigationMap=map;
        PageNavigationList.ItemsSource=map!.Thumbnails
            .Select((bitmap,index)=>new NavigationPage(index+1,bitmap)).ToList();
    }

    void ReportPageNavigationFailure()
    {
        if(!PageThumbnailsRequested)return;
        PageNavigationStatus.Text="Page thumbnails unavailable. Reopen the Pages tab to retry.";
        PageNavigationStatus.Visibility=Visibility.Visible;
    }

    void PageNavigationList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(PageNavigationList.SelectedItem is NavigationPage page)NavigateFromThumbnail(page);
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
        if(PageNavigationList.IsEnabled && !beta35PaginationDirty && displayedNavigationMap!=null &&
           FindBeta35PageStart(displayedNavigationMap,page.Number) is TextPointer target)
            NavigateToBeta35TextPointer(target);
    }
}

