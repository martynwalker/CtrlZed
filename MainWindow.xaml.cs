using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow : Window
{
    enum KeyboardProfile { Word, WordPerfect51, WordStar40 }
    KeyboardProfile profile = KeyboardProfile.Word;
    bool wordStarPrefix;
    bool dirty;
    bool suppressDirty = true;
    bool closingApproved;
    string? currentFile;
    string headerText = "";
    string footerText = "";
    double pageWidth = 794, pageHeight = 1123;
    Thickness pageMargins = new(72);
    readonly DispatcherTimer recoveryTimer;
    readonly string recoveryFile;

    sealed class CzdSettings
    {
        public double PageWidth { get; set; } = 794;
        public double PageHeight { get; set; } = 1123;
        public double Left { get; set; } = 72;
        public double Top { get; set; } = 72;
        public double Right { get; set; } = 72;
        public double Bottom { get; set; } = 72;
        public string Header { get; set; } = "";
        public string Footer { get; set; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,new KeyGesture(Key.S,ModifierKeys.Control)));

        var recoveryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        Directory.CreateDirectory(recoveryDir);
        recoveryFile = Path.Combine(recoveryDir,"Recovery.rtf");
        recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        recoveryTimer.Tick += (_,__) => WriteRecoverySnapshot();
        recoveryTimer.Start();

        ApplyPage();
        RefreshHeaderFooter();
        suppressDirty = false;

        if(File.Exists(recoveryFile))
        {
            var choice = MessageBox.Show("CtrlZed found an automatic recovery snapshot from an earlier session. Recover it now?","Recovery",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(choice == MessageBoxResult.Yes)
            {
                try
                {
                    suppressDirty = true;
                    Editor.Document = new FlowDocument();
                    using var fs = File.OpenRead(recoveryFile);
                    new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(fs,DataFormats.Rtf);
                    WirePictureHandlers();
                    suppressDirty = false;
                    MarkDirty("Recovered document — save it to keep it.");
                }
                catch(Exception ex)
                {
                    suppressDirty = false;
                    MessageBox.Show(ex.Message,"Recovery failed",MessageBoxButton.OK,MessageBoxImage.Error);
                }
            }
            else
            {
                TryDeleteRecovery();
            }
        }
    }

    void New_Click(object s,RoutedEventArgs e)
    {
        if(!CanDiscardChanges())return;
        suppressDirty = true;
        Editor.Document = new FlowDocument();
        currentFile = null;
        headerText = footerText = "";
        RefreshHeaderFooter();
        suppressDirty = false;
        dirty = false;
        UpdateTitle();
        Status.Text = "New document";
        TryDeleteRecovery();
    }

    void Open_Click(object s,RoutedEventArgs e)
    {
        if(!CanDiscardChanges())return;
        var d=new OpenFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|All files (*.*)|*.*"};
        if(d.ShowDialog()!=true)return;
        try
        {
            suppressDirty = true;
            var ext=Path.GetExtension(d.FileName).ToLowerInvariant();
            headerText = footerText = "";
            if(ext==".czd")LoadCzd(d.FileName);
            else if(ext==".txt")Editor.Document=new FlowDocument(new Paragraph(new Run(File.ReadAllText(d.FileName))));
            else
            {
                Editor.Document = new FlowDocument();
                using var fs=File.OpenRead(d.FileName);
                new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(fs,DataFormats.Rtf);
            }
            WirePictureHandlers();
            currentFile=d.FileName;
            dirty=false;
            suppressDirty=false;
            RefreshHeaderFooter();
            UpdateTitle();
            Status.Text="Opened "+currentFile;
            TryDeleteRecovery();
        }
        catch(Exception ex)
        {
            suppressDirty=false;
            MessageBox.Show(ex.Message,"Open failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void Save_Click(object s,RoutedEventArgs e) => SaveCurrent();

    bool SaveCurrent()
    {
        if(currentFile==null)
        {
            var d=new SaveFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt",DefaultExt=".czd",AddExtension=true};
            if(d.ShowDialog()!=true)return false;
            currentFile=d.FileName;
        }

        try
        {
            var ext=Path.GetExtension(currentFile).ToLowerInvariant();
            if(ext==".czd")SaveCzd(currentFile);
            else if(ext==".txt")File.WriteAllText(currentFile,new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text);
            else SaveRtf(currentFile);

            dirty=false;
            UpdateTitle();
            Status.Text="Saved "+currentFile;
            TryDeleteRecovery();
            return true;
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Save failed",MessageBoxButton.OK,MessageBoxImage.Error);
            return false;
        }
    }

    bool CanDiscardChanges()
    {
        if(!dirty)return true;
        var choice=MessageBox.Show("Save changes to this document?","CtrlZed",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(choice==MessageBoxResult.Cancel)return false;
        if(choice==MessageBoxResult.Yes)return SaveCurrent();
        return true;
    }

    void Editor_TextChanged(object sender,TextChangedEventArgs e)
    {
        if(!suppressDirty)MarkDirty();
    }

    void MarkDirty(string? message=null)
    {
        if(!dirty)
        {
            dirty=true;
            UpdateTitle();
        }
        if(message!=null)Status.Text=message;
    }

    void UpdateTitle()
    {
        var name=currentFile==null?"Untitled":Path.GetFileName(currentFile);
        Title=$"CtrlZed — {name}{(dirty?" *":"")}";
    }

    void WriteRecoverySnapshot()
    {
        if(!dirty)return;
        try
        {
            var tmp=recoveryFile+".tmp";
            using(var fs=File.Create(tmp))new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(fs,DataFormats.Rtf);
            File.Move(tmp,recoveryFile,true);
        }
        catch { }
    }

    void TryDeleteRecovery()
    {
        try{if(File.Exists(recoveryFile))File.Delete(recoveryFile);}catch{}
    }

    void SaveRtf(string path)
    {
        var tmp=path+".tmp";
        using(var fs=File.Create(tmp))new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(fs,DataFormats.Rtf);
        ReplaceFile(tmp,path);
    }

    void SaveCzd(string path)
    {
        var tmp=path+".tmp";
        if(File.Exists(tmp))File.Delete(tmp);
        using(var zip=ZipFile.Open(tmp,ZipArchiveMode.Create))
        {
            var doc=zip.CreateEntry("document.rtf");
            using(var s=doc.Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(s,DataFormats.Rtf);
            var settings=zip.CreateEntry("settings.json");
            var data=new CzdSettings
            {
                PageWidth=pageWidth,PageHeight=pageHeight,
                Left=pageMargins.Left,Top=pageMargins.Top,Right=pageMargins.Right,Bottom=pageMargins.Bottom,
                Header=headerText,Footer=footerText
            };
            using(var sw=new StreamWriter(settings.Open()))sw.Write(JsonSerializer.Serialize(data));
        }
        ReplaceFile(tmp,path);
    }

    void LoadCzd(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        var doc=zip.GetEntry("document.rtf")??throw new InvalidDataException("This CtrlZed document has no document.rtf entry.");
        Editor.Document=new FlowDocument();
        using(var s=doc.Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(s,DataFormats.Rtf);
        var st=zip.GetEntry("settings.json");
        if(st!=null)
        {
            using var sr=new StreamReader(st.Open());
            var v=JsonSerializer.Deserialize<CzdSettings>(sr.ReadToEnd());
            if(v!=null)
            {
                pageWidth=v.PageWidth;
                pageHeight=v.PageHeight;
                pageMargins=new Thickness(v.Left,v.Top,v.Right,v.Bottom);
                headerText=v.Header??"";
                footerText=v.Footer??"";
                ApplyPage();
            }
        }
    }

    static void ReplaceFile(string tmp,string path)
    {
        if(File.Exists(path))File.Replace(tmp,path,path+".bak");
        else File.Move(tmp,path);
    }

    void Print_Click(object s,RoutedEventArgs e)
    {
        var pd=new PrintDialog();
        if(pd.ShowDialog()!=true)return;
        var paginator=CreatePreviewPaginator();
        pd.PrintDocument(paginator,"CtrlZed document");
    }

    void PagePreview_Click(object s,RoutedEventArgs e)
    {
        try
        {
            var preview=new PagePreviewWindow(CreatePreviewPaginator()){Owner=this};
            preview.ShowDialog();
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Page preview failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    HeaderFooterPaginator CreatePreviewPaginator()
    {
        var clone=CloneDocument();
        clone.PageWidth=pageWidth;
        clone.PageHeight=pageHeight;
        clone.PagePadding=pageMargins;
        clone.ColumnWidth=double.PositiveInfinity;
        var inner=((IDocumentPaginatorSource)clone).DocumentPaginator;
        inner.PageSize=new Size(pageWidth,pageHeight);
        inner.ComputePageCount();
        return new HeaderFooterPaginator(inner,headerText,footerText,ExpandFields);
    }

    FlowDocument CloneDocument()
    {
        using var stream=new MemoryStream();
        var source=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd);
        source.Save(stream,DataFormats.Rtf);
        stream.Position=0;
        var clone=new FlowDocument();
        new TextRange(clone.ContentStart,clone.ContentEnd).Load(stream,DataFormats.Rtf);
        return clone;
    }

    string ExpandFields(string text,int page,int pages)
    {
        var now=DateTime.Now;
        return text.Replace("{page}",page.ToString(),StringComparison.OrdinalIgnoreCase)
                   .Replace("{pages}",pages.ToString(),StringComparison.OrdinalIgnoreCase)
                   .Replace("{date}",now.ToString("dd MMMM yyyy"),StringComparison.OrdinalIgnoreCase)
                   .Replace("{time}",now.ToString("HH:mm"),StringComparison.OrdinalIgnoreCase)
                   .Replace("{filename}",currentFile==null?"Untitled":Path.GetFileName(currentFile),StringComparison.OrdinalIgnoreCase)
                   .Replace("{moonphase}",GetMoonPhaseName(now),StringComparison.OrdinalIgnoreCase)
                   .Replace("{moonicon}",GetMoonPhaseIcon(now),StringComparison.OrdinalIgnoreCase);
    }

    static double GetMoonAge(DateTime dt)
    {
        var y=dt.Year;
        var m=dt.Month;
        var d=dt.Day;
        if(m<=2){y-=1;m+=12;}
        m+=1;
        var age=((11*(y%19))%30)+m+d-3.5;
        if(y>2000)age-=0.83;
        age%=29.53059;
        if(age<0)age+=29.53059;
        return age;
    }

    static string GetMoonPhaseName(DateTime dt)
    {
        var age=GetMoonAge(dt);
        if(age<1.0||age>28.53)return "New Moon";
        if(age<6.38)return "Waxing Crescent";
        if(age<8.38)return "First Quarter";
        if(age<13.77)return "Waxing Gibbous";
        if(age<15.77)return "Full Moon";
        if(age<21.15)return "Waning Gibbous";
        if(age<23.15)return "Third Quarter";
        return "Waning Crescent";
    }

    static string GetMoonPhaseIcon(DateTime dt)
    {
        var age=GetMoonAge(dt);
        if(age<1.0||age>28.53)return "🌑";
        if(age<6.38)return "🌒";
        if(age<8.38)return "🌓";
        if(age<13.77)return "🌔";
        if(age<15.77)return "🌕";
        if(age<21.15)return "🌖";
        if(age<23.15)return "🌗";
        return "🌘";
    }

    void HeaderFooter_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Header and Footer",Owner=this,Width=520,Height=330,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var grid=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<6;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var h=new TextBox{Text=headerText,Margin=new Thickness(0,4,0,10)};
        var ft=new TextBox{Text=footerText,Margin=new Thickness(0,4,0,10)};
        var help=new TextBlock{Text="Available fields: {page}  {pages}  {date}  {time}  {filename}  {moonphase}  {moonicon}. Page fields are previewed as page 1 in the editor.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};

        AddFullRow(grid,0,new TextBlock{Text="Header"});
        AddFullRow(grid,1,h);
        AddFullRow(grid,2,new TextBlock{Text="Footer"});
        AddFullRow(grid,3,ft);
        AddFullRow(grid,4,help);
        AddFullRow(grid,5,ok);

        ok.Click+=(_,__)=>
        {
            headerText=h.Text;
            footerText=ft.Text;
            RefreshHeaderFooter();
            MarkDirty("Header/footer updated.");
            w.DialogResult=true;
        };
        w.Content=grid;
        w.ShowDialog();
    }

    static void AddFullRow(Grid g,int row,UIElement element)
    {
        Grid.SetRow(element,row);
        g.Children.Add(element);
    }

    void RefreshHeaderFooter()
    {
        HeaderPreview.Text=ExpandFields(headerText,1,1);
        FooterPreview.Text=ExpandFields(footerText,1,1);
        HeaderPreview.Visibility=string.IsNullOrWhiteSpace(headerText)?Visibility.Collapsed:Visibility.Visible;
        FooterPreview.Visibility=string.IsNullOrWhiteSpace(footerText)?Visibility.Collapsed:Visibility.Visible;
    }

    void Picture_Click(object s,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};
        if(d.ShowDialog()!=true)return;
        try
        {
            var b=new BitmapImage();
            b.BeginInit();
            b.CacheOption=BitmapCacheOption.OnLoad;
            b.UriSource=new Uri(d.FileName);
            b.EndInit();
            var img=new Image{Source=b,MaxWidth=Math.Max(100,pageWidth-pageMargins.Left-pageMargins.Right),Stretch=Stretch.Uniform,Margin=new Thickness(4)};
            WirePicture(img);
            var ui=new InlineUIContainer(img,Editor.CaretPosition);
            Editor.CaretPosition=ui.ElementEnd;
            Editor.Focus();
            MarkDirty("Picture inserted — double-click or right-click it for properties.");
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Picture failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void WirePicture(Image img)
    {
        img.Cursor=Cursors.Hand;
        img.ToolTip="Double-click or right-click for picture properties";
        img.MouseLeftButtonDown-=Picture_MouseLeftButtonDown;
        img.MouseLeftButtonDown+=Picture_MouseLeftButtonDown;

        var menu=new ContextMenu();
        var properties=new MenuItem{Header="Picture Properties…"};
        properties.Click+=(_,__)=>ShowPictureProperties(img);
        menu.Items.Add(properties);
        img.ContextMenu=menu;
    }

    void Picture_MouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount!=2 || sender is not Image img)return;
        try
        {
            e.Handled=true;
            ShowPictureProperties(img);
        }
        catch(Exception ex)
        {
            Status.Text="Picture properties could not be opened.";
            MessageBox.Show(ex.Message,"Picture properties",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void WirePictureHandlers()
    {
        foreach(var block in Editor.Document.Blocks)WirePicturesInBlock(block);
    }

    void WirePicturesInBlock(Block block)
    {
        if(block is Paragraph p)
        {
            foreach(var inline in p.Inlines)WirePicturesInInline(inline);
        }
        else if(block is Section section)
        {
            foreach(var child in section.Blocks)WirePicturesInBlock(child);
        }
        else if(block is List list)
        {
            foreach(var item in list.ListItems)
                foreach(var child in item.Blocks)WirePicturesInBlock(child);
        }
        else if(block is Table table)
        {
            foreach(var group in table.RowGroups)
                foreach(var row in group.Rows)
                    foreach(var cell in row.Cells)
                        foreach(var child in cell.Blocks)WirePicturesInBlock(child);
        }
    }

    void WirePicturesInInline(Inline inline)
    {
        if(inline is InlineUIContainer ui && ui.Child is Image img)WirePicture(img);
        else if(inline is Span span)
            foreach(var child in span.Inlines)WirePicturesInInline(child);
    }

    InlineUIContainer? FindContainer(Image target)
    {
        foreach(var block in Editor.Document.Blocks)
        {
            var found=FindContainerInBlock(block,target);
            if(found!=null)return found;
        }
        return null;
    }

    InlineUIContainer? FindContainerInBlock(Block block,Image target)
    {
        if(block is Paragraph p)
        {
            foreach(var inline in p.Inlines)
            {
                var found=FindContainerInInline(inline,target);
                if(found!=null)return found;
            }
        }
        else if(block is Section s)
        {
            foreach(var child in s.Blocks)
            {
                var found=FindContainerInBlock(child,target);
                if(found!=null)return found;
            }
        }
        else if(block is List list)
        {
            foreach(var item in list.ListItems)
                foreach(var child in item.Blocks)
                {
                    var found=FindContainerInBlock(child,target);
                    if(found!=null)return found;
                }
        }
        else if(block is Table table)
        {
            foreach(var group in table.RowGroups)
                foreach(var row in group.Rows)
                    foreach(var cell in row.Cells)
                        foreach(var child in cell.Blocks)
                        {
                            var found=FindContainerInBlock(child,target);
                            if(found!=null)return found;
                        }
        }
        return null;
    }

    InlineUIContainer? FindContainerInInline(Inline inline,Image target)
    {
        if(inline is InlineUIContainer ui && ReferenceEquals(ui.Child,target))return ui;
        if(inline is Span span)
            foreach(var child in span.Inlines)
            {
                var found=FindContainerInInline(child,target);
                if(found!=null)return found;
            }
        return null;
    }

    void ShowPictureProperties(Image img)
    {
        var container=FindContainer(img);
        var paragraph=container?.Parent as Paragraph;
        var currentAlignment=paragraph?.TextAlignment??TextAlignment.Center;

        var w=new Window{Title="Picture Properties",Owner=this,Width=390,Height=310,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(14)};
        for(int i=0;i<6;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(130)});
        g.ColumnDefinitions.Add(new ColumnDefinition());

        var shownWidth=double.IsNaN(img.Width)||img.Width<=0?img.ActualWidth:img.Width;
        if(shownWidth<=0 && img.Source is BitmapSource bitmap)shownWidth=bitmap.PixelWidth;
        var width=new TextBox{Text=Math.Max(1,shownWidth).ToString("0")};
        var align=new ComboBox{ItemsSource=new[]{"Left","Centre","Right"},SelectedIndex=currentAlignment==TextAlignment.Left?0:currentAlignment==TextAlignment.Right?2:1,Margin=new Thickness(0,5,0,5)};
        var keep=new CheckBox{Content="Keep aspect ratio",IsChecked=true,Margin=new Thickness(0,8,0,8)};
        var caption=new TextBox{Margin=new Thickness(0,4,0,8)};
        var note=new TextBlock{Text="Pictures remain inline. Alignment applies to the containing paragraph; full floating text wrap is deliberately deferred.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
        var ok=new Button{Content="Apply",Width=85,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};

        AddRow(g,0,"Width (pixels)",width);
        AddRow(g,1,"Alignment",align);
        Grid.SetRow(keep,2);Grid.SetColumnSpan(keep,2);g.Children.Add(keep);
        AddRow(g,3,"Caption",caption);
        Grid.SetRow(note,4);Grid.SetColumnSpan(note,2);g.Children.Add(note);
        Grid.SetRow(ok,5);Grid.SetColumnSpan(ok,2);g.Children.Add(ok);

        ok.Click+=(_,__)=>
        {
            if(double.TryParse(width.Text,out var px)&&px>10)
            {
                if(keep.IsChecked==true&&img.Source is BitmapSource bs&&bs.PixelWidth>0)img.Height=px*bs.PixelHeight/bs.PixelWidth;
                img.Width=px;
            }
            if(paragraph!=null)paragraph.TextAlignment=align.SelectedIndex==0?TextAlignment.Left:align.SelectedIndex==2?TextAlignment.Right:TextAlignment.Center;

            if(!string.IsNullOrWhiteSpace(caption.Text) && paragraph!=null)
            {
                var next=paragraph.NextBlock;
                var cap=new Paragraph(new Run(caption.Text))
                {
                    TextAlignment=paragraph.TextAlignment,
                    FontSize=10*96/72,
                    FontStyle=FontStyles.Italic,
                    Foreground=Brushes.DimGray,
                    Margin=new Thickness(0,3,0,8)
                };
                if(next!=null)Editor.Document.Blocks.InsertBefore(next,cap);
                else Editor.Document.Blocks.Add(cap);
            }
            MarkDirty("Picture properties updated.");
            w.DialogResult=true;
        };
        w.Content=g;
        w.ShowDialog();
    }

    static void AddRow(Grid g,int row,string label,Control control)
    {
        var t=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetRow(t,row);Grid.SetColumn(t,0);
        Grid.SetRow(control,row);Grid.SetColumn(control,1);
        g.Children.Add(t);g.Children.Add(control);
    }

    void PageSetup_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Page Setup",Owner=this,Width=390,Height=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var p=new StackPanel{Margin=new Thickness(16)};
        var paper=new ComboBox{ItemsSource=new[]{"A4 Portrait","A4 Landscape","A5 Portrait","US Letter"},SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
        p.Children.Add(new TextBlock{Text="Paper"});p.Children.Add(paper);
        TextBox Box(double dip)=>new(){Text=(dip*25.4/96).ToString("0.0"),Margin=new Thickness(0,2,0,6)};
        TextBox l=Box(pageMargins.Left);TextBox t=Box(pageMargins.Top);TextBox r=Box(pageMargins.Right);TextBox b=Box(pageMargins.Bottom);
        foreach(var pair in new[]{("Left margin (mm)",l),("Top margin (mm)",t),("Right margin (mm)",r),("Bottom margin (mm)",b)})
        {
            p.Children.Add(new TextBlock{Text=pair.Item1});
            p.Children.Add(pair.Item2);
        }
        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};
        p.Children.Add(ok);
        ok.Click+=(_,__)=>
        {
            (pageWidth,pageHeight)=paper.SelectedIndex switch{1=>(1123d,794d),2=>(559d,794d),3=>(816d,1056d),_=>(794d,1123d)};
            double mm(TextBox x)=>double.TryParse(x.Text,out var v)?v*96/25.4:72;
            pageMargins=new Thickness(mm(l),mm(t),mm(r),mm(b));
            ApplyPage();
            MarkDirty("Page setup updated.");
            w.DialogResult=true;
        };
        w.Content=p;
        w.ShowDialog();
    }

    void ApplyPage()
    {
        PageBorder.Width=pageWidth;
        PageBorder.MinHeight=pageHeight;
        PageBorder.Padding=pageMargins;
        Editor.Document.PageWidth=pageWidth;
        Editor.Document.PageHeight=pageHeight;
        Editor.Document.PagePadding=pageMargins;
    }

    void StyleMenu_Click(object s,RoutedEventArgs e)
    {
        if(s is MenuItem m && m.Tag is string style)ApplyParagraphStyle(style);
    }

    void StyleBox_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(!IsLoaded || StyleBox.SelectedItem is not ComboBoxItem item)return;
        if(item.Content is string style)ApplyParagraphStyle(style);
    }

    void ApplyParagraphStyle(string style)
    {
        if(Editor==null)return;
        var selection=Editor.Selection;
        selection.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily("Calibri"));
        selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Normal);
        selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Normal);
        selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.Black);
        selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty,TextAlignment.Left);
        selection.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(0,0,0,8));

        switch(style)
        {
            case "Heading 1":
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,24d);
                selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);
                selection.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(0,16,0,8));
                break;
            case "Heading 2":
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,20d);
                selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);
                selection.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(0,12,0,6));
                break;
            case "Heading 3":
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,17d);
                selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.SemiBold);
                selection.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(0,10,0,5));
                break;
            case "Quote":
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,16d);
                selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
                selection.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(36,8,36,8));
                selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DimGray);
                break;
            case "Caption":
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,13.333d);
                selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
                selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty,TextAlignment.Center);
                selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DimGray);
                break;
            default:
                selection.ApplyPropertyValue(TextElement.FontSizeProperty,16d);
                break;
        }
        MarkDirty($"Style: {style}");
        Editor.Focus();
    }

    void MacroLibrary_Click(object s,RoutedEventArgs e)
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");
        Directory.CreateDirectory(dir);
        var w=new Window{Title="CtrlZed Macro Library — source only",Owner=this,Width=780,Height=560,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var grid=new Grid{Margin=new Thickness(10)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left=new DockPanel{Margin=new Thickness(0,0,10,0)};
        var list=new ListBox();
        DockPanel.SetDock(list,Dock.Top);
        left.Children.Add(list);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};
        Button nv=new(){Content="New VB",Margin=new Thickness(0,0,5,0)};
        Button nc=new(){Content="New C#"};
        buttons.Children.Add(nv);buttons.Children.Add(nc);
        DockPanel.SetDock(buttons,Dock.Bottom);
        left.Children.Add(buttons);
        Grid.SetColumn(left,0);
        grid.Children.Add(left);

        var right=new DockPanel();
        var warning=new TextBlock{Text="Macro execution is still disabled in beta 4 while the sandbox is being designed. This editor stores source in your central library.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        DockPanel.SetDock(warning,Dock.Top);
        right.Children.Add(warning);
        var save=new Button{Content="Save source",Width=100,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};
        DockPanel.SetDock(save,Dock.Bottom);
        right.Children.Add(save);
        var code=new TextBox{AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
        right.Children.Add(code);
        Grid.SetColumn(right,1);
        grid.Children.Add(right);

        string? selected=null;
        void refresh(){list.ItemsSource=Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x=>x).ToList();}
        list.SelectionChanged+=(_,__)=>
        {
            selected=list.SelectedItem?.ToString();
            if(selected!=null)code.Text=File.ReadAllText(Path.Combine(dir,selected));
        };
        void make(string ext,string starter)
        {
            var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+ext;
            File.WriteAllText(Path.Combine(dir,name),starter);
            refresh();
            list.SelectedItem=name;
        }
        nv.Click+=(_,__)=>make(".vb","' CtrlZed VB.NET macro source\n' Execution is disabled in beta 4.\n");
        nc.Click+=(_,__)=>make(".cs","// CtrlZed C# macro source\n// Execution is disabled in beta 4.\n");
        save.Click+=(_,__)=>
        {
            if(selected!=null)
            {
                File.WriteAllText(Path.Combine(dir,selected),code.Text);
                Status.Text="Macro source saved to central library.";
            }
        };
        refresh();
        w.Content=grid;
        w.ShowDialog();
    }

    void Keyboard_Click(object s,RoutedEventArgs e)
    {
        WordProfile.IsChecked=WPProfile.IsChecked=WSProfile.IsChecked=false;
        if(s==WPProfile)
        {
            profile=KeyboardProfile.WordPerfect51;
            WPProfile.IsChecked=true;
            Status.Text="Keyboard: WordPerfect 5.1 (initial DOS subset)";
        }
        else if(s==WSProfile)
        {
            profile=KeyboardProfile.WordStar40;
            WSProfile.IsChecked=true;
            Status.Text="Keyboard: WordStar 4.0 (initial subset)";
        }
        else
        {
            profile=KeyboardProfile.Word;
            WordProfile.IsChecked=true;
            Status.Text="Keyboard: Microsoft Word";
        }
        wordStarPrefix=false;
    }

    void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(profile==KeyboardProfile.WordStar40)
        {
            if(wordStarPrefix)
            {
                wordStarPrefix=false;
                if(e.Key==Key.S){SaveCurrent();e.Handled=true;}
                else if(e.Key==Key.X){if(SaveCurrent()){closingApproved=true;Close();}e.Handled=true;}
                return;
            }
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.K)
            {
                wordStarPrefix=true;
                Status.Text="WordStar ^K …";
                e.Handled=true;
                return;
            }
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var cmd=e.Key switch
                {
                    Key.E=>EditingCommands.MoveUpByLine,
                    Key.X=>EditingCommands.MoveDownByLine,
                    Key.S=>EditingCommands.MoveLeftByCharacter,
                    Key.D=>EditingCommands.MoveRightByCharacter,
                    _=>null
                };
                if(cmd!=null){cmd.Execute(null,Editor);e.Handled=true;}
            }
        }
        else if(profile==KeyboardProfile.WordPerfect51)
        {
            if(e.Key==Key.F3){About_Click(this,new RoutedEventArgs());e.Handled=true;}
            else if(e.Key==Key.F7&&!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){Close();e.Handled=true;}
            else if(e.Key==Key.F7&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){Print_Click(this,new RoutedEventArgs());e.Handled=true;}
            else if(e.Key==Key.F6&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){EditingCommands.AlignCenter.Execute(null,Editor);e.Handled=true;}
        }
    }

    void FontBox_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(FontBox.SelectedItem is FontFamily f)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,f);
            if(!suppressDirty)MarkDirty();
        }
    }

    void SizeBox_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(SizeBox.SelectedItem is ComboBoxItem i&&double.TryParse(i.Content?.ToString(),out var pt))
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,pt*96/72);
            if(!suppressDirty)MarkDirty();
        }
    }

    void Exit_Click(object s,RoutedEventArgs e)=>Close();

    void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(closingApproved)return;
        if(!CanDiscardChanges()){e.Cancel=true;return;}
        recoveryTimer.Stop();
        TryDeleteRecovery();
        closingApproved=true;
    }

    void About_Click(object s,RoutedEventArgs e)=>MessageBox.Show(
        "CtrlZed 0.4.0 beta 4\n\nThe word processor that knows its place.\n\nHuman-directed. AI-authored project code. Open source under MIT.\n\nBeta 4 adds paginated preview, repeating print headers/footers and calculated fields including moon phase.",
        "About CtrlZed");
}
