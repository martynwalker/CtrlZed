using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    // This gate uses original synthetic phrases and a deterministic transport;
    // it checks editing/privacy contracts rather than a live model's wit.
    internal static int RunWordplayWorkshopSelfTest()
    {
        var report=new StringBuilder("Beta 53 Wordplay Workshop deterministic regression\r\n");
        var failures=new List<Exception>();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var temporary=Path.Combine(Path.GetTempPath(),"CtrlZed53Wordplay_"+Guid.NewGuid().ToString("N"));
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var directories=Directory.Exists(root)?Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Prepend(root)
            .ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transport=WordplayWorkshopService.GeneratorOverride;
        var shutdown=Application.Current.ShutdownMode;
        MainWindow? window=null;IDataObject? clipboard=null;var captured=false;var clipboardCaptured=false;
        void Phase(string name,Action action)
        {
            try{action();report.AppendLine("PASS: "+name+".");}
            catch(Exception error){failures.Add(error);report.AppendLine("FAIL: "+name+": "+error);}
        }
        try
        {
            if(Directory.Exists(root))foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))originals[path]=File.ReadAllBytes(path);
            captured=true;clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(root);Directory.CreateDirectory(temporary);
            foreach(var path in Directory.GetFiles(root,"Recovery*"))File.Delete(path);
            WordplayWorkshopService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected implicit wordplay request.");
            Phase("creative axes, exact associations and bounded original phrase response",()=>VerifyWordplayContract(report));
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Normal,Width=1080,Height=780};
            window.Show();window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
            window.languageSettings.SpellingEnabled=false;window.languageSettings.ContextualSpellingSuggestions=false;
            window.languageSettings.GrammarAndStyleSuggestions=false;window.languageSettings.PoetryRhymeSuggestions=false;
            window.languageSettings.AllowAiLanguageServices=true;SpellCheck.SetIsEnabled(window.Editor,false);
            Phase("both workshops survive native, protected and recovery saves independently",()=>window.VerifyWordplayPersistence(temporary,report));
            Phase("legacy/new documents, native save and explicit body export ownership",()=>window.VerifyWordplayLifecycle(temporary,report));
            Phase("real word pile, mixed axes, favourites, Copy and rich Apply",()=>window.VerifyWordplayWindow(report));
            Phase("cancelled, changed and obsolete exploration cannot overwrite saved work",()=>window.VerifyWordplayStaleWindow(report));
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("window and editor cleanup",()=>
            {
                window.wordplayWorkshopWindow?.Close();window.storyWorkshopWindow?.Close();window.characterVoiceWindow?.Close();
                window.closingApproved=true;window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.SetDocumentPassword(null);window.Close();
            });
            WordplayWorkshopService.GeneratorOverride=transport;Application.Current.ShutdownMode=shutdown;
            if(captured)Phase("exact original application-data restoration",()=>
            {
                if(Directory.Exists(root))foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))if(!originals.ContainsKey(path))File.Delete(path);
                foreach(var pair in originals){Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);File.WriteAllBytes(pair.Key,pair.Value);}
                if(Directory.Exists(root))foreach(var path in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Append(root).OrderByDescending(x=>x.Length))
                    if(!directories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originals)RequireWordplay(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An original profile file was not restored exactly.");
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>{if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();});
            try{Directory.Delete(temporary,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            report.AppendLine("Transport: deterministic fixture only; originality and literary quality of a live response remain a writer acceptance check.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed53WordplayWorkshop.txt"),report.ToString());
            if(failures.Count>0)File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),new AggregateException(failures).ToString());
        }
        return failures.Count==0?0:133;
    }

    const string WordplaySeedFixture="WORDPLAY_PRIVATE_SEED_53\nclock\npaper\nsea\npromise";
    const string WordplayBodySecret="MANUSCRIPT_NOT_FOR_WORDPLAY_53";
    const string WordplayStorySecret="STORY_PLAN_NOT_FOR_WORDPLAY_53";
    const string WordplayFavouriteSecret="OLD_FAVOURITE_NOT_FOR_PROVIDER_53";

    static WordplayWorkshopOptions WordplayMixedOptions()=>new(){Form="Song",Style="Philosophical",ComicFlavour="Deadpan",
        Device="Pun",SubjectTarget="Observational",Situation="Farce"};

    static string WordplayFixtureResponse()
        =>JsonSerializer.Serialize(new{connections=Enumerable.Range(1,6).Select(number=>new
        {
            title="Clock and paper direction "+number,
            anchors=new[]{"clock","paper"},
            bridge="Direction "+number+" connects measured time with a fragile written promise, rather than treating the seeds as synonyms.",
            examples=new[]{new{text="The paper clock filed promise number "+number+" under tomorrow.",device="Personification and reversal"},
                new{text="At tide "+number+", the clock discovered that a paper sea keeps no appointments.",device="Literal and figurative contrast"}}
        }).ToArray()});

    static WordplayWorkshopState WordplayFixtureState(bool favourite=true)
    {
        var state=new WordplayWorkshopState{SeedText=WordplaySeedFixture,Target="A gentle thought about postponed promises.",Options=WordplayMixedOptions()};
        state.Results=WordplayWorkshopService.ParseResponse(state,WordplayFixtureResponse()).Select(value=>value.DeepClone()).ToList();
        if(favourite)state.Favourites.Add(WordplayFavourite.FromConnection(state.Results[0],WordplayFavouriteSecret,state.Options,state.Target));
        state.Validate();return state;
    }

    static string WordplayJson(WordplayWorkshopState state)=>JsonSerializer.Serialize(state);

    static void VerifyWordplayContract(StringBuilder report)
    {
        var empty=new WordplayWorkshopState();empty.Validate();
        RequireWordplay(!empty.HasContent && empty.Options.IsDefault,"A fresh all-General wordplay workshop already contains authored data.");
        foreach(var choices in new[]{WordplayWorkshopChoices.Forms,WordplayWorkshopChoices.Styles,WordplayWorkshopChoices.ComicFlavours,
            WordplayWorkshopChoices.Devices,WordplayWorkshopChoices.SubjectTargets,WordplayWorkshopChoices.Situations})
            RequireWordplay(choices[0]=="General","A creative axis does not default to General.");
        RequireWordplay(new[]{"Poetry","Song","Novel"}.All(WordplayWorkshopChoices.Forms.Contains) &&
            new[]{"Philosophical","Comic"}.All(WordplayWorkshopChoices.Styles.Contains) &&
            new[]{"Subtle and understated","Deadpan","Sarcastic","Ironic","Self-deprecating","Cringe","Awkward"}.All(WordplayWorkshopChoices.ComicFlavours.Contains) &&
            new[]{"Banter","Pun","Highbrow","Anti-humour"}.All(WordplayWorkshopChoices.Devices.Contains) &&
            new[]{"Observational","Satire","Parody","Insult"}.All(WordplayWorkshopChoices.SubjectTargets.Contains) &&
            new[]{"Slapstick","Satirical situations","Character comedy","Farce"}.All(WordplayWorkshopChoices.Situations.Contains),
            "The requested forms, humour flavours, devices or subject/situation choices are missing.");
        var state=WordplayFixtureState();var original=WordplayJson(state);var prompt=WordplayWorkshopService.BuildPrompt(state);
        foreach(var input in new[]{WordplaySeedFixture,"Song","Philosophical","Deadpan","Pun","Observational","Farce",state.Target})
            RequireWordplay(prompt.Contains(input,StringComparison.Ordinal) || prompt.Contains(JsonSerializer.Serialize(input).Trim('"'),StringComparison.Ordinal),
                "The exploration prompt omitted a selected creative input: "+input);
        RequireWordplay(!prompt.Contains(WordplayFavouriteSecret,StringComparison.Ordinal) && !prompt.Contains(WordplayBodySecret,StringComparison.Ordinal) &&
            !prompt.Contains(WordplayStorySecret,StringComparison.Ordinal),"Exploration included unrelated saved favourites, story planning or manuscript text.");
        var connections=WordplayWorkshopService.ParseResponse(state,WordplayFixtureResponse());
        RequireWordplay(connections.Count==6 && connections.All(value=>value.Examples.Count==2 && value.Anchors.All(anchor=>state.SeedText.Contains(anchor,StringComparison.Ordinal))) &&
            connections.SelectMany(value=>value.Examples).Select(value=>value.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count()==12,
            "The provider contract did not retain six traceable connections and twelve distinct phrases.");
        RequireWordplay(WordplayJson(state)==original,"Parsing an exploration mutated its saved input state.");
        var invalid=state.DeepClone();invalid.SchemaVersion=999;RejectWordplay(()=>invalid.Validate(),"An unsupported wordplay schema was accepted.");
        invalid=state.DeepClone();invalid.Options.Form="Unknown form";RejectWordplay(()=>invalid.Validate(),"An unknown creative choice was accepted.");
        invalid=state.DeepClone();invalid.SeedText=new string('x',12001);invalid.Results.Clear();RejectWordplay(()=>invalid.Validate(),"An oversized word pile was accepted or truncated.");
        invalid=state.DeepClone();invalid.Results[0].Anchors[0]="NOT_IN_THIS_SOURCE";RejectWordplay(()=>invalid.Validate(),"A fabricated source anchor entered saved results.");
        var response=WordplayFixtureResponse();
        foreach(var malformed in new[]{"{}","```json\n"+response+"\n```",response+" trailing",
            response.Replace("\"connections\":","\"extra\":true,\"connections\":",StringComparison.Ordinal),
            response.Replace("\"connections\":","\"connections\":[],\"connections\":",StringComparison.Ordinal),
            response.Replace("\"clock\"","\"NOT_IN_THIS_SOURCE\"",StringComparison.Ordinal),
            response.Replace("The paper clock filed promise number 2 under tomorrow.","The paper clock filed promise number 1 under tomorrow.",StringComparison.Ordinal),
            response.Replace(JsonSerializer.Serialize("The paper clock filed promise number 1 under tomorrow."),JsonSerializer.Serialize(state.SeedText),StringComparison.Ordinal),
            JsonSerializer.Serialize(new{connections=Array.Empty<object>()})})
            RejectWordplay(()=>WordplayWorkshopService.ParseResponse(state,malformed),"Malformed, copied, duplicate or ungrounded exploration output was accepted.");
        var copied=WordplayFavourite.FromConnection(connections[0],"A phrase kept and edited by the author.",state.Options,state.Target);
        var unedited=WordplayFavourite.FromConnection(connections[1],connections[1].Examples[0].Text,state.Options,state.Target);
        RequireWordplay(unedited.Text==connections[1].Examples[0].Text && unedited.Device==connections[1].Examples[0].Device,
            "Keeping an unedited example lost its actual device label.");
        var kept=JsonSerializer.Serialize(copied);var clone=state.DeepClone();clone.Results[0].Bridge="Only the clone changed.";clone.Options.Form="Novel";
        RequireWordplay(state.Options.Form=="Song" && state.Results[0].Bridge!="Only the clone changed.","A metadata clone shares mutable options or associations.");
        var changed=state.DeepClone();changed.Favourites.Clear();changed.Favourites.Add(copied);changed.SeedText="A different pile";changed.Results.Clear();changed.Options.Form="Novel";changed.Validate();
        RequireWordplay(JsonSerializer.Serialize(changed.Favourites[0])==kept && changed.Favourites[0].Options.Form=="Song",
            "Changing a pile or options rewrote a kept phrase's original provenance.");
        var calls=0;WordplayWorkshopService.GeneratorOverride=(actual,token)=>{calls++;token.ThrowIfCancellationRequested();return Task.FromResult(response);};
        var operation=WordplayWorkshopService.GenerateAsync(state);AwaitSentenceTask(operation);
        RequireWordplay(calls==1 && operation.Result.Count==6,"An explicit exploration did not use one provider request.");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{AwaitSentenceTask(WordplayWorkshopService.GenerateAsync(state,cancellation.Token));throw new InvalidOperationException("Pre-cancelled exploration returned results.");}
            catch(OperationCanceledException){}
            RequireWordplay(calls==1,"A pre-cancelled exploration reached the provider.");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var late=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            WordplayWorkshopService.GeneratorOverride=(_,__)=>late.Task;
            var pending=WordplayWorkshopService.GenerateAsync(state,cancellation.Token);cancellation.Cancel();late.SetResult(response);
            try{AwaitSentenceTask(pending);throw new InvalidOperationException("A late cancelled service reply remained usable.");}
            catch(OperationCanceledException){}
        }
        report.AppendLine("Independent General defaults permit mixed creative axes; the request contains only the pile, options and intention. Strict response checks require six grounded directions and twelve distinct phrases. Kept edits retain independent provenance and pre/post-cancellation is enforced.");
    }

    void SetWordplayFixture(string reason)
    {
        var state=WordplayFixtureState();state.Id=SnapshotWordplayWorkshop().Id;CommitWordplayWorkshop(state,reason);
    }

    void VerifyWordplayPersistence(string directory,StringBuilder report)
    {
        wordplayWorkshopWindow?.Close();storyWorkshopWindow?.Close();SetDocumentPassword(null);
        SentenceSetDocument(new FlowDocument(new Paragraph(new Run(WordplayBodySecret))),EditorViewMode.Continuous);
        var story=WorkshopFixtureState();story.Id=SnapshotStoryWorkshop().Id;story.Story["Title"].Text=WordplayStorySecret;
        CommitStoryWorkshop(story,"Preserve a separate story plan.");var storyJson=WorkshopJson(SnapshotStoryWorkshop());
        dirty=false;ResetSentenceUndo();SetWordplayFixture("Keep a word pile and its favourite.");
        var expected=WordplayJson(SnapshotWordplayWorkshop());var body=PagedDocumentText();
        RequireWordplay(dirty && !Editor.CanUndo && WorkshopJson(SnapshotStoryWorkshop())==storyJson,"Wordplay metadata changed body Undo or the separate Story Workshop.");
        var printed=CloneDocument(forPrint:true);
        RequireWordplay(new TextRange(printed.ContentStart,printed.ContentEnd).Text==body && !body.Contains(WordplaySeedFixture,StringComparison.Ordinal),
            "Saved wordplay entered the manuscript or printed body.");
        using(var native=new MemoryStream())
        {
            WriteCzdArchive(native);native.Position=0;var package=ReadCzdArchive(native);
            RequireWordplay(package.Settings?.WordplayWorkshop!=null && WordplayJson(package.Settings.WordplayWorkshop)==expected &&
                package.Settings.StoryWorkshop!=null && WorkshopJson(package.Settings.StoryWorkshop)==storyJson &&
                new TextRange(package.Document.ContentStart,package.Document.ContentEnd).Text==body,"Native saving lost or combined either workshop's independent metadata.");
            using var password=WorkshopPassword();using var encrypted=new MemoryStream();CzdProtection.Encrypt(encrypted,native.ToArray(),password);encrypted.Position=0;
            var decoded=CzdProtection.Decrypt(encrypted,password);using var archive=new MemoryStream(decoded);var protectedPackage=ReadCzdArchive(archive);
            RequireWordplay(WordplayJson(protectedPackage.Settings!.WordplayWorkshop!)==expected && WorkshopJson(protectedPackage.Settings.StoryWorkshop!)==storyJson,
                "Protected native saving lost one workshop's data.");
        }
        dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());
        var recovered=JsonSerializer.Deserialize<CzdSettings>(File.ReadAllText(recoverySettingsFile))??throw new InvalidOperationException("Recovery settings could not be read.");
        RequireWordplay(WordplayJson(recovered.WordplayWorkshop!)==expected && WorkshopJson(recovered.StoryWorkshop!)==storyJson,"Plain recovery lost a workshop or favourite provenance.");
        var recoveredBody=new FlowDocument();using(var source=File.OpenRead(recoveryFile))new TextRange(recoveredBody.ContentStart,recoveredBody.ContentEnd).Load(source,DataFormats.Rtf);
        Editor.Document=recoveredBody;ApplyRecoveredSettings(recovered);
        RequireWordplay(WordplayJson(SnapshotWordplayWorkshop())==expected && WorkshopJson(SnapshotStoryWorkshop())==storyJson && PagedDocumentText()==body,
            "Applying recovery changed either workshop or body.");
        DeletePlainRecoveryFiles();using(var password=WorkshopPassword())SetDocumentPassword(password.Copy());dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());
        using(var input=File.OpenRead(ProtectedRecoveryFile))using(var password=WorkshopPassword())
        {
            var decoded=CzdProtection.Decrypt(input,password);using var archive=new MemoryStream(decoded);var package=ReadCzdArchive(archive);
            RequireWordplay(WordplayJson(package.Settings!.WordplayWorkshop!)==expected && WorkshopJson(package.Settings.StoryWorkshop!)==storyJson,"Protected recovery lost independent workshop state.");
        }
        SetDocumentPassword(null);TryDeleteRecovery();
        var rtf=Path.Combine(directory,"wordplay-body.rtf");SaveRtf(rtf);var exported=new FlowDocument();
        using(var source=File.OpenRead(rtf))new TextRange(exported.ContentStart,exported.ContentEnd).Load(source,DataFormats.Rtf);
        RequireWordplay(new TextRange(exported.ContentStart,exported.ContentEnd).Text==body && !File.ReadAllText(rtf).Contains(WordplayFavouriteSecret,StringComparison.Ordinal),
            "An RTF body export embedded private wordplay.");
        report.AppendLine("Two independent workshop records, all six axes, latest connections and edited favourite provenance survived native/protected saves and both recovery paths. Body, print and RTF remained ordinary manuscript text.");
    }

    void VerifyWordplayLifecycle(string directory,StringBuilder report)
    {
        wordplayWorkshopWindow?.Close();SetDocumentPassword(null);dirty=false;New_Click(this,new RoutedEventArgs());
        RequireWordplay(!SnapshotWordplayWorkshop().HasContent && !SnapshotStoryWorkshop().HasContent && !dirty,"New retained an earlier document's workshop data.");
        var legacy=Path.Combine(directory,"legacy-without-workshops.czd");
        using(var output=File.Create(legacy))using(var archive=new ZipArchive(output,ZipArchiveMode.Create))
        using(var text=archive.CreateEntry("document.rtf").Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(text,DataFormats.Rtf);
        SetWordplayFixture("Prepare private notes before a legacy document.");OpenPath(legacy);
        RequireWordplay(!SnapshotWordplayWorkshop().HasContent,"A legacy document inherited private wordplay.");
        SetWordplayFixture("Prepare private notes before another file.");var plain=Path.Combine(directory,"unrelated.txt");File.WriteAllText(plain,"Another manuscript.");OpenPath(plain);
        RequireWordplay(!SnapshotWordplayWorkshop().HasContent && PagedDocumentText().Contains("Another manuscript.",StringComparison.Ordinal),"Non-native Open retained another file's word pile.");
        SetWordplayFixture("Wordplay alone requires native saving.");
        RequireWordplay(!SnapshotStoryWorkshop().HasContent,"The wordplay-only save fixture accidentally relies on a story plan.");
        var expected=WordplayJson(SnapshotWordplayWorkshop());var body=PagedDocumentText();currentFile=plain;dirty=true;
        storyWorkshopNativePathOverride=()=>null;RequireWordplay(!SaveCurrent() && currentFile==plain && dirty,"Cancelling native wordplay Save changed file ownership or discarded unsaved notes.");
        var native=Path.Combine(directory,"wordplay-only.czd");storyWorkshopNativePathOverride=()=>native;
        RequireWordplay(SaveCurrent() && currentFile==native && !dirty,"Wordplay-only Save did not choose native format.");
        using(var source=File.OpenRead(native))RequireWordplay(WordplayJson(ReadCzdArchive(source).Settings!.WordplayWorkshop!)==expected,"Native Save lost wordplay-only metadata.");
        dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());var recovery=File.ReadAllBytes(recoverySettingsFile);var output=Path.Combine(directory,"explicit-body.txt");
        storyWorkshopExportConfirmationOverride=_=>false;RequireWordplay(!ExportStoryWorkshopBody(output) && !File.Exists(output),"Cancelled wordplay body export wrote a file.");
        storyWorkshopExportConfirmationOverride=_=>true;
        RequireWordplay(ExportStoryWorkshopBody(output) && File.ReadAllText(output)==body && dirty && currentFile==native &&
            WordplayJson(SnapshotWordplayWorkshop())==expected && File.ReadAllBytes(recoverySettingsFile).AsSpan().SequenceEqual(recovery),"Body export lost the current path, private wordplay, dirty state or recovery.");
        using(var password=WorkshopPassword())SetDocumentPassword(password.Copy());var blocked=Path.Combine(directory,"protected-must-not-export.txt");
        RequireWordplay(!ExportStoryWorkshopBody(blocked) && !File.Exists(blocked),"A protected wordplay document acquired a plain export path.");SetDocumentPassword(null);
        storyWorkshopNativePathOverride=null;storyWorkshopExportConfirmationOverride=null;
        Editor.IsReadOnly=true;var change=SnapshotWordplayWorkshop();change.Results.Clear();change.Target="A forbidden edit";
        RejectWordplay(()=>CommitWordplayWorkshop(change,"Readonly edit"),"Read-only wordplay metadata was editable.");Editor.IsReadOnly=false;
        RequireWordplay(WordplayJson(SnapshotWordplayWorkshop())==expected,"Read-only rejection changed saved notes.");
        using var malformed=new MemoryStream();using(var archive=new ZipArchive(malformed,ZipArchiveMode.Create,leaveOpen:true))
        {
            using(var text=archive.CreateEntry("document.rtf").Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(text,DataFormats.Rtf);
            using var metadata=new StreamWriter(archive.CreateEntry("settings.json").Open());metadata.Write("{\"WordplayWorkshop\":{\"SchemaVersion\":999}}");
        }
        malformed.Position=0;RejectWordplay(()=>ReadCzdArchive(malformed),"An unsupported incoming wordplay schema was accepted.");
        RequireWordplay(WordplayJson(SnapshotWordplayWorkshop())==expected && PagedDocumentText()==body,"Malformed incoming metadata changed the current document.");
        report.AppendLine("New, legacy and non-native files start without another document's wordplay. Wordplay alone routes Save to CZD; explicit body exports retain ownership and recovery, protected export is blocked, malformed and read-only changes are non-mutating.");
    }

    void VerifyWordplayWindow(StringBuilder report)
    {
        foreach(var view in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
        {
            wordplayWorkshopWindow?.Close();dirty=false;New_Click(this,new RoutedEventArgs());
            var selected=new Run("clock paper"){FontWeight=FontWeights.Bold,Foreground=Brushes.Navy};
            var paragraph=new Paragraph(new Run(WordplayBodySecret+" BEFORE "));paragraph.Inlines.Add(selected);paragraph.Inlines.Add(new Run(" AFTER"));
            SentenceSetDocument(new FlowDocument(paragraph),view);Editor.Selection.Select(selected.ContentStart,selected.ContentEnd);
            var story=WorkshopFixtureState();story.Id=SnapshotStoryWorkshop().Id;story.Story["Title"].Text=WordplayStorySecret;CommitStoryWorkshop(story,"Keep story planning private.");
            var storyBefore=WorkshopJson(SnapshotStoryWorkshop());var body=PagedDocumentText();dirty=false;var prompts=new List<string>();
            WordplayWorkshopService.GeneratorOverride=async(prompt,token)=>{prompts.Add(prompt);await Task.Delay(70,token);return WordplayFixtureResponse();};
            if(view==EditorViewMode.Continuous)
            {
                var review=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedReviewMenu");
                review.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedWordplayWorkshop").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                var menu=new ContextMenu{PlacementTarget=Editor};AddMacroSubmenu(menu);menu.IsOpen=true;PumpInput();
                menu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedWordplayWorkshop").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
            }
            PumpInput();var dialog=wordplayWorkshopWindow??throw new InvalidOperationException("Wordplay Workshop did not open.");
            RequireWordplay(dialog.IsVisible && dialog.Owner==this && !dirty && !SnapshotWordplayWorkshop().HasContent && prompts.Count==0 &&
                WordplayControl<TextBox>(dialog,"WordplaySeed").Text=="clock paper","Opening did not preserve local selected-seed prefill without AI or document changes.");
            WordplayControl<TextBox>(dialog,"WordplaySeed").Text=WordplaySeedFixture;ChooseWordplayAxes(dialog);
            WordplayControl<TextBox>(dialog,"WordplayTarget").Text="A gentle thought about postponed promises.";
            RequireWordplay(prompts.Count==0 && PagedDocumentText()==body && WorkshopJson(SnapshotStoryWorkshop())==storyBefore,"Local creative choices read/sent prose or changed the manuscript/story plan.");
            WordplayControl<Button>(dialog,"WordplayExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var responsive=false;Dispatcher.BeginInvoke(new Action(()=>responsive=true),System.Windows.Threading.DispatcherPriority.Input);WaitSentence(()=>responsive,"Exploration blocked dispatcher input.");
            WaitSentence(()=>SnapshotWordplayWorkshop().Results.Count==6 && !WordplayControl<Button>(dialog,"WordplayCancel").IsEnabled,"Completed exploration did not save six association directions.");
            RequireWordplay(prompts.Count==1 && prompts[0].Contains("WORDPLAY_PRIVATE_SEED_53",StringComparison.Ordinal) &&
                !prompts[0].Contains(WordplayBodySecret,StringComparison.Ordinal) && !prompts[0].Contains(WordplayStorySecret,StringComparison.Ordinal) &&
                !prompts[0].Contains(WorkshopPrivateMarker,StringComparison.Ordinal),"Explore sent unrelated manuscript or story metadata, or made extra requests.");
            var connections=WordplayControl<ListBox>(dialog,"WordplayConnections");connections.SelectedIndex=0;
            var examples=WordplayControl<ListBox>(dialog,"WordplayExamples");examples.SelectedIndex=0;
            var chosen=(WordplayConnection)connections.SelectedItem;var edit=WordplayControl<TextBox>(dialog,"WordplayEdit");
            var beforeInvalidFavourite=WordplayJson(SnapshotWordplayWorkshop());edit.Text="An embedded \uFFFC object is not a phrase.";
            WordplayControl<Button>(dialog,"WordplayKeep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireWordplay(WordplayJson(SnapshotWordplayWorkshop())==beforeInvalidFavourite,
                "Rejecting invalid favourite wording changed saved exploration or favourites.");
            const string kept="The clock folded a promise into its paper sea.";edit.Text=kept;
            WordplayControl<Button>(dialog,"WordplayCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireWordplay(Clipboard.GetText()==kept && PagedDocumentText()==body,"Copy changed the document or ignored edited wording.");
            WordplayControl<Button>(dialog,"WordplayKeep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var favourite=SnapshotWordplayWorkshop().Favourites.Single();
            RequireWordplay(favourite.Text==kept && favourite.Anchors.SequenceEqual(chosen.Anchors) && favourite.Bridge==chosen.Bridge && favourite.Options.Form=="Song",
                "Keeping a phrase lost its edited wording, source connection or creative axes.");
            if(view==EditorViewMode.Continuous)CaptureWordplayWindow(dialog);
            ResetSentenceUndo();WordplayControl<Button>(dialog,"WordplayApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
            RequireWordplay(PagedDocumentText()==body.Replace("clock paper",kept,StringComparison.Ordinal) &&
                Equals(SentenceFindText(paragraph,kept).GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold) &&
                SentenceFindText(paragraph,kept).GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush ink && ink.Color==Colors.Navy,
                "Explicit Apply changed the wrong passage or lost its opening character format.");
            Editor.Undo();PumpInput();RequireWordplay(PagedDocumentText()==body,"One Undo did not restore the original selected prose.");
            Editor.Redo();PumpInput();RequireWordplay(PagedDocumentText()==body.Replace("clock paper",kept,StringComparison.Ordinal),"One Redo did not restore the chosen phrase.");
            var beforeFavourite=JsonSerializer.Serialize(SnapshotWordplayWorkshop().Favourites.Single());
            WordplayControl<TextBox>(dialog,"WordplaySeed").Text="another source pile";
            WordplayControl<ComboBox>(dialog,"WordplayForm").SelectedItem="Novel";
            RequireWordplay(SnapshotWordplayWorkshop().Results.Count==0 && JsonSerializer.Serialize(SnapshotWordplayWorkshop().Favourites.Single())==beforeFavourite && prompts.Count==1,
                "Changing the word pile or form kept stale results, rewrote favourite provenance, or called AI.");
            var favourites=WordplayControl<ListBox>(dialog,"WordplayFavourites");favourites.SelectedItem=favourites.Items.OfType<WordplayFavourite>().Single();
            WordplayControl<Button>(dialog,"WordplayUseFavourite").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireWordplay(edit.Text==kept,"A saved favourite could not return to the phrase editor after the source changed.");
            WordplayControl<Button>(dialog,"WordplayCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));RequireWordplay(Clipboard.GetText()==kept,"A reused favourite could not be copied.");
            edit.Text="The paper sea kept the clock's promise.";WordplayControl<Button>(dialog,"WordplayKeep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var updated=SnapshotWordplayWorkshop().Favourites.Single();
            RequireWordplay(updated.Id==favourite.Id && updated.Text==edit.Text && updated.Options.Form=="Song" && updated.Anchors.SequenceEqual(favourite.Anchors),
                "Updating a kept phrase changed its identity or original provenance.");
            WordplayControl<Button>(dialog,"WordplayRemoveFavourite").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireWordplay(SnapshotWordplayWorkshop().Favourites.Count==0,"Removing a favourite left it in the document metadata.");
            WordplayControl<Button>(dialog,"WordplayUndoRemove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireWordplay(SnapshotWordplayWorkshop().Favourites.Single().Id==updated.Id,"Undo remove failed to restore the original favourite identity.");
            dialog.Close();PumpInput();
        }
        report.AppendLine("Review and context-menu windows in both views opened locally, explored once, displayed traceable examples, copied and kept edited phrases, and applied only explicitly with rich Undo/Redo. Source/form changes cleared obsolete results without changing kept provenance or the separate story plan.");
    }

    void VerifyWordplayStaleWindow(StringBuilder report)
    {
        foreach(var change in new[]{"cancel","seed","option","target","metadata","opt-in","new document","close"})
        {
            wordplayWorkshopWindow?.Close();dirty=false;New_Click(this,new RoutedEventArgs());
            SentenceSetDocument(new FlowDocument(new Paragraph(new Run(WordplayBodySecret))),EditorViewMode.Continuous);SetWordplayFixture("Prepare a stable word pile.");
            var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);var started=false;var returned=false;CancellationToken requested=default;
            WordplayWorkshopService.GeneratorOverride=async(_,token)=>{requested=token;started=true;var reply=await response.Task;returned=true;return reply;};
            ShowWordplayWorkshop();var dialog=wordplayWorkshopWindow!;WordplayControl<Button>(dialog,"WordplayExplore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>started,"The delayed exploration did not start.");
            switch(change)
            {
                case "cancel":WordplayControl<Button>(dialog,"WordplayCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;
                case "seed":WordplayControl<TextBox>(dialog,"WordplaySeed").Text="A new word pile";break;
                case "option":WordplayControl<ComboBox>(dialog,"WordplayStyle").SelectedItem="Surreal";break;
                case "target":WordplayControl<TextBox>(dialog,"WordplayTarget").Text="A new target";break;
                case "metadata":var local=SnapshotWordplayWorkshop();local.Results.Clear();local.Target="External local change wins.";CommitWordplayWorkshop(local,"New local intention.");break;
                case "opt-in":languageSettings.AllowAiLanguageServices=false;dialog.Activate();PumpInput();break;
                case "new document":dirty=false;New_Click(this,new RoutedEventArgs());break;
                case "close":dialog.Close();break;
            }
            var expected=WordplayJson(SnapshotWordplayWorkshop());var body=PagedDocumentText();
            if(change!="opt-in")RequireWordplay(requested.IsCancellationRequested,"Changing "+change+" did not cancel the exploration request.");
            response.SetResult(WordplayFixtureResponse());WaitSentence(()=>returned,"The non-cooperative fixture did not return.");PumpInput();
            RequireWordplay(WordplayJson(SnapshotWordplayWorkshop())==expected && PagedDocumentText()==body,"A stale exploration overwrote current notes or prose after "+change+".");
            if(change is "new document" or "close")RequireWordplay(wordplayWorkshopWindow==null,"A document switch retained the old wordplay window.");else dialog.Close();
            languageSettings.AllowAiLanguageServices=true;PumpInput();
        }
        dirty=false;New_Click(this,new RoutedEventArgs());SetWordplayFixture("Prepare a reusable result before editing the body.");
        var paragraph=new Paragraph(new Run("Keep this document passage unchanged."));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);ShowWordplayWorkshop();var preview=wordplayWorkshopWindow!;
        var connections=WordplayControl<ListBox>(preview,"WordplayConnections");connections.SelectedIndex=0;WordplayControl<ListBox>(preview,"WordplayExamples").SelectedIndex=0;
        new TextRange(paragraph.ContentEnd,paragraph.ContentEnd).Text=" BODY EDIT";PumpInput();
        RequireWordplay(!WordplayControl<Button>(preview,"WordplayApply").IsEnabled && SnapshotWordplayWorkshop().Results.Count==6,
            "A manuscript edit retained an unsafe insertion target or discarded independent wordplay results.");
        var seedBeforeRetarget=SnapshotWordplayWorkshop().SeedText;var bodyBeforeRetarget=PagedDocumentText();
        Editor.CaretPosition=paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        WordplayControl<Button>(preview,"WordplayUseDestination").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var inserted=" "+WordplayControl<TextBox>(preview,"WordplayEdit").Text;WordplayControl<TextBox>(preview,"WordplayEdit").Text=inserted;
        RequireWordplay(WordplayControl<Button>(preview,"WordplayApply").IsEnabled && SnapshotWordplayWorkshop().SeedText==seedBeforeRetarget,
            "Capturing a fresh caret destination changed the word pile or left a current phrase unusable.");
        ResetSentenceUndo();WordplayControl<Button>(preview,"WordplayApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireWordplay(PagedDocumentText()==bodyBeforeRetarget.Replace(" BODY EDIT"," BODY EDIT"+inserted,StringComparison.Ordinal),
            "Fresh-destination Apply did not insert at the captured caret.");
        Editor.Undo();PumpInput();RequireWordplay(PagedDocumentText()==bodyBeforeRetarget,"One Undo did not restore explicit caret insertion.");preview.Close();
        languageSettings.AllowAiLanguageServices=false;ShowWordplayWorkshop();preview=wordplayWorkshopWindow!;
        RequireWordplay(!WordplayControl<Button>(preview,"WordplayExplore").IsEnabled,"AI-language-services opt-in did not gate exploration.");preview.Close();languageSettings.AllowAiLanguageServices=true;
        Editor.IsReadOnly=true;ShowWordplayWorkshop();preview=wordplayWorkshopWindow!;
        RequireWordplay(!WordplayControl<Button>(preview,"WordplayExplore").IsEnabled && !WordplayControl<Button>(preview,"WordplayKeep").IsEnabled,
            "A read-only document exposed metadata generation or editing.");preview.Close();Editor.IsReadOnly=false;
        report.AppendLine("Cancel, source/options/target edits, external metadata, AI opt-out, New and Close invalidated deliberately late replies. Body edits invalidated only insertion destinations, retaining saved exploration; read-only and explicit AI gates held.");
    }

    static void ChooseWordplayAxes(Window dialog)
    {
        foreach(var (name,value) in new[]{("WordplayForm","Song"),("WordplayStyle","Philosophical"),("WordplayComicFlavour","Deadpan"),
            ("WordplayDevice","Pun"),("WordplaySubject","Observational"),("WordplaySituation","Farce")})
            WordplayControl<ComboBox>(dialog,name).SelectedItem=value;
    }

    static T WordplayControl<T>(Window window,string name) where T:FrameworkElement
        =>window.FindName(name) as T??throw new InvalidOperationException("Wordplay Workshop is missing control "+name+".");

    static void CaptureWordplayWindow(Window dialog)
    {
        dialog.UpdateLayout();var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(dialog.ActualWidth)),Math.Max(1,(int)Math.Ceiling(dialog.ActualHeight)),96,96,PixelFormats.Pbgra32);
        image.Render(dialog);image.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var output=File.Create(Path.Combine(Path.GetTempPath(),"CtrlZed53WordplayWorkshop.png"));encoder.Save(output);
    }

    static void RequireWordplay(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void RejectWordplay(Action action,string message)
    {
        try{action();}catch(Exception error) when(error is InvalidDataException or JsonException or ArgumentException or InvalidOperationException or FormatException){return;}
        throw new InvalidOperationException(message);
    }
}
