namespace CompanyCodeAgent.Domain;

public sealed class ProjectRulesLoader(WorkspaceBoundary boundary)
{
    private static readonly string[] RulePaths = ["AGENTS.md", ".company-agent/rules.md", ".github/copilot-instructions.md", ".clinerules", "CLAUDE.md", "GEMINI.md", "REVIEW.md"];

    public async Task<string> LoadAsync(CancellationToken cancellationToken = default)
        => await LoadAsync(null, cancellationToken);

    public async Task<string> LoadAsync(string? activeRelativePath, CancellationToken cancellationToken = default)
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
        foreach (var relativePath in await LoadPathRuleFilesAsync(activeRelativePath, cancellationToken))
        {
            var path = boundary.EnsureInsideWorkspace(relativePath);
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) continue;
            rules.Add($"--- {relativePath} ---\n{await File.ReadAllTextAsync(path, cancellationToken)}");
        }
        return string.Join("\n\n", rules);
    }

    private async Task<IReadOnlyList<string>> LoadPathRuleFilesAsync(string? activeRelativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(activeRelativePath)) return [];
        var normalizedPath = activeRelativePath.Replace('\\', '/').TrimStart('/');
        if (normalizedPath.StartsWith("../", StringComparison.Ordinal) || normalizedPath.Contains("/../", StringComparison.Ordinal)) return [];
        var mappingPath = boundary.EnsureInsideWorkspace(".company-agent/rules.paths");
        if (!File.Exists(mappingPath) || new FileInfo(mappingPath).Length > 64 * 1024) return [];
        var matched = new List<string>();
        foreach (var line in (await File.ReadAllLinesAsync(mappingPath, cancellationToken)).Select(value => value.Trim()))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) continue;
            var pattern = line[..separator].Trim().Replace('\\', '/');
            var ruleFile = line[(separator + 1)..].Trim().Replace('\\', '/');
            if (GlobMatches(pattern, normalizedPath) && !matched.Contains(ruleFile, StringComparer.OrdinalIgnoreCase)) matched.Add(ruleFile);
        }
        return matched;
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
