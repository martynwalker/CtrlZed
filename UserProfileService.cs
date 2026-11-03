using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace CtrlZed;

public static class UserProfileService
{
    public const int FormatVersion=1;

    public sealed class PortablePreferences
    {
        public double ZoomPercent { get; set; }=100;
        public bool ExtendedMacrosEnabled { get; set; }
        public bool CompiledMacrosEnabled { get; set; }
        public string LastMacroLanguage { get; set; }=".vb";
        public bool KeepParagraphTogether { get; set; }=true;
    }

    public sealed class Manifest
    {
        public int FormatVersion { get; set; }=UserProfileService.FormatVersion;
        public string Product { get; set; }="CtrlZed";
        public DateTime CreatedUtc { get; set; }=DateTime.UtcNow;
        public string Note { get; set; }="Secrets are intentionally excluded.";
    }

    public sealed class ImportResult
    {
        public int FieldsImported { get; set; }
        public int MacrosImported { get; set; }
        public int StyleSetsImported { get; set; }
        public PortablePreferences? Preferences { get; set; }
    }

    static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};

    public static void Export(
        string destination,
        IReadOnlyDictionary<string,string> userFields,
        PortablePreferences preferences,
        IReadOnlyDictionary<string,StyleSetStore.StyleSet> styleSets,
        string macroDirectory)
    {
        var temp=destination+".tmp";
        if(File.Exists(temp))File.Delete(temp);
        using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
        {
            WriteJson(zip,"manifest.json",new Manifest());
            WriteJson(zip,"user-fields.json",
                userFields.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase));
            WriteJson(zip,"preferences.json",preferences);
            WriteJson(zip,"style-sets.json",styleSets);

            if(Directory.Exists(macroDirectory))
            {
                foreach(var path in Directory.GetFiles(macroDirectory)
                    .Where(p=>new[]{".czm",".vb",".cs"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)))
                {
                    var entry=zip.CreateEntry("macros/"+Path.GetFileName(path),CompressionLevel.Optimal);
                    using var input=File.OpenRead(path);
                    using var output=entry.Open();
                    input.CopyTo(output);
                }
            }
        }
        File.Move(temp,destination,true);
    }

    public static ImportResult Import(
        string source,
        bool replace,
        IDictionary<string,string> userFields,
        IDictionary<string,StyleSetStore.StyleSet> styleSets,
        string macroDirectory)
    {
        using var zip=ZipFile.OpenRead(source);
        var manifest=ReadJson<Manifest>(zip,"manifest.json")
            ??throw new InvalidDataException("This profile has no valid manifest.");
        if(!string.Equals(manifest.Product,"CtrlZed",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This is not a CtrlZed profile.");
        if(manifest.FormatVersion>FormatVersion)
            throw new InvalidDataException($"Profile format {manifest.FormatVersion} is newer than this CtrlZed build supports.");

        var importedFields=ReadJson<Dictionary<string,string>>(zip,"user-fields.json")
                           ??new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(replace)userFields.Clear();
        foreach(var pair in importedFields)
            if(replace || !userFields.ContainsKey(pair.Key))
                userFields[pair.Key]=pair.Value??"";

        var importedStyles=ReadJson<Dictionary<string,StyleSetStore.StyleSet>>(zip,"style-sets.json")
                           ??new Dictionary<string,StyleSetStore.StyleSet>(StringComparer.OrdinalIgnoreCase);
        if(replace)styleSets.Clear();
        foreach(var pair in importedStyles)
            if(replace || !styleSets.ContainsKey(pair.Key))
                styleSets[pair.Key]=pair.Value;

        Directory.CreateDirectory(macroDirectory);
        if(replace)
        {
            foreach(var path in Directory.GetFiles(macroDirectory)
                .Where(p=>new[]{".czm",".vb",".cs"}.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase)))
                File.Delete(path);
        }

        var macroCount=0;
        foreach(var entry in zip.Entries.Where(e=>e.FullName.StartsWith("macros/",StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Name)))
        {
            var ext=Path.GetExtension(entry.Name);
            if(!new[]{".czm",".vb",".cs"}.Contains(ext,StringComparer.OrdinalIgnoreCase))continue;
            var target=Path.Combine(macroDirectory,Path.GetFileName(entry.Name));
            if(!replace && File.Exists(target))continue;
            entry.ExtractToFile(target,true);
            macroCount++;
        }

        return new ImportResult
        {
            FieldsImported=importedFields.Count,
            MacrosImported=macroCount,
            StyleSetsImported=importedStyles.Count,
            Preferences=ReadJson<PortablePreferences>(zip,"preferences.json")
        };
    }

    static void WriteJson<T>(ZipArchive zip,string name,T value)
    {
        var entry=zip.CreateEntry(name,CompressionLevel.Optimal);
        using var writer=new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(value,JsonOptions));
    }

    static T? ReadJson<T>(ZipArchive zip,string name)
    {
        var entry=zip.GetEntry(name);
        if(entry==null)return default;
        using var reader=new StreamReader(entry.Open());
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd());
    }
}
