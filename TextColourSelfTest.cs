using System.Diagnostics;
using System.IO;
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
    // All palette responses in this isolated regression are injected. The
    // command still captures its actual editor destination and uses its real
    // selection/caret formatting path; no live AI or installation is involved.
    internal static int RunTextColourSelfTest()
    {
        var report=new StringBuilder("Beta 50.1 text colour selection, typing and command regression\r\n");
        var failures=new List<Exception>();
        var appRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var originals=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var originalDirectories=Directory.Exists(appRoot)
            ?Directory.GetDirectories(appRoot,"*",SearchOption.AllDirectories).Prepend(appRoot).ToHashSet(StringComparer.OrdinalIgnoreCase)
            :new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var temporary=Path.Combine(Path.GetTempPath(),"CtrlZed501Colour-"+Guid.NewGuid().ToString("N"));
        var picker=TextColourPickerOverride;
        var shutdown=Application.Current.ShutdownMode;
        IDataObject? clipboard=null;
        var clipboardCaptured=false;var snapshotCaptured=false;
        MainWindow? window=null;
        var result=129;
        void Phase(string name,Action action)
        {
            try{action();report.AppendLine("PASS: "+name+".");}
            catch(Exception error){failures.Add(error);report.AppendLine("FAIL: "+name+": "+error);}
        }
        try
        {
            if(Directory.Exists(appRoot))
                foreach(var path in Directory.GetFiles(appRoot,"*",SearchOption.AllDirectories))originals[path]=File.ReadAllBytes(path);
            snapshotCaptured=true;
            clipboard=Clipboard.GetDataObject();clipboardCaptured=true;
            Directory.CreateDirectory(appRoot);Directory.CreateDirectory(temporary);
            foreach(var path in Directory.GetFiles(appRoot,"Recovery*"))File.Delete(path);
            TextColourPickerOverride=(_,__)=>throw new InvalidOperationException("Unexpected colour palette outside a deterministic command phase.");
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Phase("real editor initialisation",()=>
            {
                window=new MainWindow{WindowState=WindowState.Normal,Width=1000,Height=720};
                window.Show();window.Activate();SetForegroundWindow(new WindowInteropHelper(window).Handle);PumpInput();
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.languageSettings.SpellingEnabled=false;
                window.languageSettings.ContextualSpellingSuggestions=false;
                window.languageSettings.GrammarAndStyleSuggestions=false;
                window.languageSettings.PoetryRhymeSuggestions=false;
                window.languageSettings.AllowAiLanguageServices=false;
                SpellCheck.SetIsEnabled(window.Editor,false);
            });
            if(window is {IsLoaded:true})
            {
                var editor=window;
                Phase("Format menu and toolbar share the current-colour palette command",()=>editor.VerifyTextColourCommands(report));
                foreach(var mode in new[]{EditorViewMode.Continuous,EditorViewMode.Paged})
                {
                    var view=mode;
                    Phase("rich multi-paragraph colour only and one Undo/Redo in "+view,()=>editor.VerifyTextColourRichRange(view,report));
                    foreach(var interior in new[]{false,true})
                    {
                        var withinWord=interior;
                        Phase((withinWord?"interior-word":"word-boundary")+" future typing colour and Undo in "+view,
                            ()=>editor.VerifyTextColourTyping(view,withinWord,report));
                    }
                }
                Phase("pending font plus accepted colour through a real toolbar click",()=>editor.VerifyTextColourPendingFont(false,report));
                Phase("cancelled colour palette retains a pending font choice",()=>editor.VerifyTextColourPendingFont(true,report));
                Phase("cancel, read-only and stale destination safeguards",()=>editor.VerifyTextColourGuards(report));
                Phase("native, protected native and DOCX colour persistence",()=>editor.VerifyTextColourPersistence(temporary,report));
                Phase("actual synthetic colour toolbar screenshot",()=>editor.CaptureTextColourScreenshot(report));
            }
            if(failures.Count==0)result=0;
        }
        catch(Exception error){failures.Add(error);report.AppendLine("FAIL: setup: "+error);}
        finally
        {
            if(window!=null)Phase("test window cleanup",()=>
            {
                window.onlineFontWindow?.Close();window.fontFinderWindow?.Close();window.sentenceCompletionWindow?.Close();
                window.closingApproved=true;
                window.recoveryTimer.Stop();window.uiRefreshTimer.Stop();window.grammarReviewTimer?.Stop();
                window.grammarAiCancellation?.Cancel();window.beta35PaginationTimer?.Stop();window.beta35PaginationCancellation?.Cancel();
                window.Close();
            });
            TextColourPickerOverride=picker;
            Application.Current.ShutdownMode=shutdown;
            if(snapshotCaptured)Phase("original application-data restoration",()=>
            {
                if(Directory.Exists(appRoot))
                    foreach(var path in Directory.GetFiles(appRoot,"*",SearchOption.AllDirectories))
                        if(!originals.ContainsKey(path))File.Delete(path);
                foreach(var pair in originals)
                {Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);File.WriteAllBytes(pair.Key,pair.Value);}
                if(Directory.Exists(appRoot))
                    foreach(var path in Directory.GetDirectories(appRoot,"*",SearchOption.AllDirectories).Append(appRoot).OrderByDescending(path=>path.Length))
                        if(!originalDirectories.Contains(path) && !Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
                foreach(var pair in originals)
                    ColourRequire(File.ReadAllBytes(pair.Key).AsSpan().SequenceEqual(pair.Value),"An application-data file did not restore exactly: "+Path.GetFileName(pair.Key));
            });
            if(clipboardCaptured)Phase("original clipboard restoration",()=>
            {if(clipboard!=null)Clipboard.SetDataObject(clipboard,true);else Clipboard.Clear();});
            try{if(Directory.Exists(temporary))Directory.Delete(temporary,true);}
            catch(IOException error){report.AppendLine("Temporary colour fixture cleanup: "+error.Message);}
            if(failures.Count>0)result=129;
            report.AppendLine("Palette responses: deterministic accepted/cancelled choices; the normal command delegates to the existing standard Windows colour dialog.");
            report.AppendLine("Exit code: "+result);
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed501TextColour.txt"),report.ToString());
        }
        return result;
    }

    void VerifyTextColourCommands(StringBuilder report)
    {
        ColourRequire(TextColourMenu!=null && TextColourButton!=null,"The text-colour menu or toolbar button is missing.");
        var format=MainMenu.Items.OfType<MenuItem>().Single(item=>(item.Header as string)?.Replace("_","")=="Format");
        ColourRequire(format.Items.Contains(TextColourMenu),"Text Colour is not under the Format menu.");
        var toolbar=ColourAncestors(TextColourButton).OfType<ToolBar>().FirstOrDefault();
        ColourRequire(toolbar!=null && toolbar.Items.IndexOf(TextColourButton)<toolbar.Items.IndexOf(FieldDisplayToggle),
            "The colour button does not appear before Fields on the small toolbar.");
        var image=ColourDescendants(TextColourButton).OfType<Image>().FirstOrDefault(item=>item.Source!=null);
        ColourRequire(image?.Source!=null,"The toolbar colour picker does not have an actual image source.");

        var paragraph=new Paragraph(new Run("Route this coloured text.")
        {FontFamily=new FontFamily("Arial"),FontSize=22,FontWeight=FontWeights.Bold,Foreground=Brushes.Green,TextDecorations=TextDecorations.Underline});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);SentenceSelectText(paragraph,"coloured text");
        var before=ColourCharacterState(Editor.Selection,omitColour:true);
        var calls=0;
        TextColourPickerOverride=(owner,initial)=>
        {
            ColourRequire(ReferenceEquals(owner,this),"The palette did not receive its editor owner.");
            ColourRequire(ColourHex(initial)=="#008000","The menu palette did not start at the selected text's current colour: "+initial);
            calls++;return ColourSimulatePaletteFocus(owner,"#5C2D91");
        };
        TextColourMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));PumpInput();
        var range=SentenceFindText(paragraph,"coloured text");
        ColourRequire(calls==1 && ColourOf(range)=="#5C2D91" && ColourCharacterState(range,omitColour:true)==before,
            "The Format command did not apply only the accepted palette colour.");
        SentenceSelectText(paragraph,"coloured text");
        TextColourPickerOverride=(_,initial)=>
        {
            ColourRequire(ColourHex(initial)=="#5C2D91","The toolbar palette did not start at the same current text colour.");
            calls++;return "#006C80";
        };
        ColourPhysicalClick(TextColourButton);PumpInput();
        ColourRequire(calls==2 && ColourOf(SentenceFindText(paragraph,"coloured text"))=="#006C80" &&
            ColourCharacterState(SentenceFindText(paragraph,"coloured text"),omitColour:true)==before,
            "The actual toolbar click did not route through the same palette command or changed other formatting.");
        report.AppendLine("The real Format item and a native mouse click on the pinwheel toolbar button invoked the same palette command, supplied the selected foreground as its initial colour and changed only foreground. Toolbar placement and rendered image were checked.");
    }

    void VerifyTextColourRichRange(EditorViewMode mode,StringBuilder report)
    {
        var picture=new DrawingImage(new GeometryDrawing(Brushes.Goldenrod,null,new RectangleGeometry(new Rect(0,0,16,11))));picture.Freeze();
        var image=new Image{Source=picture,Width=16,Height=11};
        var first=new Paragraph{Margin=new Thickness(13,9,7,5),TextIndent=8,KeepWithNext=true,TextAlignment=TextAlignment.Justify};
        first.Inlines.Add(new Run("Before "){FontFamily=new FontFamily("Arial"),FontSize=14,Foreground=Brushes.Maroon});
        first.Inlines.Add(new Run("target "){FontFamily=new FontFamily("Times New Roman"),FontSize=23,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,Foreground=Brushes.Navy,TextDecorations=TextDecorations.Underline});
        var link=new Hyperlink(new Run("linked words"){FontFamily=new FontFamily("Arial"),FontSize=18,Foreground=Brushes.Green}){NavigateUri=new Uri("https://example.com/colour")};
        first.Inlines.Add(link);first.Inlines.Add(new InlineUIContainer(image));first.Inlines.Add(new Run(" tail."){FontFamily=new FontFamily("Courier New"),FontSize=17,Foreground=Brushes.OrangeRed});
        var second=new Paragraph(new Run("second target"){FontFamily=new FontFamily("Arial"),FontSize=19,FontStyle=FontStyles.Italic,Foreground=Brushes.Teal})
        {Margin=new Thickness(2,3,4,5),TextIndent=-4,KeepTogether=true};
        var after=new Paragraph(new Run("UNRELATED AFTER PARAGRAPH."){FontFamily=new FontFamily("Arial"),FontSize=15,Foreground=Brushes.Purple});
        var document=new FlowDocument(first);document.Blocks.Add(second);document.Blocks.Add(after);
        SentenceSetDocument(document,mode);
        var watched=new[]{(first,"Before "),(first,"target "),(first,"linked words"),(first," tail."),(second,"second target"),(after,"UNRELATED AFTER PARAGRAPH.")};
        var states=watched.Select(pair=>ColourCharacterState(SentenceFindText(pair.Item1,pair.Item2))).ToArray();
        var layouts=new[]{first,second,after}.Select(FontFinderParagraphState).ToArray();
        var text=PagedDocumentText();
        Editor.Selection.Select(SentenceFindText(first,"target ").Start,SentenceFindText(second,"second target").End);
        ResetSentenceUndo();var target=CaptureTextColourTarget();ApplyTextColour(target,"#C22F6B");PumpInput();
        foreach(var index in new[]{1,2,3,4})
        {
            var range=SentenceFindText(watched[index].Item1,watched[index].Item2);
            ColourRequire(ColourOf(range)=="#C22F6B" && ColourCharacterState(range,omitColour:true)==ColourWithoutForeground(states[index]),
                "Range colour changed non-colour formatting or missed selected text in "+mode+" at "+index+".");
        }
        foreach(var index in new[]{0,5})
            ColourRequire(ColourCharacterState(SentenceFindText(watched[index].Item1,watched[index].Item2))==states[index],"Unselected text formatting changed in "+mode+".");
        ColourRequire(PagedDocumentText()==text && new[]{first,second,after}.Select(FontFinderParagraphState).SequenceEqual(layouts) &&
            first.Inlines.OfType<Hyperlink>().Single().NavigateUri==link.NavigateUri && image.Source==picture && image.Width==16 && image.Height==11,
            "Colour changed paragraphs, text, hyperlink destination or picture geometry in "+mode+".");
        Editor.Undo();PumpInput();
        ColourRequire(PagedDocumentText()==text && watched.Select(pair=>ColourCharacterState(SentenceFindText(pair.Item1,pair.Item2))).SequenceEqual(states) && Editor.CanRedo,
            "One Undo did not restore every selected range colour and rich property in "+mode+".");
        Editor.Redo();PumpInput();
        ColourRequire(ColourOf(SentenceFindText(first,"target "))=="#C22F6B" && ColourOf(SentenceFindText(second,"second target"))=="#C22F6B",
            "One Redo did not restore the multi-paragraph colour in "+mode+".");
        report.AppendLine(mode+": a mixed-font, multi-paragraph selection changed only foreground; original font/size/emphasis/underline, unselected text, paragraph layout, link and picture survived. One Undo/Redo restored the complete operation.");
    }

    void VerifyTextColourTyping(EditorViewMode mode,bool interior,StringBuilder report)
    {
        var run=new Run("Typing fixture content.")
        {FontFamily=new FontFamily("Arial"),FontSize=27,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,Foreground=Brushes.Navy,TextDecorations=TextDecorations.Underline};
        var paragraph=new Paragraph(run);SentenceSetDocument(new FlowDocument(paragraph),mode);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);Editor.CaretPosition=SentenceTextPointer(paragraph,interior?10:7);Editor.Focus();PumpInput();
        var original=ColourCharacterState(new TextRange(paragraph.ContentStart,paragraph.ContentEnd));
        var native=ColourCharacterState(Editor.Selection,omitColour:true);var text=PagedDocumentText();
        var target=CaptureTextColourTarget();ApplyTextColour(target,"#D33F23");PumpInput();
        ColourRequire(PagedDocumentText()==text && ColourCharacterState(new TextRange(paragraph.ContentStart,paragraph.ContentEnd))==original,
            "Choosing future typing colour changed existing word formatting before typing in "+mode+".");
        ResetSentenceUndo();TypeInput("X");PumpInput();
        var inserted=SentenceFindText(paragraph,"X");
        ColourRequire(ColourOf(inserted)=="#D33F23" && ColourCharacterState(inserted,omitColour:true)==native &&
            PagedDocumentText()==(interior?text.Replace("fixture","fixXture",StringComparison.Ordinal):text.Replace("Typing fixture","Typing Xfixture",StringComparison.Ordinal)),
            "Native typing lost its chosen colour or other pending formatting in "+mode+" at "+(interior?"interior":"boundary")+": "+ColourCharacterState(inserted));
        ColourRequire(ColourCharacterState(SentenceFindText(paragraph,interior?"fix":"Typing "))==original &&
            ColourCharacterState(SentenceFindText(paragraph,interior?"ture":"fixture"))==original,
            "Colour typing recoloured the existing surrounding letters in "+mode+".");
        Editor.Undo();PumpInput();
        ColourRequire(PagedDocumentText()==text && ColourCharacterState(new TextRange(paragraph.ContentStart,paragraph.ContentEnd))==original && Editor.CanRedo,
            "One Undo failed to remove the typed character and its colour in "+mode+".");
        Editor.Redo();PumpInput();
        ColourRequire(ColourOf(SentenceFindText(paragraph,"X"))=="#D33F23" && ColourCharacterState(SentenceFindText(paragraph,"X"),omitColour:true)==native,
            "One Redo lost the inserted character's intended colour or other formatting in "+mode+".");
        report.AppendLine(mode+" "+(interior?"inside a word":"at a word boundary")+": actual Windows typing received the selected foreground without recolouring existing letters; family, size, bold, italic and underline survived, including native keyboard/document-language marking. One Undo/Redo includes the inserted colour.");
    }

    void VerifyTextColourPendingFont(bool cancel,StringBuilder report)
    {
        var fonts=FontFinderInstalledFixture();
        var paragraph=new Paragraph(new Run("Typing fixture content.")
        {FontFamily=new FontFamily(fonts.Before),FontSize=27,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic,Foreground=Brushes.Navy,TextDecorations=TextDecorations.Underline});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);Editor.CaretPosition=SentenceTextPointer(paragraph,10);Editor.Focus();PumpInput();
        var original=ColourCharacterState(new TextRange(paragraph.ContentStart,paragraph.ContentEnd));
        var baseState=ColourCharacterState(Editor.Selection,omitColour:true);var text=PagedDocumentText();
        ApplyFontFinderSelection(CaptureFontFinderTarget(),new FontFinderMatch(fonts.After,null,null,"Pending font and colour fixture."),fonts.Catalogue);PumpInput();
        var calls=0;
        TextColourPickerOverride=(owner,_)=>{calls++;return ColourSimulatePaletteFocus(owner,cancel?null:"#AA276A");};
        ColourPhysicalClick(TextColourButton);PumpInput();
        ColourRequire(calls==1 && PagedDocumentText()==text && ColourCharacterState(new TextRange(paragraph.ContentStart,paragraph.ContentEnd))==original,
            "The colour palette discarded the source formatting or changed existing letters beside a pending font choice.");
        ResetSentenceUndo();Editor.Focus();TypeInput("X");PumpInput();
        var typed=SentenceFindText(paragraph,"X");
        var expected=baseState.Split('|');expected[0]=fonts.After;
        ColourRequire(ColourFamily(typed)==fonts.After && ColourOf(typed)==(cancel?"#000080":"#AA276A") &&
            ColourCharacterState(typed,omitColour:true)==string.Join("|",expected) && PagedDocumentText()==text.Replace("fixture","fixXture",StringComparison.Ordinal),
            "Choosing "+(cancel?"Cancel":"a colour")+" lost the preceding deferred font or other typing format: "+ColourCharacterState(typed));
        report.AppendLine("A deferred inside-word font choice survived a real toolbar click and picker focus transfer with "+(cancel?"Cancel":"an accepted colour")+"; the actual inserted character retained both the intended family and foreground without changing existing letters.");
    }

    void VerifyTextColourGuards(StringBuilder report)
    {
        var paragraph=new Paragraph(new Run("Cancel colour leaves this source unchanged."){Foreground=Brushes.DarkGreen});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);SentenceSelectText(paragraph,"this source");ResetSentenceUndo();dirty=false;
        var before=ColourDocumentMarkup();var calls=0;
        TextColourPickerOverride=(owner,_)=>{calls++;return ColourSimulatePaletteFocus(owner,null);};
        TextColourMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));PumpInput();
        ColourRequire(calls==1 && ColourDocumentMarkup()==before && !dirty && !Editor.CanUndo,"Cancelling the palette changed the document, dirty state or Undo history.");
        Editor.IsReadOnly=true;PumpInput();
        ColourRequire(!TextColourMenu.IsEnabled && !TextColourButton.IsEnabled,"Text Colour remains enabled in a read-only editor.");
        TextColour_Click(this,new RoutedEventArgs());PumpInput();
        ColourRequire(calls==1 && ColourDocumentMarkup()==before && !dirty,"A read-only command opened the palette or changed the document.");
        Editor.IsReadOnly=false;
        foreach(var change in new[]{"source","new document","read only","format"})
        {
            paragraph=new Paragraph(new Run("The original colour destination."));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
            SentenceSelectText(paragraph,"original colour");var target=CaptureTextColourTarget();
            if(change=="source")new TextRange(paragraph.ContentStart,paragraph.ContentStart).Text="Changed ";
            else if(change=="new document"){dirty=false;New_Click(this,new RoutedEventArgs());}
            else if(change=="read only")Editor.IsReadOnly=true;
            else Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,31d);
            var current=ColourDocumentMarkup();
            ColourReject(()=>ApplyTextColour(target,"#336699"),"A stale colour target was accepted after "+change+".");
            ColourRequire(ColourDocumentMarkup()==current,"Rejected stale colour application changed the current document after "+change+".");
            Editor.IsReadOnly=false;
        }
        paragraph=new Paragraph(new Run("Reject malformed colour."));SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);
        SentenceSelectText(paragraph,"malformed colour");var validTarget=CaptureTextColourTarget();before=ColourDocumentMarkup();
        ColourReject(()=>ApplyTextColour(validTarget,"#GG0000"),"Malformed RGB was accepted.");
        ColourRequire(ColourDocumentMarkup()==before,"Rejecting malformed colour changed the document.");
        report.AppendLine("Cancel left document/dirty/Undo unchanged. Read-only mode disabled both controls and blocked the handler; source, New document, read-only and format changes invalidated retained destinations. Invalid RGB was rejected before changes.");
    }

    void VerifyTextColourPersistence(string directory,StringBuilder report)
    {
        var paragraph=new Paragraph(new Run("Persistent colour sample."){FontFamily=new FontFamily("Arial"),FontSize=21,Foreground=Brushes.Navy});
        SentenceSetDocument(new FlowDocument(paragraph),EditorViewMode.Continuous);SentenceSelectText(paragraph,"colour sample");
        ApplyTextColour(CaptureTextColourTarget(),"#B52E67");
        var text=PagedDocumentText();
        using var native=new MemoryStream();WriteCzdArchive(native);native.Position=0;
        var package=ReadCzdArchive(native);var saved=package.Document.Blocks.OfType<Paragraph>().First();
        ColourRequire(new TextRange(package.Document.ContentStart,package.Document.ContentEnd).Text==text && ColourOf(SentenceFindText(saved,"colour sample"))=="#B52E67" &&
            ColourOf(SentenceFindText(saved,"Persistent "))=="#000080","Native CZD lost direct text colour or its selection boundary.");
        using var password=new SecureString();foreach(var character in "Text colour fixture 501")password.AppendChar(character);password.MakeReadOnly();
        using var protectedFile=new MemoryStream();CzdProtection.Encrypt(protectedFile,native.ToArray(),password);protectedFile.Position=0;
        var decrypted=CzdProtection.Decrypt(protectedFile,password);using var decryptedFile=new MemoryStream(decrypted);
        var protectedPackage=ReadCzdArchive(decryptedFile);var protectedParagraph=protectedPackage.Document.Blocks.OfType<Paragraph>().First();
        ColourRequire(ColourOf(SentenceFindText(protectedParagraph,"colour sample"))=="#B52E67" &&
            new TextRange(protectedPackage.Document.ContentStart,protectedPackage.Document.ContentEnd).Text==text,"Protected CZD lost text colour or source text.");
        var path=Path.Combine(directory,"text-colour.docx");PrepareDocumentStylesForInterop();DocxInterop.Save(path,Editor.Document);
        var docx=DocxInterop.Load(path);var imported=docx.Blocks.OfType<Paragraph>().First();
        ColourRequire(ColourOf(SentenceFindText(imported,"colour sample"))=="#B52E67" && ColourOf(SentenceFindText(imported,"Persistent "))=="#000080" &&
            new TextRange(docx.ContentStart,docx.ContentEnd).Text==text,"DOCX interchange lost the selected colour, its boundary or text.");
        report.AppendLine("Native CZD, password-protected CZD and DOCX save/reopen retained the chosen foreground and the original colour of unselected text.");
    }

    void CaptureTextColourScreenshot(StringBuilder report)
    {
        var first=new Paragraph(new Run("Text colour at your fingertips")
        {FontFamily=new FontFamily("Arial"),FontSize=30,FontWeight=FontWeights.Bold,Foreground=Brushes.DarkSlateBlue}){Margin=new Thickness(0,8,0,16)};
        var second=new Paragraph();second.Inlines.Add(new Run("Selected words can be "){FontSize=22});
        second.Inlines.Add(new Run("a different colour"){FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(181,46,103)),FontWeight=FontWeights.Bold});
        second.Inlines.Add(new Run(" while the rest retains its formatting."){FontSize=22});
        var document=new FlowDocument(first);document.Blocks.Add(second);
        SentenceSetDocument(document,EditorViewMode.Continuous);Editor.CaretPosition=second.ContentEnd;UpdateLayout();PumpInput();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(this);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path=Path.Combine(Path.GetTempPath(),"CtrlZed501TextColour.png");using(var file=File.Create(path))encoder.Save(file);
        ColourRequire(new FileInfo(path).Length>1000,"The actual colour toolbar screenshot is empty.");
        report.AppendLine("Actual main window, provided colour-picker toolbar icon and synthetic coloured text captured to "+path+".");
    }

    static string? ColourSimulatePaletteFocus(MainWindow owner,string? response)
    {
        var box=new TextBox{Text="Synthetic palette focus fixture",Margin=new Thickness(12)};
        var dialog=new Window{Owner=owner,Title="Synthetic palette focus",Width=300,Height=130,Content=box,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        try{dialog.Show();dialog.Activate();box.Focus();PumpInput();ColourRequire(box.IsKeyboardFocusWithin,"The palette focus fixture never received actual keyboard focus.");}
        finally{dialog.Close();}
        return response;
    }

    void ColourPhysicalClick(FrameworkElement control)
    {
        Activate();SetForegroundWindow(new WindowInteropHelper(this).Handle);UpdateLayout();PumpInput();
        ColourRequire(control.IsVisible && control.IsEnabled && control.ActualWidth>0 && control.ActualHeight>0,"The actual colour toolbar control is not clickable.");
        var point=control.PointToScreen(new Point(control.ActualWidth/2,control.ActualHeight/2));
        SetPagedCursor((int)Math.Round(point.X),(int)Math.Round(point.Y));
        SendKeys(new[]{new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=2}}},new NativeInput{Type=0,Data=new InputUnion{Mouse=new MouseInput{Flags=4}}}});
    }

    string ColourDocumentMarkup()
    {using var stream=new MemoryStream();new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Save(stream,DataFormats.Xaml);return Convert.ToBase64String(stream.ToArray());}
    static string ColourOf(TextRange range)=>range.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush brush
        ?$"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}":"<mixed>";
    static string ColourFamily(TextRange range)=>(range.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily)?.Source??"<mixed>";
    static string ColourHex(string? value)
    {try{if(ColorConverter.ConvertFromString(value??"") is Color colour)return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";}catch{}return "<none>";}
    static string ColourCharacterState(TextRange range,bool omitColour=false)
    {
        string Value(DependencyProperty property)=>range.GetPropertyValue(property) switch
        {
            FontFamily family=>family.Source,SolidColorBrush brush=>brush.Color.ToString(),
            TextDecorationCollection decorations=>string.Join(",",decorations.Select(item=>item.Location)),
            var value=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??""
        };
        return string.Join("|",new[]{Value(TextElement.FontFamilyProperty),Value(TextElement.FontWeightProperty),Value(TextElement.FontStyleProperty),
            Value(TextElement.FontSizeProperty),omitColour?"":Value(TextElement.ForegroundProperty),Value(Inline.TextDecorationsProperty),Value(TextElement.FontStretchProperty),Value(Inline.BaselineAlignmentProperty)});
    }
    static string ColourWithoutForeground(string state){var parts=state.Split('|');parts[4]="";return string.Join("|",parts);}
    static IEnumerable<DependencyObject> ColourAncestors(DependencyObject item)
    {for(var current=LogicalTreeHelper.GetParent(item)??VisualTreeHelper.GetParent(item);current!=null;current=LogicalTreeHelper.GetParent(current)??VisualTreeHelper.GetParent(current))yield return current;}
    static IEnumerable<DependencyObject> ColourDescendants(DependencyObject item)
    {
        yield return item;
        if(item is not Visual && item is not System.Windows.Media.Media3D.Visual3D)yield break;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(item);index++)
            foreach(var descendant in ColourDescendants(VisualTreeHelper.GetChild(item,index)))yield return descendant;
    }
    static void ColourReject(Action action,string message)
    {try{action();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or FormatException){return;}throw new InvalidOperationException(message);}
    static void ColourRequire(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
