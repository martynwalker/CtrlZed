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
            if(contextualSpellingTimer==null ||
               (!languageSettings.ContextualSpellingSuggestions && !languageSettings.ContextualSpellingAutoCorrect))return;
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
        if(!SentenceCompletionDetector.TryFindCompletedSentence(paragraphToCaret,paragraphToCaret.Length,out var sentence))return;

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
                var correction=ChooseHighConfidenceCorrection(original,suggestions);
                if(correction!=null)
                {
                    errorRange.Text=correction;
                    MarkDirty();
                    if(languageSettings.ShowAutomaticCorrectionNotice)
                        Status.Text=$"Spelling corrected after sentence completion: {original} → {correction}. Ctrl+Z to undo.";
                    return; // one visible, immediately undoable correction per completed sentence
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

    static string? ChooseHighConfidenceCorrection(string original,IReadOnlyList<string> suggestions)
    {
        if(string.IsNullOrWhiteSpace(original) || original.Length<4 || original.Length>30)return null;
        if(original.Any(char.IsDigit) || original.Any(ch=>ch is '\'' or '’' or '-'))return null;
        if(original.Any(char.IsUpper))return null; // names, acronyms and sentence-initial words remain suggestions

        var ranked=suggestions
            .Select(x=>x.Trim())
            .Where(x=>x.Length>0 && !x.Contains(' ') && !x.Contains('-') && !x.Contains('\''))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x=>(Word:x,Distance:DamerauLevenshtein(original,x)))
            .OrderBy(x=>x.Distance)
            .ThenBy(x=>x.Word.Length)
            .ToList();
        if(ranked.Count==0)return null;

        var best=ranked[0];
        if(best.Distance>1)return null;
        if(ranked.Count>1 && ranked[1].Distance<=best.Distance)return null;
        if(Math.Abs(best.Word.Length-original.Length)>1)return null;
        return best.Word;
    }

    static int DamerauLevenshtein(string source,string target)
    {
        source=source.ToLowerInvariant();target=target.ToLowerInvariant();
        var d=new int[source.Length+1,target.Length+1];
        for(var i=0;i<=source.Length;i++)d[i,0]=i;
        for(var j=0;j<=target.Length;j++)d[0,j]=j;
        for(var i=1;i<=source.Length;i++)
        {
            for(var j=1;j<=target.Length;j++)
            {
                var cost=source[i-1]==target[j-1]?0:1;
                d[i,j]=Math.Min(
                    Math.Min(d[i-1,j]+1,d[i,j-1]+1),
                    d[i-1,j-1]+cost);
                if(i>1 && j>1 && source[i-1]==target[j-2] && source[i-2]==target[j-1])
                    d[i,j]=Math.Min(d[i,j],d[i-2,j-2]+1);
            }
        }
        return d[source.Length,target.Length];
    }
}
