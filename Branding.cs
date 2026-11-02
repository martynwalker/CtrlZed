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
        var g=new DrawingGroup();
        var cyan=Color.FromRgb(55,228,240);
        var cyanPale=Color.FromRgb(185,249,255);
        var gold=Color.FromRgb(255,193,55);
        var steel=Color.FromRgb(214,235,241);
        var bg=new LinearGradientBrush(Color.FromRgb(4,15,24),Color.FromRgb(10,31,43),45);

        g.Children.Add(new GeometryDrawing(bg,null,new EllipseGeometry(new Point(50,50),47,47)));

        var gridPen=new Pen(new SolidColorBrush(Color.FromArgb(45,70,215,230)),0.8);
        foreach(var x in new[]{18d,30d,70d,82d})
            g.Children.Add(new GeometryDrawing(null,gridPen,new LineGeometry(new Point(x,8),new Point(x,92))));
        foreach(var y in new[]{18d,30d,70d,82d})
            g.Children.Add(new GeometryDrawing(null,gridPen,new LineGeometry(new Point(8,y),new Point(92,y))));

        void Ring(double radius,Color colour,double width,double start,double sweep)
        {
            var pen=new Pen(new SolidColorBrush(colour),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            var geo=new StreamGeometry();
            using(var c=geo.Open())
            {
                var a=start*Math.PI/180.0;
                var b=(start+sweep)*Math.PI/180.0;
                var p1=new Point(50+radius*Math.Cos(a),50+radius*Math.Sin(a));
                var p2=new Point(50+radius*Math.Cos(b),50+radius*Math.Sin(b));
                c.BeginFigure(p1,false,false);
                c.ArcTo(p2,new Size(radius,radius),0,Math.Abs(sweep)>180,
                    sweep>=0?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true,false);
            }
            geo.Freeze();
            g.Children.Add(new GeometryDrawing(null,pen,geo));
        }

        Ring(42,cyanPale,2.6,195,116);
        Ring(42,gold,2.6,15,116);
        Ring(34,cyan,3.2,206,96);
        Ring(34,gold,3.2,26,96);
        Ring(27,Color.FromArgb(190,55,228,240),1.5,190,140);
        Ring(27,Color.FromArgb(190,255,193,55),1.5,10,140);

        foreach(var p in new[]{new Point(16,50),new Point(84,50),new Point(50,16),new Point(50,84)})
        {
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(6,25,34)),
                new Pen(new SolidColorBrush(cyan),1.8),new EllipseGeometry(p,3.2,3.2)));
        }

        // Angular metallic Z, matching the supplied cyber logo.
        var shadow=Geometry.Parse("M 30,31 L 72,31 L 62,41 L 48,55 L 66,55 L 75,68 L 31,68 L 41,57 L 56,42 L 38,42 Z");
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(110,55,228,240)),null,shadow));
        var z=Geometry.Parse("M 27,28 L 75,28 L 64,40 L 47,57 L 69,57 L 78,71 L 26,71 L 38,58 L 57,39 L 39,39 Z");
        g.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(steel,Color.FromRgb(70,155,172),90),
            new Pen(new SolidColorBrush(cyanPale),1.1),z));

        var inner=Geometry.Parse("M 35,34 L 65,34 L 58,40 L 39,61 L 63,61 L 66,65 L 36,65 L 43,58 L 62,37 L 38,37 Z");
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(5,24,34)),null,inner));

        var image=new DrawingImage(g);
        image.Freeze();
        return image;
    }

    static UIElement CreateAboutLogo()
    {
        var panel=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};
        panel.Children.Add(new Image
        {
            Source=CreateIconImageSource(),Width=245,Height=245,
            HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,4)
        });

        var word=new TextBlock
        {
            FontFamily=new FontFamily("Segoe UI"),FontSize=52,
            HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,-2,0,2)
        };
        word.Inlines.Add(new Run("Ctrl"){Foreground=new SolidColorBrush(Color.FromRgb(175,247,255)),FontWeight=FontWeights.Light});
        word.Inlines.Add(new Run("Zed"){Foreground=new SolidColorBrush(Color.FromRgb(255,193,55)),FontWeight=FontWeights.Light});
        panel.Children.Add(word);
        return panel;
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
        var window=new Window
        {
            Title="About CtrlZed",Owner=owner,Width=620,Height=690,ResizeMode=ResizeMode.NoResize,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=new SolidColorBrush(Color.FromRgb(4,13,22)),Icon=CreateIconImageSource()
        };

        var root=new StackPanel{Margin=new Thickness(30)};
        root.Children.Add(CreateAboutLogo());

        root.Children.Add(new TextBlock
        {
            Text="AI-developed open-source word processor",
            Foreground=new SolidColorBrush(Color.FromRgb(190,218,228)),
            FontSize=16,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,14)
        });

        root.Children.Add(new Border
        {
            Height=1,Background=new SolidColorBrush(Color.FromArgb(100,55,228,240)),
            Margin=new Thickness(45,0,45,14)
        });

        root.Children.Add(new TextBlock
        {
            Text=$"Version {version}\n\nHuman-directed. AI-authored. Open to inspection.\nMIT licensed · Local-first · Keyboard-friendly",
            Foreground=new SolidColorBrush(Color.FromRgb(220,231,238)),
            FontSize=13.5,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap
        });

        var links=new StackPanel
        {
            Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,
            Margin=new Thickness(0,22,0,0)
        };
        var credits=new Button{Content="Credits",Width=90,Height=30,Margin=new Thickness(4)};
        var github=new Button{Content="GitHub",Width=90,Height=30,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=90,Height=30,Margin=new Thickness(4),IsDefault=true};
        credits.Click+=(_,__)=>CreateCreditsWindow(window).ShowDialog();
        github.Click+=(_,__)=>Process.Start(new ProcessStartInfo("https://github.com/martynwalker/CtrlZed"){UseShellExecute=true});
        close.Click+=(_,__)=>window.Close();
        links.Children.Add(credits);links.Children.Add(github);links.Children.Add(close);
        root.Children.Add(links);

        window.Content=root;
        return window;
    }
}
