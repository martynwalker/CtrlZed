using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal static bool IsBeta340Assembly()
    {
        var value=typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion??"";
        return value.StartsWith("0.34.0",StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyBeta34VersionIdentity()
    {
        if(!IsBeta340Assembly())return;
        Title=Title.Contains("—",StringComparison.Ordinal)
            ?Title[..Title.IndexOf('—')].TrimEnd()+" — beta 34"
            :"CtrlZed — beta 34";
        if(Status.Text.StartsWith("Ready — CtrlZed beta",StringComparison.OrdinalIgnoreCase))
            Status.Text="Ready — CtrlZed beta 34";
        AboutMenu.Click-=About_Click;
        AboutMenu.Click-=Beta32About_Click;
        AboutMenu.Click-=Beta33About_Click;
        AboutMenu.Click-=Beta34About_Click;
        AboutMenu.Click+=Beta34About_Click;
    }

    void Beta34About_Click(object sender,RoutedEventArgs e)
        =>Branding.CreateAboutWindow(this,"0.34.0 beta 34").ShowDialog();
}

internal static class Beta34VersionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window || !MainWindow.IsBeta340Assembly())return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(new Action(window.ApplyBeta34VersionIdentity),DispatcherPriority.ApplicationIdle);
    }
}
