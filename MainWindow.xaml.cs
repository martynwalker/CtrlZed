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
    readonly DispatcherTimer uiRefreshTimer;
    readonly string recoveryFile;
    readonly string recoverySettingsFile;
    readonly Dictionary<string,string> customFields = new(StringComparer.OrdinalIgnoreCase); // legacy per-document fields
    readonly Dictionary<string,string> systemFields = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> userFields = new(StringComparer.OrdinalIgnoreCase);
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
    bool keepParagraphTogether=true;
    string lastMacroLanguage=".vb";
    readonly Dictionary<string,StyleSetStore.StyleSet> styleSets=StyleSetStore.Load();
    string activeStyleSet="Default";
    string signatureImagePath="";
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
        public List<DocumentCommentData> Comments { get; set; } = new();
    }

    sealed class DocumentCommentData
    {
        public string Id { get; set; }=Guid.NewGuid().ToString("N");
        public int Offset { get; set; }
        public string Text { get; set; }="";
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
        RefreshStyleSetBox();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,new KeyGesture(Key.S,ModifierKeys.Control)));
        Editor.PreviewMouseLeftButtonDown += Editor_PreviewMouseLeftButtonDown;
        Editor.PreviewMouseRightButtonDown += Editor_PreviewMouseRightButtonDown;

        var recoveryDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        Directory.CreateDirectory(recoveryDir);
        recoveryFile = Path.Combine(recoveryDir,"Recovery.rtf");
        recoverySettingsFile = Path.Combine(recoveryDir,"Recovery.settings.json");
        settingsFile = Path.Combine(recoveryDir,"settings.json");
        ReloadPersistentFields();
        LoadAppSettings();
        RefreshStyleSetBox();
        UpdateKeyboardMenuState();
        ApplySavedWindowPlacement();
        EnsureDocumentPropertyDefaults();
        RefreshRecentFilesMenu();
        RefreshMacroMenus();
        recoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        recoveryTimer.Tick += (_,__) => WriteRecoverySnapshot();
        recoveryTimer.Start();

        uiRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        uiRefreshTimer.Tick += (_,__)=>
        {
            uiRefreshTimer.Stop();
            UpdatePageGuideState();
            UpdateDocumentStats();
            if(NavigationPane!=null && NavigationPane.Visibility==Visibility.Visible)RefreshNavigation();
        };

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
                    if(File.Exists(recoverySettingsFile))
                    {
                        try
                        {
                            var recovered=JsonSerializer.Deserialize<CzdSettings>(File.ReadAllText(recoverySettingsFile));
                            if(recovered!=null)ApplyRecoveredSettings(recovered);
                        }
                        catch(Exception settingsEx)
                        {
                            MessageBox.Show("The document body was recovered, but its recovery settings could not be read.\n\n"+settingsEx.Message,
                                "Recovery settings",MessageBoxButton.OK,MessageBoxImage.Warning);
                        }
                    }
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

    void ReloadPersistentFields()
    {
        systemFields.Clear();
        foreach(var pair in PersistentFieldStore.LoadSystem())systemFields[pair.Key]=pair.Value;
        userFields.Clear();
        foreach(var pair in PersistentFieldStore.LoadUser())userFields[pair.Key]=pair.Value;
    }

    void SaveUserFields()
    {
        PersistentFieldStore.SaveUser(userFields);
        Status.Text="User fields saved.";
    }

    void MigrateLegacyDocumentFields()
    {
        var changed=false;
        foreach(var pair in customFields)
        {
            if(userFields.ContainsKey(pair.Key))continue;
            userFields[pair.Key]=pair.Value;
            changed=true;
        }
        if(changed)
        {
            PersistentFieldStore.SaveUser(userFields);
            Status.Text="Legacy document fields migrated to persistent user fields.";
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
            else if(ext==".docx")
            {
                var exportClone=CloneDocument();
                DocxInterop.Save(currentFile,exportClone,headerText,footerText,pageWidth,pageHeight,pageMargins,documentTitle,documentAuthor,documentSubject,documentKeywords);
            }
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
        if(uiRefreshTimer==null)return;
        uiRefreshTimer.Stop();
        uiRefreshTimer.Start();
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

    CzdSettings CaptureDocumentSettings()
        =>new()
        {
            PageWidth=pageWidth,PageHeight=pageHeight,
            Left=pageMargins.Left,Top=pageMargins.Top,Right=pageMargins.Right,Bottom=pageMargins.Bottom,
            Header=headerText,Footer=footerText,
            CustomFields=new Dictionary<string,string>(customFields,StringComparer.OrdinalIgnoreCase),
            EmbeddedMacros=new Dictionary<string,string>(embeddedMacros,StringComparer.OrdinalIgnoreCase),
            Title=documentTitle,Author=documentAuthor,Subject=documentSubject,Keywords=documentKeywords,Version=documentVersion,
            Comments=CaptureComments()
        };

    void ApplyRecoveredSettings(CzdSettings v)
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
        if(v.CustomFields!=null)
            foreach(var pair in v.CustomFields)
                if(!string.IsNullOrWhiteSpace(pair.Key))
                    customFields[pair.Key]=pair.Value??"";
        MigrateLegacyDocumentFields();

        embeddedMacros.Clear();
        if(v.EmbeddedMacros!=null)
            foreach(var pair in v.EmbeddedMacros)
                if(!string.IsNullOrWhiteSpace(pair.Key))
                    embeddedMacros[pair.Key]=pair.Value??"";

        ApplyPage();
        RefreshHeaderFooter();
        RefreshMacroMenus();
        RestoreComments(v.Comments);

    }

    const string CommentTagPrefix="CtrlZedComment:";

    InlineUIContainer CreateCommentContainer(DocumentCommentData data,TextPointer position)
    {
        var box=new TextBox
        {
            Text=data.Text,
            AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,
            MinWidth=180,MaxWidth=280,
            MinHeight=54,
            Background=new SolidColorBrush(Color.FromRgb(255,248,168)),
            BorderThickness=new Thickness(0),
            Padding=new Thickness(6)
        };
        box.TextChanged+=(_,__)=>
        {
            if(!suppressDirty)MarkDirty("Comment updated.");
        };
        var border=new Border
        {
            Child=box,
            Background=new SolidColorBrush(Color.FromRgb(255,244,128)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(210,190,70)),
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(3),
            Margin=new Thickness(4),
            Padding=new Thickness(2),
            Tag=CommentTagPrefix+data.Id
        };
        var ui=new InlineUIContainer(border,position);
        return ui;
    }

    IEnumerable<(InlineUIContainer Container,Border Border,TextBox Box,string Id)> EnumerateComments()
    {
        foreach(var paragraph in EnumerateDocumentParagraphs())
        {
            foreach(var inline in paragraph.Inlines)
            {
                if(inline is not InlineUIContainer ui || ui.Child is not Border border || border.Tag is not string tag || !tag.StartsWith(CommentTagPrefix,StringComparison.Ordinal))continue;
                if(border.Child is not TextBox box)continue;
                yield return (ui,border,box,tag[CommentTagPrefix.Length..]);
            }
        }
    }

    List<DocumentCommentData> CaptureComments()
    {
        var result=new List<DocumentCommentData>();
        foreach(var item in EnumerateComments())
        {
            try
            {
                result.Add(new DocumentCommentData
                {
                    Id=item.Id,
                    Offset=Editor.Document.ContentStart.GetOffsetToPosition(item.Container.ContentStart),
                    Text=item.Box.Text
                });
            }
            catch{}
        }
        return result;
    }

    void RestoreComments(IEnumerable<DocumentCommentData>? comments)
    {
        if(comments==null)return;
        foreach(var data in comments)
        {
            try
            {
                var pos=Editor.Document.ContentStart.GetPositionAtOffset(Math.Max(0,data.Offset),LogicalDirection.Forward)
                        ??Editor.Document.ContentEnd;
                CreateCommentContainer(data,pos);
            }
            catch{}
        }
    }

    void SetCommentsVisibility(Visibility visibility)
    {
        foreach(var item in EnumerateComments())item.Border.Visibility=visibility;
    }

    void Comment_Click(object s,RoutedEventArgs e)
    {
        var input=ShowMacroInput("Insert Comment","Comment text:");
        if(input==null)return;
        var data=new DocumentCommentData{Text=input};
        CreateCommentContainer(data,Editor.Selection.End);
        Editor.CaretPosition=Editor.Selection.End;
        MarkDirty("Comment inserted.");
        Editor.Focus();
    }

    void WriteRecoverySnapshot()
    {
        if(!dirty)return;
        var restoreFields=fieldValuesVisible;
        if(restoreFields)SetFieldValuesVisible(false);
        try
        {
            var tmp=recoveryFile+".tmp";
            var recoveryClone=CloneDocument();
            using(var fs=File.Create(tmp))new TextRange(recoveryClone.ContentStart,recoveryClone.ContentEnd).Save(fs,DataFormats.Rtf);
            File.Move(tmp,recoveryFile,true);

            var settingsTmp=recoverySettingsFile+".tmp";
            File.WriteAllText(settingsTmp,JsonSerializer.Serialize(CaptureDocumentSettings()));
            File.Move(settingsTmp,recoverySettingsFile,true);
        }
        catch { }
        finally
        {
            if(restoreFields)SetFieldValuesVisible(true);
        }
    }

    void TryDeleteRecovery()
    {
        try{if(File.Exists(recoveryFile))File.Delete(recoveryFile);}catch{}
        try{if(File.Exists(recoverySettingsFile))File.Delete(recoverySettingsFile);}catch{}
    }

    void SaveRtf(string path)
    {
        var tmp=path+".tmp";
        var exportClone=CloneDocument();
        using(var fs=File.Create(tmp))new TextRange(exportClone.ContentStart,exportClone.ContentEnd).Save(fs,DataFormats.Rtf);
        ReplaceFile(tmp,path);
    }

    void SaveCzd(string path)
    {
        var tmp=path+".tmp";
        if(File.Exists(tmp))File.Delete(tmp);
        using(var zip=ZipFile.Open(tmp,ZipArchiveMode.Create))
        {
            var doc=zip.CreateEntry("document.rtf");
            var documentClone=CloneDocument();
            using(var s=doc.Open())new TextRange(documentClone.ContentStart,documentClone.ContentEnd).Save(s,DataFormats.Rtf);
            var settings=zip.CreateEntry("settings.json");
            var data=CaptureDocumentSettings();
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
            if(v!=null)ApplyRecoveredSettings(v);
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

    IEnumerable<Paragraph> EnumerateParagraphs(FlowDocument document)
    {
        foreach(var block in document.Blocks)
            foreach(var p in EnumerateParagraphsInBlock(block))
                yield return p;
    }

    void ExpandPreviewFields(FlowDocument document)
    {
        foreach(var paragraph in EnumerateParagraphs(document).ToList())
            ExpandPreviewInlineCollection(paragraph.Inlines);
    }

    void ExpandPreviewInlineCollection(InlineCollection inlines)
    {
        foreach(var inline in inlines.ToList())
        {
            if(inline is Span span)
            {
                ExpandPreviewInlineCollection(span.Inlines);
                continue;
            }
            if(inline is not Run run || string.IsNullOrEmpty(run.Text))continue;

            var matches=System.Text.RegularExpressions.Regex.Matches(run.Text,@"\{(?:(?:field|system|user)[:.])?[A-Za-z0-9_-]+\}");
            if(matches.Count==0)continue;

            var replacements=new List<Inline>();
            var pos=0;
            foreach(System.Text.RegularExpressions.Match match in matches)
            {
                if(match.Index>pos)replacements.Add(CloneRun(run,run.Text[pos..match.Index]));
                var token=match.Value;
                replacements.Add(CloneRun(run,TryExpandDocumentField(token,out var expanded)?expanded:token));
                pos=match.Index+match.Length;
            }
            if(pos<run.Text.Length)replacements.Add(CloneRun(run,run.Text[pos..]));

            foreach(var replacement in replacements)inlines.InsertBefore(run,replacement);
            inlines.Remove(run);
        }
    }

    HeaderFooterPaginator CreatePreviewPaginator()
    {
        var clone=CloneDocument();
        ExpandPreviewFields(clone);
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
        SetCommentsVisibility(Visibility.Collapsed);
        try
        {
            using var stream=new MemoryStream();
            var source=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd);
            source.Save(stream,DataFormats.Rtf);
            stream.Position=0;
            var clone=new FlowDocument();
            new TextRange(clone.ContentStart,clone.ContentEnd).Load(stream,DataFormats.Rtf);
            return clone;
        }
        finally
        {
            SetCommentsVisibility(Visibility.Visible);
        }
    }

    string ExpandFields(string text,int page,int pages)
    {
        return ExpandFieldsCore(text??"",page,pages,0,new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    string ExpandFieldsCore(string text,int page,int pages,int depth,HashSet<string> resolving)
    {
        if(depth>16)return text;
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

        string Resolve(string scope,string key,string template)
        {
            var id=scope+":"+key;
            if(!resolving.Add(id))return "[Circular field: "+id+"]";
            try{return ExpandFieldsCore(template,page,pages,depth+1,resolving);}
            finally{resolving.Remove(id);}
        }

        foreach(var pair in systemFields)
        {
            var token="{system:"+pair.Key+"}";
            if(!result.Contains(token,StringComparison.OrdinalIgnoreCase))continue;
            result=result.Replace(token,Resolve("system",pair.Key,pair.Value),StringComparison.OrdinalIgnoreCase);
        }

        foreach(var pair in userFields)
        {
            var explicitToken="{user:"+pair.Key+"}";
            var legacyTokens=new[]{"{field:"+pair.Key+"}","{field."+pair.Key+"}","{"+pair.Key+"}"};
            var needed=result.Contains(explicitToken,StringComparison.OrdinalIgnoreCase) ||
                       legacyTokens.Any(t=>result.Contains(t,StringComparison.OrdinalIgnoreCase));
            if(!needed)continue;

            var expanded=Resolve("user",pair.Key,pair.Value);
            result=result.Replace(explicitToken,expanded,StringComparison.OrdinalIgnoreCase);
            foreach(var token in legacyTokens)
                result=result.Replace(token,expanded,StringComparison.OrdinalIgnoreCase);
        }

        foreach(var pair in customFields)
        {
            if(userFields.ContainsKey(pair.Key))continue;
            var tokens=new[]{"{field:"+pair.Key+"}","{field."+pair.Key+"}","{"+pair.Key+"}"};
            if(!tokens.Any(t=>result.Contains(t,StringComparison.OrdinalIgnoreCase)))continue;
            var expanded=Resolve("legacy",pair.Key,pair.Value);
            foreach(var token in tokens)
                result=result.Replace(token,expanded,StringComparison.OrdinalIgnoreCase);
        }

        foreach(var pair in systemFields)
        {
            if(userFields.ContainsKey(pair.Key) || customFields.ContainsKey(pair.Key))continue;
            var token="{"+pair.Key+"}";
            if(!result.Contains(token,StringComparison.OrdinalIgnoreCase))continue;
            result=result.Replace(token,Resolve("system",pair.Key,pair.Value),StringComparison.OrdinalIgnoreCase);
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
        var w=new Window{Title="CtrlZed Fields",Owner=this,Width=720,Height=500,WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()};
        var root=new Grid{Margin=new Thickness(12)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(240)});
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

        var scope=new TextBox{Margin=new Thickness(0,3,0,8),IsReadOnly=true,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=Brushes.DimGray};
        var name=new TextBox{Margin=new Thickness(0,3,0,8)};
        var value=new TextBox{Margin=new Thickness(0,3,0,8),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=130};
        AddRow(edit,0,"Scope",scope);
        AddRow(edit,1,"Name",name);
        AddRow(edit,2,"Template",value);
        var hint=new TextBlock
        {
            Text="System fields are shipped with CtrlZed and are read-only: {system:name}. User fields are persistent on this Windows profile: {user:name}. Legacy {name}, {field.name} and {field:name} remain supported.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,8)
        };
        Grid.SetRow(hint,3);Grid.SetColumnSpan(hint,2);edit.Children.Add(hint);
        Grid.SetColumn(edit,1);Grid.SetRow(edit,0);root.Children.Add(edit);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var addUser=new Button{Content="New user field",Width=105,Margin=new Thickness(4)};
        var save=new Button{Content="Save user field",Width=105,Margin=new Thickness(4)};
        var remove=new Button{Content="Delete",Width=75,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=75,Margin=new Thickness(4)};
        buttons.Children.Add(addUser);buttons.Children.Add(remove);buttons.Children.Add(save);buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);Grid.SetColumnSpan(buttons,2);Grid.SetRow(buttons,1);root.Children.Add(buttons);

        var map=new Dictionary<string,(string Scope,string Key)>(StringComparer.Ordinal);
        void RefreshList()
        {
            map.Clear();
            var items=new List<string>();
            foreach(var key in systemFields.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
            {
                var label="System | "+key;
                map[label]=("system",key);items.Add(label);
            }
            foreach(var key in userFields.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
            {
                var label="User | "+key;
                map[label]=("user",key);items.Add(label);
            }
            list.ItemsSource=items;
        }

        void SetEditable(bool editable)
        {
            name.IsReadOnly=!editable;
            value.IsReadOnly=!editable;
            save.IsEnabled=editable;
            remove.IsEnabled=editable;
        }

        list.SelectionChanged+=(_,__)=>
        {
            if(list.SelectedItem is not string label || !map.TryGetValue(label,out var item))return;
            scope.Text=item.Scope=="system"?"System — distributed with CtrlZed":"User — persistent local profile";
            name.Text=item.Key;
            if(item.Scope=="system")
            {
                value.Text=systemFields.TryGetValue(item.Key,out var template)?template:"";
                SetEditable(false);
            }
            else
            {
                value.Text=userFields.TryGetValue(item.Key,out var template)?template:"";
                SetEditable(true);
            }
        };

        addUser.Click+=(_,__)=>
        {
            list.SelectedItem=null;
            scope.Text="User — persistent local profile";
            name.Clear();value.Clear();SetEditable(true);name.Focus();
        };

        save.Click+=(_,__)=>
        {
            var key=name.Text.Trim();
            if(string.IsNullOrWhiteSpace(key) || key.Any(ch=>!(char.IsLetterOrDigit(ch)||ch=='_'||ch=='-')))
            {
                MessageBox.Show("Field names may contain letters, numbers, hyphens and underscores.","Invalid field",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            userFields[key]=value.Text;
            SaveUserFields();
            RefreshList();
            list.SelectedItem="User | "+key;
            RefreshHeaderFooter();
            if(fieldValuesVisible){SetFieldValuesVisible(false,false);SetFieldValuesVisible(true,false);}
        };

        remove.Click+=(_,__)=>
        {
            if(list.SelectedItem is not string label || !map.TryGetValue(label,out var item) || item.Scope!="user")return;
            if(userFields.Remove(item.Key))
            {
                PersistentFieldStore.SaveUser(userFields);
                name.Clear();value.Clear();scope.Text="";RefreshList();SetEditable(false);
                RefreshHeaderFooter();
                if(fieldValuesVisible){SetFieldValuesVisible(false,false);SetFieldValuesVisible(true,false);}
                Status.Text="User field deleted.";
            }
        };

        close.Click+=(_,__)=>w.Close();
        ReloadPersistentFields();
        RefreshList();
        SetEditable(false);
        w.Content=root;
        w.ShowDialog();
    }

    const string FieldTagPrefix="CtrlZedField:";

    bool TryExpandDocumentField(string token,out string value)
    {
        value="";
        if(string.IsNullOrWhiteSpace(token) || token.Length<3 || token[0]!='{' || token[^1]!='}')return false;
        var inner=token[1..^1].Trim();

        if(inner.StartsWith("system:",StringComparison.OrdinalIgnoreCase))
        {
            var key=inner[7..];
            if(!systemFields.TryGetValue(key,out var template))return false;
            value=ExpandFields(template,1,1);
            return true;
        }
        if(inner.StartsWith("user:",StringComparison.OrdinalIgnoreCase))
        {
            var key=inner[5..];
            if(!userFields.TryGetValue(key,out var template))return false;
            value=ExpandFields(template,1,1);
            return true;
        }

        var name=inner;
        if(inner.StartsWith("field:",StringComparison.OrdinalIgnoreCase) || inner.StartsWith("field.",StringComparison.OrdinalIgnoreCase))
            name=inner[6..];

        if(userFields.TryGetValue(name,out var user))
        {
            value=ExpandFields(user,1,1);
            return true;
        }
        if(customFields.TryGetValue(name,out var legacy))
        {
            value=ExpandFields(legacy,1,1);
            return true;
        }
        if(systemFields.TryGetValue(name,out var system))
        {
            value=ExpandFields(system,1,1);
            return true;
        }

        var builtins=new HashSet<string>(new[]{"page","pages","date","time","filename","moonphase","moonicon","version","title"},StringComparer.OrdinalIgnoreCase);
        if(!builtins.Contains(inner))return false;
        value=ExpandFields(token,1,1);
        return true;
    }

    static Run CloneRun(Run source,string text)
    {
        var clone=new Run(text)
        {
            FontFamily=source.FontFamily,FontSize=source.FontSize,FontWeight=source.FontWeight,
            FontStyle=source.FontStyle,Foreground=source.Foreground,Background=source.Background
        };
        if(source.TextDecorations!=null)
            clone.TextDecorations=source.TextDecorations.Clone();
        return clone;
    }

    void ExpandFieldsInInlineCollection(InlineCollection inlines,HashSet<string> missingFields)
    {
        foreach(var inline in inlines.ToList())
        {
            if(inline is Span span)
            {
                ExpandFieldsInInlineCollection(span.Inlines,missingFields);
                continue;
            }
            if(inline is not Run run || string.IsNullOrEmpty(run.Text) || run.Tag!=null)continue;

            var matches=System.Text.RegularExpressions.Regex.Matches(run.Text,@"\{(?:(?:field|system|user)[:.])?[A-Za-z0-9_-]+\}");
            if(matches.Count==0)continue;

            var replacements=new List<Inline>();
            var pos=0;
            foreach(System.Text.RegularExpressions.Match match in matches)
            {
                if(match.Index>pos)replacements.Add(CloneRun(run,run.Text[pos..match.Index]));
                var token=match.Value;
                if(TryExpandDocumentField(token,out var expanded))
                {
                    var fieldRun=CloneRun(run,expanded);
                    fieldRun.Tag=FieldTagPrefix+token;
                    replacements.Add(fieldRun);
                }
                else
                {
                    replacements.Add(CloneRun(run,token));
                    missingFields.Add(token);
                }
                pos=match.Index+match.Length;
            }
            if(pos<run.Text.Length)replacements.Add(CloneRun(run,run.Text[pos..]));

            foreach(var replacement in replacements)inlines.InsertBefore(run,replacement);
            inlines.Remove(run);
        }
    }

    void RestoreFieldCodesInInlineCollection(InlineCollection inlines)
    {
        foreach(var inline in inlines.ToList())
        {
            if(inline is Span span)RestoreFieldCodesInInlineCollection(span.Inlines);
            else if(inline is Run run && run.Tag is string tag && tag.StartsWith(FieldTagPrefix,StringComparison.Ordinal))
            {
                run.Text=tag[FieldTagPrefix.Length..];
                run.Tag=null;
                run.ClearValue(TextElement.BackgroundProperty);
            }
        }
    }

    byte[] SnapshotEditorRtf()
    {
        using var stream=new MemoryStream();
        new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(stream,DataFormats.Rtf);
        return stream.ToArray();
    }

    void RestoreEditorRtf(byte[] snapshot)
    {
        using var stream=new MemoryStream(snapshot,false);
        Editor.Document=new FlowDocument();
        new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(stream,DataFormats.Rtf);
        WirePictureHandlers();
    }

    bool SetFieldValuesVisible(bool visible,bool showDiagnostics=true)
    {
        if(visible==fieldValuesVisible)return true;

        var snapshot=SnapshotEditorRtf();
        var oldSuppress=suppressDirty;
        var oldDirty=dirty;
        suppressDirty=true;
        var missing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach(var paragraph in EnumerateDocumentParagraphs().ToList())
            {
                if(visible)ExpandFieldsInInlineCollection(paragraph.Inlines,missing);
                else RestoreFieldCodesInInlineCollection(paragraph.Inlines);
            }

            fieldValuesVisible=visible;
            if(FieldDisplayToggle!=null)
            {
                FieldDisplayToggle.IsChecked=visible;
                FieldDisplayToggle.Content=visible?"Values":"Fields";
                FieldDisplayToggle.ToolTip=visible?"Show field codes":"Show field values";
            }

            if(visible && showDiagnostics && missing.Count>0)
            {
                var names=string.Join(Environment.NewLine,missing.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase));
                MessageBox.Show(
                    "These field codes were not found and have been left unchanged:"+Environment.NewLine+Environment.NewLine+names+
                    Environment.NewLine+Environment.NewLine+"Create the field under Tools → Fields, or correct the field name.",
                    "Field not found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return true;
        }
        catch(Exception ex)
        {
            try{RestoreEditorRtf(snapshot);}catch{}
            fieldValuesVisible=false;
            if(FieldDisplayToggle!=null)
            {
                FieldDisplayToggle.IsChecked=false;
                FieldDisplayToggle.Content="Fields";
                FieldDisplayToggle.ToolTip="Show field values";
            }
            if(showDiagnostics)
                MessageBox.Show(
                    "CtrlZed could not display field values. The document has been restored unchanged."+Environment.NewLine+Environment.NewLine+
                    ex.GetType().Name+": "+ex.Message,
                    "Field display error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            return false;
        }
        finally
        {
            suppressDirty=oldSuppress;
            dirty=oldDirty;
            UpdateTitle();
        }
    }

    void FieldDisplay_Click(object s,RoutedEventArgs e)
        =>SetFieldValuesVisible(FieldDisplayToggle.IsChecked==true);

    void HeaderFooter_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Header and Footer",Owner=this,Width=700,Height=420,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var grid=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<6;i++)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        Grid FieldRow(TextBox box,ComboBox templates)
        {
            var row=new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});
            box.Margin=new Thickness(0,3,4,8);
            templates.Width=30;templates.Margin=new Thickness(0,3,0,8);templates.ToolTip="Templates";
            Grid.SetColumn(box,0);Grid.SetColumn(templates,1);
            row.Children.Add(box);row.Children.Add(templates);
            return row;
        }

        var h=new TextBox{Text=headerText};
        var ft=new TextBox{Text=footerText};
        var headerTemplates=new ComboBox();
        var footerTemplates=new ComboBox();
        foreach(var item in new[]{"","Title | Page n of n | Date & Version","Title | Page n of n","Filename | Page n of n | Date","Title only"})
            headerTemplates.Items.Add(item);
        foreach(var item in new[]{"","Page n of n","Filename | Page n of n","Date & Version","Blank"})
            footerTemplates.Items.Add(item);
        headerTemplates.SelectedIndex=0;footerTemplates.SelectedIndex=0;

        void ApplyHeaderTemplate()
        {
            h.Text=headerTemplates.SelectedIndex switch
            {
                1=>"{title}\tPage {page} of {pages}\t{date} · v{version}",
                2=>"{title}\tPage {page} of {pages}\t",
                3=>"{filename}\tPage {page} of {pages}\t{date}",
                4=>"{title}",
                _=>h.Text
            };
        }
        void ApplyFooterTemplate()
        {
            ft.Text=footerTemplates.SelectedIndex switch
            {
                1=>"\tPage {page} of {pages}\t",
                2=>"{filename}\tPage {page} of {pages}\t",
                3=>"\t\t{date} · v{version}",
                4=>"",
                _=>ft.Text
            };
        }
        headerTemplates.SelectionChanged+=(_,__)=>{if(headerTemplates.SelectedIndex>0)ApplyHeaderTemplate();};
        footerTemplates.SelectionChanged+=(_,__)=>{if(footerTemplates.SelectedIndex>0)ApplyFooterTemplate();};

        var headerLabel=new TextBlock{Text="Header",VerticalAlignment=VerticalAlignment.Center};
        var footerLabel=new TextBlock{Text="Footer",VerticalAlignment=VerticalAlignment.Center};
        var headerRow=FieldRow(h,headerTemplates);
        var footerRow=FieldRow(ft,footerTemplates);
        Grid.SetRow(headerLabel,0);Grid.SetColumn(headerLabel,0);grid.Children.Add(headerLabel);
        Grid.SetRow(headerRow,0);Grid.SetColumn(headerRow,1);grid.Children.Add(headerRow);
        Grid.SetRow(footerLabel,1);Grid.SetColumn(footerLabel,0);grid.Children.Add(footerLabel);
        Grid.SetRow(footerRow,1);Grid.SetColumn(footerRow,1);grid.Children.Add(footerRow);

        var help=new TextBlock
        {
            Text="Use tab-separated left, centre and right sections. Built-ins: {title}, {page}, {pages}, {date}, {time}, {filename}, {version}, {moonphase}, {moonicon}. Custom fields use {field:name}.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,8)
        };
        Grid.SetRow(help,2);Grid.SetColumnSpan(help,2);grid.Children.Add(help);

        var preview=new TextBlock{Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        void RefreshPreview()
        {
            preview.Text="Header example: "+ExpandFields(h.Text,1,Math.Max(1,CreatePreviewPaginator().PageCount))+
                         "\nFooter example: "+ExpandFields(ft.Text,1,Math.Max(1,CreatePreviewPaginator().PageCount));
        }
        h.TextChanged+=(_,__)=>RefreshPreview();ft.TextChanged+=(_,__)=>RefreshPreview();
        Grid.SetRow(preview,3);Grid.SetColumnSpan(preview,2);grid.Children.Add(preview);

        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        Grid.SetRow(ok,5);Grid.SetColumnSpan(ok,2);grid.Children.Add(ok);
        ok.Click+=(_,__)=>
        {
            headerText=h.Text;
            footerText=ft.Text;
            RefreshHeaderFooter();
            MarkDirty("Header/footer updated.");
            w.DialogResult=true;
        };
        RefreshPreview();
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
        public string LastMacroLanguage { get; set; } = ".vb";
        public bool KeepParagraphTogether { get; set; } = true;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 760;
        public string SignatureImagePath { get; set; } = "";
        public string ActiveStyleSet { get; set; } = "Default";
        public string KeyboardProfile { get; set; } = "Word";
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
            lastMacroLanguage=settings.LastMacroLanguage is ".czm" or ".vb" or ".cs"?settings.LastMacroLanguage:".vb";
            keepParagraphTogether=settings.KeepParagraphTogether;
            savedWindowLeft=settings.WindowLeft;savedWindowTop=settings.WindowTop;
            savedWindowWidth=settings.WindowWidth;savedWindowHeight=settings.WindowHeight;
            signatureImagePath=settings.SignatureImagePath??"";
            activeStyleSet=styleSets.ContainsKey(settings.ActiveStyleSet??"")?settings.ActiveStyleSet:"Default";
            profile=settings.KeyboardProfile switch
            {
                "WordPerfect51"=>KeyboardProfile.WordPerfect51,
                "WordStar40"=>KeyboardProfile.WordStar40,
                _=>KeyboardProfile.Word
            };
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
                CompiledMacrosEnabled=compiledMacrosEnabled,
                LastMacroLanguage=lastMacroLanguage,
                KeepParagraphTogether=keepParagraphTogether,
                WindowLeft=savedWindowLeft,WindowTop=savedWindowTop,
                WindowWidth=savedWindowWidth,WindowHeight=savedWindowHeight,
                SignatureImagePath=signatureImagePath,
                ActiveStyleSet=activeStyleSet,
                KeyboardProfile=profile.ToString()
            }));
        }
        catch { }
    }

    void ApplySavedWindowPlacement()
    {
        if(double.IsNaN(savedWindowLeft)||double.IsNaN(savedWindowTop))return;
        Width=Math.Clamp(savedWindowWidth,700,SystemParameters.VirtualScreenWidth);
        Height=Math.Clamp(savedWindowHeight,500,SystemParameters.VirtualScreenHeight);
        Left=Math.Clamp(savedWindowLeft,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-100);
        Top=Math.Clamp(savedWindowTop,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-80);
        WindowStartupLocation=WindowStartupLocation.Manual;
    }

    void CaptureWindowPlacement()
    {
        var bounds=WindowState==WindowState.Normal
            ?new Rect(Left,Top,ActualWidth,ActualHeight)
            :RestoreBounds;
        if(bounds.Width>300&&bounds.Height>250)
        {
            savedWindowLeft=bounds.Left;savedWindowTop=bounds.Top;
            savedWindowWidth=bounds.Width;savedWindowHeight=bounds.Height;
        }
    }

    string GetPreferredAuthor()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\Common\UserInfo");
            var wordName=key?.GetValue("UserName")?.ToString()?.Trim();
            if(!string.IsNullOrWhiteSpace(wordName))return wordName;
        }
        catch{}
        try
        {
            var display=Environment.UserName?.Trim();
            return string.IsNullOrWhiteSpace(display)?"Unknown author":display;
        }
        catch{return "Unknown author";}
    }

    void EnsureDocumentPropertyDefaults()
    {
        if(string.IsNullOrWhiteSpace(documentTitle))
            documentTitle=currentFile==null?"Untitled":Path.GetFileNameWithoutExtension(currentFile);
        if(string.IsNullOrWhiteSpace(documentAuthor))
            documentAuthor=GetPreferredAuthor();
        if(string.IsNullOrWhiteSpace(documentSubject))
            documentSubject="CtrlZed document";
        if(string.IsNullOrWhiteSpace(documentVersion))
            documentVersion="1.0";
    }

    UserProfileService.PortablePreferences CapturePortablePreferences()
        =>new()
        {
            ZoomPercent=zoomPercent,
            ExtendedMacrosEnabled=extendedMacrosEnabled,
            CompiledMacrosEnabled=compiledMacrosEnabled,
            LastMacroLanguage=lastMacroLanguage,
            KeepParagraphTogether=keepParagraphTogether
        };

    void ApplyPortablePreferences(UserProfileService.PortablePreferences? p)
    {
        if(p==null)return;
        zoomPercent=Math.Clamp(p.ZoomPercent,50,150);
        extendedMacrosEnabled=p.ExtendedMacrosEnabled;
        compiledMacrosEnabled=p.CompiledMacrosEnabled;
        lastMacroLanguage=p.LastMacroLanguage is ".czm" or ".vb" or ".cs"?p.LastMacroLanguage:".vb";
        keepParagraphTogether=p.KeepParagraphTogether;
        ApplyZoom();
        SaveAppSettings();
    }

    void ExportProfile_Click(Window owner)
    {
        var d=new SaveFileDialog
        {
            Title="Export CtrlZed Profile",
            Filter="CtrlZed profile (*.czprofile)|*.czprofile|ZIP archive (*.zip)|*.zip",
            DefaultExt=".czprofile",
            AddExtension=true,
            FileName="CtrlZed-Profile.czprofile"
        };
        if(d.ShowDialog(owner)!=true)return;
        try
        {
            UserProfileService.Export(d.FileName,userFields,CapturePortablePreferences(),styleSets,MacroDirectory());
            Status.Text="CtrlZed profile exported.";
            MessageBox.Show(
                "Profile exported. User fields, central macros and portable preferences are included. Secret values are intentionally excluded.",
                "Profile exported",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Profile export failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void ImportProfile_Click(Window owner)
    {
        var d=new OpenFileDialog
        {
            Title="Import CtrlZed Profile",
            Filter="CtrlZed profile (*.czprofile;*.zip)|*.czprofile;*.zip|All files (*.*)|*.*"
        };
        if(d.ShowDialog(owner)!=true)return;

        var choice=MessageBox.Show(
            "Choose how to import this profile.\n\nYes = MERGE: keep existing user fields/macros where names clash.\nNo = REPLACE: replace portable user fields/macros with the imported profile.\nCancel = do nothing.\n\nSecret values are never imported.",
            "Import CtrlZed Profile",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if(choice==MessageBoxResult.Cancel)return;
        var replace=choice==MessageBoxResult.No;

        try
        {
            var result=UserProfileService.Import(d.FileName,replace,userFields,styleSets,MacroDirectory());
            PersistentFieldStore.SaveUser(userFields);
            StyleSetStore.Save(styleSets);
            RefreshStyleSetBox();
            ApplyPortablePreferences(result.Preferences);
            RefreshMacroMenus();
            Status.Text="CtrlZed profile imported.";
            MessageBox.Show(
                $"Profile imported.\n\nUser fields: {result.FieldsImported}\nMacros imported: {result.MacrosImported}\nStyle sets: {result.StyleSetsImported}\nMode: {(replace?"Replace":"Merge")}\n\nSecrets were not imported.",
                "Profile imported",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Profile import failed",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void Settings_Click(object s,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="CtrlZed Settings",Owner=this,Width=620,Height=600,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Document flow",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var keep=new CheckBox
        {
            Content="Keep paragraph together when it occupies no more than half a page",
            IsChecked=keepParagraphTogether,Margin=new Thickness(0,4,0,4)
        };
        panel.Children.Add(keep);
        panel.Children.Add(new TextBlock
        {
            Text="Keep-together is honoured by paginated preview/printing without changing recorded picture sizes or live editor paragraph positions. True paged editing remains on the Coming Soon list.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(20,0,0,18)
        });
        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock{Text="Fonts",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        var fontFinder=new Button{Content="AI Font Finder…",Width=120,HorizontalAlignment=HorizontalAlignment.Left};
        fontFinder.Click+=(_,__)=>ShowComingSoon(w,"AI Font Finder");
        panel.Children.Add(fontFinder);
        panel.Children.Add(new TextBlock
        {
            Text="Natural-language font discovery, licensed downloads/install and experimental generated fonts are planned under Coming Soon.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,12)
        });
        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock{Text="Signature",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        var signatureButton=new Button
        {
            Content=string.IsNullOrWhiteSpace(signatureImagePath)?"Configure Signature…":"Replace Signature…",
            Width=130,HorizontalAlignment=HorizontalAlignment.Left
        };
        signatureButton.Click+=(_,__)=>ConfigureSignature(w);
        panel.Children.Add(signatureButton);
        panel.Children.Add(new TextBlock
        {
            Text=string.IsNullOrWhiteSpace(signatureImagePath)
                ?"No signature image configured."
                :"Configured: "+signatureImagePath,
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,12)
        });

        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock{Text="Portable workspace",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,8)});
        var profileButtons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left};
        var exportProfile=new Button{Content="Export Profile…",Width=110,Margin=new Thickness(0,0,8,0)};
        var importProfile=new Button{Content="Import Profile…",Width=110};
        exportProfile.Click+=(_,__)=>ExportProfile_Click(w);
        importProfile.Click+=(_,__)=>ImportProfile_Click(w);
        profileButtons.Children.Add(exportProfile);profileButtons.Children.Add(importProfile);
        panel.Children.Add(profileButtons);
        panel.Children.Add(new TextBlock
        {
            Text="Profiles contain user fields, central macros and portable preferences. API keys/secrets are deliberately excluded.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,12)
        });
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var apply=new Button{Content="Apply",Width=80,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        apply.Click+=(_,__)=>
        {
            keepParagraphTogether=keep.IsChecked==true;
            SaveAppSettings();
            Status.Text="Settings updated. Keep-together applies to page preview/print; true paged editing remains on the Coming Soon list.";
        };
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(apply);buttons.Children.Add(close);panel.Children.Add(buttons);
        w.Content=panel;w.ShowDialog();
    }

    void ComingSoon_Click(object s,RoutedEventArgs e)=>ShowComingSoon(this,null);

    void ShowComingSoon(Window owner,string? focus)
    {
        var w=new Window
        {
            Title=focus==null?"CtrlZed — Coming Soon":$"CtrlZed — Coming Soon: {focus}",
            Owner=owner,Width=650,Height=470,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(22)};
        panel.Children.Add(new TextBlock{Text="Coming Soon",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(new TextBlock
        {
            Text="Planned work that is useful enough to record, but not yet mature enough to pretend it is finished.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)
        });
        var items=new[]
        {
            "True paged/WYSIWYG editing canvas with discrete page containers and margins.",
            "AI Font Finder: natural-language search across properly licensed free-font catalogues such as Google Fonts.",
            "Font download and Windows installation workflow with licence information before installation.",
            "Requests such as ‘Space: 1999-like’, ‘a big bad font’ or ‘Arial Bold’.",
            "Experimental AI-generated fonts, including decorative capital alphabets such as animal-shaped letters.",
            "Automatic bitmap-to-vector signature tracing and SVG-native signature handling.",
            "Further DOCX fidelity, comments/track changes and more advanced publishing layout."
        };
        foreach(var item in items)
            panel.Children.Add(new TextBlock{Text="• "+item,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,4,0,4)});
        var close=new Button{Content="Close",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        close.Click+=(_,__)=>w.Close();panel.Children.Add(close);
        var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        w.Content=scroll;w.ShowDialog();
    }

    void OpenGitHub_Click(object s,RoutedEventArgs e)
        =>Process.Start(new ProcessStartInfo("https://github.com/martynwalker/CtrlZed"){UseShellExecute=true});

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
            if(fieldValuesVisible)SetFieldValuesVisible(false);
                suppressDirty=true;
            var ext=Path.GetExtension(path).ToLowerInvariant();
            headerText=footerText="";
            documentTitle=documentAuthor=documentSubject=documentKeywords="";
            documentVersion="1.0";
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
            EnsureDocumentPropertyDefaults();
            RefreshHeaderFooter();UpdateTitle();UpdateDocumentStats();
            Status.Text="Opened "+currentFile;AddRecentFile(currentFile);TryDeleteRecovery();RefreshMacroMenus();
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
        EnsureDocumentPropertyDefaults();
        var w=new Window{Title="Document Properties",Owner=this,Width=540,Height=410,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<7;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var title=new TextBox{Text=documentTitle,Margin=new Thickness(0,3,0,8)};
        var author=new TextBox{Text=documentAuthor,Margin=new Thickness(0,3,0,8)};
        var subject=new TextBox{Text=documentSubject,Margin=new Thickness(0,3,0,8)};
        var keywords=new TextBox{Text=documentKeywords,Margin=new Thickness(0,3,0,8)};
        var version=new TextBox{Text=documentVersion,Margin=new Thickness(0,3,0,8)};
        AddRow(g,0,"Title",title);AddRow(g,1,"Author",author);AddRow(g,2,"Subject",subject);AddRow(g,3,"Keywords",keywords);AddRow(g,4,"Version",version);
        var info=new TextBlock{Text="Empty values are prefilled from the document, Windows account and Office user profile where available. Stored in .czd; standard fields map to DOCX core properties.",Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)};
        Grid.SetRow(info,5);Grid.SetColumnSpan(info,2);g.Children.Add(info);
        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        Grid.SetRow(ok,6);Grid.SetColumnSpan(ok,2);g.Children.Add(ok);
        ok.Click+=(_,__)=>
        {
            documentTitle=title.Text;documentAuthor=author.Text;documentSubject=subject.Text;documentKeywords=keywords.Text;documentVersion=version.Text;
            EnsureDocumentPropertyDefaults();
            MarkDirty("Document properties updated.");RefreshHeaderFooter();w.DialogResult=true;
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

    static MacroMenuMetadata? GetMacroMenuMetadata(string source,string extension)
    {
        extension=(extension??"").ToLowerInvariant();

        string? name=null;
        string path="";
        int position=0;
        var pathPositions=new List<int>();

        if(extension==".czm")
        {
            foreach(var raw in source.Replace("\r\n","\n").Split('\n'))
            {
                var line=raw.Trim();
                if(line.StartsWith("#"))line=line[1..].Trim();
                var eq=line.IndexOf('=');
                if(eq<1)continue;
                var key=line[..eq].Trim().ToUpperInvariant();
                var value=line[(eq+1)..].Trim();
                if(value.Length>=2 && value[0]=='"' && value[^1]=='"')value=value[1..^1];

                switch(key)
                {
                    case "MENU_NAME":
                        if(!string.IsNullOrWhiteSpace(value))name=value;
                        break;
                    case "MENU_POSITION":
                    case "MENU_OPTION":
                        if(position==0 && int.TryParse(value,out var pos) && pos>0)position=pos;
                        break;
                    case "MENU_PATH":
                    case "MENU_PARENT":
                        path=value;
                        break;
                    case "MENU_PATH_POSITIONS":
                        pathPositions=value.Split('/',StringSplitOptions.RemoveEmptyEntries)
                            .Select(v=>int.TryParse(v.Trim(),out var n)&&n>0?n:0).ToList();
                        break;
                }
            }
        }
        else
        {
            name=GetMacroFriendlyName(source,extension);
            string? StringConst(string constName)
            {
                var pattern=extension==".vb"
                    ?$@"(?im)^\s*(?:Public\s+|Friend\s+|Private\s+)?Const\s+{System.Text.RegularExpressions.Regex.Escape(constName)}\s+As\s+String\s*=\s*""([^""]*)"""
                    :$@"(?im)^\s*(?:public\s+|internal\s+|private\s+)?(?:static\s+)?const\s+string\s+{System.Text.RegularExpressions.Regex.Escape(constName)}\s*=\s*""([^""]*)""\s*;";
                var match=System.Text.RegularExpressions.Regex.Match(source,pattern);
                return match.Success?match.Groups[1].Value.Trim():null;
            }
            int IntConst(params string[] names)
            {
                foreach(var constName in names)
                {
                    var pattern=extension==".vb"
                        ?$@"(?im)^\s*(?:Public\s+|Friend\s+|Private\s+)?Const\s+{System.Text.RegularExpressions.Regex.Escape(constName)}\s+As\s+Integer\s*=\s*(\d+)"
                        :$@"(?im)^\s*(?:public\s+|internal\s+|private\s+)?(?:static\s+)?const\s+int\s+{System.Text.RegularExpressions.Regex.Escape(constName)}\s*=\s*(\d+)\s*;";
                    var match=System.Text.RegularExpressions.Regex.Match(source,pattern);
                    if(match.Success && int.TryParse(match.Groups[1].Value,out var n) && n>0)return n;
                }
                return 0;
            }

            path=StringConst("MENU_PATH")??StringConst("MENU_PARENT")??"";
            position=IntConst("MENU_POSITION","MENU_OPTION");
            var rawPositions=StringConst("MENU_PATH_POSITIONS");
            if(!string.IsNullOrWhiteSpace(rawPositions))
                pathPositions=rawPositions.Split('/',StringSplitOptions.RemoveEmptyEntries)
                    .Select(v=>int.TryParse(v.Trim(),out var n)&&n>0?n:0).ToList();
        }

        if(string.IsNullOrWhiteSpace(name) || position<=0)return null;
        path=(path??"").Trim().Trim('/');
        if(string.Equals(path,"right-click",StringComparison.OrdinalIgnoreCase))path="";
        return new MacroMenuMetadata(name,path,position,pathPositions);
    }

    static int MenuOrder(MenuItem item)
        =>item.Tag switch
        {
            int n=>n,
            MacroMenuTag tag=>tag.Position,
            _=>int.MaxValue
        };

    static void InsertOrdered(ItemCollection items,MenuItem item,int position)
    {
        if(item.Tag is not MacroMenuTag)item.Tag=position;
        var caption=item.Header?.ToString()??"";
        var index=0;
        while(index<items.Count)
        {
            if(items[index] is not MenuItem existing){index++;continue;}
            var existingPos=MenuOrder(existing);
            var comparison=existingPos.CompareTo(position);
            if(comparison>0)break;
            if(comparison==0 && string.Compare(existing.Header?.ToString(),caption,StringComparison.CurrentCultureIgnoreCase)>0)break;
            index++;
        }
        items.Insert(index,item);
    }

    MenuItem CreateMacroCommand((string Name,string Source) macro,MacroMenuMetadata meta)
    {
        var item=new MenuItem{Header=meta.Name,Tag=new MacroMenuTag("Command",meta.Position)};
        item.Click+=async (_,__)=>
        {
            try
            {
                await RunMacroSourceAsync(macro.Name,macro.Source);
                Status.Text="Macro: "+meta.Name;
            }
            catch(Exception ex)
            {
                ShowMacroError(macro.Name,macro.Source,ex);
            }
        };
        return item;
    }

    void AddMacroSubmenu(ContextMenu menu)
    {
        var entries=GetMacroSources()
            .Select(m=>(Macro:m,Meta:GetMacroMenuMetadata(m.Source,Path.GetExtension(m.Name))))
            .Where(x=>x.Meta!=null && string.IsNullOrWhiteSpace(x.Meta.Path))
            .GroupBy(x=>x.Meta!.Name.Trim(),StringComparer.CurrentCultureIgnoreCase)
            .Select(g=>g.OrderBy(x=>x.Macro.Name,StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x=>x.Meta!.Position)
            .ThenBy(x=>x.Meta!.Name,StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if(entries.Count==0)return;

        var macros=new MenuItem{Header="Macros"};
        foreach(var entry in entries)
            InsertOrdered(macros.Items,CreateMacroCommand(entry.Macro,entry.Meta!),entry.Meta!.Position);
        menu.Items.Add(macros);
    }

    static string NormalisedMenuHeader(object? header)
        =>(header?.ToString()??"").Replace("_","").Trim();

    static void RemoveDynamicMacroItems(ItemCollection items)
    {
        for(int i=items.Count-1;i>=0;i--)
        {
            if(items[i] is not MenuItem item)continue;
            RemoveDynamicMacroItems(item.Items);
            if(item.Tag is MacroMenuTag tag && tag.Kind is "Command" or "Path" or "Root")
                items.RemoveAt(i);
        }
    }

    void RefreshMacroMenus()
    {
        if(MainMenu==null)return;

        RemoveDynamicMacroItems(MainMenu.Items);

        var entries=GetMacroSources()
            .Select(m=>(Macro:m,Meta:GetMacroMenuMetadata(m.Source,Path.GetExtension(m.Name))))
            .Where(x=>x.Meta!=null && !string.IsNullOrWhiteSpace(x.Meta.Path))
            .GroupBy(x=>$"{x.Meta!.Path.Trim().ToLowerInvariant()}|{x.Meta.Name.Trim().ToLowerInvariant()}",StringComparer.OrdinalIgnoreCase)
            .Select(g=>g.OrderBy(x=>x.Macro.Name,StringComparer.OrdinalIgnoreCase).First())
            .ToList();
        if(entries.Count==0)return;

        foreach(var entry in entries
            .OrderBy(x=>x.Meta!.PathPositions.Count>0?x.Meta.PathPositions[0]:int.MaxValue)
            .ThenBy(x=>x.Meta!.Path,StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x=>x.Meta!.Position)
            .ThenBy(x=>x.Meta!.Name,StringComparer.CurrentCultureIgnoreCase))
        {
            var meta=entry.Meta!;
            var segments=meta.Path.Split('/',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(segments.Length==0)continue;

            MenuItem? current=MainMenu.Items.OfType<MenuItem>()
                .FirstOrDefault(mi=>string.Equals(NormalisedMenuHeader(mi.Header),segments[0],StringComparison.CurrentCultureIgnoreCase));

            if(current==null)
            {
                var rootPos=meta.PathPositions.Count>0 && meta.PathPositions[0]>0?meta.PathPositions[0]:MainMenu.Items.Count+1;
                current=new MenuItem{Header=segments[0],Tag=new MacroMenuTag("Root",rootPos)};
                MainMenu.Items.Insert(Math.Clamp(rootPos-1,0,MainMenu.Items.Count),current);
            }

            for(int level=1;level<segments.Length;level++)
            {
                var segment=segments[level];
                var child=current.Items.OfType<MenuItem>()
                    .FirstOrDefault(mi=>string.Equals(NormalisedMenuHeader(mi.Header),segment,StringComparison.CurrentCultureIgnoreCase));
                if(child==null)
                {
                    var childPos=meta.PathPositions.Count>level && meta.PathPositions[level]>0
                        ?meta.PathPositions[level]
                        :current.Items.Count+1;
                    child=new MenuItem{Header=segment,Tag=new MacroMenuTag("Path",childPos)};
                    InsertOrdered(current.Items,child,childPos);
                }
                current=child;
            }

            InsertOrdered(current.Items,CreateMacroCommand(entry.Macro,meta),meta.Position);
        }
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

    static void AddRow(Grid g,int row,string label,FrameworkElement control)
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

        var contentWidth=Math.Max(120,pageWidth-pageMargins.Left-pageMargins.Right);
        var contentHeight=Math.Max(180,pageHeight-pageMargins.Top-pageMargins.Bottom);
        Editor.Document.PageWidth=contentWidth;
        Editor.Document.PageHeight=double.NaN;
        Editor.Document.PagePadding=new Thickness(0);
        Editor.Document.ColumnWidth=double.PositiveInfinity;

        if(PageGuides!=null)
        {
            PageGuides.PageContentHeight=contentHeight;
            PageGuides.PageGap=Math.Max(24,pageMargins.Top+pageMargins.Bottom);
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

    void ApplyStyleToRange(TextRange range,StyleSetStore.TextStyle spec)
    {
        range.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily(spec.FontFamily));
        range.ApplyPropertyValue(TextElement.FontSizeProperty,spec.SizePt*96/72);
        range.ApplyPropertyValue(TextElement.FontStyleProperty,spec.Italic?FontStyles.Italic:FontStyles.Normal);
        range.ApplyPropertyValue(TextElement.FontWeightProperty,spec.Bold?FontWeights.Bold:FontWeights.Normal);
        range.ApplyPropertyValue(TextElement.ForegroundProperty,BrushFromHex(spec.Colour));
        range.ApplyPropertyValue(Inline.TextDecorationsProperty,spec.Underline?TextDecorations.Underline:null);
        range.ApplyPropertyValue(Paragraph.TextAlignmentProperty,spec.Alignment.ToLowerInvariant() switch
        {
            "center" or "centre"=>TextAlignment.Center,
            "right"=>TextAlignment.Right,
            "justify"=>TextAlignment.Justify,
            _=>TextAlignment.Left
        });
        range.ApplyPropertyValue(Paragraph.MarginProperty,new Thickness(
            spec.MarginLeft,spec.MarginTop,spec.MarginRight,spec.MarginBottom));
    }

    StyleSetStore.TextStyle? ResolveStyle(string style)
    {
        if(!styleSets.TryGetValue(activeStyleSet,out var set))
        {
            set=StyleSetStore.Defaults()["Default"];
            activeStyleSet="Default";
        }
        if(set.Styles.TryGetValue(style,out var spec))return spec;
        return StyleSetStore.Defaults()["Default"].Styles.TryGetValue(style,out spec)?spec:null;
    }

    void ApplyParagraphStyle(string style)
    {
        if(Editor==null)return;
        var spec=ResolveStyle(style);
        if(spec==null)return;
        ApplyStyleToRange(Editor.Selection,spec);
        MarkDirty($"Style: {style} ({activeStyleSet})");
        Editor.Focus();
    }

    static Brush BrushFromHex(string value)
    {
        try{return (Brush)new BrushConverter().ConvertFromString(value)!;}
        catch{return Brushes.Black;}
    }

    string? PickColour(Window owner,string initial)
    {
        var start=Colors.Black;
        try
        {
            if(ColorConverter.ConvertFromString(initial) is Color parsed)start=parsed;
        }
        catch { }

        var w=new Window
        {
            Title="Choose Colour",Owner=owner,Width=420,Height=310,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(16)};
        for(int i=0;i<6;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(70)});
        root.ColumnDefinitions.Add(new ColumnDefinition());

        Slider MakeSlider(byte value)=>new(){Minimum=0,Maximum=255,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,4,0,8)};
        var red=MakeSlider(start.R);var green=MakeSlider(start.G);var blue=MakeSlider(start.B);
        var preview=new Border{Height=46,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1),Margin=new Thickness(0,5,0,8)};
        var hex=new TextBox{IsReadOnly=true,Margin=new Thickness(0,4,0,8)};
        AddRow(root,0,"Red",red);AddRow(root,1,"Green",green);AddRow(root,2,"Blue",blue);AddRow(root,3,"Hex",hex);
        Grid.SetRow(preview,4);Grid.SetColumnSpan(preview,2);root.Children.Add(preview);

        string CurrentHex()=>"#"+((byte)red.Value).ToString("X2")+((byte)green.Value).ToString("X2")+((byte)blue.Value).ToString("X2");
        void Refresh()
        {
            var value=CurrentHex();
            hex.Text=value;
            preview.Background=BrushFromHex(value);
        }
        red.ValueChanged+=(_,__)=>Refresh();green.ValueChanged+=(_,__)=>Refresh();blue.ValueChanged+=(_,__)=>Refresh();

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var ok=new Button{Content="OK",Width=80,Margin=new Thickness(4),IsDefault=true};
        var cancel=new Button{Content="Cancel",Width=80,Margin=new Thickness(4),IsCancel=true};
        buttons.Children.Add(ok);buttons.Children.Add(cancel);
        Grid.SetRow(buttons,5);Grid.SetColumnSpan(buttons,2);root.Children.Add(buttons);
        ok.Click+=(_,__)=>w.DialogResult=true;
        Refresh();w.Content=root;
        return w.ShowDialog()==true?CurrentHex():null;
    }

    void RefreshStyleSetBox()
    {
        if(StyleSetBox==null)return;
        StyleSetBox.ItemsSource=styleSets.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        StyleSetBox.SelectedItem=styleSets.ContainsKey(activeStyleSet)?activeStyleSet:"Default";
    }

    void StyleSetBox_SelectionChanged(object s,SelectionChangedEventArgs e)
    {
        if(!IsLoaded || StyleSetBox.SelectedItem is not string name || !styleSets.ContainsKey(name))return;
        activeStyleSet=name;
        SaveAppSettings();
        Status.Text="Style set: "+name;
    }

    void Styles_Click(object s,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="CtrlZed Styles",Owner=this,Width=760,Height=560,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var styles=new[]{"Normal","Heading 1","Heading 2","Heading 3","Quote","Caption"};
        var selectorPanel=new StackPanel{Margin=new Thickness(0,0,12,0)};
        selectorPanel.Children.Add(new TextBlock{Text="Style to edit",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        var stylePicker=new ComboBox{ItemsSource=styles,Margin=new Thickness(0,0,0,8)};
        selectorPanel.Children.Add(stylePicker);
        selectorPanel.Children.Add(new TextBlock{Text="Choose the paragraph style here, then edit its settings on the right.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
        Grid.SetColumn(selectorPanel,0);Grid.SetRow(selectorPanel,0);root.Children.Add(selectorPanel);

        var form=new Grid();
        for(int i=0;i<11;i++)form.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        form.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});
        form.ColumnDefinitions.Add(new ColumnDefinition());

        var setName=new TextBox{Text=activeStyleSet,Margin=new Thickness(0,3,0,8)};
        var font=new ComboBox{ItemsSource=Fonts.SystemFontFamilies.OrderBy(x=>x.Source).ToList(),DisplayMemberPath="Source",Margin=new Thickness(0,3,0,8)};
        var size=new TextBox{Margin=new Thickness(0,3,0,8)};
        var colourPanel=new DockPanel{Margin=new Thickness(0,3,0,8)};
        var pickColour=new Button{Content="Pick…",Width=70,Margin=new Thickness(6,0,0,0)};
        DockPanel.SetDock(pickColour,Dock.Right);colourPanel.Children.Add(pickColour);
        var colour=new ComboBox{ItemsSource=new[]{"#000000","#333333","#666666","#808080","#FFFFFF","#8B0000","#CC0000","#006400","#00008B","#1E4D7B","#4B0082"},IsEditable=true};
        colourPanel.Children.Add(colour);
        var bold=new CheckBox{Content="Bold",Margin=new Thickness(0,3,0,5)};
        var italic=new CheckBox{Content="Italic",Margin=new Thickness(0,3,0,5)};
        var underline=new CheckBox{Content="Underline",Margin=new Thickness(0,3,0,5)};
        var alignment=new ComboBox{ItemsSource=new[]{"Left","Centre","Right","Justify"},Margin=new Thickness(0,3,0,8)};
        var margins=new TextBox{Margin=new Thickness(0,3,0,8),ToolTip="Left,Top,Right,Bottom"};

        AddRow(form,0,"Style set",setName);
        AddRow(form,1,"Font",font);
        AddRow(form,2,"Size (pt)",size);
        AddRow(form,3,"Colour",colourPanel);
        AddRow(form,4,"Bold",bold);
        AddRow(form,5,"Italic",italic);
        AddRow(form,6,"Underline",underline);
        AddRow(form,7,"Alignment",alignment);
        AddRow(form,8,"Margins",margins);

        var info=new TextBlock
        {
            Text="Select one of the six paragraph styles, edit its appearance, then save the set under a name. The active set is selectable from the toolbar.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,8)
        };
        Grid.SetRow(info,9);Grid.SetColumnSpan(info,2);form.Children.Add(info);
        Grid.SetColumn(form,1);Grid.SetRow(form,0);root.Children.Add(form);

        StyleSetStore.StyleSet working;
        if(styleSets.TryGetValue(activeStyleSet,out var current))
        {
            working=new StyleSetStore.StyleSet
            {
                Name=current.Name,
                Styles=current.Styles.ToDictionary(
                    x=>x.Key,
                    x=>new StyleSetStore.TextStyle
                    {
                        FontFamily=x.Value.FontFamily,SizePt=x.Value.SizePt,Colour=x.Value.Colour,
                        Bold=x.Value.Bold,Italic=x.Value.Italic,Underline=x.Value.Underline,
                        Alignment=x.Value.Alignment,MarginLeft=x.Value.MarginLeft,MarginTop=x.Value.MarginTop,
                        MarginRight=x.Value.MarginRight,MarginBottom=x.Value.MarginBottom
                    },
                    StringComparer.OrdinalIgnoreCase)
            };
        }
        else working=StyleSetStore.Defaults()["Default"];

        string? selectedStyle=null;

        void LoadStyle(string style)
        {
            if(!working.Styles.TryGetValue(style,out var spec))return;
            selectedStyle=style;
            font.SelectedItem=Fonts.SystemFontFamilies.FirstOrDefault(f=>string.Equals(f.Source,spec.FontFamily,StringComparison.OrdinalIgnoreCase));
            size.Text=spec.SizePt.ToString("0.##");
            colour.Text=spec.Colour;
            bold.IsChecked=spec.Bold;italic.IsChecked=spec.Italic;underline.IsChecked=spec.Underline;
            alignment.SelectedItem=spec.Alignment=="Center"?"Centre":spec.Alignment;
            margins.Text=$"{spec.MarginLeft:0.##},{spec.MarginTop:0.##},{spec.MarginRight:0.##},{spec.MarginBottom:0.##}";
        }

        void SaveCurrentStyle()
        {
            if(selectedStyle==null)return;
            if(!double.TryParse(size.Text,out var pt) || pt<=0)throw new InvalidOperationException("Enter a valid font size.");
            var parts=margins.Text.Split(',');
            if(parts.Length!=4 || !parts.All(p=>double.TryParse(p.Trim(),out _)))
                throw new InvalidOperationException("Margins must be four numbers: Left,Top,Right,Bottom.");
            var vals=parts.Select(p=>double.Parse(p.Trim())).ToArray();
            working.Styles[selectedStyle]=new StyleSetStore.TextStyle
            {
                FontFamily=(font.SelectedItem as FontFamily)?.Source??font.Text,
                SizePt=pt,Colour=string.IsNullOrWhiteSpace(colour.Text)?"#000000":colour.Text.Trim(),
                Bold=bold.IsChecked==true,Italic=italic.IsChecked==true,Underline=underline.IsChecked==true,
                Alignment=alignment.SelectedItem?.ToString()??"Left",
                MarginLeft=vals[0],MarginTop=vals[1],MarginRight=vals[2],MarginBottom=vals[3]
            };
        }

        stylePicker.SelectionChanged+=(_,__)=>
        {
            try{SaveCurrentStyle();}catch{}
            if(stylePicker.SelectedItem is string style)LoadStyle(style);
        };
        pickColour.Click+=(_,__)=>
        {
            var picked=PickColour(w,colour.Text);
            if(!string.IsNullOrWhiteSpace(picked))colour.Text=picked;
        };

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var saveSet=new Button{Content="Save Set",Width=90,Margin=new Thickness(4)};
        var deleteSet=new Button{Content="Delete Set",Width=90,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        buttons.Children.Add(deleteSet);buttons.Children.Add(saveSet);buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);Grid.SetColumnSpan(buttons,2);Grid.SetRow(buttons,1);root.Children.Add(buttons);

        saveSet.Click+=(_,__)=>
        {
            try
            {
                SaveCurrentStyle();
                var name=setName.Text.Trim();
                if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("Enter a style-set name.");
                working.Name=name;
                styleSets[name]=working;
                activeStyleSet=name;
                StyleSetStore.Save(styleSets);
                RefreshStyleSetBox();
                SaveAppSettings();
                Status.Text="Style set saved: "+name;
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Save style set",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };

        deleteSet.Click+=(_,__)=>
        {
            var name=setName.Text.Trim();
            if(string.Equals(name,"Default",StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("The Default style set cannot be deleted.","Styles",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            if(styleSets.Remove(name))
            {
                activeStyleSet="Default";
                StyleSetStore.Save(styleSets);
                RefreshStyleSetBox();
                SaveAppSettings();
                w.Close();
            }
        };
        close.Click+=(_,__)=>w.Close();

        stylePicker.SelectedIndex=0;
        w.Content=root;w.ShowDialog();
    }

    void ReformatDocument_Click(object s,RoutedEventArgs e)
    {
        var styles=new[]{"Normal","Heading 1","Heading 2","Heading 3","Quote","Caption"};
        var w=new Window
        {
            Title="Reformat Document",Owner=this,Width=440,Height=235,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new StackPanel{Margin=new Thickness(16)};
        root.Children.Add(new TextBlock
        {
            Text=$"Apply one style from the active style set \"{activeStyleSet}\" to the whole document.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        var picker=new ComboBox{ItemsSource=styles,SelectedItem="Normal",Margin=new Thickness(0,0,0,10)};
        root.Children.Add(picker);
        root.Children.Add(new TextBlock
        {
            Text="This changes paragraph formatting throughout the document. Undo remains available immediately afterwards.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,10)
        });
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var apply=new Button{Content="Reformat",Width=90,Margin=new Thickness(4),IsDefault=true};
        var cancel=new Button{Content="Cancel",Width=80,Margin=new Thickness(4),IsCancel=true};
        buttons.Children.Add(apply);buttons.Children.Add(cancel);root.Children.Add(buttons);
        apply.Click+=(_,__)=>
        {
            if(picker.SelectedItem is not string style)return;
            var spec=ResolveStyle(style);
            if(spec==null)return;
            foreach(var paragraph in EnumerateDocumentParagraphs())
                ApplyStyleToRange(new TextRange(paragraph.ContentStart,paragraph.ContentEnd),spec);
            MarkDirty($"Document reformatted: {style} ({activeStyleSet})");
            w.DialogResult=true;
            Editor.Focus();
        };
        w.Content=root;w.ShowDialog();
    }

    void KeyboardShortcuts_Click(object s,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="CtrlZed Keyboard Shortcuts",Owner=this,Width=650,Height=520,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new DockPanel{Margin=new Thickness(14)};
        var profileName=profile switch
        {
            KeyboardProfile.WordPerfect51=>"WordPerfect 5.1",
            KeyboardProfile.WordStar40=>"WordStar 4.0",
            _=>"Microsoft Word"
        };
        var heading=new TextBlock{Text="Selected profile: "+profileName,FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var search=new TextBox{Margin=new Thickness(0,0,0,8),ToolTip="Filter shortcuts"};
        DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
        var list=new ListBox();root.Children.Add(list);

        var items=profile switch
        {
            KeyboardProfile.WordPerfect51=>new[]
            {
                "F3 — About CtrlZed",
                "F7 — Exit",
                "Shift+F7 — Print",
                "Shift+F6 — Centre paragraph"
            },
            KeyboardProfile.WordStar40=>new[]
            {
                "Ctrl+K, S — Save",
                "Ctrl+K, X — Save and Exit",
                "Ctrl+E — Move up one line",
                "Ctrl+X — Move down one line",
                "Ctrl+S — Move left one character",
                "Ctrl+D — Move right one character"
            },
            _=>new[]
            {
                "Ctrl+N — New document",
                "Ctrl+O — Open",
                "Ctrl+S — Save",
                "Ctrl+Shift+S — Save As",
                "Ctrl+H — Find / Replace",
                "Ctrl+Enter — Page Break",
                "Ctrl+B — Bold",
                "Ctrl+I — Italic",
                "Ctrl+U — Underline",
                "Ctrl+Z — Undo",
                "Ctrl+Y — Redo",
                "Ctrl+X — Cut",
                "Ctrl+C — Copy",
                "Ctrl+V — Paste",
                "F9 — Reformat Document"
            }
        };

        void Refresh()
        {
            var q=search.Text.Trim();
            list.ItemsSource=items.Where(x=>q.Length==0||x.Contains(q,StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
        search.TextChanged+=(_,__)=>Refresh();
        Refresh();
        w.Content=root;w.ShowDialog();
    }

    void ConfigureSignature(Window owner)
    {
        var d=new OpenFileDialog{Title="Choose signature image",Filter="Signature images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"};
        if(d.ShowDialog(owner)!=true)return;
        try
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            var ext=Path.GetExtension(d.FileName).ToLowerInvariant();
            var target=Path.Combine(root,"signature"+ext);
            File.Copy(d.FileName,target,true);
            signatureImagePath=target;
            SaveAppSettings();
            Status.Text="Signature image configured.";
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Signature setup failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void Signature_Click(object s,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(signatureImagePath)||!File.Exists(signatureImagePath))
        {
            var choice=MessageBox.Show("No signature is configured. Configure one now?","Insert Signature",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(choice!=MessageBoxResult.Yes)return;
            ConfigureSignature(this);
            if(string.IsNullOrWhiteSpace(signatureImagePath)||!File.Exists(signatureImagePath))return;
        }
        try
        {
            var b=new BitmapImage();
            b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(signatureImagePath);b.EndInit();
            var img=new Image{Source=b,Width=180,Stretch=Stretch.Uniform,Margin=new Thickness(4)};
            var ui=new InlineUIContainer(img,Editor.CaretPosition);
            WirePictureContainer(ui,img);
            Editor.CaretPosition=ui.ElementEnd;
            MarkDirty("Signature inserted.");
            Editor.Focus();
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Insert signature failed",MessageBoxButton.OK,MessageBoxImage.Error);}
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

    string MacroDirectory()
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");
        Directory.CreateDirectory(dir);
        return dir;
    }

    void OpenMacroFolder_Click(object s,RoutedEventArgs e)
    {
        var dir=MacroDirectory();
        try
        {
            Process.Start(new ProcessStartInfo{FileName=dir,UseShellExecute=true});
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Open Macro Folder",MessageBoxButton.OK,MessageBoxImage.Error);
        }
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

        var folderInfo=new TextBlock
        {
            Text=dir,FontSize=10.5,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,
            ToolTip=dir,Margin=new Thickness(0,6,0,0)
        };
        DockPanel.SetDock(folderInfo,Dock.Bottom);
        left.Children.Add(folderInfo);

        var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};
        var language=new ComboBox{Width=92,Margin=new Thickness(0,0,5,5),ItemsSource=new[]{"Safe","VB.NET","C#"}};
        language.SelectedIndex=lastMacroLanguage==".czm"?0:lastMacroLanguage==".cs"?2:1;
        var create=new Button{Content="New",Width=58,Margin=new Thickness(0,0,5,5)};
        var ai=new Button{Content="Ask AI…",Margin=new Thickness(0,0,5,5)};
        var embed=new Button{Content="Embed",Margin=new Thickness(0,0,5,5)};
        var extract=new Button{Content="Extract",Margin=new Thickness(0,0,5,5)};
        var rename=new Button{Content="Rename",Margin=new Thickness(0,0,5,5)};
        var delete=new Button{Content="Delete",Margin=new Thickness(0,0,5,5)};
        var secrets=new Button{Content="Secrets…",Margin=new Thickness(0,0,5,5)};
        buttons.Children.Add(language);buttons.Children.Add(create);buttons.Children.Add(ai);buttons.Children.Add(embed);buttons.Children.Add(extract);buttons.Children.Add(rename);buttons.Children.Add(delete);buttons.Children.Add(secrets);
        language.SelectionChanged+=(_,__)=>
        {
            lastMacroLanguage=language.SelectedIndex==0?".czm":language.SelectedIndex==2?".cs":".vb";
            SaveAppSettings();
        };
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
        var modifyAi=new Button{Content="Ask AI to Modify…",Width=125,Margin=new Thickness(4),IsEnabled=false};
        var shortcutButton=new Button{Content="Shortcut…",Width=90,Margin=new Thickness(4),IsEnabled=false};
        var save=new Button{Content="Save source",Width=100,Margin=new Thickness(4)};
        var saveExit=new Button{Content="Save & Exit",Width=105,Margin=new Thickness(4)};
        var exit=new Button{Content="Exit",Width=75,Margin=new Thickness(4)};
        actionBar.Children.Add(run);actionBar.Children.Add(modifyAi);actionBar.Children.Add(shortcutButton);actionBar.Children.Add(save);actionBar.Children.Add(saveExit);actionBar.Children.Add(exit);
        DockPanel.SetDock(actionBar,Dock.Bottom);right.Children.Add(actionBar);

        var code=new ICSharpCode.AvalonEdit.TextEditor
        {
            FontFamily=new FontFamily("Consolas"),
            FontSize=13,
            ShowLineNumbers=true,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto
        };
        right.Children.Add(code);Grid.SetColumn(right,1);grid.Children.Add(right);

        void ApplyMacroSyntaxHighlighting()
        {
            var ext=CurrentMacroEditorExtension();
            code.SyntaxHighlighting=ext switch
            {
                ".cs"=>ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#"),
                ".vb"=>ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("VBNET"),
                _=>null
            };
        }

        string? selected=null;
        bool selectedEmbedded=false;
        var itemMap=new Dictionary<string,(bool Embedded,string Key)>(StringComparer.Ordinal);

        string CurrentMacroEditorExtension()=>selected==null?".czm":Path.GetExtension(selected).ToLowerInvariant();

        void ReplaceSelectedLines(Func<string,string> transform)
        {
            var text=code.Text;
            var start=code.SelectionStart;
            var length=code.SelectionLength;
            var end=start+length;

            var lineStart=start<=0?0:text.LastIndexOf('\n',Math.Max(0,start-1))+1;
            var lineEnd=text.IndexOf('\n',Math.Min(end,text.Length));
            if(lineEnd<0)lineEnd=text.Length;

            var segment=text[lineStart..lineEnd];
            var transformed=string.Join("\n",segment.Split('\n').Select(transform));
            code.Document.Replace(lineStart,lineEnd-lineStart,transformed);
            code.Select(lineStart,transformed.Length);
        }

        void IndentSelection(bool outdent)
        {
            ReplaceSelectedLines(line=>
            {
                if(!outdent)return "    "+line;
                if(line.StartsWith("\t"))return line[1..];
                var remove=Math.Min(4,line.TakeWhile(ch=>ch==' ').Count());
                return line[remove..];
            });
        }

        void ToggleComment()
        {
            var ext=CurrentMacroEditorExtension();
            var marker=ext==".vb"?"'":ext==".cs"?"//":"#";
            var text=code.Text;
            var start=code.SelectionStart;
            var end=start+code.SelectionLength;
            var lineStart=start<=0?0:text.LastIndexOf('\n',Math.Max(0,start-1))+1;
            var lineEnd=text.IndexOf('\n',Math.Min(end,text.Length));
            if(lineEnd<0)lineEnd=text.Length;
            var lines=text[lineStart..lineEnd].Split('\n');

            bool IsCommented(string line)
            {
                if(string.IsNullOrWhiteSpace(line))return true;
                var trimmed=line.TrimStart();
                return trimmed.StartsWith(marker,StringComparison.Ordinal);
            }

            var uncomment=lines.All(IsCommented);
            var transformed=lines.Select(line=>
            {
                if(string.IsNullOrWhiteSpace(line))return line;
                var indent=line.Length-line.TrimStart().Length;
                var prefix=line[..indent];
                var body=line[indent..];
                if(uncomment)
                {
                    if(body.StartsWith(marker,StringComparison.Ordinal))
                    {
                        body=body[marker.Length..];
                        if(body.StartsWith(" "))body=body[1..];
                    }
                    return prefix+body;
                }
                return prefix+marker+" "+body;
            });
            var replacement=string.Join("\n",transformed);
            code.Document.Replace(lineStart,lineEnd-lineStart,replacement);
            code.Select(lineStart,replacement.Length);
        }

        code.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Tab)
            {
                IndentSelection(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled=true;
                return;
            }

            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key==Key.Oem2)
            {
                ToggleComment();
                e.Handled=true;
                return;
            }

            if(e.Key==Key.Enter && Keyboard.Modifiers==ModifierKeys.None)
            {
                var caret=code.SelectionStart;
                var before=code.Text[..caret];
                var lineStart=before.LastIndexOf('\n')+1;
                var line=before[lineStart..];
                var indent=new string(' ',line.TakeWhile(ch=>ch==' ').Count());
                var trimmed=line.TrimEnd();
                var ext=CurrentMacroEditorExtension();
                if(ext==".cs" && trimmed.EndsWith("{",StringComparison.Ordinal))indent+="    ";
                else if(ext==".vb")
                {
                    var lower=trimmed.Trim().ToLowerInvariant();
                    if(lower.EndsWith(" then") || lower.StartsWith("for ") || lower.StartsWith("for each ") ||
                       lower.StartsWith("while ") || lower.StartsWith("do") || lower.StartsWith("try") ||
                       lower.Contains(" sub ") || lower.Contains(" function ") || lower.StartsWith("select case "))
                        indent+="    ";
                }
                var insertion=Environment.NewLine+indent;
                code.Document.Replace(code.SelectionStart,code.SelectionLength,insertion);
                code.CaretOffset=caret+insertion.Length;
                e.Handled=true;
            }
        };

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
            ApplyMacroSyntaxHighlighting();
            var selectedExt=selected==null?"":Path.GetExtension(selected).ToLowerInvariant();
            run.Content=selectedExt==".czm"?"Run macro":"Compile && Run";
            run.IsEnabled=selected!=null && (selectedExt==".czm" || ((selectedExt==".vb"||selectedExt==".cs") && compiledMacrosEnabled));
            embed.IsEnabled=!selectedEmbedded && selected!=null && selectedExt==".czm";
            extract.IsEnabled=selectedEmbedded && selected!=null;
            rename.IsEnabled=!selectedEmbedded && selected!=null;
            delete.IsEnabled=selected!=null;
            modifyAi.IsEnabled=selected!=null;
            shortcutButton.IsEnabled=selected!=null;
        };

        string StarterFor(string ext)=>ext switch
        {
            ".vb"=>@"Imports System.Windows.Controls

Public Module CtrlZedMacro
    Public Const MENU_NAME As String = ""New VB Macro""
    Public Const MENU_POSITION As Integer = 1

    Public Sub Run(Editor As RichTextBox)
        Editor.CaretPosition.InsertTextInRun(""Hello from a VB.NET macro"")
    End Sub
End Module
",
            ".cs"=>@"using System.Windows.Controls;

public static class CtrlZedMacro
{
    public const string MENU_NAME = ""New C# Macro"";
    public const int MENU_POSITION = 1;

    public static void Run(RichTextBox Editor)
    {
        Editor.CaretPosition.InsertTextInRun(""Hello from a C# macro"");
    }
}
",
            _=>$@"# CtrlZed Safe Macro
# NAME: New Macro
# DESCRIPTION: Describe what this macro does.
# SHORTCUT:
#
# Supported commands:
{SafeMacroLanguage.Reference}

INSERT_TEXT Hello from a CtrlZed safe macro
NEW_PARAGRAPH
INSERT_FIELD date
"
        };

        void make(string ext,string starter)
        {
            var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+ext;
            File.WriteAllText(Path.Combine(dir,name),starter);
            refresh(name,false);
        }

        create.Click+=(_,__)=>
        {
            var ext=language.SelectedIndex==0?".czm":language.SelectedIndex==2?".cs":".vb";
            make(ext,StarterFor(ext));
            ApplyMacroSyntaxHighlighting();
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

        shortcutButton.Click+=(_,__)=>
        {
            if(selected==null)return;
            var current=MacroShortcutFromSource(code.Text)??"";
            var chosen=CaptureMacroShortcut(w,current,selected);
            if(chosen==null)return;
            code.Text=SetMacroShortcutInSource(code.Text,Path.GetExtension(selected),chosen);
            SaveSelectedMacro();
            Status.Text=string.IsNullOrWhiteSpace(chosen)?"Macro shortcut cleared.":$"Macro shortcut assigned: {chosen}";
        };

        save.Click+=(_,__)=>SaveSelectedMacro();
        saveExit.Click+=(_,__)=>
        {
            SaveSelectedMacro();
            w.Close();
        };
        exit.Click+=(_,__)=>w.Close();

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
                code.Text="";
                refresh();RefreshMacroMenus();
                run.IsEnabled=false;modifyAi.IsEnabled=false;shortcutButton.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message,"Delete macro failed",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        };

        secrets.Click+=(_,__)=>ShowMacroSecretsManager(w);

        modifyAi.Click+=(_,__)=>
        {
            if(selected==null)return;
            var ext=Path.GetExtension(selected).ToLowerInvariant();
            var reference=ext==".czm"
                ?(extendedMacrosEnabled?SafeMacroLanguage.ExtendedReference:SafeMacroLanguage.Reference)
                :"";
            var instruction="Describe the changes you want to make to this selected macro. Preserve its existing purpose and metadata unless your requested change requires otherwise.";
            AiManager.ShowForMacro(w,ext,reference,code.Text,(result,targetExt)=>
            {
                if(!string.Equals(targetExt,ext,StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "The AI response changed macro language. Select the original target language to apply the revision here.",
                        "Modify macro",MessageBoxButton.OK,MessageBoxImage.Warning);
                    return;
                }
                code.Text=result;
                ApplyMacroSyntaxHighlighting();
                code.CaretOffset=0;
                code.Focus();
                Status.Text="AI revision loaded in Macro Manager — review and save when ready.";
            },instruction);
        };

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
                code.CaretOffset=code.Text.Length;
                code.Focus();
            });
        };

        refresh();run.IsEnabled=false;shortcutButton.IsEnabled=false;embed.IsEnabled=false;extract.IsEnabled=false;rename.IsEnabled=false;delete.IsEnabled=false;
        w.Content=grid;w.ShowDialog();
    }

    void ShowMacroSecretsManager(Window owner)
    {
        var w=new Window
        {
            Title="CtrlZed Macro Secrets",Owner=owner,Width=680,Height=500,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var list=new ListBox{Margin=new Thickness(0,0,12,0)};
        Grid.SetColumn(list,0);Grid.SetRow(list,0);root.Children.Add(list);

        var form=new StackPanel();
        var name=new TextBox{Margin=new Thickness(0,4,0,10)};
        var value=new PasswordBox{Margin=new Thickness(0,4,0,10)};
        var configured=new TextBlock{Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap};

        form.Children.Add(new TextBlock{Text="Provider defaults",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        var defaults=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        foreach(var provider in new[]{"ChatGPT","Gemini","Claude"})
        {
            var secret=CtrlZedSecrets.DefaultName(provider);
            var button=new Button{Content=$"{provider}: {secret}",Margin=new Thickness(0,0,6,6),Padding=new Thickness(8,3,8,3),Tag=secret};
            button.Click+=(_,__)=>
            {
                name.Text=secret;
                value.Clear();
                configured.Text=CtrlZedSecrets.Exists(secret)
                    ?$"{provider} default ({secret}) is configured. Enter a new value only to replace it."
                    :$"{provider} default ({secret}) is not configured. Enter the provider API key below.";
            };
            defaults.Children.Add(button);
        }
        form.Children.Add(defaults);

        form.Children.Add(new TextBlock{Text="Secret name"});
        form.Children.Add(name);
        form.Children.Add(new TextBlock{Text="Secret value"});
        form.Children.Add(value);
        form.Children.Add(new TextBlock
        {
            Text="Values are encrypted with Windows DPAPI for the current Windows user. Saved values are never displayed again. AI-generated macros use the provider defaults above unless you explicitly request another secret name.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)
        });
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
            var names=CtrlZedSecrets.Names();
            list.ItemsSource=names;
            var providerStatus=string.Join(" · ",new[]{"ChatGPT","Gemini","Claude"}.Select(p=>
            {
                var key=CtrlZedSecrets.DefaultName(p);
                return p+":"+(CtrlZedSecrets.Exists(key)?"configured":"missing");
            }));
            configured.Text=$"{names.Count} secret{(names.Count==1?"":"s")} configured. {providerStatus}";
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

    static string? MacroShortcutFromSource(string source)
    {
        foreach(var line in source.Replace("\r\n","\n").Split('\n'))
        {
            var trimmed=line.TrimStart();
            foreach(var prefix in new[]{"# SHORTCUT:","' SHORTCUT:","// SHORTCUT:"})
                if(trimmed.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                    return trimmed[prefix.Length..].Trim();
        }
        return null;
    }

    static string SetMacroShortcutInSource(string source,string extension,string shortcut)
    {
        var lines=source.Replace("\r\n","\n").Split('\n').ToList();
        lines.RemoveAll(line=>
        {
            var trimmed=line.TrimStart();
            return trimmed.StartsWith("# SHORTCUT:",StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("' SHORTCUT:",StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("// SHORTCUT:",StringComparison.OrdinalIgnoreCase);
        });

        if(!string.IsNullOrWhiteSpace(shortcut))
        {
            var prefix=extension.ToLowerInvariant() switch
            {
                ".vb"=>"' SHORTCUT: ",
                ".cs"=>"// SHORTCUT: ",
                _=>"# SHORTCUT: "
            };
            lines.Insert(0,prefix+shortcut.Trim());
        }
        return string.Join(Environment.NewLine,lines);
    }

    bool IsReservedShortcut(string shortcut)
    {
        var reserved=profile switch
        {
            KeyboardProfile.WordPerfect51=>new[]{"F3","F7","Shift+F7","Shift+F6"},
            KeyboardProfile.WordStar40=>new[]{"Ctrl+K","Ctrl+E","Ctrl+X","Ctrl+S","Ctrl+D"},
            _=>new[]{"Ctrl+N","Ctrl+O","Ctrl+S","Ctrl+Shift+S","Ctrl+P","Ctrl+H","Ctrl+Enter","Ctrl+B","Ctrl+I","Ctrl+U","Ctrl+Z","Ctrl+Y","Ctrl+X","Ctrl+C","Ctrl+V","F9"}
        };
        return reserved.Contains(shortcut,StringComparer.OrdinalIgnoreCase);
    }

    string? CaptureMacroShortcut(Window owner,string current,string? selectedMacro)
    {
        string? chosen=null;
        var w=new Window
        {
            Title="Assign Macro Shortcut",Owner=owner,Width=480,Height=245,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new StackPanel{Margin=new Thickness(16)};
        root.Children.Add(new TextBlock
        {
            Text="Press the key combination you want to assign. CtrlZed will reject shortcuts reserved by the selected keyboard profile.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        });
        var display=new TextBox{Text=current,IsReadOnly=true,FontSize=18,Margin=new Thickness(0,0,0,8)};
        var status=new TextBlock{Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,MinHeight=38,Margin=new Thickness(0,0,0,8)};
        root.Children.Add(display);root.Children.Add(status);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var assign=new Button{Content="Assign",Width=80,Margin=new Thickness(4),IsEnabled=!string.IsNullOrWhiteSpace(current),IsDefault=true};
        var clear=new Button{Content="Clear",Width=80,Margin=new Thickness(4)};
        var cancel=new Button{Content="Cancel",Width=80,Margin=new Thickness(4),IsCancel=true};
        buttons.Children.Add(clear);buttons.Children.Add(assign);buttons.Children.Add(cancel);root.Children.Add(buttons);

        w.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key is Key.Escape or Key.Enter)return;
            var value=BuildShortcutString(e);
            if(string.IsNullOrWhiteSpace(value))
            {
                status.Text="Use at least one of Ctrl, Alt or Shift with another key.";
                return;
            }
            if(IsReservedShortcut(value))
            {
                display.Text=value;assign.IsEnabled=false;
                status.Text="That combination is reserved by the selected keyboard profile.";
                e.Handled=true;return;
            }
            var conflict=GetMacroSources().FirstOrDefault(m=>
                !string.Equals(m.Name,selectedMacro,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(MacroShortcutFromSource(m.Source),value,StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrWhiteSpace(conflict.Name))
            {
                display.Text=value;assign.IsEnabled=false;
                status.Text=$"Already assigned to {conflict.Name}.";
                e.Handled=true;return;
            }
            display.Text=value;status.Text="Available.";assign.IsEnabled=true;e.Handled=true;
        };
        assign.Click+=(_,__)=>{chosen=display.Text.Trim();w.DialogResult=true;};
        clear.Click+=(_,__)=>{chosen="";w.DialogResult=true;};
        w.Content=root;
        return w.ShowDialog()==true?chosen:null;
    }

    async Task<bool> TryRunMacroShortcutAsync(KeyEventArgs e)
    {
        if(e.IsRepeat)return false;
        var pressed=BuildShortcutString(e);
        if(string.IsNullOrWhiteSpace(pressed))return false;

        foreach(var macro in GetMacroSources())
        {
            var shortcut=MacroShortcutFromSource(macro.Source);
            if(string.IsNullOrWhiteSpace(shortcut))continue;
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

    void UpdateKeyboardMenuState()
    {
        if(WordProfile==null)return;
        WordProfile.IsChecked=profile==KeyboardProfile.Word;
        WPProfile.IsChecked=profile==KeyboardProfile.WordPerfect51;
        WSProfile.IsChecked=profile==KeyboardProfile.WordStar40;

        foreach(var item in new[]{NewMenu,OpenMenu,SaveMenu,SaveAsMenu,PrintMenu,ExitMenu,UndoMenu,RedoMenu,CutMenu,CopyMenu,PasteMenu,FindReplaceMenu,PageBreakMenu,BoldMenu,ItalicMenu,UnderlineMenu,CentreMenu,ReformatMenu,AboutMenu})
            item.InputGestureText="";

        if(profile==KeyboardProfile.Word)
        {
            NewMenu.InputGestureText="Ctrl+N";OpenMenu.InputGestureText="Ctrl+O";SaveMenu.InputGestureText="Ctrl+S";SaveAsMenu.InputGestureText="Ctrl+Shift+S";
            PrintMenu.InputGestureText="Ctrl+P";UndoMenu.InputGestureText="Ctrl+Z";RedoMenu.InputGestureText="Ctrl+Y";
            CutMenu.InputGestureText="Ctrl+X";CopyMenu.InputGestureText="Ctrl+C";PasteMenu.InputGestureText="Ctrl+V";
            FindReplaceMenu.InputGestureText="Ctrl+H";PageBreakMenu.InputGestureText="Ctrl+Enter";
            BoldMenu.InputGestureText="Ctrl+B";ItalicMenu.InputGestureText="Ctrl+I";UnderlineMenu.InputGestureText="Ctrl+U";
            ReformatMenu.InputGestureText="F9";
        }
        else if(profile==KeyboardProfile.WordPerfect51)
        {
            PrintMenu.InputGestureText="Shift+F7";ExitMenu.InputGestureText="F7";CentreMenu.InputGestureText="Shift+F6";AboutMenu.InputGestureText="F3";
        }
        else
        {
            SaveMenu.InputGestureText="Ctrl+K, S";ExitMenu.InputGestureText="Ctrl+K, X";
        }
    }

    void Keyboard_Click(object s,RoutedEventArgs e)
    {
        if(s==WPProfile)
        {
            profile=KeyboardProfile.WordPerfect51;
            Status.Text="Keyboard: WordPerfect 5.1 (initial DOS subset)";
        }
        else if(s==WSProfile)
        {
            profile=KeyboardProfile.WordStar40;
            Status.Text="Keyboard: WordStar 4.0 (initial subset)";
        }
        else
        {
            profile=KeyboardProfile.Word;
            Status.Text="Keyboard: Microsoft Word";
        }
        wordStarPrefix=false;
        UpdateKeyboardMenuState();
        SaveAppSettings();
    }

    async void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(await TryRunMacroShortcutAsync(e)){e.Handled=true;return;}
        if(profile==KeyboardProfile.Word && Keyboard.Modifiers==ModifierKeys.None && e.Key==Key.F9)
        {
            ReformatDocument_Click(this,new RoutedEventArgs());e.Handled=true;return;
        }
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
        uiRefreshTimer.Stop();
        CaptureWindowPlacement();
        SaveAppSettings();
        TryDeleteRecovery();
        closingApproved=true;
    }

    void About_Click(object s,RoutedEventArgs e)
    {
        Branding.CreateAboutWindow(this,"0.28.0 beta 28").ShowDialog();
    }
}
