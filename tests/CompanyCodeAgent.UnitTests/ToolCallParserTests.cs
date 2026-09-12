using CompanyCodeAgent.Application;
using CompanyCodeAgent.Protocol;

namespace CompanyCodeAgent.UnitTests;

public sealed class ToolCallParserTests
{
    [Fact]
    public void Parses_Read_File_Tool_Call()
    {
        const string response = """{"type":"tool_call","id":"read-1","tool":"ReadFile","arguments":{"path":"src/Program.cs"}}""";
        var parsed = ToolCallParser.TryParse(response, out var call);
        Assert.True(parsed);
        Assert.NotNull(call);
        Assert.Equal(AgentToolKind.ReadFile, call!.Kind);
        Assert.False(call.RequiresApproval);
        Assert.Equal("src/Program.cs", call.Arguments["path"]);
    }

    [Fact]
    public void Marks_File_Writes_As_Approval_Required()
    {
        const string response = "```json\n{\"type\":\"tool_call\",\"tool\":\"WriteFile\",\"arguments\":{\"path\":\"src/Program.cs\",\"content\":\"updated\"}}\n```";
        Assert.True(ToolCallParser.TryParse(response, out var call));
        Assert.True(call!.RequiresApproval);
    }

    [Theory]
    [InlineData("list_files", AgentToolKind.ListFiles, false)]
    [InlineData("get-git-staged-diff", AgentToolKind.GetGitStagedDiff, false)]
    [InlineData("create_git_commit", AgentToolKind.CreateGitCommit, true)]
    [InlineData("web_fetch", AgentToolKind.WebFetch, true)]
    [InlineData("export_audit", AgentToolKind.ExportAudit, true)]
    public void Parses_Canonical_Snake_Or_Kebab_Case_Tool_Names(string tool, AgentToolKind expected, bool requiresApproval)
    {
        var response = "{\"type\":\"tool_call\",\"tool\":\"" + tool + "\",\"arguments\":{}}";

        Assert.True(ToolCallParser.TryParse(response, out var call));
        Assert.Equal(expected, call!.Kind);
        Assert.Equal(requiresApproval, call.RequiresApproval);
    }
}
