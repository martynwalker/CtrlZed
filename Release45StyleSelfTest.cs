using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CtrlZed;

public partial class MainWindow
{
    internal static int RunRelease45StyleSelfTest()
    {
        MainWindow? window=null;
        var report=new StringBuilder();
        var settingsRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
        var settings=new Dictionary<string,byte[]?>();
        foreach(var name in new[]{"style-sets.json","settings.json"})
        {
            var path=Path.Combine(settingsRoot,name);
            settings[path]=File.Exists(path)?File.ReadAllBytes(path):null;
        }
        var temporary=Path.Combine(Path.GetTempPath(),"CtrlZed45Styles_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            VerifyRelease45StyleModel(report);
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow();
            window.Show();
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.recoveryTimer.Stop();
            window.languageSettings.GrammarAndStyleSuggestions=false;
            window.languageSettings.PoetryRhymeSuggestions=false;
            window.grammarReviewTimer?.Stop();
            SpellCheck.SetIsEnabled(window.Editor,false);
            window.NavigationPane.Visibility=Visibility.Collapsed;
            window.ApplyBeta35ViewMode(EditorViewMode.Continuous,false);
            window.CheckRelease45StyleBehaviour(temporary,report);
            report.AppendLine("PASS: custom styles, paragraph settings, native/DOCX persistence and Return/macro following styles.");
            return 0;
        }
        catch(Exception ex)
        {
            report.AppendLine(ex.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());
            return 122;
        }
        finally
        {
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();
                window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();
                window.beta35PaginationTimer?.Stop();
                window.beta35PaginationCancellation?.Cancel();
                window.Close();
            }
            foreach(var pair in settings)
            {
                if(pair.Value==null)
                {
                    if(File.Exists(pair.Key))File.Delete(pair.Key);
                }
                else File.WriteAllBytes(pair.Key,pair.Value);
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZed45StyleSelfTest.txt"),report.ToString());
            try { Directory.Delete(temporary,true); } catch { }
        }
    }

    static void VerifyRelease45StyleModel(StringBuilder report)
    {
        var old=JsonSerializer.Deserialize<StyleSetStore.StyleSet>(
            "{\"Name\":\"Legacy\",\"Styles\":{\"Normal\":{\"FontFamily\":\"Arial\",\"SizePt\":10},\"Heading 1\":{\"SizePt\":18,\"Bold\":true}}}")!;
        var legacy=StyleSetStore.NormalizeSet(old);
        RequireRelease45Style(StyleSetStore.ParagraphStyleNames.All(legacy.Styles.ContainsKey),"Legacy sets lost a built-in style.");
        RequireRelease45Style(legacy.Styles["Normal"].FontFamily=="Arial" && legacy.Styles["Normal"].SizePt==10,
            "Legacy paragraph defaults overwrote the existing font choice.");
        RequireRelease45Style(legacy.Styles["Normal"].FirstLineIndent==0 && legacy.Styles["Normal"].LineHeight==0 &&
            !legacy.Styles["Normal"].KeepWithNext && legacy.Styles["Heading 1"].OutlineLevel==1,
            "Legacy JSON did not receive compatible paragraph defaults and heading identity.");

        var set=CreateRelease45StyleFixture();
        var copy=StyleSetStore.NormalizeSet(JsonSerializer.Deserialize<StyleSetStore.StyleSet>(JsonSerializer.Serialize(set))!);
        RequireRelease45Style(StyleSetStore.StyleNames(copy).Count==8,"The style catalogue is still limited to six entries.");
        RequireRelease45Style(StyleSetStore.ResolveStyleName(copy,"body note")=="Book Body" &&
            StyleSetStore.ResolveStyleName(copy,copy.Styles["Book Body"].Id)=="Book Body",
            "Renamed style aliases or stable identifiers were not preserved by JSON.");
        RequireRelease45Style(StyleSetStore.NormalizeFollowingStyle("body note",copy)=="Book Body" &&
            StyleSetStore.NormalizeFollowingStyle("unknown style",copy)=="Normal",
            "Following-style resolution did not recognise a custom style or handle an unavailable style.");
        var clonedHeading=StyleSetStore.CreateStyle("Another heading",copy.Styles["Section Lead"]);
        RequireRelease45Style(clonedHeading.Id!=copy.Styles["Section Lead"].Id && clonedHeading.OutlineLevel==1,
            "A new derived heading lost its outline role or reused another style's identity.");
        var invalid=StyleSetStore.Clone(copy.Styles["Book Body"]);
        invalid.LineHeight=double.PositiveInfinity;
        var rejected=false;
        try { StyleSetStore.ValidateStyle(invalid); }
        catch(InvalidOperationException) { rejected=true; }
        RequireRelease45Style(rejected,"Invalid paragraph measurements were accepted.");
        report.AppendLine("Legacy JSON, eight-style catalogue, renamed macro aliases, stable IDs and paragraph validation passed.");
    }

