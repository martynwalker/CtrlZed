using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CtrlZed;

public static class Branding
{
    public static readonly Color Navy = Color.FromRgb(34,50,76);
    public static readonly Color NavyDeep = Color.FromRgb(16,31,55);
    public static readonly Color Gold = Color.FromRgb(229,163,65);
    public static readonly Color GoldBright = Color.FromRgb(255,190,67);
    public static readonly Color Paper = Color.FromRgb(248,249,251);

    public static ImageSource CreateIconImageSource()
    {
        var group=new DrawingGroup();

        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Navy),
            null,
            new RectangleGeometry(new Rect(0,0,100,100),18,18)));

        var mainArc=Geometry.Parse("M 73,74 A 34,34 0 1 1 38,24");
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(Gold),10)
            {
                StartLineCap=PenLineCap.Round,
                EndLineCap=PenLineCap.Round,
                LineJoin=PenLineJoin.Round
            },
            mainArc));

        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Gold),
            null,
            Geometry.Parse("M 37,14 L 37,34 L 20,28 Z")));

        var trail1=Geometry.Parse("M 70,24 A 30,30 0 0 1 84,50");
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(120,229,163,65)),4)
            {
                StartLineCap=PenLineCap.Round,
                EndLineCap=PenLineCap.Round
            },
            trail1));

        var trail2=Geometry.Parse("M 75,27 A 28,28 0 0 1 88,54");
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(60,229,163,65)),2)
            {
                StartLineCap=PenLineCap.Round,
                EndLineCap=PenLineCap.Round
            },
            trail2));

        group.Children.Add(new GeometryDrawing(
            Brushes.White,
            null,
            Geometry.Parse("M 36,34 L 66,34 L 66,42 L 49,62 L 67,62 L 67,70 L 34,70 L 34,62 L 51,42 L 36,42 Z")));

        var image=new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public static Window CreateAboutWindow(Window owner,string version)
    {
        var window=new Window
        {
            Title="About CtrlZed",
            Owner=owner,
            Width=720,
            Height=560,
            ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(NavyDeep),
            Icon=CreateIconImageSource()
        };

        var border=new Border
        {
            CornerRadius=new CornerRadius(18),
            Margin=new Thickness(18),
            BorderThickness=new Thickness(1.5),
            BorderBrush=new SolidColorBrush(Color.FromArgb(180,65,92,130)),
            Background=new LinearGradientBrush(
                Color.FromRgb(21,42,72),
                Color.FromRgb(9,27,50),
                90)
        };

        var root=new Grid{Margin=new Thickness(32)};
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(235)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var hero=new Grid();
        var outer=new Ellipse
        {
            Width=270,Height=270,
            Stroke=new SolidColorBrush(Color.FromArgb(90,229,163,65)),
            StrokeThickness=2,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center
        };
        var inner=new Ellipse
        {
            Width=205,Height=205,
            Stroke=new SolidColorBrush(Color.FromArgb(42,229,163,65)),
            StrokeThickness=1.5,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center
        };
        var logo=new Image
        {
            Source=CreateIconImageSource(),
            Width=190,Height=190,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center
        };
        hero.Children.Add(outer);
        hero.Children.Add(inner);
        hero.Children.Add(logo);
        Grid.SetRow(hero,0);
        root.Children.Add(hero);

        var title=new TextBlock
        {
            HorizontalAlignment=HorizontalAlignment.Center,
            FontFamily=new FontFamily("Segoe UI"),
            FontSize=48,
            FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,-4,0,2)
        };
        title.Inlines.Add(new System.Windows.Documents.Run("Ctrl"){Foreground=Brushes.White});
        title.Inlines.Add(new System.Windows.Documents.Run("Zed"){Foreground=new SolidColorBrush(GoldBright)});
        Grid.SetRow(title,1);
        root.Children.Add(title);

        var subtitle=new TextBlock
        {
            Text="AI-developed open-source word processor",
            Foreground=new SolidColorBrush(Color.FromRgb(204,218,238)),
            FontFamily=new FontFamily("Segoe UI"),
            FontSize=18,
            HorizontalAlignment=HorizontalAlignment.Center,
            Margin=new Thickness(0,0,0,14)
        };
        Grid.SetRow(subtitle,2);
        root.Children.Add(subtitle);

        var tagline=new TextBlock
        {
            Text="The word processor that knows its place.",
            Foreground=new SolidColorBrush(Color.FromRgb(237,240,245)),
            FontFamily=new FontFamily("Segoe UI"),
            FontSize=15,
            FontStyle=FontStyles.Italic,
            HorizontalAlignment=HorizontalAlignment.Center,
            Margin=new Thickness(0,0,0,18)
        };
        Grid.SetRow(tagline,3);
        root.Children.Add(tagline);

        var body=new TextBlock
        {
            Text=$"Version {version}\n\nHuman-directed. AI-authored. Open to inspection.\nMIT licensed · Local-first · Keyboard-friendly\n\nNo programmers were employed in the writing of this software.\nSome humans were consulted.",
            Foreground=new SolidColorBrush(Color.FromRgb(210,220,235)),
            FontFamily=new FontFamily("Segoe UI"),
            FontSize=13.5,
            TextAlignment=TextAlignment.Center,
            TextWrapping=TextWrapping.Wrap,
            HorizontalAlignment=HorizontalAlignment.Stretch
        };
        Grid.SetRow(body,4);
        root.Children.Add(body);

        var close=new Button
        {
            Content="Close",
            Width=90,
            Height=30,
            HorizontalAlignment=HorizontalAlignment.Center,
            Margin=new Thickness(0,14,0,0)
        };
        close.Click+=(_,__)=>window.Close();
        Grid.SetRow(close,5);
        root.Children.Add(close);

        border.Child=root;
        window.Content=border;
        return window;
    }
}
