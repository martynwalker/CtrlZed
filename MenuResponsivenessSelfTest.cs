using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;

namespace CtrlZed;

public partial class MainWindow
{
    internal static int RunMenuResponsivenessSelfTest()
    {
        MainWindow? window=null;
        var report=new StringBuilder();
        try
        {
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window=new MainWindow();
            window.Show();
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
            PumpInput();
            window.CheckMenuResponsiveness(report);
            report.AppendLine("PASS: menus preserved the document, commands remained usable, and deferred grammar resumed after editing.");
            return 0;
        }
        catch(Exception ex)
        {
            report.AppendLine(ex.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedSelfTestFailure.txt"),ex.ToString());
            return 121;
        }
        finally
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CtrlZedMenuResponsiveness.txt"),report.ToString());
            if(window!=null)
            {
                window.closingApproved=true;
                window.recoveryTimer.Stop();
                window.uiRefreshTimer.Stop();
                window.grammarReviewTimer?.Stop();
                window.beta35PaginationTimer?.Stop();
                window.beta35PaginationCancellation?.Cancel();
                window.grammarAiCancellation?.Cancel();
                window.Close();
            }
        }
    }

    void CheckMenuResponsiveness(StringBuilder report)
    {
        languageSettings.GrammarAndStyleSuggestions=true;
        SpellCheck.SetIsEnabled(Editor,false);
        var document=new FlowDocument{PageWidth=pageWidth,PagePadding=pageMargins,ColumnWidth=double.PositiveInfinity};
        for(var i=0;i<600;i++)
        {
            var heading=i%12==0;
            document.Blocks.Add(new Paragraph(new Run(heading
                ?$"Menu responsiveness heading {i+1}"
                :$"This is is review sample {i+1}. The paragraph remains unchanged while the menus are opened repeatedly."))
            {
                FontSize=heading?22:16,
                FontWeight=heading?FontWeights.Bold:FontWeights.Normal,
                Margin=new Thickness(0,0,0,6)
            });
        }
        Editor.Document=document;
        Editor.CaretPosition=document.ContentStart;
        Editor.Focus();
        PumpInput();
        InitialiseBeta32GrammarReview();
        grammarReviewTimer?.Stop();
        RunLocalGrammarReview();
        PumpInput();
        if(grammarIssues.Count==0)throw new InvalidOperationException("The long-document grammar fixture produced no findings.");

        var review=MainMenu.Items.OfType<MenuItem>().FirstOrDefault(item=>NormalisedMenuHeader(item.Header)=="Review")
            ??throw new InvalidOperationException("The Review menu is missing.");
        var file=MainMenu.Items.OfType<MenuItem>().First(item=>NormalisedMenuHeader(item.Header)=="File");
        var edit=MainMenu.Items.OfType<MenuItem>().First(item=>NormalisedMenuHeader(item.Header)=="Edit");
        var format=MainMenu.Items.OfType<MenuItem>().First(item=>NormalisedMenuHeader(item.Header)=="Format");
        var beforeText=new TextRange(document.ContentStart,document.ContentEnd).Text;
        var analysisBeforeMenus=grammarAnalysisPasses;
        var measurements=new System.Collections.Generic.List<(string Menu,double Milliseconds)>();
        report.AppendLine($"Fixture: 600 paragraphs; {beforeText.Length:N0} characters; {grammarIssues.Count:N0} grammar findings.");

        for(var repeat=0;repeat<4;repeat++)
        {
            foreach(var menu in new[]{review,file,edit,format})
            {
                var elapsed=OpenMenuForRegression(menu);
                measurements.Add((NormalisedMenuHeader(menu.Header),elapsed));
                menu.IsSubmenuOpen=false;
            }
        }
        Editor.Focus();
        PumpInput();
        if(grammarAnalysisPasses!=analysisBeforeMenus)
            throw new InvalidOperationException("Opening and closing menus reran grammar analysis on an unchanged document.");
        if(new TextRange(document.ContentStart,document.ContentEnd).Text!=beforeText)
            throw new InvalidOperationException("Menu browsing changed document content.");

        foreach(var group in measurements.GroupBy(item=>item.Menu))
            report.AppendLine($"{group.Key}: {string.Join(", ",group.Select(item=>$"{item.Milliseconds:0} ms"))}");
        var sorted=measurements.Select(item=>item.Milliseconds).OrderBy(value=>value).ToArray();
        report.AppendLine($"Menu open incl. dispatcher: median {sorted[sorted.Length/2]:0} ms; maximum {sorted[^1]:0} ms.");
        if(sorted[^1]>2000)
            throw new InvalidOperationException($"Menu opening stalled for {sorted[^1]:0} ms on the long document.");

        // The routed formatting command must remain available after the focus
        // transitions; improving responsiveness must not disable menu commands.
        var body=(Paragraph)document.Blocks.FirstBlock.NextBlock;
        var run=(Run)body.Inlines.FirstInline;
        Editor.Selection.Select(run.ContentStart,run.ContentEnd);
        Editor.Focus();
        if(!EditingCommands.ToggleBold.CanExecute(null,Editor))
            throw new InvalidOperationException("Bold became unavailable after menu browsing.");
        EditingCommands.ToggleBold.Execute(null,Editor);
        if(!Equals(Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty),FontWeights.Bold))
            throw new InvalidOperationException("The Bold command stopped formatting selected text after menu browsing.");
        Editor.Undo();
        if(new TextRange(document.ContentStart,document.ContentEnd).Text!=beforeText)
            throw new InvalidOperationException("The formatting command changed document text.");

