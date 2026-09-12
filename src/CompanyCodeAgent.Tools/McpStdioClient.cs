using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.Tools;

public sealed class McpStdioClient(WorkspaceBoundary boundary)
{
    private const int MaxResponseCharacters = 64 * 1024;

    public async Task<string> ListToolsAsync(string serverName, CancellationToken cancellationToken = default)
        => await SendAsync(serverName, "tools/list", new { }, cancellationToken);

    public async Task<string> CallToolAsync(string serverName, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        var server = LoadServer(serverName);
        if (server.AllowedTools is null || !server.AllowedTools.Contains(toolName, StringComparer.Ordinal) && !server.AllowedTools.Contains("*", StringComparer.Ordinal))
            throw new UnauthorizedAccessException($"MCP aracı bu proje politikası tarafından izinli değil: {toolName}");
        using var arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        return await SendAsync(server, "tools/call", new { name = toolName, arguments = arguments.RootElement }, cancellationToken);
    }

    private async Task<string> SendAsync(string serverName, string method, object parameters, CancellationToken cancellationToken)
        => await SendAsync(LoadServer(serverName), method, parameters, cancellationToken);

    private async Task<string> SendAsync(McpServerDefinition server, string method, object parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.Command)) throw new InvalidDataException("MCP sunucusu komutu boş.");
        var start = new ProcessStartInfo(server.Command)
        {
            WorkingDirectory = boundary.RootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in server.Arguments ?? []) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("MCP sunucusu başlatılamadı.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await WriteAsync(process, 1, "initialize", new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "CompanyCodeAgent", version = "0.1" } }, timeout.Token);
            await ReadResponseAsync(process, 1, timeout.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/initialized", @params = new { } }));
            await WriteAsync(process, 2, method, parameters, timeout.Token);
            return Limit(await ReadResponseAsync(process, 2, timeout.Token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task WriteAsync(Process process, int id, string method, object parameters, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadResponseAsync(Process process, int id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) throw new InvalidDataException("MCP sunucusu yanıt vermeden kapandı: " + await process.StandardError.ReadToEndAsync(cancellationToken));
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number || responseId.GetInt32() != id) continue;
            if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException("MCP hatası: " + error.GetRawText());
            if (!document.RootElement.TryGetProperty("result", out var result)) throw new InvalidDataException("MCP yanıtında result yok.");
            return result.GetRawText();
        }
    }

    private McpServerDefinition LoadServer(string name)
    {
        var path = boundary.EnsureInsideWorkspace(".company-agent/mcp.json");
        if (!File.Exists(path)) throw new FileNotFoundException("MCP yapılandırması bulunamadı. .company-agent/mcp.json oluşturun.", path);
        var config = JsonSerializer.Deserialize<McpConfiguration>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new McpConfiguration();
        return config.Servers?.SingleOrDefault(server => string.Equals(server.Name, name, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("Tanımlı MCP sunucusu bulunamadı: " + name);
    }

    private static string Limit(string value) => value.Length <= MaxResponseCharacters ? value : value[..MaxResponseCharacters] + "\n[MCP çıktısı güvenlik sınırı nedeniyle kısaltıldı.]";
}

public sealed class McpConfiguration { public List<McpServerDefinition>? Servers { get; init; } }
public sealed class McpServerDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public List<string>? Arguments { get; init; }
    public List<string>? AllowedTools { get; init; }
}
