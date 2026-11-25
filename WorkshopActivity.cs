using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

internal enum WorkshopStatusKind { Neutral, Busy, Success, Error }

/// <summary>Visible request activity without inventing a provider completion percentage.</summary>
internal sealed class WorkshopActivity : IDisposable
{
    readonly TextBlock status;
    readonly ProgressBar progress;
    readonly TextBlock elapsed;
    readonly DispatcherTimer timer;
    readonly Stopwatch clock=new();
    bool disposed;

    internal WorkshopActivity(TextBlock status,ProgressBar progress,TextBlock elapsed)
    {
        this.status=status;this.progress=progress;this.elapsed=elapsed;
        timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=Tick;
        Stop();
    }

    internal static Brush BrushFor(WorkshopStatusKind kind)=>kind switch
    {
        WorkshopStatusKind.Busy=>Brushes.DarkBlue,
        WorkshopStatusKind.Success=>Brushes.DarkGreen,
        WorkshopStatusKind.Error=>Brushes.Firebrick,
        _=>Brushes.DimGray
    };

    internal void Set(string message,WorkshopStatusKind kind=WorkshopStatusKind.Neutral)
    {
        if(disposed)return;
        status.Text=message;status.Foreground=BrushFor(kind);
        AutomationProperties.SetItemStatus(status,kind.ToString());
    }

    internal void Start(string message)
    {
        if(disposed)return;
        clock.Restart();progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;
        elapsed.Visibility=Visibility.Visible;UpdateElapsed();Set(message,WorkshopStatusKind.Busy);timer.Start();
    }

    internal void Stop()
    {
        timer.Stop();clock.Stop();progress.IsIndeterminate=false;progress.Visibility=Visibility.Collapsed;
        elapsed.Text="";elapsed.Visibility=Visibility.Collapsed;
    }

    void Tick(object? sender,EventArgs args)=>UpdateElapsed();

    void UpdateElapsed()
    {
        var time=clock.Elapsed;
        elapsed.Text=time.TotalMinutes<1?$"Elapsed: {(int)time.TotalSeconds}s":
            $"Elapsed: {(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    public void Dispose()
    {
        if(disposed)return;
        Stop();timer.Tick-=Tick;disposed=true;
    }
}
