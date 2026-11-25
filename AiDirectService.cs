using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CtrlZed;

public sealed class AiProviderProfile
{
    public string Provider { get; set; } = "ChatGPT";
    public string Model { get; set; } = "";
    public bool HasKey { get; set; }
}

internal sealed record AiProviderEdit(string Provider,string Model,string? ApiKey);

public static partial class AiDirectService
{
    static readonly HttpClient Http = new(){Timeout=TimeSpan.FromMinutes(3)};
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
    static readonly string ProfilesPath=Path.Combine(Root,"ai-profiles.json");
    static readonly string SecretsPath=Path.Combine(Root,"ai-secrets.bin");
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,long> ConfigurationRevisions=new(StringComparer.OrdinalIgnoreCase);

    sealed class Store
    {
        public Dictionary<string,string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ChatGPT"]="gpt-6-astra",
            ["Claude"]="claude-sonnet-5",
            ["Gemini"]="gemini-3.8-flash"
        };
    }

    public static string DefaultModel(string provider)=>provider switch
    {
        "Claude"=>"claude-sonnet-5",
        "Gemini"=>"gemini-3.8-flash",
        _=>"gpt-6-astra"
    };

    public static AiProviderProfile GetProfile(string provider)
    {
        var store=LoadStore();
        return new AiProviderProfile
        {
            Provider=provider,
            Model=store.Models.TryGetValue(provider,out var m)&&!string.IsNullOrWhiteSpace(m)?m:DefaultModel(provider),
            HasKey=!string.IsNullOrWhiteSpace(GetApiKey(provider))
        };
    }

    public static void SaveProfile(string provider,string model,string? apiKey)
    {
        SaveProfiles(new[]{new AiProviderEdit(provider,model,apiKey)});
    }

    /// <summary>Identifies configuration changes without exposing or hashing a stored key.</summary>
    public static string ConfigurationFingerprint(string provider)
    {
        var profile=GetProfile(provider);
        ConfigurationRevisions.TryGetValue(provider,out var revision);
        return provider+"|"+profile.Model+"|"+profile.HasKey+"|"+revision;
    }

    internal static void SaveProfiles(IReadOnlyList<AiProviderEdit> edits,
        IReadOnlyDictionary<string,byte[]>? additionalFiles=null)
    {
        var store=LoadStore(true);
        var updateKeys=edits.Any(edit=>edit.ApiKey!=null);
        var secrets=updateKeys?LoadSecrets(true):null;
        var changed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var edit in edits)
        {
            var provider=AiManager.DirectProviders.FirstOrDefault(name=>name.Equals(edit.Provider,StringComparison.OrdinalIgnoreCase))
                ??throw new ArgumentException("Choose ChatGPT, Claude or Gemini for direct AI.",nameof(edits));
            var model=string.IsNullOrWhiteSpace(edit.Model)?DefaultModel(provider):edit.Model.Trim();
            if(model.Length>200 || model.Any(char.IsControl))
                throw new ArgumentException("Use a model identifier of no more than 200 characters, without line breaks.",nameof(edits));
            if(!store.Models.TryGetValue(provider,out var oldModel) || oldModel!=model)changed.Add(provider);
            store.Models[provider]=model;
            if(edit.ApiKey==null)continue;
            var key=edit.ApiKey.Trim();
            if(key.Length>16384 || key.Any(char.IsControl))
                throw new ArgumentException("The API key contains unsupported characters or is too long.",nameof(edits));
            secrets!.TryGetValue(provider,out var oldKey);
            if((oldKey??"")!=key)changed.Add(provider);
            if(key.Length==0)secrets.Remove(provider);else secrets[provider]=key;
        }
        var writes=new Dictionary<string,byte[]>{{ProfilesPath,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(store))}};
        if(updateKeys)
        {
            var plain=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets));
            try{writes[SecretsPath]=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}
            finally{CryptographicOperations.ZeroMemory(plain);}
        }
        if(additionalFiles!=null)foreach(var file in additionalFiles)writes.Add(file.Key,file.Value);
        WriteConfigurationFiles(writes);
        foreach(var provider in changed)
            ConfigurationRevisions.AddOrUpdate(provider,1,(_,revision)=>revision+1);
    }

    internal static void WriteConfigurationFiles(IReadOnlyDictionary<string,byte[]> writes)
    {
        var original=writes.Keys.ToDictionary(path=>path,path=>File.Exists(path)?File.ReadAllBytes(path):null);
        var committed=new List<string>();
        try
        {
            foreach(var file in writes)
            {
                WriteAtomic(file.Key,file.Value);committed.Add(file.Key);
            }
        }
        catch(Exception error)
        {
            Exception? restoreError=null;
            foreach(var path in committed.AsEnumerable().Reverse())
            {
                try{if(original[path] is { } bytes)WriteAtomic(path,bytes);else File.Delete(path);}
                catch(Exception restore){restoreError=restore;}
            }
            if(restoreError!=null)throw new IOException("AI settings could not be saved and their previous files could not all be restored. Check file access before trying again.",restoreError);
            throw new IOException("AI settings could not be saved. Previous settings and keys were left unchanged. Check file access and try again.",error);
        }
    }

    static void WriteAtomic(string path,byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,content);File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }

    public static async Task<string> GenerateAsync(string provider,string prompt,bool liveWeb,CancellationToken cancellationToken=default)
    {
        var profile=GetProfile(provider);
        var key=GetApiKey(provider);
        if(string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key is configured for {provider}. Open AI Settings, or use the browser fallback.");

        return provider switch
        {
            "ChatGPT"=>await OpenAiAsync(profile.Model,key,prompt,liveWeb,cancellationToken),
            "Claude"=>await AnthropicAsync(profile.Model,key,prompt,cancellationToken),
            "Gemini"=>await GeminiAsync(profile.Model,key,prompt,liveWeb,cancellationToken),
            "Copilot"=>throw new NotSupportedException("Microsoft Copilot Chat does not expose a general consumer chat API. Use the browser fallback."),
            _=>throw new InvalidOperationException("Unknown AI provider.")
        };
    }

    static async Task<string> OpenAiAsync(string model,string key,string prompt,bool liveWeb,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/responses");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        var body=new Dictionary<string,object?>
        {
            ["model"]=model,
            ["input"]=prompt
        };
        if(liveWeb)body["tools"]=new[]{new Dictionary<string,object>{{"type","web_search"}}};
        req.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var res=await Http.SendAsync(req,ct);
        var json=await res.Content.ReadAsStringAsync(ct);
        if(!res.IsSuccessStatusCode)throw new InvalidOperationException(ApiError("OpenAI",res.StatusCode,json));
        using var doc=JsonDocument.Parse(json);
        var texts=new List<string>();
        if(doc.RootElement.TryGetProperty("output",out var output))
        foreach(var item in output.EnumerateArray())
        {
            if(item.TryGetProperty("content",out var content))
            foreach(var part in content.EnumerateArray())
                if(part.TryGetProperty("text",out var t)&&t.ValueKind==JsonValueKind.String)
                    texts.Add(t.GetString()??"");
        }
        var answer=string.Join("\n",texts).Trim();
        if(answer.Length==0)throw new InvalidOperationException("OpenAI returned no text output.");
        return answer;
    }

    static async Task<string> AnthropicAsync(string model,string key,string prompt,CancellationToken ct)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key",key);
        req.Headers.Add("anthropic-version","2023-06-01");
        var body=new
        {
            model,
            max_tokens=8192,
            messages=new[]{new{role="user",content=prompt}}
        };
        req.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var res=await Http.SendAsync(req,ct);
        var json=await res.Content.ReadAsStringAsync(ct);
        if(!res.IsSuccessStatusCode)throw new InvalidOperationException(ApiError("Anthropic",res.StatusCode,json));
        using var doc=JsonDocument.Parse(json);
        var texts=new List<string>();
        if(doc.RootElement.TryGetProperty("content",out var content))
        foreach(var part in content.EnumerateArray())
            if(part.TryGetProperty("text",out var t)&&t.ValueKind==JsonValueKind.String)
                texts.Add(t.GetString()??"");
        var answer=string.Join("\n",texts).Trim();
        if(answer.Length==0)throw new InvalidOperationException("Claude returned no text output.");
        return answer;
    }

    static async Task<string> GeminiAsync(string model,string key,string prompt,bool liveWeb,CancellationToken ct)
    {
        var url=$"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        using var req=new HttpRequestMessage(HttpMethod.Post,url);
        req.Headers.Add("x-goog-api-key",key);
        var body=new Dictionary<string,object?>
        {
            ["contents"]=new[]{new{parts=new[]{new{text=prompt}}}}
        };
        if(liveWeb)body["tools"]=new[]{new Dictionary<string,object>{{"google_search",new Dictionary<string,object>()}}};
        req.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var res=await Http.SendAsync(req,ct);
        var json=await res.Content.ReadAsStringAsync(ct);
        if(!res.IsSuccessStatusCode)throw new InvalidOperationException(ApiError("Gemini",res.StatusCode,json));
        using var doc=JsonDocument.Parse(json);
        var texts=new List<string>();
        if(doc.RootElement.TryGetProperty("candidates",out var candidates))
        foreach(var candidate in candidates.EnumerateArray())
            if(candidate.TryGetProperty("content",out var content)&&content.TryGetProperty("parts",out var parts))
                foreach(var part in parts.EnumerateArray())
                    if(part.TryGetProperty("text",out var t)&&t.ValueKind==JsonValueKind.String)
                        texts.Add(t.GetString()??"");
        var answer=string.Join("\n",texts).Trim();
        if(answer.Length==0)throw new InvalidOperationException("Gemini returned no text output.");
        return answer;
    }

    static string ApiError(string provider,System.Net.HttpStatusCode status,string json)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.TryGetProperty("error",out var err))
            {
                if(err.ValueKind==JsonValueKind.Object&&err.TryGetProperty("message",out var msg))
                    return $"{provider} API error {(int)status}: {msg.GetString()}";
                if(err.ValueKind==JsonValueKind.String)return $"{provider} API error {(int)status}: {err.GetString()}";
            }
        }catch{}
        return $"{provider} API error {(int)status}.";
    }

    static Store LoadStore(bool strict=false)
    {
        try
        {
            if(File.Exists(ProfilesPath))
            {
                var result=JsonSerializer.Deserialize<Store>(File.ReadAllText(ProfilesPath));
                if(result?.Models==null)throw new InvalidDataException("Missing provider models.");
                result.Models=new Dictionary<string,string>(result.Models,StringComparer.OrdinalIgnoreCase);
                return result;
            }
        }
        catch(Exception error)
        {
            if(strict)throw new InvalidOperationException("The saved AI model settings could not be read. Existing settings were left unchanged.",error);
        }
        return new Store();
    }

    static string GetApiKey(string provider)
    {
        var map=LoadSecrets();
        return map.TryGetValue(provider,out var key)?key:"";
    }

    static Dictionary<string,string> LoadSecrets(bool strict=false)
    {
        try
        {
            if(!File.Exists(SecretsPath))return new(StringComparer.OrdinalIgnoreCase);
            var protectedBytes=File.ReadAllBytes(SecretsPath);
            var plain=ProtectedData.Unprotect(protectedBytes,null,DataProtectionScope.CurrentUser);
            try
            {
                var result=JsonSerializer.Deserialize<Dictionary<string,string>>(plain)
                    ??throw new InvalidDataException("Missing provider keys.");
                return new Dictionary<string,string>(result,StringComparer.OrdinalIgnoreCase);
            }
            finally{CryptographicOperations.ZeroMemory(plain);}
        }
        catch(Exception error)
        {
            if(strict)throw new InvalidOperationException("The saved AI key store could not be read. Existing keys were left unchanged.",error);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
