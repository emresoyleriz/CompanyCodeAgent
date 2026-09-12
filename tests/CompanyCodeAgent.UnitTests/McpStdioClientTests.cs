using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class McpStdioClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-mcp-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Denies_Mcp_Tool_Outside_Project_Allowlist_Before_Starting_Process()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".company-agent"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "mcp.json"), """
            { "servers": [{ "name": "demo", "command": "must-not-run", "allowedTools": ["safe_tool"] }] }
            """);
        var client = new McpStdioClient(new WorkspaceBoundary(_root));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.CallToolAsync("demo", "unsafe_tool", "{}"));

        Assert.Contains("izinli değil", exception.Message);
    }

    [Fact]
    public async Task Denies_Http_Mcp_Tool_Outside_Project_Allowlist_Before_Network_Request()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".company-agent"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "mcp.json"), """
            { "servers": [{ "name": "remote", "url": "https://mcp.example.com/mcp", "allowedTools": ["safe_tool"] }] }
            """);
        var client = new McpStdioClient(new WorkspaceBoundary(_root));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.CallToolAsync("remote", "unsafe_tool", "{}"));

        Assert.Contains("izinli değil", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
