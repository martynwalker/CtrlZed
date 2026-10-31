using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

public sealed class PagePreviewWindow : Window
{
    public PagePreviewWindow(DocumentPaginator paginator)
    {
        Title="CtrlZed Page Preview";
        Width=1000;
        Height=820;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brushes.DimGray;

        paginator.ComputePageCount();

        var stack=new StackPanel{Margin=new Thickness(20)};
        var scroll=new ScrollViewer
        {
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            Content=stack
        };

        for(int i=0;i<paginator.PageCount;i++)
        {
            var page=paginator.GetPage(i);
            var brush=new VisualBrush(page.Visual){Stretch=Stretch.Fill};
            var border=new Border
            {
                Width=Math.Max(200,page.Size.Width*0.72),
                Height=Math.Max(260,page.Size.Height*0.72),
                Background=brush,
                BorderBrush=Brushes.Gray,
                BorderThickness=new Thickness(1),
                Margin=new Thickness(0,0,0,20),
                HorizontalAlignment=HorizontalAlignment.Center
            };
            stack.Children.Add(border);
        }

        Content=scroll;
    }
}
