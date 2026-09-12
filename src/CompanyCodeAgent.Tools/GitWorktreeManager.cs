using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.Tools;

public sealed class GitWorktreeManager(WorkspaceBoundary boundary)
{
    public async Task<string> ListAsync(CancellationToken cancellationToken = default)
        => await RunGitAsync(["worktree", "list", "--porcelain"], cancellationToken);

    public async Task<string> CreateAsync(string branch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch) || !System.Text.RegularExpressions.Regex.IsMatch(branch, "^[a-zA-Z0-9][a-zA-Z0-9._/-]{0,100}$") || branch.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Geçersiz branch adı.", nameof(branch));
        var path = GetManagedWorktreePath(boundary.RootPath, branch);
        if (Directory.Exists(path)) throw new InvalidOperationException("Bu worktree yolu zaten var: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = await RunGitAsync(["worktree", "add", path, "-b", branch], cancellationToken);
        return "Worktree oluşturuldu: " + path + "\n" + result;
    }

    public static string GetManagedWorktreePath(string workspacePath, string branch)
    {
        using var hash = SHA256.Create();
        var normalizedWorkspace = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var projectId = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(normalizedWorkspace)), 0, 8).Replace("-", string.Empty);
        var safeBranch = branch.Replace('/', '-').Replace('\\', '-');
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompanyCodeAgent", "worktrees", projectId, safeBranch);
    }

    private async Task<string> RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = boundary.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException(output.Trim());
        return string.IsNullOrWhiteSpace(output) ? "Git işlemi tamamlandı." : output.Trim();
    }
}
