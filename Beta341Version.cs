using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal static bool IsBeta341Assembly()
    {
        var value=typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion??"";
        return value.StartsWith("0.34.1",StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyBeta341VersionIdentity()
    {
        if(!IsBeta341Assembly())return;

        AboutMenu.Click-=About_Click;
        AboutMenu.Click-=Beta32About_Click;
        AboutMenu.Click-=Beta33About_Click;
        AboutMenu.Click-=Beta34About_Click;
        AboutMenu.Click-=Beta341About_Click;
        AboutMenu.Click+=Beta341About_Click;

        if(Status.Text.StartsWith("Ready — CtrlZed beta",StringComparison.OrdinalIgnoreCase))
            Status.Text="Ready — CtrlZed beta 34.1";
        if(Title.StartsWith("CtrlZed — beta",StringComparison.OrdinalIgnoreCase))
            Title="CtrlZed — beta 34.1";
    }

    void Beta341About_Click(object sender,RoutedEventArgs e)
        =>Branding.CreateAboutWindow(this,"0.34.1 beta 34.1").ShowDialog();
}

internal static class Beta341VersionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window || !MainWindow.IsBeta341Assembly())return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyBeta341VersionIdentity),DispatcherPriority.ApplicationIdle);
    }
}
