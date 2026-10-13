using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>One place in the source that asks UiText for a word: where, which method, and the key when it is a plain string.</summary>
public sealed record UiTextCall(string File, int Line, string Method, string? Key);

/// <summary>
/// PORT-ONLY test fixture (web surfaces W22, ED-NEW-web-w22-2): finds every call of UiText.Get, Format, FormatWith, Plural and PluralWith in the
/// source of the four product projects, and the key each one names. A key built at run time (anything but a plain string) comes back with a null key.
/// </summary>
public static partial class UiTextCalls
{
    private static readonly string[] Projects = ["Marrow.Core", "Marrow.Services", "Marrow.UI", "Marrow.App"];

    [GeneratedRegex(@"\bUiText\s*\.\s*(Get|Format|FormatWith|Plural|PluralWith)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex Call();

    public static string SourceRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "src"));

    public static IReadOnlyList<UiTextCall> Scan(string? sourceRoot = null)
    {
        var root = sourceRoot ?? SourceRoot();
        var calls = new List<UiTextCall>();
        foreach (var project in Projects)
        {
            var folder = Path.Combine(root, project);
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)) continue;
                if (relative.StartsWith("Marrow.Core/Localisation/", StringComparison.Ordinal)) continue;
                var text = File.ReadAllText(file);
                var codeOnly = WithoutComments(text);
                foreach (Match match in Call().Matches(codeOnly))
                {
                    var method = match.Groups[1].Value;
                    var line = 1 + codeOnly.AsSpan(0, match.Index).Count('\n');
                    calls.Add(new UiTextCall(relative, line, method, KeyAt(codeOnly, match.Index + match.Length, method.EndsWith("With", StringComparison.Ordinal))));
                }
            }
        }
        return calls;
    }

    // The key is the first argument, or the second when the first is a format provider (FormatWith, PluralWith).
    private static string? KeyAt(string code, int start, bool providerFirst)
    {
        var at = start;
        if (providerFirst)
        {
            var comma = TopLevelComma(code, start);
            if (comma < 0) return null;
            at = comma + 1;
        }
        while (at < code.Length && char.IsWhiteSpace(code[at])) at++;
        if (at >= code.Length || code[at] != '"') return null;
        var end = code.IndexOf('"', at + 1);
        if (end < 0) return null;
        var literal = code[(at + 1)..end];
        // A plain key only: a string with an escape or an interpolation is not a key.
        var after = end + 1;
        while (after < code.Length && char.IsWhiteSpace(code[after])) after++;
        return after < code.Length && code[after] is ',' or ')' && !literal.Contains('\\', StringComparison.Ordinal) ? literal : null;
    }

    private static int TopLevelComma(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                i++;
                while (i < code.Length && code[i] != '"') i += code[i] == '\\' ? 2 : 1;
            }
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return -1;
                depth--;
            }
            else if (c == ',' && depth == 0) return i;
        }
        return -1;
    }

    // Blanks line comments and block comments (keeping line numbers) so a mention of UiText.Get in prose is not a call.
    private static string WithoutComments(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                builder.Append(c);
                i++;
                while (i < text.Length && text[i] != '"' && text[i] != '\n')
                {
                    builder.Append(text[i]);
                    if (text[i] == '\\' && i + 1 < text.Length) builder.Append(text[++i]);
                    i++;
                }
                if (i < text.Length) builder.Append(text[i]);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length) builder.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    if (text[i] == '\n') builder.Append('\n');
                    i++;
                }
                i++;
            }
            else builder.Append(c);
        }
        return builder.ToString();
    }
}
