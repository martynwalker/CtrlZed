using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
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
    string documentVersion = "1.0";
    double zoomPercent = 100;
    bool extendedMacrosEnabled;
    bool compiledMacrosEnabled;
    bool fieldValuesVisible;
    string lastMacroLanguage=".vb";
    double savedWindowLeft=double.NaN,savedWindowTop=double.NaN,savedWindowWidth=1100,savedWindowHeight=760;

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
        public string Version { get; set; } = "1.0";
    }

    sealed class HeadingItem
    {
        public string Text { get; init; } = "";
        public Paragraph Paragraph { get; init; } = null!;
        public override string ToString() => Text;
    }

    sealed record MacroMenuMetadata(string Name,string Path,int Position,IReadOnlyList<int> PathPositions);
    sealed record MacroMenuTag(string Kind,int Position);

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
        ApplySavedWindowPlacement();
        EnsureDocumentPropertyDefaults();
        RefreshRecentFilesMenu();
        RefreshMacroMenus();
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
        if(fieldValuesVisible)SetFieldValuesVisible(false);
        suppressDirty = true;
        Editor.Document = new FlowDocument();
        currentFile = null;
        headerText = footerText = "";
        customFields.Clear();
        embeddedMacros.Clear();
        documentTitle=documentAuthor=documentSubject=documentKeywords="";
        documentVersion="1.0";
        EnsureDocumentPropertyDefaults();
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

    void Import_Click(object s,RoutedEventArgs e)
    {
        var d=new OpenFileDialog
        {
            Title="Import into CtrlZed",
            Filter="Supported files (*.docx;*.rtf;*.txt;*.md;*.markdown;*.html;*.htm;*.pdf)|*.docx;*.rtf;*.txt;*.md;*.markdown;*.html;*.htm;*.pdf|Word documents (*.docx)|*.docx|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|Markdown (*.md;*.markdown)|*.md;*.markdown|HTML (*.html;*.htm)|*.html;*.htm|PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*"
        };
        if(d.ShowDialog()!=true)return;

        try
        {
            var imported=ImportService.Load(d.FileName);
            using var stream=new MemoryStream();
            new TextRange(imported.ContentStart,imported.ContentEnd).Save(stream,DataFormats.Rtf);
            stream.Position=0;
            Editor.Selection.Load(stream,DataFormats.Rtf);
            WirePictureHandlers();
            MarkDirty($"Imported {Path.GetFileName(d.FileName)}.");
            UpdateDocumentStats();
            Editor.Focus();
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.InnerException?.Message??ex.Message,"Import failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
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

        var restoreFields=fieldValuesVisible;
        if(restoreFields)SetFieldValuesVisible(false);
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
        finally
        {
            if(restoreFields)SetFieldValuesVisible(true);
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
        var restoreFields=fieldValuesVisible;
        if(restoreFields)SetFieldValuesVisible(false);
        try
        {
            var tmp=recoveryFile+".tmp";
            using(var fs=File.Create(tmp))new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(fs,DataFormats.Rtf);
            File.Move(tmp,recoveryFile,true);
        }
        catch { }
        finally{if(restoreFields)SetFieldValuesVisible(true);}
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
                Title=documentTitle,Author=documentAuthor,Subject=documentSubject,Keywords=documentKeywords,Version=documentVersion
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
                documentVersion=string.IsNullOrWhiteSpace(v.Version)?"1.0":v.Version;
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
                       .Replace("{moonicon}",GetMoonPhaseIcon(now),StringComparison.OrdinalIgnoreCase)
                       .Replace("{version}",documentVersion,StringComparison.OrdinalIgnoreCase)
                       .Replace("{title}",documentTitle,StringComparison.OrdinalIgnoreCase);

        foreach(var pair in customFields)
        {
            var expanded=ExpandFieldsCore(pair.Value,page,pages,depth+1);
            foreach(var token in new[]{"{field:"+pair.Key+"}","{field."+pair.Key+"}","{"+pair.Key+"}"})
                if(result.Contains(token,StringComparison.OrdinalIgnoreCase))
                    result=result.Replace(token,expanded,StringComparison.OrdinalIgnoreCase);
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
            Text="Use built-ins such as {date}, {filename}, {moonphase}. Custom fields may be used as {name}, {field.name}, or {field:name} anywhere in the document, header or footer.",
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

        void SaveSelectedMacro()
        {
            if(selected==null)return;
            if(selectedEmbedded)
            {
                embeddedMacros[selected]=code.Text;
                MarkDirty("Embedded macro updated.");
            }
            else File.WriteAllText(Path.Combine(dir,selected),code.Text);
            Status.Text="Macro source saved.";
            RefreshMacroMenus();
        }

        save.Click+=(_,__)=>SaveSelectedMacro();
        saveExit.Click+=(_,__)=>
        {
            SaveSelectedMacro();
            w.Close();
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
                ShowMacroError(selected,code.Text,ex);
            }
        };

        embed.Click+=(_,__)=>
        {
            if(selected==null||selectedEmbedded)return;
            embeddedMacros[selected]=code.Text;
            MarkDirty("Macro embedded in document.");
            refresh(selected,true);RefreshMacroMenus();
        };

        extract.Click+=(_,__)=>
        {
            if(selected==null||!selectedEmbedded)return;
            var path=Path.Combine(dir,selected);
            File.WriteAllText(path,code.Text);
            Status.Text="Embedded macro copied to central library.";
            refresh(selected,false);RefreshMacroMenus();
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
            refresh(newName,false);RefreshMacroMenus();
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
                refresh();RefreshMacroMenus();
                run.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message,"Delete macro failed",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        };

        secrets.Click+=(_,__)=>ShowMacroSecretsManager(w);

        ai.Click+=(_,__)=>
        {
            var ext=language.SelectedIndex==0?".czm":language.SelectedIndex==2?".cs":".vb";
            var reference=ext==".czm"
                ?(extendedMacrosEnabled?SafeMacroLanguage.ExtendedReference:SafeMacroLanguage.Reference)
                :"";
            AiManager.ShowForMacro(w,ext,reference,"",(result,targetExt)=>
            {
                var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+targetExt;
                File.WriteAllText(Path.Combine(dir,name),result);
                refresh(name,false);
                selected=name;
                selectedEmbedded=false;
                code.Text=result;
                language.SelectedIndex=targetExt==".czm"?0:targetExt==".cs"?2:1;
                RefreshMacroMenus();
                code.CaretIndex=code.Text.Length;
                code.Focus();
            });
        };

        refresh();run.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
        w.Content=grid;w.ShowDialog();
    }

    void ShowMacroSecretsManager(Window owner)
    {
        var w=new Window
        {
            Title="CtrlZed Macro Secrets",Owner=owner,Width=620,Height=430,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var list=new ListBox{Margin=new Thickness(0,0,12,0)};
        Grid.SetColumn(list,0);Grid.SetRow(list,0);root.Children.Add(list);

        var form=new StackPanel();
        form.Children.Add(new TextBlock{Text="Secret name"});
        var name=new TextBox{Margin=new Thickness(0,4,0,10)};
        form.Children.Add(name);
        form.Children.Add(new TextBlock{Text="Secret value"});
        var value=new PasswordBox{Margin=new Thickness(0,4,0,10)};
        form.Children.Add(value);
        form.Children.Add(new TextBlock
        {
            Text="Values are encrypted with Windows DPAPI for the current Windows user. Saved values are never displayed again.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)
        });
        var configured=new TextBlock{Foreground=Brushes.DimGray};
        form.Children.Add(configured);
        Grid.SetColumn(form,1);Grid.SetRow(form,0);root.Children.Add(form);

        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var save=new Button{Content="Save / Replace",Width=105,Margin=new Thickness(4)};
        var remove=new Button{Content="Delete",Width=80,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        buttons.Children.Add(remove);buttons.Children.Add(save);buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);Grid.SetColumnSpan(buttons,2);Grid.SetRow(buttons,1);root.Children.Add(buttons);

        void Refresh()
        {
            list.ItemsSource=CtrlZedSecrets.Names();
            configured.Text=$"{CtrlZedSecrets.Names().Count} secret{(CtrlZedSecrets.Names().Count==1?"":"s")} configured.";
        }

        list.SelectionChanged+=(_,__)=>
        {
            if(list.SelectedItem is string key)
            {
                name.Text=key;
                value.Clear();
                configured.Text=$"{key} — configured. Enter a new value only to replace it.";
            }
        };

        save.Click+=(_,__)=>
        {
            var key=name.Text.Trim();
            if(string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Enter a secret name.","Macro Secrets",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            if(string.IsNullOrEmpty(value.Password))
            {
                MessageBox.Show("Enter a secret value. Existing values are deliberately not revealed.","Macro Secrets",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            try
            {
                CtrlZedSecrets.Set(key,value.Password);
                value.Clear();Refresh();list.SelectedItem=key;
                Status.Text=$"Secret '{key}' saved.";
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Macro Secrets",MessageBoxButton.OK,MessageBoxImage.Error);}
        };

        remove.Click+=(_,__)=>
        {
            if(list.SelectedItem is not string key)return;
            if(MessageBox.Show($"Delete secret '{key}'?","Delete secret",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            try
            {
                CtrlZedSecrets.Remove(key);
                name.Clear();value.Clear();Refresh();
                Status.Text=$"Secret '{key}' deleted.";
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Macro Secrets",MessageBoxButton.OK,MessageBoxImage.Error);}
        };

        close.Click+=(_,__)=>w.Close();
        Refresh();
        w.Content=root;w.ShowDialog();
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
            if(line.StartsWith("MENU_",StringComparison.OrdinalIgnoreCase) || line.StartsWith("MENU_PARENT",StringComparison.OrdinalIgnoreCase))continue;
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

    void ShowMacroError(string macroName,string source,Exception ex)
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

        var w=new Window
        {
            Title="CtrlZed Macro Debugger",Owner=this,Width=1120,Height=650,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(12)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var heading=new TextBlock
        {
            Text=$"{macroName} — {stage}",
            FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new Thickness(0,0,0,10)
        };
        Grid.SetRow(heading,0);Grid.SetColumnSpan(heading,2);root.Children.Add(heading);

        var sourceBox=new TextBox
        {
            Text=source,AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            Margin=new Thickness(0,0,6,0)
        };
        Grid.SetRow(sourceBox,1);Grid.SetColumn(sourceBox,0);root.Children.Add(sourceBox);

        var diagnosticBox=new TextBox
        {
            Text=details.ToString(),IsReadOnly=true,IsReadOnlyCaretVisible=true,AcceptsReturn=true,
            FontFamily=new FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(6,0,0,0)
        };
        Grid.SetRow(diagnosticBox,1);Grid.SetColumn(diagnosticBox,1);root.Children.Add(diagnosticBox);

        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var ask=new Button{Content="Ask AI to Fix…",Width=110,Margin=new Thickness(4)};
        var save=new Button{Content="Save Fixed Source",Width=115,Margin=new Thickness(4)};
        var copy=new Button{Content="Copy Details",Width=100,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4),IsDefault=true};
        buttons.Children.Add(ask);buttons.Children.Add(save);buttons.Children.Add(copy);buttons.Children.Add(close);
        Grid.SetRow(buttons,2);Grid.SetColumnSpan(buttons,2);root.Children.Add(buttons);

        void SaveSource()
        {
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");
            var central=Path.Combine(dir,macroName);
            if(File.Exists(central))
            {
                File.WriteAllText(central,sourceBox.Text);
                Status.Text=$"Fixed macro saved: {macroName}";
                return;
            }
            if(embeddedMacros.ContainsKey(macroName))
            {
                embeddedMacros[macroName]=sourceBox.Text;
                MarkDirty("Fixed embedded macro saved.");
                return;
            }
            throw new FileNotFoundException("CtrlZed could not locate the macro source to save.",macroName);
        }

        ask.Click+=(_,__)=>
        {
            var ext=Path.GetExtension(macroName).ToLowerInvariant();
            var reference=ext==".czm"?(extendedMacrosEnabled?SafeMacroLanguage.ExtendedReference:SafeMacroLanguage.Reference):"";
            var instruction=
                "Repair this CtrlZed macro using the diagnostic below. Preserve its intended behaviour, MENU_OPTION, MENU_NAME and AI prompt provenance. "+
                "Return corrected source only. Do not expose or invent secret values; use CtrlZedSecrets for credentials.\n\nDIAGNOSTIC:\n"+details;
            AiManager.ShowForMacro(w,ext,reference,sourceBox.Text,(result,targetExt)=>
            {
                if(!string.Equals(targetExt,ext,StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("The repair changed macro language. Choose the original target language to apply the fix here.","Macro Debugger",MessageBoxButton.OK,MessageBoxImage.Warning);
                    return;
                }
                sourceBox.Text=result;
                sourceBox.CaretIndex=0;
                heading.Text=$"{macroName} — AI fix ready for review";
            },instruction);
        };

        save.Click+=(_,__)=>
        {
            try{SaveSource();}
            catch(Exception saveEx){MessageBox.Show(saveEx.Message,"Save macro",MessageBoxButton.OK,MessageBoxImage.Error);}
        };
        copy.Click+=(_,__)=>{try{Clipboard.SetText(details.ToString());Status.Text="Macro diagnostic copied.";}catch{}};
        close.Click+=(_,__)=>w.Close();

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
                catch(Exception ex){ShowMacroError(macro.Name,macro.Source,ex);}
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
        CaptureWindowPlacement();
        SaveAppSettings();
        TryDeleteRecovery();
        closingApproved=true;
    }

    void About_Click(object s,RoutedEventArgs e)
    {
        Branding.CreateAboutWindow(this,"0.18.0 beta 18").ShowDialog();
    }
}
