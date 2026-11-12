using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    sealed class FloatingImagePlacementData
    {
        public int ImageIndex { get; set; }=-1;
        public int AnchorOffset { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        [JsonIgnore]
        public Image? RuntimeImage { get; set; }
    }

    sealed record FloatingImageHit(
        FloatingImagePlacementData Placement,
        Image Original,
        Image Overlay);

    readonly List<FloatingImagePlacementData> floatingImagePlacements=new();
    readonly Dictionary<FloatingImagePlacementData,Image> floatingImageOverlays=new();
    FloatingImagePlacementData? draggingFloatingImage;
    Point floatingDragStart;
    double floatingDragStartX,floatingDragStartY;
    bool floatingImagePlacementInstalled;

    internal void InstallFloatingImagePlacement()
    {
        if(floatingImagePlacementInstalled)return;
        floatingImagePlacementInstalled=true;

        Editor.PreviewMouseMove+=FloatingImage_PreviewMouseMove;
        Editor.PreviewMouseLeftButtonUp+=FloatingImage_PreviewMouseLeftButtonUp;
        Editor.TextChanged+=(_,__)=>Dispatcher.BeginInvoke(
            new Action(RefreshFloatingImagePlacements),DispatcherPriority.Loaded);
        Editor.SizeChanged+=(_,__)=>Dispatcher.BeginInvoke(
            new Action(RefreshFloatingImagePlacements),DispatcherPriority.Loaded);
        PageBorder.SizeChanged+=(_,__)=>Dispatcher.BeginInvoke(
            new Action(RefreshFloatingImagePlacements),DispatcherPriority.Loaded);

        Dispatcher.BeginInvoke(new Action(RefreshFloatingImagePlacements),DispatcherPriority.Loaded);
    }

    List<FloatingImagePlacementData> CaptureFloatingImagePlacements()
    {
        var images=EnumerateImageContainers(Editor.Document).ToList();
        var result=new List<FloatingImagePlacementData>();
        foreach(var placement in floatingImagePlacements)
        {
            var image=placement.RuntimeImage;
            if(image==null)continue;
            var index=images.FindIndex(x=>ReferenceEquals(x.Image,image));
            if(index<0)continue;
            placement.ImageIndex=index;
            placement.AnchorOffset=Editor.Document.ContentStart.GetOffsetToPosition(images[index].Container.ContentStart);
            result.Add(new FloatingImagePlacementData
            {
                ImageIndex=index,
                AnchorOffset=placement.AnchorOffset,
                OffsetX=placement.OffsetX,
                OffsetY=placement.OffsetY,
                Width=placement.Width,
                Height=placement.Height
            });
        }
        return result;
    }

    void RestoreFloatingImagePlacements(List<FloatingImagePlacementData>? placements)
    {
        floatingImagePlacements.Clear();
        floatingImageOverlays.Clear();
        BehindTextCanvas.Children.Clear();
        if(placements!=null)
        {
            foreach(var item in placements.Where(x=>x.ImageIndex>=0&&
                double.IsFinite(x.OffsetX)&&double.IsFinite(x.OffsetY)&&
                double.IsFinite(x.Width)&&double.IsFinite(x.Height)&&x.Width>0&&x.Height>0))
                floatingImagePlacements.Add(new FloatingImagePlacementData
                {
                    ImageIndex=item.ImageIndex,
                    AnchorOffset=item.AnchorOffset,
                    OffsetX=item.OffsetX,
                    OffsetY=item.OffsetY,
                    Width=item.Width,
                    Height=item.Height
                });
        }
        Dispatcher.BeginInvoke(new Action(RefreshFloatingImagePlacements),DispatcherPriority.Loaded);
    }

    void ClearFloatingImagePlacements()
    {
        foreach(var placement in floatingImagePlacements)
            if(placement.RuntimeImage!=null)
                placement.RuntimeImage.Visibility=Visibility.Visible;
        floatingImagePlacements.Clear();
        floatingImageOverlays.Clear();
        BehindTextCanvas.Children.Clear();
        draggingFloatingImage=null;
    }

    bool IsPictureBehindText(Image image)
        =>floatingImagePlacements.Any(x=>ReferenceEquals(x.RuntimeImage,image));

    void SetPictureBehindText(Image image,bool behind)
    {
        var existing=floatingImagePlacements.FirstOrDefault(x=>ReferenceEquals(x.RuntimeImage,image));
        if(behind)
        {
            if(existing==null)
            {
                var container=FindContainer(image);
                if(container==null)return;
                var width=double.IsFinite(image.Width)&&image.Width>0?image.Width:image.ActualWidth;
                var height=double.IsFinite(image.Height)&&image.Height>0?image.Height:image.ActualHeight;
                if(width<=0&&image.Source is BitmapSource bitmap)width=bitmap.PixelWidth;
                if(height<=0&&image.Source is BitmapSource source)height=width*source.PixelHeight/Math.Max(1,source.PixelWidth);
                if(width<=0||height<=0)return;

                var images=EnumerateImageContainers(Editor.Document).ToList();
                var index=images.FindIndex(x=>ReferenceEquals(x.Image,image));
                if(index<0)return;
                existing=new FloatingImagePlacementData
                {
                    ImageIndex=index,
                    AnchorOffset=Editor.Document.ContentStart.GetOffsetToPosition(container.ContentStart),
                    OffsetX=0,
                    OffsetY=0,
                    Width=width,
                    Height=height,
                    RuntimeImage=image
                };
                floatingImagePlacements.Add(existing);
            }
            existing.Width=double.IsFinite(image.Width)&&image.Width>0?image.Width:existing.Width;
            existing.Height=double.IsFinite(image.Height)&&image.Height>0?image.Height:existing.Height;
            image.Visibility=Visibility.Collapsed;
        }
        else if(existing!=null)
        {
            floatingImagePlacements.Remove(existing);
            if(floatingImageOverlays.Remove(existing,out var overlay))
                BehindTextCanvas.Children.Remove(overlay);
            image.Visibility=Visibility.Visible;
        }

        RefreshFloatingImagePlacements();
    }

    void RefreshFloatingImagePlacements()
    {
        if(!IsLoaded||BehindTextCanvas==null)return;
        Editor.UpdateLayout();

        var images=EnumerateImageContainers(Editor.Document).ToList();
        foreach(var placement in floatingImagePlacements.ToList())
        {
            if(placement.RuntimeImage==null)
            {
                if(placement.ImageIndex<0||placement.ImageIndex>=images.Count)continue;
                placement.RuntimeImage=images[placement.ImageIndex].Image;
            }
            var container=FindContainer(placement.RuntimeImage);
            if(container==null)
            {
                floatingImagePlacements.Remove(placement);
                if(floatingImageOverlays.Remove(placement,out var staleOverlay))
                    BehindTextCanvas.Children.Remove(staleOverlay);
                continue;
            }
            placement.AnchorOffset=Editor.Document.ContentStart.GetOffsetToPosition(container.ContentStart);
            placement.RuntimeImage.Visibility=Visibility.Collapsed;

            if(!floatingImageOverlays.TryGetValue(placement,out var overlay))
            {
                overlay=new Image
                {
                    Source=placement.RuntimeImage.Source,
                    Width=placement.Width,
                    Height=placement.Height,
                    Stretch=Stretch.Uniform,
                    IsHitTestVisible=false,
                    Cursor=Cursors.SizeAll,
                    SnapsToDevicePixels=true,
                    ToolTip="Drag to move; right-click for picture properties"
                };
                floatingImageOverlays[placement]=overlay;
                BehindTextCanvas.Children.Add(overlay);
            }

            overlay.Width=placement.Width;
            overlay.Height=placement.Height;
            overlay.Source=placement.RuntimeImage.Source;
            PositionFloatingImage(placement,container,overlay);
        }
    }

    void PositionFloatingImage(
        FloatingImagePlacementData placement,InlineUIContainer container,Image overlay)
    {
        try
        {
            var rect=container.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if(rect.IsEmpty||!double.IsFinite(rect.Top)||!double.IsFinite(rect.Left))return;
            var origin=Editor.TranslatePoint(new Point(rect.Left,rect.Top),BehindTextCanvas);
            Canvas.SetLeft(overlay,origin.X+placement.OffsetX);
            Canvas.SetTop(overlay,origin.Y+placement.OffsetY);
        }
        catch(InvalidOperationException){ }
    }

    FloatingImageHit? FindFloatingImageAt(Point point)
    {
        foreach(var placement in floatingImagePlacements.AsEnumerable().Reverse())
        {
            if(placement.RuntimeImage==null||
               !floatingImageOverlays.TryGetValue(placement,out var overlay))continue;
            var rect=new Rect(
                Canvas.GetLeft(overlay),Canvas.GetTop(overlay),
                placement.Width,placement.Height);
            if(rect.Contains(point))
                return new FloatingImageHit(placement,placement.RuntimeImage,overlay);
        }
        return null;
    }

    bool BeginFloatingImageDrag(MouseButtonEventArgs e)
    {
        var hit=FindFloatingImageAt(e.GetPosition(BehindTextCanvas));
        if(hit==null)return false;

        if(e.ClickCount>=2)
        {
            e.Handled=true;
            ShowPicturePropertiesSafely(hit.Original);
            return true;
        }

        draggingFloatingImage=hit.Placement;
        floatingDragStart=e.GetPosition(BehindTextCanvas);
        floatingDragStartX=hit.Placement.OffsetX;
        floatingDragStartY=hit.Placement.OffsetY;
        Editor.Cursor=Cursors.SizeAll;
        Editor.CaptureMouse();
        e.Handled=true;
        return true;
    }

    void FloatingImage_PreviewMouseMove(object sender,MouseEventArgs e)
    {
        if(draggingFloatingImage==null)
        {
            Editor.Cursor=FindFloatingImageAt(e.GetPosition(BehindTextCanvas))!=null?Cursors.SizeAll:null;
            return;
        }
        if(e.LeftButton!=MouseButtonState.Pressed)return;
        var point=e.GetPosition(BehindTextCanvas);
        draggingFloatingImage.OffsetX=floatingDragStartX+point.X-floatingDragStart.X;
        draggingFloatingImage.OffsetY=floatingDragStartY+point.Y-floatingDragStart.Y;
        if(floatingImageOverlays.TryGetValue(draggingFloatingImage,out var overlay)&&
           draggingFloatingImage.RuntimeImage!=null)
        {
            var container=FindContainer(draggingFloatingImage.RuntimeImage);
            if(container!=null)PositionFloatingImage(draggingFloatingImage,container,overlay);
        }
        e.Handled=true;
    }

    void FloatingImage_PreviewMouseLeftButtonUp(object sender,MouseButtonEventArgs e)
    {
        if(draggingFloatingImage==null)return;
        var moved=Math.Abs(draggingFloatingImage.OffsetX-floatingDragStartX)>0.5||
                  Math.Abs(draggingFloatingImage.OffsetY-floatingDragStartY)>0.5;
        draggingFloatingImage=null;
        Editor.Cursor=null;
        if(Editor.IsMouseCaptured)Editor.ReleaseMouseCapture();
        if(moved)MarkDirty("Picture moved.");
        e.Handled=true;
    }

    void HideFloatingImagesInDocument(FlowDocument document)
    {
        var images=EnumerateImageContainers(document).ToList();
        foreach(var placement in floatingImagePlacements)
            if(placement.ImageIndex>=0&&placement.ImageIndex<images.Count)
                images[placement.ImageIndex].Image.Visibility=Visibility.Collapsed;
    }

    List<FloatingPageImage> CreateFloatingPageImages(Size targetPageSize)
    {
        var result=new List<FloatingPageImage>();
        var map=beta35PaginationMap;
        if(map==null||map.PageCount<=0)return result;
        var starts=PageGuides.CachedPageStartsY.Where(double.IsFinite).OrderBy(x=>x).ToList();
        if(starts.Count==0)starts.Add(0);

        foreach(var placement in floatingImagePlacements)
        {
            var image=placement.RuntimeImage;
            if(image?.Source is not ImageSource source)continue;
            var container=FindContainer(image);
            if(container==null)continue;
            var offset=Editor.Document.ContentStart.GetOffsetToPosition(container.ContentStart);
            var pageIndex=0;
            for(var i=1;i<map.PageStartOffsets.Count;i++)
            {
                if(offset<map.PageStartOffsets[i])break;
                pageIndex=i;
            }
            pageIndex=Math.Clamp(pageIndex,0,map.PageCount-1);
            var rect=container.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if(rect.IsEmpty||!double.IsFinite(rect.Top)||!double.IsFinite(rect.Left))continue;
            var pageTop=pageIndex<starts.Count?starts[pageIndex]:pageIndex*pageHeight;
            var sx=targetPageSize.Width/Math.Max(1,pageWidth);
            var sy=targetPageSize.Height/Math.Max(1,pageHeight);
            var bounds=new Rect(
                (pageMargins.Left+rect.Left+placement.OffsetX)*sx,
                (pageMargins.Top+rect.Top-pageTop+placement.OffsetY)*sy,
                placement.Width*sx,
                placement.Height*sy);
            result.Add(new FloatingPageImage(pageIndex,source,bounds));
        }
        return result;
    }

    static IEnumerable<(InlineUIContainer Container,Image Image)> EnumerateImageContainers(FlowDocument document)
    {
        foreach(var block in document.Blocks)
            foreach(var item in EnumerateImageContainers(block))
                yield return item;
    }

    static IEnumerable<(InlineUIContainer Container,Image Image)> EnumerateImageContainers(Block block)
    {
        if(block is Paragraph paragraph)
        {
            foreach(var inline in paragraph.Inlines)
                foreach(var item in EnumerateImageContainers(inline))
                    yield return item;
        }
        else if(block is Section section)
        {
            foreach(var child in section.Blocks)
                foreach(var item in EnumerateImageContainers(child))
                    yield return item;
        }
        else if(block is System.Windows.Documents.List list)
        {
            foreach(var listItem in list.ListItems)
                foreach(var child in listItem.Blocks)
                    foreach(var item in EnumerateImageContainers(child))
                        yield return item;
        }
        else if(block is Table table)
        {
            foreach(var group in table.RowGroups)
                foreach(var row in group.Rows)
                    foreach(var cell in row.Cells)
                        foreach(var child in cell.Blocks)
                            foreach(var item in EnumerateImageContainers(child))
                                yield return item;
        }
    }

    static IEnumerable<(InlineUIContainer Container,Image Image)> EnumerateImageContainers(Inline inline)
    {
        if(inline is InlineUIContainer ui&&ui.Child is Image image)
            yield return (ui,image);
        else if(inline is Span span)
            foreach(var child in span.Inlines)
                foreach(var item in EnumerateImageContainers(child))
                    yield return item;
    }
}

internal static class FloatingImagePlacementBootstrap
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
            new Action(window.InstallFloatingImagePlacement),DispatcherPriority.ApplicationIdle);
    }
}
