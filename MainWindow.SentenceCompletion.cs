using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? sentenceCompletionWindow;
    bool sentenceCompletionInstalled;
    Action<string?>? sentenceCompletionChooseInflection;

    internal void InstallSentenceCompletion()
    {
        if(sentenceCompletionInstalled)return;
        sentenceCompletionInstalled=true;
        Loaded+=(_,__)=>Dispatcher.BeginInvoke(new Action(InstallSentenceCompletionReviewItem),DispatcherPriority.ApplicationIdle);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(InstallSentenceCompletionReviewItem),DispatcherPriority.ApplicationIdle);
        Closed+=(_,__)=>sentenceCompletionWindow?.Close();
        if(IsLoaded)Dispatcher.BeginInvoke(new Action(InstallSentenceCompletionReviewItem),DispatcherPriority.ApplicationIdle);
    }

    void InstallSentenceCompletionReviewItem()
    {
        var review=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
        if(review==null || review.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedFinishSentence"))return;
        var item=new MenuItem{Header="Finish _Sentence…",Tag="CtrlZedFinishSentence"};
        item.Click+=(_,__)=>ShowSentenceCompletion();
        review.Items.Add(item);
    }

    internal void AddSentenceCompletionContextMenu(ContextMenu menu)
    {
        if(menu.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedFinishSentence"))return;
        var item=new MenuItem{Header="Finish Sentence…",Tag="CtrlZedFinishSentence"};
        item.Click+=(_,__)=>ShowSentenceCompletion();
        menu.Items.Add(item);
    }

    /// <summary>Opens a preview; generating and inserting are separate, explicit actions.</summary>
    public void ShowSentenceCompletion(string? inflection=null)
    {
        if(sentenceCompletionWindow!=null)
        {
            if(!string.IsNullOrWhiteSpace(inflection))sentenceCompletionChooseInflection?.Invoke(inflection);
            if(sentenceCompletionWindow.WindowState==WindowState.Minimized)sentenceCompletionWindow.WindowState=WindowState.Normal;
            sentenceCompletionWindow.Activate();
            return;
        }

        // Capture before showing a modeless window moves keyboard focus away from the editor.
        SentenceCompletionTarget? target=null;
        string? captureError=null;
        try{target=CaptureSentenceCompletionTarget();}
        catch(InvalidOperationException ex){captureError=ex.Message;}

        var preferences=SentenceCompletionPreferencesStore.Load();
        var availableWidth=Math.Max(320,SystemParameters.WorkArea.Width-32);
        var availableHeight=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window
        {
            Title="Finish Sentence",Owner=this,Width=Math.Min(990,availableWidth),Height=Math.Min(790,availableHeight),
            MinWidth=Math.Min(810,availableWidth),MinHeight=Math.Min(600,availableHeight),
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        sentenceCompletionWindow=w;
        NameScope.SetNameScope(w,new NameScope());
        T Named<T>(string name,T control) where T:FrameworkElement
        {
            control.Name=name;w.RegisterName(name,control);return control;
        }

        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var heading=new TextBlock{Text="Finish Sentence",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)};
        root.Children.Add(heading);
        var information=Named("CompletionInformation",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)});
        Grid.SetRow(information,1);root.Children.Add(information);

        var body=new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(235)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body,2);root.Children.Add(body);
        var options=new StackPanel();
        var optionScroll=new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        body.Children.Add(optionScroll);
        options.Children.Add(new TextBlock{Text="Inflection",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
        var inflections=Named("CompletionInflections",new ListBox{MinHeight=178,MaxHeight=246,DisplayMemberPath="Name",Margin=new Thickness(0,0,0,12)});
        options.Children.Add(inflections);
        options.Children.Add(new TextBlock{Text="Strength",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
        var strength=Named("CompletionStrength",new ComboBox{ItemsSource=Enum.GetValues<CompletionStrength>(),SelectedItem=preferences.Strength,Margin=new Thickness(0,0,0,12)});
        options.Children.Add(strength);
        options.Children.Add(new TextBlock{Text="Context",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
        var context=Named("CompletionContext",new ComboBox{ItemsSource=Enum.GetValues<CompletionContextScope>(),SelectedItem=preferences.ContextScope,Margin=new Thickness(0,0,0,12)});
        var contextText=new FrameworkElementFactory(typeof(TextBlock));
        contextText.SetBinding(TextBlock.TextProperty,new Binding{Converter=new SentenceCompletionScopeLabelConverter()});
        context.ItemTemplate=new DataTemplate{VisualTree=contextText};
        options.Children.Add(context);
        options.Children.Add(new TextBlock{Text="What I mean (optional)",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
        var hint=Named("CompletionHint",new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=68,MaxHeight=110,MaxLength=4000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,0,0,12)});
        options.Children.Add(hint);

        var customPanel=new StackPanel{Margin=new Thickness(0,8,0,0)};
        customPanel.Children.Add(new TextBlock{Text="Name",Margin=new Thickness(0,0,0,4)});
        var customName=Named("CompletionCustomName",new TextBox{MaxLength=48,Margin=new Thickness(0,0,0,8)});
        customPanel.Children.Add(customName);
        customPanel.Children.Add(new TextBlock{Text="Describe the inflection",Margin=new Thickness(0,0,0,4)});
        var customInstruction=Named("CompletionCustomInstruction",new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=70,MaxHeight=130,MaxLength=2000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        customPanel.Children.Add(customInstruction);
        var customActions=new WrapPanel{Margin=new Thickness(0,7,0,0)};
        var saveCustom=Named("CompletionSaveCustom",new Button{Content="Save custom",MinWidth=100,Margin=new Thickness(0,0,6,0)});
        var deleteCustom=Named("CompletionDeleteCustom",new Button{Content="Delete",MinWidth=65});
        customActions.Children.Add(saveCustom);customActions.Children.Add(deleteCustom);customPanel.Children.Add(customActions);
        var customExpander=Named("CompletionCustom",new Expander{Header="Custom inflection",Content=customPanel,Margin=new Thickness(0,0,0,10)});
        options.Children.Add(customExpander);

        var right=new Grid();
        right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star),MinHeight=110});
        right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(80),MinHeight=65});
        right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetColumn(right,2);body.Children.Add(right);
        var targetPreview=Named("CompletionTarget",new TextBlock{TextWrapping=TextWrapping.Wrap,MaxHeight=60,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,0,10)});
        right.Children.Add(targetPreview);
        var suggestions=Named("CompletionSuggestions",new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch});
        // Item contents wrap at the available width, including after the window is resized.
        ScrollViewer.SetHorizontalScrollBarVisibility(suggestions,ScrollBarVisibility.Disabled);
        Grid.SetRow(suggestions,1);right.Children.Add(suggestions);
        right.Children.Add(WithRow(new TextBlock{Text="Edit the selected completion",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,5)},2));
        var edit=Named("CompletionEdit",new TextBox{TextWrapping=TextWrapping.Wrap,MaxLength=1000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,IsEnabled=false});
        Grid.SetRow(edit,3);right.Children.Add(edit);
        var editedPreview=Named("CompletionPreview",new TextBlock{TextWrapping=TextWrapping.Wrap,MaxHeight=48,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,8,0,0)});
        Grid.SetRow(editedPreview,4);right.Children.Add(editedPreview);

        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};
        var status=Named("CompletionStatus",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,MinHeight=36,Margin=new Thickness(0,0,0,6)});
        footer.Children.Add(status);
        var actions=new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var settings=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
        var languageSetup=new Button{Content="Language settings…",Margin=new Thickness(0,4,6,4),Padding=new Thickness(9,4,9,4)};
        var providerSetup=new Button{Content="AI settings…",Margin=new Thickness(0,4,0,4),Padding=new Thickness(9,4,9,4)};
        settings.Children.Add(languageSetup);settings.Children.Add(providerSetup);actions.Children.Add(settings);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var generate=Named("CompletionGenerate",new Button{Content="Generate",MinWidth=88,Margin=new Thickness(4),Padding=new Thickness(8,4,8,4)});
        var regenerate=Named("CompletionRegenerate",new Button{Content="Regenerate",MinWidth=88,Margin=new Thickness(4),Padding=new Thickness(8,4,8,4),IsEnabled=false});
        var insert=Named("CompletionInsert",new Button{Content="Insert",MinWidth=78,Margin=new Thickness(4),Padding=new Thickness(8,4,8,4),IsEnabled=false});
        var close=new Button{Content="Close",MinWidth=78,Margin=new Thickness(4),Padding=new Thickness(8,4,8,4),IsCancel=true};
        buttons.Children.Add(generate);buttons.Children.Add(regenerate);buttons.Children.Add(insert);buttons.Children.Add(close);
        Grid.SetColumn(buttons,1);actions.Children.Add(buttons);footer.Children.Add(actions);
        Grid.SetRow(footer,3);root.Children.Add(footer);
        w.Content=root;

        var busy=false;
        var closed=false;
        var configuring=false;
        var assigning=false;
        var initialising=true;
        var validSuggestions=false;
        long generation=0;
        CancellationTokenSource? cancellation=null;
        var provider=AiManager.GetDefaultProvider();
        string? cachedAvailability=null;
        string? preferenceSaveError=null;
        var currentInflections=new List<CompletionInflection>();
        var customChoice=new CompletionInflection("Custom","");
        CompletionInflection? selectedCustom=null;

        CompletionContextScope Scope()=>context.SelectedItem is CompletionContextScope value?value:CompletionContextScope.Automatic;
        CompletionStrength Strength()=>strength.SelectedItem is CompletionStrength value?value:CompletionStrength.Balanced;
        CompletionInflection? Inflection()=>inflections.SelectedItem as CompletionInflection;
        string Instruction()=>CustomSelected()?customInstruction.Text.Trim():Inflection()?.Instruction??"";
        bool CustomSelected()=>Inflection()!=null && !SentenceCompletionService.BuiltIns.Any(x=>string.Equals(x.Name,Inflection()!.Name,StringComparison.OrdinalIgnoreCase));

        string ConfigurationSignature()
        {
            var languageTag=languageSettings.LanguageTag??"";
            var customProfile=languageSettings.CustomProfileName??"";
            var languageConfiguration=$"{(int)languageSettings.WritingMode}|{languageTag.Length}:{languageTag}|{customProfile.Length}:{customProfile}";
            if(SentenceCompletionService.GeneratorOverride!=null)return "Test provider|"+languageConfiguration;
            try
            {
                return AiDirectService.ConfigurationFingerprint(provider)+"|"+languageConfiguration;
            }
            catch{return provider+"|Unavailable|"+languageConfiguration;}
        }
        var providerConfiguration=ConfigurationSignature();

        string? UnavailableReason()
        {
            if(Editor.IsReadOnly || !Editor.IsEnabled)return "The document is read-only. Make it editable before generating or inserting a completion.";
            if(!languageSettings.AllowAiLanguageServices)return "Enable AI language services explicitly in Language settings before generating a completion.";
            if(SentenceCompletionService.GeneratorOverride!=null)return null;
            if(string.Equals(provider,"Copilot",StringComparison.OrdinalIgnoreCase))return "Copilot cannot provide sentence completions directly. Choose ChatGPT, Claude or Gemini in AI settings.";
            try{if(!AiDirectService.GetProfile(provider).HasKey)return $"No API key is configured for {provider}. Configure the provider in AI settings before generating.";}
            catch{return "The AI provider settings could not be read. Check AI settings before generating.";}
            return null;
        }

        void SaveChoices()
        {
            if(initialising)return;
            preferences.Inflection=Inflection()?.Name??"Neutral";
            preferences.Strength=Strength();preferences.ContextScope=Scope();
            try{SentenceCompletionPreferencesStore.Save(preferences);preferenceSaveError=null;}
            catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
            {
                preferenceSaveError="The choices could not be saved: "+ex.Message;
                status.Text=preferenceSaveError;
            }
        }

        void UpdateInformation()
        {
            var mode=WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName;
            information.Text=$"Writing mode: {mode}. Uses {provider}. Context: {SentenceCompletionScopeLabelConverter.Label(Scope())}. Text is sent only when you choose Generate. Nothing changes until Insert."+
                (Scope() is CompletionContextScope.Section or CompletionContextScope.WholeDocument
                    ?" Long passages use additional AI requests to prepare continuity and voice notes.":"");
        }

        void UpdateTargetPreview()
        {
            targetPreview.Text=target==null?"Place the caret in an unfinished sentence, or select wording to replace, then choose Generate.":
                (target.IsReplacement?"Replace selected wording: ":"Continue at the caret: ")+(target.Prefix+target.SelectedText+target.Suffix).Trim();
            targetPreview.ToolTip=targetPreview.Text;
        }

        void UpdateButtons()
        {
            var available=cachedAvailability==null && !Editor.IsReadOnly && Editor.IsEnabled && languageSettings.AllowAiLanguageServices;
            var validEdit=false;
            if(target!=null && !string.IsNullOrWhiteSpace(edit.Text))
            {
                try{SentenceCompletionPreviewParts(target,edit.Text);validEdit=true;}
                catch(InvalidOperationException){ }
            }
            generate.IsEnabled=!closed && !busy && available;
            regenerate.IsEnabled=!closed && !busy && available && validSuggestions;
            insert.IsEnabled=!closed && !busy && available && validSuggestions && suggestions.SelectedItem!=null && validEdit && target!=null && IsSentenceCompletionTargetCurrent(target);
            edit.IsEnabled=validSuggestions && suggestions.SelectedItem!=null && !busy;
            saveCustom.IsEnabled=CustomSelected() && !string.IsNullOrWhiteSpace(customName.Text) && !string.IsNullOrWhiteSpace(customInstruction.Text);
            deleteCustom.IsEnabled=selectedCustom!=null;
        }

        void CancelRequest()
        {
            generation++;
            var previous=cancellation;cancellation=null;
            previous?.Cancel();previous?.Dispose();busy=false;
        }

        void Invalidate(string message)
        {
            if(closed)return;
            CancelRequest();validSuggestions=false;
            suggestions.Items.Clear();edit.Text="";editedPreview.Inlines.Clear();
            status.Text=message+(preferenceSaveError==null?"":" "+preferenceSaveError);UpdateButtons();
        }

        void RefreshInflections(string? name)
        {
            currentInflections=SentenceCompletionService.BuiltIns.Concat(preferences.CustomInflections).Append(customChoice).ToList();
            inflections.ItemsSource=currentInflections;
            inflections.SelectedItem=currentInflections.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))
                ??currentInflections.FirstOrDefault(x=>x.Name=="Neutral")??currentInflections.FirstOrDefault();
        }

        void FillCustomFields()
        {
            assigning=true;
            selectedCustom=preferences.CustomInflections.FirstOrDefault(x=>string.Equals(x.Name,Inflection()?.Name,StringComparison.OrdinalIgnoreCase));
            customName.Text=selectedCustom?.Name??"";customInstruction.Text=selectedCustom?.Instruction??"";
            customName.IsEnabled=CustomSelected();customInstruction.IsEnabled=CustomSelected();
            if(CustomSelected())customExpander.IsExpanded=true;
            assigning=false;
        }

        void RenderPreview(TextBlock block,string wording)
        {
            block.Inlines.Clear();
            if(target==null)return;
            if(string.IsNullOrWhiteSpace(wording)){block.Inlines.Add(new Run(target.Prefix+target.Suffix));return;}
            try
            {
                var parts=SentenceCompletionPreviewParts(target,wording);
                block.Inlines.Add(new Run(parts.Before));
                block.Inlines.Add(new Run(parts.Inserted){Background=new SolidColorBrush(Color.FromRgb(255,239,168)),FontWeight=FontWeights.SemiBold,Foreground=Brushes.Black});
                block.Inlines.Add(new Run(parts.After));
                block.ToolTip=parts.Before+parts.Inserted+parts.After;
            }
            catch(InvalidOperationException)
            {
                block.Inlines.Add(new Run("Edit the completion to a single line of wording before inserting."){Foreground=Brushes.DimGray});
            }
        }

        async Task GenerateAsync()
        {
            if(closed || busy)return;
            provider=AiManager.GetDefaultProvider();UpdateInformation();
            var unavailable=UnavailableReason();cachedAvailability=unavailable;
            if(unavailable!=null){Invalidate(unavailable);return;}
            if(Inflection()==null || (CustomSelected() && string.IsNullOrWhiteSpace(Instruction())))
            {
                Invalidate("Describe the custom inflection before generating a completion.");customExpander.IsExpanded=true;return;
            }
            Invalidate("Preparing the current sentence…");
            SentenceCompletionRequest request;
            try
            {
                target=CaptureSentenceCompletionTarget();UpdateTargetPreview();
                request=CreateSentenceCompletionRequest(target,Scope(),Inflection()!.Name,Instruction(),Strength(),hint.Text.Trim());
                information.ToolTip=request.ContextDescription;
            }
            catch(Exception ex) when(ex is InvalidOperationException or ArgumentException)
            {
                target=null;UpdateTargetPreview();status.Text=ex.Message;UpdateButtons();return;
            }
            var requestedTarget=target;
            var requestedConfiguration=providerConfiguration=ConfigurationSignature();
            var requestGeneration=++generation;
            cancellation=new CancellationTokenSource();
            var token=cancellation.Token;
            busy=true;UpdateButtons();
            status.Text=$"Asking {provider} for three completions…";
            try
            {
                var result=await SentenceCompletionService.GenerateAsync(request,token);
                if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;
                cachedAvailability=UnavailableReason();
                if(cachedAvailability!=null || ConfigurationSignature()!=requestedConfiguration || !ReferenceEquals(target,requestedTarget) || !IsSentenceCompletionTargetCurrent(requestedTarget))
                {
                    Invalidate("The document or AI settings changed. Generate fresh suggestions before inserting.");return;
                }
                suggestions.Items.Clear();
                foreach(var suggestion in result.Take(3))
                {
                    var panel=new StackPanel{Margin=new Thickness(7)};
                    var sentence=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=15,LineHeight=23};
                    RenderPreview(sentence,suggestion.Text);panel.Children.Add(sentence);
                    if(!string.IsNullOrWhiteSpace(suggestion.Explanation))panel.Children.Add(new TextBlock{Text=suggestion.Explanation,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,6,0,0)});
                    suggestions.Items.Add(new ListBoxItem{Content=panel,Tag=suggestion.Text,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(0),Margin=new Thickness(0,0,0,6)});
                }
                validSuggestions=suggestions.Items.Count>0;
                if(validSuggestions)suggestions.SelectedIndex=0;
                status.Text=validSuggestions?"Select a completion and edit its highlighted words if needed. Insert applies it to the captured sentence; Ctrl+Z will undo the change.":"No usable completions were returned. Adjust the inflection or hint and generate again.";
            }
            catch(OperationCanceledException)
            {
                if(!closed && requestGeneration==generation)status.Text="Generation cancelled. Choose Generate when ready.";
            }
            catch(Exception ex)
            {
                if(!closed && requestGeneration==generation)status.Text="Could not generate completions: "+ex.Message;
            }
            finally
            {
                if(!closed && requestGeneration==generation)
                {
                    busy=false;cancellation?.Dispose();cancellation=null;UpdateButtons();
                }
            }
        }

        inflections.SelectionChanged+=(_,__)=>
        {
            FillCustomFields();SaveChoices();
            if(!initialising)Invalidate("Inflection changed. Generate fresh suggestions when ready.");
        };
        strength.SelectionChanged+=(_,__)=>{SaveChoices();if(!initialising)Invalidate("Strength changed. Generate fresh suggestions when ready.");};
        context.SelectionChanged+=(_,__)=>{SaveChoices();UpdateInformation();if(!initialising)Invalidate("Context changed. Generate fresh suggestions when ready.");};
        hint.TextChanged+=(_,__)=>{if(!initialising)Invalidate("Hint changed. Generate fresh suggestions when ready.");};
        customName.TextChanged+=(_,__)=>{if(!assigning)UpdateButtons();};
        customInstruction.TextChanged+=(_,__)=>CustomInstructionChanged();
        void CustomInstructionChanged()
        {
            if(assigning || initialising)return;
            if(CustomSelected())Invalidate("Custom inflection changed. Save it or choose Generate when ready.");
            UpdateButtons();
        }
        saveCustom.Click+=(_,__)=>
        {
            var name=customName.Text.Trim();var instruction=customInstruction.Text.Trim();
            if(name.Length==0 || instruction.Length==0)return;
            if(name.Length>48 || name.Any(c=>char.IsControl(c) || c is '\u2028' or '\u2029') ||
               instruction.Length>2000 || instruction.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            {
                status.Text="Use a name of up to 48 characters on one line and an instruction of up to 2,000 characters.";return;
            }
            if(name.Equals("Custom",StringComparison.OrdinalIgnoreCase) || SentenceCompletionService.BuiltIns.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
            {
                status.Text="Choose a distinct name for the custom inflection.";return;
            }
            var duplicate=preferences.CustomInflections.FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(duplicate!=null && duplicate!=selectedCustom)
            {
                status.Text="That custom inflection name is already in use. Choose a distinct name.";return;
            }
            if(selectedCustom==null && preferences.CustomInflections.Count>=32)
            {
                status.Text="Save up to 32 custom inflections. Delete an unused one before adding another.";return;
            }
            if(selectedCustom!=null)preferences.CustomInflections.Remove(selectedCustom);
            preferences.CustomInflections.Add(new CompletionInflection(name,instruction));
            RefreshInflections(name);SaveChoices();
            status.Text=preferenceSaveError??$"Saved custom inflection ‘{name}’. Choose Generate when ready.";UpdateButtons();
        };
        deleteCustom.Click+=(_,__)=>
        {
            if(selectedCustom==null)return;
            preferences.CustomInflections.Remove(selectedCustom);RefreshInflections("Neutral");SaveChoices();
            status.Text=preferenceSaveError??"Custom inflection deleted. Choose Generate when ready.";UpdateButtons();
        };
        suggestions.SelectionChanged+=(_,__)=>
        {
            edit.Text=(suggestions.SelectedItem as ListBoxItem)?.Tag as string??"";
            RenderPreview(editedPreview,edit.Text);UpdateButtons();
        };
        edit.TextChanged+=(_,__)=>{RenderPreview(editedPreview,edit.Text);UpdateButtons();};
        generate.Click+=async (_,__)=>await GenerateAsync();
        regenerate.Click+=async (_,__)=>await GenerateAsync();
        insert.Click+=(_,__)=>
        {
            if(closed || busy || !validSuggestions || target==null || string.IsNullOrWhiteSpace(edit.Text))return;
            var unavailable=UnavailableReason();cachedAvailability=unavailable;
            if(unavailable!=null){Invalidate(unavailable);return;}
            if(ConfigurationSignature()!=providerConfiguration)
            {
                Invalidate("The AI or language settings changed. Generate fresh suggestions before inserting.");return;
            }
            if(!IsSentenceCompletionTargetCurrent(target)){Invalidate("The document changed. Generate fresh suggestions before inserting.");return;}
            var wording=edit.Text;
            try
            {
                ApplySentenceCompletionSuggestion(target,wording);
                Invalidate("Completion inserted. Ctrl+Z will undo the change. Choose Generate to work on the current sentence.");
            }
            catch(InvalidOperationException ex){Invalidate(ex.Message);}
        };
        languageSetup.Click+=(_,__)=>
        {
            configuring=true;
            try{LanguageSettings_Click(languageSetup,new RoutedEventArgs());}
            finally{configuring=false;RefreshAvailability();}
        };
        providerSetup.Click+=(_,__)=>
        {
            Invalidate("AI settings opened. Generate fresh suggestions after configuring the provider.");
            AiSettings_Click(providerSetup,new RoutedEventArgs());
        };
        close.Click+=(_,__)=>w.Close();
        void RefreshAvailability()
        {
            if(closed || configuring)return;
            var nextProvider=AiManager.GetDefaultProvider();
            if(nextProvider!=provider){provider=nextProvider;Invalidate("AI provider changed. Generate fresh suggestions when ready.");}
            var nextConfiguration=ConfigurationSignature();
            if(nextConfiguration!=providerConfiguration)
            {
                providerConfiguration=nextConfiguration;Invalidate("AI settings changed. Generate fresh suggestions when ready.");
            }
            UpdateInformation();
            var unavailable=UnavailableReason();cachedAvailability=unavailable;
            if(unavailable!=null)Invalidate(unavailable);
            else if(target!=null && !IsSentenceCompletionTargetCurrent(target))Invalidate("The document changed. Choose Generate to capture the current sentence.");
            else UpdateButtons();
        }
        w.Activated+=(_,__)=>RefreshAvailability();
        TextChangedEventHandler sourceChanged=(_,__)=>Invalidate("The document changed. Choose Generate to capture the current sentence.");
        EventHandler readOnlyChanged=(_,__)=>RefreshAvailability();
        var readOnlyDescriptor=DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty,typeof(RichTextBox));
        Editor.TextChanged+=sourceChanged;
        readOnlyDescriptor?.AddValueChanged(Editor,readOnlyChanged);
        sentenceCompletionChooseInflection=name=>
        {
            var match=currentInflections.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));
            if(match!=null)inflections.SelectedItem=match;
        };
        w.Closed+=(_,__)=>
        {
            closed=true;CancelRequest();
            Editor.TextChanged-=sourceChanged;readOnlyDescriptor?.RemoveValueChanged(Editor,readOnlyChanged);
            SaveChoices();
            if(ReferenceEquals(sentenceCompletionWindow,w)){sentenceCompletionWindow=null;sentenceCompletionChooseInflection=null;}
        };
        RefreshInflections(inflection??preferences.Inflection);FillCustomFields();
        initialising=false;SaveChoices();UpdateInformation();UpdateTargetPreview();
        cachedAvailability=UnavailableReason();
        status.Text=cachedAvailability??captureError??preferenceSaveError??"Choose an inflection and Generate to see three possible completions. Proposed words are highlighted.";
        UpdateButtons();w.Show();
    }

    static T WithRow<T>(T element,int row) where T:UIElement
    {
        Grid.SetRow(element,row);return element;
    }

    sealed class SentenceCompletionScopeLabelConverter:IValueConverter
    {
        internal static string Label(CompletionContextScope value)=>value switch
        {
            CompletionContextScope.Paragraph=>"Paragraph",
            CompletionContextScope.Section=>"Section",
            CompletionContextScope.WholeDocument=>"Whole document",
            _=>"Automatic"
        };
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is CompletionContextScope scope?Label(scope):"Automatic";
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
}
