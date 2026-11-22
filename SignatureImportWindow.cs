using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

/// <summary>Reviews an image or traces a single-colour signature before it is saved.</summary>
public sealed class SignatureImportWindow : Window
{
    readonly SignatureAsset original;
    readonly byte[] originalBytes;
    readonly Image resultPreview=new(){Stretch=Stretch.Uniform};
    readonly CheckBox trace=new(){Content="Trace signature to SVG",Name="TraceSignature",Margin=new Thickness(0,0,20,8)};
    readonly Slider threshold=new(){Minimum=20,Maximum=245,Value=170,TickFrequency=5,IsSnapToTickEnabled=true,
        Width=220,Name="InkThreshold",Margin=new Thickness(8,0,8,0)};
    readonly TextBlock thresholdValue=new(){Width=30,VerticalAlignment=VerticalAlignment.Center};
    readonly CheckBox lightInk=new(){Content="Light ink on a dark background",Name="LightInk",Margin=new Thickness(0,8,0,0)};
    readonly Button colour=new(){Content="Ink Colour…",MinWidth=100,Margin=new Thickness(18,0,0,0)};
    readonly Border swatch=new(){Width=24,Height=24,Background=Brushes.Black,BorderBrush=Brushes.Gray,
        BorderThickness=new Thickness(1),Margin=new Thickness(6,0,0,0)};
    readonly Button use=new(){Content="Use Image",MinWidth=100,IsDefault=true,Name="UseSignature",Margin=new Thickness(6)};
    readonly Button export=new(){Content="Export SVG…",MinWidth=100,Margin=new Thickness(6)};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,MinHeight=38,
        Margin=new Thickness(0,12,0,4)};
    readonly DispatcherTimer debounce;
    string inkColour="#000000";
    SignatureAsset? preparedAsset;
    byte[]? preparedBytes;
    int generation;
    bool tracing;
    bool closed;

    public byte[]? ResultBytes {get;private set;}

    internal static byte[] ReadFile(string path)
    {
        var file=new FileInfo(path);
        if(file.Length==0 || file.Length>10*1024*1024)
            throw new InvalidDataException("Choose an image file smaller than 10 MB.");
        return File.ReadAllBytes(path);
    }

    public static byte[]? Choose(Window owner,byte[] bytes,string title,bool traceByDefault)
    {
        var window=new SignatureImportWindow(bytes,traceByDefault){Owner=owner,Title=title};
        return window.ShowDialog()==true?window.ResultBytes:null;
    }

    public SignatureImportWindow(byte[] bytes,bool traceByDefault)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if(bytes.Length==0 || bytes.Length>10*1024*1024)
            throw new InvalidDataException("Choose an image file smaller than 10 MB.");
        original=SignatureImages.Load(bytes);
        originalBytes=original.Svg is string svg?Encoding.UTF8.GetBytes(svg):bytes.ToArray();
        Title="Review Signature";
        Width=900;Height=600;MinWidth=700;MinHeight=500;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Icon=Branding.CreateIconImageSource();
        var root=new Grid{Margin=new Thickness(18)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var note=new TextBlock
        {
            Text=original.Svg!=null?"Review the SVG artwork before saving it.":
                "Keep the original image, or trace its ink into an SVG signature. Adjust the preview to retain the strokes and remove the background.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)
        };
        root.Children.Add(note);
        var previews=new Grid();
        previews.ColumnDefinitions.Add(new ColumnDefinition());
        previews.ColumnDefinitions.Add(new ColumnDefinition());
        void Preview(int column,string label,Image image)
        {
            var panel=new DockPanel{Margin=column==0?new Thickness(0,0,8,0):new Thickness(8,0,0,0)};
            var caption=new TextBlock{Text=label,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)};
            DockPanel.SetDock(caption,Dock.Top);panel.Children.Add(caption);
            panel.Children.Add(new Border{Background=Brushes.White,BorderBrush=Brushes.DarkGray,BorderThickness=new Thickness(1),
                Padding=new Thickness(14),MinHeight=180,Child=image});
            Grid.SetColumn(panel,column);previews.Children.Add(panel);
        }
        var originalPreview=new Image{Stretch=Stretch.Uniform,Source=DisplaySource(original)};
        Preview(0,"Original",originalPreview);Preview(1,"Result",resultPreview);
        Grid.SetRow(previews,1);root.Children.Add(previews);
        var options=new StackPanel{Margin=new Thickness(0,14,0,0)};
        trace.IsEnabled=original.Svg==null;
        trace.IsChecked=traceByDefault && original.Svg==null;
        options.Children.Add(trace);
        var ink=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
        ink.Children.Add(new TextBlock{Text="Ink threshold",VerticalAlignment=VerticalAlignment.Center});
        ink.Children.Add(threshold);ink.Children.Add(thresholdValue);ink.Children.Add(colour);ink.Children.Add(swatch);
        options.Children.Add(ink);options.Children.Add(lightInk);
        Grid.SetRow(options,2);root.Children.Add(options);
        Grid.SetRow(status,3);root.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",MinWidth=90,IsCancel=true,Margin=new Thickness(6)};
        buttons.Children.Add(export);buttons.Children.Add(use);buttons.Children.Add(cancel);
        Grid.SetRow(buttons,4);root.Children.Add(buttons);
        Content=root;
        AutomationProperties.SetName(trace,"Trace signature to SVG");
        AutomationProperties.SetName(threshold,"Ink threshold");
        AutomationProperties.SetName(lightInk,"Light ink on a dark background");
        NameScope.SetNameScope(this,new NameScope());
        RegisterName(trace.Name,trace);RegisterName(threshold.Name,threshold);RegisterName(lightInk.Name,lightInk);RegisterName(use.Name,use);
        debounce=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(220)};
        debounce.Tick+=async (_,__)=>{debounce.Stop();await UpdateTraceAsync();};
        trace.Checked+=(_,__)=>QueuePreview();trace.Unchecked+=(_,__)=>QueuePreview();
        lightInk.Checked+=(_,__)=>QueuePreview();lightInk.Unchecked+=(_,__)=>QueuePreview();
        threshold.ValueChanged+=(_,__)=>QueuePreview();
        colour.Click+=(_,__)=>
        {
            if(NativeColourDialog.Show(this,inkColour) is not string chosen)return;
            inkColour=chosen;
            swatch.Background=(Brush)new BrushConverter().ConvertFromString(chosen)!;
            QueuePreview();
        };
        use.Click+=(_,__)=>
        {
            if(preparedBytes==null)return;
            ResultBytes=preparedBytes.ToArray();DialogResult=true;
        };
        export.Click+=(_,__)=>
        {
            if(preparedAsset?.Svg is not string svg)return;
            var dialog=new SaveFileDialog{Title="Export Vector Signature",Filter="SVG vector image (*.svg)|*.svg",
                DefaultExt=".svg",AddExtension=true,FileName="signature.svg"};
            if(dialog.ShowDialog(this)!=true)return;
            try {File.WriteAllText(dialog.FileName,svg,new UTF8Encoding(false));}
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Export SVG",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
        Closed+=(_,__)=>{closed=true;generation++;debounce.Stop();};
        QueuePreview();
    }

    static ImageSource DisplaySource(SignatureAsset asset)
        =>asset.Vector is ImageSource vector?vector:asset.Bitmap;

    void QueuePreview()
    {
        if(closed)return;
        generation++;
        debounce.Stop();
        thresholdValue.Text=threshold.Value.ToString("0",System.Globalization.CultureInfo.CurrentCulture);
        var shouldTrace=trace.IsChecked==true && original.Svg==null;
        threshold.IsEnabled=lightInk.IsEnabled=colour.IsEnabled=shouldTrace;
        if(!shouldTrace)
        {
            preparedAsset=original;preparedBytes=originalBytes;
            resultPreview.Source=DisplaySource(original);resultPreview.Opacity=1;
            use.IsEnabled=true;export.IsEnabled=original.Svg!=null;
            status.Text=original.Svg!=null?"SVG artwork will retain its vector source.":"The original image will be kept.";
            return;
        }
        preparedBytes=null;preparedAsset=null;use.IsEnabled=false;export.IsEnabled=false;
        resultPreview.Opacity=0.45;
        status.Text="Updating the vector preview…";
        debounce.Start();
    }

    async Task UpdateTraceAsync()
    {
        if(closed || trace.IsChecked!=true || tracing)return;
        tracing=true;
        var current=generation;
        var inkThreshold=(int)Math.Round(threshold.Value);
        var isLightInk=lightInk.IsChecked==true;
        var colourValue=inkColour;
        try
        {
            var bytes=await Task.Run(()=>SignatureTrace.Trace(original.Bitmap,inkThreshold,isLightInk,colourValue));
            if(closed || current!=generation || trace.IsChecked!=true)return;
            var asset=SignatureImages.Load(bytes);
            preparedAsset=asset;
            preparedBytes=asset.Svg is string svg?Encoding.UTF8.GetBytes(svg):bytes;
            resultPreview.Source=DisplaySource(asset);resultPreview.Opacity=1;
            use.IsEnabled=true;export.IsEnabled=asset.Svg!=null;
            status.Text="The traced signature has a transparent background. Check fine strokes and enclosed spaces before using it.";
        }
        catch(Exception ex)
        {
            if(!closed && current==generation)
            {
                preparedBytes=null;preparedAsset=null;resultPreview.Opacity=1;
                use.IsEnabled=false;export.IsEnabled=false;
                status.Text=ex.Message;
            }
        }
        finally
        {
            tracing=false;
            // Coalesce rapid slider changes rather than running several large
            // traces concurrently. A stale result never becomes selectable.
            if(!closed && current!=generation && trace.IsChecked==true)
            {debounce.Stop();debounce.Start();}
        }
    }
}
