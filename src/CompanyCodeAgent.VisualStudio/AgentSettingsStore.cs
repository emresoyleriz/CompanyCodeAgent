using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CompanyCodeAgent.VisualStudio;

internal sealed class AgentSettings
{
    public string Endpoint { get; set; } = "http://localhost:8000/";
    public string Model { get; set; } = string.Empty;
    public bool UseSeparateModeModels { get; set; }
    public string PlanModel { get; set; } = string.Empty;
    public string ActModel { get; set; } = string.Empty;
    public int MaxAgentSteps { get; set; } = 5;
    public int TimeoutMinutes { get; set; } = 10;
    public string ProtectedApiKey { get; set; } = string.Empty;

    public string GetApiKey()
    {
        if (string.IsNullOrWhiteSpace(ProtectedApiKey)) return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedApiKey), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException) { return string.Empty; }
        catch (FormatException) { return string.Empty; }
    }

    public void SetApiKey(string value)
    {
        ProtectedApiKey = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
}

internal static class AgentSettingsStore
{
    private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CompanyCodeAgent", "settings.json");

    public static AgentSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AgentSettings();
            return Serializer.Deserialize<AgentSettings>(File.ReadAllText(FilePath)) ?? new AgentSettings();
        }
        catch { return new AgentSettings(); }
    }

    public static void Save(AgentSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath, Serializer.Serialize(settings));
    }
}
