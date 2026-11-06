using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public enum CtrlZedKeyboardProfile
{
    MicrosoftWord,
    WordPerfect51,
    WordStar40
}

public sealed record CtrlZedShortcut(
    string Command,
    string Description,
    string Word,
    string WordPerfect,
    string WordStar,
    string? MenuPath=null)
{
    public string For(CtrlZedKeyboardProfile profile)=>profile switch
    {
        CtrlZedKeyboardProfile.WordPerfect51=>WordPerfect,
        CtrlZedKeyboardProfile.WordStar40=>WordStar,
        _=>Word
    };
}

public static class CtrlZedKeyboardCatalogue
{
    public static IReadOnlyList<CtrlZedShortcut> All { get; }=new CtrlZedShortcut[]
    {
        new("New document","Create a new document.","Ctrl+N","","","File/New"),
        new("Open","Open an existing document.","Ctrl+O","","","File/Open"),
        new("Save","Save the current document.","Ctrl+S","","Ctrl+K, S","File/Save"),
        new("Save As","Save to a new file or format.","Ctrl+Shift+S","","","File/Save As"),
        new("Print","Print the current document.","Ctrl+P","Shift+F7","","File/Print"),
        new("Exit","Close CtrlZed.","","F7","Ctrl+K, X","File/Exit"),

        new("Undo","Undo the most recent editing action.","Ctrl+Z","","","Edit/Undo"),
        new("Redo","Redo the most recently undone action.","Ctrl+Y","","","Edit/Redo"),
        new("Cut","Cut the selection.","Ctrl+X","","","Edit/Cut"),
        new("Copy","Copy the selection.","Ctrl+C","","","Edit/Copy"),
        new("Paste","Paste at the caret.","Ctrl+V","","","Edit/Paste"),
        new("Find / Replace","Find and replace text.","Ctrl+H","","","Edit/Find / Replace"),

        new("Page break","Insert a manual page break.","Ctrl+Enter","","","Insert/Page Break"),
        new("Bold","Toggle bold formatting.","Ctrl+B","","","Format/Bold"),
        new("Italic","Toggle italic formatting.","Ctrl+I","","","Format/Italic"),
        new("Underline","Toggle underline formatting.","Ctrl+U","","","Format/Underline"),
        new("Centre paragraph","Centre the current paragraph.","","Shift+F6","","Format/Align Centre"),
        new("Reformat document","Reapply the active style set to the document.","F9","","","Format/Reformat Document"),

        new("Next spelling error","Move to the next red spelling mark.","F7","","","Review/Next Spelling Error"),
        new("Contextual thesaurus","Open contextual alternatives for the selection or current word.","Shift+F7","","","Review/Contextual Thesaurus"),
        new("About CtrlZed","Open the About window.","","F3","","Help/About CtrlZed"),

        new("Move up one line","WordStar cursor movement.","","","Ctrl+E"),
        new("Move down one line","WordStar cursor movement.","","","Ctrl+X"),
        new("Move left one character","WordStar cursor movement.","","","Ctrl+S"),
        new("Move right one character","WordStar cursor movement.","","","Ctrl+D")
    };

    public static IReadOnlyList<string> Validate()
    {
        var errors=new List<string>();
        foreach(var profile in Enum.GetValues<CtrlZedKeyboardProfile>())
        {
            var duplicates=All
                .Select(x=>(Item:x,Shortcut:x.For(profile)))
                .Where(x=>!string.IsNullOrWhiteSpace(x.Shortcut))
                .GroupBy(x=>x.Shortcut,StringComparer.OrdinalIgnoreCase)
                .Where(g=>g.Count()>1)
                .ToList();
            foreach(var duplicate in duplicates)
                errors.Add($"{profile}: {duplicate.Key} is assigned to {string.Join(", ",duplicate.Select(x=>x.Item.Command))}.");
        }
        if(All.Any(x=>string.IsNullOrWhiteSpace(x.Command)))errors.Add("A keyboard command has no name.");
        return errors;
    }
}

public partial class MainWindow
{
    bool beta34KeyboardCatalogueInstalled;

    CtrlZedKeyboardProfile Beta34KeyboardProfile=>profile switch
    {
        KeyboardProfile.WordPerfect51=>CtrlZedKeyboardProfile.WordPerfect51,
        KeyboardProfile.WordStar40=>CtrlZedKeyboardProfile.WordStar40,
        _=>CtrlZedKeyboardProfile.MicrosoftWord
    };

