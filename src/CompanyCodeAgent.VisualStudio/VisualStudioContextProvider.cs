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
        if (TryGetWorkspacePath(out var workspacePath)) return workspacePath;
        throw new InvalidOperationException("Açık bir solution veya proje dosyası bulunamadı. Bir solution açın ya da önce bir dosya açın.");
    }

    public static bool TryGetWorkspacePath(out string workspacePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        var solution = dte?.Solution?.FullName;
        var solutionDirectory = string.IsNullOrWhiteSpace(solution) ? null : Path.GetDirectoryName(solution);
        if (!string.IsNullOrWhiteSpace(solutionDirectory) && Directory.Exists(solutionDirectory))
        {
            workspacePath = solutionDirectory;
            return true;
        }

        var activeDocument = dte?.ActiveDocument?.FullName;
        var documentDirectory = string.IsNullOrWhiteSpace(activeDocument) ? null : Path.GetDirectoryName(activeDocument);
        if (!string.IsNullOrWhiteSpace(documentDirectory) && Directory.Exists(documentDirectory))
        {
            workspacePath = documentDirectory;
            return true;
        }

        workspacePath = string.Empty;
        return false;
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
        var activePath = (Package.GetGlobalService(typeof(DTE)) as DTE)?.ActiveDocument?.FullName;
        var ruleDirectory = Path.Combine(root, ".company-agent", "rules");
        if (Directory.Exists(ruleDirectory) && !string.IsNullOrWhiteSpace(activePath))
        {
            var extensionRule = Path.GetExtension(activePath).TrimStart('.') + ".md";
            var fileRule = Path.GetFileName(activePath) + ".md";
            foreach (var name in new[] { "all.md", extensionRule, fileRule }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(ruleDirectory, name);
                if (File.Exists(path) && new FileInfo(path).Length <= 64 * 1024) rules.Add(File.ReadAllText(path));
            }
        }
        rules.AddRange(LoadPathPatternRules(root, activePath));
        return rules.Count == 0 ? string.Empty : Redact(string.Join("\n\n", rules));
    }

    public static string Capture()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        if (dte == null) return "Visual Studio bağlamı kullanılamıyor.";
        if (dte.ActiveDocument == null)
            return $"Visual Studio bağlamı:\nSolution: {dte.Solution?.FullName ?? "bilinmiyor"}\nAktif proje: belirlenemedi\nSolution Explorer seçimi:\n{CaptureSelectedSolutionItems(dte)}\nAktif dosya: yok\nAçık belgeler:\n{CaptureOpenDocuments(dte)}\nSeçili kod:\nyok\n\nTanılar:\n{CaptureDiagnostics(dte)}\n\nSon Build çıktısı:\n{CaptureOutputPane(dte, "Build")}\n\nSon Test çıktısı:\n{CaptureOutputPane(dte, "Tests")}";

        var document = dte.ActiveDocument;
        var selection = document.Selection as TextSelection;
        var selectedText = selection?.Text;
        if (selectedText?.Length > 12000) selectedText = selectedText.Substring(0, 12000) + "\n[seçim kısaltıldı]";
        var activeProject = CaptureActiveProject(dte, document.FullName);

        return $"Visual Studio bağlamı:\nSolution: {dte.Solution?.FullName ?? "bilinmiyor"}\nAktif proje: {activeProject}\nSolution Explorer seçimi:\n{CaptureSelectedSolutionItems(dte)}\nAktif dosya: {document.FullName}\nAçık belgeler:\n{CaptureOpenDocuments(dte)}\nSeçili kod:\n{(string.IsNullOrWhiteSpace(selectedText) ? "yok" : Redact(selectedText))}\n\nTanılar:\n{CaptureDiagnostics(dte)}\n\nSon Build çıktısı:\n{CaptureOutputPane(dte, "Build")}\n\nSon Test çıktısı:\n{CaptureOutputPane(dte, "Tests")}";
    }

    public static string GetDiagnostics()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        return dte == null ? "Visual Studio hata listesi kullanılamıyor." : CaptureDiagnostics(dte);
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

    public static string ExtractImageMention(string input, out string dataUri)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        dataUri = null;
        var match = Regex.Match(input ?? string.Empty, "@image:([^\\s]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return input;
        var relativePath = match.Groups[1].Value;
        try
        {
            var fullPath = EnsureWorkspacePath(GetWorkspacePath(), relativePath);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var mediaType = extension == ".png" ? "image/png" : extension is ".jpg" or ".jpeg" ? "image/jpeg" : extension == ".gif" ? "image/gif" : extension == ".webp" ? "image/webp" : null;
            if (mediaType == null) throw new InvalidOperationException("Desteklenmeyen görsel türü.");
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new FileNotFoundException("Görsel bulunamadı.", fullPath);
            if (info.Length > 5 * 1024 * 1024) throw new InvalidOperationException("Görsel 5 MB sınırını aşıyor.");
            dataUri = "data:" + mediaType + ";base64," + Convert.ToBase64String(File.ReadAllBytes(fullPath));
            return input.Remove(match.Index, match.Length).Trim();
        }
        catch (Exception ex) { return input + "\n[Görsel eklenemedi: " + ex.Message + "]"; }
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

    private static string CaptureOpenDocuments(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var solutionPath = dte.Solution?.FullName;
            var workspace = string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
            var workspacePrefix = string.IsNullOrWhiteSpace(workspace) ? null : Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var documents = new List<string>();
            foreach (Document openDocument in dte.Documents)
            {
                if (documents.Count >= 20) break;
                var fullName = openDocument.FullName;
                if (!string.IsNullOrWhiteSpace(fullName) && workspacePrefix != null && Path.GetFullPath(fullName).StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase)) documents.Add(fullName);
            }
            return documents.Count == 0 ? "yok" : string.Join("\n", documents);
        }
        catch { return "alınamadı"; }
    }

    private static string CaptureOutputPane(DTE dte, string paneName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var outputWindow = dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput).Object as OutputWindow;
            if (outputWindow == null) return "Output penceresi kullanılamıyor.";
            foreach (OutputWindowPane pane in outputWindow.OutputWindowPanes)
            {
                if (!string.Equals(pane.Name, paneName, StringComparison.OrdinalIgnoreCase)) continue;
                var document = pane.TextDocument;
                if (document == null) return "Henüz çıktı yok.";
                var text = document.StartPoint.CreateEditPoint().GetText(document.EndPoint) ?? string.Empty;
                if (text.Length > 8000) text = "[Önceki çıktı kısaltıldı]\n" + text.Substring(text.Length - 8000);
                return string.IsNullOrWhiteSpace(text) ? "Henüz çıktı yok." : Redact(text);
            }
            return "Output panelinde '" + paneName + "' bölmesi yok.";
        }
        catch (Exception ex) { return "Çıktı alınamadı: " + ex.Message; }
    }

    private static string CaptureActiveProject(DTE dte, string documentPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var item = dte.Solution?.FindProjectItem(documentPath);
            var project = item?.ContainingProject;
            return project == null ? "belirlenemedi" : project.Name + " (" + project.FullName + ")";
        }
        catch { return "belirlenemedi"; }
    }

    private static string CaptureSelectedSolutionItems(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var selected = new List<string>();
            foreach (SelectedItem item in dte.SelectedItems)
            {
                if (selected.Count >= 10) break;
                var projectItem = item.ProjectItem;
                if (projectItem != null) selected.Add(projectItem.Name + (string.IsNullOrWhiteSpace(projectItem.FileNames[1]) ? string.Empty : " (" + projectItem.FileNames[1] + ")"));
                else if (item.Project != null) selected.Add(item.Project.Name + " (" + item.Project.FullName + ")");
            }
            return selected.Count == 0 ? "yok" : string.Join("\n", selected);
        }
        catch { return "alınamadı"; }
    }

    private static IEnumerable<string> LoadPathPatternRules(string root, string activePath)
    {
        if (string.IsNullOrWhiteSpace(activePath)) yield break;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedActive = Path.GetFullPath(activePath);
        if (!normalizedActive.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) yield break;
        var mappingPath = Path.Combine(root, ".company-agent", "rules.paths");
        if (!File.Exists(mappingPath) || new FileInfo(mappingPath).Length > 64 * 1024) yield break;
        var relativeActive = normalizedActive.Substring(normalizedRoot.Length).Replace('\\', '/');
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(mappingPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1) continue;
            var pattern = line.Substring(0, separator).Trim().Replace('\\', '/');
            var relativeRule = line.Substring(separator + 1).Trim().Replace('/', Path.DirectorySeparatorChar);
            if (!GlobMatches(pattern, relativeActive) || !used.Add(relativeRule)) continue;
            var rulePath = Path.GetFullPath(Path.Combine(normalizedRoot, relativeRule));
            if (!rulePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(rulePath) || new FileInfo(rulePath).Length > 64 * 1024) continue;
            yield return File.ReadAllText(rulePath);
        }
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase);
    }

    private static string Redact(string value)
    {
        return Regex.Replace(value, "(?im)\\b(api[_-]?key|access[_-]?token|password|secret)\\s*[:=]\\s*([^\\s,;\\\"']+|\\\"[^\\\"]*\\\")", "$1=[REDACTED]");
    }
}
