using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Tests.Git;

// Source: DroppyCode/Services/Git/DiffParser.swift (B2 1fbf81e), :1-167.
public sealed class DiffParserTests
{
    [Fact]
    [Trait("Req", "PR-GIT-083")] // Services/Git/DiffParser.swift:71-108
    public void Headers_open_files_and_set_change_path_binary_and_hunks()
    {
        var patch = "ignored\ndiff --git a/one b/one\nnew file mode 100644\n--- /dev/null\n+++ b/one\n@@ -1 +1 @@\n-old\n+new\ndiff --git a/old b/second\nrename from old\nrename to second\nBinary files a/old and b/second differ\ndiff --git a/third b/third\ndeleted file mode 100644\n+++ /dev/null\nGIT binary patch";
        var files = DiffParser.Parse(patch);
        Assert.Equal(3, files.Count);
        Assert.Equal(DiffFile.Change.Added, files[0].FileChange);
        Assert.Equal("one", files[0].Path);
        Assert.Single(files[0].Hunks);
        Assert.Equal(DiffFile.Change.Renamed, files[1].FileChange);
        Assert.Equal("old", files[1].OldPath);
        Assert.Equal("second", files[1].Path);
        Assert.True(files[1].IsBinary);
        Assert.Equal(DiffFile.Change.Deleted, files[2].FileChange);
        Assert.True(files[2].IsBinary);
    }

    [Fact]
    [Trait("Req", "PR-GIT-083")] // Services/Git/DiffParser.swift:80-108; port_impacts.md G11
    public void Header_like_lines_inside_a_hunk_are_body_lines_except_new_file_and_hunk_headers()
    {
        var files = DiffParser.Parse("diff --git a/a b/a\n--- a/old\n@@ -1 +1 @@\n--- /dev/null\n+++ /dev/null\nrename from ignored\n@@ -4 +5 @@\n+yes");
        var file = Assert.Single(files);
        Assert.Equal(DiffFile.Change.Modified, file.FileChange);
        Assert.Null(file.OldPath); // The --- arm never sets oldPath, even before a hunk.
        Assert.Equal("a", file.Path);
        Assert.Equal(2, file.Hunks.Count);
        Assert.Equal(DiffLine.Kind.Deletion, file.Hunks[0].Lines[0].LineKind);
        Assert.Equal(DiffLine.Kind.Addition, file.Hunks[0].Lines[1].LineKind);
    }

    [Fact]
    [Trait("Req", "PR-GIT-084")] // Services/Git/DiffParser.swift:56-58,100-125
    public void Lines_use_a_shared_id_counter_and_number_only_their_own_side()
    {
        var file = Assert.Single(DiffParser.Parse("diff --git a/a b/a\n@@ -3,2 +8,3 @@\n context\n-old\n+new\n\\ No newline at end of file\n\n@@ -20 +30 @@\n+later\n"));
        Assert.Equal(new long[] { 1, 7 }, file.Hunks.Select(h => h.Id));
        var first = file.Hunks[0].Lines;
        Assert.Equal(new long[] { 2, 3, 4, 5, 6 }, first.Select(line => line.Id));
        Assert.Equal(new[] { DiffLine.Kind.Context, DiffLine.Kind.Deletion, DiffLine.Kind.Addition, DiffLine.Kind.Note, DiffLine.Kind.Context }, first.Select(line => line.LineKind));
        Assert.Equal(new long?[] { 3, 4, null, null, 5 }, first.Select(line => line.OldNumber));
        Assert.Equal(new long?[] { 8, null, 9, null, 10 }, first.Select(line => line.NewNumber));
        Assert.Equal(new[] { "context", "old", "new", "No newline at end of file", "" }, first.Select(line => line.Text));
        Assert.Equal(8L, Assert.Single(file.Hunks[1].Lines).Id); // The final empty line was skipped after incrementing the ID.
    }

    [Fact]
    [Trait("Req", "PR-GIT-085")] // Services/Git/DiffParser.swift:139-154
    public void Header_uses_last_b_marker_and_prefix_stripping_only_on_the_fallback_and_plus_line()
    {
        var files = DiffParser.Parse("diff --git a/a b/path b/final\ndiff --git \"a/quoted\"\ndiff --git a/fallback\n+++ \"b/replaced\"");
        Assert.Equal(new[] { "final", "quoted", "replaced" }, files.Select(file => file.Path));
        Assert.Equal("a/quoted", Assert.Single(DiffParser.Parse("diff --git a/x b/a/quoted")).Path);
    }

    [Fact]
    [Trait("Req", "PR-GIT-086")] // Services/Git/DiffParser.swift:156-166
    public void Hunk_starts_take_first_signed_token_and_default_missing_or_bad_numbers_to_one()
    {
        var file = Assert.Single(DiffParser.Parse("diff --git a/a b/a\n@@ -bad,2 +7,3 @@\n-x\n+y\n@@ no-numbers @@\n-z\n+w"));
        Assert.Equal(1L, file.Hunks[0].Lines[0].OldNumber);
        Assert.Equal(7L, file.Hunks[0].Lines[1].NewNumber);
        Assert.Equal(1L, file.Hunks[1].Lines[0].OldNumber);
        Assert.Equal(1L, file.Hunks[1].Lines[1].NewNumber);
    }

    [Fact]
    [Trait("Req", "PR-GIT-087")] // Services/Git/DiffParser.swift:131-137
    public void ParseHunks_adds_a_header_when_needed_and_always_restores_the_known_path()
    {
        var bare = DiffParser.ParseHunks("@@ -2 +4 @@\n+x", "known/path.cs");
        Assert.Equal("known/path.cs", bare.Path);
        Assert.Equal(4L, Assert.Single(Assert.Single(bare.Hunks).Lines).NewNumber);
        var claimed = DiffParser.ParseHunks("diff --git a/other b/other\n+++ b/other\n@@ -1 +1 @@\n+yes", "known/path.cs");
        Assert.Equal("known/path.cs", claimed.Path);
        Assert.Single(claimed.Hunks);
        var fallback = DiffParser.ParseHunks("diff --git", "known/path.cs");
        Assert.Equal("known/path.cs", fallback.Path);
        Assert.Equal(DiffFile.Change.Modified, fallback.FileChange);
        Assert.False(fallback.IsBinary);
        Assert.Empty(fallback.Hunks);
    }

    [Fact]
    [Trait("Req", "PR-GIT-088")] // Services/Git/DiffParser.swift:11-26
    public void File_counts_prefer_reported_values_and_labels_come_from_its_path()
    {
        var parsed = Assert.Single(DiffParser.Parse("diff --git a/src/file.cs b/src/file.cs\n@@ -1 +1 @@\n-old\n+new"));
        Assert.Equal(1L, parsed.Additions);
        Assert.Equal(1L, parsed.Deletions);
        Assert.Equal("src/file.cs", parsed.Id);
        Assert.Equal("file.cs", parsed.Name);
        Assert.Equal("src/", parsed.Directory);
        var reported = parsed with { ReportedAdditions = 0, ReportedDeletions = 7 };
        Assert.Equal(0L, reported.Additions);
        Assert.Equal(7L, reported.Deletions);
        Assert.Equal("", (parsed with { Path = "solo" }).Directory);
        Assert.Equal("file.cs", SwiftPath.LastPathComponent(parsed.Path));
    }
}
