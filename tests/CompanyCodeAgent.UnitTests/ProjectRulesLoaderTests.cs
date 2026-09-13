using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.UnitTests;

public sealed class ProjectRulesLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-rules-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Loads_Supported_Project_Rule_Files()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".company-agent"));
        Directory.CreateDirectory(Path.Combine(_root, ".github"));
        await File.WriteAllTextAsync(Path.Combine(_root, "AGENTS.md"), "Use xUnit.");
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "rules.md"), "Do not change public APIs.");
        await File.WriteAllTextAsync(Path.Combine(_root, ".github", "copilot-instructions.md"), "Use repository naming conventions.");
        var rules = await new ProjectRulesLoader(new WorkspaceBoundary(_root)).LoadAsync();
        Assert.Contains("Use xUnit.", rules);
        Assert.Contains("Do not change public APIs.", rules);
        Assert.Contains("Use repository naming conventions.", rules);
    }

    [Fact]
    public async Task Loads_Only_Path_Rules_Matching_The_Active_File()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".company-agent", "rules"));
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "rules.paths"), "src/**/*.cs=.company-agent/rules/csharp.md\ntests/**=.company-agent/rules/tests.md");
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "rules", "csharp.md"), "Use nullable reference types.");
        await File.WriteAllTextAsync(Path.Combine(_root, ".company-agent", "rules", "tests.md"), "Keep tests deterministic.");

        var rules = await new ProjectRulesLoader(new WorkspaceBoundary(_root)).LoadAsync("src/Services/OrderService.cs");

        Assert.Contains("Use nullable reference types.", rules);
        Assert.DoesNotContain("Keep tests deterministic.", rules);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
