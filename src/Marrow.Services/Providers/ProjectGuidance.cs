using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ProjectGuidance.swift (B2 1fbf81e), :1-119.

/// <summary>
/// :16 <c>struct ProjectGuidance: Sendable</c>. Root-level project guidelines for the native API sessions: reads
/// exactly <c>AGENTS.md</c> and <c>CLAUDE.md</c> at the working directory's root (never a parent, never a
/// repository scan) and renders them as one system-prompt block, re-rendered only when a file's size or
/// modification date changed since the last call (PR-PROVREG-223, PR-PROVREG-225).
/// </summary>
/// <remarks>
/// ENGINEERING DECISION: a sealed class, not an immutable record (R-VAL-1 covers model values). The Swift struct
/// is a mutable cache container whose <c>mutating</c> method changes it in place, and per PR-PROVREG-232 each
/// native API session owns exactly one instance for its own working directory (OpenAICompatibleSession.swift:41,
/// :234) and holds it in a stored <c>var</c>, mutating it through that variable without ever copying it. With no
/// copy anywhere, a class instance behaves identically; this matches the RecentCache.swift port
/// (Marrow.Core/Support/RecentCache.cs) for the same reason.
/// </remarks>
public sealed class ProjectGuidance
{
    /// <summary>:18 The two files a project keeps its own guidance in, in injection order.</summary>
    public static readonly IReadOnlyList<string> FileNames = new[] { "AGENTS.md", "CLAUDE.md" };

    /// <summary>:20 The most bytes of any one file that are injected.</summary>
    public const int PerFileLimit = 16_000;

    /// <summary>:22 The most bytes all the files together contribute to the system prompt.</summary>
    public const int TotalLimit = 24_000;

    // :24-27 struct FileState: Equatable, Sendable.
    private readonly record struct FileState(long Size, DateTimeOffset? Modified);

    // :29
    public string WorkingDirectory { get; }

    // :30-32 private var states/block/didLoad.
    private Dictionary<string, FileState> states = new();
    private string? block;
    private bool didLoad;

