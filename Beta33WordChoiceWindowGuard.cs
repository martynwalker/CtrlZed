using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    internal bool ActivateExistingWordChoiceWindow()
    {
        var existing=Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w=>ReferenceEquals(w.Owner,this) &&
                (w.Title.StartsWith("Contextual Thesaurus",StringComparison.Ordinal) ||
                 string.Equals(w.Title,"Poetry Rhyme Assistant",StringComparison.Ordinal)));
        if(existing==null)return false;
        if(existing.WindowState==WindowState.Minimized)existing.WindowState=WindowState.Normal;
        existing.Activate();
        existing.Focus();
        Status.Text="Finish or close the current word-choice window before opening another.";
        return true;
    }
}

internal static class Beta33WordChoiceWindowGuard
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.ClickEvent,
            new RoutedEventHandler(OnMenuClick),
            true);
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown),
            true);
    }

    static void OnMenuClick(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem item || item.Tag is not string tag ||
           tag is not ("CtrlZedContextualThesaurus" or "CtrlZedPoetryRhyme"))return;
        if(Application.Current?.MainWindow is MainWindow owner && owner.ActivateExistingWordChoiceWindow())
            e.Handled=true;
    }

    static void OnPreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Handled || sender is not MainWindow owner ||
           Keyboard.Modifiers!=ModifierKeys.Shift || e.Key!=Key.F7)return;
        if(owner.ActivateExistingWordChoiceWindow())e.Handled=true;
    }
}
