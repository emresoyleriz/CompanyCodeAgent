using System.Text.RegularExpressions;

namespace CompanyCodeAgent.Domain;

public static partial class SecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var result = KeyValuePattern().Replace(value, "$1=[REDACTED]");
        result = ConnectionStringPattern().Replace(result, "$1=[REDACTED]");
        return result;
    }

    [GeneratedRegex("(?im)\\b(api[_-]?key|access[_-]?token|refresh[_-]?token|password|secret)\\s*[:=]\\s*([^\\s,;\\\"']+|\\\"[^\\\"]*\\\")")]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex("(?im)\\b(connectionstring|server|uid|user id)\\s*=\\s*[^;\\r\\n]+")]
    private static partial Regex ConnectionStringPattern();
}
