using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class ToolSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Write_And_Exact_Patch_Stay_Inside_Workspace()
    {
        Directory.CreateDirectory(_root);
        var tools = new WorkspaceTools(new WorkspaceBoundary(_root));
        var path = Path.Combine(_root, "src", "Sample.cs");
        await tools.WriteFileAsync(path, "class Before { }");
        await tools.ApplyExactReplacementAsync(path, "Before", "After");
        Assert.Equal("class After { }", await tools.ReadFileAsync(path));
    }

    [Fact]
    public async Task Searches_Text_Without_Leaving_Workspace()
    {
        Directory.CreateDirectory(_root);
        var tools = new WorkspaceTools(new WorkspaceBoundary(_root));
        await tools.WriteFileAsync(Path.Combine(_root, "src", "One.cs"), "class Needle { }");
        await tools.WriteFileAsync(Path.Combine(_root, "src", "Two.cs"), "class Other { }");
        var results = await tools.SearchTextAsync("Needle");
        var match = Assert.Single(results);
        Assert.Contains("One.cs", match);
    }

    [Theory]
    [InlineData("rm -rf .")]
    [InlineData("Remove-Item -Recurse .")]
    [InlineData("diskpart")]
    public void Dangerous_Commands_Are_Blocked(string command)
    {
        Assert.Throws<UnauthorizedAccessException>(() => new CommandPolicy().EnsureAllowed(command));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
