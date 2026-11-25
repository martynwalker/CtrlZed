using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    // Synthetic HTTP and model fixtures only. This gate spends no provider credit
    // and does not claim to assess a live model's creative quality.
    internal static int RunWorkshopReliabilitySelfTest()
    {
        var report=new StringBuilder("Beta 53.1 workshop response and activity regression\r\n");
        var failures=new List<Exception>();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var originalFiles=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var originalDirectories=Directory.Exists(root)?Directory.GetDirectories(root,"*",SearchOption.AllDirectories)
            .Prepend(root).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var oldWordplay=WordplayWorkshopService.GeneratorOverride;
        var oldStory=StoryWorkshopService.GeneratorOverride;
        var oldSend=AiDirectService.StructuredSendOverride;
        var oldShutdown=Application.Current.ShutdownMode;
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
            WordplayWorkshopService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected implicit Wordplay request.");
            StoryWorkshopService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected implicit Story request.");
            Phase("native OpenAI schema request and explicit outcome handling",()=>VerifyWorkshopStructuredTransport(report));
            Phase("whole-document wrappers preserve strict workshop validation",()=>VerifyWorkshopJsonCompatibility(report));
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Normal,Width=1080,Height=780};
            window.Show();window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
            window.languageSettings.SpellingEnabled=false;window.languageSettings.ContextualSpellingSuggestions=false;
            window.languageSettings.GrammarAndStyleSuggestions=false;window.languageSettings.PoetryRhymeSuggestions=false;
            window.languageSettings.AllowAiLanguageServices=true;SpellCheck.SetIsEnabled(window.Editor,false);
            Phase("repeated Wordplay exploration, visible activity and retained work",()=>window.VerifyWordplayReliabilityWindow(report));
            Phase("Story activity, sequential turns, errors and cancellation",()=>window.VerifyStoryReliabilityWindow(report));
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("workshop and editor cleanup",()=>
            {
                window.wordplayWorkshopWindow?.Close();window.storyWorkshopWindow?.Close();window.characterVoiceWindow?.Close();
                window.closingApproved=true;window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.SetDocumentPassword(null);window.Close();
            });
            WordplayWorkshopService.GeneratorOverride=oldWordplay;StoryWorkshopService.GeneratorOverride=oldStory;
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
            report.AppendLine("Provider: deterministic in-process HTTP/model fixtures; no live calls, keys or raw manuscript logs.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed531WorkshopReliability.txt"),report.ToString());
            if(failures.Count>0)File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),new AggregateException(failures).ToString());
        }
        return failures.Count==0?0:134;
    }

    static string ReliabilityOpenAiEnvelope(string text,string status="completed")
        =>JsonSerializer.Serialize(new
        {
            id="resp_deterministic_fixture",status,
            output=new object[]
            {
                new{type="reasoning",summary=new[]{new{type="summary_text",text="Not an assistant JSON payload."}}},
                new{type="message",role="assistant",status="completed",content=new[]{new{type="output_text",text}}}
            }
        });

    static void VerifyWorkshopStructuredTransport(StringBuilder report)
    {
        var previous=AiDirectService.StructuredSendOverride;var calls=0;
        const string prompt="PRIVATE_WORKSHOP_PROMPT_FIXTURE_531";
        try
        {
            AiDirectService.StructuredSendOverride=async(request,token)=>
            {
                calls++;token.ThrowIfCancellationRequested();
                RequireReliability(request.Method==HttpMethod.Post && request.RequestUri?.AbsoluteUri=="https://api.openai.com/v1/responses",
                    "The structured request did not use the existing Responses endpoint.");
                RequireReliability(request.Headers.Authorization?.Scheme=="Bearer" && request.Headers.Authorization.Parameter=="dummy-fixture-key",
                    "The transport changed the supplied authentication rather than using the caller's configured credentials.");
                using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                RequireReliability(body.RootElement.GetProperty("model").GetString()=="fixture-model" &&
                    body.RootElement.GetProperty("input").GetString()==prompt,"The schema request changed the configured model or supplied prompt.");
                RequireReliability(!body.RootElement.TryGetProperty("previous_response_id",out _) && !body.RootElement.TryGetProperty("tools",out _),
                    "Workshop exploration introduced conversational carry-over or unrelated tools.");
                var format=body.RootElement.GetProperty("text").GetProperty("format");
                RequireReliability(format.GetProperty("type").GetString()=="json_schema" && format.GetProperty("strict").GetBoolean() &&
                    format.GetProperty("name").GetString()=="ctrlzed_wordplay","OpenAI was not explicitly asked for the workshop's strict JSON schema.");
                var schema=format.GetProperty("schema");
                RequireReliability(schema.GetProperty("type").GetString()=="object" && !schema.GetProperty("additionalProperties").GetBoolean() &&
                    schema.GetProperty("required").EnumerateArray().Any(item=>item.GetString()=="connections"),"The native request omitted the exact workshop object shape.");
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ReliabilityOpenAiEnvelope(WordplayFixtureResponse()),Encoding.UTF8,"application/json")};
            };
            for(var attempt=0;attempt<2;attempt++)
            {
                var operation=AiDirectService.GenerateOpenAiJsonAsync("fixture-model","dummy-fixture-key",prompt,"ctrlzed_wordplay",WorkshopResponseSchemas.Wordplay);
                AwaitSentenceTask(operation);
                RequireReliability(WordplayWorkshopService.ParseResponse(new WordplayWorkshopState{SeedText=WordplaySeedFixture},operation.Result).Count==6,
                    "A completed structured response was not usable on a repeated request.");
            }
            RequireReliability(calls==2,"Two explicit requests caused hidden retries or extra provider calls.");

            var valid=WordplayFixtureResponse();
            RequireReliability(AiDirectService.ParseOpenAiJsonResponse(ReliabilityOpenAiEnvelope(valid))==valid,
                "The response extractor included reasoning text or altered the assistant payload.");
            var refused=JsonSerializer.Serialize(new{status="completed",output=new[]{new{type="message",role="assistant",status="completed",
                content=new[]{new{type="refusal",refusal="PRIVATE_REFUSAL_FIXTURE_NOT_FOR_DIAGNOSTICS"}}}}});
            RejectStructured(AiStructuredFailureKind.Refused,()=>AiDirectService.ParseOpenAiJsonResponse(refused));
            var incomplete=JsonSerializer.Serialize(new{status="incomplete",incomplete_details=new{reason="max_output_tokens"},
                output=new[]{new{type="message",role="assistant",status="incomplete",content=new[]{new{type="output_text",text=valid}}}}});
            RejectStructured(AiStructuredFailureKind.Incomplete,()=>AiDirectService.ParseOpenAiJsonResponse(incomplete));
            var failed=JsonSerializer.Serialize(new{status="failed",error=new{code="server_error",message="PRIVATE_PROVIDER_DETAIL_NOT_FOR_DIAGNOSTICS"},output=Array.Empty<object>()});
            RejectStructured(AiStructuredFailureKind.Failed,()=>AiDirectService.ParseOpenAiJsonResponse(failed));
            RejectStructured(AiStructuredFailureKind.Empty,()=>AiDirectService.ParseOpenAiJsonResponse("{\"status\":\"completed\",\"output\":[]}"));
            RejectStructured(AiStructuredFailureKind.InvalidEnvelope,()=>AiDirectService.ParseOpenAiJsonResponse("{ incomplete envelope"));

            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                var previousCalls=calls;
                RejectReliabilityCancellation(AiDirectService.GenerateOpenAiJsonAsync("fixture-model","dummy-fixture-key",prompt,"ctrlzed_wordplay",WorkshopResponseSchemas.Wordplay,cancellation.Token));
                RequireReliability(calls==previousCalls,"A pre-cancelled structured request reached the HTTP transport.");
            }
            using(var cancellation=new CancellationTokenSource())
            {
                var reply=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken observed=default;
                AiDirectService.StructuredSendOverride=(_,token)=>{observed=token;return reply.Task;};
                var pending=AiDirectService.GenerateOpenAiJsonAsync("fixture-model","dummy-fixture-key",prompt,"ctrlzed_wordplay",WorkshopResponseSchemas.Wordplay,cancellation.Token);
                cancellation.Cancel();reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ReliabilityOpenAiEnvelope(valid))});
                RejectReliabilityCancellation(pending);RequireReliability(observed.IsCancellationRequested,"Cancellation did not reach the structured HTTP request.");
            }
            AiDirectService.StructuredSendOverride=(_,__)=>Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic HTTP timeout."));
            var timedOut=false;
            try{AwaitSentenceTask(AiDirectService.GenerateOpenAiJsonAsync("fixture-model","dummy-fixture-key",prompt,"ctrlzed_wordplay",WorkshopResponseSchemas.Wordplay));}
            catch(TimeoutException){timedOut=true;}
            RequireReliability(timedOut,"An HTTP timeout without user cancellation was mistaken for a neutral user Cancel.");
            report.AppendLine("Native requests retained model/prompt/authentication, used strict text.format JSON schema, were stateless and made one request per action. Completed assistant output was extracted independently of reasoning; refusal, incomplete, failed, empty and malformed envelopes were distinguished. Pre/post HTTP cancellation held, while an unrequested transport timeout remained an error.");
        }
        finally{AiDirectService.StructuredSendOverride=previous;}
    }

    static void VerifyWorkshopJsonCompatibility(StringBuilder report)
    {
        var wordplay=new WordplayWorkshopState{SeedText=WordplaySeedFixture};var raw=WordplayFixtureResponse();
        foreach(var wrapped in new[]{raw,"\uFEFF "+raw,"```json\n"+raw+"\n```"," \r\n```JSON\r\n"+raw+"\r\n```\r\n","```\n"+raw+"\n```"})
            RequireReliability(WordplayWorkshopService.ParseResponse(wordplay,wrapped).Count==6,"A complete valid JSON document or whole-document fence was rejected.");
        foreach(var invalid in new[]{"Here is your answer:\n"+raw,raw+raw,"```json\n"+raw+"\n``` extra explanation","```json\n"+raw,raw[..^1],
            raw.Replace("\"clock\"","\"fabricated source anchor\"",StringComparison.Ordinal),raw.Replace("\"connections\":","\"unexpected\":true,\"connections\":",StringComparison.Ordinal)})
            RejectWordplay(()=>WordplayWorkshopService.ParseResponse(wordplay,invalid),"Compatibility accepted prose, incomplete or invalid association content.");
        var planning=new StoryWorkshopState();var message="A detective must choose whom to trust.";var planningReply=WorkshopResponse();
        RequireReliability(StoryWorkshopService.ParseResponse(planning,message,"\uFEFF```json\n"+planningReply+"\n```").NextState.Conversation.Count==2,
            "Story Workshop did not accept the same conservative complete-document wrapper.");
        foreach(var invalid in new[]{"Explanation:\n"+planningReply,"```json\n"+planningReply+"\n``` trailing",planningReply[..^1],planningReply+planningReply})
            RejectWorkshop(()=>StoryWorkshopService.ParseResponse(planning,message,invalid),"Compatibility accepted a non-document planning response.");
        report.AppendLine("Raw JSON, one BOM and a single whole JSON/bare fence are accepted. Preambles, postambles, multiple documents, truncated JSON, unexpected fields and invented anchors remain rejected; no repair or arbitrary JSON scanning was introduced.");
    }

    sealed class ReliabilityPending
    {
        internal TaskCompletionSource<string> Reply { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token { get; }
        internal bool Returned { get; set; }
        internal ReliabilityPending(CancellationToken token)=>Token=token;
    }

    void FreshReliabilityDocument()
    {
        wordplayWorkshopWindow?.Close();storyWorkshopWindow?.Close();characterVoiceWindow?.Close();SetDocumentPassword(null);
        dirty=false;New_Click(this,new RoutedEventArgs());
        SentenceSetDocument(new FlowDocument(new Paragraph(new Run("MANUSCRIPT_REMAINS_PRIVATE_531"))),EditorViewMode.Continuous);
        languageSettings.AllowAiLanguageServices=true;
    }

    void VerifyWordplayReliabilityWindow(StringBuilder report)
    {
        FreshReliabilityDocument();var body=PagedDocumentText();var pending=new List<ReliabilityPending>();
        WordplayWorkshopService.GeneratorOverride=async(_,token)=>
        {var request=new ReliabilityPending(token);pending.Add(request);var text=await request.Reply.Task;request.Returned=true;return text;};
        ShowWordplayWorkshop();var dialog=wordplayWorkshopWindow!;
        WordplayControl<TextBox>(dialog,"WordplaySeed").Text=WordplaySeedFixture;
        var explore=WordplayControl<Button>(dialog,"WordplayExplore");var cancel=WordplayControl<Button>(dialog,"WordplayCancel");
        void Begin(int count)
        {
            RequireReliability(explore.IsEnabled,"A usable pile could not start exploration.");explore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>pending.Count==count,"The requested exploration did not start.");AssertReliabilityBusy(dialog,"Wordplay");
        }
        void Finish(int index,string answer,WorkshopStatusKind kind)
        {
            pending[index].Reply.SetResult(answer);WaitSentence(()=>pending[index].Returned && !cancel.IsEnabled,"A repeated exploration did not become idle.");
            AssertReliabilityStopped(dialog,"Wordplay",kind);
        }
        Begin(1);var elapsed=ReliabilityControl<TextBlock>(dialog,"WordplayElapsed");var initialElapsed=elapsed.Text;
        WaitSentence(()=>elapsed.Text!=initialElapsed,"The busy gauge did not show advancing elapsed time.");
        RequireReliability(!elapsed.Text.Contains('%'),"The activity gauge claimed a fabricated completion percentage.");
        CaptureReliabilityWindow(dialog,"CtrlZed531WordplayBusy.png");
        Finish(0,WordplayFixtureResponse(),WorkshopStatusKind.Success);
        RequireReliability(SnapshotWordplayWorkshop().Results.Count==6 && PagedDocumentText()==body,"Successful exploration failed to save results separately from the manuscript.");
        WordplayControl<TextBox>(dialog,"WordplayEdit").Text="A kept phrase survives later provider problems.";
        WordplayControl<Button>(dialog,"WordplayKeep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var favourite=JsonSerializer.Serialize(SnapshotWordplayWorkshop().Favourites.Single());
        Begin(2);Finish(1,"```json\n"+WordplayFixtureResponse()+"\n```",WorkshopStatusKind.Success);
        RequireReliability(SnapshotWordplayWorkshop().Results.Count==6 && JsonSerializer.Serialize(SnapshotWordplayWorkshop().Favourites.Single())==favourite,
            "A wrapped second response erased saved ideas or failed after a successful first run.");
        var beforeFailure=WordplayJson(SnapshotWordplayWorkshop());
        Begin(3);Finish(2,"{\"connections\":[",WorkshopStatusKind.Error);
        RequireReliability(WordplayJson(SnapshotWordplayWorkshop())==beforeFailure && PagedDocumentText()==body,
            "An unreadable reply changed completed results, favourites or manuscript prose.");
        CaptureReliabilityWindow(dialog,"CtrlZed531WorkshopError.png");
        Begin(4);Finish(3,WordplayFixtureResponse(),WorkshopStatusKind.Success);
        CaptureReliabilityWindow(dialog,"CtrlZed531WorkshopSuccess.png");

        Begin(5);cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));AssertReliabilityStopped(dialog,"Wordplay",WorkshopStatusKind.Neutral);
        RequireReliability(pending[4].Token.IsCancellationRequested,"Cancel did not signal the old exploration.");
        Begin(6);pending[4].Reply.SetResult(WordplayFixtureResponse());WaitSentence(()=>pending[4].Returned,"The deliberately late cancelled response did not return.");PumpInput();
        AssertReliabilityBusy(dialog,"Wordplay");RequireReliability(!pending[5].Token.IsCancellationRequested,"An old completion cancelled the replacement request.");
        Finish(5,WordplayFixtureResponse(),WorkshopStatusKind.Success);
        RequireReliability(JsonSerializer.Serialize(SnapshotWordplayWorkshop().Favourites.Single())==favourite,"Cancelled/replacement requests rewrote kept provenance.");
        var beforeInvalid=WordplayJson(SnapshotWordplayWorkshop());var originalSeed=SnapshotWordplayWorkshop().SeedText;
        Begin(7);WordplayControl<TextBox>(dialog,"WordplaySeed").Text="Invalid embedded \uFFFC object";
        AssertReliabilityStopped(dialog,"Wordplay",WorkshopStatusKind.Error);
        RequireReliability(pending[6].Token.IsCancellationRequested && WordplayJson(SnapshotWordplayWorkshop())==beforeInvalid,
            "An invalid source edit left the old request running or changed valid saved metadata.");
        pending[6].Reply.SetResult(WordplayFixtureResponse());WaitSentence(()=>pending[6].Returned,"The invalid-edit fixture did not return.");PumpInput();
        RequireReliability(WordplayJson(SnapshotWordplayWorkshop())==beforeInvalid,"An invalid source edit allowed a late response to commit.");
        WordplayControl<TextBox>(dialog,"WordplaySeed").Text=originalSeed;
        var beforeClose=WordplayJson(SnapshotWordplayWorkshop());Begin(8);dialog.Close();
        AssertReliabilityIndicatorHidden(dialog,"Wordplay");RequireReliability(pending[7].Token.IsCancellationRequested,"Closing left exploration running without cancellation.");
        pending[7].Reply.SetResult(WordplayFixtureResponse());WaitSentence(()=>pending[7].Returned,"The closed-window fixture did not return.");PumpInput();
        RequireReliability(wordplayWorkshopWindow==null && WordplayJson(SnapshotWordplayWorkshop())==beforeClose && PagedDocumentText()==body,
            "A closed workshop committed its late reply or changed the manuscript.");
        report.AppendLine("Wordplay first/raw, second/fenced, third/malformed and fourth/recovery replies behaved correctly: visible elapsed activity, green success, red errors and retained results/favourites. Cancel/Close stopped animation; an old non-cooperative completion could not stop a new gauge. An invalid source edit cancelled before validation and its late reply could not change valid saved work.");
    }

    void VerifyStoryReliabilityWindow(StringBuilder report)
    {
        FreshReliabilityDocument();var body=PagedDocumentText();var pending=new List<ReliabilityPending>();
        StoryWorkshopService.GeneratorOverride=async(_,token)=>
        {var request=new ReliabilityPending(token);pending.Add(request);var text=await request.Reply.Task;request.Returned=true;return text;};
        ShowStoryWorkshop();var dialog=storyWorkshopWindow!;var input=WorkshopControl<TextBox>(dialog,"WorkshopInput");
        var send=WorkshopControl<Button>(dialog,"WorkshopSend");var cancel=WorkshopControl<Button>(dialog,"WorkshopCancel");
        void Begin(string message,int count)
        {
            input.Text=message;RequireReliability(send.IsEnabled,"A ready Story Workshop could not send a message.");
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitSentence(()=>pending.Count==count,"The planning request did not start.");AssertReliabilityBusy(dialog,"Workshop");
        }
        void Finish(int index,string answer,int turns,WorkshopStatusKind kind)
        {
            pending[index].Reply.SetResult(answer);WaitSentence(()=>pending[index].Returned && !cancel.IsEnabled,"A repeated planning request did not become idle.");
            AssertReliabilityStopped(dialog,"Workshop",kind);RequireReliability(SnapshotStoryWorkshop().Conversation.Count==turns,"The completed planning conversation acquired or lost a turn.");
        }
        Begin("A detective must choose whom to trust.",1);CaptureReliabilityWindow(dialog,"CtrlZed531StoryBusy.png");
        Finish(0,WorkshopResponse(reply:"Let us examine that choice.",questions:Array.Empty<string>()),2,WorkshopStatusKind.Success);
        Begin("The reversal should expose a mistaken assumption.",2);
        Finish(1,"```json\n"+WorkshopResponse(reply:"The reversal can test that assumption.",questions:Array.Empty<string>())+"\n```",4,WorkshopStatusKind.Success);
        Begin("An unanswered choice remains.",3);var beforeFailure=WorkshopJson(SnapshotStoryWorkshop());
        Finish(2,"This is explanatory prose instead of JSON.",4,WorkshopStatusKind.Error);
        RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==beforeFailure && input.Text=="An unanswered choice remains.",
            "An unreadable planning reply lost the author's retry text or changed saved notes.");
        Begin(input.Text,4);Finish(3,WorkshopResponse(reply:"Keep that uncertainty open until the writer decides.",questions:Array.Empty<string>()),6,WorkshopStatusKind.Success);
        var beforeInvalid=WorkshopJson(SnapshotStoryWorkshop());Begin("Consider an unresolved question.",5);
        WorkshopControl<TextBox>(dialog,"WorkshopQuestions").Text=new string('q',StoryWorkshopState.MaximumQuestionLength+1);
        AssertReliabilityStopped(dialog,"Workshop",WorkshopStatusKind.Error);
        RequireReliability(pending[4].Token.IsCancellationRequested && WorkshopJson(SnapshotStoryWorkshop())==beforeInvalid,
            "An invalid planning edit left the old request running or changed saved planning.");
        pending[4].Reply.SetResult(WorkshopResponse());WaitSentence(()=>pending[4].Returned,"The invalid-question fixture did not return.");PumpInput();
        RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==beforeInvalid,"An invalid planning edit permitted an old response to commit.");
        WorkshopControl<TextBox>(dialog,"WorkshopQuestions").Text="";
        var beforeCancellation=WorkshopJson(SnapshotStoryWorkshop());Begin("Consider another possibility.",6);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));AssertReliabilityStopped(dialog,"Workshop",WorkshopStatusKind.Neutral);
        pending[5].Reply.SetResult(WorkshopResponse());WaitSentence(()=>pending[5].Returned,"A cancelled Story fixture did not return.");PumpInput();
        RequireReliability(WorkshopJson(SnapshotStoryWorkshop())==beforeCancellation,"A cancelled planning reply was committed.");
        Begin("A reply should not follow a closed window.",7);dialog.Close();AssertReliabilityIndicatorHidden(dialog,"Workshop");
        pending[6].Reply.SetResult(WorkshopResponse());WaitSentence(()=>pending[6].Returned,"The closed Story fixture did not return.");PumpInput();
        RequireReliability(storyWorkshopWindow==null && WorkshopJson(SnapshotStoryWorkshop())==beforeCancellation && PagedDocumentText()==body,
            "Closing Story Workshop allowed a late response to change planning or prose.");
        report.AppendLine("Story Workshop showed the same honest busy gauge and green/red outcomes across repeated raw/wrapped/error/recovery turns. Failed replies retained author input and saved conversation. Invalid planning edits cancelled before validation; Cancel and Close stopped activity and rejected late notes.");
    }

    static void AssertReliabilityBusy(Window dialog,string prefix)
    {
        var progress=ReliabilityControl<ProgressBar>(dialog,prefix+"Progress");var elapsed=ReliabilityControl<TextBlock>(dialog,prefix+"Elapsed");
        RequireReliability(progress.Visibility==Visibility.Visible && progress.IsIndeterminate && elapsed.Visibility==Visibility.Visible && elapsed.Text.Length>0,
            prefix+" did not expose real indeterminate activity and elapsed time while awaiting its reply.");
        AssertReliabilityStatus(dialog,prefix,WorkshopStatusKind.Busy);
    }

    static void AssertReliabilityStopped(Window dialog,string prefix,WorkshopStatusKind kind)
    {AssertReliabilityIndicatorHidden(dialog,prefix);AssertReliabilityStatus(dialog,prefix,kind);}

    static void AssertReliabilityIndicatorHidden(Window dialog,string prefix)
    {
        var progress=ReliabilityControl<ProgressBar>(dialog,prefix+"Progress");var elapsed=ReliabilityControl<TextBlock>(dialog,prefix+"Elapsed");
        RequireReliability(progress.Visibility==Visibility.Collapsed && !progress.IsIndeterminate && elapsed.Visibility==Visibility.Collapsed && elapsed.Text.Length==0,
            prefix+" left a progress animation or elapsed timer visible after the request ended.");
    }

    static void AssertReliabilityStatus(Window dialog,string prefix,WorkshopStatusKind kind)
    {
        var status=ReliabilityControl<TextBlock>(dialog,prefix+"Status");
        var colour=kind switch{WorkshopStatusKind.Success=>Colors.DarkGreen,WorkshopStatusKind.Error=>Colors.Firebrick,WorkshopStatusKind.Busy=>Colors.DarkBlue,_=>Colors.DimGray};
        RequireReliability(AutomationProperties.GetItemStatus(status)==kind.ToString() && status.Foreground is SolidColorBrush ink && ink.Color==colour,
            prefix+" status did not show its explicit "+kind+" state and corresponding readable colour. Text: "+status.Text);
    }

    static void RejectStructured(AiStructuredFailureKind kind,Func<string> action)
    {
        try{action();}
        catch(AiStructuredResponseException error)
        {
            RequireReliability(error.Kind==kind,"The provider outcome was classified as "+error.Kind+" instead of "+kind+".");
            RequireReliability(!error.Message.Contains("PRIVATE_",StringComparison.Ordinal),"Provider diagnostics echoed raw reply details.");return;
        }
        throw new InvalidOperationException("The provider outcome "+kind+" was accepted as usable JSON.");
    }

    static void RejectReliabilityCancellation(Task task)
    {
        try{AwaitSentenceTask(task);}
        catch(OperationCanceledException){return;}
        throw new InvalidOperationException("A cancelled native JSON request returned usable output.");
    }

    static T ReliabilityControl<T>(Window dialog,string name) where T:FrameworkElement
        =>dialog.FindName(name) as T??throw new InvalidOperationException("A workshop is missing reliability control "+name+".");

    static void CaptureReliabilityWindow(Window dialog,string name)
    {
        dialog.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(dialog.ActualWidth)),Math.Max(1,(int)Math.Ceiling(dialog.ActualHeight)),96,96,PixelFormats.Pbgra32);
        bitmap.Render(dialog);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var screenshot=File.Create(Path.Combine(Path.GetTempPath(),name));encoder.Save(screenshot);
    }

    static void RequireReliability(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
