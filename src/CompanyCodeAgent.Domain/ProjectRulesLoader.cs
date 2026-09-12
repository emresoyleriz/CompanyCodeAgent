namespace CompanyCodeAgent.Domain;

public sealed class ProjectRulesLoader(WorkspaceBoundary boundary)
{
    private static readonly string[] RulePaths = ["AGENTS.md", ".company-agent/rules.md", ".github/copilot-instructions.md", ".clinerules", "CLAUDE.md", "GEMINI.md", "REVIEW.md"];

    public async Task<string> LoadAsync(CancellationToken cancellationToken = default)
    {
        var rules = new List<string>();
        foreach (var relativePath in RulePaths)
        {
            var path = boundary.EnsureInsideWorkspace(relativePath);
            if (!File.Exists(path)) continue;
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) continue;
            rules.Add($"--- {relativePath} ---\n{await File.ReadAllTextAsync(path, cancellationToken)}");
        }
        return string.Join("\n\n", rules);
    }
}
