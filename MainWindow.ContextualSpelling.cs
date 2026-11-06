using System.Threading;
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
    CancellationTokenSource? contextualSpellingCts;
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
        contextualSpellingTimer.Tick+=async (_,__)=>
        {
            contextualSpellingTimer.Stop();
            contextualSpellingCts?.Cancel();
            contextualSpellingCts=new CancellationTokenSource();
            if(!languageSettings.SpellingEnabled || !languageSettings.ContextualSpellingAutoCorrect)return;
            try{await ApplyHighConfidenceSentenceCorrectionAsync(contextualSpellingCts.Token);}
            catch(OperationCanceledException){ }
            catch(Exception ex)
            {
                if(languageSettings.ShowAutomaticCorrectionNotice)
                    Status.Text="Contextual spelling check was not completed: "+ex.Message;
            }
        };
        Editor.TextChanged+=(_,__)=>
        {
            contextualSpellingCts?.Cancel();
            if(applyingContextualCorrection || contextualSpellingTimer==null ||
               !languageSettings.ContextualSpellingAutoCorrect)return;
            contextualSpellingTimer.Stop();
            contextualSpellingTimer.Interval=TimeSpan.FromMilliseconds(languageSettings.SentenceCompletionDelayMs);
            contextualSpellingTimer.Start();
        };
    }

    async Task ApplyHighConfidenceSentenceCorrectionAsync(CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            var errorRange=Editor.GetSpellingErrorRange(position);
            var spellingError=Editor.GetSpellingError(position);
            if(errorRange==null || spellingError==null)break;

            var errorStart=new TextRange(paragraph.ContentStart,errorRange.Start).Text.Length;
            var errorEnd=errorStart+errorRange.Text.Length;
            if(errorStart>=sentence.Start && errorEnd<=sentenceEnd)
            {
                var original=errorRange.Text.Trim();
                var suggestions=spellingError.Suggestions.Cast<string>()
                    .Select(x=>x.Trim())
                    .Where(x=>x.Length>0 && !x.Contains(' ') && !x.Contains('-'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();
                var correction=ContextualSpellingEngine.ChooseHighConfidenceCorrection(original,suggestions);
                if(correction!=null)
                {
                    ApplyContextualCorrection(errorRange,original,correction,"local high-confidence spelling");
                    return;
                }

                if(languageSettings.ContextualSpellingSuggestions &&
                   languageSettings.AllowAiLanguageServices &&
                   suggestions.Count>0)
                {
                    var provider=AiManager.GetDefaultProvider();
                    var profile=AiDirectService.GetProfile(provider);
                    if(provider!="Copilot" && profile.HasKey)
                    {
                        if(languageSettings.ShowAutomaticCorrectionNotice)
                            Status.Text=$"Checking ‘{original}’ against the completed sentence…";
                        var prompt=ContextualSpellingEngine.BuildAiChoicePrompt(
                            sentence.Text,original,suggestions,WritingModeProfiles.Get(languageSettings.WritingMode).DisplayName);
                        var response=await AiDirectService.GenerateAsync(provider,prompt,false,cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if(!string.Equals(errorRange.Text.Trim(),original,StringComparison.Ordinal))return;
                        var aiChoice=ContextualSpellingEngine.ParseAiChoice(response,suggestions);
                        if(aiChoice!=null)
                            ApplyContextualCorrection(errorRange,original,aiChoice,$"{provider} context check");
                        else if(languageSettings.ShowAutomaticCorrectionNotice)
                            Status.Text=$"No unambiguous contextual correction was made for ‘{original}’.";
                        return; // at most one AI request per completed sentence
                    }
                }
            }

            var next=errorRange.End.GetNextInsertionPosition(LogicalDirection.Forward);
            if(next==null || next.CompareTo(caret)>=0)break;
            position=Editor.GetNextSpellingErrorPosition(next,LogicalDirection.Forward);
        }
    }

    void ApplyContextualCorrection(TextRange range,string original,string correction,string source)
    {
        try
        {
            applyingContextualCorrection=true;
            range.Text=correction;
            MarkDirty();
            if(languageSettings.ShowAutomaticCorrectionNotice)
                Status.Text=$"Spelling corrected after sentence completion: {original} → {correction} ({source}). Ctrl+Z to undo.";
        }
        finally{applyingContextualCorrection=false;}
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
