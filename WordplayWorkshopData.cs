using System.IO;
using System.Text.Json.Serialization;

namespace CtrlZed;

/// <summary>Independent creative axes; General never implies compulsory comedy.</summary>
public static class WordplayWorkshopChoices
{
    public static IReadOnlyList<string> Forms { get; }=Choices("General","Poetry","Song","Novel","Short story","Dialogue","Speech");
    public static IReadOnlyList<string> Styles { get; }=Choices("General","Philosophical","Comic","Serious","Reflective","Surreal","Dark","Lyrical");
    public static IReadOnlyList<string> ComicFlavours { get; }=Choices("General","Subtle and understated","Deadpan","Sarcastic","Ironic","Self-deprecating","Cringe","Awkward");
    public static IReadOnlyList<string> Devices { get; }=Choices("General","Banter","Pun","Highbrow","Anti-humour","Double meaning","Paradox","Metaphor","Soundplay");
    public static IReadOnlyList<string> SubjectTargets { get; }=Choices("General","Observational","Satire","Parody","Insult");
    public static IReadOnlyList<string> Situations { get; }=Choices("General","Situational","Slapstick","Satirical situations","Character comedy","Farce");

    static IReadOnlyList<string> Choices(params string[] values)=>Array.AsReadOnly(values);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WordplayWorkshopOptions
{
    public string Form { get; set; }="General";
    public string Style { get; set; }="General";
    public string ComicFlavour { get; set; }="General";
    public string Device { get; set; }="General";
    public string SubjectTarget { get; set; }="General";
    public string Situation { get; set; }="General";

    [JsonIgnore]
    public bool IsDefault=>Form=="General" && Style=="General" && ComicFlavour=="General" &&
        Device=="General" && SubjectTarget=="General" && Situation=="General";

    public void Validate()
    {
        Choice(Form,WordplayWorkshopChoices.Forms,"form");Choice(Style,WordplayWorkshopChoices.Styles,"style");
        Choice(ComicFlavour,WordplayWorkshopChoices.ComicFlavours,"comic flavour");Choice(Device,WordplayWorkshopChoices.Devices,"wordplay device");
        Choice(SubjectTarget,WordplayWorkshopChoices.SubjectTargets,"subject and target");Choice(Situation,WordplayWorkshopChoices.Situations,"situation");
    }

    public WordplayWorkshopOptions DeepClone()
    {
        Validate();return new(){Form=Form,Style=Style,ComicFlavour=ComicFlavour,Device=Device,SubjectTarget=SubjectTarget,Situation=Situation};
    }

    static void Choice(string? value,IReadOnlyList<string> choices,string label)
    {if(value==null || !choices.Contains(value,StringComparer.Ordinal))throw WordplayWorkshopState.Invalid($"Choose a supported Wordplay Workshop {label}.");}
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WordplayExample
{
    public string Text { get; set; }="";
    public string Device { get; set; }="";
    public WordplayExample DeepClone()=>new(){Text=Text,Device=Device};
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WordplayConnection
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Title { get; set; }="";
    public List<string> Anchors { get; set; }=new();
    public string Bridge { get; set; }="";
    public List<WordplayExample> Examples { get; set; }=new();

    public WordplayConnection DeepClone()=>new(){Id=Id,Title=Title,Anchors=new(Anchors),Bridge=Bridge,
        Examples=Examples.Select(example=>example.DeepClone()).ToList()};
}

/// <summary>A kept phrase owns its original connection and options, even after the word pile changes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WordplayFavourite
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string Text { get; set; }="";
    public string Title { get; set; }="";
    public List<string> Anchors { get; set; }=new();
    public string Bridge { get; set; }="";
    public string Device { get; set; }="";
    public WordplayWorkshopOptions Options { get; set; }=new();
    public string Target { get; set; }="";

    public WordplayFavourite DeepClone()=>new(){Id=Id,Text=Text,Title=Title,Anchors=new(Anchors),Bridge=Bridge,
        Device=Device,Options=Options.DeepClone(),Target=Target};

    public static WordplayFavourite FromConnection(WordplayConnection connection,string editedText,
        WordplayWorkshopOptions options,string target,string? device=null)
    {
        ArgumentNullException.ThrowIfNull(connection);ArgumentNullException.ThrowIfNull(options);
        long count=0;WordplayWorkshopState.ValidateConnection(connection,null,ref count);
        var result=new WordplayFavourite{Text=editedText,Title=connection.Title,Anchors=new(connection.Anchors),
            Bridge=connection.Bridge,Options=options.DeepClone(),Target=target,
            Device=device??connection.Examples.FirstOrDefault(example=>example.Text==editedText)?.Device??"Edited phrase"};
        count=0;WordplayWorkshopState.ValidateFavourite(result,ref count);return result;
    }
}

/// <summary>The word pile, chosen axes, latest associations and independent favourites travel in a native document.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WordplayWorkshopState
{
    public const int CurrentSchemaVersion=1;
    public const int MaximumSeedLength=12000;
    public const int MaximumTargetLength=1000;
    public const int MaximumPhraseLength=1000;
    public const int MaximumConnections=6;
    public const int MaximumFavourites=40;
    public const int MaximumTotalCharacters=256000;

    public int SchemaVersion { get; set; }=CurrentSchemaVersion;
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public long Revision { get; set; }
    public WordplayWorkshopOptions Options { get; set; }=new();
    public string SeedText { get; set; }="";
    public string Target { get; set; }="";
    public List<WordplayConnection> Results { get; set; }=new();
    public List<WordplayFavourite> Favourites { get; set; }=new();

    [JsonIgnore]
    public bool HasContent=>!string.IsNullOrWhiteSpace(SeedText) || !string.IsNullOrWhiteSpace(Target) ||
        Options?.IsDefault==false || (Results?.Count??0)>0 || (Favourites?.Count??0)>0;

    public WordplayWorkshopState DeepClone()
    {
        Validate();return new(){SchemaVersion=SchemaVersion,Id=Id,Revision=Revision,Options=Options.DeepClone(),
            SeedText=SeedText,Target=Target,Results=Results.Select(item=>item.DeepClone()).ToList(),
            Favourites=Favourites.Select(item=>item.DeepClone()).ToList()};
    }

    public void Validate()
    {
        if(SchemaVersion!=CurrentSchemaVersion)throw Invalid("This document uses an unsupported Wordplay Workshop metadata version.");
        if(Revision<0)throw Invalid("The Wordplay Workshop revision is invalid.");
        ValidateId(Id);if(Options==null)throw Invalid("Wordplay Workshop options are missing.");Options.Validate();
        ValidateText(SeedText,MaximumSeedLength,"word pile");ValidateText(Target,MaximumTargetLength,"target or intention");
        long count=Id.Length+SeedText.Length+Target.Length+OptionCharacters(Options);
        if(Results==null || Results.Count>MaximumConnections)throw Invalid($"Keep no more than {MaximumConnections} association directions.");
        if(Favourites==null || Favourites.Count>MaximumFavourites)throw Invalid($"Keep no more than {MaximumFavourites} favourite phrases.");
        var ids=new HashSet<string>(StringComparer.Ordinal){Id};
        foreach(var item in Results)
        {
            ValidateConnection(item,SeedText,ref count);
            if(!ids.Add(item.Id))throw Invalid("Wordplay Workshop result identities must be distinct.");
        }
        foreach(var favourite in Favourites)
        {
            ValidateFavourite(favourite,ref count);
            if(!ids.Add(favourite.Id))throw Invalid("Wordplay Workshop favourite identities must be distinct.");
        }
        if(count>MaximumTotalCharacters)
            throw Invalid($"Wordplay Workshop exceeds {MaximumTotalCharacters:N0} characters. Keep a smaller collection; no saved text has been discarded.");
    }

    internal static void ValidateConnection(WordplayConnection? value,string? seed,ref long count)
    {
        if(value==null)throw Invalid("An association direction is missing.");ValidateId(value.Id);
        ValidateText(value.Title,160,"association title",false,false);ValidateText(value.Bridge,1200,"association bridge",false);
        ValidateAnchors(value.Anchors,seed,ref count);
        if(value.Examples==null || value.Examples.Count!=2)throw Invalid("Each association direction needs two original phrase examples.");
        var examples=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var example in value.Examples)
        {
            if(example==null)throw Invalid("A phrase example is missing.");
            ValidateText(example.Text,MaximumPhraseLength,"phrase example",false);ValidateText(example.Device,120,"phrase device",false,false);
            if(!examples.Add(example.Text.Trim()))throw Invalid("An association direction repeats its phrase example.");
            count+=example.Text.Length+example.Device.Length;
        }
        count+=value.Id.Length+value.Title.Length+value.Bridge.Length;
    }

