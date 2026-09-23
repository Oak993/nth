using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Git;

// Source: DroppyCode/Services/Git/TurnDiff.swift (B2 1fbf81e), :1-65.

/// <summary>:3 Assembles a turn's or a thread's diff from a checkpoint snapshot, the provider's patch and tool edits.</summary>
public static class TurnDiff
{
    /// <summary>
    /// :4-50 merge(snapshot:providerPatch:edits:touched:root:repositoryRoot:providerFiles:). The snapshot is the whole
    /// checkout between two checkpoints, so when a touched set is given only files the chat's turns reported, the
    /// provider's own patch, or its tool edits name are taken from it. Reported and edit paths resolve against the
    /// working root and then normalize against the repository root; snapshot paths normalize directly. ED-138/ED-142:
    /// the <c>hasPrefix("/")</c> tests at :34 and :41 are SwiftPath.IsAbsolute on reportedPath's output (ED-173: including a forward-slash UNC "//srv/share/..."). PR-GIT-089
    /// windows_path: agent-reported paths reach this function forward-slashed (TouchedPaths.Normalize, ED-019(3)).
    /// </summary>
    public static ValueList<DiffFile> Merge(
        IReadOnlyList<DiffFile>? snapshot,
        string providerPatch,
        IReadOnlyList<FileEdit> edits,
        IReadOnlyCollection<string>? touched,
        string root,
        string? repositoryRoot = null,
        IReadOnlyList<DiffFile>? providerFiles = null)
    {
        var repository = repositoryRoot ?? root; // :5
        var directory = root; // :6 URL(fileURLWithPath: root, isDirectory: true)

        // :7-10
        string ReportedPath(string path)
        {
            var absolute = SwiftPath.StandardizedFileURLPath(path, directory); // :8
            return TouchedPaths.Normalize(absolute, repository); // :9
        }

        // :11
        HashSet<string>? touchedSet = touched is null
            ? null
            : new HashSet<string>(touched.Select(ReportedPath), SwiftStringComparer.Instance);
        var reported = providerFiles ?? DiffParser.Parse(providerPatch); // :12

        // :15-22
        var known = touchedSet;
        if (known is not null)
        {
            foreach (var file in reported)
            {
                known.Add(ReportedPath(file.Path)); // :18
                if (file.OldPath is { } old) known.Add(ReportedPath(old)); // :19
            }
            foreach (var edit in edits)
            {
                if (edit.Path.Length != 0) known.Add(ReportedPath(edit.Path)); // :21
            }
        }

        // :23-29
        var files = new Dictionary<string, DiffFile>(SwiftStringComparer.Instance);
        foreach (var source in snapshot ?? reported)
        {
            var file = source with
            {
                Path = snapshot is null ? ReportedPath(source.Path) : TouchedPaths.Normalize(source.Path, repository), // :25
                OldPath = source.OldPath is { } oldPath
                    ? (snapshot is null ? ReportedPath(oldPath) : TouchedPaths.Normalize(oldPath, repository)) // :26
                    : null,
            };
            if (known is not null && !known.Contains(file.Path) && !(file.OldPath is { } o && known.Contains(o))) continue; // :27
            Append(file, files); // :28
        }

        // :30-37
        if (snapshot is not null)
        {
            foreach (var source in reported)
            {
                var file = source with
                {
                    Path = ReportedPath(source.Path), // :32
                    OldPath = source.OldPath is { } oldPath ? ReportedPath(oldPath) : null, // :33
                };
                if (!SwiftPath.IsAbsolute(file.Path) || files.ContainsKey(file.Path)) continue; // :34, ED-138, ED-173
                Append(file, files); // :35
            }
        }

        // :38-48
        var authoritative = new HashSet<string>(files.Keys, SwiftStringComparer.Instance);
        foreach (var edit in edits)
        {
            if (edit.Path.Length == 0) continue; // :39
            var path = ReportedPath(edit.Path); // :40
            if (authoritative.Contains(path) || !(snapshot is null || SwiftPath.IsAbsolute(path))) continue; // :41, ED-138, ED-173
            var file = DiffParser.ParseHunks(edit.Diff ?? "", path); // :42
            if (file.Hunks.Count == 0) // :43-46
            {
                file = file with { ReportedAdditions = edit.Additions, ReportedDeletions = edit.Deletions };
            }
            Append(file, files); // :47
        }

        // :49 sorted { $0.path < $1.path }
        return new ValueList<DiffFile>(files.Values.OrderBy(file => file.Path, SwiftStringComparer.Instance));
    }

    /// <summary>
    /// :52-64 append(_:to:): a first entry for a path is stored as is; a second is merged into the existing one. The
    /// totals are summed into the reported counts, the binary flags are or-ed, and each incoming hunk is appended with
    /// its id set to one more than the id of the existing entry's last hunk at that moment (0 when it has none). The
    /// last hunk is read again on every iteration, after the previous append, so the ids run on from it.
    /// </summary>
    private static void Append(DiffFile file, Dictionary<string, DiffFile> files)
    {
        if (!files.TryGetValue(file.Path, out var existing)) { files[file.Path] = file; return; } // :53
        var additions = checked(existing.Additions + file.Additions); // :54
        var deletions = checked(existing.Deletions + file.Deletions); // :55
        var hunks = new List<DiffHunk>(existing.Hunks);
        foreach (var hunk in file.Hunks) // :56-59
        {
            var lastID = hunks.Count > 0 ? hunks[^1].Id : 0;
            hunks.Add(hunk with { Id = checked(lastID + 1) }); // :57, Swift Int addition traps on overflow
        }
        files[file.Path] = existing with // :60-63
        {
            Hunks = new ValueList<DiffHunk>(hunks),
            ReportedAdditions = additions,
            ReportedDeletions = deletions,
            IsBinary = existing.IsBinary || file.IsBinary,
        };
    }
}
