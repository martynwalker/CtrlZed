using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal static bool IsBeta342Assembly()
    {
        var value=typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion??"";
        return value.StartsWith("0.34.2",StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyBeta342VersionIdentity()
    {
        if(!IsBeta342Assembly())return;

        AboutMenu.Click-=About_Click;
        AboutMenu.Click-=Beta32About_Click;
        AboutMenu.Click-=Beta33About_Click;
        AboutMenu.Click-=Beta34About_Click;
        AboutMenu.Click-=Beta341About_Click;
        AboutMenu.Click-=Beta342About_Click;
        AboutMenu.Click+=Beta342About_Click;

        if(Status.Text.StartsWith("Ready — CtrlZed beta",StringComparison.OrdinalIgnoreCase))
            Status.Text="Ready — CtrlZed beta 34.2";
        if(Title.StartsWith("CtrlZed — beta",StringComparison.OrdinalIgnoreCase))
            Title="CtrlZed — beta 34.2";
    }

    void Beta342About_Click(object sender,RoutedEventArgs e)
        =>Branding.CreateAboutWindow(this,"0.34.2 beta 34.2").ShowDialog();
}

internal static class Beta342VersionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window || !MainWindow.IsBeta342Assembly())return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyBeta342VersionIdentity),DispatcherPriority.ApplicationIdle);
    }
}
