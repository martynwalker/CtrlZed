using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? characterVoiceWindow;
    Action<VoiceAuthor,CharacterVoice,WritingMode?>? characterVoiceChooseProvidedVoice;

    void InstallCharacterVoice()
    {
        void AddItem()
        {
            var review=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(x=>(x.Tag as string)=="CtrlZedReviewMenu");
            if(review==null || review.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedCharacterVoice"))return;
            var item=new MenuItem{Header="Character _Voice…",Tag="CtrlZedCharacterVoice"};
            item.Click+=(_,__)=>ShowCharacterVoice();review.Items.Add(item);
        }
        Loaded+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        Closed+=(_,__)=>characterVoiceWindow?.Close();
    }

    void AddCharacterVoiceContextMenu(ContextMenu menu)
    {
        if(menu.Items.OfType<MenuItem>().Any(x=>(x.Tag as string)=="CtrlZedCharacterVoice"))return;
        var item=new MenuItem{Header="Character Voice…",Tag="CtrlZedCharacterVoice"};
        item.Click+=(_,__)=>ShowCharacterVoice();menu.Items.Add(item);
    }

    /// <summary>Preview a character-led rewrite; no requests or document changes occur on opening.</summary>
    public void ShowCharacterVoice()=>ShowCharacterVoice(null,null,null);

    internal void ShowCharacterVoice(VoiceAuthor? providedAuthor,CharacterVoice? providedVoice,WritingMode? providedMode)
    {
        if(characterVoiceWindow!=null)
        {
            if(providedAuthor!=null && providedVoice!=null)
                (characterVoiceChooseProvidedVoice??throw new InvalidOperationException("Character Voice is still opening. Try selecting the story character again."))
                    .Invoke(providedAuthor,providedVoice,providedMode);
            if(characterVoiceWindow.WindowState==WindowState.Minimized)characterVoiceWindow.WindowState=WindowState.Normal;
            characterVoiceWindow.Activate();return;
        }
        CharacterVoiceTarget? target=null;
        string? captureError=null;
        try{target=CaptureCharacterVoiceTarget();}catch(InvalidOperationException ex){captureError=ex.Message;}
        var width=Math.Max(320,SystemParameters.WorkArea.Width-32);
        var height=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window{Title="Character Voice",Owner=this,Width=Math.Min(1060,width),Height=Math.Min(850,height),
            MinWidth=Math.Min(880,width),MinHeight=Math.Min(640,height),WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()};
        characterVoiceWindow=w;NameScope.SetNameScope(w,new NameScope());
        T Named<T>(string name,T control) where T:FrameworkElement
        {
            control.Name=name;w.RegisterName(name,control);AutomationProperties.SetName(control,name.Replace("Voice",""));return control;
        }
        static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4)};
        static Button Action(string text)=>new(){Content=text,Padding=new Thickness(9,4,9,4),Margin=new Thickness(0,4,7,4),MinWidth=80};
        static TextBox Passage(int maximum)=>new(){AcceptsReturn=true,AcceptsTab=false,TextWrapping=TextWrapping.Wrap,MaxLength=maximum,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(6)};
        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(new TextBlock{Text="Character Voice",FontSize=23,FontWeight=FontWeights.SemiBold});
        var information=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.DimGray,Margin=new Thickness(0,6,0,10)};
        Grid.SetRow(information,1);root.Children.Add(information);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(255)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body,2);root.Children.Add(body);
        var options=new StackPanel();
        body.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        options.Children.Add(Label("Author"));
        var authorQuery=Named("VoiceAuthorQuery",new TextBox{MaxLength=160,Padding=new Thickness(4),ToolTip="Search locally; use Find author with AI for other authors."});
        options.Children.Add(authorQuery);
        var authors=Named("VoiceAuthors",new ListBox{DisplayMemberPath="Name",Height=72,Margin=new Thickness(0,5,0,0)});options.Children.Add(authors);
        var findAuthor=Named("VoiceFindAuthor",Action("Find author with AI"));options.Children.Add(findAuthor);
        options.Children.Add(Label("In the voice of"));
        var voiceQuery=Named("VoiceQuery",new TextBox{MaxLength=160,Padding=new Thickness(4),ToolTip="Search the selected author's characters, including familiar names such as Jack."});options.Children.Add(voiceQuery);
        var voices=Named("VoiceCharacters",new ListBox{DisplayMemberPath="Name",Height=82,Margin=new Thickness(0,5,0,0)});options.Children.Add(voices);
        var findVoices=Named("VoiceFindCharacters",Action("Find voices with AI"));options.Children.Add(findVoices);
        options.Children.Add(Label("Delivery"));
        var delivery=Named("VoiceDelivery",new ComboBox{ItemsSource=CharacterVoiceService.Deliveries,SelectedIndex=0});options.Children.Add(delivery);
        options.Children.Add(Label("What I mean / audience (optional)"));
        var hint=Named("VoiceHint",Passage(2000));hint.Height=65;options.Children.Add(hint);
        options.Children.Add(new TextBlock{Text="Search boxes filter locally. AI lookups run only when requested and receive no passage text. New AI profiles are suggestions you can edit; they are not verified literary references.",
            TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.DimGray,Margin=new Thickness(0,10,0,4)});

        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);
        foreach(var row in new[]{new RowDefinition{Height=GridLength.Auto},new RowDefinition{Height=new GridLength(85)},
            new RowDefinition{Height=GridLength.Auto},new RowDefinition{MinHeight=90},
            new RowDefinition{Height=GridLength.Auto},new RowDefinition{MinHeight=100}})right.RowDefinitions.Add(row);
        var voiceLabel=Label("Voice notes — review or adjust the character's traits");right.Children.Add(voiceLabel);
        var profile=Named("VoiceProfile",Passage(4000));Grid.SetRow(profile,1);right.Children.Add(profile);
        var sourceHeading=new DockPanel{LastChildFill=true};
        var useSelection=Named("VoiceUseSelection",Action("Use current selection / caret"));DockPanel.SetDock(useSelection,Dock.Right);sourceHeading.Children.Add(useSelection);
        sourceHeading.Children.Add(Label("Original — selected or pasted text"));Grid.SetRow(sourceHeading,2);right.Children.Add(sourceHeading);
        var source=Named("VoiceSource",Passage(16000));source.Text=target?.OriginalText??"";Grid.SetRow(source,3);right.Children.Add(source);
        right.Children.Add(WithRow(Label("Rewrite — edit before applying or copying"),4));
        var result=Named("VoiceResult",Passage(24000));Grid.SetRow(result,5);right.Children.Add(result);

        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};
        var destination=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5)};footer.Children.Add(destination);
        var status=Named("VoiceStatus",new TextBlock{TextWrapping=TextWrapping.Wrap,MinHeight=32,Foreground=System.Windows.Media.Brushes.DimGray});footer.Children.Add(status);
        var actions=new DockPanel();
        var settings=new WrapPanel();
        var languageSetup=Action("Language settings…");var aiSetup=Action("AI settings…");settings.Children.Add(languageSetup);settings.Children.Add(aiSetup);
        DockPanel.SetDock(settings,Dock.Left);actions.Children.Add(settings);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var generate=Named("VoiceGenerate",Action("Rewrite"));var cancel=Named("VoiceCancel",Action("Cancel request"));
        var apply=Named("VoiceApply",Action("Apply"));var copy=Named("VoiceCopy",Action("Copy"));var close=Action("Close");close.IsCancel=true;
        foreach(var button in new[]{generate,cancel,apply,copy,close})buttons.Children.Add(button);
        actions.Children.Add(buttons);footer.Children.Add(actions);Grid.SetRow(footer,3);root.Children.Add(footer);w.Content=root;

        var additionalAuthors=new List<VoiceAuthor>();var additionalVoices=new List<CharacterVoice>();
        var initialising=true;var assigning=false;var closed=false;var busy=false;var applying=false;var validResult=false;
        long generation=0;CancellationTokenSource? cancellation=null;string configuration="";string resultConfiguration="";string? unavailable=null;
        string? providedAuthorId=null;WritingMode? characterWritingMode=null;
        VoiceAuthor? Author()=>authors.SelectedItem as VoiceAuthor;
        CharacterVoice? Voice()=>voices.SelectedItem as CharacterVoice;
        WritingMode EffectiveWritingMode()=>Author()?.Id==providedAuthorId && characterWritingMode.HasValue
            ?characterWritingMode.Value:languageSettings.WritingMode;
        string Configuration()
        {
            var language=$"{languageSettings.AllowAiLanguageServices}|{languageSettings.WritingMode}|{languageSettings.LanguageTag}|{languageSettings.CustomProfileName}|{EffectiveWritingMode()}";
            if(CharacterVoiceService.GeneratorOverride!=null)return "Test provider|"+language;
            var provider=AiManager.GetDefaultProvider();var ai=AiDirectService.GetProfile(provider);
            return provider+"|"+ai.Model+"|"+ai.HasKey+"|"+language;
        }
        void CancelRequest()
        {
            generation++;var old=cancellation;cancellation=null;old?.Cancel();old?.Dispose();busy=false;
        }
        void UpdateButtons()
        {
            var selected=Author()!=null && Voice()!=null && Voice()!.AuthorId==Author()!.Id;
            generate.IsEnabled=!closed && !busy && unavailable==null && selected && !string.IsNullOrWhiteSpace(profile.Text) && !string.IsNullOrWhiteSpace(source.Text);
            findAuthor.IsEnabled=!closed && !busy && unavailable==null && !string.IsNullOrWhiteSpace(authorQuery.Text);
            findVoices.IsEnabled=!closed && !busy && unavailable==null && Author()!=null;
            cancel.IsEnabled=busy;profile.IsEnabled=selected;
            var current=target!=null && IsCharacterVoiceTargetCurrent(target);
            apply.Content=target?.IsReplacement==true?"Replace selection":"Insert at caret";
            apply.IsEnabled=!closed && !busy && validResult && !string.IsNullOrWhiteSpace(result.Text) && unavailable==null && resultConfiguration==configuration && current && !Editor.IsReadOnly && Editor.IsEnabled;
            copy.IsEnabled=!closed && !busy && validResult && !string.IsNullOrWhiteSpace(result.Text);
            destination.Text=target==null?"No current document destination. Use current selection / caret to capture one; you can still rewrite and copy pasted text.":
                target.IsReplacement?$"Apply replaces the captured selection ({target.OriginalText.Length:N0} characters). Rewritten text uses its opening format. Ctrl+Z restores the original.":
                "Apply inserts at the captured caret using its format. Rewriting and editing this preview do not change the document.";
        }
        void Invalidate(string message)
        {
            if(closed)return;CancelRequest();validResult=false;result.Text="";status.Text=message;UpdateButtons();
        }
        void RefreshConfiguration()
        {
            if(closed)return;
            try
            {
                var current=Configuration();
                unavailable=!languageSettings.AllowAiLanguageServices?"Enable AI language services in Language settings before requesting a rewrite or a lookup.":null;
                var provider=AiManager.GetDefaultProvider();
                if(CharacterVoiceService.GeneratorOverride==null)
                {
                    if(provider=="Copilot")unavailable="Choose ChatGPT, Claude or Gemini in AI settings for direct requests.";
                    else if(!AiDirectService.GetProfile(provider).HasKey)unavailable=$"Configure a {provider} API key in AI settings before requesting a rewrite or a lookup.";
                }
                if(configuration.Length>0 && configuration!=current)Invalidate("AI or Writing Mode settings changed. Request a fresh rewrite.");
                configuration=current;
                information.Text=$"Writing mode: {WritingModeProfiles.Get(EffectiveWritingMode()).DisplayName}. Uses {provider}. Only Original, voice notes, delivery and your optional hint are sent when you choose Rewrite. No wider document text is read.";
                UpdateButtons();
            }
            catch(Exception ex){unavailable="Could not read AI settings: "+ex.Message;Invalidate(unavailable);}
        }
        void RefreshVoices()
        {
            voices.ItemsSource=Author()==null?Array.Empty<CharacterVoice>():CharacterVoiceService.SearchVoices(Author()!.Id,voiceQuery.Text,additionalVoices);
            voices.SelectedIndex=-1;
        }
        void RefreshAuthors()
        {
            var previous=Author()?.Id;authors.ItemsSource=CharacterVoiceService.SearchAuthors(authorQuery.Text,additionalAuthors);
            authors.SelectedItem=authors.Items.Cast<VoiceAuthor>().FirstOrDefault(x=>x.Id==previous);
        }
        void ChooseProvidedVoice(VoiceAuthor author,CharacterVoice voice,WritingMode? mode)
        {
            if(voice.AuthorId!=author.Id)throw new InvalidOperationException("This character belongs to a different story.");
            Invalidate("Story character selected. Review the voice notes before rewriting.");
            assigning=true;
            try
            {
                additionalAuthors.RemoveAll(item=>item.Id==author.Id);additionalAuthors.Add(author);
                additionalVoices.RemoveAll(item=>item.Id==voice.Id);additionalVoices.Add(voice);
                providedAuthorId=author.Id;characterWritingMode=mode;
                authorQuery.Text="";voiceQuery.Text="";RefreshAuthors();
                authors.SelectedItem=authors.Items.Cast<VoiceAuthor>().First(item=>item.Id==author.Id);
                RefreshVoices();voices.SelectedItem=voices.Items.Cast<CharacterVoice>().First(item=>item.Id==voice.Id);
                profile.Text=voice.Profile;
            }
            finally{assigning=false;}
            RefreshConfiguration();status.Text=unavailable??"Story character selected. Review the voice notes before rewriting.";UpdateButtons();
        }
        async Task RequestAsync(string kind)
        {
            if(closed || busy)return;RefreshConfiguration();
            if(unavailable!=null){status.Text=unavailable;UpdateButtons();return;}
            var author=Author();var voice=Voice();var requestedQuery=kind=="author"?authorQuery.Text:voiceQuery.Text;
            if(kind!="author" && author==null){status.Text="Select an author first.";return;}
            if(kind=="rewrite" && (voice==null || voice.AuthorId!=author!.Id)){status.Text="Select a character belonging to this author.";return;}
            Invalidate(kind=="rewrite"?"Preparing the character rewrite…":"Looking up suggestions…");
            var requestedConfiguration=configuration;var requestGeneration=++generation;cancellation=new CancellationTokenSource();var token=cancellation.Token;
            busy=true;UpdateButtons();
            try
            {
                if(kind=="author")
                {
                    var found=await CharacterVoiceService.FindAuthorsAsync(requestedQuery,token);
                    if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;
                    RefreshConfiguration();if(configuration!=requestedConfiguration || requestGeneration!=generation)return;
                    foreach(var item in found)if(!additionalAuthors.Any(x=>x.Id==item.Id) && !CharacterVoiceService.BuiltInAuthors.Any(x=>x.Id==item.Id))additionalAuthors.Add(item);
                    assigning=true;authors.ItemsSource=found;authors.SelectedIndex=-1;RefreshVoices();profile.Text="";assigning=false;
                    status.Text=found.Count==0?"No confident author matches. Try a fuller name.":"AI author suggestions. Select the intended author, then find their voices.";
                }
                else if(kind=="voices")
                {
                    var found=await CharacterVoiceService.FindVoicesAsync(author!,requestedQuery,token);
                    if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;
                    RefreshConfiguration();if(configuration!=requestedConfiguration || requestGeneration!=generation || Author()?.Id!=author!.Id)return;
                    foreach(var item in found)if(!additionalVoices.Any(x=>x.Id==item.Id) && !CharacterVoiceService.BuiltInVoices.Any(x=>x.Id==item.Id))additionalVoices.Add(item);
                    assigning=true;voices.ItemsSource=found;voices.SelectedIndex=-1;profile.Text="";assigning=false;
                    status.Text=found.Count==0?"No confident character matches. Try a fuller name or a work title.":"AI voice suggestions. Select a character and review the editable voice notes before rewriting.";
                }
                else
                {
                    var request=new VoiceRewriteRequest(author!,voice! with{Profile=profile.Text},WritingModeProfiles.Get(EffectiveWritingMode()),source.Text,delivery.SelectedItem as string??"Spoken",hint.Text);
                    var text=await CharacterVoiceService.RewriteAsync(request,token);
                    if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;
                    RefreshConfiguration();if(configuration!=requestedConfiguration || requestGeneration!=generation)return;
                    result.Text=text;validResult=true;resultConfiguration=configuration;
                    status.Text="Review or edit the rewrite, then apply it or copy it. The original document is unchanged until you apply.";
                }
            }
            catch(OperationCanceledException){if(!closed && requestGeneration==generation)status.Text="Request cancelled.";}
            catch(Exception ex){if(!closed && requestGeneration==generation)status.Text="Could not complete the request: "+ex.Message;}
            finally{assigning=false;if(!closed && requestGeneration==generation){busy=false;cancellation?.Dispose();cancellation=null;UpdateButtons();}}
        }
        authorQuery.TextChanged+=(_,__)=>{if(initialising || assigning)return;Invalidate("Author search changed. Select an author, or request AI suggestions.");RefreshAuthors();};
        authors.SelectionChanged+=(_,__)=>
        {
            if(assigning)return;Invalidate("Choose a character for this author.");
            assigning=true;voiceQuery.Text="";RefreshVoices();profile.Text="";assigning=false;UpdateButtons();
        };
        voiceQuery.TextChanged+=(_,__)=>{if(initialising || assigning)return;Invalidate("Voice search changed. Select a character, or request AI suggestions.");RefreshVoices();};
        voices.SelectionChanged+=(_,__)=>
        {
            if(assigning)return;Invalidate("Review the character's voice notes, then choose Rewrite.");
            assigning=true;profile.Text=Voice()?.Profile??"";assigning=false;UpdateButtons();
        };
        foreach(var box in new[]{source,profile,hint})box.TextChanged+=(_,__)=>{if(!initialising && !assigning)Invalidate("Passage or voice notes changed. Choose Rewrite for a fresh preview.");};
        delivery.SelectionChanged+=(_,__)=>{if(!initialising)Invalidate("Delivery changed. Choose Rewrite for a fresh preview.");};
        result.TextChanged+=(_,__)=>UpdateButtons();
        findAuthor.Click+=async (_,__)=>await RequestAsync("author");findVoices.Click+=async (_,__)=>await RequestAsync("voices");
        generate.Click+=async (_,__)=>await RequestAsync("rewrite");cancel.Click+=(_,__)=>Invalidate("Request cancelled. Choose Rewrite when ready.");
        useSelection.Click+=(_,__)=>
        {
            Invalidate("Document destination captured. Choose Rewrite when ready.");
            try
            {
                target=CaptureCharacterVoiceTarget();
                if(target.IsReplacement){assigning=true;source.Text=target.OriginalText;assigning=false;}
            }
            catch(InvalidOperationException ex){target=null;status.Text=ex.Message;}
            UpdateButtons();
        };
        apply.Click+=(_,__)=>
        {
            RefreshConfiguration();if(!apply.IsEnabled || target==null)return;
            try
            {
                applying=true;ApplyCharacterVoiceRewrite(target,result.Text);target=null;
                Invalidate("Rewrite applied. Ctrl+Z restores the original. Use current selection / caret to work on another passage.");
            }
            catch(InvalidOperationException ex){Invalidate(ex.Message);}
            finally{applying=false;UpdateButtons();}
        };
        copy.Click+=(_,__)=>
        {
            if(!copy.IsEnabled)return;
            try{Clipboard.SetText(result.Text);status.Text="Rewrite copied as plain text.";}
            catch(System.Runtime.InteropServices.ExternalException){status.Text="The clipboard is busy. Try Copy again.";}
        };
        languageSetup.Click+=(_,__)=>{Invalidate("Language settings opened. Request a fresh rewrite afterwards.");LanguageSettings_Click(languageSetup,new RoutedEventArgs());RefreshConfiguration();};
        aiSetup.Click+=(_,__)=>{Invalidate("AI settings opened. Request a fresh rewrite afterwards.");AiManager_Click(aiSetup,new RoutedEventArgs());RefreshConfiguration();};
        close.Click+=(_,__)=>w.Close();
        TextChangedEventHandler documentChanged=(_,__)=>
        {
            if(closed || applying)return;target=null;Invalidate("The document changed. Use current selection / caret before applying a rewrite.");
        };
        var readOnlyDescriptor=DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty,typeof(RichTextBox));
        EventHandler availabilityChanged=(_,__)=>UpdateButtons();
        Editor.TextChanged+=documentChanged;readOnlyDescriptor?.AddValueChanged(Editor,availabilityChanged);
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};
        timer.Tick+=(_,__)=>RefreshConfiguration();timer.Start();w.Activated+=(_,__)=>RefreshConfiguration();
        w.Closed+=(_,__)=>
        {
            closed=true;CancelRequest();timer.Stop();Editor.TextChanged-=documentChanged;readOnlyDescriptor?.RemoveValueChanged(Editor,availabilityChanged);
            if(ReferenceEquals(characterVoiceWindow,w)){characterVoiceWindow=null;characterVoiceChooseProvidedVoice=null;}
        };
        characterVoiceChooseProvidedVoice=ChooseProvidedVoice;
        RefreshAuthors();authors.SelectedItem=authors.Items.Cast<VoiceAuthor>().FirstOrDefault(x=>x.Name.Contains("O'Brian",StringComparison.OrdinalIgnoreCase))??authors.Items.Cast<VoiceAuthor>().FirstOrDefault();
        initialising=false;RefreshConfiguration();status.Text=unavailable??captureError??"Choose an author and character, then rewrite selected or pasted text. Nothing is requested automatically.";
        if(providedAuthor!=null && providedVoice!=null)ChooseProvidedVoice(providedAuthor,providedVoice,providedMode);
        UpdateButtons();w.Show();
    }
}
