using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Tests.Git;

// Source: DroppyCode/Services/Git/TurnDiff.swift (B2 1fbf81e), :1-65.
// Paths are lexical (SwiftPath.StandardizedFileURLPath, TouchedPaths.Normalize), so no folder is touched on disk.
// Sample DiffFile, DiffHunk and DiffLine values are trimmed shapes of DroppyCode/Services/Git/DiffParser.swift:3-40.
public sealed class TurnDiffTests
{
    private const string Root = @"C:\repo";

    private static DiffLine Line(long id, DiffLine.Kind kind, string text) => new(id, kind, text);

    private static DiffHunk Hunk(long id, string header, params DiffLine[] lines) => new(id, header, new ValueList<DiffLine>(lines));

    private static DiffFile File(string path, string? oldPath = null, bool isBinary = false, DiffHunk[]? hunks = null) =>
        new(path, oldPath is null ? DiffFile.Change.Modified : DiffFile.Change.Renamed, isBinary, new ValueList<DiffHunk>(hunks ?? []), oldPath);

    private static DiffHunk OneAddition(long id) => Hunk(id, "@@ -1 +1,2 @@", Line(id + 1, DiffLine.Kind.Context, "a"), Line(id + 2, DiffLine.Kind.Addition, "b"));

    private static string[] Paths(IEnumerable<DiffFile> files) => files.Select(file => file.Path).ToArray();

    private static readonly FileEdit[] NoEdits = [];

