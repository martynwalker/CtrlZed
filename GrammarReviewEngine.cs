using System.Text.RegularExpressions;

namespace CtrlZed;

public enum GrammarFindingKind
{
    Grammar,
    Style,
    Consistency
}

public sealed record GrammarFinding(
    int Start,
    int Length,
    string RuleId,
    GrammarFindingKind Kind,
    string Title,
    string Explanation,
    string Original,
    string? Replacement,
    bool FromAi=false);

public static class GrammarReviewEngine
{
    static readonly RegexOptions Rx=RegexOptions.CultureInvariant|RegexOptions.IgnoreCase;

    static readonly Dictionary<string,string> FormalContractions=new(StringComparer.OrdinalIgnoreCase)
    {
        ["can't"]="cannot",
        ["won't"]="will not",
        ["don't"]="do not",
        ["doesn't"]="does not",
        ["didn't"]="did not",
        ["isn't"]="is not",
        ["aren't"]="are not",
        ["wasn't"]="was not",
        ["weren't"]="were not",
        ["haven't"]="have not",
        ["hasn't"]="has not",
        ["hadn't"]="had not",
        ["shouldn't"]="should not",
        ["wouldn't"]="would not",
        ["couldn't"]="could not",
        ["it's"]="it is",
        ["we're"]="we are",
        ["you're"]="you are",
        ["they're"]="they are",
        ["I've"]="I have",
        ["we've"]="we have",
        ["they've"]="they have"
    };

