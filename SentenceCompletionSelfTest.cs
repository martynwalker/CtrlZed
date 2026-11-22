using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    // This Windows regression uses only the deterministic transport below. It does
    // not contact a provider, spend credit or assess a live model's prose quality.
    internal static int RunSentenceCompletionSelfTest()
    {
        MainWindow? window=null;
        IDataObject? clipboard=null;
        var report=new StringBuilder("Beta 48 Finish Sentence deterministic regression\r\n");
        var saved=new Dictionary<string,byte[]?>(StringComparer.OrdinalIgnoreCase);
        var originalTransport=SentenceCompletionService.GeneratorOverride;
        var originalShutdown=Application.Current.ShutdownMode;
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        Directory.CreateDirectory(root);
        foreach(var name in new[]{"settings.json","editor-view.json","language-settings.json","personal-en-GB.lex",
            "Recovery.rtf","Recovery.settings.json","Recovery.rtf.protected"})
            Preserve(Path.Combine(root,name));
        Preserve(SentenceCompletionPreferencesStore.FilePath);
        void Preserve(string path)
        {
            if(!saved.ContainsKey(path))saved[path]=File.Exists(path)?File.ReadAllBytes(path):null;
        }
        try
        {
            clipboard=Clipboard.GetDataObject();
            foreach(var path in saved.Keys.Where(path=>Path.GetFileName(path).StartsWith("Recovery",StringComparison.Ordinal)))
                if(File.Exists(path))File.Delete(path);
            SentenceCompletionService.ClearSummaryCache();
            VerifySentenceCompletionContract(report);
            VerifySentenceCompletionContextTransport(report);
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow{WindowState=WindowState.Maximized};
            window.Show();window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();
            window.grammarReviewTimer?.Stop();
            window.languageSettings.SpellingEnabled=false;
            window.languageSettings.ContextualSpellingSuggestions=false;
            window.languageSettings.GrammarAndStyleSuggestions=false;
            window.languageSettings.PoetryRhymeSuggestions=false;
            window.languageSettings.AllowAiLanguageServices=true;
            SpellCheck.SetIsEnabled(window.Editor,false);
            var phaseFailures=new List<Exception>();
            try
            {
                window.VerifySentenceCompletionTargets(report);
                report.AppendLine("PASS: editor target phase.");
            }
            catch(Exception error)
            {
                report.AppendLine("FAIL: editor target phase: "+error);
                phaseFailures.Add(new InvalidOperationException("The editor target phase failed.",error));
            }
            try
            {
                window.VerifySentenceCompletionWindow(report);
                report.AppendLine("PASS: real review-window phase.");
            }
            catch(Exception error)
            {
                report.AppendLine("FAIL: real review-window phase: "+error);
                phaseFailures.Add(new InvalidOperationException("The real review-window phase failed.",error));
            }
            if(phaseFailures.Count>0)throw new AggregateException("Sentence-completion regression phases failed.",phaseFailures);
            report.AppendLine("PASS: strict three-candidate contract, private preferences, complete document context, cache/cancellation, rich range fidelity, stale protection, both views and the real Finish Sentence window.");
            report.AppendLine("Provider: deterministic in-process test transport only; no live requests or prose-quality claims.");
            return 0;
        }
        catch(Exception error)
        {
            report.AppendLine("FAIL: "+error);
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),error.ToString());
            return 126;
        }
        finally
        {
            if(window!=null)
            {
                try{window.sentenceCompletionWindow?.Close();}catch{}
                window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();window.grammarAiCancellation?.Cancel();
                window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.Close();
            }
            SentenceCompletionService.GeneratorOverride=originalTransport;
            SentenceCompletionService.ClearSummaryCache();
            Application.Current.ShutdownMode=originalShutdown;
            try{if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();}catch{}
            foreach(var pair in saved)
            {
                if(pair.Value==null){if(File.Exists(pair.Key))File.Delete(pair.Key);}
                else File.WriteAllBytes(pair.Key,pair.Value);
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed48SentenceCompletion.txt"),report.ToString());
        }
    }

    static SentenceCompletionRequest SentenceFixtureRequest(string wider="")
        =>new("The committee ",", as predicted.","","The committee , as predicted.",wider,
            "Whole document",WritingModeProfiles.Get(WritingMode.Business),"Cynical",
            SentenceCompletionService.BuiltIns.Single(item=>item.Name=="Cynical").Instruction,
            CompletionStrength.Balanced,"Preserve the intended approval, with dry understatement.");

    static string SentenceFixtureJson(string label="")
        =>JsonSerializer.Serialize(new{suggestions=new[]{
            new{text="approved the proposal"+label,explanation="Keeps the intended decision concise."},
            new{text="accepted the inevitable"+label,explanation="Adds restrained cynicism."},
            new{text="declared the matter settled"+label,explanation="Uses a firmer conclusion."}}});

    static void VerifySentenceCompletionContract(StringBuilder report)
    {
        var names=SentenceCompletionService.BuiltIns.Select(item=>item.Name).ToArray();
        RequireSentence(new[]{"Comedic","Prophetic","Awkward","Sharp","Cynical","Neutral"}.All(names.Contains),
            "A required built-in inflection is missing.");
        RequireSentence(SentenceCompletionService.BuiltIns.All(item=>!string.IsNullOrWhiteSpace(item.Instruction)),
            "A built-in inflection has no useful instruction.");
        var request=SentenceFixtureRequest("Context marker: MARK_CONTEXT_END");
        var prompt=SentenceCompletionService.BuildPrompt(request);
        foreach(var value in new[]{request.Prefix,request.Suffix,request.WritingMode.DisplayName,request.Inflection,
            request.InflectionInstruction,request.MeaningHint,"MARK_CONTEXT_END","Balanced"})
            RequireSentence(prompt.Contains(value,StringComparison.Ordinal),"The completion prompt omitted a selected input: "+value);
        RequireSentence(prompt.Contains("British",StringComparison.OrdinalIgnoreCase),"The prompt omitted British English.");
        var parsed=SentenceCompletionService.ParseResponse(SentenceFixtureJson());
        RequireSentence(parsed.Count==3 && parsed.Select(item=>item.Text).Distinct().Count()==3 &&
            parsed.All(item=>!string.IsNullOrWhiteSpace(item.Explanation)),"A valid three-candidate response was not retained.");
        foreach(var malformed in new[]{
            "```json\n"+SentenceFixtureJson()+"\n```",
            SentenceFixtureJson()+" trailing prose",
            "{\"suggestions\":[{\"text\":\"one\",\"explanation\":\"only one\"}]}",
            "{\"suggestions\":[{\"text\":\"one\",\"explanation\":\"a\"},{\"text\":\"ONE\",\"explanation\":\"b\"},{\"text\":\"three\",\"explanation\":\"c\"}]}",
            SentenceFixtureJson().Replace("approved the proposal","line one\\nline two",StringComparison.Ordinal),
            SentenceFixtureJson().Replace("\"explanation\":","\"unexpected\":true,\"explanation\":",StringComparison.Ordinal),
            SentenceFixtureJson().Replace("\"suggestions\":","\"unexpected\":true,\"suggestions\":",StringComparison.Ordinal),
            SentenceFixtureJson().Replace("approved the proposal",new string('x',1001),StringComparison.Ordinal),
            SentenceFixtureJson().Replace("\"text\":\"approved the proposal\"","\"text\":\"approved the proposal\",\"text\":\"duplicate field\"",StringComparison.Ordinal)})
            ExpectSentenceRejection(()=>SentenceCompletionService.ParseResponse(malformed),"An invalid provider response was accepted.");

        var preferences=new SentenceCompletionPreferences
        {
            Inflection="Regression dry voice",Strength=CompletionStrength.Bold,ContextScope=CompletionContextScope.Section,
            CustomInflections=new(){new CompletionInflection("Regression dry voice","Use restrained, precise understatement.")}
        };
        SentenceCompletionPreferencesStore.Save(preferences);
        var loaded=SentenceCompletionPreferencesStore.Load();
        RequireSentence(loaded.Inflection==preferences.Inflection && loaded.Strength==CompletionStrength.Bold &&
            loaded.ContextScope==CompletionContextScope.Section && loaded.CustomInflections.Any(item=>
                item.Name==preferences.Inflection && item.Instruction==preferences.CustomInflections[0].Instruction),
            "Custom inflection, strength or context preference did not round-trip.");
        var persisted=File.ReadAllText(SentenceCompletionPreferencesStore.FilePath);
        RequireSentence(!persisted.Contains(request.Prefix,StringComparison.Ordinal) &&
            !persisted.Contains(request.MeaningHint,StringComparison.Ordinal) &&
            !persisted.Contains("MARK_CONTEXT_END",StringComparison.Ordinal),"Author prose or its meaning hint was saved with preferences.");
        SentenceCompletionPreferencesStore.Save(new SentenceCompletionPreferences());
        report.AppendLine("Strict JSON accepted exactly three distinct explained candidates and rejected fences, trailing prose, wrong counts, duplicate wording/fields, extra fields, line breaks and oversized text. Named inflection, strength and context preferences round-tripped without author prose or hints.");
    }

    static void VerifySentenceCompletionContextTransport(StringBuilder report)
    {
        var prompts=new List<string>();
        var cancelNextSummary=false;
        var cancelledSummaryStarted=false;
        async Task<string> Provider(string prompt,CancellationToken token)
        {
            prompts.Add(prompt);
            if(prompt.Contains("CtrlZed whole-document context summary",StringComparison.Ordinal))
            {
                if(cancelNextSummary)
                {
                    cancelNextSummary=false;cancelledSummaryStarted=true;
                    await Task.Delay(Timeout.Infinite,token);
                }
                token.ThrowIfCancellationRequested();
                var markers=Regex.Matches(prompt,@"MARK_[A-Z0-9_]+")
                    .Select(match=>match.Value).Distinct().ToArray();
                return JsonSerializer.Serialize(new{summary=string.Join(" ",markers)});
            }
            token.ThrowIfCancellationRequested();
            return SentenceFixtureJson();
        }
        Func<string,CancellationToken,Task<string>> transport=Provider;
        SentenceCompletionService.GeneratorOverride=transport;
        SentenceCompletionService.ClearSummaryCache();
        var shortContext=new string('x',4000)+" MARK_DIRECT_END";
        AwaitSentenceTask(SentenceCompletionService.GenerateAsync(SentenceFixtureRequest(shortContext)));
        RequireSentence(prompts.Count==1 && prompts[0].Contains("MARK_DIRECT_END",StringComparison.Ordinal),
            "A bounded document lost its end context or triggered unnecessary summary transport.");
        prompts.Clear();
        var source=new StringBuilder();
        for(var part=0;part<18;part++)source.Append("MARK_").Append(part.ToString("D2"))
            .Append(' ').Append(new string('x',7900)).AppendLine();
        source.Append("MARK_DOCUMENT_END");
        var request=SentenceFixtureRequest(source.ToString());
        AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request));
        var summaryPrompts=prompts.Where(IsSummary).ToArray();
        RequireSentence(summaryPrompts.Length>1,"A large document did not use bounded complete-source summaries.");
        RequireSentence(summaryPrompts.All(value=>value.Length<=60000),"A summary request exceeded the provider input budget.");
        var reconstructed=new StringBuilder();
        foreach(var prompt in summaryPrompts)
        {
            const string separator="INPUT DATA (JSON)\n";
            var normalisedPrompt=prompt.Replace("\r\n","\n");
            var index=normalisedPrompt.IndexOf(separator,StringComparison.Ordinal);
            RequireSentence(index>=0,"A summary request did not expose its bounded source data.");
            using var data=JsonDocument.Parse(normalisedPrompt[(index+separator.Length)..]);
            if(data.RootElement.GetProperty("coverage").GetString()!.StartsWith("Source chunk ",StringComparison.Ordinal))
                foreach(var part in data.RootElement.GetProperty("orderedParts").EnumerateArray())reconstructed.Append(part.GetString());
        }
        RequireSentence(reconstructed.ToString()==source.ToString(),"Summary chunk transport lost, duplicated or reordered source characters.");
        var allSummaries=string.Join("\n",summaryPrompts);
        var completion=prompts.Last(value=>!IsSummary(value));
        foreach(var marker in Enumerable.Range(0,18).Select(part=>"MARK_"+part.ToString("D2")).Append("MARK_DOCUMENT_END"))
            RequireSentence(allSummaries.Contains(marker,StringComparison.Ordinal) && completion.Contains(marker,StringComparison.Ordinal),
                "Whole-document context lost its middle or end marker: "+marker);
        prompts.Clear();
        AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request));
        RequireSentence(prompts.Count==1 && !IsSummary(prompts[0]),"An unchanged document did not reuse its complete-source summary.");
        prompts.Clear();
        AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request with{WiderContext=request.WiderContext+" MARK_CHANGED"}));
        RequireSentence(prompts.Any(IsSummary) && prompts.Last(value=>!IsSummary(value)).Contains("MARK_CHANGED",StringComparison.Ordinal),
            "An edited source reused a stale whole-document summary.");
        prompts.Clear();
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            ExpectSentenceCancellation(()=>AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request,cancelled.Token)));
            RequireSentence(prompts.Count==0,"A pre-cancelled request reached the provider transport or summary cache.");
        }
        SentenceCompletionService.ClearSummaryCache();
        cancelNextSummary=true;
        using(var cancelled=new CancellationTokenSource())
        {
            var pending=SentenceCompletionService.GenerateAsync(request,cancelled.Token);
            WaitSentence(()=>cancelledSummaryStarted,"The cancellable summary did not start.");
            cancelled.Cancel();
            ExpectSentenceCancellation(()=>AwaitSentenceTask(pending));
            RequireSentence(prompts.All(IsSummary),"A cancelled summary continued to final generation.");
        }
        prompts.Clear();
        AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request));
        RequireSentence(prompts.Count(IsSummary)>1,"A cancelled partial summary polluted the reusable complete-source cache.");
        foreach(var oversizeMerge in new[]{false,true})
        {
            SentenceCompletionService.ClearSummaryCache();
            SentenceCompletionService.GeneratorOverride=(prompt,token)=>
            {
                token.ThrowIfCancellationRequested();
                if(IsSummary(prompt) && (!oversizeMerge || prompt.Contains("Final merge of the entire supplied wider context.",StringComparison.Ordinal)))
                    return Task.FromResult(JsonSerializer.Serialize(new{summary=new string('s',oversizeMerge?18001:6001)}));
                return transport(prompt,token);
            };
            ExpectSentenceRejection(()=>AwaitSentenceTask(SentenceCompletionService.GenerateAsync(request)),
                oversizeMerge?"An oversized final summary was accepted.":"An oversized chunk summary was accepted.");
        }
        SentenceCompletionService.GeneratorOverride=transport;
        SentenceCompletionService.ClearSummaryCache();
        report.AppendLine("Deterministic transport received complete direct context, then every large-document chunk including all middle/end markers. Complete summaries were reused, source edits invalidated them, and pre-/mid-summary cancellation made no final request or reusable partial cache.");
        static bool IsSummary(string value)=>value.Contains("CtrlZed whole-document context summary",StringComparison.Ordinal);
    }

    void VerifySentenceCompletionTargets(StringBuilder report)
    {
        pageWidth=794;pageHeight=1123;pageMargins=new Thickness(72);zoomPercent=100;ApplyZoom();
        foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
        {
            var lead=new Run("The committee "){FontWeight=FontWeights.Bold,Foreground=Brushes.Navy};
            var tail=new Run(", as predicted."){FontStyle=FontStyles.Italic};
            var drawing=new DrawingImage(new GeometryDrawing(Brushes.DarkSlateBlue,null,new RectangleGeometry(new Rect(0,0,12,8))));
            drawing.Freeze();
            var paragraph=new Paragraph();
            paragraph.Inlines.Add(new InlineUIContainer(new Image{Source=drawing,Width=12,Height=8}));
            paragraph.Inlines.Add(lead);paragraph.Inlines.Add(tail);
            var following=new Paragraph(new Run("FOLLOWING PARAGRAPH MUST SURVIVE."));
            var document=new FlowDocument(paragraph);document.Blocks.Add(following);
            SentenceSetDocument(document,mode);
            Editor.CaretPosition=lead.ContentEnd;
            Editor.Focus();PumpInput();
            if(mode==EditorViewMode.Paged)FreshPagedLayout();
            var caretContext=$"CaretDirection={Editor.CaretPosition.LogicalDirection}; CaretParent={Editor.CaretPosition.Parent?.GetType().Name}; CaretRun=[{(Editor.CaretPosition.Parent as Run)?.Text}]; "+
                $"SelectionWeight={Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty)}; SelectionForeground={Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty)}; "+
                $"LeadWeight={lead.FontWeight}; LeadForeground={lead.Foreground}";
            var target=CaptureSentenceCompletionTarget();
            RequireSentence(!target.IsReplacement && target.Prefix.EndsWith("The committee ",StringComparison.Ordinal) &&
                target.Suffix.StartsWith(", as predicted.",StringComparison.Ordinal),"Caret capture lost its prefix or right context in "+mode+".");
            var before=PagedDocumentText();
            ResetSentenceUndo();
            ApplySentenceCompletionSuggestion(target,"approved the proposal");
            var after=before.Replace("The committee , as predicted.","The committee approved the proposal, as predicted.",StringComparison.Ordinal);
            RequireSentence(PagedDocumentText()==after && EnumerateImageContainers(Editor.Document).Count()==1,
                "Insertion changed adjacent prose, paragraphs or an inline picture in "+mode+".");
            var inserted=SentenceFindText(paragraph,"approved the proposal");
            var insertedWeight=inserted.GetPropertyValue(TextElement.FontWeightProperty);
            var insertedForeground=inserted.GetPropertyValue(TextElement.ForegroundProperty);
            var capturedFormatting=string.Join(", ",target.Formatting.Where(item=>item.Property==TextElement.FontWeightProperty ||
                item.Property==TextElement.ForegroundProperty).Select(item=>item.Property.Name+"="+item.Value));
            var actualRuns=string.Join(" | ",EnumeratePlainPasteRuns(paragraph.Inlines).Select(run=>
                $"[{run.Text}] Weight={run.FontWeight}, Foreground={run.Foreground}"));
            RequireSentence(Equals(insertedWeight,FontWeights.Bold) &&
                insertedForeground is SolidColorBrush ink && ink.Color==Colors.Navy,
                $"Inserted words did not inherit the destination run's formatting in {mode}. ActualWeight={insertedWeight}; ActualForeground={insertedForeground}; "+
                $"MeasuredRange=[{inserted.Text}]; RangeParents={inserted.Start.Parent?.GetType().Name}/{inserted.End.Parent?.GetType().Name}; CapturedFormatting={capturedFormatting}; {caretContext}; Runs={actualRuns}");
            RequireSentence(Equals(SentenceFindText(paragraph,", as predicted.").GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Italic),
                "Insertion changed the formatting of untouched right-hand text in "+mode+".");
            RequireSentence(Editor.CanUndo,"Insertion created no Undo operation in "+mode+".");
            Editor.Undo();PumpInput();
            RequireSentence(PagedDocumentText()==before && EnumerateImageContainers(Editor.Document).Count()==1 && Editor.CanRedo,
                "One Undo did not restore the complete rich insertion source in "+mode+".");
            Editor.Redo();PumpInput();
            RequireSentence(PagedDocumentText()==after && EnumerateImageContainers(Editor.Document).Count()==1,
                "One Redo did not restore the insertion and preserve its adjacent picture in "+mode+".");

            paragraph=new Paragraph(new Run("The plan looked odd until dawn."));
            document=new FlowDocument(paragraph);document.Blocks.Add(new Paragraph(new Run("FOLLOWING TEXT IS NOT SELECTED.")));
            SentenceSetDocument(document,mode);
            SentenceSelectText(paragraph," looked odd ");
            target=CaptureSentenceCompletionTarget();
            RequireSentence(target.IsReplacement && target.SelectedText=="looked odd",
                "A bounded phrase was not captured as replacement wording in "+mode+".");
            before=PagedDocumentText();ResetSentenceUndo();
            ApplySentenceCompletionSuggestion(target,"made sense");
            after=before.Replace(" looked odd "," made sense ",StringComparison.Ordinal);
            RequireSentence(PagedDocumentText()==after,"Selection replacement damaged edge spacing or following text in "+mode+".");
            Editor.Undo();PumpInput();
            RequireSentence(PagedDocumentText()==before && Editor.CanRedo,"One Undo did not restore the selected phrase in "+mode+".");
            Editor.Redo();PumpInput();
            RequireSentence(PagedDocumentText()==after,"One Redo did not restore the selected-phrase replacement in "+mode+".");
            paragraph=new Paragraph(new Run("An entire paragraph is selected."));
            document=new FlowDocument(paragraph);document.Blocks.Add(new Paragraph(new Run("FOLLOWING PARAGRAPH IS OUTSIDE THE SELECTION.")));
            SentenceSetDocument(document,mode);
            Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
            target=CaptureSentenceCompletionTarget();
            before=PagedDocumentText();ResetSentenceUndo();
            ApplySentenceCompletionSuggestion(target,"The complete paragraph has changed.");
            after=before.Replace("An entire paragraph is selected.","The complete paragraph has changed.",StringComparison.Ordinal);
            RequireSentence(PagedDocumentText()==after && Editor.Document.Blocks.Count==2,
                "Replacing a complete single paragraph consumed or merged its following paragraph in "+mode+".");
            Editor.Undo();PumpInput();
            RequireSentence(PagedDocumentText()==before && Editor.Document.Blocks.Count==2 && Editor.CanRedo,
                "One Undo did not restore a complete paragraph replacement in "+mode+".");
            Editor.Redo();PumpInput();
            RequireSentence(PagedDocumentText()==after && Editor.Document.Blocks.Count==2,
                "One Redo changed the following paragraph during a complete paragraph replacement in "+mode+".");
            report.AppendLine(mode+": caret insertion and selected-phrase replacement retained surrounding runs, destination formatting, picture anchors, right context and following paragraphs with one Undo/Redo.");
        }

        foreach(var spacing in new[]{
            (Prefix:"It was",Suffix:"",Text:"settled.",Expected:"It was settled."),
            (Prefix:"It was ",Suffix:"",Text:" settled.",Expected:"It was settled."),
            (Prefix:"It was",Suffix:", really.",Text:"settled",Expected:"It was settled, really."),
            (Prefix:"It was ",Suffix:"!",Text:"settled",Expected:"It was settled!")})
        {
            var paragraph=new Paragraph(new Run(spacing.Prefix+spacing.Suffix));
            SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            Editor.CaretPosition=SentenceTextPointer(paragraph,spacing.Prefix.Length);
            ApplySentenceCompletionSuggestion(CaptureSentenceCompletionTarget(),spacing.Text);
            RequireSentence(new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text==spacing.Expected,
                "Sentence insertion introduced missing/doubled spaces or spacing before punctuation: "+spacing.Expected);
        }

        var firstHeading=new Paragraph(new Run("FIRST SECTION")){Tag=ParagraphStyleTagPrefix+"Heading 1"};
        var neighbour=new Paragraph(new Run("MARK_SAME_SECTION"));
        var anchor=new Paragraph(new Run("The committee "));
        var secondHeading=new Paragraph(new Run("SECOND SECTION")){Tag=ParagraphStyleTagPrefix+"Heading 1"};
        var end=new Paragraph(new Run("MARK_OTHER_SECTION_END"));
        var contextDocument=new FlowDocument(firstHeading);
        foreach(var block in new[]{neighbour,anchor,secondHeading,end})contextDocument.Blocks.Add(block);
        SentenceSetDocument(contextDocument,EditorViewMode.Continuous);
        Editor.CaretPosition=anchor.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var contextTarget=CaptureSentenceCompletionTarget();
        SentenceCompletionRequest Scope(CompletionContextScope scope)=>CreateSentenceCompletionRequest(contextTarget,scope,
            "Neutral",SentenceCompletionService.BuiltIns.Single(item=>item.Name=="Neutral").Instruction,
            CompletionStrength.Subtle,"Private test hint");
        var paragraphRequest=Scope(CompletionContextScope.Paragraph);
        var sectionRequest=Scope(CompletionContextScope.Section);
        var wholeRequest=Scope(CompletionContextScope.WholeDocument);
        RequireSentence(!paragraphRequest.WiderContext.Contains("MARK_SAME_SECTION",StringComparison.Ordinal) &&
            sectionRequest.WiderContext.Contains("MARK_SAME_SECTION",StringComparison.Ordinal) &&
            !sectionRequest.WiderContext.Contains("MARK_OTHER_SECTION_END",StringComparison.Ordinal) &&
            wholeRequest.WiderContext.Contains("MARK_OTHER_SECTION_END",StringComparison.Ordinal),
            "Paragraph, section and whole-document context scopes did not respect heading boundaries.");
        RequireSentence(wholeRequest.MeaningHint=="Private test hint" && wholeRequest.Strength==CompletionStrength.Subtle &&
            wholeRequest.Prefix==contextTarget.Prefix && wholeRequest.Suffix==contextTarget.Suffix,
            "Request creation lost the private hint, strength or captured sentence boundaries.");
        Editor.CaretPosition=neighbour.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        RequireSentence(IsSentenceCompletionTargetCurrent(contextTarget),"Caret movement invalidated an unchanged anchored target.");
        new TextRange(anchor.ContentStart,anchor.ContentStart).Text="Edited ";
        RequireSentence(!IsSentenceCompletionTargetCurrent(contextTarget),"Editing the source left its old completion target current.");
        var edited=PagedDocumentText();
        ExpectSentenceRejection(()=>ApplySentenceCompletionSuggestion(contextTarget,"stale result"),"An edited target accepted a stale result.");
        RequireSentence(PagedDocumentText()==edited,"Rejected stale application changed the document.");

        Editor.CaretPosition=anchor.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var oldDocumentTarget=CaptureSentenceCompletionTarget();
        dirty=false;New_Click(this,new RoutedEventArgs());
        RequireSentence(!IsSentenceCompletionTargetCurrent(oldDocumentTarget),"New Document kept a previous document's target current.");
        ExpectSentenceRejection(()=>ApplySentenceCompletionSuggestion(oldDocumentTarget,"obsolete document result"),
            "A result from the previous document was inserted into New Document.");
        RequireSentence(string.IsNullOrWhiteSpace(PagedDocumentText()),"A stale result wrote into the new document.");

        anchor=new Paragraph(new Run("Read-only source "));
        SentenceSetDocument(new FlowDocument(anchor),EditorViewMode.Continuous);
        Editor.CaretPosition=anchor.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var readOnlyTarget=CaptureSentenceCompletionTarget();
        Editor.IsReadOnly=true;
        ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"Read-only source allowed target capture.");
        var readOnlyBefore=PagedDocumentText();
        ExpectSentenceRejection(()=>ApplySentenceCompletionSuggestion(readOnlyTarget,"changed"),"Read-only source accepted a suggestion.");
        RequireSentence(PagedDocumentText()==readOnlyBefore,"Read-only application modified source text.");
        Editor.IsReadOnly=false;
        Editor.CaretPosition=SentenceTextPointer(anchor,4);
        ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"A caret splitting a word was accepted without a word selection.");
        var displayed=new Run("Displayed field"){Tag=FieldTagPrefix+"REGRESSION_FIELD"};
        var protectedParagraph=new Paragraph(new Run("Before "));
        protectedParagraph.Inlines.Add(displayed);protectedParagraph.Inlines.Add(new Run(" after."));
        SentenceSetDocument(new FlowDocument(protectedParagraph),EditorViewMode.Continuous);
        foreach(var boundary in new[]{displayed.ContentStart,displayed.ContentEnd})
        {
            Editor.CaretPosition=boundary;
            ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"A displayed-field endpoint accepted ordinary prose insertion.");
        }
        protectedParagraph=new Paragraph(new Run("Before {field:NAME} after."));
        SentenceSetDocument(new FlowDocument(protectedParagraph),EditorViewMode.Continuous);
        Editor.CaretPosition=SentenceTextPointer(protectedParagraph,"Before {field:".Length);
        ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"A caret inside a field token was accepted.");
        var ordinary=new Run("Ordinary words");
        protectedParagraph=new Paragraph(new InlineUIContainer(new Image{Width=8,Height=8}));
        protectedParagraph.Inlines.Add(ordinary);
        SentenceSetDocument(new FlowDocument(protectedParagraph),EditorViewMode.Continuous);
        Editor.CaretPosition=ordinary.ContentStart;
        RequireSentence(!CaptureSentenceCompletionTarget().IsReplacement,"Ordinary text adjacent to an image was incorrectly blocked.");
        Editor.Selection.Select(protectedParagraph.ContentStart,protectedParagraph.ContentEnd);
        ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"A replacement selection spanning an inline picture was accepted.");
        protectedParagraph=new Paragraph(new Run(new string('x',601)));
        SentenceSetDocument(new FlowDocument(protectedParagraph),EditorViewMode.Continuous);
        Editor.Selection.Select(protectedParagraph.ContentStart,protectedParagraph.ContentEnd);
        ExpectSentenceRejection(()=>CaptureSentenceCompletionTarget(),"An oversized phrase replacement was accepted.");
        report.AppendLine("Spacing and punctuation remained exact. Paragraph/heading-section/whole-document scopes retained their intended boundaries. Caret movement retained anchors; edits, New Document and read-only state rejected stale or unsuitable insertion.");
    }

    void SentenceSetDocument(FlowDocument document,EditorViewMode mode)
    {
        sentenceCompletionWindow?.Close();
        ClearFloatingImagePlacements();
        Editor.IsReadOnly=false;
        Editor.Document=document;
        ApplyPage();ApplyBeta35ViewMode(mode,false);
        Editor.Focus();PumpInput();
        ResetSentenceUndo();
    }

    void ResetSentenceUndo()
    {
        Editor.IsUndoEnabled=false;Editor.IsUndoEnabled=true;
    }

    void SentenceSelectText(Paragraph paragraph,string text)
    {
        var source=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text;
        var index=source.IndexOf(text,StringComparison.Ordinal);
        RequireSentence(index>=0,"The regression selection text was not present.");
        Editor.Selection.Select(SentenceTextPointer(paragraph,index),SentenceTextPointer(paragraph,index+text.Length));
    }

    static TextRange SentenceFindText(Paragraph paragraph,string text)
    {
        var source=new TextRange(paragraph.ContentStart,paragraph.ContentEnd).Text;
        var index=source.IndexOf(text,StringComparison.Ordinal);
        RequireSentence(index>=0,"The inserted regression wording was not present: "+text);
        return new TextRange(SentenceTextPointer(paragraph,index),SentenceTextPointer(paragraph,index+text.Length));
    }

    static TextPointer SentenceTextPointer(Paragraph paragraph,int textOffset)
    {
        var pointer=paragraph.ContentStart;
        while(pointer!=null && pointer.CompareTo(paragraph.ContentEnd)<=0)
        {
            if(pointer.GetPointerContext(LogicalDirection.Forward)==TextPointerContext.Text)
            {
                var text=pointer.GetTextInRun(LogicalDirection.Forward);
                // Measure in the same plain-text coordinates as TextRange.Text;
                // embedded objects and line breaks can contribute characters.
                var preceding=new TextRange(paragraph.ContentStart,pointer).Text.Length;
                if(textOffset>=preceding && textOffset<=preceding+text.Length)
                    return pointer.GetPositionAtOffset(textOffset-preceding,LogicalDirection.Forward)!;
            }
            pointer=pointer.GetNextContextPosition(LogicalDirection.Forward);
        }
        throw new InvalidOperationException("A regression text offset exceeded its paragraph.");
    }

    void VerifySentenceCompletionWindow(StringBuilder report)
    {
        var providerCalls=0;
        var providerPrompts=new List<string>();
        SentenceCompletionService.GeneratorOverride=async (prompt,token)=>
        {
            providerCalls++;providerPrompts.Add(prompt);
            await Task.Delay(180,token);
            return SentenceFixtureJson();
        };
        foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
        {
            var paragraph=new Paragraph(new Run("The committee , as predicted."));
            var document=new FlowDocument(paragraph);document.Blocks.Add(new Paragraph(new Run("UI FOLLOWING CONTEXT.")));
            SentenceSetDocument(document,mode);
            Editor.CaretPosition=SentenceTextPointer(paragraph,"The committee ".Length);
            Editor.Focus();PumpInput();
            if(mode==EditorViewMode.Continuous)
            {
                var review=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedReviewMenu");
                var finish=review.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedFinishSentence");
                finish.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                var menu=new ContextMenu{PlacementTarget=Editor};
                AddMacroSubmenu(menu);menu.IsOpen=true;PumpInput();
                var finish=menu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedFinishSentence");
                finish.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen=false;
            }
            var dialog=sentenceCompletionWindow??throw new InvalidOperationException("The Finish Sentence menu action opened no window.");
            RequireSentence(dialog.Owner==this && dialog.IsVisible,"Finish Sentence did not open an owned, visible review window.");
            var inflections=SentenceControl<ListBox>(dialog,"CompletionInflections");
            inflections.SelectedItem=inflections.Items.OfType<CompletionInflection>().Single(item=>item.Name=="Cynical");
            SentenceControl<ComboBox>(dialog,"CompletionStrength").SelectedItem=CompletionStrength.Balanced;
            SentenceControl<ComboBox>(dialog,"CompletionContext").SelectedItem=CompletionContextScope.Paragraph;
            var hint=SentenceControl<TextBox>(dialog,"CompletionHint");
            hint.Text="Approve the proposal without changing the factual decision.";
            var generate=SentenceControl<Button>(dialog,"CompletionGenerate");
            var candidates=SentenceControl<ListBox>(dialog,"CompletionSuggestions");
            var edit=SentenceControl<TextBox>(dialog,"CompletionEdit");
            var insert=SentenceControl<Button>(dialog,"CompletionInsert");
            RequireSentence(candidates.Items.Count==0 && !insert.IsEnabled && providerCalls==(mode==EditorViewMode.Continuous?0:1),
                "Opening or configuring the review window generated or inserted text automatically.");
            var before=PagedDocumentText();
            var responsive=false;
            generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireSentence(!generate.IsEnabled && PagedDocumentText()==before,
                "Generation did not expose its pending state or changed source text before Insert.");
            Dispatcher.BeginInvoke(new Action(()=>responsive=true),System.Windows.Threading.DispatcherPriority.Input);
            WaitSentence(()=>responsive,"Input-priority work was blocked by sentence generation.");
            RequireSentence(PagedDocumentText()==before,"Generation changed source text before Insert.");
            WaitSentence(()=>candidates.Items.Count==3 && generate.IsEnabled,"The real window did not display three deterministic candidates.");
            RequireSentence(insert.IsEnabled && edit.IsEnabled,"Selecting a returned candidate did not enable editing and Insert.");
            foreach(var item in candidates.Items.OfType<ListBoxItem>())
            {
                var panel=item.Content as StackPanel??throw new InvalidOperationException("A suggestion has no readable preview panel.");
                var preview=panel.Children.OfType<TextBlock>().First();
                var runs=preview.Inlines.OfType<Run>().ToArray();
                RequireSentence(runs.Length==3 && runs.Count(run=>run.Background!=null)==1 &&
                    runs[0].Text=="The committee " && runs[1].Text==(item.Tag as string) && runs[2].Text==", as predicted." &&
                    runs[1].Background!=null && runs[0].Background==null && runs[2].Background==null,
                    "A candidate preview failed to highlight only the proposed inserted words.");
            }
            edit.Text="quietly approved the proposal";
            var editedPreview=SentenceControl<TextBlock>(dialog,"CompletionPreview").Inlines.OfType<Run>().ToArray();
            RequireSentence(editedPreview.Length==3 && editedPreview[1].Text==edit.Text && editedPreview[1].Background!=null,
                "Editing the selected candidate did not refresh its highlighted full-sentence preview.");
            if(mode==EditorViewMode.Paged)
            {
                dialog.UpdateLayout();
                var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(dialog.ActualWidth)),
                    Math.Max(1,(int)Math.Ceiling(dialog.ActualHeight)),96,96,PixelFormats.Pbgra32);
                image.Render(dialog);image.Freeze();
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using var output=File.Create(Path.Combine(Path.GetTempPath(),"CtrlZed48SentenceCompletion.png"));encoder.Save(output);
            }
            ResetSentenceUndo();
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
            var after=before.Replace("The committee , as predicted.","The committee quietly approved the proposal, as predicted.",StringComparison.Ordinal);
            RequireSentence(PagedDocumentText()==after && candidates.Items.Count==0 && !insert.IsEnabled,
                "Insert did not apply the edited candidate once and invalidate its old alternatives in "+mode+".");
            Editor.Undo();PumpInput();
            RequireSentence(PagedDocumentText()==before && Editor.CanRedo,"One Undo did not restore the real-window insertion in "+mode+".");
            Editor.Redo();PumpInput();
            RequireSentence(PagedDocumentText()==after,"One Redo did not restore the real-window insertion in "+mode+".");
            dialog.Close();PumpInput();
            RequireSentence(sentenceCompletionWindow==null,"Closing the review window retained its live window reference.");
        }
        report.AppendLine("Review and the editor context-menu action opened the real owned Finish Sentence window. In Continuous and Paged views, delayed deterministic generation kept dispatcher input responsive, displayed exactly three full-sentence previews with only new words highlighted, and applied edited wording with one Undo/Redo. Synthetic screenshot: CtrlZed48SentenceCompletion.png.");

        var customParagraph=new Paragraph(new Run("The committee , as predicted."));
        SentenceSetDocument(new FlowDocument(customParagraph),EditorViewMode.Continuous);
        Editor.CaretPosition=SentenceTextPointer(customParagraph,"The committee ".Length);
        ShowSentenceCompletion("Custom");
        var customWindow=sentenceCompletionWindow!;
        SentenceControl<TextBox>(customWindow,"CompletionCustomName").Text="Regression restrained voice";
        SentenceControl<TextBox>(customWindow,"CompletionCustomInstruction").Text="Use precise, restrained understatement while preserving the stated decision.";
        var save=SentenceControl<Button>(customWindow,"CompletionSaveCustom");
        RequireSentence(save.IsEnabled,"The named custom inflection could not be saved.");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SentenceControl<ComboBox>(customWindow,"CompletionStrength").SelectedItem=CompletionStrength.Bold;
        SentenceControl<ComboBox>(customWindow,"CompletionContext").SelectedItem=CompletionContextScope.WholeDocument;
        const string privateHint="HINT_NOT_FOR_DISK_48: the approval is already agreed.";
        SentenceControl<TextBox>(customWindow,"CompletionHint").Text=privateHint;
        SentenceControl<Button>(customWindow,"CompletionGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitSentence(()=>SentenceControl<ListBox>(customWindow,"CompletionSuggestions").Items.Count==3,
            "A saved custom inflection did not generate candidates.");
        RequireSentence(providerPrompts.Last().Contains(privateHint,StringComparison.Ordinal) &&
            providerPrompts.Last().Contains("Use precise, restrained understatement",StringComparison.Ordinal),
            "The private meaning hint or saved custom instruction was omitted from the generated request.");
        customWindow.Close();PumpInput();
        var persisted=File.ReadAllText(SentenceCompletionPreferencesStore.FilePath);
        RequireSentence(!persisted.Contains(privateHint,StringComparison.Ordinal) && !persisted.Contains("The committee",StringComparison.Ordinal),
            "The UI saved a one-off meaning hint or document prose with its preferences.");
        ShowSentenceCompletion();customWindow=sentenceCompletionWindow!;
        RequireSentence((SentenceControl<ListBox>(customWindow,"CompletionInflections").SelectedItem as CompletionInflection)?.Name=="Regression restrained voice" &&
            Equals(SentenceControl<ComboBox>(customWindow,"CompletionStrength").SelectedItem,CompletionStrength.Bold) &&
            Equals(SentenceControl<ComboBox>(customWindow,"CompletionContext").SelectedItem,CompletionContextScope.WholeDocument) &&
            SentenceControl<TextBox>(customWindow,"CompletionHint").Text.Length==0,
            "Reopening did not restore custom inflection/strength/context privately, with a fresh empty meaning hint.");
        customWindow.Close();PumpInput();

        languageSettings.AllowAiLanguageServices=false;
        ShowSentenceCompletion("Neutral");
        var disabled=sentenceCompletionWindow!;
        RequireSentence(!SentenceControl<Button>(disabled,"CompletionGenerate").IsEnabled &&
            SentenceControl<TextBlock>(disabled,"CompletionStatus").Text.Contains("Enable AI",StringComparison.OrdinalIgnoreCase),
            "The explicit AI-language-service setting did not gate generation with an actionable explanation.");
        disabled.Close();languageSettings.AllowAiLanguageServices=true;PumpInput();
        VerifySentenceCompletionStaleUi(report);
        VerifySentenceCompletionModeChanges(report);

        customParagraph=new Paragraph(new Run("Macro preview source "));
        SentenceSetDocument(new FlowDocument(customParagraph),EditorViewMode.Continuous);
        Editor.CaretPosition=customParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        dirty=false;
        var macroBefore=PagedDocumentText();
        var callsBefore=providerCalls;
        SentenceCompletionService.GeneratorOverride=(_,__)=>
        {
            providerCalls++;
            throw new InvalidOperationException("A preview-only macro requested generation.");
        };
        var legacyStart=SafeMacroLanguage.Reference.IndexOf("INSERT_TEXT text",StringComparison.Ordinal);
        RequireSentence(legacyStart>=0,"The safe-macro reference lost its legacy command reference.");
        var legacyStarter="# Supported commands:\n"+SafeMacroLanguage.Reference[legacyStart..].Replace("\r\n","\n")+"\nFINISH_SENTENCE Cynical";
        RequireSentence(SafeMacroLanguage.WithoutLegacyStarterReference(legacyStarter).Trim()=="FINISH_SENTENCE Cynical",
            "Starter help embedded by earlier releases was interpreted as executable macro commands.");
        AwaitSentenceTask(RunSafeMacroAsync("FINISH_SENTENCE Cynical"));
        RequireSentence(sentenceCompletionWindow!=null &&
            (SentenceControl<ListBox>(sentenceCompletionWindow,"CompletionInflections").SelectedItem as CompletionInflection)?.Name=="Cynical" &&
            PagedDocumentText()==macroBefore && !dirty && providerCalls==callsBefore,
            "FINISH_SENTENCE Cynical did not remain a preview-only safe macro with the requested inflection.");
        sentenceCompletionWindow!.Close();PumpInput();
        SentenceCompletionMacro.Show(Editor,"Cynical");
        RequireSentence(sentenceCompletionWindow!=null &&
            (SentenceControl<ListBox>(sentenceCompletionWindow,"CompletionInflections").SelectedItem as CompletionInflection)?.Name=="Cynical" &&
            PagedDocumentText()==macroBefore && !dirty && providerCalls==callsBefore,
            "The public compiled-macro API did not open the requested inflection without changing source or requesting a provider.");
        sentenceCompletionWindow!.Close();PumpInput();
        var originalCompiled=compiledMacrosEnabled;
        try
        {
            compiledMacrosEnabled=true;
            foreach(var macro in new[]{
                (Name:"sentence-preview.cs",Source:"using System.Windows.Controls; public static class PreviewMacro { public static void Run(RichTextBox Editor) { CtrlZed.SentenceCompletionMacro.Show(Editor, \"Cynical\"); } }"),
                (Name:"sentence-preview.vb",Source:"Option Explicit On\nImports System.Windows.Controls\nPublic Module PreviewMacro\n Public Sub Run(Editor As RichTextBox)\n  CtrlZed.SentenceCompletionMacro.Show(Editor, \"Cynical\")\n End Sub\nEnd Module")})
            {
                AwaitSentenceTask(RunMacroSourceAsync(macro.Name,macro.Source));PumpInput();
                RequireSentence(sentenceCompletionWindow is {IsActive:true} &&
                    (SentenceControl<ListBox>(sentenceCompletionWindow,"CompletionInflections").SelectedItem as CompletionInflection)?.Name=="Cynical" &&
                    PagedDocumentText()==macroBefore && !dirty && providerCalls==callsBefore,
                    "The compiled "+Path.GetExtension(macro.Name)+" wrapper generated text, dirtied the document or stole focus from the requested preview.");
                sentenceCompletionWindow!.Close();PumpInput();
            }
        }
        finally{compiledMacrosEnabled=originalCompiled;}
        report.AppendLine("The window saved and restored a named custom voice, strength and context while discarding the meaning hint. Explicit AI permission gated Generate. Safe and public compiled-macro entry points opened Cynical for review without requests, insertion or document dirtiness.");
    }

    void VerifySentenceCompletionStaleUi(StringBuilder report)
    {
        foreach(var change in new[]{"inflection","source","close"})
        {
            var paragraph=new Paragraph(new Run("The committee , as predicted."));
            SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            Editor.CaretPosition=SentenceTextPointer(paragraph,"The committee ".Length);
            var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=false;var returned=false;
            CancellationToken captured=default;
            SentenceCompletionService.GeneratorOverride=async (_,token)=>
            {
                captured=token;started=true;
                var value=await response.Task; // Deliberately ignores cancellation to test stale-result guards.
                returned=true;return value;
            };
            ShowSentenceCompletion("Cynical");
            var dialog=sentenceCompletionWindow!;
            var suggestions=SentenceControl<ListBox>(dialog,"CompletionSuggestions");
            SentenceControl<Button>(dialog,"CompletionGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>started,"The stale-result test request did not begin.");
            if(change=="inflection")
            {
                var inflections=SentenceControl<ListBox>(dialog,"CompletionInflections");
                inflections.SelectedItem=inflections.Items.OfType<CompletionInflection>().Single(item=>item.Name=="Sharp");
            }
            else if(change=="source")new TextRange(paragraph.ContentStart,paragraph.ContentStart).Text="Edited ";
            else dialog.Close();
            RequireSentence(captured.IsCancellationRequested,"Changing "+change+" did not cancel its pending generation.");
            var expected=PagedDocumentText();
            response.SetResult(SentenceFixtureJson(" STALE_"+change.ToUpperInvariant()));
            WaitSentence(()=>returned,"The deliberately non-cooperative test provider did not return.");
            PumpInput();
            RequireSentence(suggestions.Items.Count==0 && PagedDocumentText()==expected &&
                (change!="close" || sentenceCompletionWindow==null),
                "A late provider response repopulated candidates or changed source after "+change+".");
            if(change!="close")
            {
                RequireSentence(!SentenceControl<Button>(dialog,"CompletionInsert").IsEnabled,
                    "A stale generation left Insert enabled after "+change+".");
                dialog.Close();PumpInput();
            }
        }
        report.AppendLine("Inflection changes, source edits and window closure cancelled pending requests. A deliberately non-cooperative provider's late results were discarded without candidates, insertion or source changes.");
    }

    void VerifySentenceCompletionModeChanges(StringBuilder report)
    {
        var originalMode=languageSettings.WritingMode;
        try
        {
            var paragraph=new Paragraph(new Run("The committee , as predicted."));
            SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            Editor.CaretPosition=SentenceTextPointer(paragraph,"The committee ".Length);
            var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=false;var returned=false;
            SentenceCompletionService.GeneratorOverride=async (_,__)=>
            {
                started=true;
                var value=await response.Task; // Intentionally returns after a mode change, regardless of cancellation.
                returned=true;return value;
            };
            ShowSentenceCompletion("Cynical");
            var dialog=sentenceCompletionWindow!;
            var suggestions=SentenceControl<ListBox>(dialog,"CompletionSuggestions");
            SentenceControl<Button>(dialog,"CompletionGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>started,"The mode-change test request did not begin.");
            var before=PagedDocumentText();
            languageSettings.WritingMode=originalMode==WritingMode.Fiction?WritingMode.Business:WritingMode.Fiction;
            dialog.Activate();PumpInput();
            response.SetResult(SentenceFixtureJson(" STALE_MODE"));
            WaitSentence(()=>returned,"The deliberately non-cooperative mode-change provider did not return.");
            PumpInput();
            RequireSentence(suggestions.Items.Count==0 && PagedDocumentText()==before &&
                !SentenceControl<Button>(dialog,"CompletionInsert").IsEnabled,
                "A late result generated for an earlier writing mode remained selectable or changed the document.");
            dialog.Close();PumpInput();

            languageSettings.WritingMode=originalMode;
            SentenceCompletionService.GeneratorOverride=(_,token)=>
            {
                token.ThrowIfCancellationRequested();return Task.FromResult(SentenceFixtureJson());
            };
            Editor.CaretPosition=SentenceTextPointer(paragraph,"The committee ".Length);
            ShowSentenceCompletion("Cynical");
            dialog=sentenceCompletionWindow!;
            suggestions=SentenceControl<ListBox>(dialog,"CompletionSuggestions");
            SentenceControl<Button>(dialog,"CompletionGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>suggestions.Items.Count==3 && SentenceControl<Button>(dialog,"CompletionInsert").IsEnabled,
                "The ready mode-change test did not receive selectable candidates.");
            before=PagedDocumentText();
            languageSettings.WritingMode=originalMode==WritingMode.Business?WritingMode.Fiction:WritingMode.Business;
            // No activation or settings event occurs here: Insert must independently
            // compare its captured generation configuration with the current mode.
            SentenceControl<Button>(dialog,"CompletionInsert").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpInput();
            RequireSentence(PagedDocumentText()==before && suggestions.Items.Count==0 &&
                !SentenceControl<Button>(dialog,"CompletionInsert").IsEnabled,
                "Insert applied a ready candidate after the writing mode changed without an activation event.");
            dialog.Close();PumpInput();
            report.AppendLine("Changing writing mode discarded a pending non-cooperative provider result. Insert independently rejected ready candidates after a direct mode change, without relying on a window-activation or settings event.");
        }
        finally
        {
            sentenceCompletionWindow?.Close();
            languageSettings.WritingMode=originalMode;
        }
    }

    static T SentenceControl<T>(Window window,string name) where T:FrameworkElement
        =>window.FindName(name) as T??throw new InvalidOperationException("Finish Sentence is missing control "+name+".");

    static void RequireSentence(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }

    static void ExpectSentenceRejection(Action action,string message)
    {
        try{action();}
        catch(Exception error) when(error is InvalidDataException or JsonException or FormatException or ArgumentException or InvalidOperationException){return;}
        throw new InvalidOperationException(message);
    }

    static void ExpectSentenceCancellation(Action action)
    {
        try{action();}
        catch(OperationCanceledException){return;}
        throw new InvalidOperationException("A cancelled sentence-completion request returned a result.");
    }

    static void AwaitSentenceTask(Task task)
    {
        WaitSentence(()=>task.IsCompleted,"Sentence-completion work timed out.");
        task.GetAwaiter().GetResult();
    }

    static void WaitSentence(Func<bool> ready,string message)
    {
        var watch=Stopwatch.StartNew();
        while(!ready())
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(30))throw new InvalidOperationException(message);
            PumpInput();
        }
    }
}
