using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CtrlZed;

public enum CompletionContextScope { Automatic, Paragraph, Section, WholeDocument }
public enum CompletionStrength { Subtle, Balanced, Bold }
public sealed record CompletionInflection(string Name,string Instruction);

public sealed class SentenceCompletionPreferences
{
    public string Inflection { get; set; }="Comedic";
    public CompletionStrength Strength { get; set; }=CompletionStrength.Balanced;
    public CompletionContextScope ContextScope { get; set; }=CompletionContextScope.Automatic;
    public List<CompletionInflection> CustomInflections { get; set; }=new();

    public void Normalise()
    {
        if(!Enum.IsDefined(Strength))Strength=CompletionStrength.Balanced;
        if(!Enum.IsDefined(ContextScope))ContextScope=CompletionContextScope.Automatic;
        var names=new HashSet<string>(SentenceCompletionService.BuiltIns.Select(x=>x.Name),StringComparer.OrdinalIgnoreCase){"Custom"};
        var custom=new List<CompletionInflection>();
        foreach(var item in CustomInflections??new List<CompletionInflection>())
        {
            if(item is null)continue;
            var name=(item.Name??"").Trim();
            var instruction=(item.Instruction??"").Trim();
            if(custom.Count>=32 || !ValidName(name) || !ValidInstruction(instruction) || !names.Add(name))continue;
            custom.Add(new CompletionInflection(name,instruction));
        }
        CustomInflections=custom;
        Inflection=string.Equals(Inflection?.Trim(),"Custom",StringComparison.OrdinalIgnoreCase)?"Custom":
            SentenceCompletionService.BuiltIns.Concat(custom)
                .FirstOrDefault(x=>string.Equals(x.Name,Inflection?.Trim(),StringComparison.OrdinalIgnoreCase))?.Name??"Comedic";
    }

    internal SentenceCompletionPreferences ValidatedCopy()
    {
        if(!Enum.IsDefined(Strength) || !Enum.IsDefined(ContextScope))
            throw new ArgumentException("Choose a supported strength and context scope.");
        if(CustomInflections is null || CustomInflections.Count>32)
            throw new ArgumentException("Save no more than 32 custom inflections.");
        var names=new HashSet<string>(SentenceCompletionService.BuiltIns.Select(x=>x.Name),StringComparer.OrdinalIgnoreCase){"Custom"};
        var custom=new List<CompletionInflection>();
        foreach(var item in CustomInflections)
        {
            if(item is null)throw new ArgumentException("A custom inflection cannot be empty.");
            var name=(item.Name??"").Trim();
            var instruction=(item.Instruction??"").Trim();
            if(!ValidName(name))throw new ArgumentException("Custom inflection names must contain 1–48 characters on one line.");
            if(!ValidInstruction(instruction))throw new ArgumentException("Custom inflections need an instruction of 1–2,000 characters.");
            if(!names.Add(name))throw new ArgumentException("Custom inflection names must be unique and cannot replace a built-in inflection or use the name Custom.");
            custom.Add(new CompletionInflection(name,instruction));
        }
        var selected=string.Equals(Inflection?.Trim(),"Custom",StringComparison.OrdinalIgnoreCase)?"Custom":
            SentenceCompletionService.BuiltIns.Concat(custom)
                .FirstOrDefault(x=>string.Equals(x.Name,Inflection?.Trim(),StringComparison.OrdinalIgnoreCase))?.Name;
        if(selected is null)throw new ArgumentException("Choose a built-in, Custom, or a saved custom inflection.");
        return new SentenceCompletionPreferences
        {
            Inflection=selected,Strength=Strength,ContextScope=ContextScope,CustomInflections=custom
        };
    }

    static bool ValidName(string value)=>value.Length is >0 and <=48 && !value.Any(c=>char.IsControl(c) || c is '\u2028' or '\u2029');
    static bool ValidInstruction(string value)=>value.Length is >0 and <=2000 && !value.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
}

public static class SentenceCompletionPreferencesStore
{
    static readonly object Gate=new();
    static readonly JsonSerializerOptions JsonOptions=new()
    {
        WriteIndented=true,
        Converters={new JsonStringEnumConverter()}
    };

    public static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed","sentence-completion.json");

    public static SentenceCompletionPreferences Load()
    {
        lock(Gate)
        {
            try
            {
                if(File.Exists(FilePath))
                {
                    // This file contains preferences only: document text and one-off meaning hints are never persisted.
                    if(new FileInfo(FilePath).Length>256000)return new SentenceCompletionPreferences();
                    var value=JsonSerializer.Deserialize<SentenceCompletionPreferences>(File.ReadAllText(FilePath),JsonOptions);
                    if(value is not null){value.Normalise();return value;}
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            catch(JsonException) { }
            catch(NotSupportedException) { }
            return new SentenceCompletionPreferences();
        }
    }

    public static void Save(SentenceCompletionPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var validated=preferences.ValidatedCopy();
        var json=JsonSerializer.Serialize(validated,JsonOptions);
        lock(Gate)
        {
            var path=FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
                using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                // A same-directory rename atomically replaces the previous preferences file.
                File.Move(temporary,path,true);
            }
            finally
            {
                if(File.Exists(temporary))File.Delete(temporary);
            }
        }
    }
}
