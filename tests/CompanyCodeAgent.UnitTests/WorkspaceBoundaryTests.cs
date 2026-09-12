using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.UnitTests;

public sealed class WorkspaceBoundaryTests
{
    [Fact]
    public void Allows_A_File_Inside_The_Workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var boundary = new WorkspaceBoundary(root);
        var actual = boundary.EnsureInsideWorkspace(Path.Combine(root, "src", "Program.cs"));
        Assert.StartsWith(root, actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocks_A_File_Outside_The_Workspace()
    {
        var boundary = new WorkspaceBoundary(Path.Combine(Path.GetTempPath(), "agent-workspace"));
        Assert.Throws<UnauthorizedAccessException>(() => boundary.EnsureInsideWorkspace(Path.GetTempPath()));
    }
}
