using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class SimpleDiffTests
{
    [Fact] // PR-STORE-112
    public void One_hunk_preserves_three_old_context_lines_and_removals_before_additions()
    {
        var stats = SimpleDiff.Unified("a\nb\nc\nd\nold\ne\nf\ng\nh", "a\nb\nc\nd\nnew1\nnew2\ne\nf\ng\nh");
        Assert.Equal("@@ -2,7 +2,8 @@\n b\n c\n d\n-old\n+new1\n+new2\n e\n f\n g", stats.Diff);
        Assert.Equal(2, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Theory] // PR-STORE-112
    [InlineData("", "", "@@ -1,0 +1,0 @@", 0, 0)]
    [InlineData("", "a", "@@ -1,0 +1,1 @@\n+a", 1, 0)]
    [InlineData("a", "", "@@ -1,1 +1,0 @@\n-a", 0, 1)]
    [InlineData("same", "same", "@@ -1,1 +1,1 @@\n same", 0, 0)]
    [InlineData("a\n", "b\n", "@@ -1,2 +1,2 @@\n-a\n+b\n ", 1, 1)]
    [InlineData("a\r\nb\r\n", "a\r\nc\r\n", "@@ -1,3 +1,3 @@\n a\r\n-b\r\n+c\r\n ", 1, 1)]
    public void Empty_identical_and_CRLF_inputs_keep_the_source_hunk_quirks(string oldText, string newText,
        string expected, int additions, int deletions)
    {
        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(expected, stats.Diff);
        Assert.Equal(additions, stats.Additions);
        Assert.Equal(deletions, stats.Deletions);
    }

    [Theory] // PR-STORE-110, PR-STORE-111
    [InlineData(199999, true)]
    [InlineData(200000, true)]
    [InlineData(200001, false)]
    public void Exact_200000_UTF8_input_sum_limits_only_hunk_storage(int size, bool keepsHunk)
    {
        var oldText = "head\n" + new string('a', size - 15) + "\ntail";
        const string newText = "head\n";
        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(2, stats.Deletions);
        Assert.Equal(keepsHunk, stats.Diff is not null);
    }

    [Fact] // PR-STORE-110, PR-STORE-111
    public void Limit_is_bytes_and_over_limit_counts_still_trim_common_prefix_and_suffix()
    {
        var oldText = "head\n" + new string('é', 100000) + "\ntail";
        var stats = SimpleDiff.Unified(oldText, "head\nreplacement\ntail");
        Assert.Null(stats.Diff);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Fact] // PR-STORE-112: Swift String equality uses canonical equivalence.
    public void Canonically_equivalent_lines_are_context_in_their_original_old_spelling()
    {
        var stats = SimpleDiff.Unified("é\nold", "e\u0301\nnew");
        Assert.Equal("@@ -1,2 +1,2 @@\n é\n-old\n+new", stats.Diff);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }
}
