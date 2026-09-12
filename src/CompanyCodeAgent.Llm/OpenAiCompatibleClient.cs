using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CompanyCodeAgent.Protocol;

namespace CompanyCodeAgent.Llm;

public sealed class OpenAiCompatibleClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, new Uri(settings.ApiBaseUri, "v1/models"), settings.ApiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => new ModelInfo(item.GetProperty("id").GetString()!, item.TryGetProperty("name", out var name) ? name.GetString() : null))
            .ToArray();
    }

    public async IAsyncEnumerable<string> StreamChatAsync(AgentSettings settings, IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = new { model = settings.Model, stream = true, messages = messages.Select(m => new { role = m.Role, content = m.Content, name = m.Name }) };
        using var request = CreateRequest(HttpMethod.Post, new Uri(settings.ApiBaseUri, "v1/chat/completions"), settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = line[6..];
            if (data == "[DONE]") yield break;
            using var json = JsonDocument.Parse(data);
            var choices = json.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } text) yield return text;
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string? apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }
}
