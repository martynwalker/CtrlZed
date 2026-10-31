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
    readonly Dictionary<string,string> customFields = new(StringComparer.OrdinalIgnoreCase);

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
        public Dictionary<string,string> CustomFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,new KeyGesture(Key.S,ModifierKeys.Control)));
        Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
        Editor.PreviewMouseRightButtonDown += Editor_PreviewMouseRightButtonDown;

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
        customFields.Clear();
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
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if(PageGuides!=null)PageGuides.InvalidateVisual();
        }),DispatcherPriority.Background);
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
                Header=headerText,Footer=footerText,
                CustomFields=new Dictionary<string,string>(customFields,StringComparer.OrdinalIgnoreCase)
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
                customFields.Clear();
                if(v.CustomFields!=null)
                    foreach(var pair in v.CustomFields)
                        if(!string.IsNullOrWhiteSpace(pair.Key))
                            customFields[pair.Key]=pair.Value??"";
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
        return ExpandFieldsCore(text??"",page,pages,0);
    }

    string ExpandFieldsCore(string text,int page,int pages,int depth)
    {
        if(depth>8)return text;
        var now=DateTime.Now;
        var result=text.Replace("{page}",page.ToString(),StringComparison.OrdinalIgnoreCase)
                       .Replace("{pages}",pages.ToString(),StringComparison.OrdinalIgnoreCase)
                       .Replace("{date}",now.ToString("dd MMMM yyyy"),StringComparison.OrdinalIgnoreCase)
                       .Replace("{time}",now.ToString("HH:mm"),StringComparison.OrdinalIgnoreCase)
                       .Replace("{filename}",currentFile==null?"Untitled":Path.GetFileName(currentFile),StringComparison.OrdinalIgnoreCase)
                       .Replace("{moonphase}",GetMoonPhaseName(now),StringComparison.OrdinalIgnoreCase)
                       .Replace("{moonicon}",GetMoonPhaseIcon(now),StringComparison.OrdinalIgnoreCase);

        foreach(var pair in customFields)
        {
            var token="{field:"+pair.Key+"}";
            if(result.Contains(token,StringComparison.OrdinalIgnoreCase))
                result=result.Replace(token,ExpandFieldsCore(pair.Value,page,pages,depth+1),StringComparison.OrdinalIgnoreCase);
        }
        return result;
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

    void Fields_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="CtrlZed Fields",Owner=this,Width=620,Height=430,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var root=new Grid{Margin=new Thickness(12)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var list=new ListBox{Margin=new Thickness(0,0,10,0)};
        Grid.SetColumn(list,0);Grid.SetRow(list,0);root.Children.Add(list);

        var edit=new Grid();
        edit.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        edit.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        edit.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        edit.RowDefinitions.Add(new RowDefinition());
        edit.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});
        edit.ColumnDefinitions.Add(new ColumnDefinition());

        var name=new TextBox{Margin=new Thickness(0,3,0,8)};
        var value=new TextBox{Margin=new Thickness(0,3,0,8),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110};
        AddRow(edit,0,"Name",name);
        AddRow(edit,1,"Template",value);
        var hint=new TextBlock
        {
            Text="Use built-ins such as {date}, {filename}, {moonphase}, or another custom field as {field:name}. Insert it in a header/footer as {field:name}.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,4,0,8)
        };
        Grid.SetRow(hint,2);Grid.SetColumnSpan(hint,2);edit.Children.Add(hint);
        Grid.SetColumn(edit,1);Grid.SetRow(edit,0);root.Children.Add(edit);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var save=new Button{Content="Save field",Width=90,Margin=new Thickness(4)};
        var remove=new Button{Content="Delete",Width=75,Margin=new Thickness(4)};
        var moon=new Button{Content="Moon example",Width=105,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=75,Margin=new Thickness(4)};
        buttons.Children.Add(moon);buttons.Children.Add(remove);buttons.Children.Add(save);buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);Grid.SetColumnSpan(buttons,2);Grid.SetRow(buttons,1);root.Children.Add(buttons);

        void RefreshList()
        {
            list.ItemsSource=customFields.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        }

        list.SelectionChanged+=(_,__)=>
        {
            if(list.SelectedItem is string key && customFields.TryGetValue(key,out var template))
            {
                name.Text=key;
                value.Text=template;
            }
        };

        save.Click+=(_,__)=>
        {
            var key=name.Text.Trim();
            if(string.IsNullOrWhiteSpace(key) || key.Any(ch=>!(char.IsLetterOrDigit(ch)||ch=='_'||ch=='-')))
            {
                MessageBox.Show("Field names may contain letters, numbers, hyphens and underscores.","Invalid field",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            customFields[key]=value.Text;
            RefreshList();
            list.SelectedItem=key;
            RefreshHeaderFooter();
            MarkDirty("Custom field saved.");
        };

        remove.Click+=(_,__)=>
        {
            if(list.SelectedItem is string key && customFields.Remove(key))
            {
                name.Clear();value.Clear();RefreshList();RefreshHeaderFooter();MarkDirty("Custom field deleted.");
            }
        };

        moon.Click+=(_,__)=>
        {
            name.Text="moon";
            value.Text="{moonicon} {moonphase}";
        };

        close.Click+=(_,__)=>w.Close();
        RefreshList();
        w.Content=root;
        w.ShowDialog();
    }

    void HeaderFooter_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Header and Footer",Owner=this,Width=520,Height=330,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var grid=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<6;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var h=new TextBox{Text=headerText,Margin=new Thickness(0,4,0,10)};
        var ft=new TextBox{Text=footerText,Margin=new Thickness(0,4,0,10)};
        var help=new TextBlock{Text="Built-ins: {page}  {pages}  {date}  {time}  {filename}  {moonphase}  {moonicon}. Custom fields use {field:name}. Page fields are previewed as page 1 in the editor.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray};
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
        if(PageGuides==null)return;
        PageGuides.Header=headerText;
        PageGuides.Footer=footerText;
        PageGuides.Expand=ExpandFields;
        PageGuides.InvalidateVisual();
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
            var ui=new InlineUIContainer(img,Editor.CaretPosition);
            WirePictureContainer(ui,img);
            Editor.CaretPosition=ui.ElementEnd;
            Editor.Focus();
            MarkDirty("Picture inserted — double-click or right-click it for properties.");
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Picture failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void Editor_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        var img=FindPictureAtPoint(e.GetPosition(Editor));
        if(img==null)return;
        if(e.ClickCount==2)
        {
            e.Handled=true;
            ShowPicturePropertiesSafely(img);
        }
    }

    void Editor_PreviewMouseRightButtonDown(object sender,MouseButtonEventArgs e)
    {
        var img=FindPictureAtPoint(e.GetPosition(Editor));
        if(img==null)return;

        e.Handled=true;
        var menu=new ContextMenu();
        var properties=new MenuItem{Header="Picture Properties…"};
        properties.Click+=(_,__)=>ShowPicturePropertiesSafely(img);
        menu.Items.Add(properties);
        menu.PlacementTarget=Editor;
        menu.IsOpen=true;
    }

    Image? FindPictureAtPoint(Point point)
    {
        foreach(var img in EnumeratePictures())
        {
            if(!img.IsVisible || img.ActualWidth<=0 || img.ActualHeight<=0)continue;
            try
            {
                var origin=img.TranslatePoint(new Point(0,0),Editor);
                var rect=new Rect(origin,new Size(img.ActualWidth,img.ActualHeight));
                if(rect.Contains(point))return img;
            }
            catch(InvalidOperationException)
            {
                // The image is not currently connected to Editor's visual tree.
            }
        }
        return null;
    }

    IEnumerable<Image> EnumeratePictures()
    {
        foreach(var block in Editor.Document.Blocks)
            foreach(var image in EnumeratePicturesInBlock(block))
                yield return image;
    }

    IEnumerable<Image> EnumeratePicturesInBlock(Block block)
    {
        if(block is Paragraph p)
        {
            foreach(var inline in p.Inlines)
                foreach(var image in EnumeratePicturesInInline(inline))
                    yield return image;
        }
        else if(block is Section section)
        {
            foreach(var child in section.Blocks)
                foreach(var image in EnumeratePicturesInBlock(child))
                    yield return image;
        }
        else if(block is List list)
        {
            foreach(var item in list.ListItems)
                foreach(var child in item.Blocks)
                    foreach(var image in EnumeratePicturesInBlock(child))
                        yield return image;
        }
        else if(block is Table table)
        {
            foreach(var group in table.RowGroups)
                foreach(var row in group.Rows)
                    foreach(var cell in row.Cells)
                        foreach(var child in cell.Blocks)
                            foreach(var image in EnumeratePicturesInBlock(child))
                                yield return image;
        }
    }

    IEnumerable<Image> EnumeratePicturesInInline(Inline inline)
    {
        if(inline is InlineUIContainer ui && ui.Child is Image img)
            yield return img;
        else if(inline is Span span)
            foreach(var child in span.Inlines)
                foreach(var image in EnumeratePicturesInInline(child))
                    yield return image;
    }

    void ShowPicturePropertiesSafely(Image img)
    {
        try
        {
            ShowPictureProperties(img);
        }
        catch(Exception ex)
        {
            Status.Text="Picture properties could not be opened.";
            MessageBox.Show(ex.Message,"Picture properties",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void WirePicture(Image img)
    {
        img.Cursor=Cursors.Hand;
        img.ToolTip="Double-click or right-click for picture properties";
        img.PreviewMouseLeftButtonDown-=Picture_MouseLeftButtonDown;
        img.PreviewMouseLeftButtonDown+=Picture_MouseLeftButtonDown;

        var menu=new ContextMenu();
        var properties=new MenuItem{Header="Picture Properties…"};
        properties.Click+=(_,__)=>ShowPictureProperties(img);
        menu.Items.Add(properties);
        img.ContextMenu=menu;
    }

    void WirePictureContainer(InlineUIContainer ui,Image img)
    {
        WirePicture(img);
        ui.PreviewMouseLeftButtonDown-=PictureContainer_PreviewMouseLeftButtonDown;
        ui.PreviewMouseLeftButtonDown+=PictureContainer_PreviewMouseLeftButtonDown;

        var menu=new ContextMenu();
        var properties=new MenuItem{Header="Picture Properties…"};
        properties.Click+=(_,__)=>ShowPictureProperties(img);
        menu.Items.Add(properties);
        ui.ContextMenu=menu;
    }

    void PictureContainer_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount!=2 || sender is not InlineUIContainer ui || ui.Child is not Image img)return;
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

    void Picture_MouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount!=2 || sender is not Image img)return;
        try
        {
            e.Handled=true;
            ShowPicturePropertiesSafely(img);
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
        if(inline is InlineUIContainer ui && ui.Child is Image img)WirePictureContainer(ui,img);
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
        if(PageGuides!=null)
        {
            PageGuides.PageContentHeight=Math.Max(180,pageHeight-pageMargins.Top-pageMargins.Bottom);
            PageGuides.Header=headerText;
            PageGuides.Footer=footerText;
            PageGuides.Expand=ExpandFields;
            PageGuides.InvalidateVisual();
        }
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
        var w=new Window{Title="CtrlZed Macro Library",Owner=this,Width=820,Height=590,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var grid=new Grid{Margin=new Thickness(10)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(205)});
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left=new DockPanel{Margin=new Thickness(0,0,10,0)};
        var list=new ListBox();
        DockPanel.SetDock(list,Dock.Top);
        left.Children.Add(list);

        var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};
        var ns=new Button{Content="New Safe",Margin=new Thickness(0,0,5,5)};
        var nv=new Button{Content="New VB",Margin=new Thickness(0,0,5,5)};
        var nc=new Button{Content="New C#",Margin=new Thickness(0,0,5,5)};
        buttons.Children.Add(ns);buttons.Children.Add(nv);buttons.Children.Add(nc);
        DockPanel.SetDock(buttons,Dock.Bottom);
        left.Children.Add(buttons);
        Grid.SetColumn(left,0);
        grid.Children.Add(left);

        var right=new DockPanel();
        var warning=new TextBlock
        {
            Text="Safe .czm macros can run because CtrlZed interprets a restricted command set. C# and VB.NET remain source-only: arbitrary .NET code is not sandboxed by Roslyn.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8),Foreground=Brushes.DimGray
        };
        DockPanel.SetDock(warning,Dock.Top);
        right.Children.Add(warning);

        var actionBar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};
        var run=new Button{Content="Run safe macro",Width=110,Margin=new Thickness(4)};
        var save=new Button{Content="Save source",Width=100,Margin=new Thickness(4)};
        actionBar.Children.Add(run);actionBar.Children.Add(save);
        DockPanel.SetDock(actionBar,Dock.Bottom);
        right.Children.Add(actionBar);

        var code=new TextBox
        {
            AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        right.Children.Add(code);
        Grid.SetColumn(right,1);
        grid.Children.Add(right);

        string? selected=null;
        void refresh()
        {
            list.ItemsSource=Directory.GetFiles(dir)
                .Where(p=>new[]{".czm",".vb",".cs"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFileName).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        }

        list.SelectionChanged+=(_,__)=>
        {
            selected=list.SelectedItem?.ToString();
            if(selected!=null)code.Text=File.ReadAllText(Path.Combine(dir,selected));
            run.IsEnabled=selected!=null && Path.GetExtension(selected).Equals(".czm",StringComparison.OrdinalIgnoreCase);
        };

        void make(string ext,string starter)
        {
            var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+ext;
            File.WriteAllText(Path.Combine(dir,name),starter);
            refresh();
            list.SelectedItem=name;
        }

        ns.Click+=(_,__)=>make(".czm",
@"# CtrlZed Safe Macro
# Approved commands:
# INSERT_TEXT text
# INSERT_FIELD name
# NEW_PARAGRAPH
# APPLY_STYLE Normal|Heading 1|Heading 2|Heading 3|Quote|Caption
# SET_HEADER text
# SET_FOOTER text
INSERT_TEXT Hello from a CtrlZed safe macro
NEW_PARAGRAPH
INSERT_FIELD date
");
        nv.Click+=(_,__)=>make(".vb","' CtrlZed VB.NET macro source\n' Stored only. Execution is disabled until a real OS sandbox exists.\n");
        nc.Click+=(_,__)=>make(".cs","// CtrlZed C# macro source\n// Stored only. Execution is disabled until a real OS sandbox exists.\n");

        save.Click+=(_,__)=>
        {
            if(selected!=null)
            {
                File.WriteAllText(Path.Combine(dir,selected),code.Text);
                Status.Text="Macro source saved to central library.";
            }
        };

        run.Click+=(_,__)=>
        {
            if(selected==null || !Path.GetExtension(selected).Equals(".czm",StringComparison.OrdinalIgnoreCase))return;
            File.WriteAllText(Path.Combine(dir,selected),code.Text);
            try
            {
                RunSafeMacro(code.Text);
                Status.Text="Safe macro completed.";
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message,"Safe macro stopped",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        };

        refresh();
        run.IsEnabled=false;
        w.Content=grid;
        w.ShowDialog();
    }

    void RunSafeMacro(string source)
    {
        var lines=source.Replace("\r\n","\n").Split('\n');
        var executed=0;
        foreach(var raw in lines)
        {
            var line=raw.Trim();
            if(line.Length==0 || line.StartsWith("#"))continue;
            executed++;
            if(executed>200)throw new InvalidOperationException("Safe macro exceeds the 200-command limit.");

            var split=line.IndexOf(' ');
            var command=(split<0?line:line[..split]).Trim().ToUpperInvariant();
            var argument=split<0?"":line[(split+1)..];

            switch(command)
            {
                case "INSERT_TEXT":
                    Editor.CaretPosition.InsertTextInRun(argument.Replace("\\n","\n"));
                    break;
                case "INSERT_FIELD":
                {
                    var key=argument.Trim();
                    var token=customFields.ContainsKey(key)?"{field:"+key+"}":"{"+key+"}";
                    Editor.CaretPosition.InsertTextInRun(ExpandFields(token,1,1));
                    break;
                }
                case "NEW_PARAGRAPH":
                    EditingCommands.EnterParagraphBreak.Execute(null,Editor);
                    break;
                case "APPLY_STYLE":
                    ApplyParagraphStyle(argument.Trim());
                    break;
                case "SET_HEADER":
                    headerText=argument;
                    RefreshHeaderFooter();
                    MarkDirty("Header changed by safe macro.");
                    break;
                case "SET_FOOTER":
                    footerText=argument;
                    RefreshHeaderFooter();
                    MarkDirty("Footer changed by safe macro.");
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported safe macro command: {command}");
            }
        }
        MarkDirty();
        Editor.Focus();
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
        "CtrlZed 0.5.3 beta 5.3\n\nThe word processor that knows its place.\n\nHuman-directed. AI-authored project code. Open source under MIT.\n\nBeta 5.3 adds a clearer visual gap between pages in the editor.",
        "About CtrlZed");
}
