using Marrow.Core.Port;
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
        // The exact Swift pattern is \x1B\[[0-9;?]*[ -/]*[@-~]. Swift Regex compares entire NFC
        // graphemes; a naive .NET Regex would consume a partial one. Walked over SwiftString.Characters
        // (R-STR-1) rather than a second hand-rolled grapheme reader.
        var characters = SwiftString.Characters(text).ToArray();
        if (characters.Length == 0) return text;
        var starts = new int[characters.Length + 1];
        for (var i = 0; i < characters.Length; i++) starts[i + 1] = starts[i] + characters[i].Length;

        StringBuilder? output = null;
        var copiedThrough = 0; // a character index into `characters`/`starts`, not a UTF-16 offset
        var search = 0;
        while (search < characters.Length)
        {
            if (!IsAsciiPatternCharacter(characters[search], out var character) || character != '\u001B')
            { search++; continue; }
            var position = search + 1;
            if (!TryAsciiPatternCharacter(characters, position, out character) || character != '[') { search++; continue; }
            position++;
            while (TryAsciiPatternCharacter(characters, position, out character) &&
                   (character is >= '0' and <= '9' or ';' or '?')) position++;
            while (TryAsciiPatternCharacter(characters, position, out character) &&
                   character is >= ' ' and <= '/') position++;
            if (!TryAsciiPatternCharacter(characters, position, out character) || character is < '@' or > '~')
            { search++; continue; }
            position++;
            (output ??= new StringBuilder(text.Length)).Append(text, starts[copiedThrough], starts[search] - starts[copiedThrough]);
            copiedThrough = position;
            search = position;
        }
        return output is null ? text : output.Append(text, starts[copiedThrough], text.Length - starts[copiedThrough]).ToString();
    }

    private static bool TryAsciiPatternCharacter(string[] characters, int index, out char character)
    {
        character = default;
        return index < characters.Length && IsAsciiPatternCharacter(characters[index], out character);
    }

    private static bool IsAsciiPatternCharacter(string element, out char character)
    {
        character = default;
        var normalized = SwiftString.Nfc(element);
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
        // Character count and prefix are Swift grapheme semantics (R-STR-1): reuse SwiftString
        // rather than a second StringInfo-based counter.
        if (SwiftString.Count(line) <= limit) return line;
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return SwiftString.Prefix(line, limit - 1) + "…";
    }

    public static string WithoutEmDashes(string text)
    {
        if (!MayContainEmDash(Encoding.UTF8.GetBytes(text))) return text;
        // Grapheme walk via SwiftString.Characters (R-STR-1), not a second GetTextElementEnumerator use.
        var found = false;
        foreach (var character in SwiftString.Characters(text))
            if (IsEmDash(character)) { found = true; break; }
        if (!found) return text;
        var output = new StringBuilder(text.Length);
        foreach (var character in SwiftString.Characters(text))
            output.Append(IsEmDash(character) ? "-" : character);
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

    // Swift line == compares Characters by canonical equivalence (R-STR-2): call SwiftString.Equal
    // rather than a second NFC-compare here.
    private static bool Equal(string left, string right) => SwiftString.Equal(left, right);
}

public static class StringExtensions
{
    // Text.swift:127-129; whitespace-only strings are present values.
    public static string? NilIfEmpty(this string text) => text.Length == 0 ? null : text;

    // Text.swift:130-132. var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }.
    public static string Trimmed(this string text) => FoundationText.Trim(text, includeNewlines: true);
}
