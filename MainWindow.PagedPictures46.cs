using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    sealed record Beta46PictureGeometry(int AnchorPage,int DisplayedPage,Rect BodyBounds,Rect CanvasBounds);
    readonly Dictionary<int,IReadOnlyList<PagedFloatingPicture>> beta46PagedPictures=new();
    readonly Dictionary<FloatingImagePlacementData,Beta46PictureGeometry> beta46PictureGeometry=new();
    bool beta46PictureProviderInstalled;

    bool Beta46PagedPicturesActive=>beta46PagedEditingInstalled && PagedEditor?.IsPaged==true;

    internal void RefreshBeta46PagedPictures()
    {
        if(PagedEditor==null || BehindTextCanvas==null || InFrontTextCanvas==null)return;
        if(!beta46PictureProviderInstalled)
        {
            PagedEditor.FloatingPicturesProvider=page=>beta46PagedPictures.TryGetValue(page,out var pictures)
                ?pictures:Array.Empty<PagedFloatingPicture>();
            beta46PictureProviderInstalled=true;
        }

        var paged=Beta46PagedPicturesActive;
        // Hidden canvases retain the canonical coordinate system used by
        // persistence and continuous editing. Paged floats are drawn apart
        // from text slices, which can end before the paper body does.
        BehindTextCanvas.Visibility=paged?Visibility.Hidden:Visibility.Visible;
        InFrontTextCanvas.Visibility=paged?Visibility.Hidden:Visibility.Visible;
        beta46PagedPictures.Clear();
        beta46PictureGeometry.Clear();
        if(!paged || !IsLoaded || floatingImagePlacements.Count==0)
        {
            PagedEditor.InvalidateFloatingPictures();
            return;
        }

        var starts=PagedEditor.SourcePageStarts;
        var groups=new Dictionary<int,List<PagedFloatingPicture>>();
        var containers=EnumerateImageContainers(Editor.Document).ToDictionary(item=>item.Image,item=>item.Container);
        foreach(var placement in floatingImagePlacements)
        {
            var image=placement.RuntimeImage;
            if(image==null || VectorImageSupport.GetDisplaySource(image) is not ImageSource source ||
               !containers.TryGetValue(image,out var container))continue;
            try
            {
                var rect=container.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if(rect.IsEmpty || !double.IsFinite(rect.Top) || !double.IsFinite(rect.Left))continue;
                var anchor=Editor.TranslatePoint(rect.TopLeft,EditorSurfaceGrid);
                var anchorPage=Beta46FloatingAnchorPage(container,starts);
                var displayedPage=FloatingPictureDisplayedPage(anchorPage,placement.PageOffset,PagedEditor.PageCount);
                var anchorStart=anchorPage<starts.Count?starts[anchorPage]:0;
                var targetStart=displayedPage<starts.Count?starts[displayedPage]:0;
                var bounds=new Rect(anchor.X+placement.OffsetX,anchor.Y-anchorStart+placement.OffsetY,
                    placement.Width,placement.Height);
                var canvasOrigin=EditorSurfaceGrid.TranslatePoint(new Point(bounds.Left,targetStart+bounds.Top),BehindTextCanvas);
                beta46PictureGeometry[placement]=new Beta46PictureGeometry(anchorPage,displayedPage,bounds,new Rect(canvasOrigin,bounds.Size));
                if(!groups.TryGetValue(displayedPage,out var pictures))groups[displayedPage]=pictures=new List<PagedFloatingPicture>();
                pictures.Add(new PagedFloatingPicture(source,bounds,placement.Mode,
                    ReferenceEquals(placement,selectedFloatingImage)));
            }
            catch(InvalidOperationException){ }
        }
        foreach(var group in groups)beta46PagedPictures[group.Key]=group.Value.ToArray();
        PagedEditor.InvalidateFloatingPictures();
    }

    static int FloatingPictureDisplayedPage(int anchorPage,int pageOffset,int pageCount)
        =>(int)Math.Clamp((long)anchorPage+pageOffset,0,Math.Max(0,pageCount-1));

    int Beta46FloatingAnchorPage(InlineUIContainer container,IReadOnlyList<double> starts)
    {
        var stablePage=Math.Clamp(Beta46PageForPointer(container.ContentStart),0,Math.Max(0,starts.Count-1));
        if(!beta46PaginationPending || starts.Count<2)return stablePage;
        try
        {
            // Live provisional pages can be inserted before this anchor while
            // the idle paginator still has its previous page indexes. Classify
            // against current source geometry until that new map is ready.
            // Looking backward avoids borrowing the following visible line's
            // rectangle when this image's inline container is collapsed.
            var pointer=container.ElementStart.GetInsertionPosition(LogicalDirection.Backward)??container.ElementStart;
            var rect=pointer.GetCharacterRect(LogicalDirection.Backward);
            if(rect.IsEmpty || !double.IsFinite(rect.Top))return stablePage;
            var y=Editor.TranslatePoint(rect.TopLeft,EditorSurfaceGrid).Y;
            var page=0;
            for(var index=1;index<starts.Count;index++)
            {
                if(y+0.1<starts[index])break;
                page=index;
            }
            return page;
        }
        catch(InvalidOperationException){return stablePage;}
    }

    bool Beta46PictureCanBeHit(FloatingImagePlacementData placement,Point canvasPoint)
    {
        if(!Beta46PagedPicturesActive)return true;
        if(!beta46PictureGeometry.TryGetValue(placement,out var geometry) || geometry.DisplayedPage!=PagedEditor.ActivePageIndex)return false;
        try
        {
            var projected=BehindTextCanvas.TranslatePoint(canvasPoint,PagedEditor);
            var body=PagedEditor.GetPageBodyPoint(projected);
            var size=PagedEditor.PaperSize;
            var margins=PagedEditor.PageMargins;
            return body.X>=0 && body.Y>=0 && body.X<=Math.Max(80,size.Width-margins.Left-margins.Right) &&
                body.Y<=Math.Max(80,size.Height-margins.Top-margins.Bottom);
        }
        catch(InvalidOperationException){return false;}
    }

    bool TryGetBeta46FloatingBounds(FloatingImagePlacementData placement,out Rect bounds)
    {
        bounds=Rect.Empty;
        if(!Beta46PagedPicturesActive || !beta46PictureGeometry.TryGetValue(placement,out var geometry))return false;
        bounds=geometry.CanvasBounds;
        return true;
    }

    void ConstrainBeta46PictureDrag(FloatingImagePlacementData placement)
    {
        var image=placement.RuntimeImage;
        if(image==null)return;
        var container=FindContainer(image);
        if(container==null)return;
        try
        {
            var rect=container.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if(rect.IsEmpty)return;
            var anchor=Editor.TranslatePoint(rect.TopLeft,EditorSurfaceGrid);
            var starts=PagedEditor.SourcePageStarts;
            var anchorPage=Beta46FloatingAnchorPage(container,starts);
            if(anchorPage>=starts.Count)return;
            var bodyWidth=Math.Max(80,PagedEditor.PaperSize.Width-PagedEditor.PageMargins.Left-PagedEditor.PageMargins.Right);
            var bodyHeight=Math.Max(80,PagedEditor.PaperSize.Height-PagedEditor.PageMargins.Top-PagedEditor.PageMargins.Bottom);
            if(placement.Width<=bodyWidth)
                placement.OffsetX=Math.Clamp(anchor.X+placement.OffsetX,0,bodyWidth-placement.Width)-anchor.X;
            var localY=anchor.Y-starts[anchorPage];
            if(placement.Height<=bodyHeight)
                placement.OffsetY=Math.Clamp(localY+placement.OffsetY,0,bodyHeight-placement.Height)-localY;
        }
        catch(InvalidOperationException){ }
    }

    double FloatingPictureSourcePageShift(FloatingImagePlacementData placement,InlineUIContainer container)
    {
        if(placement.PageOffset==0)return 0;
        var starts=PageGuides.CachedPageStartsY;
        var map=beta35PaginationMap;
        if(map==null || starts.Count==0 || !ReferenceEquals(beta35PaginationDocument,Editor.Document))return 0;
        var offset=Editor.Document.ContentStart.GetOffsetToPosition(container.ContentStart);
        var anchorPage=0;
        for(var index=1;index<map.PageStartOffsets.Count;index++)
        {
            if(offset<map.PageStartOffsets[index])break;
            anchorPage=index;
        }
        var target=FloatingPictureDisplayedPage(anchorPage,placement.PageOffset,map.PageCount);
        if(anchorPage>=starts.Count || target>=starts.Count)return 0;
        return starts[target]-starts[anchorPage];
    }

    internal Rect Beta46FloatingPictureBounds(Image image)
    {
        var placement=floatingImagePlacements.FirstOrDefault(item=>ReferenceEquals(item.RuntimeImage,image));
        if(placement==null || !beta46PictureGeometry.TryGetValue(placement,out var geometry))return Rect.Empty;
        var bounds=geometry.BodyBounds;
        var paper=PagedEditor.GetPageBounds(geometry.DisplayedPage);
        bounds.Offset(paper.Left+PagedEditor.PageMargins.Left,paper.Top+PagedEditor.PageMargins.Top);
        return bounds;
    }
}