    internal void InstallBeta34KeyboardCatalogue()
    {
        if(beta34KeyboardCatalogueInstalled)
        {
            RefreshBeta34KeyboardLabels();
            return;
        }
        beta34KeyboardCatalogueInstalled=true;

        foreach(var top in MainMenu.Items.OfType<MenuItem>())
            top.SubmenuOpened+=(_,__)=>Dispatcher.BeginInvoke(
                new Action(RefreshBeta34KeyboardLabels),DispatcherPriority.ContextIdle);

        WordProfile.Click+=Beta34KeyboardProfileChanged;
        WPProfile.Click+=Beta34KeyboardProfileChanged;
        WSProfile.Click+=Beta34KeyboardProfileChanged;

        var help=FindBeta34MenuItem("Help","Keyboard Shortcuts");
        if(help!=null)
        {
            help.Click-=KeyboardShortcuts_Click;
            help.Click+=Beta34KeyboardShortcuts_Click;
        }
        RefreshBeta34KeyboardLabels();
    }

    void Beta34KeyboardProfileChanged(object sender,RoutedEventArgs e)
        =>Dispatcher.BeginInvoke(new Action(RefreshBeta34KeyboardLabels),DispatcherPriority.ContextIdle);

    static string Beta34CleanHeader(object? value)
    {
        var text=(value?.ToString()??"").Replace("_","").Replace("&&","&").Trim();
        while(text.EndsWith("…",StringComparison.Ordinal) || text.EndsWith("...",StringComparison.Ordinal))
            text=text.EndsWith("…",StringComparison.Ordinal)?text[..^1].TrimEnd():text[..^3].TrimEnd();
        var beta=text.IndexOf(" — Beta",StringComparison.OrdinalIgnoreCase);
        if(beta>=0)text=text[..beta].TrimEnd();
        return text;
    }

    MenuItem? FindBeta34MenuItem(params string[] path)
    {
        ItemCollection items=MainMenu.Items;
        MenuItem? current=null;
        foreach(var segment in path)
        {
            current=items.OfType<MenuItem>().FirstOrDefault(x=>
                string.Equals(Beta34CleanHeader(x.Header),segment,StringComparison.OrdinalIgnoreCase));
            if(current==null)return null;
            items=current.Items;
        }
        return current;
    }

    void RefreshBeta34KeyboardLabels()
    {
        foreach(var entry in CtrlZedKeyboardCatalogue.All.Where(x=>!string.IsNullOrWhiteSpace(x.MenuPath)))
        {
            var item=FindBeta34MenuItem(entry.MenuPath!.Split('/',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries));
            if(item!=null)item.InputGestureText=entry.For(Beta34KeyboardProfile);
        }
    }

    void Beta34KeyboardShortcuts_Click(object sender,RoutedEventArgs e)
    {
        var current=Beta34KeyboardProfile;
        var profileName=current switch
        {
            CtrlZedKeyboardProfile.WordPerfect51=>"WordPerfect 5.1",
            CtrlZedKeyboardProfile.WordStar40=>"WordStar 4.0",
            _=>"Microsoft Word"
        };
        var rows=CtrlZedKeyboardCatalogue.All
            .Select(x=>new{Shortcut=x.For(current),x.Command,x.Description})
            .Where(x=>!string.IsNullOrWhiteSpace(x.Shortcut))
            .OrderBy(x=>x.Shortcut,StringComparer.OrdinalIgnoreCase)
            .ToList();

        var w=new Window
        {
            Title=$"CtrlZed Keyboard Shortcuts — {profileName}",Owner=this,Width=760,Height=570,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        var root=new DockPanel{Margin=new Thickness(14)};
        var intro=new TextBlock
        {
            Text=$"Active profile: {profileName}. Menu labels and this list are generated from the same command catalogue.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),FontWeight=FontWeights.SemiBold
        };
        DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);

        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var copy=new Button{Content="Copy All",Width=90,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4),IsDefault=true};
        copy.Click+=(_,__)=> 
        {
            try
            {
                Clipboard.SetText(profileName+Environment.NewLine+string.Join(Environment.NewLine,rows.Select(x=>$"{x.Shortcut}\t{x.Command}\t{x.Description}")));
                Status.Text="Keyboard shortcut list copied.";
            }
            catch(Exception ex){Status.Text="Could not copy keyboard shortcuts: "+ex.Message;}
        };
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(copy);buttons.Children.Add(close);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);

        var list=new ListView{ItemsSource=rows};
        var view=new GridView();
        view.Columns.Add(new GridViewColumn{Header="Shortcut",DisplayMemberBinding=new System.Windows.Data.Binding("Shortcut"),Width=130});
        view.Columns.Add(new GridViewColumn{Header="Command",DisplayMemberBinding=new System.Windows.Data.Binding("Command"),Width=190});
        view.Columns.Add(new GridViewColumn{Header="Description",DisplayMemberBinding=new System.Windows.Data.Binding("Description"),Width=370});
        list.View=view;root.Children.Add(list);
        w.Content=root;w.ShowDialog();
    }
}

internal static class Beta34KeyboardCatalogueBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(new Action(window.InstallBeta34KeyboardCatalogue),DispatcherPriority.ApplicationIdle);
    }
}
