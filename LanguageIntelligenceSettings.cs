using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CtrlZed;

public enum WritingMode
{
    General,
    PersonalLetter,
    FormalLetter,
    Email,
    Business,
    Technical,
    Academic,
    Legal,
    Journalism,
    Article,
    Speech,
    Fiction,
    Novel,
    Novella,
    ShortStory,
    Screenplay,
    Poetry,
    Custom
}

public sealed class LanguageIntelligenceSettings
{
    public const int CurrentFormatVersion=1;

    public int FormatVersion { get; set; }=CurrentFormatVersion;
    public string LanguageTag { get; set; }="en-GB";
    public bool SpellingEnabled { get; set; }=true;
    public bool ContextualSpellingSuggestions { get; set; }=true;
    public bool ContextualSpellingAutoCorrect { get; set; }
    public bool GrammarAndStyleSuggestions { get; set; }
    public bool ContextualThesaurus { get; set; }=true;
    public bool PoetryRhymeSuggestions { get; set; }
    public bool ShowAutomaticCorrectionNotice { get; set; }=true;
    public bool AllowAiLanguageServices { get; set; }
    public int SentenceCompletionDelayMs { get; set; }=650;
    public WritingMode WritingMode { get; set; }=WritingMode.General;
    public string CustomProfileName { get; set; }="";

    public void Normalise()
    {
        if(string.IsNullOrWhiteSpace(LanguageTag))LanguageTag="en-GB";
        SentenceCompletionDelayMs=Math.Clamp(SentenceCompletionDelayMs,250,5000);
        if(WritingMode!=WritingMode.Poetry)PoetryRhymeSuggestions=false;
    }
}

public sealed record WritingModeProfile(
    WritingMode Mode,
    string DisplayName,
    string Description,
    int Formality,
    int PassiveVoiceTolerance,
    int FragmentTolerance,
    int RepetitionSensitivity,
    int VoicePreservation);

public static class WritingModeProfiles
{
    public static IReadOnlyList<WritingModeProfile> All { get; }=new[]
    {
        new(WritingMode.General,"General / neutral prose","Balanced checking without imposing a house style.",3,3,2,3,4),
        new(WritingMode.PersonalLetter,"Personal letter","Warm correspondence with natural contractions and conversational rhythm.",2,4,3,2,5),
        new(WritingMode.FormalLetter,"Formal letter","Clear, courteous and conventionally structured correspondence.",5,2,1,4,4),
        new(WritingMode.Email,"Email","Concise correspondence with moderate conversational tolerance.",3,3,3,3,4),
        new(WritingMode.Business,"Business and investor writing","Direct, evidence-led writing with strong consistency checks.",5,2,1,5,3),
        new(WritingMode.Technical,"Technical documentation","Precise terminology, explicit references and high consistency.",4,3,1,5,3),
        new(WritingMode.Academic,"Academic writing","Formal argument, cautious claims and citation-aware phrasing.",5,4,1,4,3),
        new(WritingMode.Legal,"Legal / formal writing","Strict terminology and punctuation with minimal stylistic intervention.",5,5,1,5,4),
        new(WritingMode.Journalism,"Journalism / reporting","Readable, economical prose with attribution and factual clarity.",4,2,2,4,4),
        new(WritingMode.Article,"Blog / article","Accessible explanatory prose with moderate stylistic freedom.",3,3,3,3,4),
        new(WritingMode.Speech,"Speech","Spoken rhythm, repetition for effect and audience clarity.",3,3,4,1,5),
        new(WritingMode.Fiction,"Fiction","Narrative prose with strong protection for authorial voice.",2,4,5,2,5),
        new(WritingMode.Novel,"Novel","Long-form fiction with continuity and character-voice tolerance.",2,4,5,3,5),
        new(WritingMode.Novella,"Novella","Compressed long-form fiction with continuity and pace awareness.",2,4,5,4,5),
        new(WritingMode.ShortStory,"Short story","Economical fiction with high sensitivity to repetition and rhythm.",2,4,5,5,5),
        new(WritingMode.Screenplay,"Dialogue / screenplay","Dialogue-led writing with fragments and character voice expected.",2,5,5,2,5),
        new(WritingMode.Poetry,"Poetry","Poetic licence, rhyme, metre and meaning take precedence over ordinary prose rules.",1,5,5,1,5),
        new(WritingMode.Custom,"Custom profile","User-defined language preferences.",3,3,3,3,5)
    };

