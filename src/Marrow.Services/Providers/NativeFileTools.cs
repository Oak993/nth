using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/NativeFileTools.swift (B2 1fbf81e), :1-161.

// :3-160. `struct NativeFileTools: Sendable` -> immutable sealed record (R-VAL-1).
public sealed record NativeFileTools(string WorkingDirectory) // :3-4
{
    private const int ReadLimit = 1_000_000; // :6

    // :14. Recursive listing never descends into these; the non-recursive listing skips them outright.
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.Ordinal)
    {
        ".git", "node_modules", ".build", "build", "dist", "DerivedData", "Pods", "target", ".venv", "venv",
    };

    // :7, :9-13. `private static let mutations = Mutations()`; named to avoid colliding with the nested actor
    // type it instantiates (R-NAME-3). Swift: mutations.
    private static readonly Mutations Serializer = new();

    // :9-13. `private actor Mutations { func run(_ operation: @Sendable () throws -> Void) rethrows { try
    // operation() } }`: one shared actor serializes every write and edit across the whole app.
    private sealed class Mutations : Actor
    {
        public Task Run(Action operation) => Isolated(operation); // :10-12
    }

    /// <summary>
    /// :16-31 resolveURL(_:). The absolute path a model-supplied path refers to, refused when it leaves the
    /// project. ED-137/ED-039(2)/ED-138: the root-containment test at :27 is the same case-insensitive,
    /// forward-slashed comparison TouchedPaths.Normalize/Relative apply (SwiftPath.TailUnder), replacing the
    /// exact-equal and byte-exact prefix test the macOS Swift performs; ED-138 replaces the "/" absoluteness
    /// test at :23 with SwiftPath.IsAbsolute (the "~" clause stays verbatim, as it does at those other sites).
    /// This is the confinement boundary for read, list, write, edit and search (PR-PROVAPI-176).
    /// </summary>
    public string ResolveURL(string path)
    {
        var trimmed = path.Trimmed(); // :20
        if (trimmed.Length == 0) // :21
            throw new ProviderError(new ProviderError.Case.Failed("A file path is required."));
        var root = SwiftPath.StandardizedFileURLPath(WorkingDirectory, null); // :22
        var candidate = SwiftPath.IsAbsolute(trimmed) || SwiftString.HasPrefix(trimmed, "~") // :23
            ? SwiftPath.ExpandingTildeInPath(trimmed) // :24
            : SwiftPath.FileURLAppendingPathComponentPath(root, trimmed); // :25
        var standardized = SwiftPath.StandardizedFileURLPath(candidate, null); // :26
        if (!SwiftPath.SamePath(standardized, root) && SwiftPath.TailUnder(standardized, root) is null) // :27
        {
            throw new ProviderError(new ProviderError.Case.Failed(
                $"That path is outside the project. Stay inside {WorkingDirectory}.")); // :28
        }
        return standardized; // :30
    }

    /// <summary>
    /// :33-38 displayPath(_:). `ToolTitles.RelativePath` already falls back to returning its input unchanged
    /// when the input is not under the root (ProviderSession.cs:275), which is exactly the ":37 return trimmed"
    /// branch, so the outer "hasPrefix(workingDirectory)" guard at :36 needs no separate test here.
    /// </summary>
    public string DisplayPath(string path)
    {
        var trimmed = path.Trimmed(); // :34
        if (trimmed.Length == 0) return WorkingDirectory; // :35
        return ToolTitles.RelativePath(trimmed, WorkingDirectory); // :36-37
    }

    /// <summary>
    /// :40-77 readFile(_:startLine:lineCount:). PR-PROVAPI-178, PR-PROVAPI-179, PR-PROVAPI-180, PR-PROVAPI-181.
    /// </summary>
    public async Task<string> ReadFile(string path, int? startLine = null, int? lineCount = null)
    {
        SwiftTask.CheckCancellation(); // :41
        var url = ResolveURL(path); // :42
        if (!(File.Exists(url) || Directory.Exists(url))) // :43 fileExists(atPath:)
            throw new ProviderError(new ProviderError.Case.Failed($"No such file: {DisplayPath(path)}.")); // :44
        using var handle = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read); // :46-47
        var data = new MemoryStream(); // :48
        while (data.Length <= ReadLimit) // :49
        {
            SwiftTask.CheckCancellation(); // :50
            var count = (int)Math.Min(65_536, ReadLimit + 1 - data.Length); // :51
            var chunk = new byte[count];
            var read = handle.Read(chunk, 0, count); // :52 handle.read(upToCount:)
            if (read == 0) break; // :52 !chunk.isEmpty
            data.Write(chunk, 0, read); // :53
        }
        SwiftTask.CheckCancellation(); // :55
        if (data.Length > ReadLimit) // :56
        {
            var byteCount = handle.Length; // :57 handle.seekToEnd()
            throw new ProviderError(new ProviderError.Case.Failed($"That file is too large to read ({byteCount} bytes)."));
        }
        var bytes = data.ToArray();
        // :60. ISO Latin 1 decodes every byte sequence, so the "not readable" branch is unreachable in
        // practice; it is reproduced anyway, as PR-PROVAPI-179 requires.
        var text = SwiftText.DecodeUtf8OrNil(bytes) ?? DecodeLatin1OrNull(bytes);
        if (text is null)
            throw new ProviderError(new ProviderError.Case.Failed("That file is not readable as text."));
        if (startLine is not null || lineCount is not null) // :63
        {
            var lines = SwiftString.Split(text, "\n", omittingEmptySubsequences: false); // :64
            var first = Math.Max(1, startLine ?? 1); // :65
            if (first > lines.Count) // :66
                return $"(the file has {lines.Count} lines; start_line {first} is past the end)";
            var count = Math.Max(1, lineCount ?? 200); // :67
            var last = first - 1 + Math.Min(count, lines.Count - first + 1); // :68
            var slice = string.Join("\n", lines.Skip(first - 1).Take(last - (first - 1))); // :69
            return $"(lines {first}-{last} of {lines.Count})\n" + slice; // :70
        }
        if (SwiftString.Utf8Count(text) <= 60_000) return text; // :72-73
        var characterCount = SwiftString.Count(text); // :74
        if (characterCount > 60_000) // :75
        {
            // U+2026 HORIZONTAL ELLIPSIS.
            return SwiftString.Prefix(text, 60_000) +
                "\n\U00002026(truncated, " + characterCount + " chars total; read the rest with start_line and line_count)";
        }
        return text; // :76
    }

    private static string? DecodeLatin1OrNull(byte[] bytes) => System.Text.Encoding.Latin1.GetString(bytes);

    /// <summary>:79-90 writeFile(path:content:). PR-PROVAPI-182.</summary>
    public async Task WriteFile(string path, string content)
    {
        SwiftTask.CheckCancellation(); // :80
        await Serializer.Run(() =>
        {
            SwiftTask.CheckCancellation(); // :82
            var url = ResolveURL(path); // :83
            // :84. `content.data(using: .utf8)` cannot fail for a valid Swift String (always well-formed
            // Unicode); the guard is reproduced for a C# string that could carry an unpaired surrogate.
            var data = EncodeUtf8OrNull(content)
                ?? throw new ProviderError(new ProviderError.Case.Failed("Could not encode that content."));
            SwiftTask.CheckCancellation(); // :85
            Directory.CreateDirectory(SwiftPath.FileURLDeletingLastPathComponentPath(url)); // :86
            SwiftTask.CheckCancellation(); // :87
            FileOps.WriteAtomic(url, data); // :88
        });
    }

    private static readonly System.Text.UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static byte[]? EncodeUtf8OrNull(string text)
    {
        try { return StrictUtf8.GetBytes(text); }
        catch (System.Text.EncoderFallbackException) { return null; }
    }

    /// <summary>:92-120 editFile(path:old:new:). PR-PROVAPI-182, PR-PROVAPI-183.</summary>
    public async Task EditFile(string path, string old, string @new)
    {
        SwiftTask.CheckCancellation(); // :93
        await Serializer.Run(() =>
        {
            SwiftTask.CheckCancellation(); // :95
            if (old.Length == 0) // :96
            {
                throw new ProviderError(new ProviderError.Case.Failed(
                    "old_string must not be empty. Read the file first, then edit an exact block."));
            }
            var url = ResolveURL(path); // :97
            // :98. `String(contentsOf:encoding: .utf8)` throws its own (Cocoa NSError) error when the bytes are
            // not valid UTF-8 text; the record notes only that this fails "with the system's own error", no
            // exact wording, so a natural decode failure is let through rather than a fabricated message.
            var currentBytes = File.ReadAllBytes(url);
            var current = SwiftText.DecodeUtf8OrNil(currentBytes)
                ?? throw new IOException($"The file could not be read as text: {DisplayPath(path)}.");
            var searchStart = 0; // :99-100 NSRange(location: 0, length: current.length)
            var firstMatchIndex = -1; // :101 NSNotFound
            var occurrences = 0; // :102
            while (searchStart < current.Length) // :102 remaining.length > 0
            {
                SwiftTask.CheckCancellation(); // :103
                var match = current.IndexOf(old, searchStart, StringComparison.Ordinal); // :104
                if (match < 0) break; // :105
                if (occurrences == 0) firstMatchIndex = match; // :106
                occurrences++; // :107
                searchStart = match + old.Length; // :108-109
            }
            SwiftTask.CheckCancellation(); // :111
            if (occurrences != 1) // :112
            {
                if (occurrences == 0) // :113
                {
                    throw new ProviderError(new ProviderError.Case.Failed(
                        $"That exact text was not found in {DisplayPath(path)}. Read the file and copy it exactly."));
                }
                throw new ProviderError(new ProviderError.Case.Failed(
                    $"That text appears {occurrences} times in {DisplayPath(path)}. Include more context so it matches once.")); // :114
            }
            var replacement = current[..firstMatchIndex] + @new + current[(firstMatchIndex + old.Length)..]; // :116
            SwiftTask.CheckCancellation(); // :117
            FileOps.WriteAtomic(url, System.Text.Encoding.UTF8.GetBytes(replacement)); // :118
        });
    }

    /// <summary>:122-159 listFiles(_:recursive:). PR-PROVAPI-184, PR-PROVAPI-185, PR-PROVAPI-186.</summary>
    public async Task<string> ListFiles(string path, bool recursive)
    {
        SwiftTask.CheckCancellation(); // :123
        var trimmedIsEmpty = path.Trimmed().Length == 0; // :124
        var baseUrl = trimmedIsEmpty ? WorkingDirectory : ResolveURL(path); // :124-126
        if (!Directory.Exists(baseUrl)) // :127-128 fileExists(atPath:isDirectory:), isDirectory.boolValue
        {
            // :129. `path.isEmpty`, not the trimmed check :124 uses: reproduced verbatim (an upstream quirk
            // that only shows for a whitespace-only, non-empty path).
            throw new ProviderError(new ProviderError.Case.Failed(
                $"No such directory: {DisplayPath(path.Length == 0 ? WorkingDirectory : path)}."));
        }
        var results = new List<string>(301); // :131-132
        if (recursive)
        {
            ListRecursive(baseUrl, results); // :133-143
        }
        else
        {
            ListNonRecursive(baseUrl, results); // :144-156
        }
        SwiftTask.CheckCancellation(); // :157
        return results.Count == 0 ? "(empty)" : string.Join("\n", results); // :158
    }

    // :133-143. FileManager's recursive enumerator order is unspecified (R-COLL-2); files only, hidden entries
    // and the ten named directories' descendants skipped, capped at 300 with a truncation marker.
    // DEVIATION (platform gap): `.skipsPackageDescendants` has no Windows counterpart (no bundle/package
    // concept) and is dropped; every other directory is walked.
    private void ListRecursive(string baseUrl, List<string> results)
    {
        var stack = new Stack<string>();
        stack.Push(baseUrl);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory);
            }
            catch (Exception)
            {
                continue; // :134 a failed enumerator contributes nothing further from this branch.
            }
            foreach (var entry in entries)
            {
                SwiftTask.CheckCancellation(); // :138
                var name = SwiftPath.LastPathComponent(entry);
                if (IsHiddenEntry(entry, name)) continue; // :134 .skipsHiddenFiles
                if (Directory.Exists(entry))
                {
                    if (SkippedDirectories.Contains(name)) continue; // :139 enumerator.skipDescendants()
                    stack.Push(entry);
                    continue; // :140 directories themselves are never listed.
                }
                results.Add(ToolTitles.RelativePath(entry, WorkingDirectory)); // :141
                if (results.Count >= 300) // :142
                {
                    results.Add("\U00002026(truncated)");
                    return;
                }
            }
        }
    }

    // :134. "Hidden" combines the Windows hidden attribute with a leading dot, since neither alone is what
    // .skipsHiddenFiles means on macOS.
    private static bool IsHiddenEntry(string fullPath, string name)
    {
        if (name.StartsWith('.')) return true;
        try
        {
            return File.GetAttributes(fullPath).HasFlag(FileAttributes.Hidden);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // :144-156. Sorted by name (R-COLL-1); the ten named directories and anything named like .DS_Store are
    // skipped; a directory's relative path ends with a separator; capped at 300 with a truncation marker.
    private void ListNonRecursive(string baseUrl, List<string> results)
    {
        IReadOnlyList<string> items;
        try
        {
            items = Directory.GetFileSystemEntries(baseUrl).Select(SwiftPath.LastPathComponent).ToArray(); // :145
        }
        catch (Exception)
        {
            items = [];
        }
        SwiftTask.CheckCancellation(); // :146
        foreach (var item in items.OrderBy(x => x, SwiftStringComparer.Instance)) // :147
        {
            SwiftTask.CheckCancellation(); // :148
            if (SkippedDirectories.Contains(item) || SwiftString.HasPrefix(item, ".DS_Store")) continue; // :149
            var full = SwiftPath.FileURLAppendingPathComponentPath(baseUrl, item); // :151
            var isDirectory = Directory.Exists(full); // :152
            results.Add(ToolTitles.RelativePath(full, WorkingDirectory) + (isDirectory ? "/" : "")); // :153
            if (results.Count >= 300) // :154
            {
                results.Add("\U00002026(truncated)");
                return;
            }
        }
    }
}