    internal static void ValidateFavourite(WordplayFavourite? value,ref long count)
    {
        if(value==null)throw Invalid("A favourite phrase is missing.");ValidateId(value.Id);
        ValidateText(value.Text,MaximumPhraseLength,"favourite phrase",false);ValidateText(value.Title,160,"favourite association title",false,false);
        ValidateText(value.Bridge,1200,"favourite bridge",false);ValidateText(value.Device,120,"favourite device",false,false);
        ValidateText(value.Target,MaximumTargetLength,"favourite target");
        if(value.Options==null)throw Invalid("A favourite's creative options are missing.");value.Options.Validate();
        ValidateAnchors(value.Anchors,null,ref count);
        count+=value.Id.Length+value.Text.Length+value.Title.Length+value.Bridge.Length+value.Device.Length+
            value.Target.Length+OptionCharacters(value.Options);
    }

    static long OptionCharacters(WordplayWorkshopOptions options)=>options.Form.Length+options.Style.Length+
        options.ComicFlavour.Length+options.Device.Length+options.SubjectTarget.Length+options.Situation.Length;

    static void ValidateAnchors(List<string>? anchors,string? seed,ref long count)
    {
        if(anchors==null || anchors.Count is <1 or >3)throw Invalid("An association needs one to three exact word-pile anchors.");
        var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var anchor in anchors)
        {
            ValidateText(anchor,240,"source anchor",false);
            if(!unique.Add(anchor))throw Invalid("An association repeats a source anchor.");
            if(seed!=null && !seed.Contains(anchor,StringComparison.Ordinal))throw Invalid("An association anchor is not present in its word pile.");
            count+=anchor.Length;
        }
    }

    internal static void ValidateId(string? id)
    {
        if(string.IsNullOrWhiteSpace(id) || id.Length>80 || id.Any(ch=>!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw Invalid("The Wordplay Workshop identity is invalid.");
    }

    internal static void ValidateText(string? text,int maximum,string label,bool empty=true,bool multiline=true)
    {
        if(text==null || text.Length>maximum || !empty && string.IsNullOrWhiteSpace(text))
            throw Invalid($"Keep {label} within {(empty?0:1)}–{maximum:N0} characters.");
        for(var index=0;index<text.Length;index++)
        {
            var ch=text[index];
            if(ch=='\uFFFC' || ch is '\u2028' or '\u2029' || char.IsControl(ch) && !(multiline && ch is '\r' or '\n' or '\t'))
                throw Invalid($"The {label} contains unsupported control characters or embedded objects.");
            if(char.IsHighSurrogate(ch))
            {
                if(index+1>=text.Length || !char.IsLowSurrogate(text[index+1]))throw Invalid($"The {label} contains invalid Unicode.");
                index++;
            }
            else if(char.IsLowSurrogate(ch))throw Invalid($"The {label} contains invalid Unicode.");
        }
    }

    internal static InvalidDataException Invalid(string message)=>new(message);
}
