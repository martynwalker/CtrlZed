using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    StatusBarItem? activityStatusItem;
    TextBlock? activityStatusText;
    ProgressBar? activityProgress;
    int activityDepth;

    internal void InstallActivityIndicator()
    {
        if(activityStatusItem!=null || Content is not DockPanel root)return;
        var statusBar=root.Children.OfType<StatusBar>().FirstOrDefault();
        if(statusBar==null)return;

        activityStatusText=new TextBlock
        {
            Text="Working…",
            Margin=new Thickness(0,0,8,0),
            VerticalAlignment=VerticalAlignment.Center
        };
        activityProgress=new ProgressBar
        {
            IsIndeterminate=true,
            Width=86,
            Height=9,
            Foreground=new SolidColorBrush(Color.FromRgb(55,150,85)),
            Background=new SolidColorBrush(Color.FromRgb(220,232,222)),
            VerticalAlignment=VerticalAlignment.Center
        };
        var panel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        panel.Children.Add(activityStatusText);
        panel.Children.Add(activityProgress);
        activityStatusItem=new StatusBarItem{Content=panel,Visibility=Visibility.Collapsed};
        statusBar.Items.Insert(Math.Min(2,statusBar.Items.Count),activityStatusItem);
    }

    internal void BeginActivity(string message)
    {
        if(!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(()=>BeginActivity(message)),DispatcherPriority.Normal);
            return;
        }
        InstallActivityIndicator();
        activityDepth++;
        if(activityStatusText!=null)activityStatusText.Text=message;
        if(activityStatusItem!=null)activityStatusItem.Visibility=Visibility.Visible;
        Status.Text=message;
    }

    internal void UpdateActivity(string message)
    {
        if(!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(()=>UpdateActivity(message)),DispatcherPriority.Normal);
            return;
        }
        InstallActivityIndicator();
        if(activityStatusText!=null)activityStatusText.Text=message;
        if(activityStatusItem!=null)activityStatusItem.Visibility=Visibility.Visible;
        Status.Text=message;
    }

    internal void EndActivity(string? finalMessage=null)
    {
        if(!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(()=>EndActivity(finalMessage)),DispatcherPriority.Normal);
            return;
        }
        activityDepth=Math.Max(0,activityDepth-1);
        if(activityDepth==0 && activityStatusItem!=null)
            activityStatusItem.Visibility=Visibility.Collapsed;
        if(!string.IsNullOrWhiteSpace(finalMessage))Status.Text=finalMessage;
    }
}

internal static class ActivityIndicatorBootstrap
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
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallActivityIndicator),DispatcherPriority.ApplicationIdle);
    }
}
