using System.Text.Json;
using CompanyCodeAgent.Protocol;

namespace CompanyCodeAgent.Application;

public static class ToolCallParser
{
    public static bool TryParse(string response, out ToolCall? toolCall)
    {
        toolCall = null;
        var json = ExtractJson(response);
        if (json is null) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "tool_call", StringComparison.OrdinalIgnoreCase)) return false;
            if (!root.TryGetProperty("tool", out var toolName) || !Enum.TryParse<AgentToolKind>(toolName.GetString(), true, out var kind)) return false;
            var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object)
                foreach (var item in argumentsElement.EnumerateObject()) arguments[item.Name] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.GetRawText();
            var id = root.TryGetProperty("id", out var idElement) && !string.IsNullOrWhiteSpace(idElement.GetString()) ? idElement.GetString()! : Guid.NewGuid().ToString("N");
            toolCall = new ToolCall(id, kind, arguments, RequiresApproval(kind));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool RequiresApproval(AgentToolKind kind) => kind is AgentToolKind.WriteFile or AgentToolKind.ApplyPatch or AgentToolKind.DeleteFile or AgentToolKind.RunCommand or AgentToolKind.BuildSolution or AgentToolKind.RunTests;

    private static string? ExtractJson(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var start = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            return start >= 0 && end > start ? trimmed[(start + 1)..end].Trim() : null;
        }
        return trimmed.StartsWith("{") && trimmed.EndsWith("}") ? trimmed : null;
    }
}
