using System.IO;
using System.IO.Compression;
using System.Security;
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
    // This gate uses only a deterministic in-process provider. Story content is
    // a synthetic fixture, not a claim about a live model's literary judgement.
    internal static int RunStoryWorkshopSelfTest()
    {
        var report=new StringBuilder("Beta 52 Story Workshop deterministic regression\r\n");
        var failures=new List<Exception>();
        var appRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var temporary=Path.Combine(Path.GetTempPath(),"CtrlZed52Workshop_"+Guid.NewGuid().ToString("N"));
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var directories=Directory.Exists(appRoot)?Directory.GetDirectories(appRoot,"*",SearchOption.AllDirectories)
            .Prepend(appRoot).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transport=StoryWorkshopService.GeneratorOverride;
        var shutdown=Application.Current.ShutdownMode;
        MainWindow? window=null;
        IDataObject? clipboard=null;
        var captured=false;var clipboardCaptured=false;
        void Phase(string name,Action action)
        {
            try{action();report.AppendLine("PASS: "+name+".");}
            catch(Exception error){failures.Add(error);report.AppendLine("FAIL: "+name+": "+error);}
        }
        try
        {
            if(Directory.Exists(appRoot))
                foreach(var path in Directory.GetFiles(appRoot,"*",SearchOption.AllDirectories))originals[path]=File.ReadAllBytes(path);
            captured=true;clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(appRoot);Directory.CreateDirectory(temporary);
            foreach(var path in Directory.GetFiles(appRoot,"Recovery*"))File.Delete(path);
            StoryWorkshopService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected implicit Story Workshop request.");
            Phase("bounded model and atomic conversational patch contract",()=>VerifyWorkshopContract(report));
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Normal,Width=1080,Height=780};
            window.Show();window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
            window.languageSettings.SpellingEnabled=false;
            window.languageSettings.ContextualSpellingSuggestions=false;
            window.languageSettings.GrammarAndStyleSuggestions=false;
            window.languageSettings.PoetryRhymeSuggestions=false;
            window.languageSettings.AllowAiLanguageServices=true;
            SpellCheck.SetIsEnabled(window.Editor,false);
            Phase("native, protected native and both recovery metadata round trips",()=>window.VerifyWorkshopPersistence(temporary,report));
            Phase("old files, New and non-native Open do not inherit another story",()=>window.VerifyWorkshopLifecycle(temporary,report));
            Phase("real guided conversation and editable character/storyboard data",()=>window.VerifyWorkshopWindow(report));
            Phase("late requests, local edits, settings and document-switch guards",()=>window.VerifyWorkshopStaleWindow(report));
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("review and editor cleanup",()=>
            {
                window.storyWorkshopWindow?.Close();window.characterVoiceWindow?.Close();window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.SetDocumentPassword(null);window.Close();
            });
            StoryWorkshopService.GeneratorOverride=transport;Application.Current.ShutdownMode=shutdown;
            if(captured)Phase("exact original application-data restoration",()=>
            {
                if(Directory.Exists(appRoot))
                    foreach(var path in Directory.GetFiles(appRoot,"*",SearchOption.AllDirectories))
                        if(!originals.ContainsKey(path))File.Delete(path);
                foreach(var pair in originals)
                {Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);File.WriteAllBytes(pair.Key,pair.Value);}
                if(Directory.Exists(appRoot))
                    foreach(var path in Directory.GetDirectories(appRoot,"*",SearchOption.AllDirectories).Append(appRoot).OrderByDescending(x=>x.Length))
                        if(!directories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originals)
                    RequireWorkshop(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An original preference file was not restored exactly.");
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>
            {if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();});
            try{Directory.Delete(temporary,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            report.AppendLine("Transport: deterministic fixture only; live planning quality remains a user acceptance check.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed52StoryWorkshop.txt"),report.ToString());
            if(failures.Count>0)File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),new AggregateException(failures).ToString());
        }
        return failures.Count==0?0:132;
    }

    static readonly string WorkshopFixtureCharacterId="11111111111111111111111111111111";
    static readonly string WorkshopFixtureBeatId="22222222222222222222222222222222";
    static readonly string WorkshopFixtureClueId="33333333333333333333333333333333";
    const string WorkshopPrivateMarker="WORKSHOP_PRIVATE_PLAN_52";
    const string WorkshopBodyMarker="DOCUMENT_BODY_NOT_FOR_WORKSHOP_52";

    static StoryWorkshopState WorkshopFixtureState()
    {
        var state=new StoryWorkshopState{WritingMode=WritingMode.Novel};
        foreach(var field in StoryWorkshopSchema.StoryFields)
            state.Story[field.Key]=new StoryWorkshopValue{Text=WorkshopPrivateMarker+" story "+field.Key,Status=StoryWorkshopValueStatus.Confirmed};
        var character=new StoryWorkshopEntry{Id=WorkshopFixtureCharacterId,Kind=StoryWorkshopEntryKind.Character,Name="Jock Stone",Status=StoryWorkshopValueStatus.Confirmed};
        foreach(var field in StoryWorkshopSchema.CharacterFields)
            character.Fields[field.Key]=new StoryWorkshopValue{Text=WorkshopPrivateMarker+" character "+field.Key,Status=StoryWorkshopValueStatus.Confirmed};
        var beat=new StoryWorkshopEntry{Id=WorkshopFixtureBeatId,Kind=StoryWorkshopEntryKind.Beat,Name="The misleading discovery",Status=StoryWorkshopValueStatus.Proposed,
            CharacterIds=new List<string>{character.Id}};
        foreach(var field in StoryWorkshopSchema.BeatFields)
            beat.Fields[field.Key]=new StoryWorkshopValue{Text=WorkshopPrivateMarker+" beat "+field.Key,Status=StoryWorkshopValueStatus.Proposed};
        var clue=new StoryWorkshopEntry{Id=WorkshopFixtureClueId,Kind=StoryWorkshopEntryKind.Clue,Name="An ambiguous photograph",Status=StoryWorkshopValueStatus.Confirmed,
            CharacterIds=new List<string>{character.Id}};
        foreach(var field in StoryWorkshopSchema.ClueFields)
            clue.Fields[field.Key]=new StoryWorkshopValue{Text=WorkshopPrivateMarker+" clue "+field.Key,Status=StoryWorkshopValueStatus.Confirmed};
        state.Characters.Add(character);state.Beats.Add(beat);state.Clues.Add(clue);
        state.OpenQuestions.Add("Why does the same place recur in his dreams?");
        state.Contradictions.Add("Jock is established, but a later note calls him John; clarify the identity.");
        state.Validate();return state;
    }

    static string WorkshopJson(StoryWorkshopState state)=>JsonSerializer.Serialize(state);

    static object WorkshopChange(string kind,string id,string name,string field,string value,string status="proposed",string evidence="",string[]? characterIds=null)
        =>new{kind,id,name,field,value,status,evidence,characterIds=characterIds??Array.Empty<string>()};

    static string WorkshopResponse(object[]? changes=null,string reply="I have added a suggestion for review. What does the protagonist stand to lose?",
        string[]? questions=null,string[]? contradictions=null)
        =>JsonSerializer.Serialize(new{reply,changes=changes??Array.Empty<object>(),questions=questions??new[]{"What does the protagonist stand to lose?"},
            contradictions=contradictions??Array.Empty<string>(),resolvedQuestions=Array.Empty<object>(),resolvedContradictions=Array.Empty<object>()});

    static void VerifyWorkshopContract(StringBuilder report)
    {
        var empty=new StoryWorkshopState();empty.Validate();
        RequireWorkshop(!empty.HasContent,"Opening an empty Novel planning state already counts as authored content.");
        var complete=WorkshopFixtureState();var clone=complete.DeepClone();
        RequireWorkshop(WorkshopJson(clone)==WorkshopJson(complete),"Deep cloning lost a story, character, beat, clue or review status.");
        clone.Characters[0].Name="Changed only in clone";
        RequireWorkshop(complete.Characters[0].Name=="Jock Stone","A planning clone shares mutable character state with its source.");
        var invalid=complete.DeepClone();invalid.SchemaVersion=999;
        RejectWorkshop(()=>invalid.Validate(),"An unsupported planning schema was accepted.");
        invalid=complete.DeepClone();invalid.Characters.Add(invalid.Characters[0]);
        RejectWorkshop(()=>invalid.Validate(),"Duplicate character identities were accepted.");
        invalid=complete.DeepClone();invalid.Story["unknown-operating-command"]=new StoryWorkshopValue{Text="Do not execute this"};
        RejectWorkshop(()=>invalid.Validate(),"An unknown planning field was accepted.");
        invalid=complete.DeepClone();invalid.Characters[0].CharacterIds.Add("not-a-real-character");
        RejectWorkshop(()=>invalid.Validate(),"A dangling character association was accepted.");
        invalid=complete.DeepClone();invalid.OpenQuestions.Add("One question?\nA different question?");
        RejectWorkshop(()=>invalid.Validate(),"A multi-line open-question item could not round-trip through the one-item-per-line editor.");

        var name="Role";
        var message="The private detective is Jock Stone. He wants to find the missing child.";
        var fresh=new StoryWorkshopState();
        var response=WorkshopResponse(new[]{
            WorkshopChange("character",WorkshopFixtureCharacterId,"Jock Stone",name,"private detective","confirmed","The private detective is Jock Stone."),
            WorkshopChange("story","story","",StoryWorkshopSchema.StoryFields.First().Key,"A story about loyalty under pressure.","confirmed","The private detective is Jock Stone.")});
        var original=WorkshopJson(fresh);
        var result=StoryWorkshopService.ParseResponse(fresh,message,response);
        RequireWorkshop(WorkshopJson(fresh)==original,"Parsing a planning proposal mutated its input state.");
        RequireWorkshop(result.NextState.Characters.Single().Name=="Jock Stone" &&
            result.NextState.Characters.Single().Fields[name].Status==StoryWorkshopValueStatus.Confirmed,
            "An exact author-supported fact was not retained as confirmed.");
        RequireWorkshop(result.NextState.Story.Values.Single().Status==StoryWorkshopValueStatus.Proposed,
            "An inference with unrelated evidence was presented as an author-confirmed decision.");
        RequireWorkshop(result.NextState.Conversation.Count==2 && result.NextState.Conversation[0].Text==message &&
            result.NextState.Conversation[1].Text==result.Reply,"A successful planning turn did not retain author input and assistant reply together.");

        var established=result.NextState.DeepClone();
        const string establishedFact="Established fact\nSecond line of the same decision.";
        established.Characters[0].Fields[name]=new StoryWorkshopValue{Text=establishedFact,Status=StoryWorkshopValueStatus.Confirmed};
        original=WorkshopJson(established);
        result=StoryWorkshopService.ParseResponse(established,"The fact might be different.",WorkshopResponse(new[]{
            WorkshopChange("character",WorkshopFixtureCharacterId,"Jock Stone",name,"Conflicting suggestion")}));
        RequireWorkshop(result.NextState.Characters[0].Fields[name].Text==establishedFact && result.DeferredChangeCount>0 &&
            result.NextState.Contradictions.Count+result.NextState.OpenQuestions.Count>0 &&
            result.NextState.Contradictions.All(item=>item.IndexOfAny(new[]{'\r','\n'})<0),
            "A model suggestion silently overwrote an established author fact.");
        RequireWorkshop(WorkshopJson(established)==original,"A deferred conflict changed its input state.");
        result=StoryWorkshopService.ParseResponse(established,"John sees the girl again.",WorkshopResponse(new[]{
            WorkshopChange("character",WorkshopFixtureCharacterId,"John Stone",name,"A different name")},
            "You previously called the detective Jock. Is John the same person?",new[]{"Is John the same person as Jock?"}));
        RequireWorkshop(result.NextState.Characters.Single().Name=="Jock Stone" && result.DeferredChangeCount>0,
            "A Jock/John contradiction silently renamed or merged an established character.");

        foreach(var wrapper in new[]{"```json\n"+response+"\n```","\uFEFF \r\n```JSON\n"+response+"\n```\r\n","```\n"+response+"\n```"})
        {
            var wrapped=StoryWorkshopService.ParseResponse(fresh,message,wrapper);
            RequireWorkshop(wrapped.NextState.Characters.Single().Name=="Jock Stone" && wrapped.NextState.Conversation.Count==2,
                "A complete enclosing JSON fence changed or rejected a valid planning response.");
        }
        foreach(var malformed in new[]{"{}","Here is the result:\n"+response,"```json\n"+response+"\n``` trailing prose","```json\n"+response,response+" trailing",WorkshopResponse(questions:new[]{"First question?\nA second question?"}),response.Replace("\"reply\":","\"extra\":true,\"reply\":",StringComparison.Ordinal),
            response.Replace("\"kind\":\"character\"","\"kind\":\"execute\"",StringComparison.Ordinal),
            response.Replace("\"field\":\""+name+"\"","\"field\":\"unknown-field\"",StringComparison.Ordinal)})
            RejectWorkshop(()=>StoryWorkshopService.ParseResponse(fresh,message,malformed),"A malformed or unknown-operation planning response was accepted.");
        var calls=0;var prompts=new List<string>();
        StoryWorkshopService.GeneratorOverride=(prompt,token)=>
        {calls++;token.ThrowIfCancellationRequested();prompts.Add(prompt);return Task.FromResult(WorkshopResponse());};
        var operation=StoryWorkshopService.GenerateAsync(complete,"AUTHOR_MESSAGE_MARKER_52");AwaitSentenceTask(operation);
        RequireWorkshop(calls==1 && prompts.Single().Contains(WorkshopPrivateMarker,StringComparison.Ordinal) &&
            prompts.Single().Contains("AUTHOR_MESSAGE_MARKER_52",StringComparison.Ordinal) && !prompts.Single().Contains(WorkshopBodyMarker,StringComparison.Ordinal),
            "The explicit planning request omitted planning state or read unrelated body text.");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{AwaitSentenceTask(StoryWorkshopService.GenerateAsync(complete,"Do not send.",cancellation.Token));throw new InvalidOperationException("Pre-cancelled planning returned a result.");}
            catch(OperationCanceledException){}
            RequireWorkshop(calls==1,"A pre-cancelled planning request reached the provider.");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var late=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            StoryWorkshopService.GeneratorOverride=(_,__)=>late.Task;
            var pending=StoryWorkshopService.GenerateAsync(complete,"A delayed request.",cancellation.Token);
            cancellation.Cancel();late.SetResult(WorkshopResponse());
            try{AwaitSentenceTask(pending);throw new InvalidOperationException("A late cancelled planning response remained usable.");}
            catch(OperationCanceledException){}
        }
        report.AppendLine("All descriptor fields clone and validate; strict bounded proposals retain author facts, label inference, defer established conflicts and Jock/John ambiguity, commit both conversation turns atomically, and reject pre/post-cancelled transport.");
    }

    void VerifyWorkshopPersistence(string directory,StringBuilder report)
    {
        storyWorkshopWindow?.Close();SetDocumentPassword(null);
        var paragraph=new Paragraph(new Run("The manuscript body remains ordinary prose.")){Margin=new Thickness(4,6,8,10)};
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        var body=PagedDocumentText();var fixture=WorkshopFixtureState();fixture.Id=SnapshotStoryWorkshop().Id;dirty=false;ResetSentenceUndo();
        CommitStoryWorkshop(fixture,"Planning fixture saved to the document.");
        RequireWorkshop(dirty && !Editor.CanUndo,"Metadata editing was not dirty, or acquired an unrelated native body Undo entry.");
        var expected=WorkshopJson(SnapshotStoryWorkshop());
        var printed=CloneDocument(forPrint:true);
        RequireWorkshop(PagedDocumentText()==body && !new TextRange(printed.ContentStart,printed.ContentEnd).Text.Contains(WorkshopPrivateMarker,StringComparison.Ordinal),
            "Planning metadata entered the manuscript or print body.");
        using(var native=new MemoryStream())
        {
            WriteCzdArchive(native);native.Position=0;var package=ReadCzdArchive(native);
            RequireWorkshop(package.Settings?.StoryWorkshop!=null && WorkshopJson(package.Settings.StoryWorkshop)==expected &&
                new TextRange(package.Document.ContentStart,package.Document.ContentEnd).Text==body,
                "Native CZD did not retain every structured planning field, identity, link and status separately from its body.");
            using var password=WorkshopPassword();using var encrypted=new MemoryStream();
            CzdProtection.Encrypt(encrypted,native.ToArray(),password);encrypted.Position=0;
            var decoded=CzdProtection.Decrypt(encrypted,password);using var protectedArchive=new MemoryStream(decoded);
            var protectedPackage=ReadCzdArchive(protectedArchive);
            RequireWorkshop(protectedPackage.Settings?.StoryWorkshop!=null && WorkshopJson(protectedPackage.Settings.StoryWorkshop)==expected,
                "Password-protected native data lost planning metadata.");
        }
        var rtf=Path.Combine(directory,"body-only.rtf");SaveRtf(rtf);
        var exported=new FlowDocument();using(var input=File.OpenRead(rtf))new TextRange(exported.ContentStart,exported.ContentEnd).Load(input,DataFormats.Rtf);
        RequireWorkshop(new TextRange(exported.ContentStart,exported.ContentEnd).Text==body && !File.ReadAllText(rtf).Contains(WorkshopPrivateMarker,StringComparison.Ordinal),
            "An ordinary body export unexpectedly embedded private story planning.");

        dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());
        RequireWorkshop(File.Exists(recoveryFile) && File.Exists(recoverySettingsFile),"Plain recovery did not write both document and metadata.");
        var recovered=JsonSerializer.Deserialize<CzdSettings>(File.ReadAllText(recoverySettingsFile))??throw new InvalidOperationException("Recovery settings could not be read.");
        RequireWorkshop(recovered.StoryWorkshop!=null && WorkshopJson(recovered.StoryWorkshop)==expected,"Plain recovery lost planning data.");
        var recoveredBody=new FlowDocument();using(var source=File.OpenRead(recoveryFile))new TextRange(recoveredBody.ContentStart,recoveredBody.ContentEnd).Load(source,DataFormats.Rtf);
        Editor.Document=recoveredBody;ApplyRecoveredSettings(recovered);
        RequireWorkshop(WorkshopJson(SnapshotStoryWorkshop())==expected && PagedDocumentText()==body,"Recovery restoration did not restore the same complete story.");

        DeletePlainRecoveryFiles();using(var password=WorkshopPassword())SetDocumentPassword(password.Copy());
        dirty=true;AwaitPagedTask(WriteRecoverySnapshotAsync());
        RequireWorkshop(File.Exists(ProtectedRecoveryFile),"Protected recovery was not written.");
        using(var input=File.OpenRead(ProtectedRecoveryFile))
        using(var password=WorkshopPassword())
        {
            var decoded=CzdProtection.Decrypt(input,password);using var archive=new MemoryStream(decoded);
            var package=ReadCzdArchive(archive);
            RequireWorkshop(package.Settings?.StoryWorkshop!=null && WorkshopJson(package.Settings.StoryWorkshop)==expected,
                "Protected recovery lost planning data or its review statuses.");
        }
        SetDocumentPassword(null);TryDeleteRecovery();
        currentFile=Path.Combine(directory,"working-story.czd");dirty=true;
        AwaitPagedTask(WriteRecoverySnapshotAsync());
        var recoveryBefore=File.ReadAllBytes(recoverySettingsFile);var workingPath=currentFile;
        var bodyExport=Path.Combine(directory,"explicit-body.txt");
        storyWorkshopExportConfirmationOverride=_=>false;
        RequireWorkshop(!ExportStoryWorkshopBody(bodyExport) && !File.Exists(bodyExport) && dirty && currentFile==workingPath,
            "Cancelling a body export changed the working document or wrote a file.");
        storyWorkshopExportConfirmationOverride=_=>true;
        RequireWorkshop(ExportStoryWorkshopBody(bodyExport) && File.ReadAllText(bodyExport)==body && dirty && currentFile==workingPath &&
            WorkshopJson(SnapshotStoryWorkshop())==expected && File.ReadAllBytes(recoverySettingsFile).AsSpan().SequenceEqual(recoveryBefore),
            "Explicit body export lost working ownership, dirty planning or its recovery snapshot.");
        var blockedExport=Path.Combine(directory,"protected-body-must-not-leak.txt");
        using(var password=WorkshopPassword())SetDocumentPassword(password.Copy());
        RequireWorkshop(!ExportStoryWorkshopBody(blockedExport) && !File.Exists(blockedExport) && dirty && currentFile==workingPath &&
            WorkshopJson(SnapshotStoryWorkshop())==expected,"A protected working document acquired an unprotected body-export path.");
        SetDocumentPassword(null);
        currentFile=bodyExport;storyWorkshopNativePathOverride=()=>null;
        RequireWorkshop(!SaveCurrent() && currentFile==bodyExport && dirty,"Cancelling native planning Save discarded metadata or changed its file ownership.");
        var fullSave=Path.Combine(directory,"complete-story.czd");storyWorkshopNativePathOverride=()=>fullSave;
        RequireWorkshop(SaveCurrent() && currentFile==fullSave && !dirty && File.Exists(fullSave),"Ordinary Save did not route planning into the native document.");
        using(var saved=File.OpenRead(fullSave))
            RequireWorkshop(WorkshopJson(ReadCzdArchive(saved).Settings!.StoryWorkshop!)==expected,"Native planning Save lost metadata after a body export.");
        storyWorkshopNativePathOverride=null;storyWorkshopExportConfirmationOverride=null;
        report.AppendLine("All story, characterisation, arc, beat, clue, link, open-question and contradiction fields survived native/protected CZD and both recovery paths. Body/print/RTF stayed free of planning metadata; metadata edits were dirty without body Undo.");
    }

    void CommitWorkshopFixture(string reason)
    {
        var fixture=WorkshopFixtureState();fixture.Id=SnapshotStoryWorkshop().Id;CommitStoryWorkshop(fixture,reason);
    }

    static SecureString WorkshopPassword()
    {
        var password=new SecureString();foreach(var ch in "Workshop fixture password 52")password.AppendChar(ch);password.MakeReadOnly();return password;
    }

    void VerifyWorkshopLifecycle(string directory,StringBuilder report)
    {
        storyWorkshopWindow?.Close();SetDocumentPassword(null);
        CommitWorkshopFixture("Set a story before switching documents.");
        dirty=false;New_Click(this,new RoutedEventArgs());
        RequireWorkshop(!SnapshotStoryWorkshop().HasContent && !dirty,"New document inherited the previous story or began dirty.");
        var legacy=Path.Combine(directory,"legacy.czd");
        using(var output=File.Create(legacy))
        using(var archive=new ZipArchive(output,ZipArchiveMode.Create))
        using(var legacyBodyStream=archive.CreateEntry("document.rtf").Open())
            new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(legacyBodyStream,DataFormats.Rtf);
        CommitWorkshopFixture("Set another story before an old document.");
        OpenPath(legacy);
        RequireWorkshop(!SnapshotStoryWorkshop().HasContent,"A legacy file without Story Workshop metadata inherited another document's plan.");
        var plain=Path.Combine(directory,"unrelated.txt");File.WriteAllText(plain,"A different manuscript.");
        CommitWorkshopFixture("Set a story before non-native open.");
        OpenPath(plain);
        RequireWorkshop(!SnapshotStoryWorkshop().HasContent && PagedDocumentText().Contains("A different manuscript.",StringComparison.Ordinal),
            "Opening an unrelated non-native file leaked the previous document's plan.");

        CommitWorkshopFixture("Keep this valid story while validating malformed input.");
        var before=WorkshopJson(SnapshotStoryWorkshop());var body=PagedDocumentText();
        using var malformed=new MemoryStream();
        using(var archive=new ZipArchive(malformed,ZipArchiveMode.Create,leaveOpen:true))
        {
            using(var text=archive.CreateEntry("document.rtf").Open())new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(text,DataFormats.Rtf);
            using var metadata=new StreamWriter(archive.CreateEntry("settings.json").Open());
            metadata.Write("{\"StoryWorkshop\":{\"SchemaVersion\":999}}");
        }
        malformed.Position=0;RejectWorkshop(()=>ReadCzdArchive(malformed),"An unsupported native planning schema was accepted.");
        RequireWorkshop(WorkshopJson(SnapshotStoryWorkshop())==before && PagedDocumentText()==body,"Rejecting malformed incoming metadata changed the active document.");
        Editor.IsReadOnly=true;var edited=SnapshotStoryWorkshop();edited.Story[StoryWorkshopSchema.StoryFields.First().Key]=new StoryWorkshopValue{Text="Cannot be committed"};
        RejectWorkshop(()=>CommitStoryWorkshop(edited,"Read-only metadata edit"),"A read-only document accepted planning edits.");
        RequireWorkshop(WorkshopJson(SnapshotStoryWorkshop())==before,"Read-only rejection changed planning metadata.");Editor.IsReadOnly=false;
        report.AppendLine("New, legacy CZD and non-native Open reset story ownership. Incoming malformed planning was rejected before active-state replacement; read-only metadata edits were blocked.");
    }

    void VerifyWorkshopWindow(StringBuilder report)
    {
        storyWorkshopWindow?.Close();dirty=false;New_Click(this,new RoutedEventArgs());
        SentenceSetDocument(new FlowDocument(new Paragraph(new Run(WorkshopBodyMarker))),EditorViewMode.Continuous);
        dirty=false;var body=PagedDocumentText();var calls=0;var prompts=new List<string>();
        const string authorMessage="I want a murder mystery about Jock Stone, a private detective.";
        var response=WorkshopResponse(new[]{
            WorkshopChange("character",WorkshopFixtureCharacterId,"Jock Stone","Role","private detective","confirmed",authorMessage),
            WorkshopChange("character",WorkshopFixtureCharacterId,"Jock Stone","Traits","Blunt with bullies, patient with frightened witnesses."),
            WorkshopChange("story","story","","Premise","murder mystery","confirmed",authorMessage)},
            "Jock is established as a private detective. The contrasting temperament is a suggestion for you to review.",
            new[]{"What makes this particular case personal to Jock?"});
        StoryWorkshopService.GeneratorOverride=async(prompt,token)=>
        {calls++;prompts.Add(prompt);await Task.Delay(80,token);return response;};
        var review=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedReviewMenu");
        review.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedStoryWorkshop")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));PumpInput();
        var dialog=storyWorkshopWindow??throw new InvalidOperationException("Review → Story Workshop did not open.");
        RequireWorkshop(dialog.IsVisible && dialog.Owner==this && calls==0 && !SnapshotStoryWorkshop().HasContent && !dirty,
            "Opening Story Workshop requested AI, created planning metadata or dirtied the document.");
        RequireWorkshop(WorkshopText(dialog).Contains("How can I help you?",StringComparison.Ordinal),"The local opening greeting was not shown.");
        var mode=WorkshopControl<ComboBox>(dialog,"WorkshopMode");
        mode.SelectedItem=mode.Items.OfType<WritingModeProfile>().Single(item=>item.Mode==WritingMode.Fiction);
        mode.SelectedItem=mode.Items.OfType<WritingModeProfile>().Single(item=>item.Mode==WritingMode.Novel);
        RequireWorkshop(calls==0,"Choosing a planning Writing Mode implicitly contacted a provider.");
        WorkshopControl<TextBox>(dialog,"WorkshopInput").Text=authorMessage;
        var send=WorkshopControl<Button>(dialog,"WorkshopSend");RequireWorkshop(send.IsEnabled,"An explicitly enabled workshop could not send an author message.");
        send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var responsive=false;Dispatcher.BeginInvoke(new Action(()=>responsive=true),System.Windows.Threading.DispatcherPriority.Input);
        WaitSentence(()=>responsive,"Story Workshop blocked dispatcher input while waiting for the provider.");
        WaitSentence(()=>SnapshotStoryWorkshop().Conversation.Count==2 && !WorkshopControl<Button>(dialog,"WorkshopCancel").IsEnabled,
            "A completed workshop turn did not commit both conversation messages and become idle.");
        var state=SnapshotStoryWorkshop();var character=state.Characters.Single();
        RequireWorkshop(calls==1 && prompts.Single().Contains(authorMessage,StringComparison.Ordinal) &&
            !prompts.Single().Contains(WorkshopBodyMarker,StringComparison.Ordinal) && state.WritingMode==WritingMode.Novel,
            "Explicit Send read manuscript prose, omitted the author message/mode, or made duplicate requests.");
        RequireWorkshop(character.Name=="Jock Stone" && character.Fields["Role"].Status==StoryWorkshopValueStatus.Confirmed &&
            character.Fields["Traits"].Status==StoryWorkshopValueStatus.Proposed && dirty && PagedDocumentText()==body,
            "The conversation did not build differentiated character notes separately from the manuscript.");

        WorkshopControl<TabControl>(dialog,"WorkshopTabs").SelectedIndex=1;
        var characters=WorkshopControl<ListBox>(dialog,"WorkshopCharacters");
        characters.SelectedItem=characters.Items.OfType<StoryWorkshopEntry>().Single(item=>item.Id==character.Id);
        SelectWorkshopField(dialog,"WorkshopCharacter",StoryWorkshopSchema.CharacterFields.Single(field=>field.Key=="Traits"));
        var confirm=WorkshopControl<Button>(dialog,"WorkshopCharacterConfirm");
        RequireWorkshop(confirm.IsEnabled,"A proposed trait note could not be confirmed by the author.");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireWorkshop(SnapshotStoryWorkshop().Characters.Single().Fields["Traits"].Status==StoryWorkshopValueStatus.Confirmed,
            "Confirm did not promote the selected proposed note into an author decision.");
        var editor=WorkshopControl<TextBox>(dialog,"WorkshopCharacterValue");
        editor.Text="Direct, loyal, and gentle with a frightened witness.";
        WorkshopControl<Button>(dialog,"WorkshopCharacterSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireWorkshop(SnapshotStoryWorkshop().Characters.Single().Fields["Traits"].Text==editor.Text &&
            SnapshotStoryWorkshop().Characters.Single().Fields["Traits"].Status==StoryWorkshopValueStatus.Confirmed && calls==1,
            "A direct character edit was not an author-confirmed local change.");
        SelectWorkshopField(dialog,"WorkshopStory",StoryWorkshopSchema.StoryFields.Single(field=>field.Key=="ClimaxTwist"));
        WorkshopControl<TextBox>(dialog,"WorkshopStoryValue").Text="The photograph reverses the investigator's initial assumption.";
        WorkshopControl<Button>(dialog,"WorkshopStorySave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireWorkshop(SnapshotStoryWorkshop().Story["ClimaxTwist"].Status==StoryWorkshopValueStatus.Confirmed && PagedDocumentText()==body,
            "The editable story plan did not retain an author-confirmed twist separately from body text.");
        WorkshopControl<Button>(dialog,"WorkshopAddBeat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var beatCharacters=WorkshopControl<ListBox>(dialog,"WorkshopBeatCharacters");
        beatCharacters.SelectedItems.Add(beatCharacters.Items.OfType<StoryWorkshopEntry>().Single(item=>item.Id==character.Id));
        WorkshopControl<Button>(dialog,"WorkshopAddClue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var clueCharacters=WorkshopControl<ListBox>(dialog,"WorkshopClueCharacters");
        clueCharacters.SelectedItems.Add(clueCharacters.Items.OfType<StoryWorkshopEntry>().Single(item=>item.Id==character.Id));
        RequireWorkshop(SnapshotStoryWorkshop().Beats.Single().CharacterIds.Contains(character.Id) &&
            SnapshotStoryWorkshop().Clues.Single().CharacterIds.Contains(character.Id),
            "Storyboard cards could not retain explicit character associations.");
        WorkshopControl<Button>(dialog,"WorkshopDeleteCharacter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireWorkshop(SnapshotStoryWorkshop().Characters.Count==0 && SnapshotStoryWorkshop().Beats.Single().CharacterIds.Count==0 &&
            SnapshotStoryWorkshop().Clues.Single().CharacterIds.Count==0,"Removing a character left dangling storyboard links.");
        WorkshopControl<Button>(dialog,"WorkshopUndo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireWorkshop(SnapshotStoryWorkshop().Characters.Single().Id==character.Id &&
            SnapshotStoryWorkshop().Beats.Single().CharacterIds.Contains(character.Id) && SnapshotStoryWorkshop().Clues.Single().CharacterIds.Contains(character.Id),
            "Undo planning edit did not restore the removed character and its storyboard associations together.");

        WorkshopControl<TabControl>(dialog,"WorkshopTabs").SelectedIndex=0;
        CaptureWorkshopWindow(dialog);dialog.Close();PumpInput();
        RequireWorkshop(storyWorkshopWindow==null && SnapshotStoryWorkshop().Characters.Single().Fields["Traits"].Text=="Direct, loyal, and gentle with a frightened witness.",
            "Closing the workshop lost committed character notes or retained its window reference.");
        var voiceSelection=CreateStoryWorkshopCharacterVoice(SnapshotStoryWorkshop(),SnapshotStoryWorkshop().Characters.Single());
        RequireWorkshop(voiceSelection.Voice.Name=="Jock Stone" && voiceSelection.Voice.Profile.Contains("Direct, loyal",StringComparison.Ordinal) &&
            !voiceSelection.Voice.Profile.Contains("photograph reverses",StringComparison.Ordinal),
            "Own-character voice preparation omitted stable behaviour or leaked a future plot revelation into the voice.");
        var voiceCalls=0;var previousVoiceTransport=CharacterVoiceService.GeneratorOverride;
        CharacterVoiceService.GeneratorOverride=(_,__)=>{voiceCalls++;throw new InvalidOperationException("Unexpected implicit own-character request.");};
        try
        {
            OpenStoryWorkshopCharacterVoice(SnapshotStoryWorkshop().Characters.Single());PumpInput();
            RequireWorkshop(characterVoiceWindow!=null && voiceCalls==0,"Opening an own-character voice requested AI or failed to open its preview.");
            var next=SnapshotStoryWorkshop();next.Characters[0].Fields["Traits"].Text+=" A local revision.";CommitStoryWorkshop(next,"Character behaviour revised.");
            RequireWorkshop(characterVoiceWindow==null,"An own-character voice preview retained stale personality notes after a planning edit.");
        }
        finally{characterVoiceWindow?.Close();CharacterVoiceService.GeneratorOverride=previousVoiceTransport;}
        report.AppendLine("The real Review menu opened a clean local greeting; mode choices stayed local. One explicit Send built confirmed author facts and proposed traits, retained its conversation, stayed responsive and sent no manuscript body. Structured character/twist edits and confirmation were local. Own-character voice preparation excluded future plot facts and closed on personality changes. Synthetic screenshot: CtrlZed52StoryWorkshop.png.");
    }

    void VerifyWorkshopStaleWindow(StringBuilder report)
    {
        foreach(var change in new[]{"cancel","character edit","mode","opt-in","new document","close"})
        {
            storyWorkshopWindow?.Close();dirty=false;New_Click(this,new RoutedEventArgs());
            SentenceSetDocument(new FlowDocument(new Paragraph(new Run(WorkshopBodyMarker))),EditorViewMode.Continuous);
            CommitWorkshopFixture("Prepare a stable existing plan.");
            var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=false;var returned=false;CancellationToken requested=default;
            StoryWorkshopService.GeneratorOverride=async(_,token)=>
            {requested=token;started=true;var reply=await response.Task;returned=true;return reply;};
            ShowStoryWorkshop();var dialog=storyWorkshopWindow!;
            WorkshopControl<TextBox>(dialog,"WorkshopInput").Text="Consider the protagonist's next difficult choice.";
            WorkshopControl<Button>(dialog,"WorkshopSend").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>started,"The deliberately delayed planning request did not start.");
            switch(change)
            {
                case "cancel":WorkshopControl<Button>(dialog,"WorkshopCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;
                case "character edit":
                    var local=SnapshotStoryWorkshop();local.Characters[0].Fields["Traits"].Text="Local author edit wins over a pending response.";
                    CommitStoryWorkshop(local,"Local planning revision.");break;
                case "mode":
                    var modes=WorkshopControl<ComboBox>(dialog,"WorkshopMode");
                    modes.SelectedItem=modes.Items.OfType<WritingModeProfile>().First(item=>item.Mode!=SnapshotStoryWorkshop().WritingMode);break;
                case "opt-in":languageSettings.AllowAiLanguageServices=false;dialog.Activate();PumpInput();break;
                case "new document":dirty=false;New_Click(this,new RoutedEventArgs());break;
                case "close":dialog.Close();break;
            }
            var expected=WorkshopJson(SnapshotStoryWorkshop());var body=PagedDocumentText();
            if(change!="opt-in")RequireWorkshop(requested.IsCancellationRequested,"Changing "+change+" did not cancel its pending planning request.");
            response.SetResult(WorkshopResponse(new[]{WorkshopChange("story","story","","Premise","LATE PROPOSAL MUST NOT APPLY")},"LATE REPLY MUST NOT COMMIT"));
            WaitSentence(()=>returned,"The non-cooperative planning fixture did not return.");PumpInput();
            RequireWorkshop(WorkshopJson(SnapshotStoryWorkshop())==expected && PagedDocumentText()==body,
                "A late planning response overwrote current metadata or prose after "+change+".");
            if(change is "new document" or "close")RequireWorkshop(storyWorkshopWindow==null,"A document switch or Close retained the old workshop.");
            else dialog.Close();
            languageSettings.AllowAiLanguageServices=true;PumpInput();
        }

        dirty=false;New_Click(this,new RoutedEventArgs());
        var prompts=new List<string>();
        StoryWorkshopService.GeneratorOverride=(prompt,token)=>{token.ThrowIfCancellationRequested();prompts.Add(prompt);return Task.FromResult(WorkshopResponse());};
        languageSettings.AllowAiLanguageServices=false;ShowStoryWorkshop();var readonlyPreview=storyWorkshopWindow!;
        WorkshopControl<TextBox>(readonlyPreview,"WorkshopInput").Text="Do not send until enabled.";
        RequireWorkshop(!WorkshopControl<Button>(readonlyPreview,"WorkshopSend").IsEnabled && prompts.Count==0,
            "AI-language-services opt-in did not gate explicit planning requests.");
        readonlyPreview.Close();languageSettings.AllowAiLanguageServices=true;
        Editor.IsReadOnly=true;ShowStoryWorkshop();readonlyPreview=storyWorkshopWindow!;
        RequireWorkshop(!WorkshopControl<Button>(readonlyPreview,"WorkshopAddCharacter").IsEnabled &&
            !WorkshopControl<Button>(readonlyPreview,"WorkshopSend").IsEnabled,
            "A read-only document exposed active metadata-editing or planning-send controls.");
        readonlyPreview.Close();Editor.IsReadOnly=false;
        report.AppendLine("Cancel, character edits, planning-mode changes, AI opt-out, New and Close invalidated delayed responses. A non-cooperative provider could not commit stale notes or conversation into another document. Read-only and explicit AI opt-in gates were retained.");
    }

    static void SelectWorkshopField(Window dialog,string prefix,StoryWorkshopField field)
    {
        var groups=WorkshopControl<ComboBox>(dialog,prefix+"Groups");groups.SelectedItem=groups.Items.Cast<string>().Single(group=>group==field.Group);
        var fields=WorkshopControl<ListBox>(dialog,prefix+"Fields");fields.SelectedItem=fields.Items.OfType<StoryWorkshopField>().Single(item=>item.Key==field.Key);
    }

    static string WorkshopText(DependencyObject root)
    {
        var result=new StringBuilder();
        void Visit(DependencyObject current)
        {
            if(current is TextBlock text)result.AppendLine(text.Text);
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(current);i++)Visit(VisualTreeHelper.GetChild(current,i));
        }
        Visit(root);return result.ToString();
    }

    static void CaptureWorkshopWindow(Window dialog)
    {
        dialog.UpdateLayout();var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(dialog.ActualWidth)),
            Math.Max(1,(int)Math.Ceiling(dialog.ActualHeight)),96,96,PixelFormats.Pbgra32);
        image.Render(dialog);image.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var output=File.Create(Path.Combine(Path.GetTempPath(),"CtrlZed52StoryWorkshop.png"));encoder.Save(output);
    }

    static T WorkshopControl<T>(Window window,string name) where T:FrameworkElement
        =>window.FindName(name) as T??throw new InvalidOperationException("Story Workshop is missing control "+name+".");

    static void RequireWorkshop(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}

    static void RejectWorkshop(Action action,string message)
    {
        try{action();}
        catch(Exception error) when(error is InvalidDataException or JsonException or FormatException or ArgumentException or InvalidOperationException){return;}
        throw new InvalidOperationException(message);
    }
}
