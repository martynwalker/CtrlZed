using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    // Only disposable fixture credentials and in-process replies are used here.
    // The complete pre-existing application profile is restored before exit.
    internal static int RunAiSettingsSelfTest()
    {
        var report=new StringBuilder("Beta 54 AI settings and workshop provider regression\r\n");
        var failures=new List<Exception>();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var originalFiles=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var originalDirectories=Directory.Exists(root)?Directory.GetDirectories(root,"*",SearchOption.AllDirectories)
            .Prepend(root).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var oldWordplay=WordplayWorkshopService.GeneratorOverride;var oldStory=StoryWorkshopService.GeneratorOverride;
        var oldWordplayProvider=WordplayWorkshopService.ProviderGeneratorOverride;var oldStoryProvider=StoryWorkshopService.ProviderGeneratorOverride;
        var oldSend=AiDirectService.StructuredSendOverride;var oldShutdown=Application.Current.ShutdownMode;
        MainWindow? window=null;IDataObject? clipboard=null;var captured=false;var clipboardCaptured=false;
        void Phase(string name,Action action)
        {
            try{action();report.AppendLine("PASS: "+name+".");}
            catch(Exception error){failures.Add(error);report.AppendLine("FAIL: "+name+": "+error);}
        }
        try
        {
            if(Directory.Exists(root))foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))originalFiles[path]=File.ReadAllBytes(path);
            captured=true;clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(root);foreach(var path in Directory.GetFiles(root,"Recovery*"))File.Delete(path);
            WordplayWorkshopService.GeneratorOverride=null;StoryWorkshopService.GeneratorOverride=null;
            WordplayWorkshopService.ProviderGeneratorOverride=(_,__,___)=>throw new InvalidOperationException("Unexpected implicit Wordplay request.");
            StoryWorkshopService.ProviderGeneratorOverride=(_,__,___)=>throw new InvalidOperationException("Unexpected implicit Story request.");
            AiDirectService.StructuredSendOverride=(_,__)=>throw new InvalidOperationException("Unexpected live AI settings request.");
            SeedAiSettingsFixtures();Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Normal,Width=1100,Height=790};
            window.Show();window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
            window.languageSettings.SpellingEnabled=false;window.languageSettings.ContextualSpellingSuggestions=false;
            window.languageSettings.GrammarAndStyleSuggestions=false;window.languageSettings.PoetryRhymeSuggestions=false;
            window.languageSettings.AllowAiLanguageServices=true;SpellCheck.SetIsEnabled(window.Editor,false);
            Phase("dedicated settings preserve existing keys and stage Save or Cancel",()=>window.VerifyAiSettingsWindow(report));
            Phase("main menu opens dedicated local AI settings",()=>window.VerifyAiSettingsMenu(report));
            Phase("explicit workshop routing and configured OpenAI model",()=>VerifyAiSettingsRouting(report));
            Phase("Wordplay local provider choices, default and late-response guards",()=>window.VerifyWordplayProviderWindow(report));
            Phase("Story local provider choices, settings entry and model guards",()=>window.VerifyStoryProviderWindow(report));
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("AI windows and editor cleanup",()=>
            {
                foreach(var dialog in Application.Current.Windows.OfType<AiSettingsWindow>().ToArray())dialog.Close();
                window.wordplayWorkshopWindow?.Close();window.storyWorkshopWindow?.Close();window.characterVoiceWindow?.Close();
                window.closingApproved=true;window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.SetDocumentPassword(null);window.Close();
            });
            WordplayWorkshopService.GeneratorOverride=oldWordplay;StoryWorkshopService.GeneratorOverride=oldStory;
            WordplayWorkshopService.ProviderGeneratorOverride=oldWordplayProvider;StoryWorkshopService.ProviderGeneratorOverride=oldStoryProvider;
            AiDirectService.StructuredSendOverride=oldSend;Application.Current.ShutdownMode=oldShutdown;
            if(captured)Phase("exact original application-data restoration",()=>
            {
                if(Directory.Exists(root))foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))if(!originalFiles.ContainsKey(path))File.Delete(path);
                foreach(var pair in originalFiles){Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);File.WriteAllBytes(pair.Key,pair.Value);}
                if(Directory.Exists(root))foreach(var path in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Append(root).OrderByDescending(path=>path.Length))
                    if(!originalDirectories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originalFiles)RequireReliability(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An original profile file was not restored exactly.");
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>{if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();});
            report.AppendLine("Provider: deterministic in-process fixtures; no live AI requests, real credentials or private source logs.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed54AiSettings.txt"),report.ToString());
            if(failures.Count>0)File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),new AggregateException(failures).ToString());
        }
        return failures.Count==0?0:135;
    }

    const string AiSettingsChatGptKey="fixture-openai-key-54-not-a-real-key";
    const string AiSettingsClaudeKey="fixture-anthropic-key-54-not-a-real-key";
    const string AiSettingsGeminiKey="fixture-google-key-54-not-a-real-key";
    const string AiSettingsReplacementKey="fixture-replacement-key-54-not-a-real-key";

    static string AiSettingsRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
    static string[] AiSettingsFiles=>new[]{"ai-settings.json","ai-profiles.json","ai-secrets.bin"};

    static void SeedAiSettingsFixtures()
    {
        AiDirectService.SaveProfile("ChatGPT","fixture-openai-54",AiSettingsChatGptKey);
        AiDirectService.SaveProfile("Claude","fixture-claude-54",AiSettingsClaudeKey);
        AiDirectService.SaveProfile("Gemini","fixture-gemini-54",AiSettingsGeminiKey);
        AiManager.SetDefaultProvider("Claude");
    }

    static Dictionary<string,byte[]> SnapshotAiSettingsFiles()=>AiSettingsFiles.ToDictionary(name=>name,name=>File.ReadAllBytes(Path.Combine(AiSettingsRoot,name)),StringComparer.Ordinal);

    static void AssertAiSettingsFiles(Dictionary<string,byte[]> expected,string message)
    {
        foreach(var pair in expected)RequireReliability(File.ReadAllBytes(Path.Combine(AiSettingsRoot,pair.Key)).AsSpan().SequenceEqual(pair.Value),message);
    }

    static string ReadFixtureAiKey(string provider)
    {
        var plain=ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(AiSettingsRoot,"ai-secrets.bin")),null,DataProtectionScope.CurrentUser);
        try
        {
            using var document=JsonDocument.Parse(plain);
            return document.RootElement.TryGetProperty(provider,out var key)?key.GetString()??"":"";
        }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }

    void VerifyAiSettingsWindow(StringBuilder report)
    {
        SeedAiSettingsFixtures();var before=SnapshotAiSettingsFiles();
        var settings=new AiSettingsWindow(this);settings.Show();PumpInput();
        var provider=ReliabilityControl<ComboBox>(settings,"AiSettingsProvider");
        var model=ReliabilityControl<TextBox>(settings,"AiSettingsModel");var key=ReliabilityControl<PasswordBox>(settings,"AiSettingsKey");
        var defaultProvider=ReliabilityControl<ComboBox>(settings,"AiSettingsDefault");
        RequireReliability(provider.SelectedItem?.ToString()=="Claude" && defaultProvider.SelectedItem?.ToString()=="Claude" && model.Text=="fixture-claude-54",
            "Dedicated settings did not load the existing saved provider and model.");
        RequireReliability(key.Password.Length==0 && !ReliabilityControl<TextBlock>(settings,"AiSettingsKeyStatus").Text.Contains(AiSettingsClaudeKey,StringComparison.Ordinal),
            "The settings window displayed a stored credential.");
        model.Text="cancelled-claude-model";key.Password=AiSettingsReplacementKey;provider.SelectedItem="Gemini";
        model.Text="cancelled-gemini-model";defaultProvider.SelectedItem="ChatGPT";provider.SelectedItem="Claude";
        RequireReliability(model.Text=="cancelled-claude-model" && key.Password==AiSettingsReplacementKey,
            "Changing providers lost an unsaved per-provider draft.");
        ReliabilityControl<Button>(settings,"AiSettingsCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireReliability(!settings.Saved,"Cancel reported a successful settings save.");
        AssertAiSettingsFiles(before,"Cancel altered an existing provider, key or default file.");

        settings=new AiSettingsWindow(this,"ChatGPT");settings.Show();PumpInput();
        provider=ReliabilityControl<ComboBox>(settings,"AiSettingsProvider");model=ReliabilityControl<TextBox>(settings,"AiSettingsModel");key=ReliabilityControl<PasswordBox>(settings,"AiSettingsKey");
        defaultProvider=ReliabilityControl<ComboBox>(settings,"AiSettingsDefault");
        RequireReliability(provider.SelectedItem?.ToString()=="ChatGPT" && defaultProvider.SelectedItem?.ToString()=="Claude",
            "An initial provider override changed the saved application default.");
        ReliabilityControl<CheckBox>(settings,"AiSettingsRemoveKey").IsChecked=true;
        provider.SelectedItem="Claude";model.Text="fixture-claude-updated-54";
        RequireReliability(key.Password.Length==0,"A stored provider key was loaded into the replacement field.");
        provider.SelectedItem="Gemini";model.Text="fixture-gemini-updated-54";key.Password=AiSettingsReplacementKey;defaultProvider.SelectedItem="Gemini";
        CaptureReliabilityWindow(settings,"CtrlZed54AiSettings.png");
        ReliabilityControl<Button>(settings,"AiSettingsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireReliability(settings.Saved && !settings.IsVisible,"Valid settings did not save and close.");
        RequireReliability(AiManager.GetDefaultProvider()=="Gemini" && AiDirectService.GetProfile("Claude").Model=="fixture-claude-updated-54" &&
            AiDirectService.GetProfile("Gemini").Model=="fixture-gemini-updated-54","The saved default or per-provider models did not persist.");
        RequireReliability(!AiDirectService.GetProfile("ChatGPT").HasKey && ReadFixtureAiKey("ChatGPT")=="" &&
            ReadFixtureAiKey("Claude")==AiSettingsClaudeKey && ReadFixtureAiKey("Gemini")==AiSettingsReplacementKey,
            "Explicit removal, blank-to-keep or replacement changed the wrong credential.");
        foreach(var name in AiSettingsFiles)
        {
            var content=Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(AiSettingsRoot,name)));
            RequireReliability(!new[]{AiSettingsChatGptKey,AiSettingsClaudeKey,AiSettingsGeminiKey,AiSettingsReplacementKey}.Any(secret=>content.Contains(secret,StringComparison.Ordinal)),
                "A saved profile or default file contains a plaintext fixture credential.");
        }
        var after=SnapshotAiSettingsFiles();settings=new AiSettingsWindow(this);settings.Show();PumpInput();
        RequireReliability(ReliabilityControl<ComboBox>(settings,"AiSettingsProvider").SelectedItem?.ToString()=="Gemini" &&
            ReliabilityControl<TextBox>(settings,"AiSettingsModel").Text=="fixture-gemini-updated-54" &&
            ReliabilityControl<PasswordBox>(settings,"AiSettingsKey").Password.Length==0,
            "Reopening settings did not retain the default/model or exposed a saved key.");
        ReliabilityControl<TextBox>(settings,"AiSettingsModel").Text="discard-on-window-close";settings.Close();PumpInput();
        AssertAiSettingsFiles(after,"Closing settings with the window control saved an uncommitted draft.");
        var rejected=false;try{AiManager.SetDefaultProvider("Copilot");}catch(ArgumentException){rejected=true;}
        RequireReliability(rejected && AiManager.GetDefaultProvider()=="Gemini","A browser-only or unsupported provider became the direct-AI default.");
        var fingerprint=AiDirectService.ConfigurationFingerprint("Claude");AiDirectService.SaveProfile("Claude",AiDirectService.GetProfile("Claude").Model,AiSettingsReplacementKey);
        RequireReliability(AiDirectService.ConfigurationFingerprint("Claude")!=fingerprint && AiDirectService.GetProfile("Claude").HasKey,
            "Replacing an existing key did not invalidate the selected provider configuration.");

        var beforeFailure=SnapshotAiSettingsFiles();var beforeFailedRevision=AiDirectService.ConfigurationFingerprint("Claude");
        settings=new AiSettingsWindow(this,"Claude");settings.Show();PumpInput();
        ReliabilityControl<TextBox>(settings,"AiSettingsModel").Text="must-not-survive-failed-save";
        ReliabilityControl<PasswordBox>(settings,"AiSettingsKey").Password=AiSettingsClaudeKey;
        var defaultPath=Path.Combine(AiSettingsRoot,"ai-settings.json");File.Delete(defaultPath);Directory.CreateDirectory(defaultPath);
        try
        {
            ReliabilityControl<Button>(settings,"AiSettingsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
            RequireReliability(!settings.Saved && settings.IsVisible && ReliabilityControl<TextBlock>(settings,"AiSettingsStatus").Text.Contains("could not be saved",StringComparison.OrdinalIgnoreCase),
                "A failed settings transaction closed the window or claimed success.");
            foreach(var name in new[]{"ai-profiles.json","ai-secrets.bin"})RequireReliability(File.ReadAllBytes(Path.Combine(AiSettingsRoot,name)).AsSpan().SequenceEqual(beforeFailure[name]),
                "A failed default save left a partial model or credential update.");
            RequireReliability(AiDirectService.ConfigurationFingerprint("Claude")==beforeFailedRevision,"A rolled-back save changed provider configuration identity.");
        }
        finally{settings.Close();Directory.Delete(defaultPath);File.WriteAllBytes(defaultPath,beforeFailure["ai-settings.json"]);}
        AssertAiSettingsFiles(beforeFailure,"A failed settings transaction did not retain the original profile.");
        report.AppendLine("Existing models and encrypted keys loaded without disclosure. Provider drafts survived switching; Cancel and window Close left disk unchanged. Save persisted independent models and default, blank kept the existing key, explicit remove cleared only its provider and replacement remained DPAPI encrypted. Unsupported direct defaults were rejected, and replacing a present key changed configuration identity. A real filesystem save failure reported an error, kept the window open and rolled back every earlier file without changing configuration identity.");
    }

    void VerifyAiSettingsMenu(StringBuilder report)
    {
        var before=SnapshotAiSettingsFiles();var opened=false;Exception? failure=null;
        var menu=FindName("AiSettingsMenu") as MenuItem??throw new InvalidOperationException("Tools has no named AI Settings menu entry.");
        Dispatcher.BeginInvoke(new Action(()=>
        {
            var settings=Application.Current.Windows.OfType<AiSettingsWindow>().SingleOrDefault();
            if(settings==null)
            {
                failure=new InvalidOperationException("The AI Settings menu opened a different assistant.");
                foreach(var dialog in Application.Current.Windows.OfType<Window>().Where(candidate=>ReferenceEquals(candidate.Owner,this)).ToArray())dialog.Close();
                return;
            }
            try{RequireReliability(ReferenceEquals(settings.Owner,this),"The settings menu lost its editor owner.");opened=true;}
            catch(Exception error){failure=error;}
            finally{settings.Close();}
        }),DispatcherPriority.Loaded);
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));PumpInput();
        if(failure!=null)throw failure;RequireReliability(opened,"The main AI settings menu did not open its configuration window.");
        AssertAiSettingsFiles(before,"Opening and closing main-menu AI settings changed configuration.");
        report.AppendLine("Tools → AI Settings opened the separate local configuration window directly; closing it changed no profile or default.");
    }

    static void VerifyAiSettingsRouting(StringBuilder report)
    {
        SeedAiSettingsFixtures();AiManager.SetDefaultProvider("Gemini");var calls=new List<string>();
        var oldWordplay=WordplayWorkshopService.ProviderGeneratorOverride;var oldStory=StoryWorkshopService.ProviderGeneratorOverride;
        var oldSend=AiDirectService.StructuredSendOverride;
        try
        {
            WordplayWorkshopService.ProviderGeneratorOverride=(provider,_,token)=>{token.ThrowIfCancellationRequested();calls.Add("wordplay:"+provider);return Task.FromResult(WordplayFixtureResponse());};
            StoryWorkshopService.ProviderGeneratorOverride=(provider,_,token)=>{token.ThrowIfCancellationRequested();calls.Add("story:"+provider);return Task.FromResult(WorkshopResponse());};
            AwaitSentenceTask(WordplayWorkshopService.GenerateAsync(new WordplayWorkshopState{SeedText=WordplaySeedFixture}));
            AwaitSentenceTask(WordplayWorkshopService.GenerateAsync(new WordplayWorkshopState{SeedText=WordplaySeedFixture},"Claude",CancellationToken.None));
            AwaitSentenceTask(StoryWorkshopService.GenerateAsync(new StoryWorkshopState(),"A detective must choose whom to trust."));
            AwaitSentenceTask(StoryWorkshopService.GenerateAsync(new StoryWorkshopState(),"A detective must choose whom to trust.","ChatGPT",CancellationToken.None));
            RequireReliability(calls.SequenceEqual(new[]{"wordplay:Gemini","wordplay:Claude","story:Gemini","story:ChatGPT"}),
                "A workshop service ignored an explicit provider or its backward-compatible default.");
            WordplayWorkshopService.ProviderGeneratorOverride=null;var httpCalls=0;
            AiDirectService.StructuredSendOverride=async(request,token)=>
            {
                httpCalls++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                RequireReliability(body.RootElement.GetProperty("model").GetString()=="fixture-openai-54" &&
                    request.Headers.Authorization?.Parameter==AiSettingsChatGptKey,"An explicit OpenAI request used a different provider's configured model or credential.");
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ReliabilityOpenAiEnvelope(WordplayFixtureResponse()),Encoding.UTF8,"application/json")};
            };
            AwaitSentenceTask(WordplayWorkshopService.GenerateAsync(new WordplayWorkshopState{SeedText=WordplaySeedFixture},"ChatGPT",CancellationToken.None));
            RequireReliability(httpCalls==1 && AiManager.GetDefaultProvider()=="Gemini","Explicit provider routing changed the global default or introduced extra requests.");
            report.AppendLine("Both legacy service signatures used the saved default; explicit provider overloads routed independently. A real request serializer with a synthetic HTTP reply used the selected OpenAI model and credential while Gemini remained the global default, exactly once.");
        }
        finally{WordplayWorkshopService.ProviderGeneratorOverride=oldWordplay;StoryWorkshopService.ProviderGeneratorOverride=oldStory;AiDirectService.StructuredSendOverride=oldSend;}
    }

    sealed class AiProviderPending
    {
        internal string Provider { get; }
        internal CancellationToken Token { get; }
        internal TaskCompletionSource<string> Reply { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Returned { get; set; }
        internal AiProviderPending(string provider,CancellationToken token){Provider=provider;Token=token;}
    }

    static void PumpAiSettingsRefresh()
    {
        var ready=false;var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(1200)};
        timer.Tick+=(_,__)=>{timer.Stop();ready=true;};timer.Start();WaitSentence(()=>ready,"The provider configuration refresh did not run.");PumpInput();
    }

    static void CloseAiSettingsEntry(Window owner,Button entry,string expectedProvider)
    {
        var opened=false;Exception? failure=null;
        owner.Dispatcher.BeginInvoke(new Action(()=>
        {
            var settings=Application.Current.Windows.OfType<AiSettingsWindow>().SingleOrDefault();
            if(settings==null)
            {
                failure=new InvalidOperationException("The workshop settings entry opened the writing assistant instead of dedicated settings.");
                foreach(var dialog in Application.Current.Windows.OfType<Window>().Where(candidate=>ReferenceEquals(candidate.Owner,owner)).ToArray())dialog.Close();
                return;
            }
            try
            {
                RequireReliability(ReferenceEquals(settings.Owner,owner) && ReliabilityControl<ComboBox>(settings,"AiSettingsProvider").SelectedItem?.ToString()==expectedProvider,
                    "The workshop AI settings entry lost its owner or selected provider.");opened=true;
            }
            catch(Exception error){failure=error;}
            finally{settings.Close();}
        }),DispatcherPriority.Loaded);
        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        if(failure!=null)throw failure;RequireReliability(opened,"The dedicated settings entry was not reached.");
    }

    void VerifyWordplayProviderWindow(StringBuilder report)
    {
        SeedAiSettingsFixtures();FreshReliabilityDocument();AiManager.SetDefaultProvider("Claude");var pending=new List<AiProviderPending>();
        WordplayWorkshopService.ProviderGeneratorOverride=async(provider,_,token)=>
        {var request=new AiProviderPending(provider,token);pending.Add(request);var text=await request.Reply.Task;request.Returned=true;return text;};
        ShowWordplayWorkshop();var dialog=wordplayWorkshopWindow!;var providerBox=ReliabilityControl<ComboBox>(dialog,"WordplayProvider");
        RequireReliability(providerBox.SelectedItem?.ToString()=="Claude" && pending.Count==0,"Wordplay did not open on the saved default or made an implicit request.");
        WordplayControl<TextBox>(dialog,"WordplaySeed").Text=WordplaySeedFixture;var beforeChoice=WordplayJson(SnapshotWordplayWorkshop());var revision=wordplayWorkshopRevision;dirty=false;
        providerBox.SelectedItem="ChatGPT";PumpInput();
        RequireReliability(AiManager.GetDefaultProvider()=="Claude" && !dirty && wordplayWorkshopRevision==revision && WordplayJson(SnapshotWordplayWorkshop())==beforeChoice && pending.Count==0,
            "A local Wordplay provider choice changed the application default, document metadata or request count.");
        var explore=WordplayControl<Button>(dialog,"WordplayExplore");var cancel=WordplayControl<Button>(dialog,"WordplayCancel");
        void Begin(int count,string selected)
        {
            explore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitSentence(()=>pending.Count==count,"The selected-provider exploration did not start.");
            RequireReliability(pending[^1].Provider==selected,"Wordplay sent to the saved default rather than the selected provider.");AssertReliabilityBusy(dialog,"Wordplay");
        }
        void Return(int index)
        {pending[index].Reply.SetResult(WordplayFixtureResponse());WaitSentence(()=>pending[index].Returned,"The delayed provider fixture did not return.");PumpInput();}
        Begin(1,"ChatGPT");Return(0);WaitSentence(()=>!cancel.IsEnabled,"The selected-provider exploration did not finish.");
        WordplayControl<TextBox>(dialog,"WordplayEdit").Text="A favourite survives provider changes.";WordplayControl<Button>(dialog,"WordplayKeep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var retained=WordplayJson(SnapshotWordplayWorkshop());Begin(2,"ChatGPT");providerBox.SelectedItem="Gemini";PumpInput();
        RequireReliability(pending[1].Token.IsCancellationRequested,"Changing Wordplay provider did not immediately cancel its old request.");AssertReliabilityIndicatorHidden(dialog,"Wordplay");
        Return(1);RequireReliability(WordplayJson(SnapshotWordplayWorkshop())==retained,"A late reply from the old Wordplay provider overwrote completed ideas.");
        Begin(3,"Gemini");AiManager.SetDefaultProvider("ChatGPT");PumpAiSettingsRefresh();
        RequireReliability(providerBox.SelectedItem?.ToString()=="Gemini" && !pending[2].Token.IsCancellationRequested,
            "Changing the application default hijacked an already selected workshop provider.");
        Return(2);WaitSentence(()=>!cancel.IsEnabled,"The retained local provider did not finish its reply.");CaptureReliabilityWindow(dialog,"CtrlZed54WordplayProviders.png");
        var completed=WordplayJson(SnapshotWordplayWorkshop());Begin(4,"Gemini");
        AiDirectService.SaveProfile("Gemini",AiDirectService.GetProfile("Gemini").Model,AiSettingsReplacementKey);
        WaitSentence(()=>pending[3].Token.IsCancellationRequested,"Replacing a present selected-provider key did not cancel the active Wordplay request.");AssertReliabilityIndicatorHidden(dialog,"Wordplay");
        Return(3);RequireReliability(WordplayJson(SnapshotWordplayWorkshop())==completed,"A reply authorised with an obsolete key committed after replacement.");
        var beforeSettings=SnapshotAiSettingsFiles();CloseAiSettingsEntry(dialog,ReliabilityControl<Button>(dialog,"WordplayAiSettings"),"Gemini");
        AssertAiSettingsFiles(beforeSettings,"Opening and cancelling workshop AI settings modified provider configuration.");
        RequireReliability(pending.Count==4,"A provider choice or settings window made an extra exploration request.");
        dialog.Close();ShowWordplayWorkshop();dialog=wordplayWorkshopWindow!;
        RequireReliability(ReliabilityControl<ComboBox>(dialog,"WordplayProvider").SelectedItem?.ToString()=="ChatGPT" && WordplayJson(SnapshotWordplayWorkshop())==completed,
            "A reopened Wordplay window failed to use the new saved default or lost existing ideas.");
        dialog.Close();report.AppendLine("Wordplay opened on the saved default, routed explicit requests to its local choice and retained results/favourites through provider changes. Local selection did not dirty/save document metadata or change the default. Cross-provider and replaced-key replies were cancelled and discarded. A global default change left the current window alone, while a new window adopted it. AI settings opened directly without extra requests.");
    }

    void VerifyStoryProviderWindow(StringBuilder report)
    {
        SeedAiSettingsFixtures();FreshReliabilityDocument();AiManager.SetDefaultProvider("Gemini");var pending=new List<AiProviderPending>();
        StoryWorkshopService.ProviderGeneratorOverride=async(provider,_,token)=>
        {var request=new AiProviderPending(provider,token);pending.Add(request);var text=await request.Reply.Task;request.Returned=true;return text;};
        ShowStoryWorkshop();var dialog=storyWorkshopWindow!;var providerBox=ReliabilityControl<ComboBox>(dialog,"WorkshopProvider");
        RequireReliability(providerBox.SelectedItem?.ToString()=="Gemini" && pending.Count==0,"Story did not open on the saved default or made an implicit request.");
        var beforeChoice=WorkshopJson(SnapshotStoryWorkshop());var revision=storyWorkshopRevision;dirty=false;providerBox.SelectedItem="Claude";PumpInput();
        RequireReliability(AiManager.GetDefaultProvider()=="Gemini" && !dirty && storyWorkshopRevision==revision && WorkshopJson(SnapshotStoryWorkshop())==beforeChoice,
            "A local Story provider choice changed the global default or planning metadata.");
        var input=WorkshopControl<TextBox>(dialog,"WorkshopInput");var send=WorkshopControl<Button>(dialog,"WorkshopSend");var cancel=WorkshopControl<Button>(dialog,"WorkshopCancel");
        void Begin(int count,string selected)
        {
            input.Text="A detective must choose whom to trust.";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>pending.Count==count,"The selected-provider planning turn did not start.");
            RequireReliability(pending[^1].Provider==selected,"Story sent to the global default rather than the selected provider.");AssertReliabilityBusy(dialog,"Workshop");
        }
        void Return(int index)
        {pending[index].Reply.SetResult(WorkshopResponse(reply:"Consider how that choice tests the detective.",questions:Array.Empty<string>()));WaitSentence(()=>pending[index].Returned,"The delayed Story provider fixture did not return.");PumpInput();}
        Begin(1,"Claude");Return(0);WaitSentence(()=>!cancel.IsEnabled,"The selected-provider planning turn did not finish.");
        var retained=WorkshopJson(SnapshotStoryWorkshop());Begin(2,"Claude");providerBox.SelectedItem="ChatGPT";PumpInput();
        RequireReliability(pending[1].Token.IsCancellationRequested,"Switching Story provider did not cancel its old request.");AssertReliabilityIndicatorHidden(dialog,"Workshop");
        Return(1);RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==retained,"A late Story reply from the old provider changed planning.");
        Begin(3,"ChatGPT");AiDirectService.SaveProfile("ChatGPT","fixture-openai-revised-54",null);
        WaitSentence(()=>pending[2].Token.IsCancellationRequested,"A selected-provider model change did not cancel the active Story turn.");AssertReliabilityIndicatorHidden(dialog,"Workshop");
        Return(2);RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==retained,"An obsolete-model reply changed planning notes.");
        RequireReliability(ReliabilityControl<TextBlock>(dialog,"WorkshopProviderDetails").Text.Contains("fixture-openai-revised-54",StringComparison.Ordinal),
            "The Story provider details did not refresh the configured model.");
        Begin(4,"ChatGPT");var beforeSettings=SnapshotAiSettingsFiles();CloseAiSettingsEntry(dialog,ReliabilityControl<Button>(dialog,"WorkshopAiSettings"),"ChatGPT");
        RequireReliability(pending[3].Token.IsCancellationRequested,"Opening AI settings did not cancel the unfinished Story turn.");Return(3);
        AssertAiSettingsFiles(beforeSettings,"Opening and cancelling Story AI settings changed provider configuration.");
        RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==retained && pending.Count==4,"Settings generated a turn or admitted a cancelled reply.");
        AiManager.SetDefaultProvider("Claude");PumpAiSettingsRefresh();
        RequireReliability(providerBox.SelectedItem?.ToString()=="ChatGPT","A global default edit changed the open Story window's local choice.");
        dialog.Close();ShowStoryWorkshop();dialog=storyWorkshopWindow!;
        RequireReliability(ReliabilityControl<ComboBox>(dialog,"WorkshopProvider").SelectedItem?.ToString()=="Claude" && WorkshopJson(SnapshotStoryWorkshop())==retained,
            "A new Story window did not adopt the saved default or lost the planning conversation.");
        CaptureReliabilityWindow(dialog,"CtrlZed54StoryProviders.png");dialog.Close();
        report.AppendLine("Story independently routed its selected provider, cancelled old-provider/model requests and discarded their late replies without changing notes. Opening dedicated settings cancelled unfinished work, preserved the input and made no AI call. The open window retained its local choice after a global-default edit; a reopened window used that saved default and retained the existing conversation.");
    }
}
