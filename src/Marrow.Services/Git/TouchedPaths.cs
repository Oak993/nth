using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Git;

// Source: DroppyCode/Services/Git/TouchedPaths.swift (B3 52bc792), :1-61.

/// <summary>
/// :3-4 Matches the files an agent reported editing against the files a diff contains.
/// </summary>
public static class TouchedPaths
{
    /// <summary>
    /// :6-13 normalize(_:root:): the repository-relative form of a path an agent reported, whether absolute or
    /// relative. An empty path returns "". The path is resolved relative to the standardized root, standardized
    /// itself, then the root prefix (with a trailing slash) is dropped when the resolved path is under it; a path
    /// outside the root is returned unchanged, absolute. ED-045: this is one of the four sites sharing SwiftPath's
    /// absoluteness predicate and normalization; ED-137/ED-039(2): the root-containment test is the case-insensitive
    /// comparison SwiftPath.TailUnder performs. ED-019(3): the result is forward-slashed (like TouchedPaths.relative,
    /// which feeds the same TurnRecord.touchedPaths field) since every consumer (IsBuildOutput, Matches) splits or
    /// concatenates on "/".
    /// </summary>
    public static string Normalize(string path, string root)
    {
        if (path.Length == 0) return ""; // :7
        var basePath = SwiftPath.StandardizedFileURLPath(root, null); // :8
        var resolved = SwiftPath.StandardizedFileURLPath(path, basePath); // :9
        var tail = SwiftPath.TailUnder(resolved, basePath); // :10-12
        return (tail ?? resolved).Replace('\\', '/'); // ED-019(3)
    }

    /// <summary>
    /// :15-29 relative(_:root:): the repository-relative form of a path an agent reported, or null when the path is
    /// no part of the checkout: another folder on the machine, a climb out of the root, a URL. Trims whitespace and
    /// newlines; rejects an empty result or one containing "://"; expands a leading "~"; resolves against the
    /// standardized root; requires the standardized result to sit under the root, returning null otherwise; an empty
    /// tail is also null. ED-138: only the "/" absoluteness test at :24 is replaced by SwiftPath.IsAbsolute, the "~"
    /// and "://" clauses stay verbatim. ED-137/ED-039(2): the root-containment test at :26-27 is the case-insensitive
    /// comparison SwiftPath.TailUnder performs. ED-019(3): the returned tail is forward-slashed.
    /// </summary>
    public static string? Relative(string path, string root)
    {
        var trimmed = path.Trimmed(); // :20
        if (trimmed.Length == 0 || trimmed.Contains("://", StringComparison.Ordinal)) return null; // :21
        if (trimmed.StartsWith('~')) trimmed = SwiftPath.ExpandingTildeInPath(trimmed); // :22
        var standardizedRoot = SwiftPath.StandardizingPath(root); // :23
        var absolute = SwiftPath.IsAbsolute(trimmed) ? trimmed : SwiftPath.AppendingPathComponent(standardizedRoot, trimmed); // :24
        var full = SwiftPath.StandardizingPath(absolute); // :25
        return SwiftPath.TailUnder(full, standardizedRoot)?.Replace('\\', '/').NilIfEmpty(); // :26-28, ED-019(3)
    }

    /// <summary>
    /// :31-45 isBuildOutput(_:): whether a repository-relative path is build output or a tool cache that no one
    /// wants landed or merged, even where the project's .gitignore missed it. Only names that are unmistakably
    /// caches count; a source folder called "build" or "dist" stays the project's business. The path is split on
    /// "/" discarding empty segments; a component matches when it is exactly ".DS_Store", ends with the literal
    /// ".noindex", or is one of the fifteen buildOutputNames. ED-039(3): the name comparison stays case-sensitive
    /// and the set unchanged on Windows, since folding would newly exclude directories the source keeps; only the
    /// "/" split is normalized (Windows callers pass forward slashes already, see PR-GIT-095 windows_path).
    /// </summary>
    public static bool IsBuildOutput(string path)
    {
        foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) // :40
        {
            if (component == ".DS_Store" || component.EndsWith(".noindex", StringComparison.Ordinal)) return true; // :43
            if (BuildOutputNames.Contains(component)) return true; // :44
        }
        return false; // :41
    }

    // :48-52
    private static readonly HashSet<string> BuildOutputNames = new(StringComparer.Ordinal)
    {
        "-Xcc", "DerivedData", ".build", "node_modules", "__pycache__", ".pytest_cache", ".mypy_cache",
        ".ruff_cache", "xcuserdata", ".swiftpm", "ModuleCache", ".gradle", ".turbo", ".parcel-cache",
        ".commandcode",
    };

    /// <summary>
    /// :54-59 matches(_:touched:): whether a diff file is one of the touched paths, allowing for a path reported
    /// relative to a subfolder. Both the file's path and its old path (when present) are tested; a match is exact
    /// set membership, or a set entry ending with "/" plus the file path, or a file path ending with "/" plus a set
    /// entry. PR-GIT-096 windows_path: agent-reported paths must already be normalized to forward slashes, since
    /// the rule literally concatenates "/".
    /// </summary>
    public static bool Matches(DiffFile file, IReadOnlySet<string> touched)
    {
        foreach (var path in new[] { file.Path, file.OldPath }) // :56
        {
            if (path is not { } p) continue;
            if (touched.Contains(p)) return true; // :57
            foreach (var entry in touched)
            {
                if (SwiftString.HasSuffix(entry, "/" + p) || SwiftString.HasSuffix(p, "/" + entry)) return true; // :57 external path Characters, including canonical equivalence
            }
        }
        return false;
    }
}
