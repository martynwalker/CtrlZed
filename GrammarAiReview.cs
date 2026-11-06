using System.Text.Json;

namespace CtrlZed;

public static class GrammarAiReview
{
    public static async Task<IReadOnlyList<GrammarFinding>> AnalyseParagraphAsync(
        string text,
        WritingModeProfile profile,
        CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(text))return Array.Empty<GrammarFinding>();
        if(text.Length>12000)
            throw new InvalidOperationException("AI grammar review is limited to 12,000 characters at a time. Review a smaller paragraph or selection.");

        var provider=AiManager.GetDefaultProvider();
        var prompt=$$"""
You are the conservative British English grammar and style reviewer inside CtrlZed.

WRITING MODE
Name: {{profile.DisplayName}}
Purpose: {{profile.Description}}
Formality: {{profile.Formality}}/5
Passive-voice tolerance: {{profile.PassiveVoiceTolerance}}/5
Fragment tolerance: {{profile.FragmentTolerance}}/5
Repetition sensitivity: {{profile.RepetitionSensitivity}}/5
Voice preservation: {{profile.VoicePreservation}}/5

TASK
Review the supplied paragraph in context. Grammar and style changes are suggestions only. Preserve the writer's voice and tolerate deliberate dialogue, fragments and literary choices according to the writing mode.

Do not report ordinary spelling errors; CtrlZed handles those separately. Do not rewrite the whole paragraph. Do not invent facts. Do not suggest a change unless it materially improves correctness, clarity or consistency.

Return JSON only, with no Markdown fences, in this exact form:
{"issues":[{"original":"exact continuous substring copied from the paragraph","replacement":"suggested replacement, or empty when explanation alone is appropriate","explanation":"brief reason","kind":"grammar|style|consistency","title":"short label"}]}

Rules:
- Maximum 12 issues.
- `original` must appear exactly as written in the paragraph.
- Keep each original span as short as reasonably possible.
- An empty issues array is valid and preferable to weak advice.

PARAGRAPH
{{text}}
""";

        var response=await AiDirectService.GenerateAsync(provider,prompt,false,cancellationToken);
        var json=ExtractJson(response);
        using var document=JsonDocument.Parse(json);
        if(!document.RootElement.TryGetProperty("issues",out var issues) || issues.ValueKind!=JsonValueKind.Array)
            return Array.Empty<GrammarFinding>();

        var result=new List<GrammarFinding>();
        var occupied=new List<(int Start,int End)>();
        var number=0;

        foreach(var item in issues.EnumerateArray().Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!item.TryGetProperty("original",out var originalElement) || originalElement.ValueKind!=JsonValueKind.String)continue;
            var original=(originalElement.GetString()??"").Trim();
            if(original.Length==0)continue;

            var start=FindUnusedOccurrence(text,original,occupied);
            if(start<0)continue;
            occupied.Add((start,start+original.Length));

            var replacement=item.TryGetProperty("replacement",out var replacementElement) && replacementElement.ValueKind==JsonValueKind.String
                ?replacementElement.GetString()?.Trim()
                :null;
            if(string.Equals(replacement,original,StringComparison.Ordinal))replacement=null;
            if(string.IsNullOrWhiteSpace(replacement))replacement=null;

            var explanation=item.TryGetProperty("explanation",out var explanationElement) && explanationElement.ValueKind==JsonValueKind.String
                ?explanationElement.GetString()?.Trim()
                :null;
            if(string.IsNullOrWhiteSpace(explanation))explanation="AI identified a possible improvement in the context of the selected writing mode.";

            var title=item.TryGetProperty("title",out var titleElement) && titleElement.ValueKind==JsonValueKind.String
                ?titleElement.GetString()?.Trim()
                :null;
            if(string.IsNullOrWhiteSpace(title))title="Contextual suggestion";

            var kindText=item.TryGetProperty("kind",out var kindElement) && kindElement.ValueKind==JsonValueKind.String
                ?kindElement.GetString()
                :"style";
            var kind=kindText?.ToLowerInvariant() switch
            {
                "grammar"=>GrammarFindingKind.Grammar,
                "consistency"=>GrammarFindingKind.Consistency,
                _=>GrammarFindingKind.Style
            };

            result.Add(new GrammarFinding(start,original.Length,"ai-context-"+(++number),kind,title!,explanation!,original,replacement,true));
        }

        return result.OrderBy(x=>x.Start).ToList();
    }

    static int FindUnusedOccurrence(string text,string original,IReadOnlyList<(int Start,int End)> occupied)
    {
        var start=0;
        while(start<=text.Length-original.Length)
        {
            var found=text.IndexOf(original,start,StringComparison.Ordinal);
            if(found<0)return -1;
            var end=found+original.Length;
            if(!occupied.Any(x=>found<x.End && end>x.Start))return found;
            start=found+Math.Max(1,original.Length);
        }
        return -1;
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
            throw new InvalidOperationException("The AI grammar review did not return valid JSON.");
        return response[first..(last+1)];
    }
}
