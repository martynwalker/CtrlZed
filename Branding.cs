using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Navy),null,new RectangleGeometry(new Rect(0,0,100,100),18,18)));
        var mainArc=Geometry.Parse("M 73,74 A 34,34 0 1 1 38,24");
        group.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(Gold),10){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},mainArc));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Gold),null,Geometry.Parse("M 37,14 L 37,34 L 20,28 Z")));
        var trail1=Geometry.Parse("M 70,24 A 30,30 0 0 1 84,50");
        group.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(Color.FromArgb(120,229,163,65)),4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},trail1));
        var trail2=Geometry.Parse("M 75,27 A 28,28 0 0 1 88,54");
        group.Children.Add(new GeometryDrawing(null,new Pen(new SolidColorBrush(Color.FromArgb(60,229,163,65)),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},trail2));
        group.Children.Add(new GeometryDrawing(Brushes.White,null,Geometry.Parse("M 36,34 L 66,34 L 66,42 L 49,62 L 67,62 L 67,70 L 34,70 L 34,62 L 51,42 L 36,42 Z")));
        var image=new DrawingImage(group);image.Freeze();return image;
    }

    static TextBlock Link(string text,string url)
    {
        var block=new TextBlock{HorizontalAlignment=HorizontalAlignment.Center};
        var link=new Hyperlink(new Run(text)){NavigateUri=new Uri(url)};
        link.RequestNavigate+=(_,e)=>{Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;};
        block.Inlines.Add(link);return block;
    }

    public static Window CreateCreditsWindow(Window owner)
    {
        var w=new Window{Title="CtrlZed Credits",Owner=owner,Width=560,Height=420,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=CreateIconImageSource()};
        var panel=new StackPanel{Margin=new Thickness(28)};
        panel.Children.Add(new TextBlock{Text="Credits & acknowledgements",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18)});
        panel.Children.Add(new TextBlock{Text="CtrlZed is built on open-source and platform technologies whose authors deserve explicit credit.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        panel.Children.Add(new TextBlock{Text="Microsoft .NET / WPF — application platform\nRoslyn — C# and VB.NET compiler services\nDocumentFormat.OpenXml — DOCX interoperability\nQRCoder — QR code generation",FontSize=14,LineHeight=24});
        panel.Children.Add(new TextBlock{Text="Licences and notices remain governed by their respective projects and packages.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,18,0,18)});
        panel.Children.Add(Link("CtrlZed on GitHub","https://github.com/martynwalker/CtrlZed"));
        var close=new Button{Content="Close",Width=90,Height=30,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        close.Click+=(_,__)=>w.Close();panel.Children.Add(close);w.Content=panel;return w;
    }

    public static Window CreateAboutWindow(Window owner,string version)
    {
        var window=new Window{Title="About CtrlZed",Owner=owner,Width=560,Height=430,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(NavyDeep),Icon=CreateIconImageSource()};
        var root=new Grid{Margin=new Thickness(34)};
        for(int i=0;i<7;i++)root.RowDefinitions.Add(new RowDefinition{Height=i==6?GridLength.Auto:GridLength.Auto});

        var logo=new Image{Source=CreateIconImageSource(),Width=88,Height=88,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,14)};
        Grid.SetRow(logo,0);root.Children.Add(logo);

        var title=new TextBlock{HorizontalAlignment=HorizontalAlignment.Center,FontFamily=new FontFamily("Segoe UI"),FontSize=34,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)};
        title.Inlines.Add(new Run("Ctrl"){Foreground=Brushes.White});title.Inlines.Add(new Run("Zed"){Foreground=new SolidColorBrush(GoldBright)});
        Grid.SetRow(title,1);root.Children.Add(title);

        var subtitle=new TextBlock{Text="AI-developed open-source word processor",Foreground=new SolidColorBrush(Color.FromRgb(204,218,238)),FontSize=16,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,18)};
        Grid.SetRow(subtitle,2);root.Children.Add(subtitle);

        var divider=new Border{Height=1,Background=new SolidColorBrush(Color.FromArgb(80,229,163,65)),Margin=new Thickness(50,0,50,18)};
        Grid.SetRow(divider,3);root.Children.Add(divider);

        var body=new TextBlock{Text=$"Version {version}\n\nHuman-directed. AI-authored. Open to inspection.\nMIT licensed · Local-first · Keyboard-friendly",Foreground=new SolidColorBrush(Color.FromRgb(220,226,236)),FontSize=13.5,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
        Grid.SetRow(body,4);root.Children.Add(body);

        var links=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,18,0,0)};
        var credits=new Button{Content="Credits",Width=90,Margin=new Thickness(4)};
        var github=new Button{Content="GitHub",Width=90,Margin=new Thickness(4)};
        credits.Click+=(_,__)=>CreateCreditsWindow(window).ShowDialog();
        github.Click+=(_,__)=>Process.Start(new ProcessStartInfo("https://github.com/martynwalker/CtrlZed"){UseShellExecute=true});
        links.Children.Add(credits);links.Children.Add(github);Grid.SetRow(links,5);root.Children.Add(links);

        var close=new Button{Content="Close",Width=90,Height=30,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,18,0,0)};
        close.Click+=(_,__)=>window.Close();Grid.SetRow(close,6);root.Children.Add(close);

        window.Content=root;return window;
    }
}
