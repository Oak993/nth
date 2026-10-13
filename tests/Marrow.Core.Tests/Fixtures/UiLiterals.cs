using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>An English-looking string literal found in a source file: where, and its text as written between the quotes.</summary>
public sealed record UiLiteral(string File, int Line, string Text);

/// <summary>
/// PORT-ONLY test fixture (web surfaces W22, ED-NEW-web-w22-3): finds the English-looking string literals left in the web surfaces' user-interface
/// source, so a new word added in code instead of the resource files is noticed. A literal counts when it has a word of two letters or more and a space
/// or a capital first letter. Comment lines, [Trait] and LogicalName lines, and the key of a UiText call are skipped. What stays on purpose
/// (model text, protocol, CSS, ids) is listed in tests/fixtures/web-surfaces/strings/stay-in-code.txt, one "file TAB text" per line.
/// </summary>
public static partial class UiLiterals
{
    [GeneratedRegex("(?<![\\w@$])\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Literal();

    [GeneratedRegex(@"\bUiText\s*\.\s*(?:Get|Format|FormatWith|Plural|PluralWith)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex UiTextCall();

    [GeneratedRegex("[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    public static string StringsFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "fixtures", "web-surfaces", "strings"));

    /// <summary>The files to scan, from scan-set.txt: a line ending in "/" is a folder (every .cs under it), any other line is one file; paths are under src/.</summary>
    public static IReadOnlyList<string> ScanSet(string? sourceRoot = null)
    {
        var root = sourceRoot ?? UiTextCalls.SourceRoot();
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(Path.Combine(StringsFolder(), "scan-set.txt")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.EndsWith('/'))
            {
                var folder = Path.Combine(root, line.TrimEnd('/'));
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (!relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal)) files.Add(relative);
                }
            }
            else files.Add(line);
        }
        return [.. files];
    }

    public static IReadOnlyList<UiLiteral> Scan(IEnumerable<string> files, string? sourceRoot = null)
    {
        var root = sourceRoot ?? UiTextCalls.SourceRoot();
        var found = new List<UiLiteral>();
        foreach (var relative in files)
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path)) continue;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("[Trait", StringComparison.Ordinal)
                    || trimmed.Contains("LogicalName", StringComparison.Ordinal)) continue;
                var masked = line;
                foreach (Match call in UiTextCall().Matches(line))
                {
                    var key = Literal().Match(line, call.Index + call.Length);
                    if (key.Success) masked = masked[..key.Index] + new string(' ', key.Length) + masked[(key.Index + key.Length)..];
                }
                foreach (Match match in Literal().Matches(masked))
                {
                    var text = match.Groups[1].Value;
                    if (Word().IsMatch(text) && (text.Contains(' ', StringComparison.Ordinal) || char.IsUpper(text[0])))
                        found.Add(new UiLiteral(relative, i + 1, text));
                }
            }
        }
        return found;
    }

    /// <summary>The literals that stay on purpose, from stay-in-code.txt: (file, text) pairs.</summary>
    public static IReadOnlySet<(string File, string Text)> Allowed()
    {
        var allowed = new HashSet<(string File, string Text)>();
        var path = Path.Combine(StringsFolder(), "stay-in-code.txt");
        if (!File.Exists(path)) return allowed;
        foreach (var raw in File.ReadAllLines(path))
        {
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            var tab = raw.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0) allowed.Add((raw[..tab], raw[(tab + 1)..]));
        }
        return allowed;
    }
}
