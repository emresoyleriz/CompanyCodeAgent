using System.IO.Pipes;
using System.Text.Json;
using CompanyCodeAgent.Application;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Protocol;
using CompanyCodeAgent.Tools;

const string PipeName = "CompanyCodeAgent.v1";
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

if (args.Contains("--health", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("healthy");
    return;
}

Console.WriteLine($"Company Code Agent Host dinliyor: {PipeName}");
var storage = new AgentStorage();
while (true)
{
    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.WaitForConnectionAsync();
    try
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var requestLine = await reader.ReadLineAsync();
        var request = string.IsNullOrWhiteSpace(requestLine) ? null : JsonSerializer.Deserialize<HostRequest>(requestLine, json);
        if (request is null) throw new InvalidDataException("Geçersiz host isteği.");
        var sessionId = request.SessionId ?? "default";
        ToolResult result;
        if (string.Equals(request.Operation, "save_message", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.MessageRole) || request.MessageContent is null) throw new InvalidDataException("Mesaj rolü ve içeriği zorunludur.");
            storage.SaveMessage(sessionId, request.MessageRole, request.MessageContent);
            result = new ToolResult(request.RequestId, true, "Mesaj kaydedildi.");
        }
        else if (string.Equals(request.Operation, "read_messages", StringComparison.OrdinalIgnoreCase))
        {
            var messages = storage.ReadMessages(sessionId).Select(message => new { role = message.Role, content = message.Content });
            result = new ToolResult(request.RequestId, true, JsonSerializer.Serialize(messages, json));
        }
        else if (string.Equals(request.Operation, "save_usage", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.MessageRole) || !int.TryParse(request.MessageContent, out var tokens) || tokens < 0) throw new InvalidDataException("Model adı ve geçerli token sayısı zorunludur.");
            storage.RecordUsage(sessionId, request.MessageRole, tokens);
            result = new ToolResult(request.RequestId, true, "Kullanım kaydedildi.");
        }
        else if (string.Equals(request.Operation, "read_usage", StringComparison.OrdinalIgnoreCase))
        {
            var usage = storage.ReadUsage(sessionId).Select(item => new { model = item.Model, tokens = item.Tokens, createdAt = item.CreatedAt });
            result = new ToolResult(request.RequestId, true, JsonSerializer.Serialize(usage, json));
        }
        else
        {
            var boundary = new WorkspaceBoundary(request.WorkspacePath);
            var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy(), storage);
            result = await executor.ExecuteAsync(request.ToolCall, request.Approved, sessionId);
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(new HostResponse(request.RequestId, result), json));
    }
    catch (Exception ex)
    {
        var failed = new HostResponse(Guid.NewGuid().ToString("N"), new ToolResult("unknown", false, ex.Message));
        try
        {
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(failed, json));
        }
        catch (IOException)
        {
            // A cancelled or malformed client must not terminate the long-running host.
        }
    }
}
