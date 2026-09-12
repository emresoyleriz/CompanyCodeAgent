using CompanyCodeAgent.Application;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Protocol;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class ProjectPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-policy-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Host_Policy_Blocks_Configured_Tool_Regardless_Of_Approval()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".company-agent"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "policy.json"), "{\"blockedTools\":[\"RunCommand\"]}");
        var boundary = new WorkspaceBoundary(_root);
        var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy());

        var result = await executor.ExecuteAsync(new ToolCall("command", AgentToolKind.RunCommand, new Dictionary<string, string> { ["command"] = "echo allowed-by-shell" }), approved: true);

        Assert.False(result.Success);
        Assert.Contains("proje politikası", result.Output);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
