using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CompanyCodeAgent.VisualStudio;

internal static class VisualStudioDiffPreview
{
    public static void TryOpen(string workspacePath, string toolName, System.Collections.Generic.IReadOnlyDictionary<string, string> arguments)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!arguments.TryGetValue("path", out var relativePath) || string.IsNullOrWhiteSpace(relativePath)) return;
        var root = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Önizleme hedefi workspace dışında.");

        var before = File.Exists(target) ? File.ReadAllText(target) : string.Empty;
        var after = toolName.Equals("writefile", StringComparison.OrdinalIgnoreCase)
            ? Get(arguments, "content")
            : toolName.Equals("applypatch", StringComparison.OrdinalIgnoreCase)
                ? ReplaceExactly(before, Get(arguments, "expected"), Get(arguments, "replacement"))
                : toolName.Equals("deletefile", StringComparison.OrdinalIgnoreCase) ? string.Empty : before;

        var directory = Path.Combine(Path.GetTempPath(), "CompanyCodeAgent", "diffs");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var left = Path.Combine(directory, id + ".before");
        var right = Path.Combine(directory, id + ".after");
        File.WriteAllText(left, before);
        File.WriteAllText(right, after);
        var service = Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;
        service?.OpenComparisonWindow2(left, right, $"Company Code Agent: {relativePath}", "Company Code Agent", "Mevcut", "Önerilen", null, null, 0);
    }

    private static string Get(System.Collections.Generic.IReadOnlyDictionary<string, string> values, string name) => values.TryGetValue(name, out var value) ? value : string.Empty;

    private static string ReplaceExactly(string source, string expected, string replacement)
    {
        var first = source.IndexOf(expected, StringComparison.Ordinal);
        return first < 0 ? source : source.IndexOf(expected, first + expected.Length, StringComparison.Ordinal) >= 0 ? source : source.Substring(0, first) + replacement + source.Substring(first + expected.Length);
    }
}
