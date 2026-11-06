using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
        AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(GrammarPreviewMouseDown),true);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            InstallGrammarUi();
            EnsureGrammarAdorner();
            if(languageSettings.GrammarAndStyleSuggestions)ScheduleGrammarReview(true);
        }),DispatcherPriority.ContextIdle);
    }

    void InstallGrammarUi()
    {
        var review=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review!=null)
        {
            var grammarToggle=review.Items.OfType<MenuItem>()
                .FirstOrDefault(x=>(x.Header?.ToString()??"").Contains("Grammar",StringComparison.OrdinalIgnoreCase));
            if(grammarToggle!=null && (grammarToggle.Tag as string)!="CtrlZedGrammarToggle")
            {
                grammarToggle.Header="Grammar & _Style Suggestions";
                grammarToggle.IsEnabled=true;
                grammarToggle.IsCheckable=true;
                grammarToggle.IsChecked=languageSettings.GrammarAndStyleSuggestions;
                grammarToggle.Tag="CtrlZedGrammarToggle";
                grammarToggle.Click+=GrammarToggle_Click;

                var index=review.Items.IndexOf(grammarToggle)+1;
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
        if(settings!=null && !settings.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedGrammarSettings"))
        {
            var item=new MenuItem{Header="Grammar & _Style…",Tag="CtrlZedGrammarSettings"};
            item.Click+=GrammarSettings_Click;
            settings.Items.Add(item);
        }

        var help=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"Help",StringComparison.OrdinalIgnoreCase));
        if(help!=null && !help.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedLanguageHelp"))
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
                    var start=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start);
                    var end=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start+finding.Length);
                    if(start==null || end==null || start.CompareTo(end)>=0)continue;
                    grammarIssues.Add(new GrammarIssueView{Paragraph=paragraph,Finding=finding,Start=start,End=end});
                }
            }

            RefreshGrammarMarks();
            RefreshGrammarReviewWindow();
            UpdateGrammarStatus();
        }
        finally{grammarAnalysisRunning=false;}
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
        menu.Items.Add(ignore);menu.Items.Add(ignoreRule);menu.Items.Add(ai);
        menu.Items.Add(new Separator());
        AddMacroSubmenu(menu);
        if(menu.Items.Count>0)menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem{Header="Cut",Command=ApplicationCommands.Cut,CommandTarget=Editor,InputGestureText="Ctrl+X"});
        menu.Items.Add(new MenuItem{Header="Copy",Command=ApplicationCommands.Copy,CommandTarget=Editor,InputGestureText="Ctrl+C"});
        menu.Items.Add(new MenuItem{Header="Paste",Command=ApplicationCommands.Paste,CommandTarget=Editor,InputGestureText="Ctrl+V"});
        menu.PlacementTarget=Editor;
        menu.IsOpen=true;
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
        catch(Exception ex){MessageBox.Show(ex.Message,"Apply grammar suggestion",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void IgnoreGrammarIssue(GrammarIssueView issue)
    {
        grammarIssues.Remove(issue);
        RefreshGrammarMarks();RefreshGrammarReviewWindow();UpdateGrammarStatus();
        Status.Text="Grammar suggestion ignored until the text is analysed again.";
    }

    void IgnoreGrammarRule(string ruleId)
    {
        ignoredGrammarRules.Add(ruleId);
        grammarIssues.RemoveAll(x=>string.Equals(x.Finding.RuleId,ruleId,StringComparison.OrdinalIgnoreCase));
        RefreshGrammarMarks();RefreshGrammarReviewWindow();UpdateGrammarStatus();
        Status.Text="Grammar rule ignored for this CtrlZed session.";
    }

    void GrammarReviewDocument_Click(object? sender,RoutedEventArgs e)
    {
        if(!languageSettings.GrammarAndStyleSuggestions)
        {
            languageSettings.GrammarAndStyleSuggestions=true;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
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
            Title="Grammar and Style Review",Owner=this,Width=900,Height=590,
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
        Grid.SetColumn(list,0);Grid.SetRow(list,0);root.Children.Add(list);

        var detail=new StackPanel();
        var heading=new TextBlock{FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        var kind=new TextBlock{Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,8)};
        var original=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var suggested=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var explanation=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=110,Margin=new Thickness(0,3,0,10)};
        detail.Children.Add(heading);detail.Children.Add(kind);
        detail.Children.Add(new TextBlock{Text="Original"});detail.Children.Add(original);
        detail.Children.Add(new TextBlock{Text="Suggested wording"});detail.Children.Add(suggested);
        detail.Children.Add(new TextBlock{Text="Reason"});detail.Children.Add(explanation);
        detail.Children.Add(new TextBlock
        {
            Text="Grammar and style findings are advisory. CtrlZed never applies them automatically.",
            Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)
        });
        Grid.SetColumn(detail,1);Grid.SetRow(detail,0);root.Children.Add(detail);

        void RefreshDetails()
        {
            if(list.SelectedItem is not GrammarIssueView issue)
            {
                heading.Text=grammarIssues.Count==0?"No grammar or style suggestions":"Select a suggestion";
                kind.Text=original.Text=suggested.Text=explanation.Text="";
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
        buttons.Children.Add(refresh);buttons.Children.Add(ai);buttons.Children.Add(ignoreRule);buttons.Children.Add(ignore);buttons.Children.Add(apply);buttons.Children.Add(close);
        Grid.SetColumn(buttons,0);Grid.SetColumnSpan(buttons,2);Grid.SetRow(buttons,1);root.Children.Add(buttons);

        refresh.Click+=(_,__)=>RunLocalGrammarReview();
        apply.Click+=(_,__)=>{if(list.SelectedItem is GrammarIssueView issue)ApplyGrammarIssue(issue);};
        ignore.Click+=(_,__)=>{if(list.SelectedItem is GrammarIssueView issue)IgnoreGrammarIssue(issue);};
        ignoreRule.Click+=(_,__)=>{if(list.SelectedItem is GrammarIssueView issue)IgnoreGrammarRule(issue.Finding.RuleId);};
        ai.Click+=async (_,__)=>>
        {
            var paragraph=list.SelectedItem is GrammarIssueView issue?issue.Paragraph:FindContainingParagraph(Editor.CaretPosition);
            if(paragraph!=null)await AddAiGrammarForParagraphAsync(paragraph);
        };
        close.Click+=(_,__)=>w.Close();
        w.Closed+=(_,__)=>{grammarReviewWindow=null;grammarReviewList=null;};

        RefreshGrammarReviewWindow();
        RefreshDetails();
        w.Content=root;
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
                "AI language services are disabled. Enable them under File → Settings → Grammar & Style if you want to add contextual AI suggestions.",
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
                var start=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start);
                var end=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,finding.Start+finding.Length);
                if(start==null || end==null || start.CompareTo(end)>=0)continue;
                if(grammarIssues.Any(x=>ReferenceEquals(x.Paragraph,paragraph) &&
                    x.Start.CompareTo(end)<0 && x.End.CompareTo(start)>0 &&
                    string.Equals(x.Finding.Replacement,finding.Replacement,StringComparison.Ordinal)))continue;
                grammarIssues.Add(new GrammarIssueView{Paragraph=paragraph,Finding=finding,Start=start,End=end});
            }
            grammarIssues.Sort((a,b)=>a.Start.CompareTo(b.Start));
            RefreshGrammarMarks();RefreshGrammarReviewWindow();UpdateGrammarStatus();
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

    void GrammarSettings_Click(object? sender,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Grammar and Style Settings",Owner=this,Width=620,Height=390,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Grammar and style",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var enabled=new CheckBox
        {
            Content="Show thin blue grammar and style suggestion marks",
            IsChecked=languageSettings.GrammarAndStyleSuggestions,Margin=new Thickness(0,4,0,6)
        };
        var ai=new CheckBox
        {
            Content="Allow AI contextual review when I explicitly request it",
            IsChecked=languageSettings.AllowAiLanguageServices,Margin=new Thickness(0,4,0,6)
        };
        panel.Children.Add(enabled);panel.Children.Add(ai);
        panel.Children.Add(new TextBlock
        {
            Text="Local checks run after a short pause. AI review is paragraph-scoped and only runs when requested from the Review menu or review pane. Grammar and style suggestions are never applied automatically.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,4,0,16)
        });
        panel.Children.Add(new Separator());
        var mode=WritingModeProfiles.Get(languageSettings.WritingMode);
        panel.Children.Add(new TextBlock{Text="Current writing mode: "+mode.DisplayName,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,4)});
        panel.Children.Add(new TextBlock{Text=mode.Description,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
        panel.Children.Add(new TextBlock
        {
            Text="Change the writing mode under Review → Writing Mode or File → Settings → Language & Writing Mode.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,4,0,12)
        });
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
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
        buttons.Children.Add(apply);buttons.Children.Add(close);panel.Children.Add(buttons);
        w.Content=panel;w.ShowDialog();
    }

    void ShowLanguageAssistanceHelp()
    {
        var w=new Window
        {
            Title="CtrlZed Language Assistance",Owner=this,Width=720,Height=580,
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
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22),FontSize=14
        };
        var close=new Button{Content="Close",Width=90,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,16,16)};
        close.Click+=(_,__)=>w.Close();
        var root=new DockPanel();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        root.Children.Add(new ScrollViewer{Content=text,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        w.Content=root;w.ShowDialog();
    }
}

internal static class Beta32GrammarReviewBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is MainWindow window)window.InitialiseBeta32GrammarReview();
    }
}
