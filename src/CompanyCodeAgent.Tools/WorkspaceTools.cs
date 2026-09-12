using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.Tools;

public sealed class WorkspaceTools(WorkspaceBoundary boundary)
{
    public IEnumerable<string> ListFiles(int limit = 200)
    {
        return Directory.EnumerateFiles(boundary.RootPath, "*", SearchOption.AllDirectories)
            .Where(IsWorkspaceFile)
            .Where(path => !SensitivePathPolicy.IsSensitive(path))
            .Take(limit);
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var safePath = boundary.EnsureInsideWorkspace(path);
        SensitivePathPolicy.EnsureAllowed(safePath);
        var info = new FileInfo(safePath);
        if (!info.Exists) throw new FileNotFoundException("Dosya bulunamadı.", safePath);
        if (info.Length > 512 * 1024) throw new InvalidOperationException("Dosya güvenli bağlam sınırını aşıyor.");
        return await File.ReadAllTextAsync(safePath, cancellationToken);
    }

    public IEnumerable<string> SearchFiles(string pattern, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Arama ifadesi zorunludur.", nameof(pattern));
        return Directory.EnumerateFiles(boundary.RootPath, "*", SearchOption.AllDirectories)
            .Where(IsWorkspaceFile)
            .Where(path => !SensitivePathPolicy.IsSensitive(path))
            .Where(path => Path.GetFileName(path).Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Take(limit);
    }

    public async Task<IReadOnlyList<string>> SearchTextAsync(string query, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Arama ifadesi zorunludur.", nameof(query));
        var matches = new List<string>();
        foreach (var path in ListFiles(2000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length > 512 * 1024) continue;
            string text;
            try { text = await File.ReadAllTextAsync(path, cancellationToken); }
            catch (IOException) { continue; }
            var lineNumber = 0;
            foreach (var line in text.Split('\n'))
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add($"{path}:{lineNumber}: {line.Trim()}");
                if (matches.Count >= limit) return matches;
            }
        }
        return matches;
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var safePath = boundary.EnsureInsideWorkspace(path);
        SensitivePathPolicy.EnsureAllowed(safePath);
        Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);
        await File.WriteAllTextAsync(safePath, content, cancellationToken);
    }

    public async Task ApplyExactReplacementAsync(string path, string expectedText, string replacement, CancellationToken cancellationToken = default)
    {
        var current = await ReadFileAsync(path, cancellationToken);
        var matches = current.Split(expectedText, StringSplitOptions.None).Length - 1;
        if (matches != 1) throw new InvalidOperationException($"Patch güvenle uygulanamadı: beklenen metin {matches} kez bulundu.");
        await WriteFileAsync(path, current.Replace(expectedText, replacement, StringComparison.Ordinal), cancellationToken);
    }

    public Task DeleteFileAsync(string path)
    {
        var safePath = boundary.EnsureInsideWorkspace(path);
        SensitivePathPolicy.EnsureAllowed(safePath);
        if (File.Exists(safePath)) File.Delete(safePath);
        return Task.CompletedTask;
    }

    private static bool IsWorkspaceFile(string path)
    {
        return !path.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("\\.vs\\", StringComparison.OrdinalIgnoreCase);
    }
}
