namespace CompanyCodeAgent.Domain;

public sealed class WorkspaceBoundary(string workspacePath)
{
    private readonly string _workspaceRoot = Path.GetFullPath(workspacePath)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    public string RootPath => _workspaceRoot;

    public string EnsureInsideWorkspace(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Dosya yolu zorunludur.", nameof(path));
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path));
        if (!fullPath.StartsWith(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("İşlem yalnızca açık solution klasörü içinde yapılabilir.");
        return fullPath;
    }
}
