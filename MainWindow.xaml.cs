using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;
public partial class MainWindow : Window
{
    string? currentFile;
    public MainWindow()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
        FontBox.SelectedItem = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Calibri") ?? Fonts.SystemFontFamilies.First();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_,__) => Save_Click(this,new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, new KeyGesture(System.Windows.Input.Key.S, System.Windows.Input.ModifierKeys.Control)));
    }
    void New_Click(object s,RoutedEventArgs e){ Editor.Document = new FlowDocument(); currentFile=null; Title="CtrlZed — beta 1"; }
    void Open_Click(object s,RoutedEventArgs e){
        var d=new OpenFileDialog{Filter="Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt|All files (*.*)|*.*"};
        if(d.ShowDialog()!=true)return;
        try{
            if(Path.GetExtension(d.FileName).Equals(".txt",StringComparison.OrdinalIgnoreCase)) Editor.Document=new FlowDocument(new Paragraph(new Run(File.ReadAllText(d.FileName))));
            else using(var fs=File.OpenRead(d.FileName)) new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Load(fs,DataFormats.Rtf);
            currentFile=d.FileName; Title=$"CtrlZed — {Path.GetFileName(currentFile)}"; Status.Text="Opened "+currentFile;
        }catch(Exception ex){MessageBox.Show(ex.Message,"Open failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void Save_Click(object s,RoutedEventArgs e){
        if(currentFile==null){var d=new SaveFileDialog{Filter="Rich Text Format (*.rtf)|*.rtf|Text files (*.txt)|*.txt",DefaultExt=".rtf"};if(d.ShowDialog()!=true)return;currentFile=d.FileName;}
        try{
            var range=new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd);
            if(Path.GetExtension(currentFile).Equals(".txt",StringComparison.OrdinalIgnoreCase)) File.WriteAllText(currentFile,range.Text);
            else {var tmp=currentFile+".tmp";using(var fs=File.Create(tmp))range.Save(fs,DataFormats.Rtf);if(File.Exists(currentFile))File.Replace(tmp,currentFile,currentFile+".bak");else File.Move(tmp,currentFile);}
            Title=$"CtrlZed — {Path.GetFileName(currentFile)}";Status.Text="Saved "+currentFile;
        }catch(Exception ex){MessageBox.Show(ex.Message,"Save failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void Print_Click(object s,RoutedEventArgs e){var pd=new PrintDialog();if(pd.ShowDialog()==true)pd.PrintDocument(((IDocumentPaginatorSource)Editor.Document).DocumentPaginator,"CtrlZed document");}
    void Picture_Click(object s,RoutedEventArgs e){
        var d=new OpenFileDialog{Filter="Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};if(d.ShowDialog()!=true)return;
        try{var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(d.FileName);b.EndInit();var img=new Image{Source=b,MaxWidth=600,Stretch=Stretch.Uniform,Margin=new Thickness(4)};var ui=new InlineUIContainer(img,Editor.CaretPosition);Editor.CaretPosition=ui.ElementEnd;Editor.Focus();}catch(Exception ex){MessageBox.Show(ex.Message,"Picture failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void FontBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(FontBox.SelectedItem is FontFamily f)Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,f);}
    void SizeBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(SizeBox.SelectedItem is ComboBoxItem i && double.TryParse(i.Content?.ToString(),out var pt))Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,pt*96/72);}
    void Exit_Click(object s,RoutedEventArgs e)=>Close();
    void About_Click(object s,RoutedEventArgs e)=>MessageBox.Show("CtrlZed 0.1.0 beta 1\n\nThe word processor that knows its place.\n\nHuman-directed. AI-authored project code. Open source under MIT.","About CtrlZed");
}