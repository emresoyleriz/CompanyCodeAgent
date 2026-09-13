using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CompanyCodeAgent.VisualStudio;

internal sealed class AgentHostClient
{
    private const string PipeName = "CompanyCodeAgent.v1";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly object HostStartGate = new object();
    private static bool _hostStartAttempted;
    private readonly string _sessionId;

    public AgentHostClient(string sessionId = null)
    {
        _sessionId = sessionId;
    }

    public async Task<HostToolResult> ExecuteAsync(string workspacePath, int toolKind, IDictionary<string, string> arguments, bool requiresApproval, bool approved)
    {
        try
        {
            return await SendAsync(workspacePath, toolKind, arguments, requiresApproval, approved, 250);
        }
        catch (TimeoutException) { }
        catch (IOException) { }

        await EnsureHostStartedAsync();
        try { return await SendAsync(workspacePath, toolKind, arguments, requiresApproval, approved, 5000); }
        catch { ResetHostStartAttempt(); throw; }
    }

    public async Task SaveMessageAsync(string workspacePath, string role, string content)
    {
        await ExecuteOperationAsync(workspacePath, "save_message", role, content);
    }

    public async Task<string> ReadMessagesAsync(string workspacePath)
    {
        var result = await ExecuteOperationAsync(workspacePath, "read_messages", null, null);
        return result.Output;
    }

    public async Task SaveUsageAsync(string workspacePath, string model, int tokens)
    {
        await ExecuteOperationAsync(workspacePath, "save_usage", model, tokens.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<string> ReadUsageAsync(string workspacePath)
    {
        var result = await ExecuteOperationAsync(workspacePath, "read_usage", null, null);
        return result.Output;
    }

    private async Task<HostToolResult> ExecuteOperationAsync(string workspacePath, string operation, string role, string content)
    {
        try { return await SendOperationAsync(workspacePath, operation, role, content, 250); }
        catch (TimeoutException) { }
        catch (IOException) { }
        await EnsureHostStartedAsync();
        try { return await SendOperationAsync(workspacePath, operation, role, content, 5000); }
        catch { ResetHostStartAttempt(); throw; }
    }

    private async Task<HostToolResult> SendAsync(string workspacePath, int toolKind, IDictionary<string, string> arguments, bool requiresApproval, bool approved, int timeoutMilliseconds)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutMilliseconds);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        using var reader = new StreamReader(pipe);
        var requestId = Guid.NewGuid().ToString("N");
        var request = new
        {
            RequestId = requestId,
            WorkspacePath = workspacePath,
            ToolCall = new { Id = requestId, Kind = toolKind, Arguments = arguments, RequiresApproval = requiresApproval },
            Approved = approved,
            SessionId = ResolveSessionId(workspacePath)
        };
        await writer.WriteLineAsync(Json.Serialize(request));
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException("Agent host yanıt döndürmedi.");
        var root = Json.DeserializeObject(line) as Dictionary<string, object>;
        if (root == null || !root.TryGetValue("result", out var raw) || raw is not Dictionary<string, object> result) throw new InvalidOperationException("Agent host yanıtı okunamadı.");
        return new HostToolResult(
            result.TryGetValue("success", out var success) && Convert.ToBoolean(success),
            result.TryGetValue("output", out var output) ? output?.ToString() ?? string.Empty : string.Empty,
            result.TryGetValue("wasApplied", out var applied) && Convert.ToBoolean(applied));
    }

    private async Task<HostToolResult> SendOperationAsync(string workspacePath, string operation, string role, string content, int timeoutMilliseconds)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeoutMilliseconds);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        using var reader = new StreamReader(pipe);
        var requestId = Guid.NewGuid().ToString("N");
        var request = new
        {
            RequestId = requestId,
            WorkspacePath = workspacePath,
            ToolCall = new { Id = requestId, Kind = 0, Arguments = new Dictionary<string, string>(), RequiresApproval = false },
            Approved = true,
            SessionId = ResolveSessionId(workspacePath),
            Operation = operation,
            MessageRole = role,
            MessageContent = content
        };
        await writer.WriteLineAsync(Json.Serialize(request));
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException("Agent host yanıt döndürmedi.");
        var root = Json.DeserializeObject(line) as Dictionary<string, object>;
        if (root == null || !root.TryGetValue("result", out var raw) || raw is not Dictionary<string, object> result) throw new InvalidOperationException("Agent host yanıtı okunamadı.");
        return new HostToolResult(result.TryGetValue("success", out var success) && Convert.ToBoolean(success), result.TryGetValue("output", out var output) ? output?.ToString() ?? string.Empty : string.Empty, result.TryGetValue("wasApplied", out var applied) && Convert.ToBoolean(applied));
    }

    private static async Task EnsureHostStartedAsync()
    {
        lock (HostStartGate)
        {
            if (_hostStartAttempted) return;
            _hostStartAttempted = true;
        }

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var hostDll = Path.Combine(assemblyDirectory, "AgentHost", "CompanyCodeAgent.Host.dll");
        if (!File.Exists(hostDll)) throw new FileNotFoundException("Yerel agent host pakette bulunamadı.", hostDll);
        Process.Start(new ProcessStartInfo("dotnet", "\"" + hostDll + "\"") { UseShellExecute = false, CreateNoWindow = true });
        await Task.Delay(600);
    }

    private static void ResetHostStartAttempt()
    {
        lock (HostStartGate) _hostStartAttempted = false;
    }

    internal static string CreateSessionId(string workspacePath)
    {
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(workspacePath).ToUpperInvariant()));
        return BitConverter.ToString(bytes, 0, 12).Replace("-", string.Empty);
    }

    private string ResolveSessionId(string workspacePath) => string.IsNullOrWhiteSpace(_sessionId) ? CreateSessionId(workspacePath) : _sessionId;
}

internal sealed class HostToolResult
{
    public HostToolResult(bool success, string output, bool wasApplied) { Success = success; Output = output; WasApplied = wasApplied; }
    public bool Success { get; }
    public string Output { get; }
    public bool WasApplied { get; }
}
