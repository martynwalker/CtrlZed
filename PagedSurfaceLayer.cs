using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public sealed class PagedSurfaceLayer : FrameworkElement
{
    public int PageCount { get; set; }=1;
    public IReadOnlyList<double> PageStartsY { get; set; }=Array.Empty<double>();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(ActualWidth<20 || ActualHeight<20 || PageCount<2)return;

        var gapBrush=new SolidColorBrush(Color.FromRgb(239,236,226));
        var upperPen=new Pen(new SolidColorBrush(Color.FromRgb(206,203,193)),1);
        var lowerPen=new Pen(new SolidColorBrush(Color.FromRgb(181,178,168)),1);

        foreach(var start in PageStartsY.Skip(1)
                    .Where(y=>double.IsFinite(y) && y>14 && y<ActualHeight-2)
                    .DistinctBy(y=>Math.Round(y,1)))
        {
            // The live document remains continuous. The separator is drawn
            // immediately before the mapped first character on the next page,
            // so it creates page definition without covering or moving text.
            var top=Math.Max(0,start-12);
            var bottom=Math.Max(top+2,start-2);
            dc.DrawRectangle(gapBrush,null,new Rect(0,top,ActualWidth,bottom-top));
            dc.DrawLine(upperPen,new Point(0,top),new Point(ActualWidth,top));
            dc.DrawLine(lowerPen,new Point(0,bottom),new Point(ActualWidth,bottom));
        }
    }
}

public partial class MainWindow
{
    PagedSurfaceLayer? beta35PageSurfaceLayer;

    internal void InstallBeta35PageSurfaceLayer()
    {
        if(beta35PageSurfaceLayer!=null)return;
        if(Editor.Parent is not Grid host)return;

        beta35PageSurfaceLayer=new PagedSurfaceLayer
        {
            IsHitTestVisible=false,
            HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch,
            Margin=new Thickness(-pageMargins.Left,0,-pageMargins.Right,0)
        };
        Panel.SetZIndex(beta35PageSurfaceLayer,-10);
        host.Children.Insert(0,beta35PageSurfaceLayer);
        PageBorder.SizeChanged+=(_,__)=>UpdateBeta35PageSurfaceLayout();
        UpdateBeta35PageSurfaceLayout();
        SetBeta35PageSurfaceVisibility();
    }

    internal void UpdateBeta35PageSurface(
        int pageCount,IReadOnlyList<double> pageStartsY)
    {
        if(beta35PageSurfaceLayer==null)return;
        beta35PageSurfaceLayer.PageCount=Math.Max(1,pageCount);
        beta35PageSurfaceLayer.PageStartsY=pageStartsY;
        beta35PageSurfaceLayer.InvalidateVisual();
    }

    internal void SetBeta35PageSurfaceVisibility()
    {
        if(beta35PageSurfaceLayer==null)return;
        beta35PageSurfaceLayer.Visibility=CurrentEditorViewMode==EditorViewMode.Paged
            ?Visibility.Visible
            :Visibility.Collapsed;
    }

    void UpdateBeta35PageSurfaceLayout()
    {
        if(beta35PageSurfaceLayer==null)return;
        beta35PageSurfaceLayer.Margin=new Thickness(
            -pageMargins.Left,0,-pageMargins.Right,0);
        beta35PageSurfaceLayer.InvalidateVisual();
    }
}

internal static class Beta35PageSurfaceBootstrap
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
            new Action(window.InstallBeta35PageSurfaceLayer),DispatcherPriority.ApplicationIdle);
    }
}
