using System.Text.Json;

namespace CompanyCodeAgent.Domain;

public sealed class ProjectPolicy
{
    public List<string> BlockedTools { get; init; } = [];

    public static ProjectPolicy Load(WorkspaceBoundary boundary)
    {
        var path = boundary.EnsureInsideWorkspace(".company-agent/policy.json");
        if (!File.Exists(path)) return new ProjectPolicy();
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("Proje policy dosyası 64 KB sınırını aşıyor.");
        return JsonSerializer.Deserialize<ProjectPolicy>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new ProjectPolicy();
    }

    public void EnsureAllowed(string toolName)
    {
        if (BlockedTools.Any(item => string.Equals(item, toolName, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Bu araç proje politikası tarafından engellendi: " + toolName);
    }
}
