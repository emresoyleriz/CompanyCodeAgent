using CompanyCodeAgent.Application;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Protocol;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class ApprovedToolExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-executor-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_Is_Not_Applied_Without_Approval()
    {
        Directory.CreateDirectory(_root);
        var boundary = new WorkspaceBoundary(_root);
        var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy());
        var call = new ToolCall("write", AgentToolKind.WriteFile, new Dictionary<string, string> { ["path"] = "Created.cs", ["content"] = "class Created {}" });
        var result = await executor.ExecuteAsync(call, approved: false);
        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(_root, "Created.cs")));
    }

    [Fact]
    public async Task Approved_Write_Creates_Checkpoint_And_Can_Be_Restored()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Sample.cs");
        await File.WriteAllTextAsync(path, "before");
        var boundary = new WorkspaceBoundary(_root);
        var storage = new AgentStorage(Path.Combine(_root, "agent.db"));
        var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy(), storage);
        var write = new ToolCall("write", AgentToolKind.WriteFile, new Dictionary<string, string> { ["path"] = "Sample.cs", ["content"] = "after" });
        var result = await executor.ExecuteAsync(write, approved: true, sessionId: "session");
        Assert.True(result.Success);
        Assert.Contains("Checkpoint:", result.Output);
        Assert.Equal("after", await File.ReadAllTextAsync(path));
        var checkpoint = result.Output.Split('\n')[0].Split(':')[1].Trim();
        var restore = new ToolCall("restore", AgentToolKind.RestoreCheckpoint, new Dictionary<string, string> { ["checkpointId"] = checkpoint });
        var restored = await executor.ExecuteAsync(restore, approved: true, sessionId: "session");
        Assert.True(restored.Success);
        Assert.Equal("before", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Host_Side_Policy_Rejects_Write_Even_If_Client_Marks_It_As_Not_Requiring_Approval()
    {
        var boundary = new WorkspaceBoundary(_root);
        var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy());
        var call = new ToolCall("write", AgentToolKind.WriteFile, new Dictionary<string, string> { ["path"] = "blocked.txt", ["content"] = "no" }, RequiresApproval: false);

        var result = await executor.ExecuteAsync(call, approved: false);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(_root, "blocked.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
