using Microsoft.Win32;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow : Window
{
    enum KeyboardProfile { Word, WordPerfect51, WordStar40 }
    KeyboardProfile profile = KeyboardProfile.Word;
    bool wordStarPrefix;
    string? currentFile;
    double pageWidth = 794, pageHeight = 1123;
    Thickness pageMargins = new(72);

    record CzdSettings(double PageWidth,double PageHeight,double Left,double Top,double Right,double Bottom);

    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save,new KeyGesture(Key.S,ModifierKeys.Control)));
        Editor.PreviewMouseDoubleClick += Editor_PreviewMouseDoubleClick;
        ApplyPage();
    }

    void New_Click(object s,RoutedEventArgs e){Editor.Document=new FlowDocument();currentFile=null;Title="CtrlZed — beta 2";Status.Text="New document";}
    void Open_Click(object s,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|All files (*.*)|*.*"};
        if(d.ShowDialog()!=true)return;
        try{
            var ext=Path.GetExtension(d.FileName).ToLowerInvariant();
            if(ext==".czd")LoadCzd(d.FileName);
            else if(ext==".txt")Editor.Document=new FlowDocument(new Paragraph(new Run(File.ReadAllText(d.FileName))));
            else using(var fs=File.OpenRead(d.FileName))new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(fs,DataFormats.Rtf);
            currentFile=d.FileName;Title=$"CtrlZed — {Path.GetFileName(currentFile)}";Status.Text="Opened "+currentFile;
        }catch(Exception ex){MessageBox.Show(ex.Message,"Open failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void Save_Click(object s,RoutedEventArgs e)
    {
        if(currentFile==null){
            var d=new SaveFileDialog{Filter="CtrlZed documents (*.czd)|*.czd|Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt",DefaultExt=".czd",AddExtension=true};
            if(d.ShowDialog()!=true)return;currentFile=d.FileName;
        }
        try{
            var ext=Path.GetExtension(currentFile).ToLowerInvariant();
            if(ext==".czd")SaveCzd(currentFile);
            else if(ext==".txt")File.WriteAllText(currentFile,new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text);
            else SaveRtf(currentFile);
            Title=$"CtrlZed — {Path.GetFileName(currentFile)}";Status.Text="Saved "+currentFile;
        }catch(Exception ex){MessageBox.Show(ex.Message,"Save failed",MessageBoxButton.OK,MessageBoxImage.Error);}
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
        using(var zip=ZipFile.Open(tmp,ZipArchiveMode.Create)){
            var doc=zip.CreateEntry("document.rtf");
            using(var s=doc.Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(s,DataFormats.Rtf);
            var settings=zip.CreateEntry("settings.json");
            using(var sw=new StreamWriter(settings.Open()))sw.Write(JsonSerializer.Serialize(new CzdSettings(pageWidth,pageHeight,pageMargins.Left,pageMargins.Top,pageMargins.Right,pageMargins.Bottom)));
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
        if(st!=null){using var sr=new StreamReader(st.Open());var v=JsonSerializer.Deserialize<CzdSettings>(sr.ReadToEnd());if(v!=null){pageWidth=v.PageWidth;pageHeight=v.PageHeight;pageMargins=new Thickness(v.Left,v.Top,v.Right,v.Bottom);ApplyPage();}}
    }
    static void ReplaceFile(string tmp,string path){if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}

    void Print_Click(object s,RoutedEventArgs e){var pd=new PrintDialog();if(pd.ShowDialog()==true){Editor.Document.PageWidth=pageWidth;Editor.Document.PageHeight=pageHeight;Editor.Document.PagePadding=pageMargins;pd.PrintDocument(((IDocumentPaginatorSource)Editor.Document).DocumentPaginator,"CtrlZed document");}}
    void Picture_Click(object s,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Filter="Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};
        if(d.ShowDialog()!=true)return;
        try{
            var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(d.FileName);b.EndInit();
            var img=new Image{Source=b,MaxWidth=Math.Max(100,pageWidth-pageMargins.Left-pageMargins.Right),Stretch=Stretch.Uniform,Margin=new Thickness(4),Tag="CtrlZedPicture"};
            var ui=new InlineUIContainer(img,Editor.CaretPosition);Editor.CaretPosition=ui.ElementEnd;Editor.Focus();Status.Text="Picture inserted — double-click it for size and alignment.";
        }catch(Exception ex){MessageBox.Show(ex.Message,"Picture failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void Editor_PreviewMouseDoubleClick(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject d){
            while(d!=null && d is not Image)d=VisualTreeHelper.GetParent(d);
            if(d is Image img){ShowPictureProperties(img);e.Handled=true;}
        }
    }
    void ShowPictureProperties(Image img)
    {
        var w=new Window{Title="Picture Properties",Owner=this,Width=360,Height=260,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var g=new Grid{Margin=new Thickness(14)};for(int i=0;i<5;i++)g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});g.ColumnDefinitions.Add(new ColumnDefinition());
        var width=new TextBox{Text=(double.IsNaN(img.Width)?img.ActualWidth:img.Width).ToString("0")};var align=new ComboBox{ItemsSource=new[]{"Left","Centre","Right"},SelectedIndex=1,Margin=new Thickness(0,5,0,5)};
        AddRow(g,0,"Width (pixels)",width);AddRow(g,1,"Alignment",align);
        var keep=new CheckBox{Content="Keep aspect ratio",IsChecked=true,Margin=new Thickness(0,8,0,8)};Grid.SetRow(keep,2);Grid.SetColumnSpan(keep,2);g.Children.Add(keep);
        var note=new TextBlock{Text="Images remain inline in beta 2. Alignment applies to the containing paragraph.",TextWrapping=TextWrapping.Wrap};Grid.SetRow(note,3);Grid.SetColumnSpan(note,2);g.Children.Add(note);
        var ok=new Button{Content="Apply",Width=85,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};Grid.SetRow(ok,4);Grid.SetColumnSpan(ok,2);g.Children.Add(ok);
        ok.Click+=(_,__)=>{if(double.TryParse(width.Text,out var px)&&px>10){if(keep.IsChecked==true&&img.Source is BitmapSource bs&&bs.PixelWidth>0)img.Height=px*bs.PixelHeight/bs.PixelWidth;img.Width=px;}if(img.Parent is InlineUIContainer u&&u.Parent is Paragraph p)p.TextAlignment=align.SelectedIndex==0?TextAlignment.Left:align.SelectedIndex==2?TextAlignment.Right:TextAlignment.Center;w.DialogResult=true;};
        w.Content=g;w.ShowDialog();
    }
    static void AddRow(Grid g,int row,string label,Control control){var t=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(t,row);Grid.SetColumn(t,0);Grid.SetRow(control,row);Grid.SetColumn(control,1);g.Children.Add(t);g.Children.Add(control);}

    void PageSetup_Click(object s,RoutedEventArgs e)
    {
        var w=new Window{Title="Page Setup",Owner=this,Width=390,Height=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var p=new StackPanel{Margin=new Thickness(16)};
        var paper=new ComboBox{ItemsSource=new[]{"A4 Portrait","A4 Landscape","A5 Portrait","US Letter"},SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
        p.Children.Add(new TextBlock{Text="Paper"});p.Children.Add(paper);
        TextBox Box(double dip)=>new(){Text=(dip*25.4/96).ToString("0.0"),Margin=new Thickness(0,2,0,6)};
        var l=Box(pageMargins.Left),t=Box(pageMargins.Top),r=Box(pageMargins.Right),b=Box(pageMargins.Bottom);
        foreach(var pair in new[]{("Left margin (mm)",l),("Top margin (mm)",t),("Right margin (mm)",r),("Bottom margin (mm)",b)}){p.Children.Add(new TextBlock{Text=pair.Item1});p.Children.Add(pair.Item2);}
        var ok=new Button{Content="Apply",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};p.Children.Add(ok);
        ok.Click+=(_,__)=>{(pageWidth,pageHeight)=paper.SelectedIndex switch{1=>(1123d,794d),2=>(559d,794d),3=>(816d,1056d),_=>(794d,1123d)};double mm(TextBox x)=>double.TryParse(x.Text,out var v)?v*96/25.4:72;pageMargins=new Thickness(mm(l),mm(t),mm(r),mm(b));ApplyPage();w.DialogResult=true;};
        w.Content=p;w.ShowDialog();
    }
    void ApplyPage(){PageBorder.Width=pageWidth;PageBorder.MinHeight=pageHeight;PageBorder.Padding=pageMargins;Editor.Document.PageWidth=pageWidth;Editor.Document.PageHeight=pageHeight;Editor.Document.PagePadding=pageMargins;}

    void MacroLibrary_Click(object s,RoutedEventArgs e)
    {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","Macros");Directory.CreateDirectory(dir);
        var w=new Window{Title="CtrlZed Macro Library — source only",Owner=this,Width=780,Height=560,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var grid=new Grid{Margin=new Thickness(10)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel{Margin=new Thickness(0,0,10,0)};var list=new ListBox();DockPanel.SetDock(list,Dock.Top);left.Children.Add(list);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};var nv=new Button{Content="New VB",Margin=new Thickness(0,0,5,0)},nc=new Button{Content="New C#"};buttons.Children.Add(nv);buttons.Children.Add(nc);DockPanel.SetDock(buttons,Dock.Bottom);left.Children.Add(buttons);Grid.SetColumn(left,0);grid.Children.Add(left);
        var right=new DockPanel();var warning=new TextBlock{Text="Macro execution is disabled in beta 2. This editor stores source in your central library.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(warning,Dock.Top);right.Children.Add(warning);
        var save=new Button{Content="Save source",Width=100,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(save,Dock.Bottom);right.Children.Add(save);
        var code=new TextBox{AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};right.Children.Add(code);Grid.SetColumn(right,1);grid.Children.Add(right);
        string? selected=null;void refresh(){list.ItemsSource=Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x=>x).ToList();}
        list.SelectionChanged+=(_,__)=>{selected=list.SelectedItem?.ToString();if(selected!=null)code.Text=File.ReadAllText(Path.Combine(dir,selected));};
        void make(string ext,string starter){var name="Macro_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+ext;File.WriteAllText(Path.Combine(dir,name),starter);refresh();list.SelectedItem=name;}
        nv.Click+=(_,__)=>make(".vb","' CtrlZed VB.NET macro source\n' Execution is disabled in beta 2.\n");nc.Click+=(_,__)=>make(".cs","// CtrlZed C# macro source\n// Execution is disabled in beta 2.\n");
        save.Click+=(_,__)=>{if(selected!=null){File.WriteAllText(Path.Combine(dir,selected),code.Text);Status.Text="Macro source saved to central library.";}};refresh();w.Content=grid;w.ShowDialog();
    }

    void Keyboard_Click(object s,RoutedEventArgs e)
    {
        WordProfile.IsChecked=WPProfile.IsChecked=WSProfile.IsChecked=false;
        if(s==WPProfile){profile=KeyboardProfile.WordPerfect51;WPProfile.IsChecked=true;Status.Text="Keyboard: WordPerfect 5.1 (initial DOS subset)";}
        else if(s==WSProfile){profile=KeyboardProfile.WordStar40;WSProfile.IsChecked=true;Status.Text="Keyboard: WordStar 4.0 (initial subset)";}
        else{profile=KeyboardProfile.Word;WordProfile.IsChecked=true;Status.Text="Keyboard: Microsoft Word";}
        wordStarPrefix=false;
    }
    void Window_PreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(profile==KeyboardProfile.WordStar40){
            if(wordStarPrefix){wordStarPrefix=false;if(e.Key==Key.S){Save_Click(this,new RoutedEventArgs());e.Handled=true;}else if(e.Key==Key.X){Save_Click(this,new RoutedEventArgs());Close();e.Handled=true;}return;}
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.K){wordStarPrefix=true;Status.Text="WordStar ^K …";e.Handled=true;return;}
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){var cmd=e.Key switch{Key.E=>EditingCommands.MoveUpByLine,Key.X=>EditingCommands.MoveDownByLine,Key.S=>EditingCommands.MoveLeftByCharacter,Key.D=>EditingCommands.MoveRightByCharacter,_=>null};if(cmd!=null){cmd.Execute(null,Editor);e.Handled=true;}}
        }else if(profile==KeyboardProfile.WordPerfect51){
            if(e.Key==Key.F3){About_Click(this,new RoutedEventArgs());e.Handled=true;}
            else if(e.Key==Key.F7&&!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){Close();e.Handled=true;}
            else if(e.Key==Key.F7&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){Print_Click(this,new RoutedEventArgs());e.Handled=true;}
            else if(e.Key==Key.F6&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)){EditingCommands.AlignCenter.Execute(null,Editor);e.Handled=true;}
        }
    }

    void FontBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(FontBox.SelectedItem is FontFamily f)Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,f);}
    void SizeBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(SizeBox.SelectedItem is ComboBoxItem i&&double.TryParse(i.Content?.ToString(),out var pt))Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,pt*96/72);}
    void Exit_Click(object s,RoutedEventArgs e)=>Close();
    void About_Click(object s,RoutedEventArgs e)=>MessageBox.Show("CtrlZed 0.2.0 beta 2\n\nThe word processor that knows its place.\n\nHuman-directed. AI-authored project code. Open source under MIT.","About CtrlZed");
}
