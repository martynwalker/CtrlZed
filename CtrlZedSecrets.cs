using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CtrlZed;

/// <summary>
/// Per-Windows-user secret storage for compiled CtrlZed macros.
/// Values are protected with Windows DPAPI and are never stored in documents or macro source.
/// </summary>
public static class CtrlZedSecrets
{
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
    static readonly string StorePath=Path.Combine(Root,"macro-secrets.bin");

    public static bool Exists(string name)
    {
        ValidateName(name);
        return Load().ContainsKey(name);
    }

    public static string Get(string name)
    {
        ValidateName(name);
        var map=Load();
        if(!map.TryGetValue(name,out var value) || string.IsNullOrEmpty(value))
            throw new KeyNotFoundException($"CtrlZed secret '{name}' is not configured. Open Macro Manager → Secrets… to add it.");
        return value;
    }

    public static IReadOnlyList<string> Names()=>Load().Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();

    internal static void Set(string name,string value)
    {
        ValidateName(name);
        if(string.IsNullOrEmpty(value))throw new ArgumentException("Secret value cannot be empty.",nameof(value));
        var map=Load();
        map[name]=value;
        Save(map);
    }

    internal static bool Remove(string name)
    {
        ValidateName(name);
        var map=Load();
        if(!map.Remove(name))return false;
        Save(map);
        return true;
    }

    static void ValidateName(string name)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Secret name cannot be empty.",nameof(name));
        if(name.Length>100)throw new ArgumentException("Secret name is too long.",nameof(name));
        if(name.Any(ch=>!(char.IsLetterOrDigit(ch)||ch=='_'||ch=='-'||ch=='.')))
            throw new ArgumentException("Secret names may contain letters, numbers, dots, hyphens and underscores.",nameof(name));
    }

    static Dictionary<string,string> Load()
    {
        try
        {
            if(!File.Exists(StorePath))return new(StringComparer.OrdinalIgnoreCase);
            var protectedBytes=File.ReadAllBytes(StorePath);
            var plain=ProtectedData.Unprotect(protectedBytes,null,DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string,string>>(Encoding.UTF8.GetString(plain))
                   ??new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    static void Save(Dictionary<string,string> map)
    {
        Directory.CreateDirectory(Root);
        var plain=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(map));
        var protectedBytes=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);
        var temp=StorePath+".tmp";
        File.WriteAllBytes(temp,protectedBytes);
        File.Move(temp,StorePath,true);
    }
}
