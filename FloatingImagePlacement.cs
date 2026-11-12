using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CtrlZed;

public enum PicturePlacementMode
{
    Inline,
    BehindText,
    InFrontOfText
}

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
        public PicturePlacementMode Mode { get; set; }=PicturePlacementMode.BehindText;
        [JsonIgnore]
        public Image? RuntimeImage { get; set; }
    }

    sealed record FloatingImageHit(
        FloatingImagePlacementData Placement,
        Image Original,
        Image Overlay);

    readonly List<FloatingImagePlacementData> floatingImagePlacements=new();
    readonly Dictionary<FloatingImagePlacementData,Image> floatingImageOverlays=new();
    readonly List<FrameworkElement> floatingImageChrome=new();
    FloatingImagePlacementData? draggingFloatingImage;
    FloatingImagePlacementData? selectedFloatingImage;
    Point floatingDragStart;
    double floatingDragStartX,floatingDragStartY;
    double floatingDragStartWidth,floatingDragStartHeight;
    bool resizingFloatingImage;
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
                Height=placement.Height,
                Mode=placement.Mode
            });
        }
        return result;
    }

    void RestoreFloatingImagePlacements(List<FloatingImagePlacementData>? placements)
    {
        floatingImagePlacements.Clear();
        floatingImageOverlays.Clear();
        BehindTextCanvas.Children.Clear();
        InFrontTextCanvas.Children.Clear();
        floatingImageChrome.Clear();
        selectedFloatingImage=null;
        if(placements!=null)
        {
            foreach(var item in placements.Where(x=>x.ImageIndex>=0&&
                x.Mode!=PicturePlacementMode.Inline&&
                double.IsFinite(x.OffsetX)&&double.IsFinite(x.OffsetY)&&
                double.IsFinite(x.Width)&&double.IsFinite(x.Height)&&x.Width>0&&x.Height>0))
                floatingImagePlacements.Add(new FloatingImagePlacementData
                {
                    ImageIndex=item.ImageIndex,
                    AnchorOffset=item.AnchorOffset,
                    OffsetX=item.OffsetX,
                    OffsetY=item.OffsetY,
                    Width=item.Width,
                    Height=item.Height,
                    Mode=item.Mode
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
        InFrontTextCanvas.Children.Clear();
        floatingImageChrome.Clear();
        draggingFloatingImage=null;
        selectedFloatingImage=null;
    }

    PicturePlacementMode GetPicturePlacement(Image image)
    {
        var floating=floatingImagePlacements.FirstOrDefault(x=>ReferenceEquals(x.RuntimeImage,image));
        if(floating!=null)return floating.Mode;
        return FindContainer(image)?.Child is Canvas
            ?PicturePlacementMode.InFrontOfText
            :PicturePlacementMode.Inline;
    }

    void SetPicturePlacement(Image image,PicturePlacementMode mode)
    {
        var existing=floatingImagePlacements.FirstOrDefault(x=>ReferenceEquals(x.RuntimeImage,image));
        var container=FindContainer(image);
        var previousMode=GetPicturePlacement(image);
        var changed=previousMode!=mode||existing==null&&mode!=PicturePlacementMode.Inline;

        if(mode==PicturePlacementMode.Inline)
        {
            if(existing!=null)
            {
                floatingImagePlacements.Remove(existing);
                RemoveFloatingOverlay(existing);
                image.Visibility=Visibility.Visible;
                selectedFloatingImage=null;
            }

            if(container?.Child is Canvas wrapper&&wrapper.Children.Contains(image))
            {
                wrapper.Children.Remove(image);
                container.Child=image;
            }
            image.Visibility=Visibility.Visible;
            image.IsHitTestVisible=false;
            image.Focusable=false;
            RefreshFloatingImagePlacements();
            if(changed)MarkDirty("Picture placement updated.");
            return;
        }

        if(container==null)return;
        var width=existing?.Width??(double.IsFinite(image.Width)&&image.Width>0?image.Width:image.ActualWidth);
        var height=existing?.Height??(double.IsFinite(image.Height)&&image.Height>0?image.Height:image.ActualHeight);
        if(width<=0&&image.Source is BitmapSource bitmap)width=bitmap.PixelWidth;
        if(height<=0&&image.Source is BitmapSource source)
            height=width*source.PixelHeight/Math.Max(1,source.PixelWidth);
        if(width<=0||height<=0)return;

        if(existing==null)
        {
            var images=EnumerateImageContainers(Editor.Document).ToList();
            var index=images.FindIndex(x=>ReferenceEquals(x.Image,image));
            if(index<0)return;
            var pointerRect=container.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var anchor=Editor.TranslatePoint(new Point(pointerRect.Left,pointerRect.Top),BehindTextCanvas);
            var imageOrigin=image.TranslatePoint(new Point(0,0),BehindTextCanvas);
            existing=new FloatingImagePlacementData
            {
                ImageIndex=index,
                AnchorOffset=Editor.Document.ContentStart.GetOffsetToPosition(container.ContentStart),
                OffsetX=imageOrigin.X-anchor.X,
                OffsetY=imageOrigin.Y-anchor.Y,
                Width=width,
                Height=height,
                RuntimeImage=image
            };
            floatingImagePlacements.Add(existing);
        }
        existing.Mode=mode;
        existing.Width=width;
        existing.Height=height;
        image.Visibility=Visibility.Collapsed;
        selectedFloatingImage=existing;
        RefreshFloatingImagePlacements();
        if(changed)MarkDirty("Picture placement updated.");
    }

    void RemoveFloatingOverlay(FloatingImagePlacementData placement)
    {
        if(!floatingImageOverlays.Remove(placement,out var overlay))return;
        BehindTextCanvas.Children.Remove(overlay);
        InFrontTextCanvas.Children.Remove(overlay);
    }

    Canvas GetPictureCanvas(FloatingImagePlacementData placement)
        =>placement.Mode==PicturePlacementMode.InFrontOfText?InFrontTextCanvas:BehindTextCanvas;

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
                RemoveFloatingOverlay(placement);
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
                    ToolTip="Drag to move; drag the lower-right handle to resize; right-click for placement"
                };
                floatingImageOverlays[placement]=overlay;
            }

            var targetCanvas=GetPictureCanvas(placement);
            if(!ReferenceEquals(overlay.Parent,targetCanvas))
            {
                BehindTextCanvas.Children.Remove(overlay);
                InFrontTextCanvas.Children.Remove(overlay);
                targetCanvas.Children.Add(overlay);
            }
            overlay.Width=placement.Width;
            overlay.Height=placement.Height;
            overlay.Source=placement.RuntimeImage.Source;
            PositionFloatingImage(placement,container,overlay);
        }

        foreach(var stale in floatingImageOverlays.Keys.Where(x=>!floatingImagePlacements.Contains(x)).ToList())
            RemoveFloatingOverlay(stale);
        UpdateFloatingImageChrome();
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

    Rect GetFloatingImageBounds(FloatingImagePlacementData placement)
    {
        if(!floatingImageOverlays.TryGetValue(placement,out var overlay))return Rect.Empty;
        return new Rect(Canvas.GetLeft(overlay),Canvas.GetTop(overlay),placement.Width,placement.Height);
    }

    void ClearFloatingImageChrome()
    {
        foreach(var element in floatingImageChrome)
            InFrontTextCanvas.Children.Remove(element);
        floatingImageChrome.Clear();
    }

    void UpdateFloatingImageChrome()
    {
        ClearFloatingImageChrome();
        if(selectedFloatingImage==null||!floatingImagePlacements.Contains(selectedFloatingImage))return;
        var bounds=GetFloatingImageBounds(selectedFloatingImage);
        if(bounds.IsEmpty)return;

        var outline=new Rectangle
        {
            Width=bounds.Width,
            Height=bounds.Height,
            Fill=Brushes.Transparent,
            Stroke=Brushes.DodgerBlue,
            StrokeThickness=1,
            IsHitTestVisible=false
        };
        Canvas.SetLeft(outline,bounds.Left);
        Canvas.SetTop(outline,bounds.Top);
        var handle=new Rectangle
        {
            Width=10,
            Height=10,
            Fill=Brushes.White,
            Stroke=Brushes.DodgerBlue,
            StrokeThickness=1.2,
            IsHitTestVisible=false,
            Cursor=Cursors.SizeNWSE
        };
        Canvas.SetLeft(handle,bounds.Right-5);
        Canvas.SetTop(handle,bounds.Bottom-5);
        InFrontTextCanvas.Children.Add(outline);
        InFrontTextCanvas.Children.Add(handle);
        floatingImageChrome.Add(outline);
        floatingImageChrome.Add(handle);
    }

    bool BeginFloatingImageDrag(MouseButtonEventArgs e)
    {
        var point=e.GetPosition(BehindTextCanvas);
        var hit=FindFloatingImageAt(point);
        if(hit==null)return false;

        if(e.ClickCount>=2)
        {
            e.Handled=true;
            ShowPicturePropertiesSafely(hit.Original);
            return true;
        }

        resizingFloatingImage=false;
        if(ReferenceEquals(selectedFloatingImage,hit.Placement))
        {
            var bounds=GetFloatingImageBounds(hit.Placement);
            var handle=new Rect(bounds.Right-9,bounds.Bottom-9,18,18);
            resizingFloatingImage=handle.Contains(point);
        }
        selectedFloatingImage=hit.Placement;
        UpdateFloatingImageChrome();
        draggingFloatingImage=hit.Placement;
        floatingDragStart=point;
        floatingDragStartX=hit.Placement.OffsetX;
        floatingDragStartY=hit.Placement.OffsetY;
        floatingDragStartWidth=hit.Placement.Width;
        floatingDragStartHeight=hit.Placement.Height;
        Editor.Cursor=resizingFloatingImage?Cursors.SizeNWSE:Cursors.SizeAll;
        Editor.CaptureMouse();
        e.Handled=true;
        return true;
    }

    void FloatingImage_PreviewMouseMove(object sender,MouseEventArgs e)
    {
        if(draggingFloatingImage==null)
        {
            var point=e.GetPosition(BehindTextCanvas);
            var hit=FindFloatingImageAt(point);
            var bounds=selectedFloatingImage==null?Rect.Empty:GetFloatingImageBounds(selectedFloatingImage);
            Editor.Cursor=hit!=null&&ReferenceEquals(hit.Placement,selectedFloatingImage)&&
                !bounds.IsEmpty&&new Rect(bounds.Right-9,bounds.Bottom-9,18,18).Contains(point)
                ?Cursors.SizeNWSE:hit!=null?Cursors.SizeAll:null;
            return;
        }
        if(e.LeftButton!=MouseButtonState.Pressed)return;
        var position=e.GetPosition(BehindTextCanvas);
        var dx=position.X-floatingDragStart.X;
        var dy=position.Y-floatingDragStart.Y;

        if(resizingFloatingImage)
        {
            var ratio=floatingDragStartWidth/Math.Max(1,floatingDragStartHeight);
            var candidateByX=floatingDragStartWidth+dx;
            var candidateByY=(floatingDragStartHeight+dy)*ratio;
            var width=Math.Max(12,Math.Abs(candidateByX-floatingDragStartWidth)>=Math.Abs(candidateByY-floatingDragStartWidth)
                ?candidateByX:candidateByY);
            draggingFloatingImage.Width=width;
            draggingFloatingImage.Height=width/ratio;
            if(draggingFloatingImage.RuntimeImage!=null)
            {
                draggingFloatingImage.RuntimeImage.Width=draggingFloatingImage.Width;
                draggingFloatingImage.RuntimeImage.Height=draggingFloatingImage.Height;
            }
        }
        else
        {
            draggingFloatingImage.OffsetX=floatingDragStartX+dx;
            draggingFloatingImage.OffsetY=floatingDragStartY+dy;
        }

        if(floatingImageOverlays.TryGetValue(draggingFloatingImage,out var overlay)&&
           draggingFloatingImage.RuntimeImage!=null)
        {
            overlay.Width=draggingFloatingImage.Width;
            overlay.Height=draggingFloatingImage.Height;
            var container=FindContainer(draggingFloatingImage.RuntimeImage);
            if(container!=null)PositionFloatingImage(draggingFloatingImage,container,overlay);
        }
        UpdateFloatingImageChrome();
        e.Handled=true;
    }

    void FloatingImage_PreviewMouseLeftButtonUp(object sender,MouseButtonEventArgs e)
    {
        if(draggingFloatingImage==null)return;
        var changed=Math.Abs(draggingFloatingImage.OffsetX-floatingDragStartX)>0.5||
                    Math.Abs(draggingFloatingImage.OffsetY-floatingDragStartY)>0.5||
                    Math.Abs(draggingFloatingImage.Width-floatingDragStartWidth)>0.5||
                    Math.Abs(draggingFloatingImage.Height-floatingDragStartHeight)>0.5;
        draggingFloatingImage=null;
        resizingFloatingImage=false;
        Editor.Cursor=null;
        if(Editor.IsMouseCaptured)Editor.ReleaseMouseCapture();
        if(changed)MarkDirty("Picture position or size updated.");
        UpdateFloatingImageChrome();
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
            result.Add(new FloatingPageImage(pageIndex,source,bounds,placement.Mode));
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
        if(inline is InlineUIContainer ui)
        {
            foreach(var image in EnumerateImagesInElement(ui.Child))
                yield return (ui,image);
        }
        else if(inline is Span span)
            foreach(var child in span.Inlines)
                foreach(var item in EnumerateImageContainers(child))
                    yield return item;
    }

    static IEnumerable<Image> EnumerateImagesInElement(UIElement? element)
    {
        if(element is Image image)
            yield return image;
        else if(element is Panel panel)
            foreach(UIElement child in panel.Children)
                foreach(var nested in EnumerateImagesInElement(child))
                    yield return nested;
        else if(element is Border border)
            foreach(var nested in EnumerateImagesInElement(border.Child))
                yield return nested;
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
