// PORT-ONLY: web surfaces W14, Tabula's Diff view (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.3 "a 'Diff' toggle compares
// any two versions (side by side or inline, the same diff renderer as Changes)", 6.1 "Diff compares per file"; the approved
// canvas index.html section 4 "Comparing v3 → v4"; ED-NEW-web-w14-1). No Swift counterpart. Two versions of an artifact as the
// Changes pane's own model (DiffFile, DiffHunk, DiffLine), so the pane draws them with the Changes renderer: one file for a
// single-file artifact, one per changed file for a project. Lines are compared by the port's linear-space Myers
// (SwiftDifference), with three lines of context around each change, as git shows them.
using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Web;

/// <summary>One row of a side-by-side diff: the old line on the left, the new one on the right (either may be missing), or a hunk header.</summary>
public sealed record ArtifactDiffRow(DiffLine? Left, DiffLine? Right, string? Header = null);

public static class ArtifactDiff
{
    /// <summary>Unchanged lines kept around a change.</summary>
    public const int Context = 3;

    /// <summary>The two texts as one file's diff; no hunks when they are the same. Line endings do not count as changes.</summary>
    public static DiffFile Text(string? before, string? after, string path)
    {
        var old = Lines(before);
        var @new = Lines(after);
        var change = before is null ? DiffFile.Change.Added : after is null ? DiffFile.Change.Deleted : DiffFile.Change.Modified;
        return new DiffFile(path, change, isBinary: false, Hunks(old, @new));
    }

    /// <summary>
    /// A project's two versions file by file (6.1): every file added, removed or changed, in path order; unchanged files are
    /// left out. A path compares without regard to case, as the store keeps it.
    /// </summary>
    public static IReadOnlyList<DiffFile> Project(IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string>? after)
    {
        before ??= new Dictionary<string, string>();
        after ??= new Dictionary<string, string>();
        var paths = before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var files = new List<DiffFile>();
        foreach (var path in paths)
        {
            var had = Find(before, path);
            var has = Find(after, path);
            if (had is not null && has is not null && Normal(had) == Normal(has)) continue;
            files.Add(Text(had, has, path));
        }
        return files;
    }

