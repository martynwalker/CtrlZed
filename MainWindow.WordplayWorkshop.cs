using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? wordplayWorkshopWindow;

    void InstallWordplayWorkshop()
    {
        void AddItem()
        {
            var review=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item=>(item.Tag as string)=="CtrlZedReviewMenu");
            if(review==null || review.Items.OfType<MenuItem>().Any(item=>(item.Tag as string)=="CtrlZedWordplayWorkshop"))return;
            var item=new MenuItem{Header="_Wordplay Workshop…",Tag="CtrlZedWordplayWorkshop"};
            item.Click+=(_,__)=>ShowWordplayWorkshop();review.Items.Add(item);
        }
        Loaded+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(AddItem),DispatcherPriority.ApplicationIdle);
        Closed+=(_,__)=>wordplayWorkshopWindow?.Close();
    }

    void AddWordplayWorkshopContextMenu(ContextMenu menu)
    {
        if(menu.Items.OfType<MenuItem>().Any(item=>(item.Tag as string)=="CtrlZedWordplayWorkshop"))return;
        var item=new MenuItem{Header="Wordplay Workshop…",Tag="CtrlZedWordplayWorkshop"};
        item.Click+=(_,__)=>ShowWordplayWorkshop();menu.Items.Add(item);
    }

    /// <summary>Open a local word pile and saved ideas. Exploration and manuscript changes are explicit.</summary>
    public void ShowWordplayWorkshop()
    {
        if(wordplayWorkshopWindow!=null)
        {
            if(wordplayWorkshopWindow.WindowState==WindowState.Minimized)wordplayWorkshopWindow.WindowState=WindowState.Normal;
            wordplayWorkshopWindow.Activate();return;
        }
        var document=Editor.Document;var state=SnapshotWordplayWorkshop();
        CharacterVoiceTarget? destination=null;string? captureError=null;
        try{destination=CaptureCharacterVoiceTarget();}catch(InvalidOperationException error){captureError=error.Message;}
        var initialSeed=state.SeedText;
        if(!state.HasContent && destination?.IsReplacement==true)
        {
            if(destination.OriginalText.Length<=12000)initialSeed=destination.OriginalText;
            else captureError="Select up to 12,000 characters for the word pile. Nothing has been copied or shortened.";
        }
        var width=Math.Max(320,SystemParameters.WorkArea.Width-32);var height=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window{Title="Wordplay Workshop",Owner=this,Width=Math.Min(1100,width),Height=Math.Min(850,height),
            MinWidth=Math.Min(880,width),MinHeight=Math.Min(640,height),WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Icon=Branding.CreateIconImageSource()};
        wordplayWorkshopWindow=w;NameScope.SetNameScope(w,new NameScope());
        var assigning=true;var committing=false;var applying=false;var closed=false;var busy=false;
        long generation=0;CancellationTokenSource? cancellation=null;string configuration="";string? unavailable=null;
        var removed=new List<(WordplayFavourite Favourite,int Index)>();
        T Named<T>(string name,T control) where T:FrameworkElement
        {control.Name=name;w.RegisterName(name,control);AutomationProperties.SetName(control,name.Replace("Wordplay",""));return control;}
        static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,7,0,4)};
        static Button Button(string text)=>new(){Content=text,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,4,7,4),MinWidth=72};
        static TextBox Text(int maximum)=>new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=maximum,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(6)};
        static void WrappedItems(ListBox list,string path)
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(path));
            text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(FrameworkElement.MarginProperty,new Thickness(5));
            list.ItemTemplate=new DataTemplate{VisualTree=text};list.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        }
        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(new TextBlock{Text="Wordplay Workshop",FontSize=23,FontWeight=FontWeights.SemiBold});
        var information=new TextBlock{Text="Pile up words, images and fragments, then explore their connections. The word pile, choices, results and favourites are saved in this .czd document. Nothing is generated until Explore.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,7,0,10)};
        Grid.SetRow(information,1);root.Children.Add(information);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(218)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,2);root.Children.Add(body);
        var options=new StackPanel();body.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        ComboBox Choice(string name,string label,IReadOnlyList<string> choices,string selected)
        {
            options.Children.Add(Label(label));var box=Named(name,new ComboBox{ItemsSource=choices,SelectedItem=selected,MinWidth=170});options.Children.Add(box);return box;
        }
        var form=Choice("WordplayForm","Form / genre",WordplayWorkshopChoices.Forms,state.Options.Form);
        var style=Choice("WordplayStyle","Style",WordplayWorkshopChoices.Styles,state.Options.Style);
        var comic=Choice("WordplayComicFlavour","Comic flavour",WordplayWorkshopChoices.ComicFlavours,state.Options.ComicFlavour);
        var device=Choice("WordplayDevice","Wordplay device",WordplayWorkshopChoices.Devices,state.Options.Device);
        var subject=Choice("WordplaySubject","Subject matter and target",WordplayWorkshopChoices.SubjectTargets,state.Options.SubjectTarget);
        var situation=Choice("WordplaySituation","Physical / situational",WordplayWorkshopChoices.Situations,state.Options.Situation);
        options.Children.Add(Label("Target or intention (optional)"));
        var target=Named("WordplayTarget",Text(1000));target.Height=62;target.Text=state.Target;options.Children.Add(target);
        options.Children.Add(new TextBlock{Text="Choices combine freely. General leaves that aspect open; humour is optional.",TextWrapping=TextWrapping.Wrap,
            Foreground=Brushes.DimGray,Margin=new Thickness(0,9,0,4)});

        var right=new Grid();Grid.SetColumn(right,2);body.Children.Add(right);
        right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(90),MinHeight=65});
        right.RowDefinitions.Add(new RowDefinition{MinHeight=140});right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(64),MinHeight=48});right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var seedHeading=new DockPanel();var useSelection=Named("WordplayUseSelection",Button("Use current selection"));DockPanel.SetDock(useSelection,Dock.Right);seedHeading.Children.Add(useSelection);seedHeading.Children.Add(Label("Word pile — words, images and sentences"));right.Children.Add(seedHeading);
        var seed=Named("WordplaySeed",Text(12000));seed.Text=initialSeed;Grid.SetRow(seed,1);right.Children.Add(seed);
        var tabs=Named("WordplayTabs",new TabControl{Margin=new Thickness(0,9,0,0)});Grid.SetRow(tabs,2);right.Children.Add(tabs);
        var exploration=new Grid{Margin=new Thickness(6)};exploration.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
        exploration.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});exploration.ColumnDefinitions.Add(new ColumnDefinition());
        var connections=Named("WordplayConnections",new ListBox());WrappedItems(connections,"Title");exploration.Children.Add(connections);
        var connectionDetail=new Grid();connectionDetail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});connectionDetail.RowDefinitions.Add(new RowDefinition());Grid.SetColumn(connectionDetail,2);exploration.Children.Add(connectionDetail);
        var bridge=Named("WordplayBridge",new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6),Foreground=Brushes.DimGray});
        connectionDetail.Children.Add(new ScrollViewer{Content=bridge,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=95});
        var examples=Named("WordplayExamples",new ListBox());WrappedItems(examples,"Text");Grid.SetRow(examples,1);connectionDetail.Children.Add(examples);
        tabs.Items.Add(new TabItem{Header="Connections",Content=exploration});
        var favouritesPage=new Grid{Margin=new Thickness(6)};favouritesPage.RowDefinitions.Add(new RowDefinition());favouritesPage.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});favouritesPage.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var favourites=Named("WordplayFavourites",new ListBox());WrappedItems(favourites,"Text");favouritesPage.Children.Add(favourites);
        var favouriteContext=Named("WordplayFavouriteContext",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(4)});
        var contextExpander=new Expander{Header="Original connection and choices",Content=new ScrollViewer{Content=favouriteContext,MaxHeight=90,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Margin=new Thickness(0,4,0,0)};
        Grid.SetRow(contextExpander,1);favouritesPage.Children.Add(contextExpander);
        var favouriteActions=new WrapPanel();var useFavourite=Named("WordplayUseFavourite",Button("Use selected"));var removeFavourite=Named("WordplayRemoveFavourite",Button("Remove favourite"));var undoRemove=Named("WordplayUndoRemove",Button("Undo remove"));
        favouriteActions.Children.Add(useFavourite);favouriteActions.Children.Add(removeFavourite);favouriteActions.Children.Add(undoRemove);Grid.SetRow(favouriteActions,2);favouritesPage.Children.Add(favouriteActions);
        tabs.Items.Add(new TabItem{Header="Favourites",Content=favouritesPage});
        var editLabel=Named("WordplayEditLabel",Label("Edit the selected example before copying, keeping or applying"));Grid.SetRow(editLabel,3);right.Children.Add(editLabel);
        var edit=Named("WordplayEdit",Text(1000));Grid.SetRow(edit,4);right.Children.Add(edit);
        var ideaActions=new WrapPanel();var copy=Named("WordplayCopy",Button("Copy"));var keep=Named("WordplayKeep",Button("Keep favourite"));var apply=Named("WordplayApply",Button("Insert at caret"));var useDestination=Named("WordplayUseDestination",Button("Use current destination"));
        foreach(var button in new[]{copy,keep,apply,useDestination})ideaActions.Children.Add(button);Grid.SetRow(ideaActions,5);right.Children.Add(ideaActions);
        var footer=new StackPanel{Margin=new Thickness(0,9,0,0)};
        var destinationText=Named("WordplayDestination",new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5)});footer.Children.Add(destinationText);
        var status=Named("WordplayStatus",new TextBlock{TextWrapping=TextWrapping.Wrap,MinHeight=30,Foreground=Brushes.DimGray});footer.Children.Add(status);
        var footerActions=new DockPanel();var settings=new WrapPanel();var languageSetup=Button("Language settings…");var aiSetup=Button("AI settings…");settings.Children.Add(languageSetup);settings.Children.Add(aiSetup);DockPanel.SetDock(settings,Dock.Left);footerActions.Children.Add(settings);
        var generationActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var explore=Named("WordplayExplore",Button("Explore"));var cancel=Named("WordplayCancel",Button("Cancel request"));var close=Button("Close");close.IsCancel=true;
        generationActions.Children.Add(explore);generationActions.Children.Add(cancel);generationActions.Children.Add(close);footerActions.Children.Add(generationActions);footer.Children.Add(footerActions);Grid.SetRow(footer,3);root.Children.Add(footer);w.Content=root;

        bool Editable()=>!closed && ReferenceEquals(Editor.Document,document) && !Editor.IsReadOnly && Editor.IsEnabled;
        WordplayConnection? Connection()=>connections.SelectedItem as WordplayConnection;
        WordplayExample? Example()=>examples.SelectedItem as WordplayExample;
        WordplayFavourite? Favourite()=>favourites.SelectedItem as WordplayFavourite;
        bool UsingFavourite()=>tabs.SelectedIndex==1;
        bool HasOrigin()=>UsingFavourite()?Favourite()!=null:Connection()!=null && Example()!=null;
        WordplayWorkshopOptions Choices()=>new(){Form=form.SelectedItem as string??"General",Style=style.SelectedItem as string??"General",
            ComicFlavour=comic.SelectedItem as string??"General",Device=device.SelectedItem as string??"General",SubjectTarget=subject.SelectedItem as string??"General",Situation=situation.SelectedItem as string??"General"};
        static bool SameOptions(WordplayWorkshopOptions left,WordplayWorkshopOptions right)=>left.Form==right.Form && left.Style==right.Style && left.ComicFlavour==right.ComicFlavour && left.Device==right.Device && left.SubjectTarget==right.SubjectTarget && left.Situation==right.Situation;
        void CancelRequest(string? message=null)
        {generation++;var previous=cancellation;cancellation=null;previous?.Cancel();previous?.Dispose();busy=false;if(message!=null)status.Text=message;}
        void UpdateButtons()
        {
            var editable=Editable();var phrase=HasOrigin() && !string.IsNullOrWhiteSpace(edit.Text);
            foreach(var box in new[]{form,style,comic,device,subject,situation})box.IsEnabled=editable;
            seed.IsReadOnly=!editable;target.IsReadOnly=!editable;edit.IsReadOnly=!editable || busy;
            explore.IsEnabled=editable && !busy && unavailable==null && !string.IsNullOrWhiteSpace(seed.Text);cancel.IsEnabled=busy;
            useSelection.IsEnabled=editable;copy.IsEnabled=!closed && !busy && phrase;
            keep.IsEnabled=editable && !busy && phrase && (UsingFavourite() || state.Favourites.Count<40);
            keep.Content=UsingFavourite()?"Update favourite":"Keep favourite";
            useFavourite.IsEnabled=!closed && !busy && Favourite()!=null;removeFavourite.IsEnabled=editable && !busy && Favourite()!=null;
            undoRemove.IsEnabled=editable && !busy && removed.Count>0 && state.Favourites.Count<40;
            useDestination.IsEnabled=editable;
            var current=destination!=null && IsCharacterVoiceTargetCurrent(destination);
            apply.Content=destination?.IsReplacement==true?"Replace selection":"Insert at caret";
            apply.IsEnabled=editable && !busy && phrase && current;
            destinationText.Text=destination==null?"No current manuscript destination. Copy an idea, or use current destination before applying it.":
                destination.IsReplacement?$"Apply replaces the captured selection ({destination.OriginalText.Length:N0} characters) using its opening format. Ctrl+Z restores the original.":
                "Apply inserts at the captured caret using its format. Copy and Keep favourite do not change the manuscript.";
        }
        void ShowIdea()
        {
            if(assigning)return;assigning=true;
            try
            {
                if(UsingFavourite())
                {
                    var selected=Favourite();edit.Text=selected?.Text??"";
                    bridge.Text=selected==null?"Choose a kept idea.":"Kept connection: "+selected.Title+"\nAnchors: "+string.Join(" · ",selected.Anchors)+"\n"+selected.Bridge;
                    editLabel.Text=selected==null?"Edit the selected idea":"Kept idea — "+selected.Title+" ("+selected.Device+")";
                    favouriteContext.Text=selected==null?"Choose a kept idea to see its original connection.":bridge.Text+
                        "\n\nOriginal choices: "+selected.Options.Form+" · "+selected.Options.Style+" · "+selected.Options.ComicFlavour+" · "+selected.Options.Device+" · "+selected.Options.SubjectTarget+" · "+selected.Options.Situation+
                        (string.IsNullOrWhiteSpace(selected.Target)?"":"\nTarget or intention: "+selected.Target);
                }
                else
                {
                    edit.Text=Example()?.Text??"";var connection=Connection();
                    bridge.Text=connection==null?"Explore the word pile to find six different connections.":"Anchors: "+string.Join(" · ",connection.Anchors)+"\n"+connection.Bridge;
                    editLabel.Text=Example()==null?"Edit the selected example before copying, keeping or applying":"Edit the example — "+Example()!.Device;
                }
            }
            finally{assigning=false;}UpdateButtons();
        }
        void RefreshViews(bool updateInputs=true)
        {
            var connectionId=Connection()?.Id;var exampleIndex=examples.SelectedIndex;var favouriteId=Favourite()?.Id;var wasAssigning=assigning;assigning=true;
            try
            {
                state=SnapshotWordplayWorkshop();
                if(updateInputs)
                {
                    seed.Text=state.SeedText;target.Text=state.Target;form.SelectedItem=state.Options.Form;style.SelectedItem=state.Options.Style;
                    comic.SelectedItem=state.Options.ComicFlavour;device.SelectedItem=state.Options.Device;subject.SelectedItem=state.Options.SubjectTarget;situation.SelectedItem=state.Options.Situation;
                }
                connections.ItemsSource=state.Results;connections.SelectedItem=state.Results.FirstOrDefault(item=>item.Id==connectionId)??state.Results.FirstOrDefault();
                examples.ItemsSource=Connection()?.Examples;examples.SelectedIndex=examples.Items.Count==0?-1:Math.Clamp(exampleIndex,0,examples.Items.Count-1);
                favourites.ItemsSource=state.Favourites;favourites.SelectedItem=state.Favourites.FirstOrDefault(item=>item.Id==favouriteId)??state.Favourites.FirstOrDefault();
            }
            finally{assigning=wasAssigning;}if(!assigning)ShowIdea();UpdateButtons();
        }
        bool Commit(WordplayWorkshopState next,string message,bool cancelPending=true,bool refreshInputs=true)
        {
            if(!Editable())return false;
            try
            {
                next.Validate();if(cancelPending)CancelRequest();committing=true;CommitWordplayWorkshop(next,message);state=SnapshotWordplayWorkshop();
                RefreshViews(refreshInputs);status.Text=message+" Save the .czd document to keep the workshop on disk.";return true;
            }
            catch(Exception error) when(error is InvalidDataException or InvalidOperationException or ArgumentException)
            {status.Text=error.Message;return false;}
            finally{committing=false;UpdateButtons();}
        }
        bool PersistInputs()
        {
            if(assigning || !Editable())return false;
            var next=SnapshotWordplayWorkshop();var optionsNow=Choices();
            var changed=next.SeedText!=seed.Text || next.Target!=target.Text || !SameOptions(next.Options,optionsNow);
            if(!changed)return true;
            next.SeedText=seed.Text;next.Target=target.Text;next.Options=optionsNow;next.Results.Clear();
            return Commit(next,"Word pile or choices updated; explore for fresh connections.",true,false);
        }
        string Configuration()
        {
            var language=$"{languageSettings.AllowAiLanguageServices}|{languageSettings.LanguageTag}|{languageSettings.WritingMode}|{languageSettings.CustomProfileName}";
            if(WordplayWorkshopService.GeneratorOverride!=null)return "Test provider|"+language;
            var provider=AiManager.GetDefaultProvider();var profile=AiDirectService.GetProfile(provider);return provider+"|"+profile.Model+"|"+profile.HasKey+"|"+language;
        }
        void RefreshAvailability()
        {
            if(closed)return;
            try
            {
                var next=Configuration();if(configuration.Length>0 && configuration!=next)CancelRequest("AI settings changed. Existing ideas remain available; explore again when ready.");configuration=next;
                unavailable=!languageSettings.AllowAiLanguageServices?"Enable AI language services in Language settings before exploring.":null;
                if(WordplayWorkshopService.GeneratorOverride==null)
                {
                    var provider=AiManager.GetDefaultProvider();
                    if(provider=="Copilot")unavailable="Choose ChatGPT, Claude or Gemini in AI settings for direct exploration.";
                    else if(!AiDirectService.GetProfile(provider).HasKey)unavailable=$"Configure a {provider} API key in AI settings before exploring.";
                }
            }
            catch(Exception error){unavailable="AI settings could not be read: "+error.Message;CancelRequest(unavailable);}UpdateButtons();
        }
        async Task ExploreAsync()
        {
            if(closed || busy || !Editable())return;RefreshAvailability();if(unavailable!=null){status.Text=unavailable;return;}
            if(!PersistInputs())return;
            var requested=SnapshotWordplayWorkshop();var requestedRevision=wordplayWorkshopRevision;var requestedConfiguration=configuration;
            CancelRequest();var requestGeneration=++generation;cancellation=new CancellationTokenSource();var token=cancellation.Token;busy=true;UpdateButtons();status.Text="Finding connections and fresh examples…";
            try
            {
                var result=await WordplayWorkshopService.GenerateAsync(requested,token);
                if(closed || token.IsCancellationRequested || requestGeneration!=generation)return;RefreshAvailability();
                if(!Editable() || generation!=requestGeneration || configuration!=requestedConfiguration || wordplayWorkshopRevision!=requestedRevision || SnapshotWordplayWorkshop().Id!=requested.Id)
                {status.Text="The word pile, choices or AI settings changed. Explore again when ready.";return;}
                var next=SnapshotWordplayWorkshop();next.Results=result.Select(item=>item.DeepClone()).ToList();
                if(Commit(next,"Six connections are ready. Choose an example, edit it, then copy or keep it.",false,false))
                {
                    tabs.SelectedIndex=0;connections.SelectedIndex=0;examples.SelectedIndex=0;ShowIdea();
                }
            }
            catch(OperationCanceledException){if(!closed && requestGeneration==generation)status.Text="Exploration cancelled. The word pile and kept ideas are unchanged.";}
            catch(Exception error){if(!closed && requestGeneration==generation)status.Text="Could not explore the word pile: "+error.Message;}
            finally{if(!closed && requestGeneration==generation){busy=false;cancellation?.Dispose();cancellation=null;UpdateButtons();}}
        }
        foreach(var box in new[]{form,style,comic,device,subject,situation})box.SelectionChanged+=(_,__)=>{if(!assigning)PersistInputs();};
        seed.TextChanged+=(_,__)=>{if(!assigning)PersistInputs();UpdateButtons();};target.TextChanged+=(_,__)=>{if(!assigning)PersistInputs();};
        edit.TextChanged+=(_,__)=>UpdateButtons();
        connections.SelectionChanged+=(_,__)=>
        {
            if(assigning)return;assigning=true;
            try{examples.ItemsSource=Connection()?.Examples;examples.SelectedIndex=examples.Items.Count>0?0:-1;}
            finally{assigning=false;}if(!UsingFavourite())ShowIdea();
        };
        examples.SelectionChanged+=(_,__)=>{if(!assigning && !UsingFavourite())ShowIdea();};
        favourites.SelectionChanged+=(_,__)=>{if(!assigning && UsingFavourite())ShowIdea();};tabs.SelectionChanged+=(_,args)=>{if(ReferenceEquals(args.Source,tabs) && !assigning)ShowIdea();};
        explore.Click+=async (_,__)=>await ExploreAsync();cancel.Click+=(_,__)=>{CancelRequest("Exploration cancelled. Existing ideas remain available.");UpdateButtons();};
        seed.PreviewKeyDown+=async (_,args)=>{if(args.Key==Key.Enter && (Keyboard.Modifiers&ModifierKeys.Control)!=0){args.Handled=true;await ExploreAsync();}};
        useSelection.Click+=(_,__)=>
        {
            if(!Editable())return;
            try
            {
                var selected=CaptureCharacterVoiceTarget();
                if(!selected.IsReplacement)throw new InvalidOperationException("Select the words or sentences to add to the word pile first.");
                if(selected.OriginalText.Length>12000)throw new InvalidOperationException("Use a selection of up to 12,000 characters. Nothing has been copied or shortened.");
                seed.Text=selected.OriginalText;PersistInputs();
            }
            catch(InvalidOperationException error){status.Text=error.Message;}
        };
        useDestination.Click+=(_,__)=>
        {
            if(!Editable())return;
            try{destination=CaptureCharacterVoiceTarget();status.Text="Manuscript destination captured. Review the Apply description before using it.";}
            catch(InvalidOperationException error){destination=null;status.Text=error.Message;}UpdateButtons();
        };
        copy.Click+=(_,__)=>
        {
            if(!copy.IsEnabled)return;
            try
            {
                WordplayWorkshopState.ValidateText(edit.Text,WordplayWorkshopState.MaximumPhraseLength,"edited idea",false);
                Clipboard.SetText(edit.Text);status.Text="Edited idea copied as plain text. The manuscript is unchanged.";
            }
            catch(InvalidDataException error){status.Text=error.Message;}
            catch(System.Runtime.InteropServices.ExternalException){status.Text="The clipboard is busy. Try Copy again.";}
        };
        keep.Click+=(_,__)=>
        {
            if(!keep.IsEnabled)return;var wording=edit.Text;
            try
            {
                var next=SnapshotWordplayWorkshop();var kept=false;
                if(UsingFavourite() && Favourite() is { } selected)
                {
                    var saved=next.Favourites.First(item=>item.Id==selected.Id);saved.Text=wording;
                    kept=Commit(next,"Favourite updated.",true,false);
                }
                else if(Connection() is { } connection)
                {
                    var saved=WordplayFavourite.FromConnection(connection,wording,state.Options,state.Target,Example()?.Device);
                    next.Favourites.Add(saved);kept=Commit(next,"Idea kept with its connection and choices.",true,false);
                }
                // Refreshing saved metadata must not replace the writer's edited
                // phrase with the original generated example before Copy/Apply.
                if(kept){edit.Text=wording;UpdateButtons();}
            }
            catch(Exception error) when(error is InvalidDataException or InvalidOperationException or ArgumentException){status.Text=error.Message;}
        };
        useFavourite.Click+=(_,__)=>{if(Favourite()!=null){tabs.SelectedIndex=1;ShowIdea();edit.Focus();}};
        removeFavourite.Click+=(_,__)=>
        {
            if(!removeFavourite.IsEnabled || Favourite() is not { } selected)return;var next=SnapshotWordplayWorkshop();var index=next.Favourites.FindIndex(item=>item.Id==selected.Id);var saved=next.Favourites[index].DeepClone();next.Favourites.RemoveAt(index);
            if(Commit(next,"Favourite removed. Undo remove can restore it.",true,false)){removed.Add((saved,index));while(removed.Count>10)removed.RemoveAt(0);UpdateButtons();}
        };
        undoRemove.Click+=(_,__)=>
        {
            if(!undoRemove.IsEnabled)return;var last=removed[^1];var next=SnapshotWordplayWorkshop();next.Favourites.Insert(Math.Min(last.Index,next.Favourites.Count),last.Favourite.DeepClone());
            if(Commit(next,"Removed favourite restored.",true,false)){removed.RemoveAt(removed.Count-1);UpdateButtons();}
        };
        apply.Click+=(_,__)=>
        {
            if(!apply.IsEnabled || destination==null)return;
            try
            {
                WordplayWorkshopState.ValidateText(edit.Text,WordplayWorkshopState.MaximumPhraseLength,"edited idea",false);
                applying=true;ApplyCharacterVoiceRewrite(destination,edit.Text);destination=null;status.Text="Idea applied. Ctrl+Z restores the original manuscript. The workshop and favourites are retained.";
            }
            catch(Exception error) when(error is InvalidDataException or InvalidOperationException or ArgumentException){status.Text=error.Message;}
            finally{applying=false;UpdateButtons();}
        };
        languageSetup.Click+=(_,__)=>{CancelRequest("Language settings opened. Existing ideas remain available.");LanguageSettings_Click(languageSetup,new RoutedEventArgs());RefreshAvailability();};
        aiSetup.Click+=(_,__)=>{CancelRequest("AI settings opened. Existing ideas remain available.");AiManager_Click(aiSetup,new RoutedEventArgs());RefreshAvailability();};close.Click+=(_,__)=>w.Close();
        void MetadataChanged()
        {if(closed || committing)return;CancelRequest("The wordplay record changed. Any pending exploration was cancelled.");removed.Clear();RefreshViews();}
        void DocumentChanged(){if(!closed)w.Close();}
        WordplayWorkshopChanged+=MetadataChanged;WordplayWorkshopDocumentChanged+=DocumentChanged;
        TextChangedEventHandler manuscriptChanged=(_,__)=>
        {if(closed || applying)return;destination=null;status.Text="The manuscript changed. Wordplay ideas remain available; capture a new destination before applying one.";UpdateButtons();};
        Editor.TextChanged+=manuscriptChanged;
        var readOnlyDescriptor=DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty,typeof(RichTextBox));
        EventHandler readOnlyChanged=(_,__)=>{if(!Editable())CancelRequest("Read-only document: saved ideas remain available to view and copy.");RefreshAvailability();};readOnlyDescriptor?.AddValueChanged(Editor,readOnlyChanged);
        DependencyPropertyChangedEventHandler enabledChanged=(_,__)=>{if(!Editable())CancelRequest("Editing is disabled; pending exploration was cancelled.");UpdateButtons();};Editor.IsEnabledChanged+=enabledChanged;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,__)=>RefreshAvailability();timer.Start();w.Activated+=(_,__)=>RefreshAvailability();
        w.Closed+=(_,__)=>
        {
            closed=true;CancelRequest();timer.Stop();WordplayWorkshopChanged-=MetadataChanged;WordplayWorkshopDocumentChanged-=DocumentChanged;Editor.TextChanged-=manuscriptChanged;
            readOnlyDescriptor?.RemoveValueChanged(Editor,readOnlyChanged);Editor.IsEnabledChanged-=enabledChanged;
            if(ReferenceEquals(wordplayWorkshopWindow,w))wordplayWorkshopWindow=null;
        };
        RefreshViews(false);assigning=false;ShowIdea();RefreshAvailability();
        status.Text=Editor.IsReadOnly?"Read-only document: view and copy saved ideas.":captureError??unavailable??"Add words and fragments, combine the choices, then Explore. You decide which connections are worth keeping.";
        UpdateButtons();w.Show();
    }
}
