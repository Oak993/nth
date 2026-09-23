using Marrow.Core.Port;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Marrow.Core.Support;

// Core/Support/Text.swift:3-9. Assembly metadata replaces the bundle's Info dictionary;
// the packaged Debug/Release build channel replaces the macOS bundle identifier.
public sealed record AppInfo
{
#if DEBUG
    private const bool DevelopmentBuild = true;
#else
    private const bool DevelopmentBuild = false;
#endif
    public static Ambient<AppInfo> Running { get; } = new(() => FromAssembly(Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly, DevelopmentBuild));
    public static AppInfo Current => Running.Current;

    public string Name { get; }
    public string Version { get; }
    public string Build { get; }
    public bool IsDevelopment { get; }
    public string StateDirectoryName => IsDevelopment ? ".droppy-code-dev" : ".droppy-code";

    private AppInfo(string name, string version, string build, bool isDevelopment) =>
        (Name, Version, Build, IsDevelopment) = (name, version, build, isDevelopment);

    public static AppInfo Read(IReadOnlyDictionary<string, object?> metadata, bool isDevelopment)
    {
        string Value(string key, string fallback) =>
            metadata.TryGetValue(key, out var value) && value is string text ? text : fallback;
        return new(Value("CFBundleDisplayName", "Droppy Code"),
            Value("CFBundleShortVersionString", "1.0.0"), Value("CFBundleVersion", "1"), isDevelopment);
    }

    public static AppInfo FromAssembly(Assembly assembly, bool isDevelopment)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            metadata[attribute.Key] = attribute.Value;
        return Read(metadata, isDevelopment);
    }
}

// Core/Support/Text.swift:11-75.
public static class TextCleanup
{
    public static string StripANSI(string text)
    {
        // The exact Swift pattern is \x1B\[[0-9;?]*[ -/]*[@-~]. Swift Regex
        // compares entire NFC graphemes; .NET Regex would consume a partial one.
        StringBuilder? output = null;
        var copiedThrough = 0;
        var search = 0;
        while (search < text.Length)
        {
            var escape = text.IndexOf('\u001B', search);
            if (escape < 0) break;
            search = escape + 1;
            if (!ReadPatternCharacter(text, search, out var character, out var next) || character != '[') continue;
            var position = next;
            while (ReadPatternCharacter(text, position, out character, out next) &&
                   (character is >= '0' and <= '9' or ';' or '?')) position = next;
            while (ReadPatternCharacter(text, position, out character, out next) &&
                   character is >= ' ' and <= '/') position = next;
            if (!ReadPatternCharacter(text, position, out character, out next) || character is < '@' or > '~') continue;
            (output ??= new StringBuilder(text.Length)).Append(text, copiedThrough, escape - copiedThrough);
            copiedThrough = next;
            search = next;
        }
        return output is null ? text : output.Append(text, copiedThrough, text.Length - copiedThrough).ToString();
    }

    private static bool ReadPatternCharacter(string text, int index, out char character, out int next)
    {
        character = default;
        next = index;
        if (index >= text.Length) return false;
        var element = StringInfo.GetNextTextElement(text, index);
        next = index + element.Length;
        var normalized = element.Normalize(NormalizationForm.FormC);
        if (normalized.Length != 1 || normalized[0] > 0x7F) return false;
        character = normalized[0];
        return true;
    }

    public static string? LastLines(string text, int count = 4)
    {
        var lines = StripANSI(text).Split(['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'])
            .Select(line => FoundationText.Trim(line)).Where(line => line.Length != 0).ToArray();
        if (lines.Length == 0) return null;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return string.Join("\n", lines.AsSpan(Math.Max(0, lines.Length - count)).ToArray());
    }

    public static string SingleLine(string text, int limit = 120)
    {
        var start = 0;
        while (start < text.Length && FoundationText.IsNewline(text[start])) start++;
        var end = start;
        while (end < text.Length && !FoundationText.IsNewline(text[end])) end++;
        var line = FoundationText.Trim(text[start..end]);
        var characters = StringInfo.ParseCombiningCharacters(line);
        if (characters.Length <= limit) return line;
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return line[..characters[limit - 1]] + "…";
    }

