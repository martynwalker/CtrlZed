using System.Text.Json;

namespace CtrlZed;

/// <summary>Provider output shapes; application validators still check every saved value and reference.</summary>
internal static class WorkshopResponseSchemas
{
    internal static JsonElement Wordplay { get; }=JsonSerializer.SerializeToElement(BuildWordplay());
    internal static JsonElement Story { get; }=JsonSerializer.SerializeToElement(BuildStory());

    static Dictionary<string,object> Text()=>new(){{"type","string"}};

    static Dictionary<string,object> Choice(params string[] choices)
        =>new(){{"type","string"},{"enum",choices}};

    static Dictionary<string,object> Object(params (string Name,object Schema)[] fields)
        =>new()
        {
            {"type","object"},
            {"properties",fields.ToDictionary(field=>field.Name,field=>field.Schema,StringComparer.Ordinal)},
            {"required",fields.Select(field=>field.Name).ToArray()},
            {"additionalProperties",false}
        };

    static Dictionary<string,object> Array(object item,int minimum,int maximum)
        =>new(){{"type","array"},{"items",item},{"minItems",minimum},{"maxItems",maximum}};

    static object BuildWordplay()
    {
        var example=Object(("text",Text()),("device",Text()));
        var connection=Object(("title",Text()),("anchors",Array(Text(),1,3)),("bridge",Text()),
            ("examples",Array(example,WordplayWorkshopService.ExamplesPerConnection,WordplayWorkshopService.ExamplesPerConnection)));
        return Object(("connections",Array(connection,WordplayWorkshopService.ConnectionCount,WordplayWorkshopService.ConnectionCount)));
    }

    static object BuildStory()
    {
        var fields=StoryWorkshopSchema.StoryFields.Concat(StoryWorkshopSchema.CharacterFields)
            .Concat(StoryWorkshopSchema.BeatFields).Concat(StoryWorkshopSchema.ClueFields)
            .Select(field=>field.Key).Distinct(StringComparer.Ordinal).ToArray();
        var change=Object(("kind",Choice("story","character","beat","clue")),("id",Text()),("name",Text()),
            ("field",Choice(fields)),("value",Text()),("status",Choice("confirmed","proposed")),("evidence",Text()),
            ("characterIds",Array(Text(),0,16)));
        var resolution=Object(("text",Text()),("evidence",Text()));
        return Object(("reply",Text()),("changes",Array(change,0,StoryWorkshopService.MaximumChanges)),
            ("questions",Array(Text(),0,2)),("contradictions",Array(Text(),0,8)),
            ("resolvedQuestions",Array(resolution,0,8)),("resolvedContradictions",Array(resolution,0,8)));
    }
}
