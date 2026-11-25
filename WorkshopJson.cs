namespace CtrlZed;

/// <summary>Compatibility for a single complete JSON document wrapped as a code block.</summary>
internal static class WorkshopJson
{
    internal static string UnwrapDocument(string response)
    {
        var text=response.Trim();
        if(text.StartsWith('\uFEFF'))text=text[1..].TrimStart();
        if(!text.StartsWith("```",StringComparison.Ordinal))return text;

        var newline=text.IndexOf('\n');
        if(newline<0)return text;
        var opening=text[..newline].TrimEnd('\r',' ','\t');
        if(opening!="```" && !opening.Equals("```json",StringComparison.OrdinalIgnoreCase))return text;
        if(!text.EndsWith("```",StringComparison.Ordinal) || text.Length<newline+4)return text;

        // This is not a search for the first opening brace. Preambles, multiple
        // documents, trailing prose and incomplete JSON still fail the normal
        // JSON parser and the workshop's strict semantic validation.
        var document=text[(newline+1)..^3].Trim();
        if(document.StartsWith('\uFEFF'))document=document[1..].TrimStart();
        return document;
    }
}
