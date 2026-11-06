using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace CtrlZed;

public partial class MainWindow
{
    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ContextualSpellingWindowLoaded));
    }

    bool contextualSpellingInitialised;
    bool applyingContextualCorrection;
    DispatcherTimer? contextualSpellingTimer;
    string lastContextualSentence="";

    static void ContextualSpellingWindowLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is MainWindow window)window.InitialiseContextualSpelling();
    }

    void InitialiseContextualSpelling()
    {
        if(contextualSpellingInitialised)return;
        contextualSpellingInitialised=true;
        contextualSpellingTimer=new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval=TimeSpan.FromMilliseconds(languageSettings.SentenceCompletionDelayMs)
        };
        contextualSpellingTimer.Tick+=(_,__)=>
        {
            contextualSpellingTimer.Stop();
            if(languageSettings.SpellingEnabled && languageSettings.ContextualSpellingAutoCorrect)
                ApplyHighConfidenceSentenceCorrection();
        };
        Editor.TextChanged+=(_,__)=>
        {
            if(applyingContextualCorrection || contextualSpellingTimer==null ||
               !languageSettings.ContextualSpellingAutoCorrect)return;
            contextualSpellingTimer.Stop();
            contextualSpellingTimer.Interval=TimeSpan.FromMilliseconds(languageSettings.SentenceCompletionDelayMs);
            contextualSpellingTimer.Start();
        };
    }

    void ApplyHighConfidenceSentenceCorrection()
    {
        var caret=Editor.CaretPosition;
        var paragraph=FindContainingParagraph(caret);
        if(paragraph==null)return;

        var paragraphToCaret=new TextRange(paragraph.ContentStart,caret).Text;
        if(!ContextualSpellingEngine.TryFindCompletedSentence(paragraphToCaret,paragraphToCaret.Length,out var sentence))return;

        var fingerprint=$"{currentFile}|{paragraph.ContentStart.GetOffsetToPosition(caret)}|{sentence.Text}";
        if(string.Equals(fingerprint,lastContextualSentence,StringComparison.Ordinal))return;
        lastContextualSentence=fingerprint;

        var sentenceEnd=sentence.Start+sentence.Length;
        var position=Editor.GetNextSpellingErrorPosition(paragraph.ContentStart,LogicalDirection.Forward);
        while(position!=null && position.CompareTo(caret)<0)
        {
            var errorRange=Editor.GetSpellingErrorRange(position);
            var spellingError=Editor.GetSpellingError(position);
            if(errorRange==null || spellingError==null)break;

            var errorStart=new TextRange(paragraph.ContentStart,errorRange.Start).Text.Length;
            var errorEnd=errorStart+errorRange.Text.Length;
            if(errorStart>=sentence.Start && errorEnd<=sentenceEnd)
            {
                var original=errorRange.Text.Trim();
                var suggestions=spellingError.Suggestions.Cast<string>().Take(8).ToList();
                var correction=ContextualSpellingEngine.ChooseHighConfidenceCorrection(original,suggestions);
                if(correction!=null)
                {
                    try
                    {
                        applyingContextualCorrection=true;
                        errorRange.Text=correction;
                        MarkDirty();
                        if(languageSettings.ShowAutomaticCorrectionNotice)
                            Status.Text=$"Spelling corrected after sentence completion: {original} → {correction}. Ctrl+Z to undo.";
                    }
                    finally{applyingContextualCorrection=false;}
                    return;
                }
            }

            var next=errorRange.End.GetNextInsertionPosition(LogicalDirection.Forward);
            if(next==null || next.CompareTo(caret)>=0)break;
            position=Editor.GetNextSpellingErrorPosition(next,LogicalDirection.Forward);
        }
    }

    static Paragraph? FindContainingParagraph(TextPointer pointer)
    {
        object? current=pointer.Parent;
        while(current!=null)
        {
            if(current is Paragraph paragraph)return paragraph;
            current=current switch
            {
                FrameworkContentElement content=>content.Parent,
                FrameworkElement element=>element.Parent,
                _=>null
            };
        }
        return null;
    }
}
