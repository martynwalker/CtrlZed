using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    sealed class GrammarIssueView
    {
        public required Paragraph Paragraph { get; init; }
        public required GrammarFinding Finding { get; init; }
        public required TextPointer Start { get; init; }
        public required TextPointer End { get; init; }

        public override string ToString()
        {
            var source=Finding.FromAi?"AI":"Local";
            var original=Finding.Original.Replace('\r',' ').Replace('\n',' ').Trim();
            if(original.Length>52)original=original[..49]+"…";
            return $"{Finding.Kind} · {source} — {Finding.Title}: {original}";
        }
    }

    readonly List<GrammarIssueView> grammarIssues=new();
    readonly HashSet<string> ignoredGrammarRules=new(StringComparer.OrdinalIgnoreCase);
    GrammarSquiggleAdorner? grammarAdorner;
    DispatcherTimer? grammarReviewTimer;
    Window? grammarReviewWindow;
    ListBox? grammarReviewList;
    TextBlock? grammarStatus;
    CancellationTokenSource? grammarAiCancellation;
    bool grammarReviewInitialised;
    bool grammarAnalysisRunning;

    internal void InitialiseBeta32GrammarReview()
    {
        if(grammarReviewInitialised)return;
        grammarReviewInitialised=true;

        grammarReviewTimer=new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval=TimeSpan.FromMilliseconds(900)
        };
        grammarReviewTimer.Tick+=GrammarReviewTimer_Tick;
        Editor.TextChanged+=GrammarEditor_TextChanged;
        Editor.LayoutUpdated+=(_,__)=>grammarAdorner?.InvalidateVisual();
        LanguageProfileBridge.ReloadRequested+=GrammarProfileReloaded;
        Closed+=(_,__)=>
        {
            grammarAiCancellation?.Cancel();
            LanguageProfileBridge.ReloadRequested-=GrammarProfileReloaded;
        };

        Dispatcher.BeginInvoke(new Action(() =>
        {
            InstallGrammarUi();
            EnsureGrammarAdorner();
            if(languageSettings.GrammarAndStyleSuggestions)ScheduleGrammarReview(true);
        }),DispatcherPriority.ContextIdle);
    }

    void GrammarProfileReloaded()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            InstallGrammarUi();
            if(languageSettings.GrammarAndStyleSuggestions)ScheduleGrammarReview(true);
            else ClearGrammarIssues();
        }),DispatcherPriority.Background);
    }

    void GrammarEditor_TextChanged(object sender,TextChangedEventArgs e)
    {
        if(!languageSettings.GrammarAndStyleSuggestions || grammarReviewTimer==null)return;
        grammarReviewTimer.Stop();
        grammarReviewTimer.Start();
    }

    void GrammarReviewTimer_Tick(object? sender,EventArgs e)
    {
        grammarReviewTimer?.Stop();
        RunLocalGrammarReview();
    }

    void ScheduleGrammarReview(bool immediate=false)
    {
        if(!languageSettings.GrammarAndStyleSuggestions || grammarReviewTimer==null)return;
        grammarReviewTimer.Stop();
        if(immediate)
        {
            Dispatcher.BeginInvoke(new Action(RunLocalGrammarReview),DispatcherPriority.Background);
            return;
        }
        grammarReviewTimer.Start();
    }

    void RunLocalGrammarReview()
    {
        if(grammarAnalysisRunning || !languageSettings.GrammarAndStyleSuggestions)return;
        grammarAnalysisRunning=true;
        try
        {
            grammarIssues.Clear();
            var mode=WritingModeProfiles.Get(languageSettings.WritingMode);

            foreach(var paragraph in EnumerateDocumentParagraphs())
            {
                var text=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text.TrimEnd('\r','\n');
                if(string.IsNullOrWhiteSpace(text))continue;
                var isHeading=IsGrammarHeading(paragraph,text);
                foreach(var finding in GrammarReviewEngine.Analyse(text,mode,isHeading))
                {
                    if(ignoredGrammarRules.Contains(finding.RuleId))continue;
                    AddGrammarFinding(paragraph,finding);
                }
            }

            RefreshGrammarMarks();
            RefreshGrammarReviewWindow();
            UpdateGrammarStatus();
        }
        finally{grammarAnalysisRunning=false;}
    }

    void AddGrammarFinding(Paragraph paragraph,GrammarFinding finding)
    {
        var start=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start);
        var end=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start+finding.Length);
        if(start==null || end==null || start.CompareTo(end)>=0)return;
        grammarIssues.Add(new GrammarIssueView{Paragraph=paragraph,Finding=finding,Start=start,End=end});
    }

    static bool IsGrammarHeading(Paragraph paragraph,string text)
    {
        if(text.Length>180)return false;
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        var size=range.GetPropertyValue(TextElement.FontSizeProperty) is double d?d:0;
        var bold=range.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight fw && fw>=FontWeights.SemiBold;
        return size>=17 || (bold && size>=15);
    }

    void EnsureGrammarAdorner()
    {
        if(grammarAdorner!=null)return;
        var layer=AdornerLayer.GetAdornerLayer(Editor);
        if(layer==null)
        {
            Dispatcher.BeginInvoke(new Action(EnsureGrammarAdorner),DispatcherPriority.ContextIdle);
            return;
        }
        grammarAdorner=new GrammarSquiggleAdorner(Editor);
        layer.Add(grammarAdorner);
    }

    void RefreshGrammarMarks()
    {
        EnsureGrammarAdorner();
        grammarAdorner?.SetRanges(grammarIssues
            .Select(x=>new GrammarMarkedRange(x.Start,x.End,x.Finding.Kind))
            .ToList());
    }

    void ClearGrammarIssues(string? message=null)
    {
        grammarAiCancellation?.Cancel();
        grammarIssues.Clear();
        grammarAdorner?.SetRanges(Array.Empty<GrammarMarkedRange>());
        RefreshGrammarReviewWindow();
        UpdateGrammarStatus();
        if(!string.IsNullOrWhiteSpace(message))Status.Text=message;
    }

    void UpdateGrammarStatus()
    {
        if(grammarStatus==null)return;
        grammarStatus.Text=!languageSettings.GrammarAndStyleSuggestions
            ?"Grammar: off"
            :$"Grammar: {grammarIssues.Count}";
    }

    void ApplyGrammarIssue(GrammarIssueView issue)
    {
        var replacement=issue.Finding.Replacement;
        if(string.IsNullOrWhiteSpace(replacement))
        {
            Status.Text="This suggestion requires the writer's judgement; no automatic replacement was proposed.";
            return;
        }
        try
        {
            new TextRange(issue.Start,issue.End).Text=replacement;
            MarkDirty("Grammar suggestion applied. Ctrl+Z will undo it.");
            ScheduleGrammarReview(true);
        }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"Apply grammar suggestion",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    void IgnoreGrammarIssue(GrammarIssueView issue)
    {
        grammarIssues.Remove(issue);
        RefreshGrammarMarks();
        RefreshGrammarReviewWindow();
        UpdateGrammarStatus();
        Status.Text="Grammar suggestion ignored until the text is analysed again.";
    }

    void IgnoreGrammarRule(string ruleId)
    {
        ignoredGrammarRules.Add(ruleId);
        grammarIssues.RemoveAll(x=>string.Equals(x.Finding.RuleId,ruleId,StringComparison.OrdinalIgnoreCase));
        RefreshGrammarMarks();
        RefreshGrammarReviewWindow();
        UpdateGrammarStatus();
        Status.Text="Grammar rule ignored for this CtrlZed session.";
    }

    void SelectGrammarIssue(GrammarIssueView issue)
    {
        try
        {
            Editor.Selection.Select(issue.Start,issue.End);
            issue.Paragraph.BringIntoView();
            Editor.Focus();
        }
        catch{ }
    }

    async Task AddAiGrammarForParagraphAsync(Paragraph paragraph)
    {
        if(!languageSettings.AllowAiLanguageServices)
        {
            MessageBox.Show(
                "AI language services are disabled. Enable them under File → Settings → Grammar & Style if you want contextual AI suggestions.",
                "AI grammar review",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var text=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text.TrimEnd('\r','\n');
        if(string.IsNullOrWhiteSpace(text))return;
        grammarAiCancellation?.Cancel();
        grammarAiCancellation=new CancellationTokenSource();
        Status.Text="AI is reviewing the current paragraph for suggestions…";

        try
        {
            var findings=await GrammarAiReview.AnalyseParagraphAsync(
                text,WritingModeProfiles.Get(languageSettings.WritingMode),grammarAiCancellation.Token);

            grammarIssues.RemoveAll(x=>ReferenceEquals(x.Paragraph,paragraph) && x.Finding.FromAi);
            foreach(var finding in findings)
            {
                if(ignoredGrammarRules.Contains(finding.RuleId))continue;
                var before=grammarIssues.Count;
                AddGrammarFinding(paragraph,finding);
                if(grammarIssues.Count==before)continue;
                var added=grammarIssues[^1];
                var duplicate=grammarIssues.Take(grammarIssues.Count-1).Any(x=>
                    ReferenceEquals(x.Paragraph,paragraph) &&
                    x.Start.CompareTo(added.End)<0 && x.End.CompareTo(added.Start)>0 &&
                    string.Equals(x.Finding.Replacement,added.Finding.Replacement,StringComparison.Ordinal));
                if(duplicate)grammarIssues.RemoveAt(grammarIssues.Count-1);
            }

            grammarIssues.Sort((a,b)=>a.Start.CompareTo(b.Start));
            RefreshGrammarMarks();
            RefreshGrammarReviewWindow();
            UpdateGrammarStatus();
            Status.Text=findings.Count==0
                ?"AI found no sufficiently useful contextual grammar suggestions for this paragraph."
                :$"AI added {findings.Count} contextual suggestion{(findings.Count==1?"":"s")}.";
        }
        catch(OperationCanceledException){ }
        catch(Exception ex)
        {
            MessageBox.Show(ex.Message,"AI grammar review",MessageBoxButton.OK,MessageBoxImage.Error);
            Status.Text="AI grammar review failed.";
        }
    }
}