    // :34-36 init(workingDirectory:)
    public ProjectGuidance(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// :38-47 <c>mutating func systemPromptBlock() -> String?</c>. The block for the current files, refreshed
    /// only when one of them changed since the last call. Null when the project has neither file.
    /// </summary>
    public string? SystemPromptBlock()
    {
        var current = States(WorkingDirectory); // :41
        if (didLoad && StatesEqual(current, states)) return block; // :42
        didLoad = true; // :43
        states = current; // :44
        block = Render(WorkingDirectory); // :45
        return block; // :46
    }

    private static bool StatesEqual(Dictionary<string, FileState> a, Dictionary<string, FileState> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !value.Equals(other)) return false;
        }
        return true;
    }

    // :49-60
    private static Dictionary<string, FileState> States(string directory)
    {
        var result = new Dictionary<string, FileState>();
        foreach (var name in FileNames) // :51
        {
            var path = ResolvedFile(name, directory); // :52
            if (path is null) continue;
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists) continue;
            }
            catch
            {
                continue; // :52 try? attributesOfItem
            }
            result[name] = new FileState(
                info.Length, // :55 attributes[.size]
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)); // :56 attributes[.modificationDate]
        }
        return result;
    }

    /// <summary>
    /// :62-75 The file's real path inside the project, or null when it is absent or a symlink (or, on Windows, a
    /// junction/reparse point) whose target leaves the working directory. The repository is never scanned, only
    /// these names at the root are looked up.
    /// </summary>
    private static string? ResolvedFile(string name, string directory)
    {
        var resolvedRoot = ResolveRealPath(directory); // :66
        var candidate = ResolveRealPath(Path.Combine(directory, name)); // :67
        // :68 rootPrefix: trailing separator appended so a sibling whose name merely starts with the root's name
        // is not accepted by the :69 prefix check.
        var rootPrefix = resolvedRoot.EndsWith('\\') ? resolvedRoot : resolvedRoot + "\\";
        // PLATFORM BOUNDARY (PR-PROVREG-226 windows_path): Windows paths are case-insensitive.
        if (!string.Equals(candidate, resolvedRoot, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null; // :69
        }
        try
        {
            if (!File.Exists(candidate)) return null; // :71 fileExists && !isDirectory
            var attributes = File.GetAttributes(candidate);
            if ((attributes & FileAttributes.Directory) != 0) return null; // :71
            // :72-73 attributes[.type] == .typeRegular: a reparse point that File.Exists still reports here would
            // be a link type ResolveRealPath could not follow (e.g. a broken target); reject it as non-regular.
            if ((attributes & FileAttributes.ReparsePoint) != 0) return null;
            return candidate; // :74
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves every symlink and, on Windows, junction/mount-point reparse component along <paramref name="path"/>,
    /// the way <c>URL.resolvingSymlinksInPath()</c> resolves the whole path (ED-141: this is the explicit
    /// symlink-resolution operation ED-141 calls out at :66-67, separate from the purely lexical SwiftPath helpers).
    /// A path, or a path component, that does not exist is left as-is from that point on.
    /// </summary>
    private static string ResolveRealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var rest = full.Length > root.Length ? full[root.Length..] : string.Empty;
        var segments = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var segment in segments)
        {
            current = current.Length == 0 ? segment : current + Path.DirectorySeparatorChar + segment;
            current = ResolveIfReparsePoint(current);
        }
        return current.Length == 0 ? full : current;
    }

    private static string ResolveIfReparsePoint(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0) return path;
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                return target is null ? path : Path.GetFullPath(target.FullName);
            }
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0) return path;
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                return target is null ? path : Path.GetFullPath(target.FullName);
            }
        }
        catch
        {
            // Best effort: an inaccessible or vanished component is left as-is; the caller's existence checks
            // downstream will reject it.
        }
        return path;
    }

    // :77-103
    private static string? Render(string directory)
    {
        var sections = new List<string>(); // :78
        var remaining = TotalLimit; // :79
        foreach (var name in FileNames) // :80
        {
            var path = ResolvedFile(name, directory); // :81
            if (path is null) continue;
            var data = BoundedData(path); // :82
            if (data is null)
            {
                // :83
                sections.Add($"Could not load {name}. Read it with the read_file tool before changing the project.");
                continue;
            }
            long size; // :86
            try
            {
                size = new FileInfo(path).Length;
            }
            catch
            {
                size = data.Length;
            }
            if (data.Length > PerFileLimit || data.Length > remaining) // :89
            {
                // :90
                sections.Add($"{name} is too large to include here ({size} bytes). Read {name} with the read_file tool; it may contain rules the project expects you to follow.");
                continue;
            }
            var text = SwiftText.DecodeUtf8OrNil(data); // :93
            if (text is null || text.Trimmed().Length == 0) continue; // :93-94
            remaining -= data.Length; // :95
            sections.Add($"--- {name} ---\n{text}"); // :96
        }
        if (sections.Count == 0) return null; // :98
        // :99-102
        return $"Project guidelines from {directory}. They supplement the rules above; where they conflict, the nth rules take priority.\n" +
            // DEVIATION OD-0012 F1: "Marrow" replaces "Droppy Code" in the sentence above.
            string.Join("\n\n", sections);
    }

    /// <summary>
    /// :105-118 At most <see cref="PerFileLimit"/> + 1 bytes, read in chunks of at most 65536 bytes, so an
    /// oversized file is detected without reading it whole.
    /// </summary>
    private static byte[]? BoundedData(string path)
    {
        FileStream handle;
        try
        {
            handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); // :108 FileHandle takes no lock, so an editor's write handle does not block the read
        }
        catch
        {
            return null;
        }
        using (handle) // :109 defer { try? handle.close() }
        {
            using var buffer = new MemoryStream();
            try
            {
                while (buffer.Length <= PerFileLimit) // :111
                {
                    var toRead = (int)Math.Min(65_536, PerFileLimit + 1 - buffer.Length); // :113
                    var chunk = new byte[toRead];
                    var read = handle.Read(chunk, 0, toRead);
                    if (read == 0) break; // :113 !chunk.isEmpty
                    buffer.Write(chunk, 0, read); // :114
                }
            }
            catch
            {
                return null; // :115
            }
            return buffer.ToArray(); // :117
        }
    }
}
