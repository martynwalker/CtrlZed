using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    void ShowGrammarReviewWindow()
    {
        if(grammarReviewWindow!=null)
        {
            grammarReviewWindow.Activate();
            RefreshGrammarReviewWindow();
            return;
        }

        var w=new Window
        {
            Title="Grammar and Style Review",
            Owner=this,
            Width=900,
            Height=590,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        grammarReviewWindow=w;

        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(330)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var list=new ListBox{Margin=new Thickness(0,0,12,0)};
        grammarReviewList=list;
        Grid.SetColumn(list,0);
        Grid.SetRow(list,0);
        root.Children.Add(list);

        var detail=new StackPanel();
        var heading=new TextBlock
        {
            FontSize=20,
            FontWeight=FontWeights.SemiBold,
            TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,0,0,10)
        };
        var kind=new TextBlock{Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,8)};
        var original=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var suggested=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var explanation=new TextBox
        {
            IsReadOnly=true,
            TextWrapping=TextWrapping.Wrap,
            AcceptsReturn=true,
            MinHeight=110,
            Margin=new Thickness(0,3,0,10)
        };
        detail.Children.Add(heading);
        detail.Children.Add(kind);
        detail.Children.Add(new TextBlock{Text="Original"});
        detail.Children.Add(original);
        detail.Children.Add(new TextBlock{Text="Suggested wording"});
        detail.Children.Add(suggested);
        detail.Children.Add(new TextBlock{Text="Reason"});
        detail.Children.Add(explanation);
        detail.Children.Add(new TextBlock
        {
            Text="Grammar and style findings are advisory. CtrlZed never applies them automatically.",
            Foreground=Brushes.DimGray,
            TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(0,8,0,0)
        });
        Grid.SetColumn(detail,1);
        Grid.SetRow(detail,0);
        root.Children.Add(detail);

        void RefreshDetails()
        {
            if(list.SelectedItem is not GrammarIssueView issue)
            {
                heading.Text=grammarIssues.Count==0?"No grammar or style suggestions":"Select a suggestion";
                kind.Text="";
                original.Text="";
                suggested.Text="";
                explanation.Text="";
                return;
            }

            heading.Text=issue.Finding.Title;
            kind.Text=$"{issue.Finding.Kind} · {(issue.Finding.FromAi?"AI contextual review":"local rule")} · {WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName}";
            original.Text=issue.Finding.Original;
            suggested.Text=issue.Finding.Replacement??"(No automatic wording proposed)";
            explanation.Text=issue.Finding.Explanation;
            SelectGrammarIssue(issue);
        }
        list.SelectionChanged+=(_,__)=>RefreshDetails();

        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var refresh=new Button{Content="Refresh",Width=80,Margin=new Thickness(4)};
        var ai=new Button{Content="AI Review Paragraph",Width=130,Margin=new Thickness(4)};
        var ignoreRule=new Button{Content="Ignore Rule",Width=90,Margin=new Thickness(4)};
        var ignore=new Button{Content="Ignore",Width=80,Margin=new Thickness(4)};
        var apply=new Button{Content="Apply",Width=80,Margin=new Thickness(4)};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        buttons.Children.Add(refresh);
        buttons.Children.Add(ai);
        buttons.Children.Add(ignoreRule);
        buttons.Children.Add(ignore);
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);
        Grid.SetColumnSpan(buttons,2);
        Grid.SetRow(buttons,1);
        root.Children.Add(buttons);

        refresh.Click+=(_,__)=>RunLocalGrammarReview();
        apply.Click+=(_,__)=>
        {
            if(list.SelectedItem is GrammarIssueView issue)ApplyGrammarIssue(issue);
        };
        ignore.Click+=(_,__)=>
        {
            if(list.SelectedItem is GrammarIssueView issue)IgnoreGrammarIssue(issue);
        };
        ignoreRule.Click+=(_,__)=>
        {
            if(list.SelectedItem is GrammarIssueView issue)IgnoreGrammarRule(issue.Finding.RuleId);
        };
        ai.Click+=async (_,__)=>
        {
            var paragraph=list.SelectedItem is GrammarIssueView issue
                ?issue.Paragraph
                :FindContainingParagraph(Editor.CaretPosition);
            if(paragraph!=null)await AddAiGrammarForParagraphAsync(paragraph);
        };
        close.Click+=(_,__)=>w.Close();
        w.Closed+=(_,__)=>
        {
            grammarReviewWindow=null;
            grammarReviewList=null;
        };

        w.Content=root;
        RefreshGrammarReviewWindow();
        RefreshDetails();
        w.Show();
    }

    void RefreshGrammarReviewWindow()
    {
        if(grammarReviewList==null)return;
        var selected=grammarReviewList.SelectedItem as GrammarIssueView;
        grammarReviewList.ItemsSource=null;
        grammarReviewList.ItemsSource=grammarIssues.ToList();
        if(selected!=null && grammarIssues.Contains(selected))grammarReviewList.SelectedItem=selected;
        else if(grammarIssues.Count>0)grammarReviewList.SelectedIndex=0;
    }

    void GrammarSettings_Click(object? sender,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Grammar and Style Settings",
            Owner=this,
            Width=620,
            Height=390,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock
        {
            Text="Grammar and style",
            FontSize=21,
            FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,0,0,10)
        });
        var enabled=new CheckBox
        {
            Content="Show thin blue grammar and style suggestion marks",
            IsChecked=languageSettings.GrammarAndStyleSuggestions,
            Margin=new Thickness(0,4,0,6)
        };
        var ai=new CheckBox
        {
            Content="Allow AI contextual review when I explicitly request it",
            IsChecked=languageSettings.AllowAiLanguageServices,
            Margin=new Thickness(0,4,0,6)
        };
        panel.Children.Add(enabled);
        panel.Children.Add(ai);
        panel.Children.Add(new TextBlock
        {
            Text="Local checks run after a short pause. AI review is paragraph-scoped and only runs when requested from the Review menu or review pane. Grammar and style suggestions are never applied automatically.",
            TextWrapping=TextWrapping.Wrap,
            Foreground=Brushes.DimGray,
            Margin=new Thickness(0,4,0,16)
        });
        panel.Children.Add(new Separator());

        var mode=WritingModeProfiles.Get(languageSettings.WritingMode);
        panel.Children.Add(new TextBlock
        {
            Text="Current writing mode: "+mode.DisplayName,
            FontWeight=FontWeights.SemiBold,
            Margin=new Thickness(0,14,0,4)
        });
        panel.Children.Add(new TextBlock{Text=mode.Description,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
        panel.Children.Add(new TextBlock
        {
            Text="Change the writing mode under Review → Writing Mode or File → Settings → Language & Writing Mode.",
            TextWrapping=TextWrapping.Wrap,
            Foreground=Brushes.DimGray,
            Margin=new Thickness(0,4,0,12)
        });

        var buttons=new StackPanel
        {
            Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right,
            Margin=new Thickness(0,18,0,0)
        };
        var apply=new Button{Content="Apply",Width=85,Margin=new Thickness(4),IsDefault=true};
        var close=new Button{Content="Close",Width=85,Margin=new Thickness(4),IsCancel=true};
        apply.Click+=(_,__)=>
        {
            languageSettings.GrammarAndStyleSuggestions=enabled.IsChecked==true;
            languageSettings.AllowAiLanguageServices=ai.IsChecked==true;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            if(languageSettings.GrammarAndStyleSuggestions)RunLocalGrammarReview();
            else ClearGrammarIssues();
            InstallGrammarUi();
            Status.Text="Grammar and style settings applied.";
        };
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        w.Content=panel;
        w.ShowDialog();
    }

    void ShowLanguageAssistanceHelp()
    {
        var w=new Window
        {
            Title="CtrlZed Language Assistance",
            Owner=this,
            Width=720,
            Height=580,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var text=new TextBlock
        {
            Text="""
SPELLING

Red squiggly marks are conventional local spelling checks. Right-click a marked word for suggestions, Ignore All, or Add to Personal Dictionary.

CONTEXTUAL SPELLING

Optional contextual correction examines a completed sentence after its punctuation and following space. It changes only high-confidence cases and the change remains immediately undoable.

Context does not make every unrecognisable word understandable. A fragment such as ‘berr’ may have several possible intentions, or none that can be established reliably. In that situation CtrlZed does not guess. The original word remains in place with its red spelling mark so that the writer can decide.

GRAMMAR AND STYLE

Thin blue marks are suggestions, not corrections. Right-click for the proposed wording and explanation, or use Review → Review Document. The selected writing mode changes the tolerance and priorities of the review. Grammar and style suggestions are never applied automatically.

AI REVIEW

AI review is optional and paragraph-scoped. It runs only when explicitly requested. CtrlZed asks for small, exact suggestions rather than a wholesale rewrite, and every suggestion still requires the writer's decision.
""",
            TextWrapping=TextWrapping.Wrap,
            Margin=new Thickness(22),
            FontSize=14
        };
        var close=new Button
        {
            Content="Close",
            Width=90,
            HorizontalAlignment=HorizontalAlignment.Right,
            Margin=new Thickness(0,10,16,16)
        };
        close.Click+=(_,__)=>w.Close();
        var root=new DockPanel();
        DockPanel.SetDock(close,Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        w.Content=root;
        w.ShowDialog();
    }
}
