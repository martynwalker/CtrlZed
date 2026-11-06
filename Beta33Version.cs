using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal void ApplyBeta33VersionIdentity()
    {
        AboutMenu.Click-=About_Click;
        AboutMenu.Click-=Beta32About_Click;
        AboutMenu.Click-=Beta33About_Click;
        AboutMenu.Click+=Beta33About_Click;
        if(Status.Text.StartsWith("Ready — CtrlZed beta",StringComparison.OrdinalIgnoreCase))
            Status.Text="Ready — CtrlZed beta 33";
        if(Title.StartsWith("CtrlZed — beta",StringComparison.OrdinalIgnoreCase))
            Title="CtrlZed — beta 33";
    }

    void Beta33About_Click(object sender,RoutedEventArgs e)
        =>Branding.CreateAboutWindow(this,"0.33.0 beta 33").ShowDialog();
}

internal static class Beta33VersionBootstrap
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
        window.Dispatcher.BeginInvoke(new Action(window.ApplyBeta33VersionIdentity),DispatcherPriority.ApplicationIdle);
    }
}
