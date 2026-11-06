using System.Globalization;
using System.IO;
using System.Text;

namespace CtrlZed;

public static class PersonalDictionaryStore
{
    const int BritishEnglishLcid=2057;

    public static string DictionaryPath
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"personal-en-GB.lex");
        }
    }

    public static Uri EnsureDictionary()
    {
        if(!File.Exists(DictionaryPath))
            WriteWords(Array.Empty<string>());
        return new Uri(DictionaryPath,UriKind.Absolute);
    }

    public static IReadOnlyList<string> LoadWords()
    {
        try
        {
            if(!File.Exists(DictionaryPath))return Array.Empty<string>();
            return File.ReadAllLines(DictionaryPath,Encoding.Unicode)
                .Skip(1)
                .Select(x=>x.Trim())
                .Where(x=>x.Length>0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch{return Array.Empty<string>();}
    }

    public static bool Add(string word)
    {
        word=NormaliseWord(word);
        if(string.IsNullOrWhiteSpace(word))return false;
        var words=LoadWords().ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if(!words.Add(word))return false;
        WriteWords(words);
        return true;
    }

    public static bool Remove(string word)
    {
        word=NormaliseWord(word);
        var words=LoadWords().ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if(!words.Remove(word))return false;
        WriteWords(words);
        return true;
    }

    static string NormaliseWord(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        return value.Trim().Trim('“','”','‘','’','\'','"','.',',',';',':','!','?','(',')','[',']','{','}');
    }

    static void WriteWords(IEnumerable<string> words)
    {
        var ordered=words
            .Select(NormaliseWord)
            .Where(x=>x.Length>0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var lines=new List<string>{BritishEnglishLcid.ToString(CultureInfo.InvariantCulture)};
        lines.AddRange(ordered);
        var temporary=DictionaryPath+".tmp";
        File.WriteAllLines(temporary,lines,Encoding.Unicode);
        File.Move(temporary,DictionaryPath,true);
    }
}
