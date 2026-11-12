using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal static bool IsBeta39Assembly()
    {
        var value = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        return value.StartsWith("0.39.0", StringComparison.OrdinalIgnoreCase);
    }

    internal void ApplyBeta39VersionIdentity()
    {
        if (!IsBeta39Assembly()) return;
        AboutMenu.Click -= About_Click;
        AboutMenu.Click -= Beta32About_Click;
        AboutMenu.Click -= Beta33About_Click;
        AboutMenu.Click -= Beta34About_Click;
        AboutMenu.Click -= Beta341About_Click;
        AboutMenu.Click -= Beta342About_Click;
        AboutMenu.Click -= Beta35About_Click;
        AboutMenu.Click -= Beta36About_Click;
        AboutMenu.Click -= Beta38About_Click;
        AboutMenu.Click += Beta39About_Click;
        if (Status.Text.StartsWith("Ready — CtrlZed beta", StringComparison.OrdinalIgnoreCase)) Status.Text = "Ready — CtrlZed beta 39";
        if (Title.StartsWith("CtrlZed — beta", StringComparison.OrdinalIgnoreCase)) Title = "CtrlZed — beta 39";
    }

    void Beta39About_Click(object sender, RoutedEventArgs e)
        => Branding.CreateAboutWindow(this, "0.39.0 beta 39").ShowDialog();
}

internal static class Beta39VersionBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !MainWindow.IsBeta39Assembly()) return;
        window.ContentRendered -= OnContentRendered;
        window.ContentRendered += OnContentRendered;
    }

    static void OnContentRendered(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window) return;
        window.Dispatcher.BeginInvoke(new Action(window.ApplyBeta39VersionIdentity), DispatcherPriority.ApplicationIdle);
    }
}