    static StyleSetStore.StyleSet CreateRelease45StyleFixture()
    {
        var set=StyleSetStore.Clone(StyleSetStore.Defaults()["Default"],"Release 45 Test");
        var body=StyleSetStore.CreateStyle("Body Note",set.Styles["Normal"]);
        body.SizePt=10.5;
        body.Colour="#203040";
        body.Italic=true;
        body.Underline=true;
        body.MarginLeft=18;
        body.MarginRight=12;
        body.MarginTop=7;
        body.MarginBottom=9;
        body.FirstLineIndent=-6;
        body.LineHeight=22;
        body.KeepTogether=false;
        body.KeepWithNext=false;
        body.BreakPageBefore=true;
        body.FollowingStyle="Normal";
        set.Styles["Book Body"]=body;
        set.Aliases["Body Note"]=body.Id;
        set.Aliases["Body, old name"]=body.Id;
        var lead=StyleSetStore.CreateStyle("Section Lead",set.Styles["Heading 1"]);
        lead.SizePt=19;
        lead.Colour="#006400";
        lead.MarginLeft=6;
        lead.MarginRight=3;
        lead.MarginTop=12;
        lead.MarginBottom=5;
        lead.FirstLineIndent=4;
        lead.LineHeight=28;
        lead.KeepWithNext=true;
        lead.KeepTogether=true;
        lead.FollowingStyle="Body Note";
        set.Styles["Section Lead"]=lead;
        return StyleSetStore.NormalizeSet(set);
    }

