using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CtrlZed;

public partial class MainWindow
{
    // Windows-only integration regression. All AI transport is replaced in
    // process; this never contacts a provider or downloads/installs a font.
    internal static int RunFontFinderSelfTest()
    {
        var report=new StringBuilder("Beta 49 Font Finder deterministic regression\r\n");
        var failures=new List<Exception>();
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var originalDirectories=Directory.Exists(root)
            ?Directory.GetDirectories(root,"*",SearchOption.AllDirectories).Prepend(root).ToHashSet(StringComparer.OrdinalIgnoreCase)
            :new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originalGenerator=FontFinderService.GeneratorOverride;
        var originalShutdown=Application.Current.ShutdownMode;
        MainWindow? window=null;
        IDataObject? clipboard=null;
        var clipboardCaptured=false;
        var snapshotCaptured=false;
        var result=127;
        void Phase(string name,Action action)
        {
            try{action();report.AppendLine("PASS: "+name+".");}
            catch(Exception error){failures.Add(error);report.AppendLine("FAIL: "+name+": "+error);}
        }
        try
        {
            if(Directory.Exists(root))
                foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
                    originals[path]=File.ReadAllBytes(path);
            snapshotCaptured=true;
            clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(root);
            // A user's recovery document must never become the test fixture.
            foreach(var path in Directory.GetFiles(root,"Recovery*"))File.Delete(path);
            FontFinderService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("Unexpected Font Finder provider request outside a deterministic transport phase.");
            Phase("synthetic catalogue, local queries and strict JSON",()=>VerifyFontFinderEngine(report));
            Phase("deterministic AI transport and ranked catalogue whitelist",()=>VerifyFontFinderTransport(report));
            FontFinderService.GeneratorOverride=(_,__)=>throw new InvalidOperationException("A local font search requested an AI provider.");
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Phase("real editor initialisation",()=>
            {
                window=new MainWindow{WindowState=WindowState.Maximized};
                window.Show();window.Activate();
                SetForegroundWindow(new WindowInteropHelper(window).Handle);
                PumpInput();
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.languageSettings.SpellingEnabled=false;
                window.languageSettings.ContextualSpellingSuggestions=false;
                window.languageSettings.GrammarAndStyleSuggestions=false;
                window.languageSettings.PoetryRhymeSuggestions=false;
                window.languageSettings.AllowAiLanguageServices=false;
                SpellCheck.SetIsEnabled(window.Editor,false);
                window.InstallFontFinderTargetTracking();
            });
            if(window is {IsLoaded:true})
            {
                var editor=window;
                Phase("raw WPF language-mismatch typing diagnostic at the fixture's exact word-start caret",()=>editor.VerifyFontFinderInteriorCaretControl(false,report));
                Phase("raw WPF matching-keyboard-language typing control at the exact word-start caret",()=>editor.VerifyFontFinderInteriorCaretControl(false,report,true));
                Phase("direct Font Finder API typing at the fixture's exact word-start caret",()=>editor.VerifyFontFinderInteriorCaretControl(true,report));
                Phase("inside-word future typing preserves existing text formatting and native Undo",()=>editor.VerifyFontFinderInsideWordCaret(report));
                foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
                {
                    var view=mode;
                    Phase("rich selection, caret and one Undo/Redo in "+view,()=>editor.VerifyFontFinderTargets(view,report));
                    Phase("real local Font Finder window in "+view,()=>editor.VerifyFontFinderLocalWindow(view,report));
                    Phase("real Font Finder focus and pending caret format in "+view,()=>editor.VerifyFontFinderCaretWindow(view,report));
                }
                Phase("stale, New document and read-only protection",()=>editor.VerifyFontFinderStaleTargets(report));
                foreach(var change in new[]{"caret movement","source edit","pending format"})
                {
                    var guard=change;
                    Phase("closed finder ignores an obsolete caret return after "+guard,()=>editor.VerifyFontFinderCaretReturnGuard(guard,report));
                }
                Phase("named style scope, aliases, stable identities and native persistence",()=>editor.VerifyFontFinderStyles(report));
                Phase("explicit AI permission and private deterministic UI request",()=>editor.VerifyFontFinderAiWindow(report));
                Phase("late query, writing-mode and closed-window responses",()=>editor.VerifyFontFinderLateResponses(report));
                // Keep screenshot capture independent: an earlier assertion must
                // not prevent the real UI from producing reviewable evidence.
                Phase("actual synthetic Font Finder screenshot",()=>editor.CaptureFontFinderScreenshot(report));
            }
            if(failures.Count==0)
            {
                report.AppendLine("PASS: all Font Finder regression phases completed.");
                result=0;
            }
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: regression setup: "+error);}
        finally
        {
            if(window!=null)
            {
                Phase("test window cleanup",()=>
                {
                    window.fontFinderWindow?.Close();window.sentenceCompletionWindow?.Close();
                    window.closingApproved=true;
                    window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                    window.grammarAiCancellation?.Cancel();
                    window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                    window.Close();
                });
            }
            FontFinderService.GeneratorOverride=originalGenerator;
            Application.Current.ShutdownMode=originalShutdown;
            if(snapshotCaptured)Phase("original application data restoration",()=>
            {
                if(Directory.Exists(root))
                    foreach(var path in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
                        if(!originals.ContainsKey(path))File.Delete(path);
                foreach(var pair in originals)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
                    File.WriteAllBytes(pair.Key,pair.Value);
                }
                if(Directory.Exists(root))
                    foreach(var path in Directory.GetDirectories(root,"*",SearchOption.AllDirectories)
                        .Append(root).OrderByDescending(path=>path.Length))
                        if(!originalDirectories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originals)
                    RequireFontFinder(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An application-data file did not restore exactly: "+Path.GetFileName(pair.Key));
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>
            {
                if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();
            });
            if(failures.Count>0)result=127;
            report.AppendLine("Provider: deterministic in-process fixtures only. No live AI quality, font licence, download or installation claim is made.");
            report.AppendLine("Exit code: "+result);
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed49FontFinder.txt"),report.ToString());
        }
        return result;
    }

    static IReadOnlyList<FontFinderFamily> FontFinderFixtureCatalogue()=>new FontFinderFamily[]
    {
        new("Arial","Arial",new[]{"Arial","Arial Localised"},new[]{"sans-serif","modern","neutral"}),
        new("Arial Narrow","Arial Narrow",new[]{"Arial Narrow","Arial Narrow Localised"},new[]{"sans-serif","condensed"}),
        new("Times New Roman","Times New Roman",new[]{"Times New Roman"},new[]{"serif","traditional","formal"}),
        new("Comic Sans MS","Comic Sans MS",new[]{"Comic Sans MS"},new[]{"handwriting","casual","playful"}),
        new("Cascadia Mono","Cascadia Mono",new[]{"Cascadia Mono"},new[]{"monospace","code","technical"}),
        new("Segoe UI","Segoe UI",new[]{"Segoe UI"},new[]{"sans-serif","humanist","readable"}),
        new("Bold Delight","Bold Delight",new[]{"Bold Delight","Famille Bold"},new[]{"display","decorative"}),
        new("Fixture Serif Eight","Fixture Serif Eight",Array.Empty<string>(),new[]{"serif"}),
        new("Fixture Sans Nine","Fixture Sans Nine",Array.Empty<string>(),new[]{"sans-serif"})
    };

    static string FontFinderResponse(params (string Source,bool? Bold,bool? Italic,string Reason)[] matches)
        =>JsonSerializer.Serialize(new{matches=matches.Select(item=>new{source=item.Source,bold=item.Bold,italic=item.Italic,reason=item.Reason})});

    static void VerifyFontFinderEngine(StringBuilder report)
    {
        var catalogue=FontFinderFixtureCatalogue();
        var mode=WritingModeProfiles.Get(WritingMode.Technical);
        var valid=FontFinderResponse(("Arial",null,null,"A neutral installed family."),("Cascadia Mono",true,false,"Monospaced technical text."));
        var parsed=FontFinderService.ParseResponse(valid,catalogue);
        RequireFontFinder(parsed.Count==2 && parsed[0].Source=="Arial" && parsed[0].Bold==null && parsed[0].Italic==null &&
            parsed[1].Bold==true && parsed[1].Italic==false,"A valid strict response lost canonical families or nullable emphasis.");
        RequireFontFinder(FontFinderService.ParseResponse("{\"matches\":[]}",catalogue).Count==0,"An honest empty result was rejected.");
        var nine=FontFinderResponse(catalogue.Select(item=>(item.Source,(bool?)null,(bool?)null,"Installed fixture.")).ToArray());
        RequireFontFinder(FontFinderService.ParseResponse(FontFinderResponse(catalogue.Take(8).Select(item=>(item.Source,(bool?)null,(bool?)null,"Installed fixture.")).ToArray()),catalogue).Count==8,
            "Eight distinct installed canonical sources were not accepted.");
        foreach(var invalid in new[]
        {
            "```json\n"+valid+"\n```",valid+" trailing prose","{not json}","[]",
            FontFinderResponse(("Never Installed 49",null,null,"Fabricated.")),
            FontFinderResponse(("Arial",null,null,"First."),("Arial",true,null,"Duplicate.")),nine,
            "{\"matches\":[{\"source\":\"Arial\",\"bold\":\"yes\",\"italic\":null,\"reason\":\"Wrong type.\"}]}",
            "{\"matches\":[{\"source\":\"Arial\",\"bold\":null,\"italic\":null}]}",
            "{\"matches\":[{\"source\":\"Arial\",\"bold\":null,\"italic\":null,\"reason\":\"\"}]}",
            valid.Replace("\"matches\":","\"extra\":true,\"matches\":",StringComparison.Ordinal),
            valid.Replace("\"reason\":","\"extra\":true,\"reason\":",StringComparison.Ordinal),
            valid.Replace("\"source\":\"Arial\"","\"source\":\"Arial\",\"source\":\"Segoe UI\"",StringComparison.Ordinal)
        })ExpectFontFinderRejection(()=>FontFinderService.ParseResponse(invalid,catalogue),"An invalid/fabricated/duplicate provider response was accepted: "+invalid[..Math.Min(100,invalid.Length)]);
        foreach(var query in new[]{"Arial","Arial Localised","Bold Delight","Famille Bold"})
        {
            var expected=query is "Bold Delight" or "Famille Bold"?"Bold Delight":"Arial";
            var matches=FontFinderService.SearchLocal(catalogue,query,mode);
            RequireFontFinder(matches.Count>0 && matches[0].Source==expected && matches[0].Exact &&
                matches[0].Bold==null && matches[0].Italic==null,"An exact family/alias query changed emphasis or failed: "+query);
        }
        var narrow=FontFinderService.SearchLocal(catalogue,"Arial Narrow Bold Italic",mode);
        RequireFontFinder(narrow.Count>0 && narrow[0].Source=="Arial Narrow" && narrow[0].Bold==true && narrow[0].Italic==true,
            "The longest installed family was not separated from requested Bold Italic.");
        var bold=FontFinderService.SearchLocal(catalogue,"Arial Bold",mode);
        RequireFontFinder(bold.Count>0 && bold[0].Source=="Arial" && bold[0].Bold==true,
            "Arial Bold did not resolve to its installed family with explicit bold.");
        var regular=FontFinderService.SearchLocal(catalogue,"Arial Regular",mode);
        RequireFontFinder(regular.Count>0 && regular[0].Bold==false && regular[0].Italic==false,"Regular did not explicitly request normal emphasis.");
        var intrinsic=FontFinderService.SearchLocal(catalogue,"please use Bold Delight for a heading",mode);
        RequireFontFinder(intrinsic.Count>0 && intrinsic.All(item=>item.Bold==null),"A family name containing Bold became an emphasis instruction inside a descriptive request.");
        foreach(var query in new[]{"a serif without italics","a serif not oblique"})
        {
            var matches=FontFinderService.SearchLocal(catalogue,query,mode);
            RequireFontFinder(matches.Count>0 && matches.All(item=>item.Italic==false),"An explicit negative slant request became italic: "+query);
        }
        var browse=FontFinderService.SearchLocal(catalogue,"",mode);
        RequireFontFinder(browse.Count is >0 and <=8 && browse.Select(item=>item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()==browse.Count,
            "Empty local browsing did not return a bounded distinct installed catalogue.");
        RequireFontFinder(FontFinderService.SearchLocal(catalogue,"DefinitelyNotInstalled49",mode).Count==0,
            "An unknown literal family was presented as an installed exact match.");
        var natural=FontFinderService.SearchLocal(catalogue,"a monospaced font for source code",mode);
        RequireFontFinder(natural.Any(item=>item.Source=="Cascadia Mono") &&
            natural.All(item=>catalogue.Any(family=>family.Source==item.Source) && !string.IsNullOrWhiteSpace(item.Reason)),
            "A descriptive local query omitted its tagged family, invented a font or lacked an explanation.");
        report.AppendLine("Synthetic font names isolate parser/ranking tests from the machine's font installation. Exact aliases, families containing Bold, longest-family matching, explicit emphasis, empty browsing and unknown literals were checked.");
    }

    static void VerifyFontFinderTransport(StringBuilder report)
    {
        var original=FontFinderService.GeneratorOverride;
        try
        {
            var catalogue=FontFinderFixtureCatalogue();
            var mode=WritingModeProfiles.Get(WritingMode.Business);
            var calls=0;string? supplied=null;
            FontFinderService.GeneratorOverride=(prompt,token)=>
            {
                token.ThrowIfCancellationRequested();calls++;supplied=prompt;
                return Task.FromResult(FontFinderResponse(("Arial",null,null,"Deterministic installed match.")));
            };
            var task=FontFinderService.SuggestAsync(catalogue,"clear investor correspondence",mode,CancellationToken.None);
            AwaitFontFinderTask(task);
            RequireFontFinder(task.Result.Count==1 && task.Result[0].Source=="Arial" && calls==1 && supplied!=null &&
                supplied.Contains("clear investor correspondence",StringComparison.Ordinal) && supplied.Contains(mode.DisplayName,StringComparison.Ordinal),
                "Deterministic generation lost the query, writing profile or canonical response.");
            FontFinderService.GeneratorOverride=(_,token)=>
            {
                token.ThrowIfCancellationRequested();calls++;return Task.FromResult(FontFinderResponse(("Times New Roman",false,false,"Deterministic provider default emphasis.")));
            };
            var emphasis=FontFinderService.SuggestAsync(catalogue,"a traditional serif",mode,CancellationToken.None);AwaitFontFinderTask(emphasis);
            RequireFontFinder(emphasis.Result.Single().Bold==null && emphasis.Result.Single().Italic==null,
                "Model default false/false cleared emphasis when the description requested only a family.");
            using(var cancelled=new CancellationTokenSource())
            {
                var cancelledCalls=0;
                FontFinderService.GeneratorOverride=(_,token)=>
                {
                    cancelledCalls++;token.ThrowIfCancellationRequested();return Task.FromResult("{\"matches\":[]}");
                };
                cancelled.Cancel();
                try{AwaitFontFinderTask(FontFinderService.SuggestAsync(catalogue,"cancelled",mode,cancelled.Token));throw new InvalidOperationException("Pre-cancelled generation returned candidates.");}
                catch(OperationCanceledException){}
                RequireFontFinder(cancelledCalls==0,"A pre-cancelled request reached the provider transport.");
            }
            var large=Enumerable.Range(0,1200).Select(index=>new FontFinderFamily("Regression Installed Family "+index.ToString("D4"),
                "Regression Installed Family "+index.ToString("D4"),Array.Empty<string>(),new[]{index%2==0?"serif":"sans-serif"})).ToArray();
            var prompt=FontFinderService.BuildPrompt(large,"a readable serif for reports",mode);
            var omitted=large.FirstOrDefault(item=>!prompt.Contains(item.Source,StringComparison.Ordinal));
            RequireFontFinder(omitted!=null && prompt.Contains("subset",StringComparison.OrdinalIgnoreCase),
                "A large AI catalogue was neither bounded nor honestly labelled as a subset.");
            FontFinderService.GeneratorOverride=(_,token)=>
            {
                token.ThrowIfCancellationRequested();return Task.FromResult(FontFinderResponse((omitted!.Source,null,null,"Outside the advertised subset.")));
            };
            ExpectFontFinderRejection(()=>AwaitFontFinderTask(FontFinderService.SuggestAsync(large,"a readable serif for reports",mode,CancellationToken.None)),
                "AI accepted an installed family outside the subset it was actually offered.");
            report.AppendLine("An injected in-process transport exercised real prompt construction and strict response validation without credentials or cost. Large catalogues advertised a bounded subset, and a response outside that exact subset was rejected.");
        }
        finally{FontFinderService.GeneratorOverride=original;}
    }

    static (IReadOnlyList<FontFinderFamily> Catalogue,string Before,string After) FontFinderInstalledFixture()
    {
        var catalogue=FontFinderService.CaptureInstalledFonts();
        RequireFontFinder(catalogue.Count>0,"Windows returned no installed font families.");
        RequireFontFinder(catalogue.All(item=>!string.IsNullOrWhiteSpace(item.Source)) &&
            catalogue.Select(item=>item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count()==catalogue.Count,
            "Installed font capture contains empty or duplicate canonical sources.");
        var first=catalogue.FirstOrDefault(item=>item.Source.Equals("Arial",StringComparison.OrdinalIgnoreCase))??catalogue[0];
        var second=catalogue.FirstOrDefault(item=>!item.Source.Equals(first.Source,StringComparison.OrdinalIgnoreCase))??first;
        return(catalogue,first.Source,second.Source);
    }

    void VerifyFontFinderTargets(EditorViewMode mode,StringBuilder report)
    {
        fontFinderWindow?.Close();
        var installed=FontFinderInstalledFixture();
        var picture=new DrawingImage(new GeometryDrawing(Brushes.DarkSlateBlue,null,new RectangleGeometry(new Rect(0,0,15,10))));picture.Freeze();
        var image=new Image{Source=picture,Width=15,Height=10};
        var paragraph=new Paragraph{Margin=new Thickness(13,7,11,9),TextIndent=6,KeepWithNext=true};
        paragraph.Inlines.Add(new InlineUIContainer(image));
        paragraph.Inlines.Add(new Run("Prefix "){FontFamily=new FontFamily(installed.Before)});
        paragraph.Inlines.Add(new Run("format these words")
        {
            FontFamily=new FontFamily(installed.Before),FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,
            FontSize=23,Foreground=Brushes.Navy,TextDecorations=TextDecorations.Underline
        });
        paragraph.Inlines.Add(new Run(" tail."){FontFamily=new FontFamily(installed.Before)});
        var following=new Paragraph(new Run("UNRELATED PARAGRAPH MUST SURVIVE."){FontFamily=new FontFamily(installed.Before)}){Margin=new Thickness(4,3,2,1)};
        var document=new FlowDocument(paragraph);document.Blocks.Add(following);
        SentenceSetDocument(document,mode);
        SentenceSelectText(paragraph,"format these words");Editor.Focus();PumpInput();
        if(mode==EditorViewMode.Paged)FreshPagedLayout();
        var before=PagedDocumentText();
        var original=FontFinderCharacterState(SentenceFindText(paragraph,"format these words"));
        var unchanged=FontFinderCharacterState(SentenceFindText(paragraph,"Prefix "));
        var layout=FontFinderParagraphState(paragraph);
        var target=CaptureFontFinderTarget();
        RequireFontFinder(!target.IsCaret && target.Document==Editor.Document && target.SampleText.Contains("format these words",StringComparison.Ordinal) &&
            IsFontFinderTargetCurrent(target),"The selected font target did not capture its actual document/range.");
        ResetSentenceUndo();
        ApplyFontFinderSelection(target,new FontFinderMatch(installed.After,false,null,"Deterministic selected range."),installed.Catalogue);PumpInput();
        var changed=SentenceFindText(paragraph,"format these words");
        RequireFontFinder(FontFinderFamilyOf(changed)==installed.After && Equals(changed.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Normal) &&
            Equals(changed.GetPropertyValue(TextElement.FontStyleProperty),FontStyles.Italic),"Font apply lost explicit/kept emphasis in "+mode+".");
        RequireFontFinder(FontFinderCharacterState(changed,omitFamily:true,omitBold:true)==FontFinderCharacterStateFrom(original,omitFamily:true,omitBold:true),
            "Font apply changed size, colour, underline or kept italic in "+mode+".");
        RequireFontFinder(PagedDocumentText()==before && FontFinderParagraphState(paragraph)==layout && image.Source==picture &&
            paragraph.Inlines.OfType<InlineUIContainer>().Any(container=>ReferenceEquals(container.Child,image)) &&
            FontFinderCharacterState(SentenceFindText(paragraph,"Prefix "))==unchanged && FontFinderFamilyOf(new TextRange(following.ContentStart,following.ContentEnd))==installed.Before,
            "Font apply changed surrounding text, paragraph properties, pictures or unselected formatting in "+mode+".");
        Editor.Undo();PumpInput();
        RequireFontFinder(FontFinderCharacterState(SentenceFindText(paragraph,"format these words"))==original && PagedDocumentText()==before && Editor.CanRedo,
            "One Undo failed to restore the complete range formatting in "+mode+".");
        Editor.Redo();PumpInput();
        RequireFontFinder(FontFinderFamilyOf(SentenceFindText(paragraph,"format these words"))==installed.After &&
            Equals(SentenceFindText(paragraph,"format these words").GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Normal),
            "One Redo failed to restore font application in "+mode+".");

        var left=new Run("LEFT"){FontFamily=new FontFamily(installed.Before),FontWeight=FontWeights.Bold};
        var right=new Run("RIGHT"){FontFamily=new FontFamily(installed.Before),FontStyle=FontStyles.Italic};
        var caretParagraph=new Paragraph();caretParagraph.Inlines.Add(left);caretParagraph.Inlines.Add(right);
        SentenceSetDocument(new FlowDocument(caretParagraph),mode);
        // The insertion position deliberately sits at a run boundary. WPF's
        // actual active typing properties are the oracle, not either Run.
        Editor.CaretPosition=left.ContentEnd;Editor.Focus();PumpInput();
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,29d);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkMagenta);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,TextDecorations.Underline);
        var activeState=FontFinderCharacterState(Editor.Selection,omitFamily:true);
        var activeFamily=(Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source;
        var activeBold=Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
        var activeItalic=Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty);
        target=CaptureFontFinderTarget();
        RequireFontFinder(target.IsCaret && target.FamilySource==activeFamily &&
            target.Bold==Equals(activeBold,FontWeights.Bold) && target.Italic==Equals(activeItalic,FontStyles.Italic),
            "The empty-caret target substituted adjacent-run formatting for WPF's actual typing state in "+mode+".");
        before=PagedDocumentText();
        ApplyFontFinderSelection(target,new FontFinderMatch(installed.After,null,null,"Next typing family only."),installed.Catalogue);PumpInput();
        RequireFontFinder(PagedDocumentText()==before &&
            (Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source==installed.After &&
            Equals(Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty),activeBold) &&
            Equals(Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty),activeItalic),
            "Caret application changed text, lost emphasis or failed to set the next typing family in "+mode+".");
        RequireFontFinder(FontFinderCharacterState(Editor.Selection,omitFamily:true)==activeState,"Caret application lost pending size, colour or underline in "+mode+".");
        Editor.Focus();TypeInput("X");PumpInput();
        var typed=SentenceFindText(caretParagraph,"X");
        RequireFontFinder(FontFinderFamilyOf(typed)==installed.After && Equals(typed.GetPropertyValue(TextElement.FontWeightProperty),activeBold) &&
            Equals(typed.GetPropertyValue(TextElement.FontStyleProperty),activeItalic),"Actual typed text did not receive the caret font choice in "+mode+".");
        RequireFontFinder(FontFinderCharacterState(typed,omitFamily:true)==activeState,"Actual next typing lost pending size, colour or underline in "+mode+".");
        report.AppendLine(mode+": real installed families applied only to a rich selected range; one Undo/Redo restored family and emphasis while size, colour, underline, paragraphs and picture survived. An empty run-boundary caret used WPF's active format and the next typed character received it.");
    }

    void VerifyFontFinderStaleTargets(StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var match=new FontFinderMatch(installed.After,null,null,"Stale guard fixture.");
        foreach(var invalidation in new[]{"source edit","new document","read only","format edit"})
        {
            var paragraph=new Paragraph(new Run("Select this source text."));
            SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            SentenceSelectText(paragraph,"this source");var target=CaptureFontFinderTarget();
            if(invalidation=="source edit")new TextRange(paragraph.ContentStart,paragraph.ContentStart).Text="Changed ";
            else if(invalidation=="new document"){dirty=false;New_Click(this,new RoutedEventArgs());}
            else if(invalidation=="read only")Editor.IsReadOnly=true;
            else Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,27d);
            var before=PagedDocumentText();
            RequireFontFinder(!IsFontFinderTargetCurrent(target),"The old target remained current after "+invalidation+".");
            ExpectFontFinderRejection(()=>ApplyFontFinderSelection(target,match,installed.Catalogue),"Apply accepted a target after "+invalidation+".");
            RequireFontFinder(PagedDocumentText()==before,"Rejected application changed text after "+invalidation+".");
            Editor.IsReadOnly=false;
        }
        var safe=new Paragraph(new Run("Unknown font rejection"));SentenceSetDocument(new FlowDocument(safe),EditorViewMode.Continuous);
        SentenceSelectText(safe,"Unknown font");var current=CaptureFontFinderTarget();
        var state=FontFinderCharacterState(Editor.Selection);
        ExpectFontFinderRejection(()=>ApplyFontFinderSelection(current,new FontFinderMatch("Not Installed 49",null,null,"Unknown."),installed.Catalogue),
            "An uninstalled canonical source reached font application.");
        RequireFontFinder(FontFinderCharacterState(Editor.Selection)==state,"Rejected unknown font changed selection formatting.");
        Editor.CaretPosition=SentenceTextPointer(safe,4);Editor.Focus();PumpInput();
        current=CaptureFontFinderTarget();var caretText=PagedDocumentText();
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkOrange);
        RequireFontFinder(PagedDocumentText()==caretText && !IsFontFinderTargetCurrent(current),
            "A changed pending typing colour left the original empty-caret target current.");
        Editor.CaretPosition=SentenceTextPointer(safe,8);PumpInput();
        RequireFontFinder(!IsFontFinderTargetCurrent(current),"Moving the caret revived a target already invalidated by a pending-format change.");
        report.AppendLine("Source edits, New document, read-only mode, range formatting and pending caret-colour changes invalidated captured targets. Moving the caret could not revive an observed stale target. Unknown sources were blocked independently of UI selection.");
    }

    void VerifyFontFinderStyles(StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var set=CreateRelease45StyleFixture();set.Name="Font Finder regression 49";
        foreach(var style in set.Styles.Values)style.FontFamily=installed.Before;
        styleSets.Clear();styleSets[set.Name]=set;activeStyleSet=set.Name;
        RefreshStyleSetBox();RefreshParagraphStyleBox();
        var first=new Paragraph(new Run("FIRST MATCHING STYLE PARAGRAPH."));
        var second=new Paragraph(new Run("SECOND MATCHING STYLE PARAGRAPH."));
        var other=new Paragraph(new Run("UNRELATED NORMAL PARAGRAPH."));
        var document=new FlowDocument(first);document.Blocks.Add(second);document.Blocks.Add(other);
        SentenceSetDocument(document,EditorViewMode.Continuous);
        foreach(var pair in new[]{(first,"Book Body"),(second,"Body Note"),(other,"Normal")})
        {Editor.Selection.Select(pair.Item1.ContentStart,pair.Item1.ContentEnd);ApplyParagraphStyle(pair.Item2);}
        var firstRange=new TextRange(first.ContentStart,first.ContentEnd);
        firstRange.ApplyPropertyValue(TextElement.FontSizeProperty,31d);
        firstRange.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkCyan);
        firstRange.ApplyPropertyValue(Inline.TextDecorationsProperty,TextDecorations.Underline);
        var oldDefinition=StyleSetStore.Clone(set.Styles["Book Body"]);
        var aliases=JsonSerializer.Serialize(set.Aliases);
        var otherDefinition=JsonSerializer.Serialize(set.Styles["Normal"]);
        var oldFirst=FontFinderCharacterState(firstRange,omitFamily:true,omitBold:true);
        var oldOther=FontFinderCharacterState(new TextRange(other.ContentStart,other.ContentEnd));
        var firstLayout=FontFinderParagraphState(first);var text=PagedDocumentText();
        SentenceSelectText(first,"FIRST");var target=CaptureFontFinderTarget();
        RequireFontFinder(FontFinderStyleNames().Contains("Book Body") && FontFinderStyleNames().Contains("Section Lead"),"Named destination omitted custom styles.");
        ApplyFontFinderStyle(target,set.Name,"Body Note",new FontFinderMatch(installed.After,true,null,"Named style fixture."),installed.Catalogue);PumpInput();
        set=styleSets[set.Name];
        var definition=set.Styles["Book Body"];
        RequireFontFinder(definition.FontFamily==installed.After && definition.Bold && definition.Italic==oldDefinition.Italic &&
            definition.Id==oldDefinition.Id && JsonSerializer.Serialize(set.Aliases)==aliases && JsonSerializer.Serialize(set.Styles["Normal"])==otherDefinition,
            "Named style application lost its stable identity/aliases or changed unrelated definitions.");
        var expected=StyleSetStore.Clone(oldDefinition);expected.FontFamily=installed.After;expected.Bold=true;
        RequireFontFinder(JsonSerializer.Serialize(definition)==JsonSerializer.Serialize(expected),"Named style application changed size, colour, underline, following style or paragraph parameters.");
        foreach(var paragraph in new[]{first,second})
        {
            var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
            RequireFontFinder(FontFinderFamilyOf(range)==installed.After && Equals(range.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold),
                "A paragraph carrying the matching stable style identity missed the family/emphasis update.");
        }
        RequireFontFinder(FontFinderCharacterState(new TextRange(first.ContentStart,first.ContentEnd),omitFamily:true,omitBold:true)==oldFirst &&
            FontFinderCharacterState(new TextRange(other.ContentStart,other.ContentEnd))==oldOther && FontFinderParagraphState(first)==firstLayout && PagedDocumentText()==text,
            "Named style update changed direct formatting, unrelated paragraphs, paragraph layout or text.");
        var persisted=StyleSetStore.Load();
        RequireFontFinder(persisted.TryGetValue(set.Name,out var saved) && saved.Styles["Book Body"].FontFamily==installed.After &&
            saved.Styles["Book Body"].Id==oldDefinition.Id && StyleSetStore.ResolveStyleName(saved,"Body Note")=="Book Body", "The named style update/alias did not persist in the existing style-set store.");
        using var native=new MemoryStream();WriteCzdArchive(native);native.Position=0;
        var reopened=ReadCzdArchive(native);
        RequireFontFinder(reopened.Settings?.Styles?.Definitions.Styles["Book Body"].FontFamily==installed.After &&
            reopened.Settings.Styles.Definitions.Styles["Book Body"].Id==oldDefinition.Id &&
            StyleSetStore.ResolveStyleName(reopened.Settings.Styles.Definitions,"Body Note")=="Book Body", "Native CZD metadata lost the updated style, identity or alias.");
        Editor.Document=reopened.Document;ApplyRecoveredSettings(reopened.Settings!);PumpInput();
        var paragraphs=EnumerateDocumentParagraphs().ToArray();
        RequireFontFinder(paragraphs.Length==3 && PagedDocumentText()==text &&
            FontFinderFamilyOf(new TextRange(paragraphs[0].ContentStart,paragraphs[0].ContentEnd))==installed.After &&
            FontFinderFamilyOf(new TextRange(paragraphs[2].ContentStart,paragraphs[2].ContentEnd))==installed.Before,
            "Saving and reopening native CZD changed named-style scope or document text.");
        SentenceSelectText(paragraphs[0],"FIRST");target=CaptureFontFinderTarget();
        styleSets[activeStyleSet]=StyleSetStore.Clone(styleSets[activeStyleSet]);
        ExpectFontFinderRejection(()=>ApplyFontFinderStyle(target,activeStyleSet,"Book Body",new FontFinderMatch(installed.Before,null,null,"Stale set."),installed.Catalogue),
            "Replacing a style-set object with the same name did not invalidate named-style application.");
        report.AppendLine("A custom named style updated its existing definition and only matching document paragraphs, including a renamed alias. Stable IDs, aliases, following style, paragraph parameters and direct size/colour/underline survived. Existing style-set persistence and native CZD save/reopen retained the result. Global style definitions are outside WPF's native Undo guarantee.");
    }

    void VerifyFontFinderLocalWindow(EditorViewMode mode,StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var paragraph=new Paragraph(new Run("FONT FINDER LOCAL PREVIEW SOURCE.")
        {FontFamily=new FontFamily(installed.Before),FontSize=21,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,Foreground=Brushes.Navy,TextDecorations=TextDecorations.Underline});
        SentenceSetDocument(new FlowDocument(paragraph),mode);SentenceSelectText(paragraph,"LOCAL PREVIEW");
        var before=PagedDocumentText();var original=FontFinderCharacterState(Editor.Selection);dirty=false;
        var format=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Header as string)?.Replace("_","")=="Format");
        var action=format.Items.OfType<MenuItem>().SingleOrDefault(item=>Equals(item.Tag,"CtrlZedFindFont"));
        RequireFontFinder(action!=null,"Format does not expose the Find Font action.");
        action!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));PumpInput();
        var dialog=fontFinderWindow??throw new InvalidOperationException("Find Font did not open its real window.");
        RequireFontFinder(dialog.Owner==this && !ReferenceEquals(dialog,this),"Font Finder is not an owned independent review window.");
        var query=FontFinderControl<TextBox>(dialog,"FontFinderQuery");
        var results=FontFinderControl<ListBox>(dialog,"FontFinderResults");
        WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"Installed catalogue loading did not finish.");
        query.Text=installed.After;ClickFontFinder(dialog,"FontFinderSearch");
        WaitFontFinder(()=>results.Items.Count>0,"The real local search returned no installed exact match.");
        results.SelectedIndex=0;PumpInput();
        var selected=(results.SelectedItem as ListBoxItem)?.Tag as FontFinderMatch??throw new InvalidOperationException("A local result has no canonical match identity.");
        RequireFontFinder(selected.Source==installed.After,"The UI search did not rank the installed exact family first.");
        var sample=FontFinderControl<TextBox>(dialog,"FontFinderSample");
        RequireFontFinder(sample.FontFamily.Source==selected.Source,"The preview text is not actually rendered using its selected installed font family.");
        sample.Text="Editable preview only — CtrlZed Beta 49";
        FontFinderControl<Slider>(dialog,"FontFinderPreviewSize").Value=34;
        FontFinderChoose(dialog,"FontFinderBold","Keep existing");FontFinderChoose(dialog,"FontFinderItalic","Keep existing");PumpInput();
        RequireFontFinder(PagedDocumentText()==before && !dirty && FontFinderCharacterState(SentenceFindText(paragraph,"LOCAL PREVIEW"))==original,
            "Searching or editing the rendered preview changed the author document.");
        RequireFontFinder(!FontFinderControl<Button>(dialog,"FontFinderAskAi").IsEnabled,"AI generation was enabled without explicit AI permission.");
        ResetSentenceUndo();ClickFontFinder(dialog,"FontFinderApply");PumpInput();
        var changed=SentenceFindText(paragraph,"LOCAL PREVIEW");
        RequireFontFinder(FontFinderFamilyOf(changed)==installed.After && PagedDocumentText()==before &&
            FontFinderCharacterState(changed,omitFamily:true)==FontFinderCharacterStateFrom(original,omitFamily:true),
            "The real Apply button changed content or non-family formatting in "+mode+".");
        Editor.Undo();PumpInput();
        RequireFontFinder(FontFinderCharacterState(SentenceFindText(paragraph,"LOCAL PREVIEW"))==original,"One Undo failed for the actual UI application in "+mode+".");
        Editor.Redo();PumpInput();
        RequireFontFinder(FontFinderFamilyOf(SentenceFindText(paragraph,"LOCAL PREVIEW"))==installed.After,"One Redo failed for the actual UI application in "+mode+".");
        dialog.Close();PumpInput();RequireFontFinder(fontFinderWindow==null,"Closing Font Finder retained its live window reference.");
        report.AppendLine(mode+": Format opened the real owned window; local exact search rendered the selected installed family, editable sample and preview size without changing source. Apply retained emphasis/size/colour/underline and one Undo/Redo worked.");
    }

    void VerifyFontFinderAiWindow(StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        const string secret="DOCUMENT_PRIVATE_MARKER_49_NEVER_SEND";
        var paragraph=new Paragraph(new Run(secret));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Editor.CaretPosition=SentenceTextPointer(paragraph,8);
        var original=FontFinderService.GeneratorOverride;
        var calls=0;string? prompt=null;
        try
        {
            FontFinderService.GeneratorOverride=(value,token)=>
            {
                token.ThrowIfCancellationRequested();calls++;prompt=value;
                return Task.FromResult(FontFinderResponse((installed.After,null,null,"Deterministic installed UI recommendation.")));
            };
            languageSettings.AllowAiLanguageServices=false;ShowFontFinder();PumpInput();
            var dialog=fontFinderWindow!;
            WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"Permission-gate catalogue did not load.");
            RequireFontFinder(!FontFinderControl<Button>(dialog,"FontFinderAskAi").IsEnabled && calls==0,"An override bypassed the user's explicit AI permission gate.");
            ClickFontFinder(dialog,"FontFinderAskAi");PumpInput();
            RequireFontFinder(calls==0,"The AI click handler reached transport while permission was disabled.");
            dialog.Close();languageSettings.AllowAiLanguageServices=true;
            ShowFontFinder();dialog=fontFinderWindow!;PumpInput();
            FontFinderControl<TextBox>(dialog,"FontFinderQuery").Text="clear readable type for a report";
            WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderAskAi").IsEnabled,"The permitted deterministic AI action remained disabled.");
            var before=PagedDocumentText();
            ClickFontFinder(dialog,"FontFinderAskAi");
            var results=FontFinderControl<ListBox>(dialog,"FontFinderResults");
            WaitFontFinder(()=>calls==1 && results.Items.OfType<ListBoxItem>().Any(item=>(item.Tag as FontFinderMatch)?.Reason=="Deterministic installed UI recommendation."),
                "The permitted real UI did not show the deterministic AI match.");
            RequireFontFinder(prompt!=null && !prompt.Contains(secret,StringComparison.Ordinal) && PagedDocumentText()==before,
                "Font recommendation sent private document/sample text or changed the document before Apply.");
            dialog.Close();PumpInput();
            report.AppendLine("The real Ask AI action required explicit AI-language permission even with the injected transport. The permitted request sent query/profile/catalogue data, omitted the private author-document marker and displayed a deterministic installed match without editing the source.");
        }
        finally{fontFinderWindow?.Close();languageSettings.AllowAiLanguageServices=false;FontFinderService.GeneratorOverride=original;}
    }

    void VerifyFontFinderCaretWindow(EditorViewMode mode,StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var paragraph=new Paragraph(new Run("Typing fixture content."){FontFamily=new FontFamily(installed.Before)});
        SentenceSetDocument(new FlowDocument(paragraph),mode);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Editor.CaretPosition=SentenceTextPointer(paragraph,7);Editor.Focus();PumpInput();
        // These are pending WPF typing properties, deliberately distinct from
        // the adjacent Run. Opening the real modeless window must retain them.
        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily(installed.Before));
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,29d);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkMagenta);
        Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);
        Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,TextDecorations.Underline);
        var pending=FontFinderCharacterState(Editor.Selection,omitFamily:true);
        var before=PagedDocumentText();
        ShowFontFinder();var dialog=fontFinderWindow??throw new InvalidOperationException("The empty-caret Font Finder window did not open.");
        var query=FontFinderControl<TextBox>(dialog,"FontFinderQuery");
        dialog.Activate();query.Focus();PumpInput();
        RequireFontFinder(query.IsKeyboardFocusWithin,"The empty-caret regression did not transfer actual keyboard focus to the finder's query field.");
        WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"Empty-caret installed catalogue loading did not finish.");
        query.Text=installed.After;ClickFontFinder(dialog,"FontFinderSearch");
        var results=FontFinderControl<ListBox>(dialog,"FontFinderResults");
        WaitFontFinder(()=>results.Items.Count>0 && FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"The empty-caret real local search returned no match.");
        results.SelectedIndex=0;PumpInput();
        var apply=FontFinderControl<Button>(dialog,"FontFinderApply");
        RequireFontFinder(apply.IsEnabled,"Losing editor focus invalidated the captured pending caret format: "+FontFinderControl<TextBlock>(dialog,"FontFinderStatus").Text);
        RequireFontFinder(PagedDocumentText()==before,"Opening or searching the empty-caret finder changed author text.");
        ClickFontFinder(dialog,"FontFinderApply");PumpInput();
        RequireFontFinder(PagedDocumentText()==before &&
            (Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source==installed.After &&
            FontFinderCharacterState(Editor.Selection,omitFamily:true)==pending,
            "The actual Apply button lost the empty caret's pending size, colour, underline or emphasis after query focus in "+mode+".");
        report.AppendLine(mode+" caret UI diagnostic: expected family="+installed.After+"; expected non-family="+pending+
            "; after Apply="+FontFinderCharacterState(Editor.Selection)+"; editor keyboard focus="+Editor.IsKeyboardFocusWithin+".");
        RequireFontFinder(Editor.IsKeyboardFocusWithin,"The real caret Apply action did not return actual keyboard focus to the document in "+mode+".");
        CloseFontFinderThroughButton(dialog);
        report.AppendLine(mode+" caret UI diagnostic: after finder Close="+FontFinderCharacterState(Editor.Selection)+
            "; editor keyboard focus="+Editor.IsKeyboardFocusWithin+".");
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);
        report.AppendLine(mode+" caret UI diagnostic: after owner Activate="+FontFinderCharacterState(Editor.Selection)+
            "; editor keyboard focus="+Editor.IsKeyboardFocusWithin+".");
        Editor.Focus();PumpInput();
        report.AppendLine(mode+" caret UI diagnostic: after Editor.Focus="+FontFinderCharacterState(Editor.Selection)+
            "; editor keyboard focus="+Editor.IsKeyboardFocusWithin+".");
        TraceFontFinderInput(report,mode+" real UI after Close",()=>TypeInput("X"));PumpInput();
        var typed=SentenceFindText(paragraph,"X");
        report.AppendLine(mode+" caret UI diagnostic: typed X="+FontFinderCharacterState(typed)+
            "; current caret="+FontFinderCharacterState(Editor.Selection)+"; actual document="+JsonSerializer.Serialize(PagedDocumentText())+".");
        RequireFontFinder(FontFinderFamilyOf(typed)==installed.After && FontFinderCharacterState(typed,omitFamily:true)==pending,
            "Typing after closing the real finder lost its chosen family or captured pending size, colour, underline or emphasis in "+mode+
            ": expected="+installed.After+"|"+pending+"; actual="+FontFinderCharacterState(typed)+".");
        RequireFontFinder(PagedDocumentText()==before.Replace("Typing fixture","Typing Xfixture",StringComparison.Ordinal),
            "The real empty-caret font application changed anything beyond the explicitly typed test character in "+mode+".");
        report.AppendLine(mode+": an empty caret carried pending WPF size, colour, underline, bold and italic into the real finder. Keyboard focus moved to its query; local search and Apply retained that state, and an actual typed X received the selected installed family after the finder closed.");
    }

    void VerifyFontFinderInteriorCaretControl(bool useFinderApi,StringBuilder report,bool matchInputLanguage=false)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var keyboardLanguage=InputLanguageManager.Current.CurrentInputLanguage.IetfLanguageTag;
        var fixtureLanguage=matchInputLanguage?keyboardLanguage:keyboardLanguage.Equals("en-GB",StringComparison.OrdinalIgnoreCase)?"en-US":"en-GB";
        var paragraph=new Paragraph(new Run("Typing fixture content."){FontFamily=new FontFamily(installed.Before)})
        {Language=XmlLanguage.GetLanguage(fixtureLanguage)};
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Editor.CaretPosition=SentenceTextPointer(paragraph,7);Editor.Focus();PumpInput();
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,29d);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkMagenta);
        Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);
        Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,TextDecorations.Underline);
        if(useFinderApi)
        {
            var target=CaptureFontFinderTarget();
            ApplyFontFinderSelection(target,new FontFinderMatch(installed.After,null,null,"Exact-position direct API control."),installed.Catalogue);
        }
        else Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily(installed.After));
        PumpInput();
        var expected=FontFinderCharacterState(Editor.Selection);
        var before=PagedDocumentText();
        var label=useFinderApi?"Direct API word-start control":matchInputLanguage?"Raw WPF matching-language word-start control":"Raw WPF language-mismatch word-start diagnostic";
        report.AppendLine(label+" before native X: expected="+expected+"; keyboard focus="+Editor.IsKeyboardFocusWithin+".");
        report.AppendLine(label+" culture diagnostic: keyboard="+InputLanguageManager.Current.CurrentInputLanguage.IetfLanguageTag+
            "; editor="+Editor.Language.IetfLanguageTag+"; paragraph="+paragraph.Language.IetfLanguageTag+
            "; caret="+Editor.Selection.GetPropertyValue(FrameworkElement.LanguageProperty)+".");
        RequireFontFinder(Editor.IsKeyboardFocusWithin && PagedDocumentText()=="Typing fixture content.\r\n",
            label+" did not start at the real document caret with its unchanged source.");
        TraceFontFinderInput(report,label,()=>TypeInput("X"));PumpInput();
        var typed=SentenceFindText(paragraph,"X");
        report.AppendLine(label+" after native X: typed="+FontFinderCharacterState(typed)+"; caret="+FontFinderCharacterState(Editor.Selection)+
            "; document="+JsonSerializer.Serialize(PagedDocumentText())+".");
        RequireFontFinder(PagedDocumentText()==before.Replace("Typing fixture","Typing Xfixture",StringComparison.Ordinal),label+" did not insert the actual native character at its intended caret.");
        if(useFinderApi || matchInputLanguage)
            RequireFontFinder(FontFinderCharacterState(typed)==expected,
                label+" lost pending formatting: expected="+expected+"; actual="+FontFinderCharacterState(typed)+".");
        else report.AppendLine("Raw framework diagnostic only: keyboard/document language mismatch "+
            (FontFinderCharacterState(typed)==expected?"retained":"discarded")+" the pending character properties. Font Finder API/UI regressions independently require the intended formatting.");
    }

    void VerifyFontFinderInsideWordCaret(StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var paragraph=new Paragraph();
        paragraph.Inlines.Add(new Run("Typing "){FontFamily=new FontFamily(installed.Before)});
        paragraph.Inlines.Add(new Run("fixture")
        {
            FontFamily=new FontFamily(installed.Before),FontSize=29,FontWeight=FontWeights.Bold,
            FontStyle=FontStyles.Italic,Foreground=Brushes.DarkMagenta,TextDecorations=TextDecorations.Underline
        });
        paragraph.Inlines.Add(new Run(" content."){FontFamily=new FontFamily(installed.Before)});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Editor.CaretPosition=SentenceTextPointer(paragraph,10);Editor.Focus();PumpInput();
        var before=PagedDocumentText();
        var prefix=FontFinderCharacterState(SentenceFindText(paragraph,"Typing "));
        var word=FontFinderCharacterState(SentenceFindText(paragraph,"fixture"));
        var suffix=FontFinderCharacterState(SentenceFindText(paragraph," content."));
        var expected=installed.After+FontFinderCharacterState(Editor.Selection,omitFamily:true);
        var target=CaptureFontFinderTarget();
        RequireFontFinder(target.IsCaret,"The inside-word fixture did not capture an empty caret.");
        ApplyFontFinderSelection(target,new FontFinderMatch(installed.After,null,null,"Inside-word future typing only."),installed.Catalogue);PumpInput();
        RequireFontFinder(PagedDocumentText()==before && FontFinderCharacterState(SentenceFindText(paragraph,"Typing "))==prefix &&
            FontFinderCharacterState(SentenceFindText(paragraph,"fixture"))==word && FontFinderCharacterState(SentenceFindText(paragraph," content."))==suffix,
            "Applying a future-typing font inside a word changed existing text formatting before any input.");
        ResetSentenceUndo();TraceFontFinderInput(report,"Inside-word Font Finder future typing",()=>TypeInput("X"));PumpInput();
        var typed=SentenceFindText(paragraph,"X");
        RequireFontFinder(FontFinderCharacterState(typed)==expected && PagedDocumentText()==before.Replace("fixture","fixXture",StringComparison.Ordinal),
            "Inside-word actual typing lost its chosen family or existing emphasis/size/colour/underline: expected="+expected+"; actual="+FontFinderCharacterState(typed)+".");
        RequireFontFinder(FontFinderCharacterState(SentenceFindText(paragraph,"fix"))==word &&
            FontFinderCharacterState(SentenceFindText(paragraph,"ture"))==word && FontFinderCharacterState(SentenceFindText(paragraph,"Typing "))==prefix &&
            FontFinderCharacterState(SentenceFindText(paragraph," content."))==suffix,
            "Inside-word font typing changed characters outside the newly inserted span.");
        Editor.Undo();PumpInput();
        RequireFontFinder(PagedDocumentText()==before && FontFinderCharacterState(SentenceFindText(paragraph,"fixture"))==word && Editor.CanRedo,
            "One Undo did not remove both the native inserted character and its font formatting inside a word.");
        Editor.Redo();PumpInput();
        RequireFontFinder(FontFinderCharacterState(SentenceFindText(paragraph,"X"))==expected && PagedDocumentText()==before.Replace("fixture","fixXture",StringComparison.Ordinal),
            "One Redo did not restore the actual inside-word character and chosen font formatting together.");
        report.AppendLine("At an empty caret inside an existing word, choosing a family left every original character's formatting intact. Only the actual next typed character received the choice and retained the caret's emphasis, size, colour and underline; one native Undo/Redo removed/restored character and font formatting together.");
    }

    void VerifyFontFinderCaretReturnGuard(string change,StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var paragraph=new Paragraph(new Run("Caret return guard sample."){FontFamily=new FontFamily(installed.Before)});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Editor.CaretPosition=SentenceTextPointer(paragraph,6);Editor.Focus();PumpInput();
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,29d);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkMagenta);
        Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Bold);
        Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Italic);
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,TextDecorations.Underline);
        ShowFontFinder();var dialog=fontFinderWindow!;
        var query=FontFinderControl<TextBox>(dialog,"FontFinderQuery");dialog.Activate();query.Focus();PumpInput();
        WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"Caret-return guard catalogue did not finish loading.");
        query.Text=installed.After;ClickFontFinder(dialog,"FontFinderSearch");
        WaitFontFinder(()=>FontFinderControl<ListBox>(dialog,"FontFinderResults").Items.Count>0 && FontFinderControl<Button>(dialog,"FontFinderApply").IsEnabled,
            "Caret-return guard local match did not become applicable.");
        ClickFontFinder(dialog,"FontFinderApply");PumpInput();
        RequireFontFinder(Editor.IsKeyboardFocusWithin,"The caret-return guard could not obtain actual document keyboard focus after Apply.");
        var applied=FontFinderCharacterState(Editor.Selection);
        if(change=="caret movement")
        {
            SendKeys(Enumerable.Range(0,7).SelectMany(_=>new[]{KeyInput(0x27),KeyInput(0x27,true)}).ToArray());
            PumpInput();
            RequireFontFinder(new TextRange(paragraph.ContentStart,Editor.CaretPosition).Text=="Caret return ",
                "Actual Right-arrow navigation did not establish the intended later caret destination.");
        }
        else if(change=="source edit"){TypeInput("Y");PumpInput();}
        else
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty,new FontFamily(installed.Before));
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,Brushes.DarkOrange);
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,17d);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Normal);
            Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Normal);
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,null);
        }
        PumpInput();
        var expectedDocument=Editor.Document;
        var expectedOffset=Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
        var before=PagedDocumentText();
        var prefix=new TextRange(Editor.Document.ContentStart,Editor.CaretPosition).Text;
        var deliberate=FontFinderCharacterState(Editor.Selection);
        var destinationBaseline=change=="caret movement"?FontFinderCharacterState(SentenceFindText(paragraph,"guard")):deliberate;
        report.AppendLine("Caret return "+change+" diagnostic: obsolete applied="+applied+"; deliberate current="+deliberate+".");
        CloseFontFinderThroughButton(dialog);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);Editor.Focus();PumpInput();
        RequireFontFinder(ReferenceEquals(Editor.Document,expectedDocument) && Editor.Selection.IsEmpty &&
            Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition)==expectedOffset && PagedDocumentText()==before,
            "Closing the finder restored an obsolete caret/document destination after "+change+".");
        var typing=FontFinderCharacterState(Editor.Selection);
        report.AppendLine("Caret return "+change+" diagnostic: after Close/owner focus="+typing+".");
        if(change=="pending format")
            RequireFontFinder((Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source==installed.Before && typing!=applied,
                "Closing the finder restored obsolete font formatting after "+change+".");
        if(change=="pending format")
            RequireFontFinder((Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color!=Colors.DarkMagenta,
                "Closing the finder overwrote the author's later pending colour with the old applied colour.");
        TraceFontFinderInput(report,"Stale return "+change,()=>TypeInput("X"));PumpInput();
        var typed=SentenceFindText(paragraph,"X");
        var expectedTyping=change=="caret movement"?destinationBaseline:typing;
        RequireFontFinder(FontFinderCharacterState(typed)==expectedTyping && PagedDocumentText()==before.Insert(prefix.Length,"X"),
            "Typing after finder closure used an obsolete destination or format after "+change+".");
        report.AppendLine("After "+change+", reactivating and closing the finder through its actual Close button could not restore the obsolete caret or formatting; the next typed character used the current destination.");
    }

    void VerifyFontFinderLateResponses(StringBuilder report)
    {
        var installed=FontFinderInstalledFixture();var original=FontFinderService.GeneratorOverride;var originalMode=languageSettings.WritingMode;
        try
        {
            languageSettings.AllowAiLanguageServices=true;
            foreach(var change in new[]{"query","mode","close"})
            {
                fontFinderWindow?.Close();
                var paragraph=new Paragraph(new Run("LATE RESPONSE SOURCE MUST SURVIVE."));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
                Editor.CaretPosition=SentenceTextPointer(paragraph,5);
                var response=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var started=false;var returned=false;
                FontFinderService.GeneratorOverride=async (_,__)=>
                {
                    started=true;var value=await response.Task;returned=true;return value; // Intentionally ignores cancellation.
                };
                ShowFontFinder();var dialog=fontFinderWindow!;PumpInput();
                FontFinderControl<TextBox>(dialog,"FontFinderQuery").Text="readable report font";
                WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderAskAi").IsEnabled,"Late-response UI did not become ready.");
                ClickFontFinder(dialog,"FontFinderAskAi");WaitFontFinder(()=>started,"Late-response deterministic transport never started.");
                if(change=="query")FontFinderControl<TextBox>(dialog,"FontFinderQuery").Text="a different query";
                else if(change=="mode")
                {
                    languageSettings.WritingMode=originalMode==WritingMode.Fiction?WritingMode.Technical:WritingMode.Fiction;
                    dialog.Activate();PumpInput();
                }
                else dialog.Close();
                var before=PagedDocumentText();
                response.SetResult(FontFinderResponse((installed.After,true,true,"LATE_STALE_"+change)));
                WaitFontFinder(()=>returned,"The intentionally non-cooperative transport did not return.");PumpInput();
                var results=FontFinderControl<ListBox>(dialog,"FontFinderResults");
                RequireFontFinder(!results.Items.OfType<ListBoxItem>().Any(item=>(item.Tag as FontFinderMatch)?.Reason=="LATE_STALE_"+change) &&
                    PagedDocumentText()==before && (change!="close" || fontFinderWindow==null),"A late response escaped its "+change+" guard.");
                if(change!="close")dialog.Close();
                languageSettings.WritingMode=originalMode;PumpInput();
            }
            report.AppendLine("A deliberately non-cooperative in-process transport returned after query changes, writing-mode changes and window closure. Every obsolete response was discarded without applying fonts or repopulating stale AI matches.");
        }
        finally{fontFinderWindow?.Close();FontFinderService.GeneratorOverride=original;languageSettings.WritingMode=originalMode;languageSettings.AllowAiLanguageServices=false;}
    }

    void CaptureFontFinderScreenshot(StringBuilder report)
    {
        fontFinderWindow?.Close();var installed=FontFinderInstalledFixture();
        var paragraph=new Paragraph(new Run("Synthetic preview: clear type for a technical report."){FontFamily=new FontFamily(installed.Before)});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);SentenceSelectText(paragraph,"clear type");
        ShowFontFinder();var dialog=fontFinderWindow!;PumpInput();
        WaitFontFinder(()=>FontFinderControl<Button>(dialog,"FontFinderSearch").IsEnabled,"Screenshot catalogue did not finish loading.");
        FontFinderControl<TextBox>(dialog,"FontFinderQuery").Text=installed.After;ClickFontFinder(dialog,"FontFinderSearch");
        var results=FontFinderControl<ListBox>(dialog,"FontFinderResults");
        WaitFontFinder(()=>results.Items.Count>0,"Screenshot local query had no result.");results.SelectedIndex=0;
        FontFinderControl<TextBox>(dialog,"FontFinderSample").Text="Synthetic regression preview\r\nThe quick brown fox jumps over the lazy dog.\r\n0123456789 — British English";
        dialog.UpdateLayout();PumpInput();
        RequireFontFinder(dialog.ActualWidth>0 && dialog.ActualHeight>0,"The actual Font Finder window has no rendered extent.");
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(dialog);bitmap.Freeze();
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path=Path.Combine(Path.GetTempPath(),"CtrlZed49FontFinder.png");using(var file=File.Create(path))encoder.Save(file);
        RequireFontFinder(new FileInfo(path).Length>1000,"The actual UI screenshot is empty.");
        dialog.Close();PumpInput();report.AppendLine("Actual rendered WPF window with synthetic document/sample text captured to "+path+".");
    }

    // A short stable representation checks effective properties rather than
    // assuming that native TextRange edits keep the same Run boundaries.
    static string FontFinderCharacterState(TextRange range,bool omitFamily=false,bool omitBold=false)
    {
        string Value(DependencyProperty property)
        {
            var value=range.GetPropertyValue(property);
            return value switch
            {
                FontFamily family=>family.Source,
                SolidColorBrush brush=>brush.Color.ToString(),
                TextDecorationCollection decorations=>string.Join(",",decorations.Select(item=>item.Location.ToString())),
                _=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??""
            };
        }
        return string.Join("|",new[]{omitFamily?"":Value(TextElement.FontFamilyProperty),omitBold?"":Value(TextElement.FontWeightProperty),
            Value(TextElement.FontStyleProperty),Value(TextElement.FontSizeProperty),Value(TextElement.ForegroundProperty),Value(Inline.TextDecorationsProperty)});
    }

    static string FontFinderCharacterStateFrom(string state,bool omitFamily=false,bool omitBold=false)
    {
        var parts=state.Split('|');if(omitFamily)parts[0]="";if(omitBold)parts[1]="";return string.Join("|",parts);
    }

    static string FontFinderFamilyOf(TextRange range)=>(range.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source??"<mixed>";
    static string FontFinderParagraphState(Paragraph paragraph)=>JsonSerializer.Serialize(new
    {
        paragraph.Margin,paragraph.TextIndent,LineHeight=double.IsNaN(paragraph.LineHeight)?0:paragraph.LineHeight,
        paragraph.LineStackingStrategy,paragraph.KeepWithNext,paragraph.KeepTogether,paragraph.BreakPageBefore,paragraph.TextAlignment,paragraph.Tag
    });
    static T FontFinderControl<T>(Window window,string name) where T:FrameworkElement
        =>window.FindName(name) as T??throw new InvalidOperationException("Font Finder is missing control "+name+".");
    static void ClickFontFinder(Window window,string name)=>FontFinderControl<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    void TraceFontFinderInput(StringBuilder report,string context,Action input)
    {
        void Record(string stage,InputEventArgs value)
        {
            if(value is not KeyEventArgs && value is not TextCompositionEventArgs)return;
            try
            {
                var details=value switch
                {
                    KeyEventArgs key=>"key="+key.Key+"; system key="+key.SystemKey,
                    TextCompositionEventArgs text=>"composition="+JsonSerializer.Serialize(text.Text)+"; composition type="+text.TextComposition.GetType().FullName,
                    _=>""
                };
                report.AppendLine(context+" input trace: "+stage+"; event="+value.RoutedEvent?.Name+"; "+details+
                    "; modifiers="+Keyboard.Modifiers+"; focus="+Editor.IsKeyboardFocusWithin+"; caret="+FontFinderCharacterState(Editor.Selection)+
                    "; keyboard culture="+InputLanguageManager.Current.CurrentInputLanguage.IetfLanguageTag+
                    "; caret language="+Editor.Selection.GetPropertyValue(FrameworkElement.LanguageProperty)+
                    "; symbols="+Editor.Document.ContentStart.GetOffsetToPosition(Editor.Selection.Start)+"/"+
                    Editor.Document.ContentStart.GetOffsetToPosition(Editor.Selection.End)+".");
            }
            catch(Exception error){report.AppendLine(context+" input trace diagnostic unavailable: "+error.Message);}
        }
        void Before(object sender,PreProcessInputEventArgs args)=>Record("PreProcessInput",args.StagingItem.Input);
        void After(object sender,ProcessInputEventArgs args)=>Record("PostProcessInput",args.StagingItem.Input);
        var manager=InputManager.Current;
        manager.PreProcessInput+=Before;manager.PostProcessInput+=After;
        try{input();}
        finally{manager.PreProcessInput-=Before;manager.PostProcessInput-=After;}
    }
    static void CloseFontFinderThroughButton(Window window)
    {
        window.Activate();FontFinderControl<TextBox>(window,"FontFinderQuery").Focus();PumpInput();
        var close=FontFinderLogicalDescendants(window).OfType<Button>().SingleOrDefault(button=>Equals(button.Content,"Close"))
            ??throw new InvalidOperationException("The actual Font Finder Close button was not found.");
        close.Focus();PumpInput();
        RequireFontFinder(close.IsKeyboardFocusWithin,"The actual Font Finder Close button did not receive keyboard focus.");
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpInput();
        RequireFontFinder(!window.IsVisible,"The actual Font Finder Close button did not close its window.");
    }
    static IEnumerable<DependencyObject> FontFinderLogicalDescendants(DependencyObject root)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach(var descendant in FontFinderLogicalDescendants(child))yield return descendant;
        }
    }
    static void FontFinderChoose(Window window,string name,string label)
    {
        var combo=FontFinderControl<ComboBox>(window,name);
        var item=combo.Items.Cast<object>().FirstOrDefault(value=>string.Equals((value as ComboBoxItem)?.Content?.ToString()??value.ToString(),label,StringComparison.Ordinal));
        RequireFontFinder(item!=null,"Font Finder is missing the "+label+" option in "+name+".");combo.SelectedItem=item;
    }
    static void RequireFontFinder(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void ExpectFontFinderRejection(Action action,string message)
    {
        try{action();}catch(Exception error) when(error is InvalidOperationException or InvalidDataException or JsonException or FormatException or ArgumentException){return;}
        throw new InvalidOperationException(message);
    }
    static void AwaitFontFinderTask(Task task){WaitFontFinder(()=>task.IsCompleted,"Font Finder work timed out.");task.GetAwaiter().GetResult();}
    static void WaitFontFinder(Func<bool> ready,string message)
    {
        var watch=Stopwatch.StartNew();
        while(!ready())
        {
            if(watch.Elapsed>TimeSpan.FromSeconds(30))throw new InvalidOperationException(message);
            PumpInput();
        }
    }
}
