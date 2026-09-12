using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Text.RegularExpressions;

namespace CompanyCodeAgent.VisualStudio;

internal static class VisualStudioContextProvider
{
    public static string GetWorkspacePath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        var solution = dte?.Solution?.FullName;
        if (string.IsNullOrWhiteSpace(solution)) throw new InvalidOperationException("Açık bir solution bulunamadı.");
        return System.IO.Path.GetDirectoryName(solution)!;
    }

    public static string LoadProjectRules()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var root = GetWorkspacePath();
        var rules = new List<string>();
        foreach (var relativePath in new[] { "AGENTS.md", ".company-agent\\rules.md", ".github\\copilot-instructions.md", ".clinerules", "CLAUDE.md", "GEMINI.md", "REVIEW.md" })
        {
            var path = System.IO.Path.Combine(root, relativePath);
            if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length > 64 * 1024) continue;
            rules.Add(System.IO.File.ReadAllText(path));
        }
        return rules.Count == 0 ? string.Empty : Redact(string.Join("\n\n", rules));
    }

    public static string Capture()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        if (dte?.ActiveDocument == null) return "Visual Studio bağlamı: Açık dosya yok.";

        var document = dte.ActiveDocument;
        var selection = document.Selection as TextSelection;
        var selectedText = selection?.Text;
        if (selectedText?.Length > 12000) selectedText = selectedText.Substring(0, 12000) + "\n[seçim kısaltıldı]";

        return $"Visual Studio bağlamı:\nSolution: {dte.Solution?.FullName ?? "bilinmiyor"}\nAktif dosya: {document.FullName}\nSeçili kod:\n{(string.IsNullOrWhiteSpace(selectedText) ? "yok" : Redact(selectedText))}\n\nTanılar:\n{CaptureDiagnostics(dte)}";
    }

    public static string ExpandPromptOrSkill(string input)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/", StringComparison.Ordinal)) return input;
        var firstSpace = input.IndexOfAny(new[] { ' ', '\r', '\n', '\t' });
        var name = (firstSpace < 0 ? input.Substring(1) : input.Substring(1, firstSpace - 1)).Trim();
        var remainder = firstSpace < 0 ? string.Empty : input.Substring(firstSpace).Trim();
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[a-zA-Z0-9_-]+$")) return input;
        if (string.Equals(name, "deep-planning", StringComparison.OrdinalIgnoreCase))
            return "Bu karmaşık görevi derin planlama ile ele al. Önce kod tabanını salt-okunur araçlarla sistematik keşfet; etkilenen dosyaları, bağımlılıkları, riskleri, kabul kriterlerini ve test planını çıkar. Ardından numaralı görevler oluştur. " + remainder;

        var root = GetWorkspacePath();
        var candidates = new[]
        {
            Path.Combine(root, ".company-agent", "prompts", name + ".md"),
            Path.Combine(root, ".github", "prompts", name + ".prompt.md"),
            Path.Combine(root, ".company-agent", "skills", name, "SKILL.md")
        };
        var file = candidates.FirstOrDefault(path => File.Exists(path) && new FileInfo(path).Length <= 64 * 1024);
        if (file == null) return input;
        var instruction = Redact(File.ReadAllText(file));
        return "Çağrılan proje prompt/skill dosyası: " + Path.GetFileName(file) + "\n\n" + instruction + (string.IsNullOrWhiteSpace(remainder) ? string.Empty : "\n\nKullanıcı isteği:\n" + remainder);
    }

    public static string ExpandMentions(string input)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = input;
        if (result.IndexOf("@problems", StringComparison.OrdinalIgnoreCase) >= 0)
            result = Regex.Replace(result, "@problems\\b", "Tanılar:\n" + CaptureDiagnostics((DTE)Package.GetGlobalService(typeof(DTE))), RegexOptions.IgnoreCase);

        result = Regex.Replace(result, "@file:([^\\s]+)", match => ReadMentionedFile(match.Groups[1].Value), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, "@folder:([^\\s]+)", match => ListMentionedFolder(match.Groups[1].Value), RegexOptions.IgnoreCase);
        return result;
    }

    private static string ReadMentionedFile(string relativePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var root = GetWorkspacePath();
            var fullPath = EnsureWorkspacePath(root, relativePath);
            if (IsSensitive(fullPath)) return "[Hassas dosya bağlama eklenmedi: " + relativePath + "]";
            if (!File.Exists(fullPath)) return "[Dosya bulunamadı: " + relativePath + "]";
            if (new FileInfo(fullPath).Length > 128 * 1024) return "[Dosya bağlam sınırını aşıyor: " + relativePath + "]";
            return "Dosya bağlamı (" + relativePath + "):\n" + Redact(File.ReadAllText(fullPath));
        }
        catch (Exception ex) { return "[Dosya bağlamı alınamadı: " + ex.Message + "]"; }
    }

    private static string ListMentionedFolder(string relativePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var root = GetWorkspacePath();
            var folder = EnsureWorkspacePath(root, relativePath);
            if (!Directory.Exists(folder)) return "[Klasör bulunamadı: " + relativePath + "]";
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(path => !IsSensitive(path) && path.IndexOf("\\.git\\", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) < 0)
                .Take(50).Select(path => path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return "Klasör bağlamı (" + relativePath + "):\n" + string.Join("\n", files);
        }
        catch (Exception ex) { return "[Klasör bağlamı alınamadı: " + ex.Message + "]"; }
    }

    private static string EnsureWorkspacePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Workspace dışı yol.");
        return fullPath;
    }

    private static bool IsSensitive(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase) || name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 || new[] { ".pem", ".key", ".pfx", ".p12", ".snk" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static string CaptureDiagnostics(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            dynamic dte2 = dte;
            dynamic errors = dte2.ToolWindows.ErrorList.ErrorItems;
            var items = new List<string>();
            for (var index = 1; index <= errors.Count && items.Count < 50; index++)
            {
                dynamic item = errors.Item(index);
                items.Add($"{item.ErrorLevel}: {item.FileName}({item.Line},{item.Column}) {Redact(item.Description)}");
            }
            return items.Count == 0 ? "Error List boş." : string.Join("\n", items);
        }
        catch (Exception ex) { return "Tanılar alınamadı: " + ex.Message; }
    }

    private static string Redact(string value)
    {
        return Regex.Replace(value, "(?im)\\b(api[_-]?key|access[_-]?token|password|secret)\\s*[:=]\\s*([^\\s,;\\\"']+|\\\"[^\\\"]*\\\")", "$1=[REDACTED]");
    }
}
