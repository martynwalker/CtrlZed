using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace CtrlZed;

internal sealed class MacroManagerWindowState
{
    public double Left { get; set; }=double.NaN;
    public double Top { get; set; }=double.NaN;
    public double Width { get; set; }=900;
    public double Height { get; set; }=640;
    public bool Maximised { get; set; }
}

internal static class MacroManagerWindowStore
{
    static string PathName
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"macro-manager-window.json");
        }
    }

    public static MacroManagerWindowState Load()
    {
        try
        {
            if(File.Exists(PathName))
                return JsonSerializer.Deserialize<MacroManagerWindowState>(File.ReadAllText(PathName))
                    ??new MacroManagerWindowState();
        }
        catch{ }
        return new MacroManagerWindowState();
    }

    public static void Save(MacroManagerWindowState value)
    {
        try{File.WriteAllText(PathName,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));}
        catch{ }
    }
}

internal static class MacroManagerRefinementBootstrap
{
    static readonly HashSet<Window> Enhanced=new();

    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not Window window ||
           !string.Equals(window.Title,"CtrlZed Macro Manager",StringComparison.Ordinal) ||
           !Enhanced.Add(window))return;

        Enhance(window);
        window.Closed+=(_,__)=>Enhanced.Remove(window);
    }

    static void Enhance(Window window)
    {
        window.MinWidth=780;
        window.MinHeight=520;
        var saved=MacroManagerWindowStore.Load();
        if(saved.Width>=window.MinWidth)window.Width=saved.Width;
        if(saved.Height>=window.MinHeight)window.Height=saved.Height;
        if(IsVisiblePosition(saved.Left,saved.Top,saved.Width,saved.Height))
        {
            window.WindowStartupLocation=WindowStartupLocation.Manual;
            window.Left=saved.Left;
            window.Top=saved.Top;
        }
        if(saved.Maximised)window.WindowState=WindowState.Maximized;

        foreach(var button in Descendants<Button>(window))
        {
            if(button.Content is string caption && caption.Contains("&&",StringComparison.Ordinal))
                button.Content=caption.Replace("&&","&",StringComparison.Ordinal);
        }

        var editor=Descendants<TextEditor>(window).FirstOrDefault();
        if(editor!=null)InstallFind(window,editor);

        window.Closing+=(_,__)=>
        {
            var bounds=window.WindowState==WindowState.Normal
                ?new Rect(window.Left,window.Top,window.Width,window.Height)
                :window.RestoreBounds;
            MacroManagerWindowStore.Save(new MacroManagerWindowState
            {
                Left=bounds.Left,Top=bounds.Top,
                Width=Math.Max(window.MinWidth,bounds.Width),
                Height=Math.Max(window.MinHeight,bounds.Height),
                Maximised=window.WindowState==WindowState.Maximized
            });
        };
    }

    static void InstallFind(Window owner,TextEditor editor)
    {
        Window? findWindow=null;
        TextBox? queryBox=null;
        string lastQuery="";

        void FindNext()
        {
            var query=queryBox?.Text??lastQuery;
            if(string.IsNullOrWhiteSpace(query))return;
            lastQuery=query;
            var start=Math.Clamp(editor.SelectionStart+Math.Max(0,editor.SelectionLength),0,editor.Text.Length);
            var index=editor.Text.IndexOf(query,start,StringComparison.CurrentCultureIgnoreCase);
            if(index<0 && start>0)index=editor.Text.IndexOf(query,0,StringComparison.CurrentCultureIgnoreCase);
            if(index<0)
            {
                MessageBox.Show(owner,$"‘{query}’ was not found.","Find in macro",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            editor.Select(index,query.Length);
            editor.CaretOffset=index+query.Length;
            editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber);
            editor.Focus();
        }

        void OpenFind()
        {
            if(findWindow!=null)
            {
                findWindow.Activate();
                queryBox?.Focus();
                queryBox?.SelectAll();
                return;
            }

            findWindow=new Window
            {
                Title="Find in Macro",
                Owner=owner,
                Width=460,
                Height=150,
                ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                ShowInTaskbar=false,
                Icon=Branding.CreateIconImageSource()
            };
            var root=new Grid{Margin=new Thickness(14)};
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.ColumnDefinitions.Add(new ColumnDefinition());
            queryBox=new TextBox{Text=lastQuery,Margin=new Thickness(0,0,0,10)};
            Grid.SetRow(queryBox,0);root.Children.Add(queryBox);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var next=new Button{Content="Find Next",Width=90,Margin=new Thickness(4),IsDefault=true};
            var close=new Button{Content="Close",Width=80,Margin=new Thickness(4),IsCancel=true};
            next.Click+=(_,__)=>FindNext();
            close.Click+=(_,__)=>findWindow.Close();
            buttons.Children.Add(next);buttons.Children.Add(close);
            Grid.SetRow(buttons,1);root.Children.Add(buttons);
            findWindow.Content=root;
            findWindow.Closed+=(_,__)=>{findWindow=null;queryBox=null;};
            findWindow.Show();
            queryBox.Focus();
            queryBox.SelectAll();
        }

        editor.PreviewKeyDown+=(_,e)=>
        {
            if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.F)
            {
                OpenFind();
                e.Handled=true;
            }
        };
    }

    static bool IsVisiblePosition(double left,double top,double width,double height)
    {
        if(double.IsNaN(left)||double.IsNaN(top))return false;
        var right=left+Math.Max(200,width);
        var bottom=top+Math.Max(200,height);
        return right>SystemParameters.VirtualScreenLeft &&
               left<SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth &&
               bottom>SystemParameters.VirtualScreenTop &&
               top<SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight;
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        var count=VisualTreeHelper.GetChildrenCount(root);
        for(var i=0;i<count;i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match)yield return match;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
}
