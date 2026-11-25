using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    internal Window? onlineFontWindow;
    internal OnlineFontInstallResult? onlineFontLastInstallResult;

    /// <summary>Browses licensed catalogue fonts without changing the document.</summary>
    public void ShowOnlineFontFinder()
    {
        if(onlineFontWindow!=null)
        {
            if(onlineFontWindow.WindowState==WindowState.Minimized)onlineFontWindow.WindowState=WindowState.Normal;
            onlineFontWindow.Activate();return;
        }
        // This is a read-only sample, not a retained editing target. Browsing
        // is also useful for a read-only document and must not discard a
        // pending font choice from the installed-font finder.
        var localSample=CaptureOnlineFontSample();
        var availableWidth=Math.Max(320,SystemParameters.WorkArea.Width-32);
        var availableHeight=Math.Max(320,SystemParameters.WorkArea.Height-32);
        var w=new Window
        {
            Title="Google Fonts",Owner=this,Width=Math.Min(1000,availableWidth),Height=Math.Min(720,availableHeight),
            MinWidth=Math.Min(720,availableWidth),MinHeight=Math.Min(560,availableHeight),
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Icon=Branding.CreateIconImageSource()
        };
        onlineFontWindow=w;onlineFontLastInstallResult=null;
        NameScope.SetNameScope(w,new NameScope());
        T Named<T>(string name,T control) where T:FrameworkElement
        {control.Name=name;w.RegisterName(name,control);return control;}
        Button ActionButton(string text)=>new(){Content=text,Padding=new Thickness(9,5,9,5),MinHeight=30,Margin=new Thickness(0,0,7,0)};

        var root=new Grid{Margin=new Thickness(16)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.Children.Add(new TextBlock{Text="Google Fonts",FontSize=23,FontWeight=FontWeights.SemiBold});
        var information=Named("OnlineFontInformation",new TextBlock
        {
            Text="Find a font, review its licence and download a local preview. Installation is a separate choice.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,10)
        });
        Grid.SetRow(information,1);root.Children.Add(information);

        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(15)});body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(body,2);root.Children.Add(body);
        var left=new Grid();left.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});left.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(left);
        var searchPanel=new StackPanel{Margin=new Thickness(0,0,0,7)};left.Children.Add(searchPanel);
        searchPanel.Children.Add(new TextBlock{Text="Name or description",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
        var query=Named("OnlineFontQuery",new TextBox
        {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=64,MaxLength=1000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        query.ToolTip="For example: a traditional serif for a novel; a bold display font; a technical monospaced face.";
        searchPanel.Children.Add(query);
        var searchActions=new WrapPanel{Margin=new Thickness(0,7,0,0)};
        var search=Named("OnlineFontSearch",ActionButton("Search catalogue"));
        var askAi=Named("OnlineFontAskAi",ActionButton("Ask AI"));
        searchActions.Children.Add(search);searchActions.Children.Add(askAi);searchPanel.Children.Add(searchActions);
        var catalogueInfo=Named("OnlineFontCatalogue",new TextBlock
        {Text="Reading the supplied Google Fonts catalogue…",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,7,0,0)});
        searchPanel.Children.Add(catalogueInfo);
        var aiInfo=Named("OnlineFontAiInformation",new TextBlock
        {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,5,0,0)});searchPanel.Children.Add(aiInfo);
        var results=Named("OnlineFontResults",new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch});
        ScrollViewer.SetHorizontalScrollBarVisibility(results,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(results,ScrollBarVisibility.Auto);Grid.SetRow(results,1);left.Children.Add(results);

        var detail=new Grid();Grid.SetColumn(detail,2);body.Children.Add(detail);
        detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        detail.RowDefinitions.Add(new RowDefinition());
        detail.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var familyTitle=Named("OnlineFontFamilyTitle",new TextBlock
        {Text="Choose a catalogue family",FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});detail.Children.Add(familyTitle);
        var familyInfo=Named("OnlineFontFamilyInformation",new TextBlock
        {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,4,0,6)});Grid.SetRow(familyInfo,1);detail.Children.Add(familyInfo);
        var sourcePanel=new Grid{Margin=new Thickness(0,0,0,7)};
        sourcePanel.ColumnDefinitions.Add(new ColumnDefinition());sourcePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var source=Named("OnlineFontSource",new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,MaxHeight=46,FontSize=11});
        sourcePanel.Children.Add(source);
        var openSource=Named("OnlineFontOpenSource",ActionButton("Open source…"));openSource.Margin=new Thickness(8,0,0,0);Grid.SetColumn(openSource,1);sourcePanel.Children.Add(openSource);
        Grid.SetRow(sourcePanel,2);detail.Children.Add(sourcePanel);
        var facePanel=new Grid{Margin=new Thickness(0,0,0,7)};
        facePanel.ColumnDefinitions.Add(new ColumnDefinition());facePanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var faces=Named("OnlineFontFaces",new ComboBox{DisplayMemberPath=nameof(OnlineFontFace.DisplayName),MinHeight=29,VerticalContentAlignment=VerticalAlignment.Center});
        facePanel.Children.Add(faces);
        var previewButton=Named("OnlineFontPreview",ActionButton("Download preview"));previewButton.Margin=new Thickness(8,0,0,0);Grid.SetColumn(previewButton,1);facePanel.Children.Add(previewButton);
        Grid.SetRow(facePanel,3);detail.Children.Add(facePanel);

        var tabs=Named("OnlineFontTabs",new TabControl());
        var previewTab=new TabItem{Header="Preview"};
        var previewPanel=new Grid{Margin=new Thickness(9)};previewPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        previewPanel.RowDefinitions.Add(new RowDefinition());previewPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var previewInfo=Named("OnlineFontPreviewInformation",new TextBlock
        {Text="Download a preview to see the selected face. Your sample text stays on this computer.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,7)});previewPanel.Children.Add(previewInfo);
        var sample=Named("OnlineFontSample",new TextBox
        {Text=localSample,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=4000,FontSize=32,Visibility=Visibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Grid.SetRow(sample,1);previewPanel.Children.Add(sample);
        var sizePanel=new StackPanel{Margin=new Thickness(0,7,0,0)};
        var sizeLabel=Named("OnlineFontPreviewSizeLabel",new TextBlock{Text="Preview size: 24 pt — does not change the document"});
        sizePanel.Children.Add(sizeLabel);
        var size=Named("OnlineFontPreviewSize",new Slider{Minimum=12,Maximum=48,Value=24,TickFrequency=2,IsSnapToTickEnabled=true});sizePanel.Children.Add(size);
        Grid.SetRow(sizePanel,2);previewPanel.Children.Add(sizePanel);previewTab.Content=previewPanel;tabs.Items.Add(previewTab);
        var licenceTab=new TabItem{Header="Licence and notices"};
        var licence=Named("OnlineFontLicence",new TextBox
        {IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,FontFamily=new FontFamily("Consolas"),FontSize=12,Margin=new Thickness(7)});
        licenceTab.Content=licence;tabs.Items.Add(licenceTab);tabs.SelectedItem=licenceTab;
        Grid.SetRow(tabs,4);detail.Children.Add(tabs);
        var accept=Named("OnlineFontAcceptLicence",new CheckBox
        {Content="I have read the licence terms for this family",Margin=new Thickness(0,8,0,0)});
        Grid.SetRow(accept,5);detail.Children.Add(accept);

        var status=Named("OnlineFontStatus",new TextBlock
        {TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,MinHeight=34,MaxHeight=63,Margin=new Thickness(0,9,0,7)});
        Grid.SetRow(status,3);root.Children.Add(status);
        var footer=new Grid();footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var settings=new WrapPanel();var languageSetup=ActionButton("Language settings…");var aiSetup=ActionButton("AI settings…");
        settings.Children.Add(languageSetup);settings.Children.Add(aiSetup);footer.Children.Add(settings);
        var mainActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var install=Named("OnlineFontInstall",ActionButton("Install family for this Windows user"));
        var close=Named("OnlineFontClose",ActionButton("Close"));close.MinWidth=70;close.Margin=new Thickness(0);close.IsCancel=true;
        mainActions.Children.Add(install);mainActions.Children.Add(close);Grid.SetColumn(mainActions,1);footer.Children.Add(mainActions);
        Grid.SetRow(footer,4);root.Children.Add(footer);w.Content=root;

        OnlineFontCatalogue? catalogue=null;
        OnlineFontFamily? selectedFamily=null;
        OnlineFontPreview? downloadedPreview=null;
        var loaded=false;var closed=false;var busy=false;var installing=false;var populating=false;var changingFaces=false;
        var validMatches=false;var matchesUseAi=false;var configuring=false;
        var installedFamilies=new HashSet<string>(StringComparer.Ordinal);
        var provider=AiManager.GetDefaultProvider();
        var observedMode=languageSettings.WritingMode;
        long operationId=0;
        CancellationTokenSource? operationCancellation=null;
        var lifetimeCancellation=new CancellationTokenSource();var lifetimeToken=lifetimeCancellation.Token;
        string ConfigurationSignature()
        {
            var language=$"{languageSettings.WritingMode}|{languageSettings.CustomProfileName}|{languageSettings.AllowAiLanguageServices}";
            if(OnlineFontService.GeneratorOverride!=null)return "Test provider|"+language;
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
            if(OnlineFontService.GeneratorOverride!=null)return null;
            if(string.Equals(provider,"Copilot",StringComparison.OrdinalIgnoreCase))return "Copilot has no direct font search API. Choose ChatGPT, Claude or Gemini in AI settings.";
            try{if(!AiDirectService.GetProfile(provider).HasKey)return $"Ask AI needs a configured {provider} API key in AI settings.";}
            catch{return "AI settings could not be read. Check AI settings to use Ask AI.";}
            return null;
        }
        FontFinderMatch? SelectedMatch()=>(results.SelectedItem as ListBoxItem)?.Tag as FontFinderMatch;
        OnlineFontFace? SelectedFace()=>faces.SelectedItem as OnlineFontFace;
        bool PreviewCurrent()=>downloadedPreview!=null && selectedFamily!=null && SelectedFace() is { } face &&
            downloadedPreview.Family.Id==selectedFamily.Id && downloadedPreview.File.Face.Path==face.Path && downloadedPreview.File.Face.BlobSha==face.BlobSha;
        void UpdateButtons()
        {
            if(closed)return;
            query.IsReadOnly=installing;results.IsEnabled=!installing;faces.IsEnabled=selectedFamily!=null && !installing;
            search.IsEnabled=loaded && catalogue?.Families.Count>0 && !installing;
            askAi.IsEnabled=loaded && catalogue?.Families.Count>0 && !busy && !installing && !string.IsNullOrWhiteSpace(query.Text) && AiUnavailableReason()==null;
            openSource.IsEnabled=selectedFamily!=null && !installing;
            previewButton.IsEnabled=validMatches && selectedFamily!=null && SelectedFace()!=null && !busy && !installing;
            accept.IsEnabled=selectedFamily!=null && !installing;
            install.IsEnabled=validMatches && PreviewCurrent() && accept.IsChecked==true && !busy && !installing && !installedFamilies.Contains(selectedFamily!.Id);
            close.IsEnabled=!installing;languageSetup.IsEnabled=!installing;aiSetup.IsEnabled=!installing;
        }
        void UpdateInformation()
        {
            information.Text=$"Writing mode: {WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName}. Click a catalogue font to download its preview. Installation is a separate choice.";
            var unavailable=AiUnavailableReason();
            aiInfo.Text=(unavailable??$"Ask AI uses {provider} only when requested.")+" Sample text is never sent.";
            askAi.ToolTip=unavailable??"Only your description, Writing Mode and catalogue entries are sent to the configured provider.";
        }
        void CancelOperation()
        {
            operationId++;
            var old=operationCancellation;operationCancellation=null;
            old?.Cancel();old?.Dispose();busy=false;
        }
        void ResetPreview()
        {
            downloadedPreview=null;accept.IsChecked=false;
            sample.Visibility=Visibility.Hidden;sample.FontFamily=SystemFonts.MessageFontFamily;sample.FontWeight=FontWeights.Normal;sample.FontStyle=FontStyles.Normal;
            previewInfo.Text="Download a preview to see the selected face. Your sample text stays on this computer.";
        }
        void SelectFamily(bool cancelOperation)
        {
            if(closed)return;
            if(cancelOperation)CancelOperation();
            var match=SelectedMatch();
            selectedFamily=catalogue?.Families.FirstOrDefault(family=>string.Equals(family.Id,match?.Source,StringComparison.Ordinal));
            changingFaces=true;
            try
            {
                faces.ItemsSource=selectedFamily?.Faces;
                faces.SelectedItem=selectedFamily?.Faces.FirstOrDefault(face=>face.Weight==(match?.Bold==true?700:400) && face.Italic==(match?.Italic==true))??
                    selectedFamily?.Faces.FirstOrDefault(face=>face.Weight==400 && !face.Italic)??selectedFamily?.Faces.FirstOrDefault();
            }
            finally{changingFaces=false;}
            ResetPreview();
            familyTitle.Text=selectedFamily?.Name??"Choose a catalogue family";
            familyInfo.Text=selectedFamily==null?"":$"{selectedFamily.Designer}  •  {selectedFamily.Category.Replace('_',' ')}  •  {selectedFamily.Licence}\nFamily installation includes {selectedFamily.Faces.Count} available face{(selectedFamily.Faces.Count==1?"":"s")}: {string.Join(", ",selectedFamily.Faces.Select(face=>face.DisplayName))}.";
            source.Text=selectedFamily==null || catalogue==null?"":OnlineFontService.SourcePageUrl(catalogue,selectedFamily).AbsoluteUri;
            source.ToolTip=source.Text;
            licence.Text=selectedFamily==null?"Select a family to read its full licence and notices before downloading.":
                string.Join("\r\n\r\n",selectedFamily.LicenceFiles.Select(item=>Path.GetFileName(item.Path)+"\r\n"+new string('—',24)+"\r\n"+item.Text));
            tabs.SelectedItem=licenceTab;
            if(selectedFamily!=null)status.Text=installedFamilies.Contains(selectedFamily.Id)
                ?"This family has been installed. Save your work and restart CtrlZed before choosing it in Find a Font."
                :"Read the licence and choose a face. Download preview fetches that font file; it does not install it.";
            UpdateButtons();
        }
        void InvalidateMatches(string message)
        {
            if(closed || installing)return;
            CancelOperation();validMatches=false;matchesUseAi=false;
            populating=true;try{results.Items.Clear();}finally{populating=false;}
            SelectFamily(false);status.Text=message;UpdateButtons();
        }
        void RefreshAvailability()
        {
            if(closed || configuring || installing)return;
            provider=AiManager.GetDefaultProvider();var configuration=ConfigurationSignature();
            if(languageSettings.WritingMode!=observedMode)
            {
                observedMode=languageSettings.WritingMode;
                InvalidateMatches("Writing Mode changed. Search again for fresh catalogue matches.");
            }
            else if(configuration!=observedConfiguration && (busy || matchesUseAi))
                InvalidateMatches("AI settings changed. Search again for fresh catalogue matches.");
            observedConfiguration=configuration;UpdateInformation();UpdateButtons();
        }
        void PopulateMatches(IReadOnlyList<FontFinderMatch> matches,bool usedAi)
        {
            if(catalogue==null)return;
            populating=true;
            try
            {
                results.Items.Clear();
                foreach(var match in matches.DistinctBy(item=>item.Source,StringComparer.Ordinal).Take(8))
                {
                    var family=catalogue.Families.FirstOrDefault(item=>string.Equals(item.Id,match.Source,StringComparison.Ordinal));
                    if(family==null)continue;
                    var panel=new StackPanel{Margin=new Thickness(6)};
                    panel.Children.Add(new TextBlock{Text=family.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                    panel.Children.Add(new TextBlock{Text=$"{family.Category.Replace('_',' ')} • {family.Licence}",Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)});
                    panel.Children.Add(new TextBlock{Text=match.Reason,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap});
                    results.Items.Add(new ListBoxItem{Content=panel,Tag=match,Padding=new Thickness(0),Margin=new Thickness(0,0,0,5),HorizontalContentAlignment=HorizontalAlignment.Stretch});
                }
                validMatches=results.Items.Count>0;matchesUseAi=usedAi;
                if(validMatches)results.SelectedIndex=0;
            }
            finally{populating=false;}
            SelectFamily(false);
            if(!validMatches)status.Text="No matching supported catalogue families. Try a shorter name or describe serif, sans serif, display or monospaced text.";
            UpdateButtons();
        }
        async Task SearchAsync(bool useAi)
        {
            if(closed || installing)return;
            RefreshAvailability();
            if(!loaded || catalogue==null){status.Text="Wait for the catalogue to finish loading.";return;}
            if(useAi)
            {
                var unavailable=AiUnavailableReason();
                if(unavailable!=null){status.Text=unavailable+" Local search remains available.";return;}
                if(string.IsNullOrWhiteSpace(query.Text)){status.Text="Enter a name or description before asking AI.";return;}
            }
            InvalidateMatches(useAi?$"Asking {provider} for catalogue suggestions…":"Searching the catalogue locally…");
            var thisCatalogue=catalogue;var requestedQuery=query.Text.Trim();var requestedMode=languageSettings.WritingMode;
            var mode=WritingModeProfiles.Get(requestedMode);var configuration=ConfigurationSignature();
            var thisOperation=++operationId;operationCancellation=new CancellationTokenSource();var token=operationCancellation.Token;
            busy=true;UpdateButtons();
            try
            {
                if(useAi)status.Text=$"Asking {provider} to rank {OnlineFontService.DescribeAiCatalogueScope(thisCatalogue,requestedQuery,mode)}. Sample text stays local.";
                var matches=useAi?await OnlineFontService.SuggestAsync(thisCatalogue,requestedQuery,mode,token):
                    await Task.Run(()=>OnlineFontService.Search(thisCatalogue,requestedQuery,mode,8),token);
                if(closed || token.IsCancellationRequested || thisOperation!=operationId)return;
                provider=AiManager.GetDefaultProvider();
                if(!ReferenceEquals(catalogue,thisCatalogue) || requestedQuery!=query.Text.Trim() || requestedMode!=languageSettings.WritingMode ||
                    (useAi && (configuration!=ConfigurationSignature() || AiUnavailableReason()!=null)))
                {InvalidateMatches("The request or settings changed. Search again for fresh catalogue matches.");return;}
                PopulateMatches(matches,useAi);
            }
            catch(OperationCanceledException){if(!closed && thisOperation==operationId)status.Text="Search cancelled.";}
            catch(Exception ex){if(!closed && thisOperation==operationId)status.Text=(useAi?"AI search failed: ":"Catalogue search failed: ")+ex.Message;}
            finally
            {
                if(!closed && thisOperation==operationId)
                {busy=false;operationCancellation?.Dispose();operationCancellation=null;UpdateButtons();}
            }
        }
        async Task PreviewAsync()
        {
            if(closed || installing || catalogue==null || selectedFamily==null || SelectedFace() is not { } face || !validMatches)return;
            CancelOperation();ResetPreview();
            tabs.SelectedItem=previewTab;
            var requestedCatalogue=catalogue;var requestedFamily=selectedFamily;var thisOperation=++operationId;
            operationCancellation=new CancellationTokenSource();var token=operationCancellation.Token;busy=true;
            status.Text=$"Downloading a verified preview of {requestedFamily.Name} — {face.DisplayName}. Nothing is being installed.";UpdateButtons();
            try
            {
                var preview=await OnlineFontService.PreviewAsync(requestedCatalogue,requestedFamily.Id,face,token);
                if(closed || token.IsCancellationRequested || thisOperation!=operationId || !ReferenceEquals(catalogue,requestedCatalogue) ||
                    selectedFamily?.Id!=requestedFamily.Id || SelectedFace()?.Path!=face.Path || SelectedFace()?.BlobSha!=face.BlobSha)return;
                downloadedPreview=preview;
                sample.FontFamily=preview.PreviewFamily;sample.FontWeight=face.Weight==700?FontWeights.Bold:FontWeights.Normal;
                sample.FontStyle=face.Italic?FontStyles.Italic:FontStyles.Normal;
                sample.Visibility=Visibility.Visible;
                previewInfo.Text=$"Preview ready: {preview.ActualFamilyName} — {face.DisplayName}. Sample text stays local; the document is unchanged.";
                status.Text="Preview ready. Installation is optional and includes the family's listed faces. Read the licence, tick the acknowledgement and choose Install family when ready.";
                tabs.SelectedItem=previewTab;
            }
            catch(OperationCanceledException){if(!closed && thisOperation==operationId)status.Text="Preview download cancelled. No font was installed.";}
            catch(Exception ex){if(!closed && thisOperation==operationId)status.Text="Preview download failed: "+ex.Message;}
            finally
            {
                if(!closed && thisOperation==operationId)
                {busy=false;operationCancellation?.Dispose();operationCancellation=null;UpdateButtons();}
            }
        }
        async Task InstallAsync()
        {
            RefreshAvailability();
            if(closed || installing || busy || catalogue==null || selectedFamily==null || !validMatches || !PreviewCurrent() || accept.IsChecked!=true ||
                installedFamilies.Contains(selectedFamily.Id))return;
            var requestedCatalogue=catalogue;var requestedFamily=selectedFamily;var requestedFace=SelectedFace()!;
            CancelOperation();var thisOperation=++operationId;operationCancellation=new CancellationTokenSource();var token=operationCancellation.Token;
            busy=true;status.Text=$"Downloading the available regular/bold/italic faces for {requestedFamily.Name}. Installation has not started.";UpdateButtons();
            try
            {
                var package=await OnlineFontService.DownloadFamilyAsync(requestedCatalogue,requestedFamily.Id,token);
                RefreshAvailability();
                if(closed || token.IsCancellationRequested || thisOperation!=operationId || !ReferenceEquals(catalogue,requestedCatalogue) || !validMatches ||
                    selectedFamily?.Id!=requestedFamily.Id || SelectedFace()?.Path!=requestedFace.Path || SelectedFace()?.BlobSha!=requestedFace.BlobSha ||
                    accept.IsChecked!=true || !PreviewCurrent())return;
                installing=true;status.Text=$"Installing {requestedFamily.Name} for this Windows user…";UpdateButtons();
                var result=await OnlineFontInstallation.InstallAsync(package,token);
                onlineFontLastInstallResult=result;
                if(result.Installed || result.AlreadyInstalled)installedFamilies.Add(requestedFamily.Id);
                if(!closed)
                {
                    status.Text=result.Message+(result.RestartRequired && !result.Message.Contains("restart",StringComparison.OrdinalIgnoreCase)
                        ?" Save your work and restart CtrlZed before choosing the font in Find a Font.":"");
                    accept.IsChecked=false;
                }
            }
            catch(OperationCanceledException){if(!closed && thisOperation==operationId)status.Text="Installation cancelled. No partial installation has been retained.";}
            catch(Exception ex){if(!closed && thisOperation==operationId)status.Text="Font installation failed: "+ex.Message;}
            finally
            {
                installing=false;
                if(!closed && thisOperation==operationId)
                {busy=false;operationCancellation?.Dispose();operationCancellation=null;UpdateButtons();}
            }
        }

        query.TextChanged+=(_,__)=>InvalidateMatches(loaded?"Request changed. Search again for fresh catalogue matches.":"Reading the supplied catalogue…");
        search.Click+=async (_,__)=>await SearchAsync(false);askAi.Click+=async (_,__)=>await SearchAsync(true);
        results.SelectionChanged+=(_,__)=>{if(!populating && !installing)SelectFamily(true);};
        results.PreviewMouseLeftButtonDown+=async (_,args)=>
        {
            if(closed || installing || populating || args.ClickCount>1 ||
                args.OriginalSource is not DependencyObject sourceElement ||
                ItemsControl.ContainerFromElement(results,sourceElement) is not ListBoxItem item)
                return;
            // Activate only an actual result click. Search population selects
            // the first row programmatically and must not start a download.
            // Selecting here completes SelectFamily before PreviewAsync; the
            // native ListBox click then sees the same row, so it cannot reset
            // the newly started preview or queue a second download.
            if(!ReferenceEquals(results.SelectedItem,item))results.SelectedItem=item;
            await PreviewAsync();
        };
        results.PreviewKeyDown+=async (_,args)=>
        {
            if(args.Handled || args.IsRepeat || installing || Keyboard.Modifiers!=ModifierKeys.None ||
                args.Key is not (Key.Enter or Key.Space) || results.SelectedItem is not ListBoxItem)
                return;
            args.Handled=true;
            await PreviewAsync();
        };
        faces.SelectionChanged+=(_,__)=>
        {
            if(changingFaces || closed || installing)return;
            CancelOperation();ResetPreview();status.Text="Face changed. Download a preview of this face before installing.";UpdateButtons();
        };
        size.ValueChanged+=(_,__)=>{sample.FontSize=size.Value*96d/72d;sizeLabel.Text=$"Preview size: {size.Value:0} pt — does not change the document";};
        accept.Checked+=(_,__)=>UpdateButtons();accept.Unchecked+=(_,__)=>UpdateButtons();
        previewButton.Click+=async (_,__)=>await PreviewAsync();install.Click+=async (_,__)=>await InstallAsync();
        openSource.Click+=(_,__)=>
        {
            if(catalogue==null || selectedFamily==null || installing)return;
            try{Process.Start(new ProcessStartInfo(OnlineFontService.SourcePageUrl(catalogue,selectedFamily).AbsoluteUri){UseShellExecute=true});}
            catch(Exception ex){status.Text="Could not open the source page: "+ex.Message;}
        };
        languageSetup.Click+=(_,__)=>
        {
            configuring=true;
            try{LanguageSettings_Click(languageSetup,new RoutedEventArgs());}
            finally{configuring=false;RefreshAvailability();}
        };
        aiSetup.Click+=(_,__)=>
        {
            CancelOperation();
            if(matchesUseAi)InvalidateMatches("AI settings opened. Ask AI again after changing the provider.");
            AiSettings_Click(aiSetup,new RoutedEventArgs());RefreshAvailability();
        };
        close.Click+=(_,__)=>w.Close();w.Activated+=(_,__)=>RefreshAvailability();
        CancelEventHandler ownerClosing=(_,args)=>
        {
            if(!installing)return;
            args.Cancel=true;status.Text="Please let this short font installation finish before closing CtrlZed.";
        };
        EventHandler ownerClosed=(_,__)=>w.Close();
        Closing+=ownerClosing;Closed+=ownerClosed;
        w.Closing+=(_,args)=>
        {
            if(!installing)return;
            args.Cancel=true;status.Text="Please let this short font installation finish before closing the window.";
        };
        w.Closed+=(_,__)=>
        {
            closed=true;CancelOperation();lifetimeCancellation.Cancel();lifetimeCancellation.Dispose();
            Closing-=ownerClosing;Closed-=ownerClosed;
            if(ReferenceEquals(onlineFontWindow,w))onlineFontWindow=null;
        };
        UpdateInformation();UpdateButtons();status.Text="Reading the supplied catalogue. Searches need no extra Google API key.";
        w.Show();_=LoadCatalogueAsync();

        async Task LoadCatalogueAsync()
        {
            try
            {
                var value=await Task.Run(OnlineFontService.LoadCatalogue,lifetimeToken);
                if(closed || lifetimeToken.IsCancellationRequested)return;
                catalogue=value;loaded=true;
                catalogueInfo.Text=$"{catalogue.Families.Count:N0} supported static Google Fonts families. Snapshot {catalogue.GeneratedUtc:dd MMM yyyy}. Variable and unsupported faces are excluded.";
                catalogueInfo.ToolTip=$"{catalogue.Scope}\nSource revision: {catalogue.Commit}\n{catalogue.MetadataFamiliesScanned:N0} source families examined.";
                UpdateInformation();UpdateButtons();await SearchAsync(false);
            }
            catch(OperationCanceledException){ }
            catch(Exception ex){if(!closed){status.Text="The supplied font catalogue could not be loaded: "+ex.Message;UpdateButtons();}}
        }
    }

    string CaptureOnlineFontSample()
    {
        const string fallback="The quick brown fox jumps over the lazy dog. 0123456789";
        try
        {
            var start=Editor.Selection.Start;
            var end=Editor.Selection.End;
            if(Editor.Selection.IsEmpty && start.Paragraph is { } paragraph)
            {
                var before=Math.Min(200,paragraph.ContentStart.GetOffsetToPosition(start));
                start=start.GetPositionAtOffset(-before,LogicalDirection.Backward)??paragraph.ContentStart;end=paragraph.ContentEnd;
            }
            var symbols=Math.Min(1000,start.GetOffsetToPosition(end));
            var sampleEnd=start.GetPositionAtOffset(symbols,LogicalDirection.Forward);
            var text=sampleEnd==null?"":new TextRange(start,sampleEnd).Text;
            if(text.Length>1000)text=text[..1000];
            return string.IsNullOrWhiteSpace(text)?fallback:text;
        }
        catch(ArgumentException){return fallback;}
        catch(InvalidOperationException){return fallback;}
    }
}
