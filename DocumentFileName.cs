using System.Text;

namespace CtrlZed;

internal static class DocumentFileName
{
    internal const string Fallback="Untitled";
    internal const int MaximumStemLength=120;

    static readonly HashSet<string> Abbreviations=new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr","Mrs","Ms","Dr","Prof","Rev","St","No","Nos","Fig","Figs",
        "Eq","Eqs","Vol","Ch","Ltd","Co","Inc","etc","e.g","i.e","vs"
    };

    internal static string Suggest(IEnumerable<(string Text,bool IsHeading)> paragraphs)
    {
        string? heading=null;
        var opening=new StringBuilder();
        foreach(var paragraph in paragraphs)
        {
            var text=NormaliseWhitespace(paragraph.Text);
            if(text.Length==0)continue;
            if(paragraph.IsHeading)
            {
                heading=text;
                break;
            }
            // Only the opening can contribute to a 120-character filename.
            // Keep looking for a heading even after collecting that opening.
            if(opening.Length<8192)
            {
                if(opening.Length>0)opening.Append(' ');
                opening.Append(text.AsSpan(0,Math.Min(text.Length,8192-opening.Length)));
            }
        }
        return SanitiseStem(heading??FirstSentence(opening.ToString()));
    }

    internal static string FirstSentence(string? value)
    {
        var text=NormaliseWhitespace(value);
        for(var index=0;index<text.Length;index++)
        {
            if(text[index] is not ('.' or '?' or '!'))continue;
            var end=index+1;
            while(end<text.Length && (text[end] is '.' or '?' or '!'))end++;
            while(end<text.Length && IsClosingPunctuation(text[end]))end++;
            if(end<text.Length && !char.IsWhiteSpace(text[end]))continue;
            if(text[index]=='.' && IsAbbreviationOrNumber(text,index))continue;
            return text[..end].Trim();
        }
        return text;
    }

    internal static string SanitiseStem(string? value)
    {
        var clean=new StringBuilder();
        var pendingSpace=false;
        foreach(var ch in value??"")
        {
            if(char.IsWhiteSpace(ch) || char.IsControl(ch) || ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' or '\uFFFC' or '\u200B' or '\uFEFF')
            {
                pendingSpace=clean.Length>0;
                continue;
            }
            if(pendingSpace)clean.Append(' ');
            clean.Append(ch);
            pendingSpace=false;
        }
        var result=clean.ToString().Trim().TrimEnd('.', ' ');
        if(result.Length==0 || result is "." or "..")return Fallback;

        // Windows device names remain reserved even when followed by an
        // extension (for example CON.txt), independent of the host platform.
        var device=result.Split('.')[0].TrimEnd();
        if(IsReservedDevice(device))result="_"+result;
        if(result.Length>MaximumStemLength)
        {
            result=result[..MaximumStemLength];
            if(result.Length>0 && char.IsHighSurrogate(result[^1]))result=result[..^1];
            result=result.TrimEnd('.', ' ');
        }
        return result;
    }

    internal static string WithExtension(string stem,string? extension=".czd")
    {
        var suffix=(extension??"").Trim().TrimStart('.');
        if(suffix.Length==0 || suffix.Length>10 || !suffix.All(char.IsLetterOrDigit))suffix="czd";
        suffix="."+suffix;
        return stem.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)?stem:stem+suffix;
    }

    static string NormaliseWhitespace(string? value)
        =>string.Join(" ",(value??"").Replace('\uFFFC',' ').Replace('\u200B',' ').Replace('\uFEFF',' ')
            .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));

    static bool IsClosingPunctuation(char ch)=>ch is '"' or '\'' or '”' or '’' or ')' or ']' or '}';

    static bool IsAbbreviationOrNumber(string text,int index)
    {
        if(index>0 && index+1<text.Length && char.IsDigit(text[index-1]) && char.IsDigit(text[index+1]))return true;
        if(index>0 && text[..index].All(char.IsDigit))return true;
        var start=index-1;
        while(start>=0 && (char.IsLetter(text[start]) || text[start]=='.'))start--;
        var token=text[(start+1)..index].Trim('.');
        if(Abbreviations.Contains(token))return true;
        if(token.Length==1 && char.IsUpper(token[0]))return true;
        var initials=token.Split('.',StringSplitOptions.RemoveEmptyEntries);
        return initials.Length>1 && initials.All(part=>part.Length==1 && char.IsLetter(part[0]));
    }

    static bool IsReservedDevice(string value)
    {
        if(value.Equals("CON",StringComparison.OrdinalIgnoreCase) || value.Equals("PRN",StringComparison.OrdinalIgnoreCase) ||
           value.Equals("AUX",StringComparison.OrdinalIgnoreCase) || value.Equals("NUL",StringComparison.OrdinalIgnoreCase) ||
           value.Equals("CONIN$",StringComparison.OrdinalIgnoreCase) || value.Equals("CONOUT$",StringComparison.OrdinalIgnoreCase))return true;
        if(value.Length!=4 || (!value.StartsWith("COM",StringComparison.OrdinalIgnoreCase) && !value.StartsWith("LPT",StringComparison.OrdinalIgnoreCase)))return false;
        return value[3] is >= '1' and <= '9' or '¹' or '²' or '³';
    }
}
