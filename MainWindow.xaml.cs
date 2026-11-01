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
    readonly Dictionary<string,string> embeddedMacros = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> recentFiles = new();
    readonly string settingsFile;
    string documentTitle = "";
    string documentAuthor = "";
    string documentSubject = "";
    string documentKeywords = "";
    double zoomPercent = 100;
    bool extendedMacrosEnabled;
    bool compiledMacrosEnabled;

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
        public Dictionary<string,string> EmbeddedMacros { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Keywords { get; set; } = "";
    }

    sealed class HeadingItem
    {
        public string Text { get; init; } = "";
        public Paragraph Paragraph { get; init; } = null!;
        public override string ToString() => Text;
    }

    public MainWindow()
    {
        InitializeComponent();
        Icon=Branding.CreateIconImageSource();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,new KeyGesture(Key.S,ModifierKeys.Control)));
        Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
        Editor.PreviewMouseRightButtonDown += Editor_PreviewMouseRightButtonDown;

        var recoveryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        Directory.CreateDirectory(recoveryDir);
        recoveryFile = Path.Combine(recoveryDir,"Recovery.rtf");
        settingsFile = Path.Combine(recoveryDir,"settings.json");
        LoadAppSettings();
        RefreshRecentFilesMenu();
        recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        recoveryTimer.Tick += (_,__) => WriteRecoverySnapshot();
        recoveryTimer.Start();

        ApplyPage();
        RefreshHeaderFooter();
        suppressDirty = false;
        ApplyZoom();
        UpdateDocumentStats();

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
        embeddedMacros.Clear();
        documentTitle=documentAuthor=documentSubject=documentKeywords="";
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
        var d=new OpenFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Word documents (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|All files (*.*)|*.*"};
        if(d.ShowDialog()==true)OpenPath(d.FileName);
    }

    void Save_Click(object s,RoutedEventArgs e) => SaveCurrent();

    bool SaveCurrent()
    {
        if(currentFile==null)
        {
            var d=new SaveFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Word documents (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt",DefaultExt=".czd",AddExtension=true};
            if(d.ShowDialog()!=true)return false;
            currentFile=d.FileName;
        }

        try
        {
            var ext=Path.GetExtension(currentFile).ToLowerInvariant();
            if(ext==".czd")SaveCzd(currentFile);
            else if(ext==".docx")DocxInterop.Save(currentFile,Editor.Document,headerText,footerText,pageWidth,pageHeight,pageMargins,documentTitle,documentAuthor,documentSubject,documentKeywords);
            else if(ext==".txt")File.WriteAllText(currentFile,new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text);
            else SaveRtf(currentFile);

            dirty=false;
            UpdateTitle();
            Status.Text="Saved "+currentFile;
            AddRecentFile(currentFile);
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
            UpdatePageGuideState();
            UpdateDocumentStats();
            if(NavigationPane!=null && NavigationPane.Visibility==Visibility.Visible)RefreshNavigation();
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
                CustomFields=new Dictionary<string,string>(customFields,StringComparer.OrdinalIgnoreCase),
                EmbeddedMacros=new Dictionary<string,string>(embeddedMacros,StringComparer.OrdinalIgnoreCase),
                Title=documentTitle,Author=documentAuthor,Subject=documentSubject,Keywords=documentKeywords
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
                documentTitle=v.Title??"";
                documentAuthor=v.Author??"";
                documentSubject=v.Subject??"";
                documentKeywords=v.Keywords??"";
                customFields.Clear();
                embeddedMacros.Clear();
                if(v.EmbeddedMacros!=null)
                    foreach(var pair in v.EmbeddedMacros)
                        if(!string.IsNullOrWhiteSpace(pair.Key))
                            embeddedMacros[pair.Key]=pair.Value??"";
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

    void NavigationPane_Click(object s,RoutedEventArgs e)
    {
        var show=NavigationPane.Visibility!=Visibility.Visible;
        NavigationPane.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        if(show)RefreshNavigation();
    }

    void RefreshNavigation_Click(object s,RoutedEventArgs e)=>RefreshNavigation();

    void RefreshNavigation()
    {
        if(NavigationList==null)return;
        var items=new List<HeadingItem>();
        foreach(var block in Editor.Document.Blocks)
            CollectHeadings(block,items);
        NavigationList.ItemsSource=items;
        if(items.Count==0)NavigationList.ItemsSource=new[]{new HeadingItem{Text="(No headings found)",Paragraph=Editor.Document.Blocks.OfType<Paragraph>().FirstOrDefault()??new Paragraph()}};
    }

    void CollectHeadings(Block block,List<HeadingItem> items)
    {
        if(block is Paragraph p)
        {
            var text=new TextRange(p.ContentStart,p.ContentEnd).Text.Trim();
            if(text.Length==0)return;
            var range=new TextRange(p.ContentStart,p.ContentEnd);
            var sizeValue=range.GetPropertyValue(TextElement.FontSizeProperty);
            var weightValue=range.GetPropertyValue(TextElement.FontWeightProperty);
            var size=sizeValue is double d?d:0;
            var weight=weightValue is FontWeight fw?fw.ToOpenTypeWeight():0;
            if(size>=17 || weight>=600 && size>=15)
            {
                var level=size>=23?1:size>=19?2:3;
                items.Add(new HeadingItem{Text=$"{new string(' ',(level-1)*2)}{text}",Paragraph=p});
            }
        }
        else if(block is Section section)
        {
            foreach(var child in section.Blocks)CollectHeadings(child,items);
        }
    }

    void NavigationList_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(NavigationList.SelectedItem is not HeadingItem item || item.Paragraph.Parent==null)return;
        try
        {
            item.Paragraph.BringIntoView();
            Editor.CaretPosition=item.Paragraph.ContentStart;
            Editor.Focus();
        }
        catch { }
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
        UpdatePageGuideState();
    }

    void UpdatePageGuideState()
    {
        if(PageGuides==null)return;
        PageGuides.Header=headerText;
        PageGuides.Footer=footerText;
        PageGuides.Expand=ExpandFields;
        PageGuides.ManualBreakY=GetManualBreakPositions();
        PageGuides.InvalidateVisual();
    }

    IReadOnlyList<double> GetManualBreakPositions()
    {
        var points=new List<double>();
        foreach(var p in EnumerateDocumentParagraphs())
        {
            if(!p.BreakPageBefore)continue;
            try
            {
                var rect=p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if(!rect.IsEmpty && !double.IsNaN(rect.Top) && rect.Top>0)points.Add(rect.Top);
            }
            catch { }
        }
        return points;
    }

    sealed class AppSettings
    {
        public List<string> RecentFiles { get; set; } = new();
        public double ZoomPercent { get; set; } = 100;
        public bool ExtendedMacrosEnabled { get; set; }
        public bool CompiledMacrosEnabled { get; set; }
    }

    void LoadAppSettings()
    {
        try
        {
            if(!File.Exists(settingsFile))return;
            var settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile));
            if(settings==null)return;
            recentFiles.Clear();
            recentFiles.AddRange(settings.RecentFiles.Where(File.Exists).Take(10));
            zoomPercent=Math.Clamp(settings.ZoomPercent,50,150);
            extendedMacrosEnabled=settings.ExtendedMacrosEnabled;
            compiledMacrosEnabled=settings.CompiledMacrosEnabled;
            ApplyZoom();
        }
        catch { }
    }

    void SaveAppSettings()
    {
        try
        {
            File.WriteAllText(settingsFile,JsonSerializer.Serialize(new AppSettings
            {
                RecentFiles=recentFiles.Take(10).ToList(),
                ZoomPercent=zoomPercent,
                ExtendedMacrosEnabled=extendedMacrosEnabled,
                CompiledMacrosEnabled=compiledMacrosEnabled
            }));
        }
        catch { }
    }

    void AddRecentFile(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return;
        recentFiles.RemoveAll(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));
        recentFiles.Insert(0,path);
        if(recentFiles.Count>10)recentFiles.RemoveRange(10,recentFiles.Count-10);
        SaveAppSettings();
        RefreshRecentFilesMenu();
    }

    void RefreshRecentFilesMenu()
    {
        if(RecentFilesMenu==null)return;
        RecentFilesMenu.Items.Clear();
        if(recentFiles.Count==0)
        {
            RecentFilesMenu.Items.Add(new MenuItem{Header="(None)",IsEnabled=false});
            return;
        }
        foreach(var path in recentFiles.ToList())
        {
            var item=new MenuItem{Header=System.IO.Path.GetFileName(path),ToolTip=path,Tag=path};
            item.Click+=RecentFile_Click;
            RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.Items.Add(new Separator());
        var clear=new MenuItem{Header="Clear Recent Files"};
        clear.Click+=(_,__)=>{recentFiles.Clear();SaveAppSettings();RefreshRecentFilesMenu();};
        RecentFilesMenu.Items.Add(clear);
    }

    void RecentFile_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem {Tag:string path})return;
        if(!File.Exists(path))
        {
            recentFiles.Remove(path);SaveAppSettings();RefreshRecentFilesMenu();
            MessageBox.Show("That recent file no longer exists.","Recent Files",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(!CanDiscardChanges())return;
        OpenPath(path);
    }

    void OpenPath(string path)
    {
        try
        {
            suppressDirty=true;
            var ext=Path.GetExtension(path).ToLowerInvariant();
            headerText=footerText="";
            documentTitle=documentAuthor=documentSubject=documentKeywords="";
            if(ext==".czd")LoadCzd(path);
            else if(ext==".docx")
            {
                var imported=DocxInterop.LoadWithLayout(path);
                Editor.Document=imported.Document;
                headerText=imported.Header;footerText=imported.Footer;
                if(imported.PageWidth>0)pageWidth=imported.PageWidth;
                if(imported.PageHeight>0)pageHeight=imported.PageHeight;
                if(imported.Margins!=null)pageMargins=imported.Margins.Value;
                documentTitle=imported.Title;documentAuthor=imported.Author;
                documentSubject=imported.Subject;documentKeywords=imported.Keywords;
                ApplyPage();
            }
            else if(ext==".txt")Editor.Document=new FlowDocument(new Paragraph(new Run(File.ReadAllText(path))));
            else
            {
                Editor.Document=new FlowDocument();
                using var fs=File.OpenRead(path);
                new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(fs,DataFormats.Rtf);
            }
            WirePictureHandlers();RefreshNavigation();
            currentFile=path;dirty=false;suppressDirty=false;
            RefreshHeaderFooter();UpdateTitle();UpdateDocumentStats();
            Status.Text="Opened "+currentFile;AddRecentFile(currentFile);TryDeleteRecovery();
        }
        catch(Exception ex)
        {
            suppressDirty=false;
            MessageBox.Show(ex.Message,"Open failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void SaveAs_Click(object s,RoutedEventArgs e)
    {
        var old=currentFile;
        var d=new SaveFileDialog
        {
            Filter="CtrlZed documents (*.czd)|*.czd|Word documents (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt",
            DefaultExt=old==null?".czd":Path.GetExtension(old),
            FileName=old==null?"":Path.GetFileName(old),
            AddExtension=true
        };
        if(d.ShowDialog()!=true)return;
        currentFile=d.FileName;
        if(!SaveCurrent())currentFile=old;
    }

    void DocumentProperties_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Document Properties",Owner=this,Width=520,Height=365,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<6;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var title=new TextBox{Text=documentTitle,Margin=new Thickness(0,3,0,8)};
        var author=new TextBox{Text=documentAuthor,Margin=new Thickness(0,3,0,8)};
        var subject=new TextBox{Text=documentSubject,Margin=new Thickness(0,3,0,8)};
        var keywords=new TextBox{Text=documentKeywords,Margin=new Thickness(0,3,0,8)};
        AddRow(g,0,"Title",title);AddRow(g,1,"Author",author);AddRow(g,2,"Subject",subject);AddRow(g,3,"Keywords",keywords);
        var info=new TextBlock{Text="Stored in .czd and mapped to DOCX core properties.",Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,5)};
        Grid.SetRow(info,4);Grid.SetColumnSpan(info,2);g.Children.Add(info);
        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        Grid.SetRow(ok,5);Grid.SetColumnSpan(ok,2);g.Children.Add(ok);
        ok.Click+=(_,__)=>
        {
            documentTitle=title.Text;documentAuthor=author.Text;documentSubject=subject.Text;documentKeywords=keywords.Text;
            MarkDirty("Document properties updated.");w.DialogResult=true;
        };
        w.Content=g;w.ShowDialog();
    }

    void Zoom_Click(object s,RoutedEventArgs e)
    {
        if(s is MenuItem item && double.TryParse(item.Tag?.ToString(),out var value))
        {
            zoomPercent=Math.Clamp(value,50,150);
            ApplyZoom();SaveAppSettings();
        }
    }

    void ApplyZoom()
    {
        if(PageBorder==null)return;
        PageBorder.LayoutTransform=new ScaleTransform(zoomPercent/100.0,zoomPercent/100.0);
        if(ZoomStatus!=null)ZoomStatus.Text=$"{zoomPercent:0}%";
    }

    void UpdateDocumentStats()
    {
        if(DocumentStats==null)return;
        var text=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text;
        var chars=text.TrimEnd('\r','\n').Length;
        var words=System.Text.RegularExpressions.Regex.Matches(text,@"\b[\p{L}\p{N}’'-]+\b").Count;
        var pages=1;
        try
        {
            var paginator=CreatePreviewPaginator();
            paginator.ComputePageCount();
            pages=Math.Max(1,paginator.PageCount);
        }
        catch { }
        DocumentStats.Text=$"{words:N0} words · {chars:N0} characters · {pages:N0} page{(pages==1?"":"s")}";
    }

    void FindReplace_Click(object s,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Find and Replace",
            Owner=this,
            Width=500,
            Height=255,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize
        };

        var g=new Grid{Margin=new Thickness(14)};
        for(int i=0;i<5;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
        g.ColumnDefinitions.Add(new ColumnDefinition());

        var find=new TextBox{Margin=new Thickness(0,3,0,7)};
        var replace=new TextBox{Margin=new Thickness(0,3,0,7)};
        var matchCase=new CheckBox{Content="Match case",Margin=new Thickness(0,5,0,5)};
        AddRow(g,0,"Find",find);
        AddRow(g,1,"Replace",replace);
        Grid.SetRow(matchCase,2);Grid.SetColumn(matchCase,1);g.Children.Add(matchCase);

        var status=new TextBlock{Margin=new Thickness(0,5,0,5),Foreground=Brushes.DimGray};
        Grid.SetRow(status,3);Grid.SetColumnSpan(status,2);g.Children.Add(status);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var findNext=new Button{Content="Find Next",Width=90,Margin=new Thickness(4)};
        var replaceOne=new Button{Content="Replace",Width=80,Margin=new Thickness(4)};
        var replaceAll=new Button{Content="Replace All",Width=90,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=70,Margin=new Thickness(4)};
        buttons.Children.Add(findNext);buttons.Children.Add(replaceOne);buttons.Children.Add(replaceAll);buttons.Children.Add(close);
        Grid.SetRow(buttons,4);Grid.SetColumnSpan(buttons,2);g.Children.Add(buttons);

        bool FindNext()
        {
            var term=find.Text;
            if(string.IsNullOrEmpty(term))return false;
            var comparison=matchCase.IsChecked==true?StringComparison.CurrentCulture:StringComparison.CurrentCultureIgnoreCase;
            var found=FindText(Editor.CaretPosition,term,comparison) ?? FindText(Editor.Document.ContentStart,term,comparison);
            if(found==null){status.Text="Not found.";return false;}
            Editor.Selection.Select(found.Value.Start,found.Value.End);
            Editor.CaretPosition=found.Value.End;
            Editor.Focus();
            status.Text="Found.";
            return true;
        }

        findNext.Click+=(_,__)=>FindNext();
        replaceOne.Click+=(_,__)=>
        {
            var comparison=matchCase.IsChecked==true?StringComparison.CurrentCulture:StringComparison.CurrentCultureIgnoreCase;
            if(!string.IsNullOrEmpty(find.Text) && string.Equals(Editor.Selection.Text,find.Text,comparison))
            {
                Editor.Selection.Text=replace.Text;
                MarkDirty("Text replaced.");
            }
            FindNext();
        };
        replaceAll.Click+=(_,__)=>
        {
            if(string.IsNullOrEmpty(find.Text))return;
            var comparison=matchCase.IsChecked==true?StringComparison.CurrentCulture:StringComparison.CurrentCultureIgnoreCase;
            var count=0;
            var start=Editor.Document.ContentStart;
            while(true)
            {
                var found=FindText(start,find.Text,comparison);
                if(found==null)break;
                var range=new TextRange(found.Value.Start,found.Value.End);
                range.Text=replace.Text;
                start=range.End;
                count++;
                if(count>10000)break;
            }
            if(count>0)MarkDirty($"{count} replacement{(count==1?"":"s")} made.");
            status.Text=$"{count} replacement{(count==1?"":"s")}.";
        };
        close.Click+=(_,__)=>w.Close();

        w.Content=g;
        w.ShowDialog();
    }

    (TextPointer Start,TextPointer End)? FindText(TextPointer start,string term,StringComparison comparison)
    {
        var paragraphs=EnumerateDocumentParagraphs().ToList();
        if(paragraphs.Count==0)return null;

        var startIndex=0;
        var startOffset=0;
        for(int i=0;i<paragraphs.Count;i++)
        {
            var p=paragraphs[i];
            if(start.CompareTo(p.ContentStart)>=0 && start.CompareTo(p.ContentEnd)<=0)
            {
                startIndex=i;
                startOffset=new TextRange(p.ContentStart,start).Text.Length;
                break;
            }
            if(start.CompareTo(p.ContentEnd)>0)startIndex=Math.Min(i+1,paragraphs.Count-1);
        }

        for(int pass=0;pass<2;pass++)
        {
            var first=pass==0?startIndex:0;
            var last=pass==0?paragraphs.Count:startIndex+1;
            for(int i=first;i<last;i++)
            {
                var p=paragraphs[i];
                var text=new TextRange(p.ContentStart,p.ContentEnd).Text;
                var from=pass==0&&i==startIndex?Math.Clamp(startOffset,0,text.Length):0;
                var index=text.IndexOf(term,from,comparison);
                if(index<0)continue;

                var s=PointerAtTextOffset(p.ContentStart,p.ContentEnd,index);
                var e=PointerAtTextOffset(p.ContentStart,p.ContentEnd,index+term.Length);
                if(s!=null&&e!=null)return(s,e);
            }
        }
        return null;
    }

    IEnumerable<Paragraph> EnumerateDocumentParagraphs()
    {
        foreach(var block in Editor.Document.Blocks)
            foreach(var p in EnumerateParagraphsInBlock(block))
                yield return p;
    }

    IEnumerable<Paragraph> EnumerateParagraphsInBlock(Block block)
    {
        if(block is Paragraph p)yield return p;
        else if(block is Section section)
        {
            foreach(var child in section.Blocks)
                foreach(var paragraph in EnumerateParagraphsInBlock(child))
                    yield return paragraph;
        }
        else if(block is List list)
        {
            foreach(var item in list.ListItems)
                foreach(var child in item.Blocks)
                    foreach(var paragraph in EnumerateParagraphsInBlock(child))
                        yield return paragraph;
        }
        else if(block is Table table)
        {
            foreach(var group in table.RowGroups)
                foreach(var row in group.Rows)
                    foreach(var cell in row.Cells)
                        foreach(var child in cell.Blocks)
                            foreach(var paragraph in EnumerateParagraphsInBlock(child))
                                yield return paragraph;
        }
    }

    TextPointer? PointerAtTextOffset(TextPointer start,TextPointer end,int target)
    {
        var p=start;
        var count=0;
        while(p!=null && p.CompareTo(end)<=0)
        {
            var context=p.GetPointerContext(LogicalDirection.Forward);
            if(context==TextPointerContext.Text)
            {
                var text=p.GetTextInRun(LogicalDirection.Forward);
                if(count+text.Length>=target)
                    return p.GetPositionAtOffset(target-count,LogicalDirection.Forward);
                count+=text.Length;
                p=p.GetPositionAtOffset(text.Length,LogicalDirection.Forward);
            }
            else
            {
                p=p.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
        return target==count?end:null;
    }

    void PageBreak_Click(object s,RoutedEventArgs e)
    {
        Editor.Focus();
        EditingCommands.EnterParagraphBreak.Execute(null,Editor);
        var paragraph=Editor.CaretPosition.Paragraph;
        if(paragraph!=null)
        {
            paragraph.BreakPageBefore=true;
            MarkDirty("Page break inserted.");
        }
    }

    void Table_Click(object s,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Insert Table",
            Owner=this,
            Width=330,
            Height=220,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize
        };
        var g=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<3;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});
        g.ColumnDefinitions.Add(new ColumnDefinition());

        var rows=new TextBox{Text="2",Margin=new Thickness(0,4,0,8)};
        var cols=new TextBox{Text="2",Margin=new Thickness(0,4,0,8)};
        AddRow(g,0,"Rows",rows);
        AddRow(g,1,"Columns",cols);
        var ok=new Button{Content="Insert",Width=85,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        Grid.SetRow(ok,2);Grid.SetColumnSpan(ok,2);g.Children.Add(ok);

        ok.Click+=(_,__)=>
        {
            if(!int.TryParse(rows.Text,out var r)||!int.TryParse(cols.Text,out var col)||r<1||r>50||col<1||col>20)
            {
                MessageBox.Show("Use 1–50 rows and 1–20 columns.","Insert Table",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }

            var table=new Table
            {
                CellSpacing=0,
                BorderBrush=Brushes.Gray,
                BorderThickness=new Thickness(1),
                Margin=new Thickness(0,8,0,8)
            };
            for(int i=0;i<col;i++)table.Columns.Add(new TableColumn());
            var group=new TableRowGroup();
            table.RowGroups.Add(group);

            for(int ri=0;ri<r;ri++)
            {
                var row=new TableRow();
                group.Rows.Add(row);
                for(int ci=0;ci<col;ci++)
                {
                    var cell=new TableCell(new Paragraph(new Run("")))
                    {
                        BorderBrush=Brushes.LightGray,
                        BorderThickness=new Thickness(0.5),
                        Padding=new Thickness(5)
                    };
                    row.Cells.Add(cell);
                }
            }

            InsertBlockAfterCaret(table);
            MarkDirty($"Inserted {r} × {col} table.");
            w.DialogResult=true;
            Editor.Focus();
        };

        w.Content=g;
        w.ShowDialog();
    }

    void InsertBlockAfterCaret(Block block)
    {
        var paragraph=Editor.CaretPosition.Paragraph;
        if(paragraph==null)
        {
            Editor.Document.Blocks.Add(block);
            return;
        }

        switch(paragraph.Parent)
        {
            case FlowDocument doc:
                doc.Blocks.InsertAfter(paragraph,block);
                break;
            case Section section:
                section.Blocks.InsertAfter(paragraph,block);
                break;
            case ListItem item:
                item.Blocks.InsertAfter(paragraph,block);
                break;
            case TableCell cell:
                cell.Blocks.InsertAfter(paragraph,block);
                break;
            default:
                Editor.Document.Blocks.Add(block);
                break;
        }
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
        var point=e.GetPosition(Editor);
        var img=FindPictureAtPoint(point);
        e.Handled=true;

        if(img!=null)
        {
            var imageMenu=new ContextMenu();
            var properties=new MenuItem{Header="Picture Properties…"};
            properties.Click+=(_,__)=>ShowPicturePropertiesSafely(img);
            imageMenu.Items.Add(properties);
            AddMacroSubmenu(imageMenu);
            imageMenu.PlacementTarget=Editor;
            imageMenu.IsOpen=true;
            return;
        }

        var pointer=Editor.GetPositionFromPoint(point,true);
        if(pointer!=null && Editor.Selection.IsEmpty)Editor.CaretPosition=pointer;

        var menu=new ContextMenu();
        AddMacroSubmenu(menu);
        if(menu.Items.Count>0)menu.Items.Add(new Separator());

        var cut=new MenuItem{Header="Cut",Command=ApplicationCommands.Cut,CommandTarget=Editor,InputGestureText="Ctrl+X"};
        var copy=new MenuItem{Header="Copy",Command=ApplicationCommands.Copy,CommandTarget=Editor,InputGestureText="Ctrl+C"};
        var paste=new MenuItem{Header="Paste",Command=ApplicationCommands.Paste,CommandTarget=Editor,InputGestureText="Ctrl+V"};
        menu.Items.Add(cut);menu.Items.Add(copy);menu.Items.Add(paste);
        menu.PlacementTarget=Editor;
        menu.IsOpen=true;
    }

    IEnumerable<(string Name,string Source)> GetMacroSources()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");
        if(Directory.Exists(dir))
            foreach(var path in Directory.GetFiles(dir)
                .Where(p=>new[]{".czm",".vb",".cs"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)))
                yield return (Path.GetFileName(path),File.ReadAllText(path));
        foreach(var pair in embeddedMacros)yield return (pair.Key,pair.Value);
    }

    static string? GetMacroFriendlyName(string source,string extension)
    {
        extension=(extension??"").ToLowerInvariant();
        if(extension==".czm")
        {
            foreach(var raw in source.Replace("\r\n","\n").Split('\n'))
            {
                var line=raw.Trim();
                if(line.StartsWith("#"))line=line[1..].Trim();
                if(!line.StartsWith("MENU_NAME",StringComparison.OrdinalIgnoreCase))continue;
                var eq=line.IndexOf('=');
                if(eq<0)continue;
                var value=line[(eq+1)..].Trim();
                if(value.Length>=2 && value[0]=='"' && value[^1]=='"')value=value[1..^1];
                if(!string.IsNullOrWhiteSpace(value))return value;
            }
            return null;
        }

        if(extension==".vb")
        {
            var match=System.Text.RegularExpressions.Regex.Match(source,
                @"(?im)^\s*(?:Public\s+|Friend\s+|Private\s+)?Const\s+MENU_NAME\s+As\s+String\s*=\s*""([^""]+)""");
            return match.Success?match.Groups[1].Value.Trim():null;
        }

        if(extension==".cs")
        {
            var match=System.Text.RegularExpressions.Regex.Match(source,
                @"(?im)^\s*(?:public\s+|internal\s+|private\s+)?(?:static\s+)?const\s+string\s+MENU_NAME\s*=\s*""([^""]+)""\s*;");
            return match.Success?match.Groups[1].Value.Trim():null;
        }

        return null;
    }

    static (int Option,string MenuName)? GetMacroMenuMetadata(string source,string extension)
    {
        extension=(extension??"").ToLowerInvariant();
        if(extension==".czm")
        {
            int? option=null;
            string? menuName=null;
            foreach(var raw in source.Replace("\r\n","\n").Split('\n'))
            {
                var line=raw.Trim();
                if(line.StartsWith("#"))line=line[1..].Trim();
                if(line.StartsWith("MENU_OPTION",StringComparison.OrdinalIgnoreCase))
                {
                    var eq=line.IndexOf('=');
                    if(eq>=0 && int.TryParse(line[(eq+1)..].Trim(),out var n) && n>0)option=n;
                }
                else if(line.StartsWith("MENU_NAME",StringComparison.OrdinalIgnoreCase))
                {
                    var eq=line.IndexOf('=');
                    if(eq>=0)
                    {
                        var value=line[(eq+1)..].Trim();
                        if(value.Length>=2 && value[0]=='"' && value[^1]=='"')value=value[1..^1];
                        if(!string.IsNullOrWhiteSpace(value))menuName=value;
                    }
                }
            }
            return option.HasValue && !string.IsNullOrWhiteSpace(menuName)?(option.Value,menuName):null;
        }

        var name=GetMacroFriendlyName(source,extension);
        if(string.IsNullOrWhiteSpace(name))return null;

        var pattern=extension==".vb"
            ?@"(?im)^\s*(?:Public\s+|Friend\s+|Private\s+)?Const\s+MENU_OPTION\s+As\s+Integer\s*=\s*(\d+)"
            :@"(?im)^\s*(?:public\s+|internal\s+|private\s+)?(?:static\s+)?const\s+int\s+MENU_OPTION\s*=\s*(\d+)\s*;";
        var match=System.Text.RegularExpressions.Regex.Match(source,pattern);
        return match.Success && int.TryParse(match.Groups[1].Value,out var optionValue) && optionValue>0?(optionValue,name):null;
    }

    void AddMacroSubmenu(ContextMenu menu)
    {
        var entries=GetMacroSources()
            .Select(m=>(Macro:m,Meta:GetMacroMenuMetadata(m.Source,Path.GetExtension(m.Name))))
            .Where(x=>x.Meta.HasValue)
            .OrderBy(x=>x.Meta!.Value.Option)
            .ThenBy(x=>x.Meta!.Value.MenuName,StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if(entries.Count==0)return;

        var macros=new MenuItem{Header="Macros"};
        foreach(var entry in entries)
        {
            var item=new MenuItem{Header=entry.Meta!.Value.MenuName};
            var macro=entry.Macro;
            item.Click+=async (_,__)=>
            {
                try
                {
                    await RunMacroSourceAsync(macro.Name,macro.Source);
                    Status.Text="Macro: "+entry.Meta!.Value.MenuName;
                }
                catch(Exception ex)
                {
                    ShowMacroError(macro.Name,ex);
                }
            };
            macros.Items.Add(item);
        }
        menu.Items.Add(macros);
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

    TableCell? CurrentTableCell()
    {
        DependencyObject? node=Editor.CaretPosition.Parent;
        while(node!=null)
        {
            if(node is TableCell cell)return cell;
            node=node switch
            {
                FrameworkContentElement fce=>fce.Parent,
                _=>null
            };
        }
        return null;
    }

    void TableAddRow_Click(object s,RoutedEventArgs e)
    {
        var cell=CurrentTableCell();
        if(cell?.Parent is not TableRow row || row.Parent is not TableRowGroup group)
        {
            Status.Text="Place the caret inside a table first.";
            return;
        }
        var newRow=new TableRow();
        var cols=row.Cells.Count;
        for(int i=0;i<cols;i++)
            newRow.Cells.Add(new TableCell(new Paragraph(new Run(""))){BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0.5),Padding=new Thickness(5)});
        var rowIndex=group.Rows.IndexOf(row);
        group.Rows.Insert(Math.Min(rowIndex+1,group.Rows.Count),newRow);
        MarkDirty("Table row added.");
    }

    void TableAddColumn_Click(object s,RoutedEventArgs e)
    {
        var cell=CurrentTableCell();
        if(cell?.Parent is not TableRow currentRow || currentRow.Parent is not TableRowGroup group || group.Parent is not Table table)
        {
            Status.Text="Place the caret inside a table first.";
            return;
        }
        var index=currentRow.Cells.IndexOf(cell)+1;
        table.Columns.Insert(index,new TableColumn());
        foreach(var row in group.Rows)
        {
            var newCell=new TableCell(new Paragraph(new Run(""))){BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0.5),Padding=new Thickness(5)};
            if(index>=row.Cells.Count)row.Cells.Add(newCell);
            else row.Cells.Insert(index,newCell);
        }
        MarkDirty("Table column added.");
    }

    void TableDeleteRow_Click(object s,RoutedEventArgs e)
    {
        var cell=CurrentTableCell();
        if(cell?.Parent is not TableRow row || row.Parent is not TableRowGroup group)
        {
            Status.Text="Place the caret inside a table first.";
            return;
        }
        if(group.Rows.Count<=1)
        {
            Status.Text="A table must retain at least one row.";
            return;
        }
        group.Rows.Remove(row);
        MarkDirty("Table row deleted.");
    }

    void TableDeleteColumn_Click(object s,RoutedEventArgs e)
    {
        var cell=CurrentTableCell();
        if(cell?.Parent is not TableRow currentRow || currentRow.Parent is not TableRowGroup group || group.Parent is not Table table)
        {
            Status.Text="Place the caret inside a table first.";
            return;
        }
        if(table.Columns.Count<=1)
        {
            Status.Text="A table must retain at least one column.";
            return;
        }
        var index=currentRow.Cells.IndexOf(cell);
        foreach(var row in group.Rows)
            if(index>=0 && index<row.Cells.Count)row.Cells.Remove(row.Cells[index]);
        if(index>=0 && index<table.Columns.Count)table.Columns.RemoveAt(index);
        MarkDirty("Table column deleted.");
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
            UpdatePageGuideState();
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

    void AiManager_Click(object s,RoutedEventArgs e)
    {
        var selected=new TextRange(Editor.Selection.Start,Editor.Selection.End).Text;
        var whole=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text;
        AiManager.ShowForDocument(this,selected,whole,ApplyAiDocumentResult);
    }

    void ApplyAiDocumentResult(string result,bool replaceSelection)
    {
        if(string.IsNullOrWhiteSpace(result))return;

        if(replaceSelection && !Editor.Selection.IsEmpty)
        {
            Editor.Selection.Text="";
            Editor.CaretPosition=Editor.Selection.Start;
        }

        foreach(var part in AiResultRenderer.Parse(result))
        {
            if(!string.IsNullOrWhiteSpace(part.Text))
            {
                var text=part.Text.Trim();
                Editor.CaretPosition.InsertTextInRun(text);
                EditingCommands.EnterParagraphBreak.Execute(null,Editor);
            }
            if(part.Image!=null)
            {
                try
                {
                    var image=new Image
                    {
                        Source=part.Image,Width=Math.Min(680,part.Image.PixelWidth),
                        Stretch=Stretch.Uniform,Margin=new Thickness(4,8,4,8)
                    };
                    new InlineUIContainer(image,Editor.CaretPosition);
                    EditingCommands.EnterParagraphBreak.Execute(null,Editor);
                    WirePicture(image);
                }
                catch
                {
                    Editor.CaretPosition.InsertTextInRun("[Chart could not be inserted]");
                    EditingCommands.EnterParagraphBreak.Execute(null,Editor);
                }
            }
        }

        MarkDirty(replaceSelection?"AI replaced the selection.":"AI result inserted.");
        UpdateDocumentStats();
        Editor.Focus();
    }

    void MacroLibrary_Click(object s,RoutedEventArgs e)
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");
        Directory.CreateDirectory(dir);
        var w=new Window{Title="CtrlZed Macro Manager",Owner=this,Width=900,Height=640,WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()};
        var grid=new Grid{Margin=new Thickness(10)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(240)});
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left=new DockPanel{Margin=new Thickness(0,0,10,0)};
        var list=new ListBox();
        DockPanel.SetDock(list,Dock.Top);
        left.Children.Add(list);

        var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};
        var ns=new Button{Content="New Safe",Margin=new Thickness(0,0,5,5)};
        var nv=new Button{Content="New VB",Margin=new Thickness(0,0,5,5)};
        var nc=new Button{Content="New C#",Margin=new Thickness(0,0,5,5)};
        var embed=new Button{Content="Embed",Margin=new Thickness(0,0,5,5)};
        var extract=new Button{Content="Extract",Margin=new Thickness(0,0,5,5)};
        var rename=new Button{Content="Rename",Margin=new Thickness(0,0,5,5)};
        var delete=new Button{Content="Delete",Margin=new Thickness(0,0,5,5)};
        var ai=new Button{Content="Ask AI…",Margin=new Thickness(0,0,5,5)};
        buttons.Children.Add(ns);buttons.Children.Add(nv);buttons.Children.Add(nc);buttons.Children.Add(embed);buttons.Children.Add(extract);buttons.Children.Add(rename);buttons.Children.Add(delete);buttons.Children.Add(ai);
        DockPanel.SetDock(buttons,Dock.Bottom);
        left.Children.Add(buttons);
        Grid.SetColumn(left,0);grid.Children.Add(left);

        var right=new DockPanel();
        var warningPanel=new StackPanel{Margin=new Thickness(0,0,0,8)};
        var warning=new TextBlock
        {
            Text="Safe mode restricts .czm macros to CtrlZed document commands. Enable extended macros to allow interactive prompts and generated images such as QR codes.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray
        };
        var extended=new CheckBox
        {
            Content="Allow extended macros (interactive prompts and generated images)",
            IsChecked=extendedMacrosEnabled,
            Margin=new Thickness(0,7,0,0),
            ToolTip="Off by default. When enabled, macros may ask for input and generate images inside the document."
        };
        extended.Checked+=(_,__)=>
        {
            extendedMacrosEnabled=true;SaveAppSettings();
            warning.Text="Extended macros are enabled. Interactive prompts and generated images are allowed.";
        };
        extended.Unchecked+=(_,__)=>
        {
            extendedMacrosEnabled=false;SaveAppSettings();
            warning.Text="Safe mode is enabled. Interactive prompts and generated images are blocked.";
        };
        var compiled=new CheckBox
        {
            Content="Allow compiled VB/C# macros (full trust)",
            IsChecked=compiledMacrosEnabled,
            Margin=new Thickness(0,5,0,0),
            ToolTip="Compiled macros run as your Windows user and may access files, network, processes and other system resources."
        };
        compiled.Checked+=(_,__)=>
        {
            compiledMacrosEnabled=true;SaveAppSettings();
            warning.Text="Compiled VB/C# macros are enabled. They run with the same Windows permissions as CtrlZed.";
        };
        compiled.Unchecked+=(_,__)=>
        {
            compiledMacrosEnabled=false;SaveAppSettings();
            warning.Text="Compiled VB/C# macros are disabled. .czm macros remain available.";
        };
        warningPanel.Children.Add(warning);warningPanel.Children.Add(extended);warningPanel.Children.Add(compiled);
        DockPanel.SetDock(warningPanel,Dock.Top);right.Children.Add(warningPanel);

        var actionBar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};
        var run=new Button{Content="Run safe macro",Width=110,Margin=new Thickness(4)};
        var save=new Button{Content="Save source",Width=100,Margin=new Thickness(4)};
        actionBar.Children.Add(run);actionBar.Children.Add(save);
        DockPanel.SetDock(actionBar,Dock.Bottom);right.Children.Add(actionBar);

        var code=new TextBox
        {
            AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        right.Children.Add(code);Grid.SetColumn(right,1);grid.Children.Add(right);

        string? selected=null;
        bool selectedEmbedded=false;
        var itemMap=new Dictionary<string,(bool Embedded,string Key)>(StringComparer.Ordinal);

        void refresh(string? reselectKey=null,bool reselectEmbedded=false)
        {
            itemMap.Clear();
            var items=new List<string>();

            foreach(var path in Directory.GetFiles(dir)
                .Where(p=>new[]{".czm",".vb",".cs"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)))
            {
                var file=Path.GetFileName(path);
                var source=File.ReadAllText(path);
                var friendly=GetMacroFriendlyName(source,Path.GetExtension(file));
                var label=string.IsNullOrWhiteSpace(friendly)?"Central | "+file:$"Central | {friendly}  [{file}]";
                itemMap[label]=(false,file);
                items.Add(label);
            }

            foreach(var key in embeddedMacros.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
            {
                var source=embeddedMacros[key];
                var friendly=GetMacroFriendlyName(source,Path.GetExtension(key));
                var label=string.IsNullOrWhiteSpace(friendly)?"Document | "+key:$"Document | {friendly}  [{key}]";
                itemMap[label]=(true,key);
                items.Add(label);
            }

            list.ItemsSource=items.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();

            if(!string.IsNullOrWhiteSpace(reselectKey))
            {
                var match=itemMap.FirstOrDefault(x=>x.Value.Embedded==reselectEmbedded && string.Equals(x.Value.Key,reselectKey,StringComparison.OrdinalIgnoreCase));
                if(!string.IsNullOrWhiteSpace(match.Key))list.SelectedItem=match.Key;
            }
        }

        list.SelectionChanged+=(_,__)=>
        {
            var item=list.SelectedItem?.ToString();
            if(string.IsNullOrWhiteSpace(item) || !itemMap.TryGetValue(item,out var mapped)){selected=null;return;}
            selectedEmbedded=mapped.Embedded;
            selected=mapped.Key;
            if(selectedEmbedded)
                code.Text=embeddedMacros.TryGetValue(selected,out var src)?src:"";
            else
            {
                var path=Path.Combine(dir,selected);
                code.Text=File.Exists(path)?File.ReadAllText(path):"";
            }
            var selectedExt=selected==null?"":Path.GetExtension(selected).ToLowerInvariant();
            run.Content=selectedExt==".czm"?"Run macro":"Compile && Run";
            run.IsEnabled=selected!=null && (selectedExt==".czm" || ((selectedExt==".vb"||selectedExt==".cs") && compiledMacrosEnabled));
            embed.IsEnabled=!selectedEmbedded && selected!=null && selectedExt==".czm";
            extract.IsEnabled=selectedEmbedded && selected!=null;
            rename.IsEnabled=!selectedEmbedded && selected!=null;
            delete.IsEnabled=selected!=null;
        };

        void make(string ext,string starter)
        {
            var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+ext;
            File.WriteAllText(Path.Combine(dir,name),starter);
            refresh(name,false);
        }

        ns.Click+=(_,__)=>make(".czm",
$@"# CtrlZed Safe Macro
# NAME: New Macro
# DESCRIPTION: Describe what this macro does.
# SHORTCUT:
#
# Supported commands:
{SafeMacroLanguage.Reference}

INSERT_TEXT Hello from a CtrlZed safe macro
NEW_PARAGRAPH
INSERT_FIELD date
");
        nv.Click+=(_,__)=>make(".vb",
@"Imports System.Windows.Controls

Public Module CtrlZedMacro
    Public Const MENU_OPTION As Integer = 1
    Public Const MENU_NAME As String = ""New VB Macro""

    Public Sub Run(Editor As RichTextBox)
        Editor.CaretPosition.InsertTextInRun(""Hello from a VB.NET macro"")
    End Sub
End Module
");
        nc.Click+=(_,__)=>make(".cs",
@"using System.Windows.Controls;

public static class CtrlZedMacro
{
    public const int MENU_OPTION = 1;
    public const string MENU_NAME = ""New C# Macro"";

    public static void Run(RichTextBox Editor)
    {
        Editor.CaretPosition.InsertTextInRun(""Hello from a C# macro"");
    }
}
");

        save.Click+=(_,__)=>
        {
            if(selected==null)return;
            if(selectedEmbedded)
            {
                embeddedMacros[selected]=code.Text;
                MarkDirty("Embedded macro updated.");
            }
            else File.WriteAllText(Path.Combine(dir,selected),code.Text);
            Status.Text="Macro source saved.";
        };

        run.Click+=async (_,__)=>
        {
            if(selected==null)return;
            if(selectedEmbedded)embeddedMacros[selected]=code.Text;
            else File.WriteAllText(Path.Combine(dir,selected),code.Text);
            try
            {
                await RunMacroSourceAsync(selected,code.Text);
                Status.Text="Macro completed.";
            }
            catch(Exception ex)
            {
                ShowMacroError(selected,ex);
            }
        };

        embed.Click+=(_,__)=>
        {
            if(selected==null||selectedEmbedded)return;
            embeddedMacros[selected]=code.Text;
            MarkDirty("Macro embedded in document.");
            refresh(selected,true);
        };

        extract.Click+=(_,__)=>
        {
            if(selected==null||!selectedEmbedded)return;
            var path=Path.Combine(dir,selected);
            File.WriteAllText(path,code.Text);
            Status.Text="Embedded macro copied to central library.";
            refresh(selected,false);
        };

        rename.Click+=(_,__)=>
        {
            if(selected==null || selectedEmbedded)return;
            var oldName=selected;
            var ext=Path.GetExtension(oldName);
            var stem=Path.GetFileNameWithoutExtension(oldName);
            var newStem=ShowMacroInput("New macro filename (without extension):",stem);
            if(string.IsNullOrWhiteSpace(newStem))return;
            foreach(var ch in Path.GetInvalidFileNameChars())newStem=newStem.Replace(ch,'_');
            newStem=newStem.Trim();
            if(string.IsNullOrWhiteSpace(newStem))return;
            var newName=newStem+ext;
            var oldPath=Path.Combine(dir,oldName);
            var newPath=Path.Combine(dir,newName);
            if(File.Exists(newPath))
            {
                MessageBox.Show("A macro with that filename already exists.","Rename macro",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            File.Move(oldPath,newPath);
            selected=newName;
            Status.Text=$"Macro renamed to {newName}.";
            refresh(newName,false);
        };

        delete.Click+=(_,__)=>
        {
            if(selected==null)return;

            var source=code.Text;
            var friendly=GetMacroFriendlyName(source,Path.GetExtension(selected));
            var display=string.IsNullOrWhiteSpace(friendly)?selected:$"{friendly} ({selected})";
            var scope=selectedEmbedded?"embedded document macro":"central macro file";

            var choice=MessageBox.Show(
                $"Delete {scope}:\n\n{display}?\n\nThis cannot be undone from CtrlZed.",
                "Delete macro",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if(choice!=MessageBoxResult.Yes)return;

            try
            {
                if(selectedEmbedded)
                {
                    if(embeddedMacros.Remove(selected))
                        MarkDirty("Embedded macro removed from document.");
                }
                else
                {
                    var path=Path.Combine(dir,selected);
                    if(File.Exists(path))File.Delete(path);
                    Status.Text="Macro file deleted.";
                }

                selected=null;
                code.Clear();
                refresh();
                run.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message,"Delete macro failed",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        };

        ai.Click+=(_,__)=>
        {
            var ext=selected==null?".czm":Path.GetExtension(selected).ToLowerInvariant();
            var reference=ext==".czm"
                ?(extendedMacrosEnabled?SafeMacroLanguage.ExtendedReference:SafeMacroLanguage.Reference)
                :"";
            AiManager.ShowForMacro(w,ext,reference,code.Text,result=>
            {
                code.Text=result;
                code.CaretIndex=code.Text.Length;
                code.Focus();
            });
        };

        refresh();run.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
        w.Content=grid;w.ShowDialog();
    }

    async Task RunMacroSourceAsync(string name,string source)
    {
        var ext=Path.GetExtension(name).ToLowerInvariant();
        if(ext==".czm")
        {
            await RunSafeMacroAsync(source);
            return;
        }

        if(ext is ".vb" or ".cs")
        {
            if(!compiledMacrosEnabled)
                throw new InvalidOperationException("Compiled VB/C# macros are disabled. Enable them in Macro Manager first.");
            await CompiledMacroHost.RunAsync(source,ext,Editor,this);
            MarkDirty("Compiled macro completed.");
            UpdateDocumentStats();
            Editor.Focus();
            return;
        }

        throw new InvalidOperationException("Unsupported macro type: "+ext);
    }

    async Task RunSafeMacroAsync(string source)
    {
        var lines=source.Replace("\r\n","\n").Split('\n');
        var executed=0;
        var variables=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string ExpandMacroVariables(string value)
        {
            foreach(var pair in variables)value=value.Replace("{"+pair.Key+"}",pair.Value,StringComparison.OrdinalIgnoreCase);
            return value;
        }
        foreach(var raw in lines)
        {
            var line=raw.Trim();
            if(line.Length==0 || line.StartsWith("#"))continue;
            if(line.StartsWith("MENU_OPTION",StringComparison.OrdinalIgnoreCase) || line.StartsWith("MENU_NAME",StringComparison.OrdinalIgnoreCase))continue;
            executed++;
            if(executed>200)throw new InvalidOperationException("Safe macro exceeds the 200-command limit.");

            var split=line.IndexOf(' ');
            var command=(split<0?line:line[..split]).Trim().ToUpperInvariant();
            var argument=split<0?"":line[(split+1)..];
            argument=ExpandMacroVariables(argument);

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
                case "PAGE_BREAK":
                    PageBreak_Click(this,new RoutedEventArgs());
                    break;
                case "APPLY_STYLE":
                    ApplyParagraphStyle(argument.Trim());
                    break;
                case "ALIGN":
                {
                    var a=argument.Trim().ToUpperInvariant();
                    var cmd=a switch
                    {
                        "LEFT"=>EditingCommands.AlignLeft,
                        "CENTRE"=>EditingCommands.AlignCenter,
                        "CENTER"=>EditingCommands.AlignCenter,
                        "RIGHT"=>EditingCommands.AlignRight,
                        "JUSTIFY"=>EditingCommands.AlignJustify,
                        _=>null
                    };
                    if(cmd==null)throw new InvalidOperationException("ALIGN expects LEFT, CENTRE, RIGHT or JUSTIFY.");
                    cmd.Execute(null,Editor);
                    break;
                }
                case "BULLETS":
                    EditingCommands.ToggleBullets.Execute(null,Editor);
                    break;
                case "NUMBERING":
                    EditingCommands.ToggleNumbering.Execute(null,Editor);
                    break;
                case "SELECT_ALL":
                    Editor.SelectAll();
                    break;
                case "FIND_REPLACE":
                {
                    var parts=argument.Split(new[]{"||"},2,StringSplitOptions.None);
                    if(parts.Length!=2)throw new InvalidOperationException("FIND_REPLACE expects: find text || replacement text");
                    ReplaceAllText(parts[0].Trim(),parts[1].Trim());
                    break;
                }
                case "INSERT_TABLE":
                {
                    var pieces=argument.Split(' ',StringSplitOptions.RemoveEmptyEntries);
                    if(pieces.Length!=2||!int.TryParse(pieces[0],out var rows)||!int.TryParse(pieces[1],out var cols)||rows<1||rows>50||cols<1||cols>20)
                        throw new InvalidOperationException("INSERT_TABLE expects rows columns (1–50 rows, 1–20 columns).");
                    InsertTable(rows,cols);
                    break;
                }
                case "SET_HEADER":
                    headerText=argument;RefreshHeaderFooter();MarkDirty("Header changed by safe macro.");
                    break;
                case "SET_FOOTER":
                    footerText=argument;RefreshHeaderFooter();MarkDirty("Footer changed by safe macro.");
                    break;
                case "SET_PROPERTY":
                {
                    var pos=argument.IndexOf(' ');
                    if(pos<1)throw new InvalidOperationException("SET_PROPERTY expects PROPERTY value.");
                    var name=argument[..pos].Trim().ToUpperInvariant();
                    var value=argument[(pos+1)..];
                    switch(name)
                    {
                        case "TITLE":documentTitle=value;break;
                        case "AUTHOR":documentAuthor=value;break;
                        case "SUBJECT":documentSubject=value;break;
                        case "KEYWORDS":documentKeywords=value;break;
                        default:throw new InvalidOperationException("Unknown document property: "+name);
                    }
                    MarkDirty("Document property changed by safe macro.");
                    break;
                }
                case "PROMPT":
                {
                    if(!extendedMacrosEnabled)throw new InvalidOperationException("PROMPT requires 'Allow extended macros' in Macro Manager.");
                    var pos=argument.IndexOf(' ');
                    if(pos<1)throw new InvalidOperationException("PROMPT expects: variable prompt text");
                    var name=argument[..pos].Trim();
                    var promptText=argument[(pos+1)..].Trim();
                    var value=ShowMacroInput(promptText);
                    if(value==null)return;
                    variables[name]=value;
                    break;
                }
                case "INSERT_QR":
                {
                    if(!extendedMacrosEnabled)throw new InvalidOperationException("INSERT_QR requires 'Allow extended macros' in Macro Manager.");
                    if(string.IsNullOrWhiteSpace(argument))throw new InvalidOperationException("INSERT_QR expects text or a URL.");
                    InsertQrCode(argument);
                    break;
                }
                case "AI_REWRITE":
                {
                    var selectedText=new TextRange(Editor.Selection.Start,Editor.Selection.End).Text;
                    if(string.IsNullOrWhiteSpace(selectedText))
                        throw new InvalidOperationException("AI_REWRITE requires selected text.");
                    var provider=AiManager.GetDefaultProvider();
                    var prompt=AiResultRenderer.DocumentPrompt(argument,selectedText,"",false);
                    var response=await AiDirectService.GenerateAsync(provider,prompt,false);
                    Editor.Selection.Text=response;
                    MarkDirty("AI rewrote the selected text.");
                    break;
                }
                case "AI_INSERT":
                {
                    var provider=AiManager.GetDefaultProvider();
                    var whole=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text;
                    var prompt=AiResultRenderer.DocumentPrompt(argument,"",whole,true);
                    var response=await AiDirectService.GenerateAsync(provider,prompt,true);
                    ApplyAiDocumentResult(response,false);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported safe macro command: {command}");
            }
        }
        MarkDirty();UpdateDocumentStats();Editor.Focus();
    }

    void ShowMacroError(string macroName,Exception ex)
    {
        var stage=ex is CompiledMacroException cme?cme.Stage:"Execution";
        var details=new System.Text.StringBuilder();
        details.AppendLine("CtrlZed Macro Diagnostic");
        details.AppendLine($"Macro: {macroName}");
        details.AppendLine($"Type: {Path.GetExtension(macroName).TrimStart('.').ToUpperInvariant()}");
        details.AppendLine($"Stage: {stage}");
        details.AppendLine($"Exception: {ex.GetType().FullName}");
        details.AppendLine($"Message: {ex.Message}");
        if(ex.InnerException!=null)
        {
            details.AppendLine();
            details.AppendLine($"Inner exception: {ex.InnerException.GetType().FullName}");
            details.AppendLine($"Inner message: {ex.InnerException.Message}");
        }
        if(!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            details.AppendLine();
            details.AppendLine("Stack trace:");
            details.AppendLine(ex.StackTrace);
        }

        var w=new Window{Title="Macro Error",Owner=this,Width=780,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()};
        var root=new DockPanel{Margin=new Thickness(12)};

        var top=new DockPanel{Margin=new Thickness(0,0,0,10)};
        var copy=new Button{Content="Copy Details",Width=105,Margin=new Thickness(8,0,0,0),HorizontalAlignment=HorizontalAlignment.Right};
        DockPanel.SetDock(copy,Dock.Right);top.Children.Add(copy);
        var summary=new TextBlock
        {
            Text=$"{macroName}\n{stage}: {ex.Message}",
            TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center
        };
        top.Children.Add(summary);
        DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4),IsDefault=true};
        copy.Click+=(_,__)=>{try{Clipboard.SetText(details.ToString());Status.Text="Macro diagnostic copied.";}catch{}};
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);

        var box=new TextBox
        {
            Text=details.ToString(),IsReadOnly=true,AcceptsReturn=true,IsReadOnlyCaretVisible=true,
            TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas")
        };
        root.Children.Add(box);
        w.Content=root;w.ShowDialog();
    }

    string? ShowMacroInput(string prompt,string initialValue="")
    {
        var w=new Window{Title="CtrlZed Macro",Owner=this,Width=500,Height=180,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(14)};
        g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var label=new TextBlock{Text=prompt,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        var input=new TextBox{Text=initialValue,Margin=new Thickness(0,0,0,10)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var ok=new Button{Content="OK",Width=80,IsDefault=true,Margin=new Thickness(4)};
        var cancel=new Button{Content="Cancel",Width=80,IsCancel=true,Margin=new Thickness(4)};
        buttons.Children.Add(ok);buttons.Children.Add(cancel);
        Grid.SetRow(label,0);Grid.SetRow(input,1);Grid.SetRow(buttons,2);
        g.Children.Add(label);g.Children.Add(input);g.Children.Add(buttons);
        ok.Click+=(_,__)=>w.DialogResult=true;
        w.Content=g;
        input.Loaded+=(_,__)=>input.Focus();
        return w.ShowDialog()==true?input.Text:null;
    }

    void InsertQrCode(string text)
    {
        using var generator=new QRCoder.QRCodeGenerator();
        using var data=generator.CreateQrCode(text,QRCoder.QRCodeGenerator.ECCLevel.Q);
        var qr=new QRCoder.PngByteQRCode(data);
        var bytes=qr.GetGraphic(12);
        var bitmap=new BitmapImage();
        using(var stream=new MemoryStream(bytes))
        {
            bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
        }
        var image=new Image{Source=bitmap,Width=180,Height=180,Stretch=Stretch.Uniform,Margin=new Thickness(4)};
        var ui=new InlineUIContainer(image,Editor.CaretPosition);
        WirePictureContainer(ui,image);
        Editor.CaretPosition=ui.ElementEnd;
        MarkDirty("QR code inserted.");
    }

    void ReplaceAllText(string find,string replacement)
    {
        if(string.IsNullOrEmpty(find))return;
        var start=Editor.Document.ContentStart;
        var count=0;
        while(true)
        {
            var found=FindText(start,find,StringComparison.CurrentCultureIgnoreCase);
            if(found==null)break;
            var range=new TextRange(found.Value.Start,found.Value.End);
            range.Text=replacement;
            start=range.End;
            if(++count>10000)break;
        }
        if(count>0)MarkDirty($"{count} replacement{(count==1?"":"s")} made by macro.");
    }

    void InsertTable(int rows,int cols)
    {
        var table=new Table{CellSpacing=0,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Margin=new Thickness(0,8,0,8)};
        for(int i=0;i<cols;i++)table.Columns.Add(new TableColumn());
        var group=new TableRowGroup();table.RowGroups.Add(group);
        for(int ri=0;ri<rows;ri++)
        {
            var row=new TableRow();group.Rows.Add(row);
            for(int ci=0;ci<cols;ci++)
                row.Cells.Add(new TableCell(new Paragraph(new Run(""))){BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0.5),Padding=new Thickness(5)});
        }
        InsertBlockAfterCaret(table);
        MarkDirty($"Inserted {rows} × {cols} table.");
    }

    async Task<bool> TryRunMacroShortcutAsync(KeyEventArgs e)
    {
        if(e.IsRepeat)return false;
        var pressed=BuildShortcutString(e);
        if(string.IsNullOrWhiteSpace(pressed))return false;

        foreach(var macro in GetMacroSources())
        {
            var configured=macro.Source.Replace("\r\n","\n").Split('\n')
                .FirstOrDefault(x=>x.TrimStart().StartsWith("# SHORTCUT:",StringComparison.OrdinalIgnoreCase));
            if(configured==null)continue;
            var shortcut=configured[(configured.IndexOf(':')+1)..].Trim();
            if(string.Equals(shortcut,pressed,StringComparison.OrdinalIgnoreCase))
            {
                try{await RunMacroSourceAsync(macro.Name,macro.Source);Status.Text="Macro: "+macro.Name;}
                catch(Exception ex){ShowMacroError(macro.Name,ex);}
                return true;
            }
        }
        return false;
    }

    static string BuildShortcutString(KeyEventArgs e)
    {
        var parts=new List<string>();
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))parts.Add("Alt");
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))parts.Add("Shift");
        if(parts.Count==0)return "";
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return "";
        parts.Add(key.ToString());
        return string.Join("+",parts);
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

    async void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(await TryRunMacroShortcutAsync(e)){e.Handled=true;return;}
        if(profile==KeyboardProfile.Word && Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift) && e.Key==Key.S)
        {
            SaveAs_Click(this,new RoutedEventArgs());e.Handled=true;return;
        }
        if(profile==KeyboardProfile.Word && Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.H)
        {
            FindReplace_Click(this,new RoutedEventArgs());
            e.Handled=true;
            return;
        }
        if(profile==KeyboardProfile.Word && Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.Enter)
        {
            PageBreak_Click(this,new RoutedEventArgs());
            e.Handled=true;
            return;
        }

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
        SaveAppSettings();
        TryDeleteRecovery();
        closingApproved=true;
    }

    void About_Click(object s,RoutedEventArgs e)
    {
        Branding.CreateAboutWindow(this,"0.14.3 beta 14.3").ShowDialog();
    }
}
