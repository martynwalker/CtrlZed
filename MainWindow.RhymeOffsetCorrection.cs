using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    bool correctingRhymeOffsets;

    internal void InstallBeta33RhymeOffsetCorrection()
    {
        if(rhymeReviewList==null)return;
        rhymeReviewList.ItemContainerGenerator.StatusChanged+=(_,__)=>
        {
            if(rhymeReviewList.ItemContainerGenerator.Status!=GeneratorStatus.ContainersGenerated)return;
            Dispatcher.BeginInvoke(new Action(CorrectRhymeIssueOffsets),DispatcherPriority.Background);
        };
    }

    void CorrectRhymeIssueOffsets()
    {
        if(correctingRhymeOffsets || rhymeIssues.Count==0)return;
        correctingRhymeOffsets=true;
        try
        {
            var selected=(rhymeReviewList?.SelectedItem as RhymeIssueView)?.Suggestion;
            var changed=false;
            for(var i=0;i<rhymeIssues.Count;i++)
            {
                var issue=rhymeIssues[i];
                string current;
                try{current=new TextRange(issue.Start,issue.End).Text;}
                catch{current="";}
                if(string.Equals(current,issue.Suggestion.Original,StringComparison.Ordinal))continue;

                var raw=new TextRange(issue.Paragraph.ContentStart,issue.Paragraph.ContentEnd).Text.TrimEnd('\r','\n');
                var offset=raw.IndexOf(issue.Suggestion.Original,StringComparison.Ordinal);
                if(offset<0)continue;
                var start=PointerAtTextOffset(issue.Paragraph.ContentStart,issue.Paragraph.ContentEnd,offset);
                var end=PointerAtTextOffset(issue.Paragraph.ContentStart,issue.Paragraph.ContentEnd,offset+issue.Suggestion.Original.Length);
                if(start==null || end==null || start.CompareTo(end)>=0)continue;

                rhymeIssues[i]=new RhymeIssueView
                {
                    Paragraph=issue.Paragraph,
                    Start=start,
                    End=end,
                    Suggestion=issue.Suggestion
                };
                changed=true;
            }

            if(!changed)return;
            if(rhymeReviewList!=null)
            {
                rhymeReviewList.ItemsSource=null;
                rhymeReviewList.ItemsSource=rhymeIssues.ToList();
                if(selected!=null)
                    rhymeReviewList.SelectedItem=rhymeIssues.FirstOrDefault(x=>ReferenceEquals(x.Suggestion,selected) || x.Suggestion==selected);
                if(rhymeReviewList.SelectedIndex<0 && rhymeIssues.Count>0)rhymeReviewList.SelectedIndex=0;
            }
            RefreshRhymeMarks();
        }
        finally{correctingRhymeOffsets=false;}
    }
}

internal static class Beta33RhymeOffsetCorrectionBootstrap
{
    static readonly HashSet<Window> WiredWindows=new();

    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not Window window ||
           window.Owner is not MainWindow owner ||
           !string.Equals(window.Title,"Poetry Rhyme Assistant",StringComparison.Ordinal) ||
           !WiredWindows.Add(window))return;
        window.Closed+=(_,__)=>WiredWindows.Remove(window);
        owner.InstallBeta33RhymeOffsetCorrection();
    }
}
