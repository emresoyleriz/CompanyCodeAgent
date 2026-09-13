using System.Text.Json;

namespace CompanyCodeAgent.Domain;

public sealed class ProjectPolicy
{
    public List<string> BlockedTools { get; init; } = [];
    public List<string> AllowedCommandPrefixes { get; init; } = [];

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

    public void EnsureCommandAllowed(string command)
    {
        if (AllowedCommandPrefixes.Count == 0) return;
        if (string.IsNullOrWhiteSpace(command)) throw new UnauthorizedAccessException("Boş komut izinli değildir.");
        if (command.IndexOfAny(['\r', '\n', ';', '|', '&']) >= 0)
            throw new UnauthorizedAccessException("Komut allowlist etkin olduğunda shell zincirleme operatörleri kullanılamaz.");
        var trimmed = command.TrimStart();
        var allowed = AllowedCommandPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix)).Any(prefix =>
        {
            var normalized = prefix.Trim();
            return trimmed.StartsWith(normalized, StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == normalized.Length || char.IsWhiteSpace(trimmed[normalized.Length]));
        });
        if (!allowed) throw new UnauthorizedAccessException("Bu komut proje terminal allowlist'inde değil.");
    }
}
