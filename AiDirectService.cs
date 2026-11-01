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

public static class AiDirectService
{
    static readonly HttpClient Http = new(){Timeout=TimeSpan.FromMinutes(3)};
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CtrlZed");
    static readonly string ProfilesPath=Path.Combine(Root,"ai-profiles.json");
    static readonly string SecretsPath=Path.Combine(Root,"ai-secrets.bin");

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
        Directory.CreateDirectory(Root);
        var store=LoadStore();
        store.Models[provider]=string.IsNullOrWhiteSpace(model)?DefaultModel(provider):model.Trim();
        File.WriteAllText(ProfilesPath,JsonSerializer.Serialize(store));
        if(apiKey!=null)SaveApiKey(provider,apiKey.Trim());
    }

    public static async Task<string> GenerateAsync(string provider,string prompt,bool liveWeb,CancellationToken cancellationToken=default)
    {
        var profile=GetProfile(provider);
        var key=GetApiKey(provider);
        if(string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key is configured for {provider}. Use Configure Direct AI, or use the browser fallback.");

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

    static Store LoadStore()
    {
        try
        {
            if(File.Exists(ProfilesPath))
                return JsonSerializer.Deserialize<Store>(File.ReadAllText(ProfilesPath))??new Store();
        }catch{}
        return new Store();
    }

    static void SaveApiKey(string provider,string key)
    {
        Directory.CreateDirectory(Root);
        var map=LoadSecrets();
        if(string.IsNullOrWhiteSpace(key))map.Remove(provider);
        else map[provider]=key;
        var plain=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(map));
        var protectedBytes=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretsPath,protectedBytes);
    }

    static string GetApiKey(string provider)
    {
        var map=LoadSecrets();
        return map.TryGetValue(provider,out var key)?key:"";
    }

    static Dictionary<string,string> LoadSecrets()
    {
        try
        {
            if(!File.Exists(SecretsPath))return new(StringComparer.OrdinalIgnoreCase);
            var protectedBytes=File.ReadAllBytes(SecretsPath);
            var plain=ProtectedData.Unprotect(protectedBytes,null,DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string,string>>(Encoding.UTF8.GetString(plain))
                   ??new(StringComparer.OrdinalIgnoreCase);
        }
        catch{return new(StringComparer.OrdinalIgnoreCase);}
    }
}
