using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Tests.Git;

// Source: DroppyCode/Services/Git/FileChangeSummary+Diff.swift (B2 1fbf81e), :3-18.
public sealed class FileChangeSummaryDiffTests
{
    [Fact]
    [Trait("Req", "PR-GIT-092")] // Services/Git/FileChangeSummary+Diff.swift:5-17
    public void Create_keeps_every_diff_file_in_order_and_sums_all_counts()
    {
        var files = new ValueList<DiffFile>([
            new("src/one.cs", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty,
                reportedAdditions: 2_000_000, reportedDeletions: 3),
            new("assets/logo.bin", DiffFile.Change.Modified, true, ValueList<DiffHunk>.Empty,
                reportedAdditions: 0, reportedDeletions: 0),
            new("src/one.cs", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty,
                reportedAdditions: 5, reportedDeletions: 7)
        ]);

        var summary = FileChangeSummaryDiff.Create(files);

        Assert.Equal(new[] { "src/one.cs", "assets/logo.bin", "src/one.cs" }, summary.Files.Select(file => file.Path));
        Assert.Equal(new long[] { 2_000_000, 0, 5 }, summary.Files.Select(file => file.Additions));
        Assert.Equal(new long[] { 3, 0, 7 }, summary.Files.Select(file => file.Deletions));
        Assert.Equal(new[] { false, true, false }, summary.Files.Select(file => file.IsBinary));
        Assert.Equal(2_000_005L, summary.Additions);
        Assert.Equal(10L, summary.Deletions);
    }
}