    /// <summary>The diff between two versions of one artifact: one file for a single-file kind, one per changed file for a project.</summary>
    public static IReadOnlyList<DiffFile> Between(ArtifactSnapshot before, ArtifactSnapshot after, string fileName)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.IsProject || after.IsProject) return Project(before.Files, after.Files);
        var file = Text(before.Text ?? "", after.Text ?? "", fileName);
        return file.Hunks.Count == 0 ? [] : [file];
    }

    /// <summary>
    /// The side-by-side rows of a file (6.3): context on both sides, a run of removals beside the run of additions that
    /// follows it, row by row, and each hunk's header across both.
    /// </summary>
    public static IReadOnlyList<ArtifactDiffRow> SideBySide(DiffFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var rows = new List<ArtifactDiffRow>();
        foreach (var hunk in file.Hunks)
        {
            rows.Add(new ArtifactDiffRow(null, null, hunk.Header));
            var removed = new List<DiffLine>();
            var added = new List<DiffLine>();
            void Flush()
            {
                for (var i = 0; i < Math.Max(removed.Count, added.Count); i++)
                    rows.Add(new ArtifactDiffRow(i < removed.Count ? removed[i] : null, i < added.Count ? added[i] : null));
                removed.Clear();
                added.Clear();
            }
            foreach (var line in hunk.Lines)
            {
                switch (line.LineKind)
                {
                    case DiffLine.Kind.Deletion:
                        if (added.Count > 0) Flush();
                        removed.Add(line);
                        break;
                    case DiffLine.Kind.Addition:
                        added.Add(line);
                        break;
                    default:
                        Flush();
                        rows.Add(new ArtifactDiffRow(line, line));
                        break;
                }
            }
            Flush();
        }
        return rows;
    }

    /// <summary>A text's lines with its line endings removed; an empty or missing text has none, a final line break adds none.</summary>
    public static IReadOnlyList<string> Lines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var lines = Normal(text).Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static string? Find(IReadOnlyDictionary<string, string> files, string path)
    {
        if (files.TryGetValue(path, out var exact)) return exact;
        foreach (var (key, value) in files)
            if (string.Equals(key, path, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }

    private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // Every line of both texts in reading order, each marked kept, removed or added, then cut into hunks with Context
    // unchanged lines around each change (two changes closer than twice that share a hunk).
    private static ValueList<DiffHunk> Hunks(IReadOnlyList<string> old, IReadOnlyList<string> @new)
    {
        var difference = SwiftDifference.Difference(old, @new, string.Equals);
        if (difference.Removals.Count == 0 && difference.Insertions.Count == 0) return ValueList<DiffHunk>.Empty;
        var removed = difference.Removals.Select(r => r.Offset).ToHashSet();
        var inserted = difference.Insertions.Select(i => i.Offset).ToHashSet();
        var all = new List<(DiffLine.Kind Kind, string Text, int Old, int New)>(old.Count + inserted.Count);
        int a = 0, b = 0;
        while (a < old.Count || b < @new.Count)
        {
            if (a < old.Count && removed.Contains(a)) { all.Add((DiffLine.Kind.Deletion, old[a], a + 1, 0)); a++; }
            else if (b < @new.Count && inserted.Contains(b)) { all.Add((DiffLine.Kind.Addition, @new[b], 0, b + 1)); b++; }
            else
            {
                all.Add((DiffLine.Kind.Context, b < @new.Count ? @new[b] : old[a], a + 1, b + 1));
                a++;
                b++;
            }
        }

        var hunks = new List<DiffHunk>();
        long id = 0;
        var at = 0;
        while (at < all.Count)
        {
            var first = all.FindIndex(at, entry => entry.Kind != DiffLine.Kind.Context);
            if (first < 0) break;
            var start = Math.Max(at, first - Context);
            var last = first;
            for (var i = first + 1; i < all.Count; i++)
            {
                if (all[i].Kind == DiffLine.Kind.Context) continue;
                if (i - last > 2 * Context) break;
                last = i;
            }
            var end = Math.Min(all.Count - 1, last + Context);
            var lines = new List<DiffLine>(end - start + 1);
            for (var i = start; i <= end; i++)
            {
                var (kind, text, o, n) = all[i];
                lines.Add(new DiffLine(++id, kind, text, kind == DiffLine.Kind.Addition ? null : o, kind == DiffLine.Kind.Deletion ? null : n));
            }
            hunks.Add(new DiffHunk(hunks.Count, Header(all, start, end), new ValueList<DiffLine>(lines)));
            at = end + 1;
        }
        return new ValueList<DiffHunk>(hunks);
    }

    // "@@ -12,7 +12,9 @@", git's form: a side with no lines names the line before the hunk.
    private static string Header(List<(DiffLine.Kind Kind, string Text, int Old, int New)> all, int start, int end)
    {
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;
        for (var i = start; i <= end; i++)
        {
            var (kind, _, o, n) = all[i];
            if (kind != DiffLine.Kind.Addition) { if (oldCount++ == 0) oldStart = o; }
            if (kind != DiffLine.Kind.Deletion) { if (newCount++ == 0) newStart = n; }
        }
        if (oldCount == 0) oldStart = Before(all, start, old: true);
        if (newCount == 0) newStart = Before(all, start, old: false);
        return string.Create(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
    }

    private static int Before(List<(DiffLine.Kind Kind, string Text, int Old, int New)> all, int start, bool old)
    {
        for (var i = start - 1; i >= 0; i--)
        {
            var (kind, _, o, n) = all[i];
            if (old && kind != DiffLine.Kind.Addition) return o;
            if (!old && kind != DiffLine.Kind.Deletion) return n;
        }
        return 0;
    }
}