    static readonly HashSet<string> RepetitionStopWords=new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","and","are","as","at","be","been","but","by","for","from","had","has","have",
        "he","her","his","i","if","in","is","it","its","not","of","on","or","our","she","that",
        "the","their","there","they","this","to","was","we","were","which","with","you","your"
    };

    public static IReadOnlyList<GrammarFinding> Analyse(string text,WritingModeProfile profile,bool isHeading=false)
    {
        if(string.IsNullOrWhiteSpace(text))return Array.Empty<GrammarFinding>();
        text=text.TrimEnd('\r','\n');
        if(text.Length==0)return Array.Empty<GrammarFinding>();

        var findings=new List<GrammarFinding>();
        var creative=profile.Mode is WritingMode.Poetry or WritingMode.Screenplay;
        var fiction=profile.Mode is WritingMode.Fiction or WritingMode.Novel or WritingMode.Novella or WritingMode.ShortStory;

        AddMatches(findings,text,new Regex(@"\b([\p{L}][\p{L}’'-]*)\s+\1\b",Rx),"duplicate-word",GrammarFindingKind.Grammar,
            "Repeated word","The same word appears twice in succession.",m=>m.Groups[1].Value);

        AddMatches(findings,text,new Regex(@"\b(could|would|should|might|must)\s+of\b",Rx),"modal-of",GrammarFindingKind.Grammar,
            "Use ‘have’, not ‘of’","After a modal verb, the auxiliary verb is ‘have’.",m=>m.Groups[1].Value+" have");

        AddMatches(findings,text,new Regex(@"\balot\b",Rx),"a-lot",GrammarFindingKind.Grammar,
            "Write ‘a lot’ as two words","In standard British English, ‘a lot’ is two words.",_=>"a lot");

        if(!creative)
        {
            AddMatches(findings,text,new Regex(@"(?<=\S)[ \t]{2,}(?=\S)"),"multiple-spaces",GrammarFindingKind.Consistency,
                "Multiple spaces","Use a single space between words unless the spacing is deliberate.",_=>" ");

            AddMatches(findings,text,new Regex(@"[ \t]+(?=[,.;:!?])"),"space-before-punctuation",GrammarFindingKind.Grammar,
                "Space before punctuation","Ordinary punctuation follows the preceding word without an intervening space.",_=>"");

            AddMatches(findings,text,new Regex(@"([,;:!?])([\p{L}])"),"missing-space-after-punctuation",GrammarFindingKind.Grammar,
                "Missing space after punctuation","Add a space after this punctuation mark.",m=>m.Groups[1].Value+" "+m.Groups[2].Value);

            AddMatches(findings,text,new Regex(@"([!?;,])\1+"),"repeated-punctuation",GrammarFindingKind.Consistency,
                "Repeated punctuation","Repeated punctuation is unusual in ordinary prose; retain it only when deliberate.",m=>m.Groups[1].Value);
        }

        if(!isHeading && profile.Mode!=WritingMode.Poetry)
        {
            foreach(Match m in Regex.Matches(text,@"(?:^|[.!?]\s+)[“”‘’'\"(\[]*(?<letter>[a-z])",RegexOptions.CultureInvariant))
            {
                var g=m.Groups["letter"];
                findings.Add(new GrammarFinding(g.Index,g.Length,"sentence-capital",GrammarFindingKind.Grammar,
                    "Sentence begins with a lower-case letter","Sentences normally begin with a capital letter.",g.Value,g.Value.ToUpperInvariant()));
            }
        }

        if(!creative && !fiction)
        {
            AddPhrase(findings,text,"due to the fact that","because","wordy-because","Wordy phrase",
                "‘Because’ usually conveys the same meaning more directly.");
            AddPhrase(findings,text,"at this point in time","now","wordy-now","Wordy phrase",
                "‘Now’ is usually clearer and shorter.");
            if(profile.Mode is WritingMode.Business or WritingMode.Technical or WritingMode.Academic or WritingMode.Legal or WritingMode.Journalism)
                AddPhrase(findings,text,"in order to","to","wordy-in-order-to","Wordy phrase",
                    "In most technical and formal prose, ‘to’ is sufficient.");
            AddPhrase(findings,text,"different than","different from","british-different-from","British English usage",
                "British English normally prefers ‘different from’.");
        }

        if(profile.Formality>=5 && !creative && !fiction)
        {
            var pattern="\\b(?:"+string.Join('|',FormalContractions.Keys.Select(Regex.Escape))+")\\b";
            foreach(Match m in Regex.Matches(text,pattern,Rx))
            {
                var replacement=FormalContractions.TryGetValue(m.Value,out var value)?MatchCase(m.Value,value):null;
                findings.Add(new GrammarFinding(m.Index,m.Length,"formal-contraction",GrammarFindingKind.Style,
                    "Contraction in formal writing","The selected writing mode favours the expanded form, although the contraction may be retained deliberately.",m.Value,replacement));
            }
        }

        if(!isHeading && profile.PassiveVoiceTolerance<=2 && !creative && !fiction)
        {
            var passive=new Regex(@"\b(?:is|are|was|were|be|been|being)\s+(?:(?:very|carefully|clearly|often|usually)\s+)?[\p{L}]+(?:ed|en)\b",Rx);
            foreach(Match m in passive.Matches(text).Cast<Match>().Take(6))
                findings.Add(new GrammarFinding(m.Index,m.Length,"possible-passive",GrammarFindingKind.Style,
                    "Possible passive voice","Passive voice is not wrong, but the selected writing mode favours naming the actor when that improves clarity.",m.Value,null));
        }

        if(!isHeading && profile.Mode!=WritingMode.Poetry)
            AddLongSentenceFindings(findings,text,SentenceLimit(profile));

        if(!creative && profile.RepetitionSensitivity>=4)
            AddRepetitionFindings(findings,text);

        if(!isHeading && !creative && RequiresTerminalPunctuation(profile) && WordCount(text)>=6)
        {
            var trimmed=text.TrimEnd();
            if(trimmed.Length>0 && trimmed[^1] is not ('.' or '?' or '!' or ':' or ';' or '”' or '’' or '\'' or '"'))
            {
                var start=Math.Max(0,trimmed.Length-Math.Min(12,trimmed.Length));
                findings.Add(new GrammarFinding(start,trimmed.Length-start,"terminal-punctuation",GrammarFindingKind.Grammar,
                    "Possible missing end punctuation","This paragraph reads as a complete sentence but has no closing punctuation.",trimmed[start..],trimmed[start..]+"."));
            }
        }

        return findings
            .Where(x=>x.Start>=0 && x.Length>0 && x.Start+x.Length<=text.Length)
            .GroupBy(x=>$"{x.RuleId}:{x.Start}:{x.Length}",StringComparer.Ordinal)
            .Select(g=>g.First())
            .OrderBy(x=>x.Start)
            .ThenBy(x=>x.Kind)
            .ToList();
    }

    static void AddMatches(List<GrammarFinding> list,string text,Regex regex,string rule,GrammarFindingKind kind,
        string title,string explanation,Func<Match,string?> replacement)
    {
        foreach(Match m in regex.Matches(text))
            list.Add(new GrammarFinding(m.Index,m.Length,rule,kind,title,explanation,m.Value,replacement(m)));
    }

    static void AddPhrase(List<GrammarFinding> list,string text,string phrase,string replacement,string rule,string title,string explanation)
    {
        foreach(Match m in Regex.Matches(text,@"\b"+Regex.Escape(phrase)+@"\b",Rx))
            list.Add(new GrammarFinding(m.Index,m.Length,rule,GrammarFindingKind.Style,title,explanation,m.Value,MatchCase(m.Value,replacement)));
    }

    static string MatchCase(string original,string replacement)
    {
        if(original.Length>0 && char.IsUpper(original[0]))
            return char.ToUpperInvariant(replacement[0])+replacement[1..];
        return replacement;
    }

    static int SentenceLimit(WritingModeProfile profile)=>profile.Mode switch
    {
        WritingMode.FormalLetter or WritingMode.Business or WritingMode.Technical or WritingMode.Academic or WritingMode.Legal or WritingMode.Journalism=>35,
        WritingMode.Email or WritingMode.Article or WritingMode.General or WritingMode.PersonalLetter=>45,
        WritingMode.Speech=>55,
        WritingMode.Fiction or WritingMode.Novel or WritingMode.Novella or WritingMode.ShortStory or WritingMode.Screenplay=>70,
        _=>int.MaxValue
    };

    static void AddLongSentenceFindings(List<GrammarFinding> list,string text,int limit)
    {
        if(limit==int.MaxValue)return;
        foreach(Match m in Regex.Matches(text,@"[^.!?\r\n]+(?:[.!?]+|$)"))
        {
            var words=WordCount(m.Value);
            if(words<=limit)continue;
            var leading=m.Value.Length-m.Value.TrimStart().Length;
            var length=Math.Min(24,m.Length-leading);
            if(length<=0)continue;
            list.Add(new GrammarFinding(m.Index+leading,length,"long-sentence",GrammarFindingKind.Style,
                "Long sentence",$"This sentence contains {words} words. The selected writing mode normally benefits from shorter units, although rhythm and meaning take priority.",m.Value.Trim(),null));
        }
    }

    static void AddRepetitionFindings(List<GrammarFinding> list,string text)
    {
        var words=Regex.Matches(text,@"\b[\p{L}][\p{L}’'-]{3,}\b")
            .Cast<Match>()
            .Where(m=>!RepetitionStopWords.Contains(m.Value))
            .GroupBy(m=>m.Value,StringComparer.OrdinalIgnoreCase);

        foreach(var group in words)
        {
            var occurrences=group.ToList();
            if(occurrences.Count<5)continue;
            var m=occurrences[^1];
            list.Add(new GrammarFinding(m.Index,m.Length,"repeated-term",GrammarFindingKind.Style,
                "Frequent repetition",$"‘{m.Value}’ appears {occurrences.Count} times in this paragraph. Repetition may be intentional; review it in the context of the selected writing mode.",m.Value,null));
        }
    }

    static int WordCount(string text)=>Regex.Matches(text,@"\b[\p{L}\p{N}’'-]+\b").Count;

    static bool RequiresTerminalPunctuation(WritingModeProfile profile)=>profile.Mode is
        WritingMode.FormalLetter or WritingMode.Business or WritingMode.Technical or WritingMode.Academic or
        WritingMode.Legal or WritingMode.Journalism or WritingMode.Article;
}
