using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Tools;

namespace CompanyCodeAgent.UnitTests;

public sealed class SensitivePathPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "company-agent-sensitive-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Sensitive_Files_Are_Not_Listed_Or_Readable_By_Agent_Tools()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, ".env"), "API_KEY=secret");
        await File.WriteAllTextAsync(Path.Combine(_root, "Program.cs"), "class Program { }");
        var tools = new WorkspaceTools(new WorkspaceBoundary(_root));

        var files = tools.ListFiles().ToList();

        Assert.DoesNotContain(files, path => Path.GetFileName(path) == ".env");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => tools.ReadFileAsync(".env"));
        Assert.Contains(files, path => Path.GetFileName(path) == "Program.cs");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
