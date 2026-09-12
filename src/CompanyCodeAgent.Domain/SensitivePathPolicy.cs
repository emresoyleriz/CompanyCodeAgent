namespace CompanyCodeAgent.Domain;

public static class SensitivePathPolicy
{
    private static readonly string[] SensitiveNames = [".env", ".env.local", ".env.production", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "credentials.json", "secrets.json", "appsettings.secrets.json"];
    private static readonly string[] SensitiveExtensions = [".pem", ".key", ".pfx", ".p12", ".snk", ".kdbx"];

    public static void EnsureAllowed(string path)
    {
        if (IsSensitive(path)) throw new UnauthorizedAccessException("Hassas dosya agent araçları için kapalı: " + Path.GetFileName(path));
    }

    public static bool IsSensitive(string path)
    {
        var name = Path.GetFileName(path);
        return SensitiveNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || SensitiveExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }
}
