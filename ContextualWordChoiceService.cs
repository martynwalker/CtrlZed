using System.Text.Json;

namespace CtrlZed;

public sealed record ThesaurusSuggestion(
    string Replacement,
    string Nuance,
    string Register,
    string Explanation,
    int? Syllables);

public sealed record RhymeSuggestion(
    int LineNumber,
    string Original,
    string Replacement,
    int RhymesWithLine,
    string RhymesWith,
    string RhymeType,
    string Explanation,
    int? Syllables);

public sealed record RhymeAnalysis(
    string Scheme,
    string Summary,
    IReadOnlyList<RhymeSuggestion> Opportunities);

public static class ContextualWordChoiceService
{
    public static async Task<IReadOnlyList<ThesaurusSuggestion>> SuggestThesaurusAsync(
        string target,
        string paragraph,
        WritingModeProfile profile,
        CancellationToken cancellationToken=default)
    {
        target=(target??"").Trim();
        paragraph=(paragraph??"").Trim();
        if(target.Length==0)throw new InvalidOperationException("Select a word or place the caret inside one first.");
        if(target.Length>80)throw new InvalidOperationException("The contextual thesaurus is intended for a word or short phrase, not a whole sentence.");
        if(paragraph.Length>6000)paragraph=paragraph[..6000];

        var provider=AiManager.GetDefaultProvider();
        if(string.Equals(provider,"Copilot",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Copilot is available only through browser handoff. Choose ChatGPT, Claude or Gemini as the direct AI provider for contextual word suggestions.");

        var prompt=$$"""
You are the conservative British-English contextual thesaurus inside CtrlZed.

WRITING MODE
Name: {{profile.DisplayName}}
Purpose: {{profile.Description}}
Voice preservation: {{profile.VoicePreservation}}/5

TASK
Suggest alternatives for the marked word or short phrase that fit its meaning in the supplied paragraph. Preserve grammatical form, tense, number, register and the writer's voice. Do not rewrite the paragraph. Prefer precise British-English usage. Weak or merely related words should be omitted.

Return JSON only, with no Markdown fences, in this exact form:
{"alternatives":[{"replacement":"word or short phrase","nuance":"brief distinction","register":"neutral|formal|informal|technical|literary|archaic|comic|gentler|stronger","explanation":"why it fits this exact context","syllables":2}]}

Rules:
- Maximum 12 alternatives.
- Do not repeat the original wording.
- A one-word target should normally receive one-word alternatives.
- Preserve capitalisation where appropriate.
- `syllables` may be null when uncertain.
- An empty alternatives array is better than poor advice.

MARKED WORD OR PHRASE
{{target}}

PARAGRAPH
{{paragraph}}
""";

        var response=await AiDirectService.GenerateAsync(provider,prompt,false,cancellationToken);
        return ParseThesaurusResponse(response,target);
    }

    public static async Task<RhymeAnalysis> AnalyseRhymeAsync(
        IReadOnlyList<string> lines,
        WritingModeProfile profile,
        CancellationToken cancellationToken=default)
    {
        if(lines==null || lines.Count<2)
            throw new InvalidOperationException("The Poetry Rhyme Assistant needs at least two non-empty lines in the current stanza.");
        if(lines.Count>12)
            throw new InvalidOperationException("The Poetry Rhyme Assistant reviews up to twelve lines at a time. Use a smaller stanza.");

        var numbered=string.Join(Environment.NewLine,lines.Select((line,index)=>$"{index+1}: {line}"));
        if(numbered.Length>7000)
            throw new InvalidOperationException("This stanza is too long for a focused rhyme review. Use a shorter selection.");

        var provider=AiManager.GetDefaultProvider();
        if(string.Equals(provider,"Copilot",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Copilot is available only through browser handoff. Choose ChatGPT, Claude or Gemini as the direct AI provider for rhyme assistance.");

        var prompt=$$"""
You are the restrained British-English poetry and rhyme assistant inside CtrlZed.

WRITING MODE
Name: {{profile.DisplayName}}
Purpose: {{profile.Description}}
Voice preservation: {{profile.VoicePreservation}}/5

TASK
Read the numbered stanza, infer its likely rhyme relationships, and identify only strong opportunities where replacing an exact word or short phrase in one line can preserve approximately the same meaning while making that line rhyme more effectively with another line.

Do not rewrite whole lines. Do not force rhyme at the expense of meaning, tone, metre or grammar. Exact rhyme, near rhyme and slant rhyme are all permitted, but label them honestly. Prefer sparse, useful suggestions. If there is no responsible suggestion, return none.

Return JSON only, with no Markdown fences, in this exact form:
{"scheme":"ABAB or free verse or uncertain","summary":"brief assessment","opportunities":[{"line":2,"original":"exact substring copied from that line","replacement":"proposed replacement","rhymesWithLine":4,"rhymesWith":"end word or sound in the partner line","rhymeType":"exact|near|slant","explanation":"meaning, rhyme and metre rationale","syllables":1}]}

Rules:
- Maximum 10 opportunities.
- `line` and `rhymesWithLine` are one-based line numbers from the supplied stanza.
- `original` must appear exactly in the stated line.
- Keep `original` as short as reasonably possible.
- Never return the same text as both original and replacement.
- `syllables` may be null when uncertain.
- Green marks in CtrlZed are reserved for genuinely useful opportunities, so omit speculative ideas.

STANZA
{{numbered}}
""";

        var response=await AiDirectService.GenerateAsync(provider,prompt,false,cancellationToken);
        return ParseRhymeResponse(response,lines);
    }

    internal static IReadOnlyList<ThesaurusSuggestion> ParseThesaurusResponse(string response,string original)
    {
        using var document=JsonDocument.Parse(ExtractJson(response));
        if(!document.RootElement.TryGetProperty("alternatives",out var alternatives) || alternatives.ValueKind!=JsonValueKind.Array)
            return Array.Empty<ThesaurusSuggestion>();

        var result=new List<ThesaurusSuggestion>();
        foreach(var item in alternatives.EnumerateArray().Take(12))
        {
            var replacement=GetString(item,"replacement");
            if(string.IsNullOrWhiteSpace(replacement) || replacement.Contains('\n') || replacement.Contains('\r'))continue;
            replacement=replacement.Trim();
            if(string.Equals(replacement,original,StringComparison.CurrentCultureIgnoreCase))continue;
            if(result.Any(x=>string.Equals(x.Replacement,replacement,StringComparison.CurrentCultureIgnoreCase)))continue;

            var nuance=GetString(item,"nuance")?.Trim();
            var register=GetString(item,"register")?.Trim();
            var explanation=GetString(item,"explanation")?.Trim();
            var syllables=GetNullableInt(item,"syllables");
            result.Add(new ThesaurusSuggestion(
                replacement,
                string.IsNullOrWhiteSpace(nuance)?"Alternative wording":nuance,
                string.IsNullOrWhiteSpace(register)?"neutral":register,
                string.IsNullOrWhiteSpace(explanation)?"Fits the supplied context.":explanation,
                syllables));
        }
        return result;
    }

    internal static RhymeAnalysis ParseRhymeResponse(string response,IReadOnlyList<string> lines)
    {
        using var document=JsonDocument.Parse(ExtractJson(response));
        var root=document.RootElement;
        var scheme=GetString(root,"scheme")?.Trim();
        var summary=GetString(root,"summary")?.Trim();
        var result=new List<RhymeSuggestion>();

        if(root.TryGetProperty("opportunities",out var opportunities) && opportunities.ValueKind==JsonValueKind.Array)
        {
            foreach(var item in opportunities.EnumerateArray().Take(10))
            {
                var lineNumber=GetInt(item,"line");
                var partner=GetInt(item,"rhymesWithLine");
                if(lineNumber<1 || lineNumber>lines.Count || partner<1 || partner>lines.Count || partner==lineNumber)continue;

                var original=GetString(item,"original")?.Trim();
                var replacement=GetString(item,"replacement")?.Trim();
                if(string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(replacement))continue;
                if(original.Contains('\n') || original.Contains('\r') || replacement.Contains('\n') || replacement.Contains('\r'))continue;
                if(string.Equals(original,replacement,StringComparison.CurrentCultureIgnoreCase))continue;
                if(lines[lineNumber-1].IndexOf(original,StringComparison.Ordinal)<0)continue;

                var rhymesWith=GetString(item,"rhymesWith")?.Trim();
                var rhymeType=GetString(item,"rhymeType")?.Trim().ToLowerInvariant();
                if(rhymeType is not ("exact" or "near" or "slant"))rhymeType="near";
                var explanation=GetString(item,"explanation")?.Trim();
                var syllables=GetNullableInt(item,"syllables");

                if(result.Any(x=>x.LineNumber==lineNumber && string.Equals(x.Original,original,StringComparison.Ordinal) && string.Equals(x.Replacement,replacement,StringComparison.CurrentCultureIgnoreCase)))continue;
                result.Add(new RhymeSuggestion(
                    lineNumber,
                    original,
                    replacement,
                    partner,
                    string.IsNullOrWhiteSpace(rhymesWith)?$"line {partner}":rhymesWith,
                    rhymeType,
                    string.IsNullOrWhiteSpace(explanation)?"A possible meaning-preserving rhyme.":explanation,
                    syllables));
            }
        }

        return new RhymeAnalysis(
            string.IsNullOrWhiteSpace(scheme)?"uncertain":scheme,
            string.IsNullOrWhiteSpace(summary)?"No general rhyme assessment was supplied.":summary,
            result);
    }

    static string ExtractJson(string response)
    {
        response=(response??"").Trim();
        if(response.StartsWith("```",StringComparison.Ordinal))
        {
            var firstNewline=response.IndexOf('\n');
            if(firstNewline>=0)response=response[(firstNewline+1)..];
            var fence=response.LastIndexOf("```",StringComparison.Ordinal);
            if(fence>=0)response=response[..fence];
        }
        var first=response.IndexOf('{');
        var last=response.LastIndexOf('}');
        if(first<0 || last<first)
            throw new InvalidOperationException("The AI word-choice service did not return valid JSON.");
        return response[first..(last+1)];
    }

    static string? GetString(JsonElement element,string name)
        =>element.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String?value.GetString():null;

    static int GetInt(JsonElement element,string name)
        =>element.TryGetProperty(name,out var value) && value.TryGetInt32(out var number)?number:0;

    static int? GetNullableInt(JsonElement element,string name)
        =>element.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var number) && number>0?number:null;
}
