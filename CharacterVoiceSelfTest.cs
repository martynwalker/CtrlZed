using System.IO;
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
    // The transport below is deterministic: this gate exercises source capture,
    // real review windows and native Undo without making live model requests.
    internal static int RunCharacterVoiceSelfTest()
    {
        var report=new StringBuilder("Beta 51 Character Voice deterministic regression\r\n");
        var failures=new List<Exception>();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var directories=Directory.Exists(root)?Directory.GetDirectories(root,"*",SearchOption.AllDirectories)
            .Prepend(root).ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transport=CharacterVoiceService.GeneratorOverride;
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
            if(Directory.Exists(root))
                foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))originals[path]=File.ReadAllBytes(path);
            captured=true;
            clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(root);
            foreach(var path in Directory.GetFiles(root,"Recovery*"))File.Delete(path);
            CharacterVoiceService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected implicit Character Voice request.");
            Phase("catalogue aliases, strict rewrite contract and explicit transport",()=>VerifyCharacterVoiceContract(report));
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
            foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
            {
                var view=mode;
                Phase("rich source replacement, pasted insertion and one Undo/Redo in "+view,
                    ()=>window.VerifyCharacterVoiceTargets(view,report));
            }
            Phase("stale destinations, read-only, fields and embedded objects",()=>window.VerifyCharacterVoiceTargetGuards(report));
            Phase("real author and character search, editable review, Copy and Apply",()=>window.VerifyCharacterVoiceWindow(report));
            Phase("explicit author and character discovery without passage disclosure",()=>window.VerifyCharacterVoiceDiscoveryWindow(report));
            Phase("non-cooperative late provider results and changed configuration",()=>window.VerifyCharacterVoiceStaleWindow(report));
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("review and editor cleanup",()=>
            {
                window.characterVoiceWindow?.Close();window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.Close();
            });
            CharacterVoiceService.GeneratorOverride=transport;
            Application.Current.ShutdownMode=shutdown;
            if(captured)Phase("exact original application-data restoration",()=>
            {
                if(Directory.Exists(root))
                    foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
                        if(!originals.ContainsKey(path))File.Delete(path);
                foreach(var pair in originals)
                {Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);File.WriteAllBytes(pair.Key,pair.Value);}
                if(Directory.Exists(root))
                    foreach(var path in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Append(root).OrderByDescending(x=>x.Length))
                        if(!directories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originals)
                    RequireVoice(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An original preference file was not restored exactly.");
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>
            {if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();});
            report.AppendLine("Transport: deterministic fixture only; live model literary quality remains a user acceptance check.");
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed51CharacterVoice.txt"),report.ToString());
            if(failures.Count>0)File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),new AggregateException(failures).ToString());
        }
        return failures.Count==0?0:131;
    }

    static VoiceRewriteRequest VoiceFixtureRequest()
    {
        var author=CharacterVoiceService.SearchAuthors("Patrick O'").Single();
        var voice=CharacterVoiceService.SearchVoices(author.Id,"Maturin").Single();
        return new(author,voice,WritingModeProfiles.Get(WritingMode.PersonalLetter),
            "MARK_PRIVATE_SOURCE: The meeting is at 14:30 and costs £25.","Written","MARK_PRIVATE_HINT: keep the invitation warm.");
    }

    static void VerifyCharacterVoiceContract(StringBuilder report)
    {
        var request=VoiceFixtureRequest();
        var author=request.Author;
        RequireVoice(author.Name.Contains("Brian",StringComparison.Ordinal),"Partial author search did not find Patrick O'Brian.");
        var jack=CharacterVoiceService.SearchVoices(author.Id,"Jack").Single();
        RequireVoice(jack.Name.Contains("Aubrey",StringComparison.Ordinal) && request.Voice.Name.Contains("Maturin",StringComparison.Ordinal),
            "Character short names did not resolve to Aubrey and Maturin.");
        RequireVoice(CharacterVoiceService.SearchAuthors("Patrick O’").Any(x=>x.Id==author.Id),"Typographic apostrophe stopped local author search.");
        RequireVoice(CharacterVoiceService.SearchVoices("unrelated-author","Maturin").Count==0,"An unrelated author exposed Maturin.");
        var prompt=CharacterVoiceService.BuildRewritePrompt(request);
        foreach(var value in new[]{author.Name,request.Voice.Name,"MARK_PRIVATE_SOURCE","MARK_PRIVATE_HINT",request.Mode.DisplayName,"Written"})
            RequireVoice(prompt.Contains(value,StringComparison.Ordinal),"The rewrite prompt lost an explicit input: "+value);
        RequireVoice(prompt.Contains("British",StringComparison.OrdinalIgnoreCase),"The rewrite omitted British English.");
        const string rewritten="The invitation is for half past two; the cost remains £25.";
        RequireVoice(CharacterVoiceService.ParseRewriteResponse(JsonSerializer.Serialize(new{text=rewritten}))==rewritten,
            "A bounded original rewrite was not retained exactly.");
        RequireVoice(CharacterVoiceService.ParseRewriteResponse(JsonSerializer.Serialize(new{text="First paragraph.\nSecond paragraph."})).Contains("Second paragraph.",StringComparison.Ordinal),
            "A multi-paragraph rewrite was rejected.");
        foreach(var malformed in new[]{
            "```json\n{\"text\":\"rewritten\"}\n```", "{\"text\":\"rewritten\"} trailing prose",
            "{\"text\":\"\"}","{\"text\":\" \"}","{\"text\":17}","{\"text\":\"rewritten\",\"extra\":true}",
            "{\"text\":\"first\",\"text\":\"second\"}","{\"text\":\"bad\\u0000text\"}","{\"text\":\"bad\\ufffctext\"}",
            JsonSerializer.Serialize(new{text=new string('x',24001)})})
            RejectVoice(()=>CharacterVoiceService.ParseRewriteResponse(malformed),"An invalid model rewrite was accepted.");
        RejectVoice(()=>CharacterVoiceService.BuildRewritePrompt(request with{Source=new string('x',16001)}),"Oversized source was silently truncated or accepted.");
        RejectVoice(()=>CharacterVoiceService.BuildRewritePrompt(request with{Voice=jack with{AuthorId="different-author"}}),"A voice was used under the wrong author.");
        var calls=0;
        CharacterVoiceService.GeneratorOverride=(actual,token)=>
        {calls++;token.ThrowIfCancellationRequested();return Task.FromResult(JsonSerializer.Serialize(new{text=rewritten}));};
        var operation=CharacterVoiceService.RewriteAsync(request);AwaitSentenceTask(operation);
        RequireVoice(operation.Result==rewritten && calls==1,"Explicit rewriting did not use exactly one configured transport request.");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();
            try{AwaitSentenceTask(CharacterVoiceService.RewriteAsync(request,cancellation.Token));throw new InvalidOperationException("Pre-cancelled rewriting returned text.");}
            catch(OperationCanceledException){}
            RequireVoice(calls==1,"A pre-cancelled rewrite reached the provider.");
        }
        var lookupPrompts=new List<string>();
        CharacterVoiceService.GeneratorOverride=(actual,token)=>
        {
            token.ThrowIfCancellationRequested();lookupPrompts.Add(actual);
            return Task.FromResult(actual.Contains("author lookup",StringComparison.Ordinal)
                ?"{\"authors\":[{\"name\":\"Regression Author\",\"description\":\"Deterministic contract fixture\"}]}"
                :JsonSerializer.Serialize(new{authorId=author.Id,voices=new[]{new{name="Regression Character",work="Fixture work",aliases="Fixture",profile="Original dry, restrained trait notes."}}}));
        };
        var authorsTask=CharacterVoiceService.FindAuthorsAsync("Regression");AwaitSentenceTask(authorsTask);
        var voicesTask=CharacterVoiceService.FindVoicesAsync(author,"Regression");AwaitSentenceTask(voicesTask);
        RequireVoice(authorsTask.Result.Count==1 && authorsTask.Result[0].Description.Contains("AI",StringComparison.Ordinal) &&
            voicesTask.Result.Count==1 && voicesTask.Result[0].AuthorId==author.Id,
            "Discovery did not label generated author suggestions or retain the selected author binding.");
        RequireVoice(lookupPrompts.Count==2 && lookupPrompts.All(value=>!value.Contains("MARK_PRIVATE_SOURCE",StringComparison.Ordinal) &&
            !value.Contains("MARK_PRIVATE_HINT",StringComparison.Ordinal)),"Discovery exposed private rewrite source or its hint.");
        RejectVoice(()=>CharacterVoiceService.ParseVoicesResponse("{\"authorId\":\"different-author\",\"voices\":[]}",author),
            "Character discovery accepted a response for a different author.");
        RejectVoice(()=>CharacterVoiceService.ParseAuthorsResponse("{\"authors\":[{\"name\":\"One\",\"description\":\"a\"},{\"name\":\"ONE\",\"description\":\"b\"}]}"),
            "Discovery accepted duplicate author identities.");
        RejectVoice(()=>CharacterVoiceService.ParseAuthorsResponse("{\"authors\":[],\"extra\":true}"),"Discovery accepted an unexpected top-level property.");
        using(var cancellation=new CancellationTokenSource())
        {
            var late=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CharacterVoiceService.GeneratorOverride=(_,__)=>late.Task;
            var pending=CharacterVoiceService.RewriteAsync(request,cancellation.Token);
            cancellation.Cancel();late.SetResult(JsonSerializer.Serialize(new{text=rewritten}));
            try{AwaitSentenceTask(pending);throw new InvalidOperationException("A late cancelled service response returned usable text.");}
            catch(OperationCanceledException){}
        }
        report.AppendLine("Local author and short-character aliases resolved without transport; author binding and bounded strict JSON rejected malformed/extra/duplicate properties and oversized or embedded-object output. Explicit rewriting preserved the supplied mode, delivery and private passage/hint in one request.");
    }

    void VerifyCharacterVoiceTargets(EditorViewMode mode,StringBuilder report)
    {
        var lead=new Run("PREFIX "){FontStyle=FontStyles.Italic};
        var source=new Run("The plan "){FontWeight=FontWeights.Bold,Foreground=Brushes.Navy,FontSize=15};
        var mixed=new Run("is ready."){FontStyle=FontStyles.Italic,Foreground=Brushes.DarkRed,FontSize=12};
        var tail=new Run(" SUFFIX"){TextDecorations=TextDecorations.Underline,Foreground=Brushes.DarkGreen};
        var paragraph=new Paragraph(lead){Margin=new Thickness(9,7,11,13),TextAlignment=TextAlignment.Right};
        paragraph.Inlines.Add(source);paragraph.Inlines.Add(mixed);paragraph.Inlines.Add(tail);
        var following=new Paragraph(new Run("UNTOUCHED FOLLOWING PARAGRAPH."));
        var document=new FlowDocument(paragraph);document.Blocks.Add(following);
        SentenceSetDocument(document,mode);
        Editor.Selection.Select(source.ContentStart,mixed.ContentEnd);
        var target=CaptureCharacterVoiceTarget();
        var before=PagedDocumentText();ResetSentenceUndo();
        RequireVoice(target.IsReplacement && target.OriginalText=="The plan is ready.","The original selected source was not captured.");
        ApplyCharacterVoiceRewrite(target,"The proposal is prepared.");PumpInput();
        var after=before.Replace("The plan is ready.","The proposal is prepared.",StringComparison.Ordinal);
        RequireVoice(PagedDocumentText()==after,"Replacing selected prose changed surrounding text in "+mode+".");
        var inserted=SentenceFindText(paragraph,"The proposal is prepared.");
        RequireVoice(Equals(inserted.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold) &&
            inserted.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush ink && ink.Color==Colors.Navy &&
            Equals(inserted.GetPropertyValue(TextElement.FontSizeProperty),15d),"The replacement lost destination character formatting.");
        RequireVoice(Equals(SentenceFindText(paragraph,"PREFIX ").GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Italic) &&
            SentenceFindText(paragraph," SUFFIX").GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush tailInk && tailInk.Color==Colors.DarkGreen &&
            paragraph.TextAlignment==TextAlignment.Right && paragraph.Margin==new Thickness(9,7,11,13),
            "The replacement changed untouched formatting or paragraph layout.");
        RequireVoice(Editor.CanUndo,"A character rewrite produced no native Undo.");
        Editor.Undo();PumpInput();RequireVoice(PagedDocumentText()==before && Editor.CanRedo,"One Undo did not restore the complete rich source.");
        var restored=EnumerateDocumentParagraphs().First();
        RequireVoice(Equals(SentenceFindText(restored,"The plan ").GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold) &&
            SentenceFindText(restored,"The plan ").GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush restoredInk && restoredInk.Color==Colors.Navy &&
            Equals(SentenceFindText(restored,"is ready.").GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Italic) &&
            SentenceFindText(restored,"is ready.").GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush restoredSecondInk && restoredSecondInk.Color==Colors.DarkRed,
            "One Undo restored source text but lost its original mixed character formatting.");
        Editor.Redo();PumpInput();RequireVoice(PagedDocumentText()==after,"One Redo did not restore the rewrite.");

        var first=new Paragraph(new Run("First selected paragraph."));
        var second=new Paragraph(new Run("Second selected paragraph."));
        document=new FlowDocument(first);document.Blocks.Add(second);document.Blocks.Add(new Paragraph(new Run("KEEP AFTER MULTI-PARAGRAPH RANGE.")));
        SentenceSetDocument(document,mode);
        Editor.Selection.Select(first.ContentStart,second.ContentEnd);
        target=CaptureCharacterVoiceTarget();before=PagedDocumentText();ResetSentenceUndo();
        RequireVoice(target.OriginalText.Contains("First selected",StringComparison.Ordinal) && target.OriginalText.Contains("Second selected",StringComparison.Ordinal),
            "A multi-paragraph source was not captured completely.");
        ApplyCharacterVoiceRewrite(target,"First rewritten paragraph.\r\nSecond rewritten paragraph.");PumpInput();
        RequireVoice(PagedDocumentText().Contains("First rewritten paragraph.",StringComparison.Ordinal) &&
            PagedDocumentText().Contains("Second rewritten paragraph.",StringComparison.Ordinal) &&
            PagedDocumentText().Contains("KEEP AFTER MULTI-PARAGRAPH RANGE.",StringComparison.Ordinal),"A multi-paragraph rewrite lost source or following text.");
        Editor.Undo();PumpInput();RequireVoice(PagedDocumentText()==before,"One Undo did not restore a multi-paragraph selection.");

        var caretParagraph=new Paragraph(new Run("Insert here: "));
        SentenceSetDocument(new FlowDocument(caretParagraph),mode);
        Editor.CaretPosition=caretParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        target=CaptureCharacterVoiceTarget();before=PagedDocumentText();ResetSentenceUndo();
        RequireVoice(!target.IsReplacement,"An empty captured caret was treated as source replacement.");
        ApplyCharacterVoiceRewrite(target,"Pasted prose, rewritten.");PumpInput();
        RequireVoice(PagedDocumentText()==before.Replace("Insert here: ","Insert here: Pasted prose, rewritten.",StringComparison.Ordinal),
            "Pasted-source output did not insert at the captured caret.");
        Editor.Undo();PumpInput();RequireVoice(PagedDocumentText()==before,"One Undo did not restore pasted-source caret insertion.");
        var manual=new Paragraph(new Run("One manually separated paragraph.")){BreakPageBefore=true};
        SentenceSetDocument(new FlowDocument(manual),mode);
        Editor.Selection.Select(manual.ContentStart,manual.ContentEnd);target=CaptureCharacterVoiceTarget();before=PagedDocumentText();ResetSentenceUndo();
        ApplyCharacterVoiceRewrite(target,"First rewritten paragraph.\r\nSecond rewritten paragraph.");PumpInput();
        var resultingParagraphs=EnumerateDocumentParagraphs().ToArray();
        RequireVoice(resultingParagraphs.Length==2 && resultingParagraphs[0].BreakPageBefore && !resultingParagraphs[1].BreakPageBefore,
            "A manual page break was lost or duplicated into new rewritten paragraphs.");
        Editor.Undo();PumpInput();RequireVoice(PagedDocumentText()==before && EnumerateDocumentParagraphs().First().BreakPageBefore,
            "One Undo did not restore the manually separated source paragraph.");
        report.AppendLine(mode+": selected and pasted-source rewriting retained unselected prose, direct character formatting and paragraph layout; multi-paragraph selection was complete; native single Undo/Redo restored the rich source.");
    }

    void VerifyCharacterVoiceTargetGuards(StringBuilder report)
    {
        var paragraph=new Paragraph(new Run("Ordinary selected prose."));
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
        var target=CaptureCharacterVoiceTarget();
        Editor.CaretPosition=paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        RequireVoice(IsCharacterVoiceTargetCurrent(target),"Caret movement invalidated an unchanged captured replacement.");
        new TextRange(paragraph.ContentEnd,paragraph.ContentEnd).Text=" EDIT";
        RequireVoice(!IsCharacterVoiceTargetCurrent(target),"An edited source kept its old target current.");
        var before=PagedDocumentText();
        RejectVoice(()=>ApplyCharacterVoiceRewrite(target,"Obsolete result."),"Stale prose was overwritten.");
        RequireVoice(PagedDocumentText()==before,"Rejecting a stale result changed the document.");

        paragraph=new Paragraph(new Run("Formatting is part of the target."));
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);target=CaptureCharacterVoiceTarget();
        Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
        RequireVoice(!IsCharacterVoiceTargetCurrent(target),"A direct formatting edit left a stale target current.");

        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);target=CaptureCharacterVoiceTarget();
        Editor.IsReadOnly=true;before=PagedDocumentText();
        RejectVoice(()=>ApplyCharacterVoiceRewrite(target,"Read-only result."),"Read-only text accepted a rewrite.");
        RequireVoice(PagedDocumentText()==before,"Read-only rejection changed the source.");
        Editor.IsReadOnly=false;
        var field=new Run("DISPLAYED_FIELD"){Tag=FieldTagPrefix+"{name}"};
        paragraph=new Paragraph(field);SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(field.ContentStart,field.ContentEnd);
        RejectVoice(()=>CaptureCharacterVoiceTarget(),"A displayed field was treated as ordinary source prose.");
        paragraph=new Paragraph(new Run("Read {name} safely."));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        SentenceSelectText(paragraph,"{name}");RejectVoice(()=>CaptureCharacterVoiceTarget(),"A field token was accepted for destructive rewriting.");
        paragraph=new Paragraph(new Run("Before "));paragraph.Inlines.Add(new InlineUIContainer(new Button{Content="OBJECT"}));paragraph.Inlines.Add(new Run(" after."));
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
        RejectVoice(()=>CaptureCharacterVoiceTarget(),"A selection containing an embedded object was accepted.");
        paragraph=new Paragraph(new Run(new string('x',16001)));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
        RejectVoice(()=>CaptureCharacterVoiceTarget(),"An oversized selected source was silently truncated or accepted.");
        var cellParagraph=new Paragraph(new Run("Ordinary source in one table cell."));
        var otherCellParagraph=new Paragraph(new Run("The next cell must remain separate."));
        var row=new TableRow();row.Cells.Add(new TableCell(cellParagraph));row.Cells.Add(new TableCell(otherCellParagraph));
        var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);
        var tableDocument=new FlowDocument();tableDocument.Blocks.Add(table);
        SentenceSetDocument(tableDocument,EditorViewMode.Continuous);
        Editor.Selection.Select(cellParagraph.ContentStart,cellParagraph.ContentEnd);
        RequireVoice(CaptureCharacterVoiceTarget().IsReplacement,"Ordinary prose wholly inside one table cell was unnecessarily blocked.");
        Editor.Selection.Select(cellParagraph.ContentStart,otherCellParagraph.ContentEnd);
        RejectVoice(()=>CaptureCharacterVoiceTarget(),"A rewrite spanning separate table cells was allowed to destroy table structure.");
        report.AppendLine("Captured targets rejected changed prose/formatting, read-only application, displayed fields, field tokens, embedded objects and oversized selections without modifying source text.");
    }

    void VerifyCharacterVoiceWindow(StringBuilder report)
    {
        var prompts=new List<string>();
        CharacterVoiceService.GeneratorOverride=async(prompt,token)=>
        {prompts.Add(prompt);await Task.Delay(70,token);return JsonSerializer.Serialize(new{text="The arrangements are settled; we leave tomorrow."});};
        foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
        {
            var paragraph=new Paragraph(new Run("The plan is ready. We leave tomorrow."));
            SentenceSetDocument(new FlowDocument(paragraph),mode);
            Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
            var original=PagedDocumentText();var calls=prompts.Count;dirty=false;
            if(mode==EditorViewMode.Continuous)
            {
                var review=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedReviewMenu");
                review.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedCharacterVoice")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                var menu=new ContextMenu{PlacementTarget=Editor};AddMacroSubmenu(menu);menu.IsOpen=true;PumpInput();
                menu.Items.OfType<MenuItem>().Single(item=>(item.Tag as string)=="CtrlZedCharacterVoice")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
            }
            PumpInput();
            var dialog=characterVoiceWindow??throw new InvalidOperationException("Character Voice did not open.");
            RequireVoice(dialog.Owner==this && dialog.IsVisible,"Character Voice is not an owned visible review window.");
            ChooseVoiceFixture(dialog);
            var source=VoiceControl<TextBox>(dialog,"VoiceSource");
            RequireVoice(source.Text.Contains("The plan is ready.",StringComparison.Ordinal),"Opening the review did not capture selected source text.");
            RequireVoice(prompts.Count==calls && PagedDocumentText()==original && !dirty,"Opening or searching local author/character lists made an AI request or edited the document.");
            VoiceControl<ComboBox>(dialog,"VoiceDelivery").SelectedItem="Addressing crew";
            VoiceControl<TextBox>(dialog,"VoiceHint").Text="MARK_VOICE_HINT: firm, without a threat.";
            var generate=VoiceControl<Button>(dialog,"VoiceGenerate");
            RequireVoice(generate.IsEnabled,"An explicitly selected author, voice and source could not generate.");
            generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var responsive=false;Dispatcher.BeginInvoke(new Action(()=>responsive=true),System.Windows.Threading.DispatcherPriority.Input);
            WaitSentence(()=>responsive,"The provider request blocked dispatcher input.");
            RequireVoice(PagedDocumentText()==original,"Generating a preview changed the document.");
            WaitSentence(()=>VoiceControl<Button>(dialog,"VoiceApply").IsEnabled,"A completed voice rewrite did not become reviewable/applicable.");
            RequireVoice(prompts.Count==calls+1 && prompts.Last().Contains("MARK_VOICE_HINT",StringComparison.Ordinal) &&
                prompts.Last().Contains("Addressing crew",StringComparison.Ordinal),"Explicit generation omitted its source choices or made extra requests.");
            var result=VoiceControl<TextBox>(dialog,"VoiceResult");
            result.Text="The preparations are complete; we leave tomorrow.";
            VoiceControl<Button>(dialog,"VoiceCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            RequireVoice(Clipboard.GetText()==result.Text && PagedDocumentText()==original,"Copy did not copy the edited result without applying it.");
            if(mode==EditorViewMode.Paged)CaptureCharacterVoiceWindow(dialog);
            ResetSentenceUndo();VoiceControl<Button>(dialog,"VoiceApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
            RequireVoice(PagedDocumentText().Contains("The preparations are complete; we leave tomorrow.",StringComparison.Ordinal) &&
                !PagedDocumentText().Contains("The plan is ready.",StringComparison.Ordinal),"Apply did not use the edited review text at its captured destination.");
            Editor.Undo();PumpInput();RequireVoice(PagedDocumentText()==original && Editor.CanRedo,"One Undo did not restore real-window rewriting.");
            Editor.Redo();PumpInput();RequireVoice(PagedDocumentText().Contains("The preparations are complete; we leave tomorrow.",StringComparison.Ordinal),"One Redo did not restore real-window rewriting.");
            dialog.Close();PumpInput();RequireVoice(characterVoiceWindow==null,"Closing retained the live Character Voice window reference.");
        }

        var caretParagraph=new Paragraph(new Run("Destination: "));
        SentenceSetDocument(new FlowDocument(caretParagraph),EditorViewMode.Continuous);
        Editor.CaretPosition=caretParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        ShowCharacterVoice();var pasted=characterVoiceWindow!;ChooseVoiceFixture(pasted);
        VoiceControl<TextBox>(pasted,"VoiceSource").Text="PASTED SOURCE: The plan is ready. We leave tomorrow.";
        VoiceControl<Button>(pasted,"VoiceGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitSentence(()=>VoiceControl<Button>(pasted,"VoiceApply").IsEnabled,"A pasted passage could not generate for an empty captured caret.");
        RequireVoice(prompts.Last().Contains("PASTED SOURCE",StringComparison.Ordinal),"The pasted passage was not used as rewrite source.");
        VoiceControl<Button>(pasted,"VoiceApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireVoice(PagedDocumentText().StartsWith("Destination: The arrangements",StringComparison.Ordinal),"Pasted source overwrote the destination rather than inserting at its caret.");
        pasted.Close();PumpInput();
        report.AppendLine("Real Continuous and Paged review windows performed local partial author/alias search without requests, generated only explicitly, stayed responsive, allowed result editing/Copy and applied once with Undo/Redo. A pasted-source rewrite inserted at the captured caret. Synthetic screenshot: CtrlZed51CharacterVoice.png.");
    }

    void VerifyCharacterVoiceStaleWindow(StringBuilder report)
    {
        foreach(var change in new[]{"source","voice","profile","cancel","document","close","mode"})
        {
            var paragraph=new Paragraph(new Run("A selected passage for a delayed reply."));
            SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);
            var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=false;var returned=false;CancellationToken captured=default;
            CharacterVoiceService.GeneratorOverride=async(_,token)=>
            {captured=token;started=true;var value=await response.Task;returned=true;return value;};
            ShowCharacterVoice();var dialog=characterVoiceWindow!;ChooseVoiceFixture(dialog);
            VoiceControl<Button>(dialog,"VoiceGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitSentence(()=>started,"The delayed voice request did not begin.");
            var oldMode=languageSettings.WritingMode;
            switch(change)
            {
                case "source":VoiceControl<TextBox>(dialog,"VoiceSource").AppendText(" Changed.");break;
                case "voice":VoiceControl<TextBox>(dialog,"VoiceQuery").Text="Maturin";break;
                case "profile":VoiceControl<TextBox>(dialog,"VoiceProfile").AppendText(" More restraint.");break;
                case "cancel":VoiceControl<Button>(dialog,"VoiceCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;
                case "document":new TextRange(paragraph.ContentEnd,paragraph.ContentEnd).Text=" DOCUMENT EDIT";break;
                case "close":dialog.Close();break;
                case "mode":languageSettings.WritingMode=oldMode==WritingMode.Fiction?WritingMode.Business:WritingMode.Fiction;dialog.Activate();PumpInput();break;
            }
            var expected=PagedDocumentText();
            if(change!="mode")RequireVoice(captured.IsCancellationRequested,"Changing "+change+" did not cancel its provider request.");
            response.SetResult(JsonSerializer.Serialize(new{text="LATE RESULT MUST NOT APPLY"}));
            WaitSentence(()=>returned,"A non-cooperative voice fixture did not return.");PumpInput();
            RequireVoice(VoiceControl<TextBox>(dialog,"VoiceResult").Text.Length==0 &&
                !VoiceControl<Button>(dialog,"VoiceApply").IsEnabled && PagedDocumentText()==expected,
                "A late result remained usable or changed the document after "+change+".");
            if(change!="close")dialog.Close();PumpInput();languageSettings.WritingMode=oldMode;
        }

        var sourceParagraph=new Paragraph(new Run("The ready response belongs to this writing mode."));
        SentenceSetDocument(new FlowDocument(sourceParagraph),EditorViewMode.Continuous);
        Editor.Selection.Select(sourceParagraph.ContentStart,sourceParagraph.ContentEnd);
        CharacterVoiceService.GeneratorOverride=(_,token)=>{token.ThrowIfCancellationRequested();return Task.FromResult(JsonSerializer.Serialize(new{text="READY RESULT MUST NOT APPLY"}));};
        ShowCharacterVoice();var ready=characterVoiceWindow!;ChooseVoiceFixture(ready);
        VoiceControl<Button>(ready,"VoiceGenerate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitSentence(()=>VoiceControl<Button>(ready,"VoiceApply").IsEnabled,"The ready stale-mode fixture did not complete.");
        var mode=languageSettings.WritingMode;var before=PagedDocumentText();
        languageSettings.WritingMode=mode==WritingMode.Business?WritingMode.Fiction:WritingMode.Business;
        VoiceControl<Button>(ready,"VoiceApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireVoice(PagedDocumentText()==before && !VoiceControl<Button>(ready,"VoiceApply").IsEnabled,
            "Apply accepted a ready response from a superseded writing mode without a settings event.");
        ready.Close();languageSettings.WritingMode=mode;
        languageSettings.AllowAiLanguageServices=false;
        ShowCharacterVoice();ready=characterVoiceWindow!;ChooseVoiceFixture(ready);
        RequireVoice(!VoiceControl<Button>(ready,"VoiceGenerate").IsEnabled && !VoiceControl<Button>(ready,"VoiceFindAuthor").IsEnabled &&
            !VoiceControl<Button>(ready,"VoiceFindCharacters").IsEnabled,"The AI-language-services opt-in did not gate rewriting and AI lookup.");
        ready.Close();languageSettings.AllowAiLanguageServices=true;PumpInput();
        report.AppendLine("Source/profile/voice edits, Cancel, document changes and Close cancelled requests; deliberately late replies were discarded. Pending and ready responses from a changed Writing Mode were unusable. AI-language-services opt-in gated rewriting and both lookup actions.");
    }

    void VerifyCharacterVoiceDiscoveryWindow(StringBuilder report)
    {
        const string authorReply="{\"authors\":[{\"name\":\"Regression Author\",\"description\":\"Deterministic search fixture\"}]}";
        var discovered=CharacterVoiceService.ParseAuthorsResponse(authorReply).Single();
        var prompts=new List<string>();
        CharacterVoiceService.GeneratorOverride=(prompt,token)=>
        {
            token.ThrowIfCancellationRequested();prompts.Add(prompt);
            return Task.FromResult(prompt.Contains("author lookup",StringComparison.Ordinal)?authorReply:
                JsonSerializer.Serialize(new{authorId=discovered.Id,voices=new[]{new{name="Regression Character",work="Fixture work",aliases="Fixture",profile="Precise and restrained; an original interpretation for review."}}}));
        };
        var paragraph=new Paragraph(new Run("DOCUMENT_SECRET_NOT_FOR_DISCOVERY"));
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.Selection.Select(paragraph.ContentStart,paragraph.ContentEnd);ShowCharacterVoice();
        var dialog=characterVoiceWindow!;
        VoiceControl<TextBox>(dialog,"VoiceHint").Text="HINT_SECRET_NOT_FOR_DISCOVERY";
        VoiceControl<TextBox>(dialog,"VoiceAuthorQuery").Text="Regression Author";
        RequireVoice(prompts.Count==0,"Author search keystrokes made a discovery request.");
        VoiceControl<Button>(dialog,"VoiceFindAuthor").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var authors=VoiceControl<ListBox>(dialog,"VoiceAuthors");
        WaitSentence(()=>authors.Items.OfType<VoiceAuthor>().Any(item=>item.Id==discovered.Id),"Explicit author discovery did not populate its results.");
        authors.SelectedItem=authors.Items.OfType<VoiceAuthor>().Single(item=>item.Id==discovered.Id);
        VoiceControl<TextBox>(dialog,"VoiceQuery").Text="Fixture";
        RequireVoice(prompts.Count==1,"Selecting an author or typing a character query made an implicit request.");
        VoiceControl<Button>(dialog,"VoiceFindCharacters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var voices=VoiceControl<ListBox>(dialog,"VoiceCharacters");
        WaitSentence(()=>voices.Items.OfType<CharacterVoice>().Any(item=>item.Name=="Regression Character"),"Explicit character discovery did not populate its results.");
        var found=voices.Items.OfType<CharacterVoice>().Single();voices.SelectedItem=found;
        RequireVoice(found.AuthorId==discovered.Id && VoiceControl<TextBox>(dialog,"VoiceProfile").Text.Contains("AI suggestion",StringComparison.Ordinal),
            "A discovered voice lost its author binding or its interpretive-profile label.");
        RequireVoice(prompts.Count==2 && prompts.All(prompt=>!prompt.Contains("DOCUMENT_SECRET_NOT_FOR_DISCOVERY",StringComparison.Ordinal) &&
            !prompt.Contains("HINT_SECRET_NOT_FOR_DISCOVERY",StringComparison.Ordinal)),"Author/character discovery disclosed source prose or a one-off hint.");
        VoiceControl<TextBox>(dialog,"VoiceAuthorQuery").Text="Patrick O'";
        authors.SelectedItem=authors.Items.OfType<VoiceAuthor>().Single();
        RequireVoice(voices.Items.OfType<CharacterVoice>().All(item=>item.AuthorId!=discovered.Id),"Switching authors retained another author's voice choices.");
        dialog.Close();PumpInput();
        report.AppendLine("The real Find author/Find voices buttons were the only discovery request triggers, labelled generated notes for review, bound characters to their selected author and sent neither document prose nor a private meaning hint.");
    }

    static void ChooseVoiceFixture(Window dialog)
    {
        VoiceControl<TextBox>(dialog,"VoiceAuthorQuery").Text="Patrick O'";
        var authors=VoiceControl<ListBox>(dialog,"VoiceAuthors");
        authors.SelectedItem=authors.Items.OfType<VoiceAuthor>().Single(item=>item.Name.Contains("Brian",StringComparison.Ordinal));
        VoiceControl<TextBox>(dialog,"VoiceQuery").Text="Jack";
        var voices=VoiceControl<ListBox>(dialog,"VoiceCharacters");
        voices.SelectedItem=voices.Items.OfType<CharacterVoice>().Single(item=>item.Name.Contains("Aubrey",StringComparison.Ordinal));
    }

    static T VoiceControl<T>(Window window,string name) where T:FrameworkElement
        =>window.FindName(name) as T??throw new InvalidOperationException("Character Voice is missing control "+name+".");

    static void CaptureCharacterVoiceWindow(Window dialog)
    {
        dialog.UpdateLayout();
        var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(dialog.ActualWidth)),Math.Max(1,(int)Math.Ceiling(dialog.ActualHeight)),96,96,PixelFormats.Pbgra32);
        image.Render(dialog);image.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var output=File.Create(Path.Combine(Path.GetTempPath(),"CtrlZed51CharacterVoice.png"));encoder.Save(output);
    }

    static void RequireVoice(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}

    static void RejectVoice(Action action,string message)
    {
        try{action();}
        catch(Exception error) when(error is InvalidDataException or JsonException or FormatException or ArgumentException or InvalidOperationException){return;}
        throw new InvalidOperationException(message);
    }
}
