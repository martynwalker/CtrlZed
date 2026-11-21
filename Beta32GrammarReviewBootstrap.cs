using System.Runtime.CompilerServices;
using System.Windows;

namespace CtrlZed;

internal static class Beta32GrammarReviewBootstrap
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
        if(sender is MainWindow window)window.InitialiseBeta32GrammarReview();
    }
}
