using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal void ApplyBeta32MenuPolish()
    {
        if(MainMenu==null)return;

        static void Polish(ItemCollection items)
        {
            foreach(var entry in items)
            {
                if(entry is not MenuItem item)continue;
                if(item.Header is string caption && caption.Contains("&&",StringComparison.Ordinal))
                    item.Header=caption.Replace("&&","&",StringComparison.Ordinal);
                Polish(item.Items);
            }
        }

        Polish(MainMenu.Items);
    }
}

internal static class Beta32UiPolishBootstrap
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
        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyBeta32MenuPolish),
            DispatcherPriority.ApplicationIdle);
    }
}