    void CheckRelease45StyleBehaviour(string temporary,StringBuilder report)
    {
        var set=CreateRelease45StyleFixture();
        styleSets.Clear();
        styleSets[set.Name]=set;
        activeStyleSet=set.Name;
        RefreshStyleSetBox();
        RefreshParagraphStyleBox();
        RequireRelease45Style(StyleBox.Items.OfType<ComboBoxItem>().Any(item=>Equals(item.Content,"Section Lead")) &&
            StyleBox.Items.OfType<ComboBoxItem>().Any(item=>Equals(item.Content,"Book Body")),
            "The document toolbar does not offer custom styles.");

        var variant=StyleSetStore.Clone(set,"Variant Test");
        var variantBody=variant.Styles["Book Body"];
        variant.Styles.Remove("Book Body");
        variant.Styles["Alternate Body"]=variantBody;
        variant.Aliases["Book Body"]=variantBody.Id;
        variant=StyleSetStore.NormalizeSet(variant);
        styleSets[variant.Name]=variant;
        activeStyleSet=variant.Name;
        RefreshStyleSetBox();
        var variantParagraph=PrepareStyledInputParagraph("Alternate Body","Variant body");
        VerifyRelease45Paragraph(variantParagraph,"Alternate Body",variantBody,"renamed style in a copied set");
        activeStyleSet=set.Name;
        RefreshStyleSetBox();
        RefreshParagraphStyleBox();
        report.AppendLine("Copied sets resolve shared style identities using the active set's current names.");

        foreach(var path in new[]{"physical Return","safe macro","WPF command"})
        {
            var heading=PrepareStyledInputParagraph("Section Lead","Custom heading");
            VerifyRelease45Paragraph(heading,"Section Lead",set.Styles["Section Lead"],"before paragraph insertion");
            if(path=="physical Return")PressReturn();
            else if(path=="safe macro")RunSafeMacroAsync("NEW_PARAGRAPH").GetAwaiter().GetResult();
            else EditingCommands.EnterParagraphBreak.Execute(null,Editor);
            TypeInput("Body words");
            VerifyRelease45Paragraph(Editor.CaretPosition.Paragraph!,"Book Body",set.Styles["Book Body"],
                path);
        }
        report.AppendLine("Physical Return and the macro paragraph path both selected the custom following style with its paragraph settings.");

        var document=new FlowDocument();
        var first=new Paragraph(new Run("Custom heading"));
        var second=new Paragraph(new Run("Body words"));
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var cellParagraph=new Paragraph(new Run("Cell words"));
        var table=new Table();
        var group=new TableRowGroup();
        var row=new TableRow();
        row.Cells.Add(new TableCell(cellParagraph));
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        var trailing=new Paragraph(new Run("Trailing words"));
        document.Blocks.Add(trailing);
        Editor.Document=document;
        foreach(var item in new[]{(first,"Section Lead"),(second,"Book Body"),(cellParagraph,"Body Note"),(trailing,"Normal")})
        {
            Editor.Selection.Select(item.Item1.ContentStart,item.Item1.ContentEnd);
            ApplyParagraphStyle(item.Item2);
        }
        var beforeText=new TextRange(document.ContentStart,document.ContentEnd).Text;
        using var packageStream=new MemoryStream();
        WriteCzdArchive(packageStream);
        packageStream.Position=0;
        var package=ReadCzdArchive(packageStream);
        styleSets.Clear();
        foreach(var entry in StyleSetStore.Defaults())styleSets[entry.Key]=entry.Value;
        activeStyleSet="Default";
        Editor.Document=package.Document;
        if(package.Settings==null)throw new InvalidOperationException("The native document lost its settings.");
        ApplyRecoveredSettings(package.Settings);
        RequireRelease45Style(new TextRange(Editor.Document.ContentStart,Editor.Document.ContentEnd).Text==beforeText,
            "The native custom-style document changed text while reopening.");
        RequireRelease45Style(StyleSetStore.ResolveStyleName(styleSets[activeStyleSet],"Body Note")=="Book Body",
            "The native document did not restore its custom style definitions and renamed macro alias.");
        var restored=EnumerateDocumentParagraphs().ToArray();
        RequireRelease45Style(restored.Length==4,"The native document lost or added a paragraph around the table.");
        VerifyRelease45Paragraph(restored[0],"Section Lead",set.Styles["Section Lead"],"native heading");
        VerifyRelease45Paragraph(restored[1],"Book Body",set.Styles["Book Body"],"native body");
        VerifyRelease45Paragraph(restored[2],"Book Body",set.Styles["Book Body"],"native table cell");
        RequireRelease45Style(InferParagraphStyle(restored[3],styleSets.Values)=="Normal","The native document lost the trailing Normal paragraph role.");
        report.AppendLine("CZD save/reopen restored custom definitions, renamed aliases and paragraph roles inside a table.");

        var docx=Path.Combine(temporary,"custom-styles.docx");
        PrepareDocumentStylesForInterop();
        DocxInterop.Save(docx,Editor.Document);
        using(var validationPackage=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docx,false))
        {
            var errors=new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(validationPackage).Take(3).ToArray();
            RequireRelease45Style(errors.Length==0,"The custom-style DOCX is invalid: "+
                string.Join("; ",errors.Select(error=>error.Description)));
        }
        using(var sourcePackage=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docx,true))
        {
            var definition=sourcePackage.MainDocumentPart!.StyleDefinitionsPart!.Styles!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.Style>()
                .First(style=>style.StyleName?.Val?.Value=="Book Body");
            definition.StyleParagraphProperties!.AddChild(new DocumentFormat.OpenXml.Wordprocessing.Tabs(
                new DocumentFormat.OpenXml.Wordprocessing.TabStop
                {
                    Val=DocumentFormat.OpenXml.Wordprocessing.TabStopValues.Right,Position=1440
                }),true);
            definition.StyleRunProperties!.AddChild(new DocumentFormat.OpenXml.Wordprocessing.Languages{Val="en-GB"},true);
            sourcePackage.MainDocumentPart.StyleDefinitionsPart.Styles.Save();
        }
        Editor.Document=DocxInterop.Load(docx);
        AdoptImportedDocumentStyles();
        RequireRelease45Style(StyleSetStore.ResolveStyleName(styleSets[activeStyleSet],"Body Note")=="Book Body",
            "The DOCX document lost the alias used by a renamed style's existing macros.");
        RequireRelease45Style(StyleSetStore.ResolveStyleName(styleSets[activeStyleSet],"Body, old name")=="Book Body",
            "The DOCX document lost punctuation in a renamed style's macro alias.");
        restored=EnumerateDocumentParagraphs().ToArray();
        RequireRelease45Style(restored.Length==4,"The custom-style DOCX lost a paragraph.");
        VerifyRelease45Paragraph(restored[0],"Section Lead",set.Styles["Section Lead"],"DOCX heading",0.15);
        VerifyRelease45Paragraph(restored[1],"Book Body",set.Styles["Book Body"],"DOCX body",0.15);
        VerifyRelease45Paragraph(restored[2],"Book Body",set.Styles["Book Body"],"DOCX table cell",0.15);
        RequireRelease45Style(InferParagraphStyle(restored[3],styleSets.Values)=="Normal","The DOCX document lost the trailing Normal paragraph role.");
        var roundTrip=Path.Combine(temporary,"custom-styles-round-trip.docx");
        PrepareDocumentStylesForInterop();
        DocxInterop.Save(roundTrip,Editor.Document);
        using(var retainedPackage=DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(roundTrip,false))
        {
            var definition=retainedPackage.MainDocumentPart!.StyleDefinitionsPart!.Styles!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.Style>()
                .First(style=>style.StyleName?.Val?.Value=="Book Body");
            RequireRelease45Style(definition.StyleParagraphProperties?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Tabs>()!=null &&
                definition.StyleRunProperties?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Languages>()?.Val?.Value=="en-GB",
                "Saving an imported style discarded its original tab stops or language.");
            var errors=new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(retainedPackage).Take(3).ToArray();
            RequireRelease45Style(errors.Length==0,"The saved imported-style DOCX is invalid: "+
                string.Join("; ",errors.Select(error=>error.Description)));
        }
        report.AppendLine("Custom DOCX style names and paragraph settings survived save/import.");
    }

    void VerifyRelease45Paragraph(Paragraph paragraph,string name,StyleSetStore.TextStyle style,string context,double tolerance=0.01)
    {
        var actual=InferParagraphStyle(paragraph,styleSets.Values);
        RequireRelease45Style(actual==name,$"The {context} paragraph lost its named style: expected {name}, found {actual??"none"}.");
        bool Near(double a,double b)=>Math.Abs(a-b)<=tolerance;
        RequireRelease45Style(Near(paragraph.Margin.Left,style.MarginLeft) && Near(paragraph.Margin.Top,style.MarginTop) &&
            Near(paragraph.Margin.Right,style.MarginRight) && Near(paragraph.Margin.Bottom,style.MarginBottom) &&
            Near(paragraph.TextIndent,style.FirstLineIndent) &&
            (style.LineHeight==0?double.IsNaN(paragraph.LineHeight):Near(paragraph.LineHeight,style.LineHeight)) &&
            paragraph.KeepTogether==style.KeepTogether && paragraph.KeepWithNext==style.KeepWithNext &&
            paragraph.BreakPageBefore==style.BreakPageBefore,
            $"Paragraph settings changed in {context}: margin={paragraph.Margin}, indent={paragraph.TextIndent}, line={paragraph.LineHeight}, keep={paragraph.KeepTogether}/{paragraph.KeepWithNext}, break={paragraph.BreakPageBefore}.");
        var range=new TextRange(paragraph.ContentStart,paragraph.ContentEnd);
        RequireRelease45Style(Equals(range.GetPropertyValue(TextElement.FontWeightProperty),style.Bold?FontWeights.Bold:FontWeights.Normal) &&
            Equals(range.GetPropertyValue(TextElement.FontStyleProperty),style.Italic?FontStyles.Italic:FontStyles.Normal) &&
            range.GetPropertyValue(TextElement.FontSizeProperty) is double size && Near(size,style.SizePt*96/72),
            "Character formatting changed in "+context+".");
        var expected=(Color)ColorConverter.ConvertFromString(style.Colour)!;
        RequireRelease45Style((range.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush)?.Color==expected,
            "Text colour changed in "+context+".");
    }

    static void RequireRelease45Style(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }
}
