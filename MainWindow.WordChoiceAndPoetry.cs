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
    sealed record LexicalTarget(
        Paragraph Paragraph,
        TextPointer Start,
        TextPointer End,
        string Text,
        string ParagraphText,
        int StartOffset);

    sealed record PoetryLine(Paragraph Paragraph,string Text);

    sealed class RhymeIssueView
    {
        public required Paragraph Paragraph { get; init; }
        public required TextPointer Start { get; init; }
        public required TextPointer End { get; init; }
        public required RhymeSuggestion Suggestion { get; init; }

        public override string ToString()
            =>$"Line {Suggestion.LineNumber}: {Suggestion.Original} → {Suggestion.Replacement} ({Suggestion.RhymeType} rhyme)";
    }

    bool beta33WordChoiceInitialised;
    RhymeSquiggleAdorner? rhymeAdorner;
    readonly List<RhymeIssueView> rhymeIssues=new();
    CancellationTokenSource? wordChoiceCancellation;
    Window? rhymeReviewWindow;
    ListBox? rhymeReviewList;

    internal void InitialiseBeta33WordChoice()
    {
        if(beta33WordChoiceInitialised)return;
        beta33WordChoiceInitialised=true;
        Editor.TextChanged+=WordChoiceEditor_TextChanged;
        Closed+=(_,__)=>wordChoiceCancellation?.Cancel();
        InstallBeta33WordChoiceUi();
        EnsureRhymeAdorner();
    }

    void InstallBeta33WordChoiceUi()
    {
        var review=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review==null)
        {
            Dispatcher.BeginInvoke(new Action(InstallBeta33WordChoiceUi),DispatcherPriority.ApplicationIdle);
            return;
        }

        var thesaurus=review.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Header?.ToString()??"").Contains("Contextual Thesaurus",StringComparison.OrdinalIgnoreCase));
        if(thesaurus!=null && (thesaurus.Tag as string)!="CtrlZedContextualThesaurus")
        {
            thesaurus.Header="Contextual _Thesaurus…";
            thesaurus.IsEnabled=true;
            thesaurus.Tag="CtrlZedContextualThesaurus";
            thesaurus.Click+=ContextualThesaurus_Click;
        }

        var rhyme=review.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>(x.Header?.ToString()??"").Contains("Poetry Rhyme Assistant",StringComparison.OrdinalIgnoreCase));
        if(rhyme!=null && (rhyme.Tag as string)!="CtrlZedPoetryRhyme")
        {
            rhyme.Header="_Poetry Rhyme Assistant…";
            rhyme.IsEnabled=true;
            rhyme.Tag="CtrlZedPoetryRhyme";
            rhyme.Click+=PoetryRhymeAssistant_Click;
        }

        var fileMenu=MainMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>string.Equals(NormalisedMenuHeader(x.Header),"File",StringComparison.OrdinalIgnoreCase));
        var settings=fileMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(x=>NormalisedMenuHeader(x.Header).StartsWith("Settings",StringComparison.OrdinalIgnoreCase));
        if(settings!=null && !settings.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedWordChoiceSettings"))
        {
            var item=new MenuItem{Header="_Word Choice & Poetry…",Tag="CtrlZedWordChoiceSettings"};
            item.Click+=WordChoiceSettings_Click;
            settings.Items.Add(item);
        }
    }

    void WordChoiceEditor_TextChanged(object sender,TextChangedEventArgs e)
    {
        if(rhymeIssues.Count==0)return;
        ClearRhymeIssues("The poem changed, so the earlier rhyme opportunities were cleared. Analyse the stanza again when ready.");
    }

    void EnsureRhymeAdorner()
    {
        if(rhymeAdorner!=null)return;
        var layer=AdornerLayer.GetAdornerLayer(Editor);
        if(layer==null)
        {
            Dispatcher.BeginInvoke(new Action(EnsureRhymeAdorner),DispatcherPriority.ContextIdle);
            return;
        }
        rhymeAdorner=new RhymeSquiggleAdorner(Editor);
        layer.Add(rhymeAdorner);
    }

    void RefreshRhymeMarks()
    {
        EnsureRhymeAdorner();
        var ranges=languageSettings.PoetryRhymeSuggestions
            ?rhymeIssues.Select(x=>new RhymeMarkedRange(x.Start,x.End)).ToList()
            :new List<RhymeMarkedRange>();
        rhymeAdorner?.SetRanges(ranges);
    }

    void ClearRhymeIssues(string? message=null)
    {
        wordChoiceCancellation?.Cancel();
        rhymeIssues.Clear();
        rhymeAdorner?.SetRanges(Array.Empty<RhymeMarkedRange>());
        RefreshRhymeWindowList();
        if(!string.IsNullOrWhiteSpace(message))Status.Text=message;
    }

    LexicalTarget? GetLexicalTarget()
    {
        var anchor=Editor.Selection.IsEmpty?Editor.CaretPosition:Editor.Selection.Start;
        var paragraph=FindContainingParagraph(anchor);
        if(paragraph==null)return null;
        var paragraphText=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text.TrimEnd('\r','\n');
        if(paragraphText.Length==0)return null;

        if(!Editor.Selection.IsEmpty)
        {
            var endParagraph=FindContainingParagraph(Editor.Selection.End);
            if(!ReferenceEquals(paragraph,endParagraph))return null;
            var raw=new TextRange(Editor.Selection.Start,Editor.Selection.End).Text;
            var trimmed=raw.Trim();
            if(trimmed.Length==0 || trimmed.Length>80 || trimmed.Contains('\n') || trimmed.Contains('\r'))return null;
            var left=raw.Length-raw.TrimStart().Length;
            var right=raw.Length-raw.TrimEnd().Length;
            var baseOffset=new TextRange(paragraph.ContentStart,Editor.Selection.Start).Text.Length;
            var startOffset=baseOffset+left;
            var endOffset=baseOffset+raw.Length-right;
            var start=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,startOffset);
            var end=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,endOffset);
            return start==null || end==null?null:new LexicalTarget(paragraph,start,end,trimmed,paragraphText,startOffset);
        }

        var caretOffset=Math.Clamp(new TextRange(paragraph.ContentStart,Editor.CaretPosition).Text.Length,0,paragraphText.Length);
        bool IsWordCharacter(char ch)=>char.IsLetterOrDigit(ch) || ch is '\'' or '’' or '-';
        var probe=caretOffset;
        if(probe==paragraphText.Length || (probe<paragraphText.Length && !IsWordCharacter(paragraphText[probe])))probe--;
        while(probe>=0 && probe<paragraphText.Length && !IsWordCharacter(paragraphText[probe]))probe--;
        if(probe<0)return null;
        var startOffsetWord=probe;
        while(startOffsetWord>0 && IsWordCharacter(paragraphText[startOffsetWord-1]))startOffsetWord--;
        var endOffsetWord=probe+1;
        while(endOffsetWord<paragraphText.Length && IsWordCharacter(paragraphText[endOffsetWord]))endOffsetWord++;
        if(endOffsetWord<=startOffsetWord)return null;
        var word=paragraphText[startOffsetWord..endOffsetWord];
        var wordStart=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,startOffsetWord);
        var wordEnd=PointerAtTextOffset(paragraph.ContentStart,paragraph.ContentEnd,endOffsetWord);
        return wordStart==null || wordEnd==null?null:new LexicalTarget(paragraph,wordStart,wordEnd,word,paragraphText,startOffsetWord);
    }

    async void ContextualThesaurus_Click(object? sender,RoutedEventArgs e)
    {
        if(!languageSettings.ContextualThesaurus)
        {
            MessageBox.Show(
                "The contextual thesaurus is disabled. Enable it under File → Settings → Word Choice & Poetry.",
                "Contextual Thesaurus",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(!languageSettings.AllowAiLanguageServices)
        {
            MessageBox.Show(
                "Contextual word suggestions require an explicitly enabled direct AI provider. Enable AI language services under File → Settings → Grammar & Style or Language & Writing Mode.",
                "Contextual Thesaurus",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var target=GetLexicalTarget();
        if(target==null)
        {
            MessageBox.Show(
                "Select a word or short phrase, or place the caret inside a word, then try again.",
                "Contextual Thesaurus",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        wordChoiceCancellation?.Cancel();
        wordChoiceCancellation=new CancellationTokenSource();
        var cancellationToken=wordChoiceCancellation.Token;

        var w=new Window
        {
            Title="Contextual Thesaurus — "+target.Text,
            Owner=this,Width=820,Height=570,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()
        };
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(290)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var context=new TextBlock
        {
            Text=$"Word or phrase: {target.Text}\nWriting mode: {WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName}",
            FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)
        };
        Grid.SetColumnSpan(context,2);root.Children.Add(context);

        var list=new ListBox{Margin=new Thickness(0,0,12,0)};
        Grid.SetRow(list,1);Grid.SetColumn(list,0);root.Children.Add(list);
        var details=new StackPanel();
        var replacement=new TextBlock{FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        var nuance=new TextBlock{Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        var explanation=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=180};
        details.Children.Add(replacement);details.Children.Add(nuance);
        details.Children.Add(new TextBlock{Text="Why it fits this context",Margin=new Thickness(0,8,0,4)});
        details.Children.Add(explanation);
        details.Children.Add(new TextBlock
        {
            Text="Alternatives are suggestions only. CtrlZed never substitutes a thesaurus choice automatically.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,12,0,0)
        });
        Grid.SetRow(details,1);Grid.SetColumn(details,1);root.Children.Add(details);

        var status=new TextBlock
        {
            Text="Preparing contextual alternatives…",TextWrapping=TextWrapping.Wrap,
            VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.DimGray,Margin=new Thickness(4,0,14,0)
        };
        var refresh=new Button{Content="Refresh",Width=85,Margin=new Thickness(4)};
        var apply=new Button{Content="Apply",Width=85,Margin=new Thickness(4),IsEnabled=false};
        var close=new Button{Content="Close",Width=85,Margin=new Thickness(4)};
        var actionGrid=new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition());
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        actionGrid.Children.Add(status);
        var actionButtons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        actionButtons.Children.Add(refresh);actionButtons.Children.Add(apply);actionButtons.Children.Add(close);
        Grid.SetColumn(actionButtons,1);actionGrid.Children.Add(actionButtons);
        var box=new GroupBox{Header="Word choice",Content=actionGrid,Padding=new Thickness(8),Margin=new Thickness(0,12,0,0)};
        Grid.SetRow(box,2);Grid.SetColumnSpan(box,2);root.Children.Add(box);

        var suggestions=new List<ThesaurusSuggestion>();
        var busy=false;
        void UpdateDetails()
        {
            if(list.SelectedItem is not ThesaurusSuggestion item)
            {
                replacement.Text="";nuance.Text="";explanation.Text="";apply.IsEnabled=false;return;
            }
            replacement.Text=item.Replacement;
            nuance.Text=$"{item.Nuance} · {item.Register}"+(item.Syllables is int count?$" · {count} syllable{(count==1?"":"s")}":"");
            explanation.Text=item.Explanation;
            apply.IsEnabled=!busy;
        }
        list.SelectionChanged+=(_,__)=>UpdateDetails();

        async Task LoadSuggestionsAsync()
        {
            if(busy)return;
            busy=true;refresh.IsEnabled=false;apply.IsEnabled=false;
            var provider=AiManager.GetDefaultProvider();
            status.Text=$"Asking {provider} for meaning-aware alternatives…";
            try
            {
                var result=await ContextualWordChoiceService.SuggestThesaurusAsync(
                    target.Text,target.ParagraphText,WritingModeProfiles.Get(languageSettings.WritingMode),cancellationToken);
                suggestions=result.ToList();
                list.ItemsSource=suggestions;
                list.DisplayMemberPath="Replacement";
                if(suggestions.Count>0)list.SelectedIndex=0;
                status.Text=suggestions.Count==0
                    ?"No sufficiently strong contextual alternatives were found."
                    :$"{suggestions.Count} contextual alternative{(suggestions.Count==1?"":"s")} ready. Select one to review.";
            }
            catch(OperationCanceledException){status.Text="Contextual thesaurus request cancelled.";}
            catch(Exception ex){status.Text="Contextual thesaurus failed: "+ex.Message;}
            finally{busy=false;refresh.IsEnabled=true;UpdateDetails();}
        }

        refresh.Click+=async (_,__)=>await LoadSuggestionsAsync();
        apply.Click+=(_,__)=>
        {
            if(busy || list.SelectedItem is not ThesaurusSuggestion item)return;
            var current=new TextRange(target.Start,target.End).Text;
            if(!string.Equals(current,target.Text,StringComparison.Ordinal))
            {
                status.Text="The source text has changed. Close this window and request fresh alternatives.";
                apply.IsEnabled=false;return;
            }
            new TextRange(target.Start,target.End).Text=item.Replacement;
            MarkDirty($"Replaced ‘{target.Text}’ with ‘{item.Replacement}’. Ctrl+Z will undo it.");
            status.Text=$"Applied ‘{item.Replacement}’. Ctrl+Z will restore ‘{target.Text}’.";
            apply.IsEnabled=false;refresh.IsEnabled=false;list.IsEnabled=false;
        };
        close.Click+=(_,__)=>w.Close();
        w.Closed+=(_,__)=>wordChoiceCancellation?.Cancel();
        w.Content=root;w.Show();
        await LoadSuggestionsAsync();
    }

    IReadOnlyList<PoetryLine> CollectCurrentStanza()
    {
        var paragraphs=EnumerateDocumentParagraphs().ToList();
        if(paragraphs.Count==0)return Array.Empty<PoetryLine>();
        var current=FindContainingParagraph(Editor.CaretPosition)??FindContainingParagraph(Editor.Selection.Start);
        var index=current==null?-1:paragraphs.IndexOf(current);
        if(index<0)index=0;

        string TextOf(Paragraph paragraph)=>new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text.Trim();
        if(string.IsNullOrWhiteSpace(TextOf(paragraphs[index])))
        {
            var nearest=Enumerable.Range(0,paragraphs.Count)
                .OrderBy(i=>Math.Abs(i-index))
                .FirstOrDefault(i=>!string.IsNullOrWhiteSpace(TextOf(paragraphs[i])));
            index=nearest;
        }

        var start=index;
        while(start>0 && index-start<11 && !string.IsNullOrWhiteSpace(TextOf(paragraphs[start-1])))start--;
        var end=index;
        while(end+1<paragraphs.Count && end-start<11 && !string.IsNullOrWhiteSpace(TextOf(paragraphs[end+1])))end++;

        return paragraphs.Skip(start).Take(end-start+1)
            .Select(p=>new PoetryLine(p,TextOf(p)))
            .Where(x=>!string.IsNullOrWhiteSpace(x.Text))
            .Take(12)
            .ToList();
    }

    async void PoetryRhymeAssistant_Click(object? sender,RoutedEventArgs e)
    {
        if(languageSettings.WritingMode!=WritingMode.Poetry)
        {
            var switchMode=MessageBox.Show(
                "The Rhyme Assistant is designed for Poetry writing mode. Switch to Poetry mode and continue?",
                "Poetry Rhyme Assistant",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(switchMode!=MessageBoxResult.Yes)return;
            languageSettings.WritingMode=WritingMode.Poetry;
            UpdateLanguageMenuState();
        }
        if(!languageSettings.AllowAiLanguageServices)
        {
            MessageBox.Show(
                "Rhyme analysis requires an explicitly enabled direct AI provider. Enable AI language services under File → Settings → Grammar & Style or Language & Writing Mode.",
                "Poetry Rhyme Assistant",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        var lines=CollectCurrentStanza();
        if(lines.Count<2)
        {
            MessageBox.Show(
                "Place the caret in a stanza containing at least two non-empty lines, separated from surrounding text by blank paragraphs.",
                "Poetry Rhyme Assistant",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }

        languageSettings.PoetryRhymeSuggestions=true;
        LanguageIntelligenceSettingsStore.Save(languageSettings);
        await ShowRhymeAssistantAsync(lines);
    }

    async Task ShowRhymeAssistantAsync(IReadOnlyList<PoetryLine> lines)
    {
        if(rhymeReviewWindow!=null)
        {
            rhymeReviewWindow.Close();
            rhymeReviewWindow=null;
        }
        wordChoiceCancellation?.Cancel();
        wordChoiceCancellation=new CancellationTokenSource();
        var cancellationToken=wordChoiceCancellation.Token;

        var w=new Window
        {
            Title="Poetry Rhyme Assistant",Owner=this,Width=940,Height=620,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        rhymeReviewWindow=w;
        var root=new Grid{Margin=new Thickness(14)};
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(350)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var summary=new TextBlock{Text="Analysing the current stanza…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        Grid.SetColumnSpan(summary,2);root.Children.Add(summary);

        var list=new ListBox{Margin=new Thickness(0,0,12,0)};
        rhymeReviewList=list;
        Grid.SetRow(list,1);Grid.SetColumn(list,0);root.Children.Add(list);
        var detail=new StackPanel();
        var heading=new TextBlock{FontSize=21,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        var metadata=new TextBlock{Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        var original=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var replacement=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MinHeight=55,Margin=new Thickness(0,3,0,10)};
        var reason=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=120};
        detail.Children.Add(heading);detail.Children.Add(metadata);
        detail.Children.Add(new TextBlock{Text="Original wording"});detail.Children.Add(original);
        detail.Children.Add(new TextBlock{Text="Suggested rhyme wording"});detail.Children.Add(replacement);
        detail.Children.Add(new TextBlock{Text="Reason",Margin=new Thickness(0,4,0,4)});detail.Children.Add(reason);
        detail.Children.Add(new TextBlock
        {
            Text="Green marks identify optional word choices, not faults. Meaning and voice take precedence over rhyme.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,12,0,0)
        });
        Grid.SetRow(detail,1);Grid.SetColumn(detail,1);root.Children.Add(detail);

        var actionStatus=new TextBlock
        {
            Text="Preparing rhyme opportunities…",TextWrapping=TextWrapping.Wrap,
            VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.DimGray,Margin=new Thickness(4,0,14,0)
        };
        var showMarks=new CheckBox{Content="Show green marks",IsChecked=languageSettings.PoetryRhymeSuggestions,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)};
        var analyse=new Button{Content="Analyse Again",Width=100,Margin=new Thickness(4)};
        var ignore=new Button{Content="Ignore",Width=80,Margin=new Thickness(4),IsEnabled=false};
        var apply=new Button{Content="Apply",Width=80,Margin=new Thickness(4),IsEnabled=false};
        var close=new Button{Content="Close",Width=80,Margin=new Thickness(4)};
        var actionGrid=new Grid();
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition());
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        actionGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        actionGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRowSpan(actionStatus,2);actionGrid.Children.Add(actionStatus);
        var firstRow=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};firstRow.Children.Add(showMarks);firstRow.Children.Add(analyse);
        Grid.SetColumn(firstRow,1);actionGrid.Children.Add(firstRow);
        var secondRow=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};secondRow.Children.Add(ignore);secondRow.Children.Add(apply);secondRow.Children.Add(close);
        Grid.SetColumn(secondRow,1);Grid.SetRow(secondRow,1);actionGrid.Children.Add(secondRow);
        var actionBox=new GroupBox{Header="Rhyme actions",Content=actionGrid,Padding=new Thickness(8),Margin=new Thickness(0,12,0,0)};
        Grid.SetRow(actionBox,2);Grid.SetColumnSpan(actionBox,2);root.Children.Add(actionBox);

        var busy=false;
        void RefreshDetails()
        {
            if(list.SelectedItem is not RhymeIssueView issue)
            {
                heading.Text=rhymeIssues.Count==0?"No strong rhyme opportunities":"Select an opportunity";
                metadata.Text="";original.Text="";replacement.Text="";reason.Text="";
                apply.IsEnabled=false;ignore.IsEnabled=false;return;
            }
            var s=issue.Suggestion;
            heading.Text=$"Line {s.LineNumber}: {s.RhymeType} rhyme";
            metadata.Text=$"Rhymes with line {s.RhymesWithLine}: {s.RhymesWith}"+(s.Syllables is int count?$" · {count} syllable{(count==1?"":"s")}":"");
            original.Text=s.Original;replacement.Text=s.Replacement;reason.Text=s.Explanation;
            apply.IsEnabled=!busy;ignore.IsEnabled=!busy;
            try{Editor.Selection.Select(issue.Start,issue.End);issue.Paragraph.BringIntoView();}catch{ }
        }
        list.SelectionChanged+=(_,__)=>RefreshDetails();

        async Task AnalyseAsync()
        {
            if(busy)return;
            busy=true;analyse.IsEnabled=false;apply.IsEnabled=false;ignore.IsEnabled=false;
            var provider=AiManager.GetDefaultProvider();
            actionStatus.Text=$"Asking {provider} to infer the stanza's rhyme relationships…";
            try
            {
                var analysis=await ContextualWordChoiceService.AnalyseRhymeAsync(
                    lines.Select(x=>x.Text).ToList(),WritingModeProfiles.Get(WritingMode.Poetry),cancellationToken);
                rhymeIssues.Clear();
                foreach(var suggestion in analysis.Opportunities)
                {
                    var line=lines[suggestion.LineNumber-1];
                    var offset=line.Text.IndexOf(suggestion.Original,StringComparison.Ordinal);
                    if(offset<0)continue;
                    var start=PointerAtTextOffset(line.Paragraph.ContentStart,line.Paragraph.ContentEnd,offset);
                    var end=PointerAtTextOffset(line.Paragraph.ContentStart,line.Paragraph.ContentEnd,offset+suggestion.Original.Length);
                    if(start==null || end==null || start.CompareTo(end)>=0)continue;
                    rhymeIssues.Add(new RhymeIssueView{Paragraph=line.Paragraph,Start=start,End=end,Suggestion=suggestion});
                }
                summary.Text=$"Likely scheme: {analysis.Scheme}. {analysis.Summary}";
                RefreshRhymeWindowList();RefreshRhymeMarks();
                actionStatus.Text=rhymeIssues.Count==0
                    ?"No strong meaning-preserving rhyme substitutions were found. Nothing has been marked."
                    :$"{rhymeIssues.Count} optional rhyme opportunit{(rhymeIssues.Count==1?"y is":"ies are")} ready. Green marks appear only when enabled.";
            }
            catch(OperationCanceledException){actionStatus.Text="Rhyme analysis cancelled.";}
            catch(Exception ex){actionStatus.Text="Rhyme analysis failed: "+ex.Message;}
            finally{busy=false;analyse.IsEnabled=true;RefreshDetails();}
        }

        analyse.Click+=async (_,__)=>await AnalyseAsync();
        showMarks.Click+=(_,__)=>
        {
            languageSettings.PoetryRhymeSuggestions=showMarks.IsChecked==true;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            RefreshRhymeMarks();
            actionStatus.Text=languageSettings.PoetryRhymeSuggestions?"Green rhyme opportunity marks are visible.":"Green rhyme opportunity marks are hidden; the suggestions remain in this window.";
        };
        ignore.Click+=(_,__)=>
        {
            if(busy || list.SelectedItem is not RhymeIssueView issue)return;
            rhymeIssues.Remove(issue);RefreshRhymeWindowList();RefreshRhymeMarks();
            actionStatus.Text="Rhyme opportunity ignored for this analysis.";
        };
        apply.Click+=(_,__)=>
        {
            if(busy || list.SelectedItem is not RhymeIssueView issue)return;
            var current=new TextRange(issue.Start,issue.End).Text;
            if(!string.Equals(current,issue.Suggestion.Original,StringComparison.Ordinal))
            {
                actionStatus.Text="The poem changed. Analyse the stanza again before applying this suggestion.";return;
            }
            new TextRange(issue.Start,issue.End).Text=issue.Suggestion.Replacement;
            MarkDirty($"Applied rhyme suggestion: {issue.Suggestion.Original} → {issue.Suggestion.Replacement}. Ctrl+Z will undo it.");
            ClearRhymeIssues();
            summary.Text="The stanza changed after applying a suggestion.";
            actionStatus.Text="Rhyme suggestion applied. Ctrl+Z will undo it. Analyse the stanza again before making another rhyme change.";
        };
        close.Click+=(_,__)=>w.Close();
        w.Closed+=(_,__)=>
        {
            wordChoiceCancellation?.Cancel();
            rhymeReviewWindow=null;rhymeReviewList=null;
        };
        w.Content=root;w.Show();
        await AnalyseAsync();
    }

    void RefreshRhymeWindowList()
    {
        if(rhymeReviewList==null)return;
        var selected=rhymeReviewList.SelectedItem as RhymeIssueView;
        rhymeReviewList.ItemsSource=null;
        rhymeReviewList.ItemsSource=rhymeIssues.ToList();
        if(selected!=null && rhymeIssues.Contains(selected))rhymeReviewList.SelectedItem=selected;
        else if(rhymeIssues.Count>0)rhymeReviewList.SelectedIndex=0;
    }

    void WordChoiceSettings_Click(object? sender,RoutedEventArgs e)
    {
        var w=new Window
        {
            Title="Word Choice and Poetry Settings",Owner=this,Width=640,Height=350,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,
            Icon=Branding.CreateIconImageSource()
        };
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Word choice and poetry",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var thesaurus=new CheckBox
        {
            Content="Enable the contextual thesaurus command",
            IsChecked=languageSettings.ContextualThesaurus,Margin=new Thickness(0,4,0,7)
        };
        var rhyme=new CheckBox
        {
            Content="Show green marks after an explicit Poetry Rhyme Assistant analysis",
            IsChecked=languageSettings.PoetryRhymeSuggestions,Margin=new Thickness(0,4,0,7)
        };
        panel.Children.Add(thesaurus);panel.Children.Add(rhyme);
        panel.Children.Add(new TextBlock
        {
            Text="Both services are explicitly requested and paragraph/stanza scoped. They do not poll, analyse or contact an AI provider while you type. Thesaurus and rhyme replacements always require your approval.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,18)
        });
        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock
        {
            Text="Current writing mode: "+WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName,
            FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,5)
        });
        panel.Children.Add(new TextBlock
        {
            Text="Green rhyme marks are available only in Poetry mode. They identify optional meaning-preserving word choices, not errors.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray
        });
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        var apply=new Button{Content="Apply",Width=85,Margin=new Thickness(4),IsDefault=true};
        var close=new Button{Content="Close",Width=85,Margin=new Thickness(4),IsCancel=true};
        apply.Click+=(_,__)=>
        {
            languageSettings.ContextualThesaurus=thesaurus.IsChecked==true;
            languageSettings.PoetryRhymeSuggestions=rhyme.IsChecked==true && languageSettings.WritingMode==WritingMode.Poetry;
            LanguageIntelligenceSettingsStore.Save(languageSettings);
            RefreshRhymeMarks();
            Status.Text="Word-choice and poetry settings applied.";
        };
        close.Click+=(_,__)=>w.Close();
        buttons.Children.Add(apply);buttons.Children.Add(close);panel.Children.Add(buttons);
        w.Content=panel;w.ShowDialog();
    }
}

internal static class Beta33WordChoiceBootstrap
{
    [ModuleInitializer]
    public static void Initialise()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    static void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.ContentRendered-=OnContentRendered;
        window.ContentRendered+=OnContentRendered;
    }

    static void OnContentRendered(object? sender,EventArgs e)
    {
        if(sender is not MainWindow window)return;
        window.Dispatcher.BeginInvoke(new Action(window.InitialiseBeta33WordChoice),DispatcherPriority.ApplicationIdle);
    }
}