    public static string WithoutEmDashes(string text)
    {
        if (!MayContainEmDash(Encoding.UTF8.GetBytes(text))) return text;
        var characters = StringInfo.GetTextElementEnumerator(text);
        var found = false;
        while (characters.MoveNext())
            if (IsEmDash(characters.GetTextElement())) { found = true; break; }
        if (!found) return text;
        var output = new StringBuilder(text.Length);
        characters.Reset();
        while (characters.MoveNext())
        {
            var character = characters.GetTextElement();
            output.Append(IsEmDash(character) ? "-" : character);
        }
        return output.ToString();
    }

    // Kept callable for the packet's truncated-byte fixtures; String itself is valid text.
    public static bool MayContainEmDash(ReadOnlySpan<byte> utf8)
    {
        for (var i = 0; i < utf8.Length; i++)
        {
            if (utf8[i] != 0xE2 || ++i == utf8.Length) continue;
            var second = utf8[i];
            if (second == 0x80)
            {
                if (++i == utf8.Length) return false;
                if (utf8[i] is 0x94 or 0x95) return true;
            }
            else if (second == 0xB8)
            {
                if (++i == utf8.Length) return false;
                if (utf8[i] is 0xBA or 0xBB) return true;
            }
        }
        return false;
    }

    private static bool IsEmDash(string character) => character is "—" or "―" or "⸺" or "⸻";
}

// FileEdit.Stats is a plain value; the nested Swift name is flattened at the Core boundary.
public sealed record FileEditStats(string? Diff, int Additions, int Deletions);

public static class SimpleDiff
{
    public const int DiffLimit = 200_000;

    public static FileEditStats Unified(string oldText, string newText)
    {
        var oldLines = oldText.Length == 0 ? [] : oldText.Split('\n');
        var newLines = newText.Length == 0 ? [] : newText.Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && Equal(oldLines[prefix], newLines[prefix]))
            prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               Equal(oldLines[oldLines.Length - 1 - suffix], newLines[newLines.Length - 1 - suffix]))
            suffix++;
        var removed = oldLines[prefix..(oldLines.Length - suffix)];
        var added = newLines[prefix..(newLines.Length - suffix)];
        var leading = oldLines[Math.Max(0, prefix - 3)..prefix];
        var trailingEnd = Math.Min(oldLines.Length, oldLines.Length - suffix + 3);
        var trailing = oldLines[(oldLines.Length - suffix)..trailingEnd];
        // Preserve the upstream cost quirk: split, scan and slice BEFORE checking the input sum.
        if ((long)Encoding.UTF8.GetByteCount(oldText) + Encoding.UTF8.GetByteCount(newText) > DiffLimit)
            return new(null, added.Length, removed.Length);
        var oldStart = prefix - leading.Length + 1;
        var lines = new List<string>
        {
            FormattableString.Invariant($"@@ -{oldStart},{leading.Length + removed.Length + trailing.Length} +{oldStart},{leading.Length + added.Length + trailing.Length} @@")
        };
        lines.AddRange(leading.Select(line => " " + line));
        lines.AddRange(removed.Select(line => "-" + line));
        lines.AddRange(added.Select(line => "+" + line));
        lines.AddRange(trailing.Select(line => " " + line));
        return new(string.Join("\n", lines), added.Length, removed.Length);
    }

    private static bool Equal(string left, string right) => left == right ||
        left.Normalize(NormalizationForm.FormC) == right.Normalize(NormalizationForm.FormC);
}

public static class StringExtensions
{
    // Text.swift:127-129; whitespace-only strings are present values.
    public static string? NilIfEmpty(this string text) => text.Length == 0 ? null : text;

    // Text.swift:132 var trimmed. SKELETON (cycle c4): stub until the Text.swift unit ports it.
    public static string Trimmed(this string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Support/Text.swift:132");
}
