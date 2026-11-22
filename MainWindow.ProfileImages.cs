using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    void ProfileImages_Click(object sender, RoutedEventArgs e) => ShowProfileImages();

    void ShowProfileImages()
    {
        var window = new Window
        {
            Title = "CtrlZed Profile Images",
            Owner = this,
            Width = 830,
            Height = 540,
            MinWidth = 760,
            MinHeight = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Branding.CreateIconImageSource()
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var list = new ListBox { Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(list, 0); Grid.SetColumn(list, 0); root.Children.Add(list);

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        form.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 5; i++) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition());

        var name = new TextBox { Margin = new Thickness(0, 3, 0, 9) };
        var fileName = new TextBlock { Text = "No image selected", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 9) };
        var preview = new Border { Height = 220, BorderBrush = Brushes.DarkGray, BorderThickness = new Thickness(1), Background = Brushes.WhiteSmoke, Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 8) };
        var previewImage = new Image { Stretch = Stretch.Uniform, MaxWidth = 400, MaxHeight = 200 };
        preview.Child = previewImage;

        AddProfileImageRow(form, 0, "Name", name);
        AddProfileImageRow(form, 1, "Image file", fileName);
        AddProfileImageRow(form, 2, "Preview", preview);
        var note=new TextBlock{Text="Upload SVG artwork directly, or use Trace Signature to turn an existing PNG or JPEG signature into a vector image.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,0)};
        Grid.SetRow(note,3);Grid.SetColumnSpan(note,2);form.Children.Add(note);
        Grid.SetRow(form, 0); Grid.SetColumn(form, 1); root.Children.Add(form);

        byte[]? pendingBytes = null;

        void RefreshList(string? select = null)
        {
            list.ItemsSource = ProfileImageStore.Names();
            if (select is not null) list.SelectedItem = list.Items.Cast<string>().FirstOrDefault(x => string.Equals(x, select, StringComparison.OrdinalIgnoreCase));
        }

        void ShowStored(string imageName)
        {
            try
            {
                name.Text = imageName;
                pendingBytes = null;
                var asset=ProfileImageStore.LoadAsset(imageName);
                fileName.Text = asset.Svg!=null?"Stored SVG vector image":"Stored in the CtrlZed profile";
                previewImage.Source = asset.Vector is ImageSource vector?vector:asset.Bitmap;
            }
            catch (Exception ex)
            {
                previewImage.Source = null;
                MessageBox.Show(window, ex.Message, "Profile image", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        list.SelectionChanged += (_, __) =>
        {
            if (list.SelectedItem is string imageName) ShowStored(imageName);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var choose = new Button { Content = "Upload Image…", MinWidth = 105, Margin = new Thickness(4) };
        var trace = new Button { Content = "Trace Signature…", MinWidth = 110, Margin = new Thickness(4) };
        var export = new Button { Content = "Export SVG…", MinWidth = 95, Margin = new Thickness(4) };
        var save = new Button { Content = "Save Image", MinWidth = 95, Margin = new Thickness(4), IsDefault = true };
        var delete = new Button { Content = "Delete", MinWidth = 75, Margin = new Thickness(4) };
        var close = new Button { Content = "Close", MinWidth = 75, Margin = new Thickness(4), IsCancel = true };
        buttons.Children.Add(choose);buttons.Children.Add(trace);buttons.Children.Add(export);
        buttons.Children.Add(save); buttons.Children.Add(delete); buttons.Children.Add(close);
        Grid.SetRow(buttons, 1); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons);

        choose.Click += (_, __) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose a profile image",
                Filter = "Image files|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif|SVG vector image (*.svg)|*.svg|All files|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                var bytes = SignatureImportWindow.ReadFile(dialog.FileName);
                var selected=SignatureImportWindow.Choose(window,bytes,"Review Profile Image",false);
                if(selected==null)return;
                var asset=SignatureImages.Load(selected);
                pendingBytes = selected;
                fileName.Text = Path.GetFileName(dialog.FileName);
                if (string.IsNullOrWhiteSpace(name.Text)) name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                previewImage.Source = asset.Vector is ImageSource vector?vector:asset.Bitmap;
            }
            catch (Exception ex)
            {
                pendingBytes = null;
                MessageBox.Show(window, ex.Message, "Image upload failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        trace.Click+=(_,__)=>
        {
            try
            {
                var bytes=pendingBytes ?? (list.SelectedItem is string imageName?ProfileImageStore.ReadBytes(imageName):null);
                if(bytes==null)throw new InvalidOperationException("Upload an image or select a stored signature first.");
                var selected=SignatureImportWindow.Choose(window,bytes,"Trace Signature",true);
                if(selected==null)return;
                var asset=SignatureImages.Load(selected);
                pendingBytes=selected;
                previewImage.Source=asset.Vector is ImageSource vector?vector:asset.Bitmap;
                fileName.Text=asset.Svg!=null?"Vector signature ready — choose Save Image to keep it":"Image ready — choose Save Image to keep it";
            }
            catch(Exception ex){MessageBox.Show(window,ex.Message,"Trace Signature",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };

        export.Click+=(_,__)=>
        {
            try
            {
                var bytes=pendingBytes ?? (list.SelectedItem is string imageName?ProfileImageStore.ReadBytes(imageName):null);
                if(bytes==null || SignatureImages.Load(bytes).Svg is not string svg)
                    throw new InvalidOperationException("Select an SVG image or trace a signature before exporting it.");
                var dialog=new SaveFileDialog{Title="Export Vector Signature",Filter="SVG vector image (*.svg)|*.svg",
                    DefaultExt=".svg",AddExtension=true,FileName=string.IsNullOrWhiteSpace(name.Text)?"signature.svg":name.Text.Trim()+".svg"};
                if(dialog.ShowDialog(window)==true)File.WriteAllText(dialog.FileName,svg,new System.Text.UTF8Encoding(false));
            }
            catch(Exception ex){MessageBox.Show(window,ex.Message,"Export SVG",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };

        save.Click += (_, __) =>
        {
            try
            {
                if (pendingBytes is null) throw new InvalidOperationException("Choose an image file with Upload Image first.");
                var imageName = name.Text.Trim();
                ProfileImageStore.Save(imageName, pendingBytes);
                pendingBytes = null;
                RefreshList(imageName);
                fileName.Text = "Stored in the CtrlZed profile";
                Status.Text = $"Profile image saved: {{{imageName}}}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(window, ex.Message, "Save profile image", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        delete.Click += (_, __) =>
        {
            var imageName = name.Text.Trim();
            if (string.IsNullOrWhiteSpace(imageName)) return;
            if (MessageBox.Show(window, $"Delete the profile image '{imageName}'?", "Delete profile image", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ProfileImageStore.Delete(imageName);
            pendingBytes = null;
            name.Clear(); fileName.Text = "No image selected"; previewImage.Source = null;
            RefreshList();
            Status.Text = $"Profile image deleted: {imageName}";
        };

        close.Click += (_, __) => window.Close();
        Grid.SetColumnSpan(name, 1);
        RefreshList();
        window.Content = root;
        window.ShowDialog();
    }

    static void AddProfileImageRow(Grid grid, int row, string label, FrameworkElement control)
    {
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0), FontWeight = FontWeights.SemiBold };
        Grid.SetRow(caption, row); Grid.SetColumn(caption, 0); grid.Children.Add(caption);
        Grid.SetRow(control, row); Grid.SetColumn(control, 1); grid.Children.Add(control);
    }

    public void InsertProfileImage(string imageName, double widthMillimetres, PicturePlacementMode placement)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => InsertProfileImage(imageName, widthMillimetres, placement));
            return;
        }
        if (Editor.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
        if (!double.IsFinite(widthMillimetres) || widthMillimetres <= 0 || widthMillimetres > 500)
            throw new InvalidOperationException("Image width must be greater than 0 and no more than 500 mm.");

        var asset = ProfileImageStore.LoadAsset(imageName);
        var width = widthMillimetres * 96.0 / 25.4;
        var height = width * asset.Height / asset.Width;
        var image = new Image
        {
            Source = asset.Bitmap, Width = width, Height = height,
            Stretch = Stretch.Uniform, IsHitTestVisible = false, Focusable = false
        };
        VectorImageSupport.SetAsset(image,asset);
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var insertion = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? Editor.Document.ContentEnd;
        Editor.BeginChange();
        try
        {
            var container = new InlineUIContainer(image, insertion) { BaselineAlignment = BaselineAlignment.Baseline };
            WirePictureContainer(container, image);
            var next = container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            if (next is not null) Editor.CaretPosition = next;
            if (placement != PicturePlacementMode.Inline) SetPicturePlacement(image, placement);
        }
        finally {Editor.EndChange();}
        MarkDirty($"Profile image inserted: {{{imageName}}}.");
        UpdateDocumentStats();
        Editor.Focus();
    }
}
