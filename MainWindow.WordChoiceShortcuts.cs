using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    bool beta33WordChoiceShortcutInstalled;

    internal void InstallBeta33WordChoiceShortcut()
    {
        if(beta33WordChoiceShortcutInstalled)return;
        beta33WordChoiceShortcutInstalled=true;
        AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(Beta33WordChoicePreviewKeyDown),true);

        var review=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review==null)return;
        void Refresh()
        {
            var thesaurus=review.Items.OfType<MenuItem>()
                .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedContextualThesaurus" ||
                    (x.Header?.ToString()??"").Contains("Contextual Thesaurus",StringComparison.OrdinalIgnoreCase));
            if(thesaurus!=null)thesaurus.InputGestureText=profile==KeyboardProfile.Word?"Shift+F7":"";
        }
        review.SubmenuOpened+=(_,__)=>Refresh();
        Refresh();
    }

    void Beta33WordChoicePreviewKeyDown(object sender,KeyEventArgs e)
    {
        if(profile!=KeyboardProfile.Word || Keyboard.Modifiers!=ModifierKeys.Shift || e.Key!=Key.F7)return;
        ContextualThesaurus_Click(this,new RoutedEventArgs());
        e.Handled=true;
    }
}

internal static class Beta33WordChoiceShortcutBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
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
        window.Dispatcher.BeginInvoke(new Action(window.InstallBeta33WordChoiceShortcut),DispatcherPriority.ApplicationIdle);
    }
}
