using CompanyCodeAgent.Tools;
using CompanyCodeAgent.Domain;
using CompanyCodeAgent.Application;
using CompanyCodeAgent.Protocol;
using System.Diagnostics;

namespace CompanyCodeAgent.UnitTests;

public sealed class GitWorktreeManagerTests
{
    [Fact]
    public void Managed_Worktree_Path_Is_Stable_And_Outside_Source_Workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "company-agent-project");
        var first = GitWorktreeManager.GetManagedWorktreePath(root, "feature/auth");
        var second = GitWorktreeManager.GetManagedWorktreePath(root, "feature/auth");

        Assert.Equal(first, second);
        Assert.DoesNotContain(Path.GetFullPath(root), first, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("feature-auth", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creates_Real_Isolated_Worktree_For_A_Git_Repository()
    {
        var root = Path.Combine(Path.GetTempPath(), "company-agent-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? worktree = null;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.email agent@example.test");
            RunGit(root, "config user.name Agent");
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "test");
            RunGit(root, "add README.md");
            RunGit(root, "commit -m initial");

            var manager = new GitWorktreeManager(new WorkspaceBoundary(root));
            var output = await manager.CreateAsync("feature/isolated-test");
            worktree = GitWorktreeManager.GetManagedWorktreePath(root, "feature/isolated-test");

            Assert.True(Directory.Exists(worktree));
            Assert.Contains("Worktree oluşturuldu", output);
            Assert.True(File.Exists(Path.Combine(worktree, "README.md")));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(worktree) && Directory.Exists(worktree)) RunGit(root, "worktree remove --force \"" + worktree + "\"");
            if (Directory.Exists(root)) DeleteDirectoryWithNormalizedAttributes(root);
        }
    }

    [Fact]
    public async Task Commit_Tool_Commits_Only_Staged_Changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "company-agent-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RunGit(root, "init"); RunGit(root, "config user.email agent@example.test"); RunGit(root, "config user.name Agent");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "one"); RunGit(root, "add tracked.txt"); RunGit(root, "commit -m initial");
            await File.WriteAllTextAsync(Path.Combine(root, "tracked.txt"), "two");
            await File.WriteAllTextAsync(Path.Combine(root, "unstaged.txt"), "must-not-be-committed");
            RunGit(root, "add tracked.txt");
            var boundary = new WorkspaceBoundary(root);
            var executor = new ApprovedToolExecutor(new WorkspaceTools(boundary), boundary, new CommandPolicy());

            var staged = await executor.ExecuteAsync(new ToolCall("staged", AgentToolKind.GetGitStagedDiff, new Dictionary<string, string>()), true);

            var result = await executor.ExecuteAsync(new ToolCall("commit", AgentToolKind.CreateGitCommit, new Dictionary<string, string> { ["message"] = "agent commit" }), true);

            Assert.True(staged.Success);
            Assert.Contains("two", staged.Output);
            Assert.True(result.Success);
            Assert.Equal("two", File.ReadAllText(Path.Combine(root, "tracked.txt")));
            Assert.True(File.Exists(Path.Combine(root, "unstaged.txt")));
            Assert.Contains("unstaged.txt", RunGitOutput(root, "status --short"));
        }
        finally { if (Directory.Exists(root)) DeleteDirectoryWithNormalizedAttributes(root); }
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = workingDirectory, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        process!.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }

    private static string RunGitOutput(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = workingDirectory, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        process!.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(process.StandardError.ReadToEnd());
        return process.StandardOutput.ReadToEnd();
    }

    private static void DeleteDirectoryWithNormalizedAttributes(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) File.SetAttributes(entry, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