        // Make actual work due while a menu is open. It must wait, and must
        // subsequently finish after the menu closes rather than being dropped.
        body=(Paragraph)document.Blocks.FirstBlock.NextBlock;
        Editor.CaretPosition=body.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        Editor.Focus();
        TypeInput(" could of");
        var analysisBeforeEdit=grammarAnalysisPasses;
        const string pendingStats="Pending menu regression statistics";
        DocumentStats.Text=pendingStats;
        uiRefreshTimer.Stop();
        uiRefreshTimer.Interval=TimeSpan.FromMilliseconds(15);
        grammarReviewTimer?.Stop();
        if(grammarReviewTimer!=null)grammarReviewTimer.Interval=TimeSpan.FromMilliseconds(15);
        OpenMenuForRegression(review);
        uiRefreshTimer.Start();
        grammarReviewTimer?.Start();
        PumpInput();
        if(grammarAnalysisPasses!=analysisBeforeEdit || DocumentStats.Text!=pendingStats)
            throw new InvalidOperationException("Document maintenance ran while the menu was open.");
        review.IsSubmenuOpen=false;
        Editor.Focus();
        WaitForMenuRegression(()=>grammarAnalysisPasses>analysisBeforeEdit && !grammarAnalysisRunning &&
            DocumentStats.Text!=pendingStats,TimeSpan.FromSeconds(6));
        if(!grammarIssues.Any(issue=>issue.Finding.RuleId=="modal-of"))
            throw new InvalidOperationException("Deferred grammar review did not analyse the newly edited text.");
        report.AppendLine($"Unchanged-menu grammar passes: {analysisBeforeMenus}; resumed after edit: {grammarAnalysisPasses}.");
    }

    static double OpenMenuForRegression(MenuItem menu)
    {
        var watch=Stopwatch.StartNew();
        menu.Focus();
        menu.IsSubmenuOpen=true;
        CommandManager.InvalidateRequerySuggested();
        PumpInput();
        if(!menu.IsSubmenuOpen)throw new InvalidOperationException("A menu failed to stay open: "+menu.Header);
        return watch.Elapsed.TotalMilliseconds;
    }

    static void WaitForMenuRegression(Func<bool> completed,TimeSpan timeout)
    {
        var watch=Stopwatch.StartNew();
        while(!completed())
        {
            if(watch.Elapsed>timeout)throw new InvalidOperationException("Deferred document maintenance did not resume after closing the menu.");
            PumpInput();
        }
    }
}
