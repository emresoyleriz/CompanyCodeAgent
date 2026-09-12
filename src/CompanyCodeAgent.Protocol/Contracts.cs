namespace CompanyCodeAgent.Protocol;

public sealed record AgentSettings(
    Uri ApiBaseUri,
    string? ApiKey,
    string Model,
    string WorkspacePath,
    int MaxAgentSteps = 12);

public sealed record WorkspaceContext(
    string? SolutionPath,
    string? ActiveDocumentPath,
    string? SelectedText,
    string? ActiveDocumentText);

public sealed record ChatRequest(string ConversationId, string UserMessage, WorkspaceContext Context);

public sealed record ModelInfo(string Id, string? DisplayName = null);

public sealed record ChatMessage(string Role, string Content, string? Name = null);

public sealed record AgentEvent(string Type, string Content, DateTimeOffset Timestamp)
{
    public static AgentEvent Status(string content) => new("status", content, DateTimeOffset.UtcNow);
    public static AgentEvent Delta(string content) => new("delta", content, DateTimeOffset.UtcNow);
    public static AgentEvent Error(string content) => new("error", content, DateTimeOffset.UtcNow);
    public static AgentEvent Complete(string content) => new("complete", content, DateTimeOffset.UtcNow);
}

public enum AgentToolKind
{
    ListFiles,
    SearchFiles,
    ReadFile,
    ReadMultipleFiles,
    SearchText,
    WriteFile,
    ApplyPatch,
    DeleteFile,
    RunCommand,
    BuildSolution,
    RunTests,
    GetGitDiff,
    GetGitStatus,
    RestoreCheckpoint,
    ListCheckpoints,
    CompareCheckpoint,
    McpListTools,
    McpCallTool,
    CreateTask,
    UpdateTask,
    ListTasks
    ,ListGitWorktrees
    ,CreateGitWorktree
    ,ListAuditEvents
    ,WebFetch
    ,GetGitBranch
    ,CreateGitCommit
}

public sealed record ToolCall(
    string Id,
    AgentToolKind Kind,
    IReadOnlyDictionary<string, string> Arguments,
    bool RequiresApproval = true);

public sealed record ToolResult(string ToolCallId, bool Success, string Output, bool WasApplied = false);

public sealed record ProposedChange(
    string Id,
    string Path,
    string Before,
    string After,
    string Summary,
    DateTimeOffset CreatedAt);

public sealed record HostRequest(
    string RequestId,
    string WorkspacePath,
    ToolCall ToolCall,
    bool Approved,
    string? SessionId = null,
    string Operation = "tool",
    string? MessageRole = null,
    string? MessageContent = null);

public sealed record HostResponse(
    string RequestId,
    ToolResult Result);