    // ---- PR-GIT-089: the touched filter over the snapshot ---------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:15-16, :24-28
    public void Merge_without_a_touched_set_takes_the_snapshot_whole()
    {
        var snapshot = new[] { File("src/a.cs"), File("notes/other-chat.md") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, null, Root);
        Assert.Equal(new[] { "notes/other-chat.md", "src/a.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:11, :27
    public void Merge_with_a_touched_set_drops_snapshot_files_outside_it()
    {
        var snapshot = new[] { File("src/a.cs"), File("src/b.cs"), File("notes/other-chat.md") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, new HashSet<string> { "src/a.cs" }, Root);
        Assert.Equal(new[] { "src/a.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:15-16, :27
    public void Merge_with_an_empty_touched_set_still_filters_the_snapshot()
    {
        var snapshot = new[] { File("src/a.cs"), File("merge/theirs.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, new HashSet<string>(), Root);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:7-11
    public void Merge_runs_touched_paths_through_the_path_resolver()
    {
        var snapshot = new[] { File("src/a.cs"), File("src/b.cs"), File("src/c.cs") };
        var touched = new HashSet<string> { @"C:\repo\src\a.cs", "lib/../src/b.cs" };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, touched, Root);
        Assert.Equal(new[] { "src/a.cs", "src/b.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:12, :16-20, :27
    public void Merge_grows_the_known_set_with_reported_paths_and_old_paths()
    {
        var snapshot = new[] { File("src/b.cs"), File("src/moved.cs", oldPath: "src/c_old.cs"), File("src/d.cs") };
        var providerFiles = new[] { File("src/b.cs"), File("src/c.cs", oldPath: "src/c_old.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, new HashSet<string>(), Root, providerFiles: providerFiles);
        Assert.Equal(new[] { "src/b.cs", "src/moved.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:12, :16-20
    public void Merge_parses_the_provider_patch_when_no_provider_files_are_given()
    {
        var snapshot = new[] { File("src/b.cs"), File("src/d.cs") };
        const string patch = "diff --git a/src/b.cs b/src/b.cs\n--- a/src/b.cs\n+++ b/src/b.cs\n@@ -1 +1 @@\n-x\n+y\n";
        var result = TurnDiff.Merge(snapshot, patch, NoEdits, new HashSet<string>(), Root);
        Assert.Equal(new[] { "src/b.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:21, :27
    public void Merge_grows_the_known_set_with_non_empty_edit_paths_only()
    {
        var snapshot = new[] { File("src/e.cs"), File("src/f.cs") };
        var edits = new[] { new FileEdit("src/e.cs"), new FileEdit("") };
        var result = TurnDiff.Merge(snapshot, "", edits, new HashSet<string>(), Root);
        Assert.Equal(new[] { "src/e.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:5-11, :25, :27
    public void Merge_resolves_reported_paths_against_the_working_root_and_snapshot_paths_against_the_repository_root()
    {
        var snapshot = new[] { File("sub/a.cs"), File("a.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, new HashSet<string> { "a.cs" }, @"C:\repo\sub", repositoryRoot: Root);
        Assert.Equal(new[] { "sub/a.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:5
    public void Merge_uses_the_working_root_as_the_repository_root_by_default()
    {
        var snapshot = new[] { File(@"C:\repo\sub\a.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, new HashSet<string> { "a.cs" }, @"C:\repo\sub");
        Assert.Equal(new[] { "a.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:25-26
    public void Merge_normalizes_snapshot_paths_and_old_paths_against_the_repository_root()
    {
        var snapshot = new[] { File(@"C:\repo\src\new.cs", oldPath: @"C:\repo\src\old.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, null, Root);
        Assert.Equal("src/new.cs", result[0].Path);
        Assert.Equal("src/old.cs", result[0].OldPath);
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:24-26
    public void Merge_without_a_snapshot_takes_reported_files_through_the_path_resolver()
    {
        var providerFiles = new[] { File("a.cs", oldPath: "old/../b.cs") };
        var result = TurnDiff.Merge(null, "", NoEdits, null, @"C:\repo\sub", repositoryRoot: Root, providerFiles: providerFiles);
        Assert.Equal("sub/a.cs", result[0].Path);
        Assert.Equal("sub/b.cs", result[0].OldPath);
    }

    [Fact]
    [Trait("Req", "PR-GIT-089")] // Services/Git/TurnDiff.swift:30-37
    public void Merge_with_a_snapshot_adds_only_absolute_reported_files_not_already_present()
    {
        var snapshot = new[] { File("src/a.cs"), File(@"D:\shared\seen.md", hunks: [OneAddition(1)]) };
        var providerFiles = new[]
        {
            File(@"D:\notes\plan.md", hunks: [OneAddition(1)]),
            File("src/new.cs", hunks: [OneAddition(1)]),
            File(@"D:\shared\seen.md", hunks: [OneAddition(1)]),
        };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, null, Root, providerFiles: providerFiles);
        Assert.Equal(new[] { "D:/notes/plan.md", "D:/shared/seen.md", "src/a.cs" }, Paths(result));
        var seen = result.Single(file => file.Path == "D:/shared/seen.md");
        Assert.Single(seen.Hunks);
        Assert.Null(seen.ReportedAdditions);
    }

    // ---- PR-GIT-090: per-edit files last, then the sort -----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:38-41
    public void Merge_skips_an_edit_whose_path_an_authoritative_entry_covers()
    {
        var snapshot = new[] { File("src/a.cs", hunks: [OneAddition(1)]) };
        var edits = new[] { new FileEdit("src/a.cs", null, 7, 4) };
        var result = TurnDiff.Merge(snapshot, "", edits, null, Root);
        Assert.Single(result);
        Assert.Null(result[0].ReportedAdditions);
        Assert.Null(result[0].ReportedDeletions);
        Assert.Equal(1, result[0].Additions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:41
    public void Merge_with_a_snapshot_adds_only_absolute_edit_paths()
    {
        var snapshot = new[] { File("src/a.cs") };
        var edits = new[] { new FileEdit("src/unseen.cs", null, 1, 0), new FileEdit(@"D:\notes\todo.md", null, 2, 0) };
        var result = TurnDiff.Merge(snapshot, "", edits, null, Root);
        Assert.Equal(new[] { "D:/notes/todo.md", "src/a.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:39-47
    public void Merge_without_a_snapshot_adds_relative_edit_paths_resolved_against_the_root()
    {
        var edits = new[] { new FileEdit("./src/../src/x.cs", null, 1, 0) };
        var result = TurnDiff.Merge(null, "", edits, null, Root);
        Assert.Equal(new[] { "src/x.cs" }, Paths(result));
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:39
    public void Merge_ignores_an_edit_with_an_empty_path()
    {
        var edits = new[] { new FileEdit("", null, 5, 5) };
        Assert.Empty(TurnDiff.Merge(null, "", edits, null, Root));
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:42-46
    public void Merge_falls_back_to_the_edit_counts_when_its_patch_has_no_hunks()
    {
        var edits = new[] { new FileEdit("src/x.cs", null, 3, 2) };
        var result = TurnDiff.Merge(null, "", edits, null, Root);
        Assert.Empty(result[0].Hunks);
        Assert.Equal(3L, result[0].ReportedAdditions);
        Assert.Equal(2L, result[0].ReportedDeletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:42-46
    public void Merge_takes_counts_from_the_edit_patch_when_it_has_hunks()
    {
        var edits = new[] { new FileEdit("src/x.cs", "@@ -1,2 +1,3 @@\n ctx\n-old\n+new1\n+new2\n", 9, 9) };
        var result = TurnDiff.Merge(null, "", edits, null, Root);
        Assert.Equal("src/x.cs", result[0].Path);
        Assert.Single(result[0].Hunks);
        Assert.Null(result[0].ReportedAdditions);
        Assert.Null(result[0].ReportedDeletions);
        Assert.Equal(2, result[0].Additions);
        Assert.Equal(1, result[0].Deletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:42-46
    public void Merge_turns_an_edit_with_neither_patch_nor_counts_into_a_zero_row()
    {
        var edits = new[] { new FileEdit("src/x.cs") };
        var result = TurnDiff.Merge(null, "", edits, null, Root);
        Assert.Single(result);
        Assert.Equal(0, result[0].Additions);
        Assert.Equal(0, result[0].Deletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:38-47
    public void Merge_merges_two_edits_of_one_path_because_the_authoritative_set_is_fixed_before_the_loop()
    {
        var edits = new[] { new FileEdit("src/x.cs", null, 3, 1), new FileEdit(@"C:\repo\src\x.cs", null, 1, 2) };
        var result = TurnDiff.Merge(null, "", edits, null, Root);
        Assert.Single(result);
        Assert.Equal(4L, result[0].ReportedAdditions);
        Assert.Equal(3L, result[0].ReportedDeletions);
    }

    [Fact]
    [Trait("Req", "PR-GIT-090")] // Services/Git/TurnDiff.swift:49
    [Trait("Rule", "R-STR-2")]
    public void Merge_sorts_the_result_by_path_ascending_in_Swift_string_order()
    {
        var snapshot = new[] { File("src/b.cs"), File("src/a.cs"), File("src/B.cs"), File("src/_x.cs") };
        var result = TurnDiff.Merge(snapshot, "", NoEdits, null, Root);
        string[] swiftOrder = ["src/B.cs", "src/_x.cs", "src/a.cs", "src/b.cs"];
        Assert.Equal(swiftOrder, Paths(result));
        // The obvious .NET culture sort orders these differently (R-TEST-4).
        Assert.NotEqual(swiftOrder, swiftOrder.OrderBy(path => path, StringComparer.InvariantCulture).ToArray());
    }

    // ---- PR-GIT-091: append(_:to:) --------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-091")] // Services/Git/TurnDiff.swift:52-61
    public void Append_sums_both_totals_into_the_reported_counts()
    {
        var first = File("src/a.cs", hunks: [Hunk(1, "@@ -1 +1,2 @@", Line(2, DiffLine.Kind.Addition, "a"), Line(3, DiffLine.Kind.Deletion, "b"))]);
        var second = new DiffFile("src/a.cs", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty, reportedAdditions: 5, reportedDeletions: 7);
        var result = TurnDiff.Merge([first, second], "", NoEdits, null, Root);
        Assert.Single(result);
        Assert.Equal(6L, result[0].ReportedAdditions);
        Assert.Equal(8L, result[0].ReportedDeletions);
    }

    [Theory]
    [Trait("Req", "PR-GIT-091")] // Services/Git/TurnDiff.swift:62
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Append_ors_the_binary_flags(bool existing, bool incoming, bool expected)
    {
        var result = TurnDiff.Merge([File("img.png", isBinary: existing), File("img.png", isBinary: incoming)], "", NoEdits, null, Root);
        Assert.Equal(expected, result[0].IsBinary);
    }

    [Fact]
    [Trait("Req", "PR-GIT-091")] // Services/Git/TurnDiff.swift:53, :56-59
    public void Append_keeps_the_first_entry_and_appends_the_incoming_hunks_after_its_own_in_order()
    {
        var first = File("src/a.cs", hunks: [OneAddition(5)]);
        var second = File("src/a.cs", hunks: [Hunk(1, "@@ -10 +10 @@"), Hunk(2, "@@ -20 +20 @@")]);
        var result = TurnDiff.Merge([first, second], "", NoEdits, null, Root);
        Assert.Equal(new[] { "@@ -1 +1,2 @@", "@@ -10 +10 @@", "@@ -20 +20 @@" }, result[0].Hunks.Select(hunk => hunk.Header).ToArray());
        Assert.Equal(DiffFile.Change.Modified, result[0].FileChange);
    }

    // The Swift at :56-59 reads existing.hunks.last inside the loop, after each append, so the appended hunks get
    // last+1, last+2, ... in turn. PR-GIT-091's expected_result says every appended hunk gets the identical id; the
    // Swift does not do that (reported as a record conflict), so this test pins the Swift and carries no Req trait.
    [Fact]
    [Trait("Rule", "R-VAL-1")]
    public void Append_renumbers_each_appended_hunk_from_the_last_hunk_id_read_after_each_append()
    {
        var first = File("src/a.cs", hunks: [Hunk(9, "@@ -1 +1 @@"), Hunk(5, "@@ -2 +2 @@")]);
        var second = File("src/a.cs", hunks: [Hunk(1, "@@ -3 +3 @@"), Hunk(1, "@@ -4 +4 @@"), Hunk(40, "@@ -5 +5 @@")]);
        var result = TurnDiff.Merge([first, second], "", NoEdits, null, Root);
        Assert.Equal(new long[] { 9, 5, 6, 7, 8 }, result[0].Hunks.Select(hunk => hunk.Id).ToArray());
    }

    [Fact]
    [Trait("Rule", "R-VAL-1")]
    public void Append_onto_an_entry_without_hunks_numbers_from_one()
    {
        var first = File("src/a.cs");
        var second = File("src/a.cs", hunks: [Hunk(7, "@@ -1 +1 @@"), Hunk(8, "@@ -2 +2 @@")]);
        var result = TurnDiff.Merge([first, second], "", NoEdits, null, Root);
        Assert.Equal(new long[] { 1, 2 }, result[0].Hunks.Select(hunk => hunk.Id).ToArray());
    }
}
