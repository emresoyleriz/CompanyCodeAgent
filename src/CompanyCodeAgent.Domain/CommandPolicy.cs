namespace CompanyCodeAgent.Domain;

public sealed class CommandPolicy
{
    private static readonly string[] ForbiddenFragments =
    [
        "rm -rf", "remove-item -recurse", "format ", "diskpart", "reg delete", "shutdown", "restart-computer",
        "del /s", "rmdir /s", "mkfs", ":(){", "curl | sh", "wget | sh"
    ];

    public void EnsureAllowed(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Komut boş olamaz.", nameof(command));
        var normalized = command.Trim().ToLowerInvariant();
        if (ForbiddenFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("Komut güvenlik politikası tarafından engellendi.");
    }
}
