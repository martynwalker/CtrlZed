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
        var cyan=Color.FromRgb(71,215,255);
        var cyanSoft=Color.FromArgb(85,71,215,255);

        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(Color.FromRgb(20,34,58),Color.FromRgb(28,48,78),45),
            null,new RectangleGeometry(new Rect(0,0,100,100),20,20)));

        // Subtle circuit traces: visible at larger sizes, harmless at 16px.
        var circuitPen=new Pen(new SolidColorBrush(cyanSoft),1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        group.Children.Add(new GeometryDrawing(null,circuitPen,Geometry.Parse("M 10,28 L 22,28 L 28,22")));
        group.Children.Add(new GeometryDrawing(null,circuitPen,Geometry.Parse("M 90,72 L 78,72 L 72,78")));
        group.Children.Add(new GeometryDrawing(null,circuitPen,Geometry.Parse("M 72,10 L 72,20 L 79,27")));
        group.Children.Add(new GeometryDrawing(null,circuitPen,Geometry.Parse("M 28,90 L 28,80 L 21,73")));

        foreach(var p in new[]{new Point(10,28),new Point(90,72),new Point(72,10),new Point(28,90)})
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(cyan),null,new EllipseGeometry(p,2.3,2.3)));

        // Gold undo/orbit sweep.
        var goldArc=Geometry.Parse("M 73,73 A 34,34 0 1 1 39,23");
        group.Children.Add(new GeometryDrawing(null,
            new Pen(new SolidColorBrush(GoldBright),8.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},
            goldArc));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(GoldBright),null,Geometry.Parse("M 38,14 L 38,33 L 22,27 Z")));

        // Cyan counter-orbit suggests data/AI motion.
        var cyanArc=Geometry.Parse("M 69,23 A 31,31 0 0 1 83,49");
        group.Children.Add(new GeometryDrawing(null,
            new Pen(new SolidColorBrush(cyan),4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},
            cyanArc));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(cyan),null,new EllipseGeometry(new Point(84,52),2.7,2.7)));

        // Slight cyan shadow behind the Z gives depth without blurring small icons.
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(90,71,215,255)),null,
            Geometry.Parse("M 38,33 L 67,33 L 67,41 L 50,61 L 68,61 L 68,70 L 35,70 L 35,61 L 52,41 L 38,41 Z")));
        var z=Geometry.Parse("M 36,31 L 65,31 L 65,39 L 48,59 L 66,59 L 66,68 L 33,68 L 33,59 L 50,39 L 36,39 Z");
        group.Children.Add(new GeometryDrawing(Brushes.White,null,z));

        var image=new DrawingImage(group);
        image.Freeze();
        return image;
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
        panel.Children.Add(new TextBlock{Text="Microsoft .NET / WPF — application platform\nRoslyn — C# and VB.NET compiler services\nDocumentFormat.OpenXml — DOCX interoperability\nQRCoder — QR code generation\nPdfPig — PDF text import",FontSize=14,LineHeight=24});
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

        var links=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,20,0,0)};
        var credits=new Button{Content="Credits",Width=90,Height=30,Margin=new Thickness(4)};
        var github=new Button{Content="GitHub",Width=90,Height=30,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=90,Height=30,Margin=new Thickness(4),IsDefault=true};
        credits.Click+=(_,__)=>CreateCreditsWindow(window).ShowDialog();
        github.Click+=(_,__)=>Process.Start(new ProcessStartInfo("https://github.com/martynwalker/CtrlZed"){UseShellExecute=true});
        close.Click+=(_,__)=>window.Close();
        links.Children.Add(credits);links.Children.Add(github);links.Children.Add(close);
        Grid.SetRow(links,5);Grid.SetRowSpan(links,2);root.Children.Add(links);

        window.Content=root;return window;
    }
}
