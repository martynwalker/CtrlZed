using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? fontFinderWindow;
    bool fontFinderInstalled;

    internal void InstallFontFinder()
    {
        if(fontFinderInstalled)return;
        fontFinderInstalled=true;
        InstallFontFinderTargetTracking();
        Loaded+=(_,__)=>Dispatcher.BeginInvoke(new Action(InstallFontFinderMenu),DispatcherPriority.ApplicationIdle);
        ContentRendered+=(_,__)=>Dispatcher.BeginInvoke(new Action(InstallFontFinderMenu),DispatcherPriority.ApplicationIdle);
        Closed+=(_,__)=>fontFinderWindow?.Close();
        if(IsLoaded)Dispatcher.BeginInvoke(new Action(InstallFontFinderMenu),DispatcherPriority.ApplicationIdle);
    }

    void InstallFontFinderMenu()
    {
        var format=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item=>
            string.Equals(NormalisedMenuHeader(item.Header),"Format",StringComparison.OrdinalIgnoreCase));
        if(format==null || format.Items.OfType<MenuItem>().Any(item=>(item.Tag as string)=="CtrlZedFindFont"))return;
        var find=new MenuItem{Header="Find a _Font…",Tag="CtrlZedFindFont"};
        find.Click+=(_,__)=>ShowFontFinder();
        format.Items.Add(new Separator());format.Items.Add(find);
    }

    /// <summary>Finds installed families and previews them without changing the document.</summary>
    public void ShowFontFinder()
    {
        if(fontFinderWindow!=null)
        {
            if(fontFinderWindow.WindowState==WindowState.Minimized)fontFinderWindow.WindowState=WindowState.Normal;
            fontFinderWindow.Activate();return;
        }
        // Also supports callers that open the window before the main window is loaded.
        InstallFontFinder();
        FontFinderTarget? target=null;
        string? targetError=null;
        try{target=CaptureFontFinderTarget();}
        catch(InvalidOperationException ex){targetError=ex.Message;}

        var availableWidth=Math.Max(320,SystemParameters.WorkArea.Width-32);
        var availableHeight=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window
        {
            Title="Find a Font",Owner=this,Width=Math.Min(1000,availableWidth),Height=Math.Min(720,availableHeight),
            MinWidth=Math.Min(740,availableWidth),MinHeight=Math.Min(600,availableHeight),
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        fontFinderWindow=w;
        NameScope.SetNameScope(w,new NameScope());
        T Named<T>(string name,T control) where T:FrameworkElement
        {control.Name=name;w.RegisterName(name,control);return control;}
        TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,10,0,4)};
        Button ActionButton(string text)=>new(){Content=text,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,7,0),MinHeight=30};
        string LocalSample()=>string.IsNullOrWhiteSpace(target?.SampleText)?"The quick brown fox jumps over the lazy dog. 0123456789":target.SampleText;

        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var finderHeading=new Grid();finderHeading.ColumnDefinitions.Add(new ColumnDefinition());finderHeading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        finderHeading.Children.Add(new TextBlock{Text="Find a Font",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        var googleFonts=Named("FontFinderGoogleFonts",ActionButton("Google Fonts…"));googleFonts.Margin=new Thickness(8,0,0,6);
        googleFonts.Click+=(_,__)=>ShowOnlineFontFinder();Grid.SetColumn(googleFonts,1);finderHeading.Children.Add(googleFonts);root.Children.Add(finderHeading);
        var information=Named("FontFinderInformation",new TextBlock
        {
            Text="Search installed fonts by name or describe the look you need. Previewing never changes the document.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,10)
        });
        Grid.SetRow(information,1);root.Children.Add(information);
        var body=new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(295)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(15)});
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body,2);root.Children.Add(body);
        var options=new StackPanel{Margin=new Thickness(0,0,5,0)};
        body.Children.Add(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});

        var requestLabel=Label("Name or description");requestLabel.Margin=new Thickness(0,0,0,4);options.Children.Add(requestLabel);
        var query=Named("FontFinderQuery",new TextBox
        {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=66,MaxHeight=90,MaxLength=1000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        query.ToolTip="For example: Aptos; a traditional serif for a novel; a clean sans serif for a technical report.";
        options.Children.Add(query);
        var searchActions=new WrapPanel{Margin=new Thickness(0,7,0,0)};
        var search=Named("FontFinderSearch",ActionButton("Search installed"));
        var askAi=Named("FontFinderAskAi",ActionButton("Ask AI"));
        searchActions.Children.Add(search);searchActions.Children.Add(askAi);options.Children.Add(searchActions);
        var catalogueInformation=Named("FontFinderCatalogue",new TextBlock{Text="Reading installed font families…",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,7,0,0)});
        options.Children.Add(catalogueInformation);
        var aiInformation=Named("FontFinderAiInformation",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,0)});
        options.Children.Add(aiInformation);

        options.Children.Add(Label("Preview text — stays on this computer"));
        var sample=Named("FontFinderSample",new TextBox
        {Text=LocalSample(),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=94,MaxLength=4000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=32});
        options.Children.Add(sample);
        var sizeLabel=Named("FontFinderPreviewSizeLabel",Label("Preview size: 24 pt — preview only"));
        options.Children.Add(sizeLabel);
        var previewSize=Named("FontFinderPreviewSize",new Slider{Minimum=18,Maximum=48,Value=24,TickFrequency=2,IsSnapToTickEnabled=true,AutoToolTipPlacement=AutoToolTipPlacement.TopLeft});
        previewSize.ToolTip="Changes only these samples. Applying a font preserves the document's point size.";options.Children.Add(previewSize);
        var emphasis=new Grid{Margin=new Thickness(0,4,0,0)};
        emphasis.ColumnDefinitions.Add(new ColumnDefinition());emphasis.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(10)});emphasis.ColumnDefinitions.Add(new ColumnDefinition());
        var boldPanel=new StackPanel();boldPanel.Children.Add(Label("Weight"));
        var bold=Named("FontFinderBold",new ComboBox{ItemsSource=new[]{"As requested","Keep existing","Regular","Bold"},SelectedIndex=0});
        boldPanel.Children.Add(bold);emphasis.Children.Add(boldPanel);
        var italicPanel=new StackPanel();italicPanel.Children.Add(Label("Slant"));
        var italic=Named("FontFinderItalic",new ComboBox{ItemsSource=new[]{"As requested","Keep existing","Normal","Italic"},SelectedIndex=0});
        italicPanel.Children.Add(italic);Grid.SetColumn(italicPanel,2);emphasis.Children.Add(italicPanel);options.Children.Add(emphasis);
        options.Children.Add(new TextBlock{Text="As requested preserves existing emphasis unless your request explicitly asks to change it.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,0)});

        options.Children.Add(Label("Apply to"));
        var destination=Named("FontFinderDestination",new ComboBox{ItemsSource=new[]{"Selection / typing caret","Named style"},SelectedIndex=0});
        options.Children.Add(destination);
        var styleLabel=Named("FontFinderStyleLabel",Label("Style set: "+(target?.SetName??activeStyleSet)));
        options.Children.Add(styleLabel);
        var style=Named("FontFinderStyle",new ComboBox{IsEnabled=false});options.Children.Add(style);
        var destinationInformation=Named("FontFinderDestinationInformation",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,0)});
        options.Children.Add(destinationInformation);
        var targetInformation=Named("FontFinderTarget",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,6)});
        options.Children.Add(targetInformation);
        var refreshTarget=Named("FontFinderRefreshTarget",ActionButton("Use current selection / caret"));
        refreshTarget.HorizontalAlignment=HorizontalAlignment.Left;options.Children.Add(refreshTarget);

        var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.RowDefinitions.Add(new RowDefinition());
        Grid.SetColumn(right,2);body.Children.Add(right);
        var matchesLabel=Named("FontFinderMatchesLabel",new TextBlock{Text="Installed font samples",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
        right.Children.Add(matchesLabel);
        var results=Named("FontFinderResults",new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch});
        ScrollViewer.SetHorizontalScrollBarVisibility(results,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(results,ScrollBarVisibility.Auto);
        Grid.SetRow(results,1);right.Children.Add(results);

        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};
        var status=Named("FontFinderStatus",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,MinHeight=34,MaxHeight=55,Margin=new Thickness(0,0,0,6)});
        footer.Children.Add(status);
        var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var settings=new WrapPanel();
        var languageSetup=ActionButton("Language settings…");var aiSetup=ActionButton("AI settings…");
        settings.Children.Add(languageSetup);settings.Children.Add(aiSetup);actions.Children.Add(settings);
        var mainActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var apply=Named("FontFinderApply",ActionButton("Apply to selection"));apply.MinWidth=125;apply.IsEnabled=false;
        var close=ActionButton("Close");close.MinWidth=70;close.Margin=new Thickness(0);close.IsCancel=true;
        mainActions.Children.Add(apply);mainActions.Children.Add(close);Grid.SetColumn(mainActions,1);actions.Children.Add(mainActions);
        footer.Children.Add(actions);Grid.SetRow(footer,3);root.Children.Add(footer);w.Content=root;

        IReadOnlyList<FontFinderFamily> catalogue=Array.Empty<FontFinderFamily>();
        var catalogueLoaded=false;var closed=false;var busy=false;var validMatches=false;var matchesUseAi=false;var configuring=false;
        var provider=AiManager.GetDefaultProvider();
        var capturedSetName=target?.SetName??activeStyleSet;
        var observedMode=languageSettings.WritingMode;
        long requestId=0;
        CancellationTokenSource? requestCancellation=null;
        var lifetimeCancellation=new CancellationTokenSource();
        var lifetimeToken=lifetimeCancellation.Token;
        var previewBlocks=new List<(TextBlock Block,FontFinderMatch Match)>();

        string ConfigurationSignature()
        {
            var language=$"{languageSettings.WritingMode}|{languageSettings.CustomProfileName}|{languageSettings.AllowAiLanguageServices}";
            if(FontFinderService.GeneratorOverride!=null)return "Test provider|"+language;
            try
            {
                return AiDirectService.ConfigurationFingerprint(provider)+"|"+language;
            }
            catch{return provider+"|Unavailable|"+language;}
        }
        var observedConfiguration=ConfigurationSignature();
        string? AiUnavailableReason()
        {
            if(!languageSettings.AllowAiLanguageServices)return "AI is off. Enable AI language services in Language settings to use Ask AI.";
            if(FontFinderService.GeneratorOverride!=null)return null;
            if(string.Equals(provider,"Copilot",StringComparison.OrdinalIgnoreCase))return "Copilot has no direct font search API. Choose ChatGPT, Claude or Gemini in AI settings.";
            try{if(!AiDirectService.GetProfile(provider).HasKey)return $"Ask AI needs a configured {provider} API key in AI settings.";}
            catch{return "AI settings could not be read. Check AI settings to use Ask AI.";}
            return null;
        }
        FontFinderMatch? SelectedMatch()=>(results.SelectedItem as ListBoxItem)?.Tag as FontFinderMatch;
        FontFinderMatch WithOverrides(FontFinderMatch match)=>match with
        {
            Bold=bold.SelectedIndex switch{1=>null,2=>false,3=>true,_=>match.Bold},
            Italic=italic.SelectedIndex switch{1=>null,2=>false,3=>true,_=>match.Italic}
        };
        bool TargetCurrent()=>target!=null && IsFontFinderTargetCurrent(target);
        bool CanonicalMatch(FontFinderMatch? match)=>match!=null && catalogue.Any(family=>string.Equals(family.Source,match.Source,StringComparison.Ordinal));

        void UpdateTargetInformation()
        {
            targetInformation.Text=target==null?(targetError??"Choose a selection or typing caret in the document."):
                !TargetCurrent()?"The captured selection or document changed. Use current selection / caret before applying.":
                target.IsCaret?"Captured destination: typing caret. Its paragraph supplies the preview text.":"Captured destination: selected text. Preview text can be edited independently.";
            styleLabel.Text="Style set: "+capturedSetName;
        }
        void UpdateButtons()
        {
            if(closed)return;
            var isStyle=destination.SelectedIndex==1;
            style.IsEnabled=isStyle;
            apply.Content=isStyle?"Update named style":target?.IsCaret==true?"Apply at typing caret":"Apply to selection";
            destinationInformation.Text=isStyle?"Updates and saves this style in the shown set, including paragraphs using it in the current document. Saved style definitions are outside Ctrl+Z; restore them through Styles.":
                "Changes the font at the captured selection or typing caret. Existing point size is preserved; Ctrl+Z can undo this change.";
            search.IsEnabled=catalogueLoaded && catalogue.Count>0;
            askAi.IsEnabled=catalogueLoaded && catalogue.Count>0 && !busy && AiUnavailableReason()==null && !string.IsNullOrWhiteSpace(query.Text);
            apply.IsEnabled=!busy && validMatches && CanonicalMatch(SelectedMatch()) && TargetCurrent() && !Editor.IsReadOnly && Editor.IsEnabled &&
                (!isStyle || style.SelectedItem is string);
            refreshTarget.IsEnabled=!busy;
        }
        void UpdateInformation()
        {
            var mode=WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName;
            information.Text=$"Writing mode: {mode}. Search installed works locally. Nothing changes until you choose Apply or Update named style.";
            var unavailable=AiUnavailableReason();
            aiInformation.Text=(unavailable??$"Ask AI uses {provider} only when requested.")+" Only your description, writing mode and installed font catalogue are sent; preview text stays local.";
            askAi.ToolTip=unavailable??"Ask the configured AI provider to rank real installed font families.";
        }
        void CancelRequest()
        {
            requestId++;
            var previous=requestCancellation;requestCancellation=null;
            previous?.Cancel();previous?.Dispose();busy=false;
        }
        void InvalidateMatches(string message)
        {
            if(closed)return;
            CancelRequest();validMatches=false;matchesUseAi=false;results.Items.Clear();previewBlocks.Clear();
            matchesLabel.Text="Installed font samples";status.Text=message;UpdateButtons();
        }
        void RenderPreview(TextBlock block,FontFinderMatch match)
        {
            var effective=WithOverrides(match);
            block.Text=sample.Text;
            block.FontFamily=new FontFamily(match.Source);
            block.FontSize=previewSize.Value*96d/72d;
            block.FontWeight=(effective.Bold??target?.Bold??false)?FontWeights.Bold:FontWeights.Normal;
            block.FontStyle=(effective.Italic??target?.Italic??false)?FontStyles.Italic:FontStyles.Normal;
        }
        void UpdatePreviews()
        {
            foreach(var preview in previewBlocks)RenderPreview(preview.Block,preview.Match);
            var selected=SelectedMatch();
            if(selected!=null)
            {
                var effective=WithOverrides(selected);
                sample.FontFamily=new FontFamily(selected.Source);
                sample.FontWeight=(effective.Bold??target?.Bold??false)?FontWeights.Bold:FontWeights.Normal;
                sample.FontStyle=(effective.Italic??target?.Italic??false)?FontStyles.Italic:FontStyles.Normal;
            }
            sample.FontSize=previewSize.Value*96d/72d;
            sizeLabel.Text=$"Preview size: {previewSize.Value:0} pt — preview only";
            UpdateButtons();
        }
        void PopulateMatches(IReadOnlyList<FontFinderMatch> matches,bool useAi)
        {
            results.Items.Clear();previewBlocks.Clear();
            foreach(var match in matches.Where(CanonicalMatch).DistinctBy(item=>item.Source,StringComparer.OrdinalIgnoreCase).Take(8))
            {
                var family=catalogue.First(item=>string.Equals(item.Source,match.Source,StringComparison.Ordinal));
                var panel=new StackPanel{Margin=new Thickness(7)};
                panel.Children.Add(new TextBlock{Text=family.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                var preview=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxHeight=100,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,5,0,5)};
                RenderPreview(preview,match);previewBlocks.Add((preview,match));panel.Children.Add(preview);
                panel.Children.Add(new TextBlock{Text=match.Reason,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray});
                results.Items.Add(new ListBoxItem{Content=panel,Tag=match,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(0),Margin=new Thickness(0,0,0,6)});
            }
            validMatches=results.Items.Count>0;matchesUseAi=useAi;
            if(validMatches)results.SelectedIndex=0;
            matchesLabel.Text=$"{(useAi?"AI-ranked":"Local")} installed matches ({results.Items.Count})";
            status.Text=validMatches?$"{(useAi?"AI-ranked":"Local")} results: {results.Items.Count} installed {(results.Items.Count==1?"family":"families")}. Select a sample, then apply explicitly. Preview size is never applied.":
                "No matching installed families. Try a shorter name or describe serif, sans serif, handwriting or monospaced text.";
            UpdatePreviews();
        }
        void RefreshStyles()
        {
            var previous=style.SelectedItem as string;
            var names=FontFinderStyleNames();style.ItemsSource=names;
            style.SelectedItem=names.FirstOrDefault(name=>string.Equals(name,previous,StringComparison.OrdinalIgnoreCase))??
                names.FirstOrDefault(name=>string.Equals(name,"Normal",StringComparison.OrdinalIgnoreCase))??names.FirstOrDefault();
        }
        void RefreshAvailability()
        {
            if(closed || configuring)return;
            provider=AiManager.GetDefaultProvider();
            var configuration=ConfigurationSignature();
            if(languageSettings.WritingMode!=observedMode)
            {
                observedMode=languageSettings.WritingMode;
                InvalidateMatches("Writing mode changed. Search installed or Ask AI again for fresh matches.");
            }
            else if(configuration!=observedConfiguration && (busy || matchesUseAi))
                InvalidateMatches("AI settings changed. Search installed or Ask AI again for fresh matches.");
            observedConfiguration=configuration;UpdateInformation();UpdateTargetInformation();UpdateButtons();
        }
        async Task SearchAsync(bool useAi)
        {
            if(closed)return;
            RefreshAvailability();
            if(!catalogueLoaded || catalogue.Count==0){status.Text="Wait for the installed font catalogue to load.";return;}
            if(useAi)
            {
                var unavailable=AiUnavailableReason();
                if(unavailable!=null){status.Text=unavailable+" Search installed remains available.";UpdateButtons();return;}
                if(string.IsNullOrWhiteSpace(query.Text)){status.Text="Enter a name or description before asking AI.";return;}
            }
            InvalidateMatches(useAi?$"Asking {provider} to rank installed font families…":"Searching installed font families locally…");
            var requestedQuery=query.Text.Trim();var requestedMode=languageSettings.WritingMode;
            var mode=WritingModeProfiles.Get(requestedMode);
            if(useAi)status.Text=$"Asking {provider} to rank {FontFinderService.DescribeAiCatalogueScope(catalogue,requestedQuery,mode)}… Preview text stays local.";
            var configuration=ConfigurationSignature();
            var requestedTarget=target;
            var thisRequest=++requestId;
            requestCancellation=new CancellationTokenSource();var token=requestCancellation.Token;
            busy=true;UpdateButtons();
            try
            {
                IReadOnlyList<FontFinderMatch> matches;
                if(useAi)matches=await FontFinderService.SuggestAsync(catalogue,requestedQuery,mode,token);
                else matches=await Task.Run(()=>FontFinderService.SearchLocal(catalogue,requestedQuery,mode,8),token);
                if(closed || token.IsCancellationRequested || thisRequest!=requestId)return;
                provider=AiManager.GetDefaultProvider();
                if(query.Text.Trim()!=requestedQuery || languageSettings.WritingMode!=requestedMode ||
                    (useAi && (configuration!=ConfigurationSignature() || AiUnavailableReason()!=null || !ReferenceEquals(target,requestedTarget))))
                {InvalidateMatches("The request or settings changed. Search again for fresh installed matches.");return;}
                PopulateMatches(matches,useAi);
            }
            catch(OperationCanceledException)
            {if(!closed && thisRequest==requestId)status.Text="Search cancelled. Search installed or Ask AI when ready.";}
            catch(Exception ex)
            {if(!closed && thisRequest==requestId)status.Text=(useAi?"AI font search failed: ":"Installed font search failed: ")+ex.Message+(useAi?" Search installed remains available.":"");}
            finally
            {
                if(!closed && thisRequest==requestId)
                {busy=false;requestCancellation?.Dispose();requestCancellation=null;UpdateButtons();}
            }
        }

        query.TextChanged+=(_,__)=>InvalidateMatches(catalogueLoaded?"Request changed. Search installed or Ask AI for fresh matches.":"Reading installed font families…");
        search.Click+=async (_,__)=>await SearchAsync(false);
        askAi.Click+=async (_,__)=>await SearchAsync(true);
        results.SelectionChanged+=(_,__)=>UpdatePreviews();
        sample.TextChanged+=(_,__)=>UpdatePreviews();
        previewSize.ValueChanged+=(_,__)=>UpdatePreviews();
        bold.SelectionChanged+=(_,__)=>UpdatePreviews();italic.SelectionChanged+=(_,__)=>UpdatePreviews();
        destination.SelectionChanged+=(_,__)=>UpdateButtons();style.SelectionChanged+=(_,__)=>UpdateButtons();
        refreshTarget.Click+=(_,__)=>
        {
            CancelRequest();
            try
            {
                target=CaptureFontFinderTarget();targetError=null;capturedSetName=target.SetName;
                sample.Text=LocalSample();RefreshStyles();
                status.Text="Captured the current selection or typing caret. The preview remains local; choose Apply when ready.";
            }
            catch(InvalidOperationException ex){target=null;targetError=ex.Message;status.Text=ex.Message;}
            UpdateTargetInformation();UpdatePreviews();
        };
        apply.Click+=(_,__)=>
        {
            RefreshAvailability();
            var match=SelectedMatch();
            if(closed || busy || !validMatches || !CanonicalMatch(match))return;
            if(target==null || !TargetCurrent())
            {status.Text="The captured destination changed. Use current selection / caret before applying.";UpdateButtons();return;}
            if(Editor.IsReadOnly || !Editor.IsEnabled)
            {status.Text="The document is read-only. Make it editable before applying a font.";UpdateButtons();return;}
            try
            {
                var effective=WithOverrides(match!);
                if(destination.SelectedIndex==1)
                {
                    if(style.SelectedItem is not string styleName)return;
                    ApplyFontFinderStyle(target,capturedSetName,styleName,effective,catalogue);
                    status.Text=$"Updated and saved style ‘{styleName}’ in ‘{capturedSetName}’, including paragraphs using it in this document.";
                }
                else
                {
                    ApplyFontFinderSelection(target,effective,catalogue);
                    status.Text="Applied the font to the captured selection or typing caret. Point size was preserved. Ctrl+Z can undo this change.";
                }
                UpdateTargetInformation();UpdateButtons();
            }
            catch(InvalidOperationException ex){status.Text=ex.Message;UpdateTargetInformation();UpdateButtons();}
            catch(Exception ex)
            {
                status.Text="Could not complete the font change: "+ex.Message;
                UpdateTargetInformation();UpdateButtons();
            }
        };
        languageSetup.Click+=(_,__)=>
        {
            configuring=true;
            try{LanguageSettings_Click(languageSetup,new RoutedEventArgs());}
            finally{configuring=false;RefreshAvailability();}
        };
        aiSetup.Click+=(_,__)=>
        {
            CancelRequest();
            if(matchesUseAi)InvalidateMatches("AI settings opened. Ask AI again after changing the provider.");
            AiSettings_Click(aiSetup,new RoutedEventArgs());RefreshAvailability();
        };
        close.Click+=(_,__)=>w.Close();
        w.Activated+=(_,__)=>RefreshAvailability();
        TextChangedEventHandler documentChanged=(_,__)=>
        {
            if(closed)return;
            CancelRequest();status.Text="The document changed. Use current selection / caret before applying a font.";
            UpdateTargetInformation();UpdateButtons();
        };
        RoutedEventHandler selectionChanged=(_,__)=>
        {
            if(closed)return;
            if(!TargetCurrent() && busy)CancelRequest();
            UpdateTargetInformation();UpdateButtons();
        };
        EventHandler readOnlyChanged=(_,__)=>{UpdateTargetInformation();UpdateButtons();};
        DependencyPropertyChangedEventHandler enabledChanged=(_,__)=>{UpdateTargetInformation();UpdateButtons();};
        var readOnlyDescriptor=DependencyPropertyDescriptor.FromProperty(TextBoxBase.IsReadOnlyProperty,typeof(RichTextBox));
        Editor.TextChanged+=documentChanged;Editor.SelectionChanged+=selectionChanged;Editor.IsEnabledChanged+=enabledChanged;
        readOnlyDescriptor?.AddValueChanged(Editor,readOnlyChanged);
        w.Closed+=(_,__)=>
        {
            closed=true;CancelRequest();lifetimeCancellation.Cancel();lifetimeCancellation.Dispose();
            Editor.TextChanged-=documentChanged;Editor.SelectionChanged-=selectionChanged;Editor.IsEnabledChanged-=enabledChanged;
            readOnlyDescriptor?.RemoveValueChanged(Editor,readOnlyChanged);
            if(ReferenceEquals(fontFinderWindow,w))fontFinderWindow=null;
        };
        RefreshStyles();UpdateInformation();UpdateTargetInformation();UpdateButtons();
        status.Text=targetError??"Reading installed font families. Search installed is local and needs no AI account.";
        w.Show();
        _=LoadCatalogueAsync();

        async Task LoadCatalogueAsync()
        {
            var token=lifetimeToken;
            try
            {
                var installed=await Task.Run(FontFinderService.CaptureInstalledFonts,token);
                if(closed || token.IsCancellationRequested)return;
                catalogue=installed;catalogueLoaded=true;
                catalogueInformation.Text=$"{catalogue.Count:N0} installed font {(catalogue.Count==1?"family":"families")}. No fonts are downloaded or installed.";
                UpdateInformation();UpdateButtons();
                if(catalogue.Count==0){status.Text="No installed font families could be read on this computer.";return;}
                await SearchAsync(false);
            }
            catch(OperationCanceledException){ }
            catch(Exception ex)
            {
                if(closed)return;
                catalogueInformation.Text="The installed font catalogue could not be read.";
                status.Text="Could not read installed fonts: "+ex.Message;UpdateButtons();
            }
        }
    }
}
