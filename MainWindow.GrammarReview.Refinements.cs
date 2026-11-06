using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    internal void EnhanceBeta33GrammarReviewWindow(Window window)
    {
        if(window.Content is not Grid root || grammarReviewList==null)return;
        var oldPanel=root.Children.OfType<WrapPanel>()
            .FirstOrDefault(x=>Grid.GetRow(x)==1);
        if(oldPanel==null)return;

        var existingButtons=oldPanel.Children.OfType<Button>().ToList();
        Button? FindButton(string caption)=>existingButtons.FirstOrDefault(x=>
            string.Equals(x.Content?.ToString(),caption,StringComparison.OrdinalIgnoreCase));

        var refresh=FindButton("Refresh");
        var aiReview=FindButton("AI Review Paragraph");
        var ignoreRule=FindButton("Ignore Rule");
        var ignore=FindButton("Ignore");
        var apply=FindButton("Apply");
        var close=FindButton("Close");
        if(refresh==null || aiReview==null || ignoreRule==null || ignore==null || apply==null || close==null)return;

        oldPanel.Children.Clear();
        root.Children.Remove(oldPanel);

        var actionStatus=new TextBlock
        {
            Text="Ready. Select a suggestion to review or apply.",
            TextWrapping=TextWrapping.Wrap,
            VerticalAlignment=VerticalAlignment.Center,
            Foreground=Brushes.DimGray,
            MinWidth=220,
            Margin=new Thickness(2,2,18,2)
        };
        var aiSuggest=new Button{Content="AI Suggest",Width=95,Margin=new Thickness(4)};

        var actionGrid=new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition());
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        actionGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        actionGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        Grid.SetColumn(actionStatus,0);
        Grid.SetRow(actionStatus,0);
        Grid.SetRowSpan(actionStatus,2);
        actionGrid.Children.Add(actionStatus);

        var firstRow=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        firstRow.Children.Add(refresh);
        firstRow.Children.Add(aiReview);
        firstRow.Children.Add(aiSuggest);
        Grid.SetColumn(firstRow,1);
        Grid.SetRow(firstRow,0);
        actionGrid.Children.Add(firstRow);

        var secondRow=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        secondRow.Children.Add(ignoreRule);
        secondRow.Children.Add(ignore);
        secondRow.Children.Add(apply);
        secondRow.Children.Add(close);
        Grid.SetColumn(secondRow,1);
        Grid.SetRow(secondRow,1);
        actionGrid.Children.Add(secondRow);

        var actionBox=new GroupBox
        {
            Header="Review actions",
            Content=actionGrid,
            Padding=new Thickness(8),
            Margin=new Thickness(0,12,0,0),
            BorderBrush=SystemColors.ControlDarkBrush,
            BorderThickness=new Thickness(1)
        };
        Grid.SetColumn(actionBox,0);
        Grid.SetColumnSpan(actionBox,2);
        Grid.SetRow(actionBox,1);
        root.Children.Add(actionBox);

        var actionBusy=false;
        void SetMessage(string message)
        {
            actionStatus.Text=message;
            Status.Text=message;
        }

        void UpdateButtons()
        {
            var selected=grammarReviewList.SelectedItem as GrammarIssueView;
            refresh.IsEnabled=!actionBusy;
            aiReview.IsEnabled=!actionBusy && selected!=null;
            aiSuggest.IsEnabled=!actionBusy && selected!=null;
            ignoreRule.IsEnabled=!actionBusy && selected!=null;
            ignore.IsEnabled=!actionBusy && selected!=null;
            apply.IsEnabled=!actionBusy && selected!=null && !string.IsNullOrWhiteSpace(selected.Finding.Replacement);
        }

        grammarReviewList.SelectionChanged+=(_,__)=>
        {
            UpdateButtons();
            if(!actionBusy && grammarReviewList.SelectedItem is GrammarIssueView selected)
                actionStatus.Text=$"Selected: {selected.Finding.Title}.";
        };

        refresh.Click+=(_,__)=>
        {
            if(actionBusy)return;
            SetMessage("Grammar and style suggestions refreshed.");
            UpdateButtons();
        };

        apply.Click+=async (_,__)=>
        {
            if(actionBusy)return;
            actionBusy=true;
            UpdateButtons();
            SetMessage("Suggestion applied. Ctrl+Z will undo it. Controls are paused briefly to prevent an accidental second application.");
            await Task.Delay(700);
            actionBusy=false;
            UpdateButtons();
        };

        ignore.Click+=async (_,__)=>
        {
            if(actionBusy)return;
            actionBusy=true;
            UpdateButtons();
            SetMessage("Suggestion ignored for the current review.");
            await Task.Delay(350);
            actionBusy=false;
            UpdateButtons();
        };

        ignoreRule.Click+=async (_,__)=>
        {
            if(actionBusy)return;
            actionBusy=true;
            UpdateButtons();
            SetMessage("This grammar rule is ignored for the current CtrlZed session.");
            await Task.Delay(350);
            actionBusy=false;
            UpdateButtons();
        };

        aiReview.Click+=(_,__)=>
        {
            if(!actionBusy)SetMessage("AI paragraph review requested. Suggestions will be added to the list when the review completes.");
        };

        aiSuggest.Click+=async (_,__)=>
        {
            if(actionBusy || grammarReviewList.SelectedItem is not GrammarIssueView issue)return;
            if(!languageSettings.AllowAiLanguageServices)
            {
                SetMessage("AI language services are disabled. Enable them under File → Settings → Grammar & Style.");
                return;
            }

            actionBusy=true;
            UpdateButtons();
            var provider=AiManager.GetDefaultProvider();
            SetMessage($"Asking {provider} to draft a replacement for the selected passage…");

            try
            {
                var paragraphText=new TextRange(issue.Paragraph.ContentStart,issue.Paragraph.ContentEnd)
                    .Text.TrimEnd('\r','\n');
                var replacement=await GrammarReplacementAi.SuggestReplacementAsync(
                    paragraphText,
                    issue.Finding,
                    WritingModeProfiles.Get(languageSettings.WritingMode));

                if(string.IsNullOrWhiteSpace(replacement))
                {
                    SetMessage("AI did not find a sufficiently reliable replacement. The original advisory suggestion remains unchanged.");
                    return;
                }

                var updatedFinding=issue.Finding with
                {
                    Replacement=replacement,
                    FromAi=true,
                    Explanation=issue.Finding.Explanation+" AI has drafted replacement wording; review it before applying."
                };
                var updatedIssue=new GrammarIssueView
                {
                    Paragraph=issue.Paragraph,
                    Finding=updatedFinding,
                    Start=issue.Start,
                    End=issue.End
                };
                var index=grammarIssues.IndexOf(issue);
                if(index>=0)grammarIssues[index]=updatedIssue;
                RefreshGrammarReviewWindow();
                grammarReviewList.SelectedItem=updatedIssue;
                SetMessage("AI replacement ready. Review the wording, then press Apply if it is suitable.");
            }
            catch(Exception ex)
            {
                SetMessage("AI Suggest failed: "+ex.Message);
            }
            finally
            {
                actionBusy=false;
                UpdateButtons();
            }
        };

        UpdateButtons();
    }
}

internal static class Beta33GrammarReviewRefinementBootstrap
{
    static readonly HashSet<Window> EnhancedWindows=new();

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
           !string.Equals(window.Title,"Grammar and Style Review",StringComparison.Ordinal) ||
           !EnhancedWindows.Add(window))return;

        window.Closed+=(_,__)=>EnhancedWindows.Remove(window);
        owner.EnhanceBeta33GrammarReviewWindow(window);
    }
}
