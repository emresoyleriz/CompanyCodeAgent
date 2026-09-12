using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Protocol;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.Application;

public sealed class ApprovedToolExecutor(WorkspaceTools tools, WorkspaceBoundary boundary, CommandPolicy commandPolicy, AgentStorage? storage = null, ProjectPolicy? projectPolicy = null)
{
    private const int MaxCommandOutputCharacters = 64 * 1024;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, bool approved, string sessionId = "default", CancellationToken cancellationToken = default)
    {
        if (RequiresExplicitApproval(call.Kind) && !approved)
            return new ToolResult(call.Id, false, "İşlem kullanıcı tarafından reddedildi.");
        try
        {
            (projectPolicy ?? ProjectPolicy.Load(boundary)).EnsureAllowed(call.Kind.ToString());
            string? checkpointId = null;
            if (storage != null && call.Kind is AgentToolKind.WriteFile or AgentToolKind.ApplyPatch or AgentToolKind.DeleteFile or AgentToolKind.ApplyMultiPatch)
                checkpointId = storage.CreateCheckpoint(sessionId, boundary.RootPath, GetCheckpointPaths(call));
            var output = call.Kind switch
            {
                AgentToolKind.ListFiles => string.Join(Environment.NewLine, tools.ListFiles()),
                AgentToolKind.SearchFiles => string.Join(Environment.NewLine, tools.SearchFiles(Required(call, "pattern"))),
                AgentToolKind.ReadFile => await tools.ReadFileAsync(Required(call, "path"), cancellationToken),
                AgentToolKind.ReadMultipleFiles => await ReadMultipleFilesAsync(call, cancellationToken),
                AgentToolKind.SearchText => string.Join(Environment.NewLine, await tools.SearchTextAsync(Required(call, "query"), cancellationToken: cancellationToken)),
                AgentToolKind.WriteFile => await WriteAsync(call, cancellationToken),
                AgentToolKind.ApplyPatch => await PatchAsync(call, cancellationToken),
                AgentToolKind.ApplyMultiPatch => await MultiPatchAsync(call, cancellationToken),
                AgentToolKind.DeleteFile => await DeleteAsync(call),
                AgentToolKind.RunCommand or AgentToolKind.BuildSolution or AgentToolKind.RunTests => await RunCommandAsync(call, cancellationToken),
                AgentToolKind.GetGitDiff => await RunGitDiffAsync(cancellationToken),
                AgentToolKind.GetGitStatus => await RunGitAsync("status --short", cancellationToken),
                AgentToolKind.RestoreCheckpoint => RestoreCheckpoint(call, sessionId),
                AgentToolKind.ListCheckpoints => ListCheckpoints(sessionId),
                AgentToolKind.CompareCheckpoint => CompareCheckpoint(call),
                AgentToolKind.McpListTools => await new McpStdioClient(boundary).ListToolsAsync(Required(call, "server"), cancellationToken),
                AgentToolKind.McpCallTool => await new McpStdioClient(boundary).CallToolAsync(Required(call, "server"), Required(call, "toolName"), Required(call, "argumentsJson"), cancellationToken),
                AgentToolKind.CreateTask => CreateTask(call, sessionId),
                AgentToolKind.UpdateTask => UpdateTask(call, sessionId),
                AgentToolKind.ListTasks => ListTasks(sessionId),
                AgentToolKind.ListGitWorktrees => await new GitWorktreeManager(boundary).ListAsync(cancellationToken),
                AgentToolKind.CreateGitWorktree => await new GitWorktreeManager(boundary).CreateAsync(Required(call, "branch"), cancellationToken),
                AgentToolKind.ListAuditEvents => ListAuditEvents(sessionId),
                AgentToolKind.ExportAudit => ExportAudit(call, sessionId),
                AgentToolKind.WebFetch => await new WebFetchTool().FetchAsync(Required(call, "url"), cancellationToken),
                AgentToolKind.GetGitBranch => await RunGitAsync("branch --show-current", cancellationToken),
                AgentToolKind.CreateGitCommit => await CreateGitCommitAsync(call, cancellationToken),
                AgentToolKind.GetGitStagedDiff => await RunGitAsync("diff --cached --no-ext-diff", cancellationToken),
                _ => throw new NotSupportedException($"Araç henüz desteklenmiyor: {call.Kind}")
            };
            var fullOutput = checkpointId == null ? output : $"Checkpoint: {checkpointId}\n{output}";
            storage?.WriteAudit(sessionId, "tool_executed", $"{call.Kind}: {fullOutput}");
            return new ToolResult(call.Id, true, fullOutput, call.Kind is AgentToolKind.WriteFile or AgentToolKind.ApplyPatch or AgentToolKind.ApplyMultiPatch or AgentToolKind.DeleteFile or AgentToolKind.RestoreCheckpoint);
        }
        catch (Exception ex) { storage?.WriteAudit(sessionId, "tool_failed", $"{call.Kind}: {ex.Message}"); return new ToolResult(call.Id, false, ex.Message); }
    }

    private async Task<string> WriteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        await tools.WriteFileAsync(Required(call, "path"), Required(call, "content"), cancellationToken);
        return "Dosya yazıldı.";
    }

    private async Task<string> ReadMultipleFilesAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var paths = Required(call, "paths").Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(20);
        var result = new StringBuilder();
        foreach (var path in paths)
        {
            result.AppendLine("--- " + path + " ---");
            result.AppendLine(await tools.ReadFileAsync(path, cancellationToken));
        }
        return result.ToString();
    }

    private async Task<string> PatchAsync(ToolCall call, CancellationToken cancellationToken)
    {
        await tools.ApplyExactReplacementAsync(Required(call, "path"), Required(call, "expected"), Required(call, "replacement"), cancellationToken);
        return "Patch uygulandı.";
    }

    private async Task<string> MultiPatchAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var patches = ParseMultiPatches(call);
        await tools.ApplyExactReplacementsTransactionAsync(patches, cancellationToken);
        return $"{patches.Count} dosyada transaction patch uygulandı.";
    }

    private IReadOnlyList<string> GetCheckpointPaths(ToolCall call)
    {
        if (call.Kind != AgentToolKind.ApplyMultiPatch) return [boundary.EnsureInsideWorkspace(Required(call, "path"))];
        return ParseMultiPatches(call).Select(patch => boundary.EnsureInsideWorkspace(patch.Path)).ToArray();
    }

    private static IReadOnlyList<TextReplacement> ParseMultiPatches(ToolCall call)
    {
        using var document = JsonDocument.Parse(Required(call, "patchesJson"));
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("patchesJson bir JSON dizi olmalıdır.");
        var patches = new List<TextReplacement>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("path", out var path) || !item.TryGetProperty("expected", out var expected) || !item.TryGetProperty("replacement", out var replacement))
                throw new ArgumentException("Her çoklu patch path, expected ve replacement içermelidir.");
            patches.Add(new TextReplacement(path.GetString() ?? string.Empty, expected.GetString() ?? string.Empty, replacement.GetString() ?? string.Empty));
        }
        return patches;
    }

    private async Task<string> DeleteAsync(ToolCall call)
    {
        await tools.DeleteFileAsync(Required(call, "path"));
        return "Dosya silindi.";
    }

    private async Task<string> RunCommandAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var command = call.Kind switch
        {
            AgentToolKind.BuildSolution => "dotnet build",
            AgentToolKind.RunTests => "dotnet test",
            _ => Required(call, "command")
        };
        commandPolicy.EnsureAllowed(command);
        var isWindows = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo(isWindows ? "cmd.exe" : "/bin/sh", isWindows ? "/c " + command : "-c \"" + command.Replace("\"", "\\\"") + "\"")
        {
            WorkingDirectory = boundary.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Komut başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Komut iki dakika içinde tamamlanmadığı için durduruldu.");
        }
        return LimitOutput($"Exit code: {process.ExitCode}\n{await stdout}\n{await stderr}".Trim());
    }

    private async Task<string> RunGitDiffAsync(CancellationToken cancellationToken)
    {
        return await RunGitAsync("diff --no-ext-diff", cancellationToken);
    }

    private async Task<string> CreateGitCommitAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var message = Required(call, "message").Trim();
        if (message.Length > 200) throw new ArgumentException("Commit mesajı en fazla 200 karakter olabilir.");
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = boundary.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("commit"); start.ArgumentList.Add("-m"); start.ArgumentList.Add(message);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? "Git commit başarısız." : output.Trim());
        return LimitOutput(output.Trim());
    }

    private async Task<string> RunGitAsync(string arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = boundary.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var result = (await stdout) + (await stderr);
        return string.IsNullOrWhiteSpace(result) ? "Git sonucu boş." : LimitOutput(result);
    }

    private string RestoreCheckpoint(ToolCall call, string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Checkpoint deposu kullanılabilir değil.");
        var restored = storage.RestoreCheckpoint(Required(call, "checkpointId"));
        storage.WriteAudit(sessionId, "checkpoint_restored", string.Join(";", restored));
        return "Geri yüklenen dosyalar:\n" + string.Join(Environment.NewLine, restored);
    }

    private string ListCheckpoints(string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Checkpoint deposu kullanılabilir değil.");
        var items = storage.ListCheckpoints(sessionId);
        return items.Count == 0 ? "Bu oturum için checkpoint yok." : string.Join(Environment.NewLine, items.Select(x => $"{x.Id} | {x.CreatedAt.LocalDateTime:g} | {x.FileCount} dosya"));
    }

    private string CompareCheckpoint(ToolCall call)
    {
        if (storage == null) throw new InvalidOperationException("Checkpoint deposu kullanılabilir değil.");
        var differences = storage.CompareCheckpoint(Required(call, "checkpointId"));
        return string.Join(Environment.NewLine, differences.Select(x => $"{x.Status}: {x.Path}"));
    }

    private string CreateTask(ToolCall call, string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Görev deposu kullanılabilir değil.");
        var id = storage.CreateTask(sessionId, Required(call, "title"), call.Arguments.TryGetValue("status", out var status) ? status : "pending");
        return "Görev oluşturuldu: " + id;
    }

    private string UpdateTask(ToolCall call, string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Görev deposu kullanılabilir değil.");
        if (!long.TryParse(Required(call, "id"), out var id)) throw new ArgumentException("Görev id sayısal olmalıdır.");
        storage.UpdateTask(sessionId, id, Required(call, "status"));
        return "Görev güncellendi: " + id;
    }

    private string ListTasks(string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Görev deposu kullanılabilir değil.");
        var tasks = storage.ListTasks(sessionId);
        return tasks.Count == 0 ? "Görev yok." : string.Join(Environment.NewLine, tasks.Select(task => $"{task.Id} | {task.Status} | {task.Title}"));
    }

    private string ListAuditEvents(string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Audit deposu kullanılabilir değil.");
        var events = storage.ReadAuditEvents(sessionId);
        return events.Count == 0 ? "Audit kaydı yok." : string.Join(Environment.NewLine, events.Select(item => $"{item.CreatedAt.LocalDateTime:g} | {item.EventType} | {item.Detail}"));
    }

    private string ExportAudit(ToolCall call, string sessionId)
    {
        if (storage == null) throw new InvalidOperationException("Audit deposu kullanılabilir değil.");
        var relativePath = Required(call, "path");
        var fullPath = boundary.EnsureInsideWorkspace(relativePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Audit dışa aktarma hedefi .json uzantılı olmalıdır.");
        var events = storage.ReadAuditEvents(sessionId).Select(item => new { timestamp = item.CreatedAt, type = item.EventType, detail = item.Detail });
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true }));
        return "Audit dışa aktarıldı: " + relativePath;
    }

    private static string Required(ToolCall call, string name) => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"Araç parametresi zorunlu: {name}");

    private static bool RequiresExplicitApproval(AgentToolKind kind) => kind is
        AgentToolKind.WriteFile or AgentToolKind.ApplyPatch or AgentToolKind.ApplyMultiPatch or AgentToolKind.DeleteFile or
        AgentToolKind.RunCommand or AgentToolKind.BuildSolution or AgentToolKind.RunTests or
        AgentToolKind.RestoreCheckpoint or AgentToolKind.McpListTools or AgentToolKind.McpCallTool or AgentToolKind.CreateGitWorktree or AgentToolKind.WebFetch or AgentToolKind.CreateGitCommit or AgentToolKind.ExportAudit;

    private static string LimitOutput(string value) => value.Length <= MaxCommandOutputCharacters
        ? value
        : value[..MaxCommandOutputCharacters] + "\n[Çıktı güvenlik sınırı nedeniyle kısaltıldı.]";
}
