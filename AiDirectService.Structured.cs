using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CtrlZed;

internal enum AiStructuredFailureKind { Refused, Incomplete, Failed, Empty, InvalidEnvelope }

internal sealed class AiStructuredResponseException : InvalidOperationException
{
    internal AiStructuredFailureKind Kind { get; }
    internal AiStructuredResponseException(AiStructuredFailureKind kind,string message,Exception? inner=null)
        :base(message,inner)=>Kind=kind;
}

public static partial class AiDirectService
{
    // Used only by the deterministic HTTP-contract gate, never persisted in a
    // profile. Ordinary free-text requests retain their existing transport.
    internal static Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>? StructuredSendOverride { get; set; }

    public static Task<string> GenerateJsonAsync(string provider,string prompt,string schemaName,
        JsonElement schema,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(provider!="ChatGPT")
            return GenerateAsync(provider,prompt,false,cancellationToken);
        var profile=GetProfile(provider);
        var key=GetApiKey(provider);
        if(string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key is configured for {provider}. Use Configure Direct AI.");
        return GenerateOpenAiJsonAsync(profile.Model,key,prompt,schemaName,schema,cancellationToken);
    }

    internal static async Task<string> GenerateOpenAiJsonAsync(string model,string key,string prompt,
        string schemaName,JsonElement schema,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(string.IsNullOrWhiteSpace(schemaName) || schemaName.Length>64 ||
            schemaName.Any(ch=>!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')))
            throw new ArgumentException("Use a supported name for the workshop response format.",nameof(schemaName));
        if(schema.ValueKind!=JsonValueKind.Object)
            throw new ArgumentException("The workshop response format must be a JSON schema object.",nameof(schema));

        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.openai.com/v1/responses");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        var body=new
        {
            model,input=prompt,
            text=new{format=new{type="json_schema",name=schemaName,strict=true,schema}}
        };
        request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        try
        {
            var send=StructuredSendOverride;
            using var response=send==null
                ?await Http.SendAsync(request,cancellationToken)
                :await send(request,cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var json=await response.Content.ReadAsStringAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if(!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ApiError("OpenAI",response.StatusCode,json));
            return ParseOpenAiJsonResponse(json);
        }
        catch(OperationCanceledException error) when(!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout is an error, not the writer choosing Cancel.
            // Keep user cancellation on its existing neutral UI path.
            throw new TimeoutException("The OpenAI request timed out before a complete reply arrived. Nothing from this reply was saved or applied. Try again when ready.",error);
        }
    }

    internal static string ParseOpenAiJsonResponse(string json)
    {
        JsonDocument document;
        try{document=JsonDocument.Parse(json);}
        catch(JsonException error)
        {
            throw new AiStructuredResponseException(AiStructuredFailureKind.InvalidEnvelope,
                "OpenAI returned an unreadable service response. No saved work was changed. Try the request again.",error);
        }
        using(document)
        {
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("status",out var status) || status.ValueKind!=JsonValueKind.String)
                throw BadEnvelope();
            var state=status.GetString();
            if(state=="incomplete")
            {
                var outputLimit=root.TryGetProperty("incomplete_details",out var details) && details.ValueKind==JsonValueKind.Object &&
                    details.TryGetProperty("reason",out var reason) && reason.ValueKind==JsonValueKind.String && reason.GetString()=="max_output_tokens";
                throw new AiStructuredResponseException(AiStructuredFailureKind.Incomplete,outputLimit
                    ?"OpenAI stopped before completing its reply because it reached the output limit. Try a shorter request. No saved work was changed."
                    :"OpenAI stopped before completing its workshop reply. No partial result was saved. Adjust the wording or try again.");
            }
            if(state!="completed")
                throw new AiStructuredResponseException(AiStructuredFailureKind.Failed,
                    "OpenAI did not complete the workshop request. No saved work was changed. Try again when ready.");
            if(root.TryGetProperty("error",out var serviceError) && serviceError.ValueKind!=JsonValueKind.Null)
                throw new AiStructuredResponseException(AiStructuredFailureKind.Failed,
                    "OpenAI reported a service error for this request. No saved work was changed. Try again when ready.");
            if(!root.TryGetProperty("output",out var output) || output.ValueKind!=JsonValueKind.Array)
                throw BadEnvelope();
            var texts=new List<string>();
            foreach(var item in output.EnumerateArray())
            {
                // Reasoning summaries and other output items are not workshop
                // data, even if their text happens to resemble JSON.
                if(item.ValueKind!=JsonValueKind.Object || !IsString(item,"type","message") || !IsString(item,"role","assistant"))continue;
                if(item.TryGetProperty("status",out var messageStatus) &&
                    (messageStatus.ValueKind!=JsonValueKind.String || messageStatus.GetString()!="completed"))
                    throw new AiStructuredResponseException(AiStructuredFailureKind.Incomplete,
                        "OpenAI returned an unfinished workshop message. No partial result was saved. Try again.");
                if(!item.TryGetProperty("content",out var content) || content.ValueKind!=JsonValueKind.Array)throw BadEnvelope();
                foreach(var part in content.EnumerateArray())
                {
                    if(part.ValueKind!=JsonValueKind.Object)throw BadEnvelope();
                    if(IsString(part,"type","refusal"))
                        throw new AiStructuredResponseException(AiStructuredFailureKind.Refused,
                            "OpenAI declined this request and did not produce a workshop reply. Adjust the seed or wording and try again. No saved work was changed.");
                    if(IsString(part,"type","output_text"))
                    {
                        if(!part.TryGetProperty("text",out var text) || text.ValueKind!=JsonValueKind.String)throw BadEnvelope();
                        texts.Add(text.GetString()??"");
                    }
                }
            }
            var result=string.Concat(texts).Trim();
            if(result.Length==0)
                throw new AiStructuredResponseException(AiStructuredFailureKind.Empty,
                    "OpenAI completed the request without a workshop reply. No saved work was changed. Try again.");
            // The service's existing strict schema, length, identity and source
            // validators still decide whether this completed text is usable.
            return result;
        }
    }

    static bool IsString(JsonElement value,string name,string expected)
        =>value.TryGetProperty(name,out var property) && property.ValueKind==JsonValueKind.String && property.GetString()==expected;

    static AiStructuredResponseException BadEnvelope()=>new(AiStructuredFailureKind.InvalidEnvelope,
        "OpenAI returned an unexpected service response. No saved work was changed. Try again.");
}
