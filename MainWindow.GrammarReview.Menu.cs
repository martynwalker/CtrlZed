using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CtrlZed;

public partial class MainWindow
{
    bool grammarUiInstalled;
    bool grammarMouseHandlerInstalled;
    MenuItem? grammarToggleMenu;

    void InstallGrammarUi()
    {
        if(grammarUiInstalled)
        {
            if(grammarToggleMenu!=null)
                grammarToggleMenu.IsChecked=languageSettings.GrammarAndStyleSuggestions;
            UpdateGrammarStatus();
            return;
        }
        grammarUiInstalled=true;

        if(!grammarMouseHandlerInstalled)
        {
            grammarMouseHandlerInstalled=true;
            AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(GrammarPreviewMouseDown),true);
        }

        var review=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review!=null)
        {
            grammarToggleMenu=review.Items.OfType<MenuItem>()
                .FirstOrDefault(x=>(x.Header?.ToString()??"").Contains("Grammar",StringComparison.OrdinalIgnoreCase));
            if(grammarToggleMenu!=null)
            {
                grammarToggleMenu.Header="Grammar & _Style Suggestions";
                grammarToggleMenu.IsEnabled=true;
                grammarToggleMenu.IsCheckable=true;
                grammarToggleMenu.IsChecked=languageSettings.GrammarAndStyleSuggestions;
                grammarToggleMenu.Tag="CtrlZedGrammarToggle";
                grammarToggleMenu.Click+=GrammarToggle_Click;

                var index=review.Items.IndexOf(grammarToggleMenu)+1;
                var reviewDocument=new MenuItem{Header="_Review Document…",Tag="CtrlZedGrammarReviewDocument"};
                reviewDocument.Click+=GrammarReviewDocument_Click;
                review.Items.Insert(index++,reviewDocument);

                var reviewParagraph=new MenuItem{Header="Review Current _Paragraph with AI…",Tag="CtrlZedGrammarReviewParagraph"};
                reviewParagraph.Click+=GrammarReviewParagraphWithAi_Click;
                review.Items.Insert(index++,reviewParagraph);

                var clear=new MenuItem{Header="_Clear Grammar Marks",Tag="CtrlZedGrammarClear"};
                clear.Click+=(_,__)=>ClearGrammarIssues("Grammar marks cleared.");
                review.Items.Insert(index,clear);
            }

            review.SubmenuClosed+=(_,__)=>
            {
                if(languageSettings.GrammarAndStyleSuggestions)ScheduleGrammarReview(true);
            };
        }

        var fileMenu=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"File",StringComparison.OrdinalIgnoreCase));
        var settings=fileMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>NormalisedMenuHeader(x.Header).StartsWith("Settings",StringComparison.OrdinalIgnoreCase));
        if(settings!=null)
        {
            var item=new MenuItem{Header="Grammar & _Style…",Tag="CtrlZedGrammarSettings"};
            item.Click+=GrammarSettings_Click;
            if(settings.Items.Count>0)settings.Items.Add(item);
            else if(fileMenu!=null)
            {
                var index=fileMenu.Items.IndexOf(settings);
                fileMenu.Items.Insert(index+1,item);
            }
        }

        var help=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"Help",StringComparison.OrdinalIgnoreCase));
        if(help!=null)
        {
            var item=new MenuItem{Header="Language _Assistance…",Tag="CtrlZedLanguageHelp"};
            item.Click+=(_,__)=>ShowLanguageAssistanceHelp();
            var comingSoonIndex=help.Items.OfType<object>().ToList()
                .FindIndex(x=>x is MenuItem m && NormalisedMenuHeader(m.Header).StartsWith("Coming Soon",StringComparison.OrdinalIgnoreCase));
            help.Items.Insert(comingSoonIndex>=0?comingSoonIndex:Math.Max(0,help.Items.Count-1),item);
        }

        if(Content is DockPanel root)
        {
            var statusBar=root.Children.OfType<StatusBar>().FirstOrDefault();
            if(statusBar!=null && grammarStatus==null)
            {
                statusBar.Items.Add(new Separator());
                grammarStatus=new TextBlock();
                statusBar.Items.Add(new StatusBarItem{Content=grammarStatus});
            }
        }
        UpdateGrammarStatus();
    }

    void GrammarToggle_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem item)return;
        languageSettings.GrammarAndStyleSuggestions=item.IsChecked;
        LanguageIntelligenceSettingsStore.Save(languageSettings);
        if(languageSettings.GrammarAndStyleSuggestions)
        {
            ScheduleGrammarReview(true);
            Status.Text="Grammar and style suggestions enabled. Changes remain suggestions only.";
        }
        else ClearGrammarIssues("Grammar and style suggestions disabled.");
        UpdateGrammarStatus();
    }

    void GrammarReviewDocument_Click(object? sender,RoutedEventArgs e)
    {
        if(!languageSettings.GrammarAndStyleSuggestions)
        {
            languageSettings.GrammarAndStyleSuggestions=true;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            if(grammarToggleMenu!=null)grammarToggleMenu.IsChecked=true;
        }
        RunLocalGrammarReview();
        ShowGrammarReviewWindow();
    }

    async void GrammarReviewParagraphWithAi_Click(object? sender,RoutedEventArgs e)
    {
        var paragraph=FindContainingParagraph(Editor.CaretPosition);
        if(paragraph==null)
        {
            Status.Text="Place the caret in a paragraph first.";
            return;
        }
        await AddAiGrammarForParagraphAsync(paragraph);
        ShowGrammarReviewWindow();
    }

    void GrammarPreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.Handled || e.ChangedButton!=MouseButton.Right || !Editor.IsMouseOver || grammarIssues.Count==0)return;
        var pointer=Editor.GetPositionFromPoint(e.GetPosition(Editor),true);
        if(pointer==null)return;
        var issue=grammarIssues.FirstOrDefault(x=>x.Start.CompareTo(pointer)<=0 && x.End.CompareTo(pointer)>=0);
        if(issue==null)return;

        e.Handled=true;
        var menu=new ContextMenu();
        if(!string.IsNullOrWhiteSpace(issue.Finding.Replacement))
        {
            var apply=new MenuItem{Header=issue.Finding.Replacement,FontWeight=FontWeights.SemiBold};
            apply.Click+=(_,__)=>ApplyGrammarIssue(issue);
            menu.Items.Add(apply);
        }
        menu.Items.Add(new MenuItem{Header=issue.Finding.Title+": "+issue.Finding.Explanation,IsEnabled=false});
        menu.Items.Add(new Separator());

        var ignore=new MenuItem{Header="Ignore This Suggestion"};
        ignore.Click+=(_,__)=>IgnoreGrammarIssue(issue);
        var ignoreRule=new MenuItem{Header="Ignore This Rule for This Session"};
        ignoreRule.Click+=(_,__)=>IgnoreGrammarRule(issue.Finding.RuleId);
        var ai=new MenuItem{Header="Ask AI to Review This Paragraph…"};
        ai.Click+=async (_,__)=>await AddAiGrammarForParagraphAsync(issue.Paragraph);
        menu.Items.Add(ignore);
        menu.Items.Add(ignoreRule);
        menu.Items.Add(ai);
        menu.Items.Add(new Separator());

        AddMacroSubmenu(menu);
        if(menu.Items.Count>0)menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem{Header="Cut",Command=ApplicationCommands.Cut,CommandTarget=Editor,InputGestureText="Ctrl+X"});
        menu.Items.Add(new MenuItem{Header="Copy",Command=ApplicationCommands.Copy,CommandTarget=Editor,InputGestureText="Ctrl+C"});
        menu.Items.Add(new MenuItem{Header="Paste",Command=ApplicationCommands.Paste,CommandTarget=Editor,InputGestureText="Ctrl+V"});
        menu.PlacementTarget=Editor;
        menu.IsOpen=true;
    }
}
