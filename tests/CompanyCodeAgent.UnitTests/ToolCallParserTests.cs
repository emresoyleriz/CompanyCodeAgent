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
}
