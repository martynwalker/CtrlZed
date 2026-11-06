using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public sealed class PagePreviewWindow : Window
{
    static readonly Brush PreviewSurround=CreatePreviewSurround();
    readonly DocumentPaginator paginator;
    readonly Border pageHost;
    readonly TextBlock pageLabel;
    readonly ComboBox zoomBox;
    int pageIndex;
    double zoom=0.72;

    public PagePreviewWindow(DocumentPaginator paginator)
    {
        this.paginator=paginator;
        Title="CtrlZed Page Preview";
        Width=1000;
        Height=820;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=PreviewSurround;

        paginator.ComputePageCount();

        var root=new DockPanel{Background=PreviewSurround};
        var bar=new StackPanel
        {
            Orientation=Orientation.Horizontal,
            Background=Brushes.WhiteSmoke,
            Margin=new Thickness(0),
            Height=42,
            VerticalAlignment=VerticalAlignment.Top
        };
        DockPanel.SetDock(bar,Dock.Top);

        var previous=new Button{Content="◀ Previous",Margin=new Thickness(8,7,4,7),Padding=new Thickness(12,2,12,2)};
        var next=new Button{Content="Next ▶",Margin=new Thickness(4,7,12,7),Padding=new Thickness(12,2,12,2)};
        pageLabel=new TextBlock{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,18,0)};
        zoomBox=new ComboBox{Width=90,Margin=new Thickness(0,7,8,7)};
        zoomBox.Items.Add("50%");
        zoomBox.Items.Add("72%");
        zoomBox.Items.Add("85%");
        zoomBox.Items.Add("100%");
        zoomBox.SelectedItem="72%";

        previous.Click+=(_,__)=>{if(pageIndex>0){pageIndex--;RenderPage();}};
        next.Click+=(_,__)=>{if(pageIndex<paginator.PageCount-1){pageIndex++;RenderPage();}};
        zoomBox.SelectionChanged+=(_,__)=>
        {
            zoom=zoomBox.SelectedItem?.ToString() switch
            {
                "50%" => 0.50,
                "85%" => 0.85,
                "100%" => 1.00,
                _ => 0.72
            };
            RenderPage();
        };

        bar.Children.Add(previous);
        bar.Children.Add(next);
        bar.Children.Add(pageLabel);
        bar.Children.Add(new TextBlock{Text="Zoom",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)});
        bar.Children.Add(zoomBox);
        root.Children.Add(bar);

        pageHost=new Border
        {
            Background=Brushes.White,
            BorderBrush=new SolidColorBrush(Color.FromRgb(184,181,172)),
            BorderThickness=new Thickness(1),
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Top,
            Margin=new Thickness(24),
            Effect=new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius=7,
                ShadowDepth=2,
                Opacity=0.18,
                Color=Colors.Black
            }
        };

        var scroll=new ScrollViewer
        {
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            Background=PreviewSurround,
            Content=pageHost
        };
        root.Children.Add(scroll);

        Content=root;
        RenderPage();
    }

    static Brush CreatePreviewSurround()
    {
        var brush=new SolidColorBrush(Color.FromRgb(239,237,229));
        brush.Freeze();
        return brush;
    }

    void RenderPage()
    {
        var count=Math.Max(1,paginator.PageCount);
        pageIndex=Math.Clamp(pageIndex,0,count-1);
        var page=paginator.GetPage(pageIndex);
        pageLabel.Text=$"Page {pageIndex+1} of {count}";

        if(page==DocumentPage.Missing || page.Visual==null)
        {
            pageHost.Background=Brushes.White;
            pageHost.Width=600;
            pageHost.Height=800;
            return;
        }

        pageHost.Width=Math.Max(200,page.Size.Width*zoom);
        pageHost.Height=Math.Max(260,page.Size.Height*zoom);
        pageHost.Background=new VisualBrush(page.Visual){Stretch=Stretch.Fill};
    }
}
