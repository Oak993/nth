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
    // Text.swift:6. DEVIATION OD-0012 F1 (ED scope: the state folder is user-visible): ".marrow" / ".marrow-dev";
    // OD-0135 renames it ".nth" / ".nth-dev" (LegacyDataCopy copies the old folder once).
    public string StateDirectoryName => StateDirectoryNameFor(IsDevelopment);

    /// <summary>The home state folder's name for a build channel (Storage reads it for the worktrees and patches).</summary>
    public static string StateDirectoryNameFor(bool isDevelopment) => isDevelopment ? ".nth-dev" : ".nth";

    /// <summary>The state folder's name before OD-0135, read only by the one-time copy (LegacyDataCopy).</summary>
    public static string LegacyStateDirectoryNameFor(bool isDevelopment) => isDevelopment ? ".marrow-dev" : ".marrow";

    /// <summary>
    /// The data folder's name under %LOCALAPPDATA% (OD-0135; "Marrow Code" before it, OD-0008 C7): a fixed literal rather
    /// than <see cref="Name"/>, the same for both build channels. Never the owner's separate Electron app's "Marrow" folder,
    /// and never the installer's package folder (Velopack's %LOCALAPPDATA%\NthApp, which uninstall deletes whole).
    /// </summary>
    public const string DataFolderName = "nth";

    /// <summary>The data folder's name before OD-0135, read only by the one-time copy (LegacyDataCopy).</summary>
    public const string LegacyDataFolderName = "Marrow Code";

    /// <summary>
    /// The display name a build channel had before OD-0135 ("Marrow" / "Marrow Dev"), which was also the Credential Manager
    /// service of the provider keys and MCP values: read only to carry an item over to the new name once.
    /// </summary>
    public static string LegacyNameFor(bool isDevelopment) => isDevelopment ? "Marrow Dev" : "Marrow";

    private AppInfo(string name, string version, string build, bool isDevelopment) =>
        (Name, Version, Build, IsDevelopment) = (name, version, build, isDevelopment);

    public static AppInfo Read(IReadOnlyDictionary<string, object?> metadata, bool isDevelopment)
    {
        string Value(string key, string fallback) =>
            metadata.TryGetValue(key, out var value) && value is string text ? text : fallback;
        return new(Value("CFBundleDisplayName", "nth"), // OD-0135: the product's own name, not upstream's "Droppy Code"
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
        // ED-NEW-lean-3: every match starts with an ESC grapheme, and NFC never makes an ESC out of anything else, so text without
        // U+001B has nothing to strip and is returned as is, without the per-grapheme walk below (most CLI output has no escape).
        if (text.AsSpan().IndexOf('\u001B') < 0) return text;
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
        if (count < 0) throw SwiftTraps.Fatal("Can't take a suffix of negative length from a collection"); // :24 suffix(count) precondition
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
        if (limit < 1) throw SwiftTraps.Fatal("Can't take a prefix of negative length from a collection"); // :29 prefix(limit - 1) precondition
        return SwiftString.Prefix(line, limit - 1) + "…";
    }

    public static string WithoutEmDashes(string text)
    {
        // ED-NEW-lean-3: the byte prescan below is true exactly when one of the four long dashes is in the text, so a text without
        // any of them is returned as is before the UTF-8 copy of the whole text (almost every streamed delta).
        if (text.AsSpan().IndexOfAny(LongDashes) < 0) return text;
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

    // U+2014, U+2015, U+2E3A, U+2E3B: the characters IsEmDash matches, as UTF-16 code units.
    private const string LongDashes = "—―⸺⸻";

    private static bool IsEmDash(string character) => character is "—" or "―" or "⸺" or "⸻";
}

// FileEdit.Stats is a plain value; the nested Swift name is flattened at the Core boundary.
public sealed record FileEditStats(string? Diff, int Additions, int Deletions);

// Core/Support/Text.swift:77-166 (B3, droppy-code-v1.7.0).
public static class SimpleDiff
{
    // :78-81
    public const int DiffLimit = 200_000;

    // :83-84. Unchanged lines kept on each side of a change; hunks closer than twice that merge.
    private const int ContextLines = 3;

    // :86-165. A compact unified diff between two texts, one hunk per run of changes with context.
    // B3 rewrite (ED-199): the B2 head/tail common-prefix-and-suffix trim is replaced by an exact line
    // diff via SwiftDifference (Marrow.Core.Port.SwiftDifference, a port of stdlib
    // CollectionDifference.difference(from:)), grouped into one or more hunks. See PR-STORE-112,
    // PR-STORE-175 and the retirement of PR-STORE-111.
    public static FileEditStats Unified(string oldText, string newText)
    {
        var oldLines = oldText.Length == 0 ? [] : oldText.Split('\n'); // :88
        var newLines = newText.Length == 0 ? [] : newText.Split('\n'); // :89

        // :91-103. Too large for an exact diff, so count by the unchanged head and tail instead. The
        // guard now runs FIRST (upstream defect 47 is fixed at B3: see PR-STORE-111's retirement), so
        // nothing below this branch runs for an over-cap input.
        if ((long)Encoding.UTF8.GetByteCount(oldText) + Encoding.UTF8.GetByteCount(newText) > DiffLimit)
        {
            var prefix = 0;
            while (prefix < oldLines.Length && prefix < newLines.Length && Equal(oldLines[prefix], newLines[prefix]))
                prefix++;
            var suffix = 0;
            while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
                   Equal(oldLines[oldLines.Length - 1 - suffix], newLines[newLines.Length - 1 - suffix]))
                suffix++;
            return new(null, newLines.Length - prefix - suffix, oldLines.Length - prefix - suffix);
        }

        // :105-112. newLines.difference(from: oldLines): removed offsets are into oldLines, inserted
        // offsets are into newLines (SwiftDifference.Difference(from: oldLines, to: newLines, ...)).
        var difference = SwiftDifference.Difference(oldLines, newLines, Equal);
        var removed = difference.Removals.Select(removal => removal.Offset).ToHashSet();
        var inserted = difference.Insertions.Select(insertion => insertion.Offset).ToHashSet();
        // :113-115
        if (removed.Count == 0 && inserted.Count == 0) return new(null, 0, 0);

        // :117-136. Walk both texts together, emitting the merged line stream a unified diff is cut from.
        var ops = new List<DiffOp>(oldLines.Length + newLines.Length);
        var i = 0;
        var j = 0;
        while (i < oldLines.Length || j < newLines.Length)
        {
            if (i < oldLines.Length && removed.Contains(i)) { ops.Add(new DiffOp.Deletion(oldLines[i], i + 1)); i++; }
            else if (j < newLines.Length && inserted.Contains(j)) { ops.Add(new DiffOp.Addition(newLines[j], j + 1)); j++; }
            else if (i < oldLines.Length && j < newLines.Length) { ops.Add(new DiffOp.Context(oldLines[i], i + 1, j + 1)); i++; j++; }
            else break;
        }

        // :138-164
        var changes = Enumerable.Range(0, ops.Count).Where(index => ops[index].IsChange).ToList();
        var hunks = new List<string>();
        var groupStart = changes[0];
        var groupEnd = changes[0];
        void AppendHunk()
        {
            var start = Math.Max(0, groupStart - ContextLines);
            var end = Math.Min(ops.Count - 1, groupEnd + ContextLines);
            var slice = ops.GetRange(start, end - start + 1);
            var oldStart = FirstNumberOrZero(slice.Select(op => op.OldNumber));
            var newStart = FirstNumberOrZero(slice.Select(op => op.NewNumber));
            var oldCount = slice.Count(op => op.OldNumber is not null);
            var newCount = slice.Count(op => op.NewNumber is not null);
            var lines = new List<string>
            {
                FormattableString.Invariant($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@")
            };
            lines.AddRange(slice.Select(op => op.Line));
            hunks.Add(string.Join("\n", lines));
        }
        foreach (var change in changes.Skip(1))
        {
            if (change - groupEnd <= 2 * ContextLines + 1) groupEnd = change;
            else { AppendHunk(); groupStart = change; groupEnd = change; }
        }
        AppendHunk();
        return new(string.Join("\n", hunks), inserted.Count, removed.Count);
    }

    // Swift `slice.compactMap(\.oldNumber/\.newNumber).first ?? 0` (:146-147): the first non-null
    // number in a hunk's slice, or 0 when that side has no op in it. Written as an explicit loop
    // rather than LINQ's FirstOrDefault (R-COLL-3): default(int?) happens to be null here, but the
    // loop keeps this call site unambiguous with the rest of the port's first/last conventions.
    private static int FirstNumberOrZero(IEnumerable<int?> numbers)
    {
        foreach (var number in numbers)
            if (number is { } value) return value;
        return 0;
    }

    // Swift line == compares Characters by canonical equivalence (R-STR-2): call SwiftString.Equal
    // rather than a second NFC-compare here.
    private static bool Equal(string left, string right) => SwiftString.Equal(left, right);
}

// Core/Support/Text.swift:168-204 (B3). File-private in Swift; a C# `file` type here (R-NAME-6). One
// line of the merged stream a unified diff is assembled from, numbered from one.
// R-ENUM-2: associated-value enum -> abstract record with private constructor and nested sealed records.
file abstract record DiffOp
{
    private DiffOp() { }

    public abstract int? OldNumber { get; } // :174-180
    public abstract int? NewNumber { get; } // :182-188
    public abstract bool IsChange { get; } // :190-195
    public abstract string Line { get; } // :197-203

    // :170
    public sealed record Context(string Text, int Old, int New) : DiffOp
    {
        public override int? OldNumber => Old;
        public override int? NewNumber => New;
        public override bool IsChange => false;
        public override string Line => " " + Text;
    }

    // :171
    public sealed record Deletion(string Text, int Old) : DiffOp
    {
        public override int? OldNumber => Old;
        public override int? NewNumber => null;
        public override bool IsChange => true;
        public override string Line => "-" + Text;
    }

    // :172
    public sealed record Addition(string Text, int New) : DiffOp
    {
        public override int? OldNumber => null;
        public override int? NewNumber => New;
        public override bool IsChange => true;
        public override string Line => "+" + Text;
    }
}

public static class StringExtensions
{
    // Text.swift:220 (B3); whitespace-only strings are present values.
    public static string? NilIfEmpty(this string text) => text.Length == 0 ? null : text;

    // Text.swift:222-224 (B3). var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }.
    public static string Trimmed(this string text) => FoundationText.Trim(text, includeNewlines: true);
}
