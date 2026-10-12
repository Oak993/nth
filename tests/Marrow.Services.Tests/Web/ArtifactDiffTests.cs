using Marrow.Core.Models;
using Marrow.Services.Git;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W14, Tabula's Diff (DESIGN.md 6.3 "compares any two versions (side by side or inline, the same diff
// renderer as Changes)", 6.1 "Diff compares per file"). Two versions become the Changes pane's own model: hunks with three
// lines of context and git's headers, one file per changed project file, and the side-by-side rows.
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class ArtifactDiffTests
{
    private static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => "line " + i + "\n"));

    [Fact]
    public void One_changed_line_is_one_hunk_with_three_lines_of_context_and_a_git_header()
    {
        var before = Lines(1, 20);
        var after = before.Replace("line 10\n", "line ten\n", StringComparison.Ordinal);
        var file = ArtifactDiff.Text(before, after, "pricing.html");
        Assert.Equal(DiffFile.Change.Modified, file.FileChange);
        var hunk = Assert.Single(file.Hunks);
        Assert.Equal("@@ -7,7 +7,7 @@", hunk.Header);
        Assert.Equal(
            ["  line 7", "  line 8", "  line 9", "- line 10", "+ line ten", "  line 11", "  line 12", "  line 13"],
            hunk.Lines.Select(l => (l.LineKind switch { DiffLine.Kind.Addition => "+ ", DiffLine.Kind.Deletion => "- ", _ => "  " }) + l.Text));
        var removed = hunk.Lines.Single(l => l.LineKind == DiffLine.Kind.Deletion);
        var added = hunk.Lines.Single(l => l.LineKind == DiffLine.Kind.Addition);
        Assert.Equal((10L, (long?)null), (removed.OldNumber!.Value, removed.NewNumber));
        Assert.Equal(((long?)null, 10L), (added.OldNumber, added.NewNumber!.Value));
    }

    [Fact]
    public void Changes_close_together_share_a_hunk_and_far_apart_ones_do_not()
    {
        var before = Lines(1, 40);
        var near = before.Replace("line 5\n", "five\n", StringComparison.Ordinal).Replace("line 10\n", "ten\n", StringComparison.Ordinal);
        Assert.Single(ArtifactDiff.Text(before, near, "a").Hunks);
        var far = before.Replace("line 5\n", "five\n", StringComparison.Ordinal).Replace("line 30\n", "thirty\n", StringComparison.Ordinal);
        var hunks = ArtifactDiff.Text(before, far, "a").Hunks;
        Assert.Equal(2, hunks.Count);
        Assert.Equal(["@@ -2,7 +2,7 @@", "@@ -27,7 +27,7 @@"], hunks.Select(h => h.Header));
    }

    [Fact]
    public void Line_endings_do_not_count_and_the_same_text_has_no_hunks()
    {
        Assert.Empty(ArtifactDiff.Text("a\nb\n", "a\r\nb\r\n", "a").Hunks);
        Assert.Empty(ArtifactDiff.Text("a\nb", "a\nb\n", "a").Hunks);
        Assert.Equal(["a", "b"], ArtifactDiff.Lines("a\r\nb\r\n"));
        Assert.Empty(ArtifactDiff.Lines(""));
    }

    [Fact]
    public void A_missing_side_is_an_added_or_deleted_file_with_every_line()
    {
        var added = ArtifactDiff.Text(null, "x\ny\n", "new.css");
        Assert.Equal(DiffFile.Change.Added, added.FileChange);
        Assert.Equal("@@ -0,0 +1,2 @@", Assert.Single(added.Hunks).Header);
        var deleted = ArtifactDiff.Text("x\n", null, "old.css");
        Assert.Equal(DiffFile.Change.Deleted, deleted.FileChange);
        Assert.Equal("@@ -1,1 +0,0 @@", Assert.Single(deleted.Hunks).Header);
    }

    [Fact]
    public void A_project_compares_file_by_file_in_path_order_and_leaves_unchanged_files_out()
    {
        var before = new Dictionary<string, string> { ["index.html"] = "<p>a</p>\n", ["style.css"] = "p{}\n", ["app.js"] = "1\n" };
        var after = new Dictionary<string, string> { ["INDEX.html"] = "<p>a</p>\n", ["style.css"] = "p{color:red}\n", ["util.js"] = "2\n" };
        var files = ArtifactDiff.Project(before, after);
        Assert.Equal([("app.js", DiffFile.Change.Deleted), ("style.css", DiffFile.Change.Modified), ("util.js", DiffFile.Change.Added)],
            files.Select(f => (f.Path, f.FileChange)));
    }

    [Fact]
    public void Side_by_side_pairs_a_run_of_removals_with_the_additions_that_follow_and_keeps_context_on_both_sides()
    {
        var file = ArtifactDiff.Text("a\nb\nc\nd\n", "a\nB\nC\nX\nd\n", "f");
        var rows = ArtifactDiff.SideBySide(file);
        Assert.Equal("@@ -1,4 +1,5 @@", rows[0].Header);
        string Cell(DiffLine? line) => line?.Text ?? "·";
        Assert.Equal(["a|a", "b|B", "c|C", "·|X", "d|d"], rows.Skip(1).Select(r => Cell(r.Left) + "|" + Cell(r.Right)));
    }

    [Fact]
    public void Two_versions_of_one_artifact_are_one_file_named_as_given_and_none_when_equal()
    {
        var artifact = new Artifact
        {
            Thread = Guid.NewGuid(),
            Id = "pricing",
            Title = "Pricing",
            Kind = ArtifactKind.Html,
            Created = DateTimeOffset.UnixEpoch,
            Versions = [new ArtifactVersion { Number = 1, Timestamp = DateTimeOffset.UnixEpoch, Author = ArtifactAuthor.You }],
        };
        var v1 = new ArtifactSnapshot(artifact, artifact.Versions[0], "<p>1</p>", null);
        var v2 = v1 with { Text = "<p>2</p>" };
        Assert.Equal("pricing.html", Assert.Single(ArtifactDiff.Between(v1, v2, "pricing.html")).Path);
        Assert.Empty(ArtifactDiff.Between(v1, v1, "pricing.html"));
    }
}