    public static WritingModeProfile Get(WritingMode mode)
        =>All.FirstOrDefault(x=>x.Mode==mode)??All[0];
}

public static class LanguageIntelligenceSettingsStore
{
    static readonly JsonSerializerOptions JsonOptions=new()
    {
        WriteIndented=true,
        Converters={new JsonStringEnumConverter()}
    };

    public static string SettingsPath
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"language-settings.json");
        }
    }

    public static LanguageIntelligenceSettings Load()
    {
        try
        {
            if(File.Exists(SettingsPath))
            {
                var value=JsonSerializer.Deserialize<LanguageIntelligenceSettings>(File.ReadAllText(SettingsPath),JsonOptions);
                if(value!=null)
                {
                    value.Normalise();
                    return value;
                }
            }
        }
        catch { }
        return new LanguageIntelligenceSettings();
    }

    public static void Save(LanguageIntelligenceSettings settings)
    {
        settings.Normalise();
        var temporary=SettingsPath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(settings,JsonOptions));
        File.Move(temporary,SettingsPath,true);
    }
}

public readonly record struct CompletedSentence(int Start,int Length,string Text);

public static class SentenceCompletionDetector
{
    static readonly HashSet<string> CommonAbbreviations=new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr","Mrs","Ms","Dr","Prof","Rev","St","No","Nos","Fig","Figs","Eq","Eqs","Vol","Ch","Ltd","Co","Inc","etc","e.g","i.e"
    };

    public static bool TryFindCompletedSentence(string text,int caretOffset,out CompletedSentence sentence)
    {
        sentence=default;
        if(string.IsNullOrEmpty(text) || caretOffset<2)return false;
        caretOffset=Math.Clamp(caretOffset,0,text.Length);

        var whitespaceIndex=caretOffset-1;
        if(!char.IsWhiteSpace(text[whitespaceIndex]))return false;

        var punctuationIndex=whitespaceIndex-1;
        while(punctuationIndex>=0 && char.IsWhiteSpace(text[punctuationIndex]))punctuationIndex--;
        if(punctuationIndex<0 || text[punctuationIndex] is not ('.' or '?' or '!'))return false;
        if(IsFalseBoundary(text,punctuationIndex))return false;

        var start=punctuationIndex-1;
        while(start>=0)
        {
            if(text[start] is '\n' or '\r')break;
            if(text[start] is '.' or '?' or '!')
            {
                var after=start+1;
                if(after<text.Length && char.IsWhiteSpace(text[after]))break;
            }
            start--;
        }
        start++;
        while(start<punctuationIndex && char.IsWhiteSpace(text[start]))start++;

        var length=punctuationIndex-start+1;
        if(length<2)return false;
        sentence=new CompletedSentence(start,length,text.Substring(start,length));
        return true;
    }

    static bool IsFalseBoundary(string text,int punctuationIndex)
    {
        if(text[punctuationIndex]!='.')return false;

        if(punctuationIndex>0 && punctuationIndex+1<text.Length &&
           char.IsDigit(text[punctuationIndex-1]) && char.IsDigit(text[punctuationIndex+1]))
            return true;

        var wordEnd=punctuationIndex;
        var wordStart=wordEnd-1;
        while(wordStart>=0 && (char.IsLetter(text[wordStart]) || text[wordStart]=='.'))wordStart--;
        var token=text[(wordStart+1)..wordEnd].Trim('.');
        if(CommonAbbreviations.Contains(token))return true;
        if(token.Length==1 && char.IsUpper(token[0]))return true;

        return false;
    }
}
