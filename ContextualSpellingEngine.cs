namespace CtrlZed;

public readonly record struct SentenceBoundary(int Start,int Length,string Text);

public static class ContextualSpellingEngine
{
    static readonly HashSet<string> CommonAbbreviations=new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr","Mrs","Ms","Dr","Prof","Rev","St","No","Nos","Fig","Figs","Eq","Eqs","Vol","Ch","Ltd","Co","Inc","etc","e.g","i.e"
    };

    public static bool TryFindCompletedSentence(string text,int caretOffset,out SentenceBoundary sentence)
    {
        sentence=default;
        if(string.IsNullOrEmpty(text) || caretOffset<2)return false;
        caretOffset=Math.Clamp(caretOffset,0,text.Length);

        var whitespaceIndex=caretOffset-1;
        if(!char.IsWhiteSpace(text[whitespaceIndex]))return false;

        var punctuationIndex=whitespaceIndex-1;
        while(punctuationIndex>=0 && IsClosingPunctuation(text[punctuationIndex]))punctuationIndex--;
        if(punctuationIndex<0 || text[punctuationIndex] is not ('.' or '?' or '!'))return false;
        if(IsFalseBoundary(text,punctuationIndex))return false;

        var start=punctuationIndex-1;
        while(start>=0)
        {
            if(text[start] is '\n' or '\r')break;
            if(text[start] is '.' or '?' or '!')
            {
                var after=start+1;
                while(after<text.Length && IsClosingPunctuation(text[after]))after++;
                if(after<text.Length && char.IsWhiteSpace(text[after]) && !IsFalseBoundary(text,start))break;
            }
            start--;
        }
        start++;
        while(start<punctuationIndex && char.IsWhiteSpace(text[start]))start++;

        var end=punctuationIndex+1;
        while(end<caretOffset && IsClosingPunctuation(text[end]))end++;
        var length=end-start;
        if(length<2)return false;
        sentence=new SentenceBoundary(start,length,text.Substring(start,length));
        return true;
    }

    static bool IsClosingPunctuation(char ch)=>ch is '"' or '\'' or '”' or '’' or ')' or ']' or '}';

    static bool IsFalseBoundary(string text,int punctuationIndex)
    {
        if(text[punctuationIndex]!='.')return false;

        if(punctuationIndex>0 && punctuationIndex+1<text.Length &&
           char.IsDigit(text[punctuationIndex-1]) && char.IsDigit(text[punctuationIndex+1]))
            return true;

        if(punctuationIndex>0 && text[punctuationIndex-1]=='.')return true;
        if(punctuationIndex+1<text.Length && text[punctuationIndex+1]=='.')return true;

        var wordEnd=punctuationIndex;
        var wordStart=wordEnd-1;
        while(wordStart>=0 && (char.IsLetter(text[wordStart]) || text[wordStart]=='.'))wordStart--;
        var token=text[(wordStart+1)..wordEnd].Trim('.');
        if(CommonAbbreviations.Contains(token))return true;
        if(token.Length==1 && char.IsUpper(token[0]))return true;

        return false;
    }

    public static string? ChooseHighConfidenceCorrection(string original,IReadOnlyList<string> suggestions)
    {
        if(string.IsNullOrWhiteSpace(original) || original.Length<3 || original.Length>30)return null;
        if(original.Any(char.IsDigit) || original.Any(ch=>ch is '\'' or '’' or '-'))return null;
        if(original.Any(char.IsUpper))return null;

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

    internal static int DamerauLevenshtein(string source,string target)
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
