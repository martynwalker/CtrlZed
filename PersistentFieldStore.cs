using System.IO;
using System.Text.Json;

namespace CtrlZed;

public static class PersistentFieldStore
{
    static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};

    public static string SystemPath=>Path.Combine(AppContext.BaseDirectory,"system-fields.json");
    public static string UserPath
    {
        get
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
            Directory.CreateDirectory(root);
            return Path.Combine(root,"user-fields.json");
        }
    }

    public static Dictionary<string,string> LoadSystem()=>Load(SystemPath);
    public static Dictionary<string,string> LoadUser()=>Load(UserPath);

    static Dictionary<string,string> Load(string path)
    {
        try
        {
            if(!File.Exists(path))return new(StringComparer.OrdinalIgnoreCase);
            var parsed=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))
                       ??new Dictionary<string,string>();
            return new Dictionary<string,string>(parsed,StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveUser(IReadOnlyDictionary<string,string> fields)
    {
        var path=UserPath;
        var tmp=path+".tmp";
        var ordered=fields.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(tmp,JsonSerializer.Serialize(ordered,JsonOptions));
        File.Move(tmp,path,true);
    }
}
