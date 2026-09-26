using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class SimpleDiffTests
{
    [Fact]
    [Trait("Req", "PR-STORE-112")] // Text.swift:117-165
    public void One_hunk_preserves_three_old_context_lines_and_removals_before_additions()
    {
        var stats = SimpleDiff.Unified("a\nb\nc\nd\nold\ne\nf\ng\nh", "a\nb\nc\nd\nnew1\nnew2\ne\nf\ng\nh");
        Assert.Equal("@@ -2,7 +2,8 @@\n b\n c\n d\n-old\n+new1\n+new2\n e\n f\n g", stats.Diff);
        Assert.Equal(2, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Theory]
    [Trait("Req", "PR-STORE-112")] // Text.swift:105-136
    [Trait("Req", "PR-STORE-175")] // Text.swift:113-115
    [InlineData("", "", null, 0, 0)]
    [InlineData("", "a", "@@ -0,0 +1,1 @@\n+a", 1, 0)]
    [InlineData("a", "", "@@ -1,1 +0,0 @@\n-a", 0, 1)]
    [InlineData("same", "same", null, 0, 0)]
    [InlineData("a\n", "b\n", "@@ -1,2 +1,2 @@\n-a\n+b\n ", 1, 1)]
    [InlineData("a\r\nb\r\n", "a\r\nc\r\n", "@@ -1,3 +1,3 @@\n a\r\n-b\r\n+c\r\n ", 1, 1)]
    public void Empty_identical_and_CRLF_inputs_give_the_B3_exact_diff(string oldText, string newText,
        string? expected, int additions, int deletions)
    {
        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(expected, stats.Diff);
        Assert.Equal(additions, stats.Additions);
        Assert.Equal(deletions, stats.Deletions);
    }

    [Theory]
    [Trait("Req", "PR-STORE-110")] // Text.swift:91-103
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

    [Fact]
    [Trait("Req", "PR-STORE-110")] // Text.swift:91-103
    public void Limit_is_bytes_and_over_limit_counts_still_trim_common_prefix_and_suffix()
    {
        var oldText = "head\n" + new string('\U000000E9', 100000) + "\ntail"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        var stats = SimpleDiff.Unified(oldText, "head\nreplacement\ntail");
        Assert.Null(stats.Diff);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Fact] // Swift String equality uses canonical equivalence.
    [Trait("Req", "PR-STORE-112")] // Text.swift:105-112, :196-198 (SwiftString.Equal)
    public void Canonically_equivalent_lines_are_context_in_their_original_old_spelling()
    {
        var stats = SimpleDiff.Unified("\U000000E9\nold", "e\U00000301\nnew"); // U+00E9 precomposed old side; e + U+0301 combining acute new side
        Assert.Equal("@@ -1,2 +1,2 @@\n \U000000E9\n-old\n+new", stats.Diff);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Fact] // Context lines are rendered from OLD lines (the op walk prefers oldLines[i]); the trailing
    // context in this fixture keeps the old side's precomposed U+00E9 (e with acute), never the
    // canonically equivalent new side's spelling (U+0065 + U+0301 combining acute accent).
    [Trait("Req", "PR-STORE-112")] // Text.swift:117-136
    public void Trailing_context_is_taken_from_the_old_texts_spelling()
    {
        var stats = SimpleDiff.Unified("old\n\U000000E9", "new\ne\U00000301");
        Assert.Equal("@@ -1,2 +1,2 @@\n-old\n+new\n \U000000E9", stats.Diff);
        Assert.Equal(1, stats.Additions);
        Assert.Equal(1, stats.Deletions);
    }

    [Theory] // ED-199 oracle O-67: where CollectionDifference has more than one minimal edit script,
    // SwiftDifference (the ported swift-6.4.0-RELEASE linear-space Myers) always shrinks a common
    // prefix first, so it keeps the FIRST "a" as context and removes/inserts the second - true under
    // both the pre- and post-6.3 stdlib algorithms, so this fixture needs no Oracle trait.
    [Trait("Req", "PR-STORE-112")] // Text.swift:105-115
    [InlineData("a\na", "a", "@@ -1,2 +1,1 @@\n a\n-a", 0, 1)]
    [InlineData("a", "a\na", "@@ -1,1 +1,2 @@\n a\n+a", 1, 0)]
    public void Tie_break_fixture_keeps_the_first_of_two_identical_lines_as_context(
        string oldText, string newText, string expected, int additions, int deletions)
    {
        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(expected, stats.Diff);
        Assert.Equal(additions, stats.Additions);
        Assert.Equal(deletions, stats.Deletions);
    }

    [Theory] // Text.swift:154-156: a change starts a new hunk unless it is at most 2*contextLines+1 = 7
    // op-positions past the previous change in its group. Two single-line replacements separated by six
    // unchanged lines merge into one hunk (op gap 7); separated by seven, they split into two.
    [Trait("Req", "PR-STORE-112")] // Text.swift:138-164
    [InlineData(6, "@@ -1,8 +1,8 @@\n-r0\n+s0\n m1\n m2\n m3\n m4\n m5\n m6\n-r1\n+s1")]
    [InlineData(7, "@@ -1,4 +1,4 @@\n-r0\n+s0\n m1\n m2\n m3\n@@ -6,4 +6,4 @@\n m5\n m6\n m7\n-r1\n+s1")]
    public void A_gap_of_six_unchanged_lines_merges_but_seven_splits_into_two_hunks(int gap, string expected)
    {
        var unchanged = Enumerable.Range(1, gap).Select(n => $"m{n}").ToArray();
        var oldText = string.Join("\n", new[] { "r0" }.Concat(unchanged).Append("r1"));
        var newText = string.Join("\n", new[] { "s0" }.Concat(unchanged).Append("s1"));

        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(expected, stats.Diff);
        Assert.Equal(2, stats.Additions);
        Assert.Equal(2, stats.Deletions);
    }

    [Fact] // Two change runs far enough apart (op gap 11, past the 7-op merge threshold) form separate
    // hunks, each with its own independent old/new start (Text.swift:146-147): the second hunk's new
    // start (10) differs from its old start (9) by the net line the first hunk added. Additions/
    // deletions total across both hunks (scattered edits), not just the last one.
    [Trait("Req", "PR-STORE-112")] // Text.swift:117-164
    public void Multiple_scattered_hunks_have_independent_old_and_new_starts_and_exact_totals()
    {
        var unchanged = Enumerable.Range(1, 10).Select(n => $"k{n}").ToArray();
        var oldText = string.Join("\n", new[] { "A0" }.Concat(unchanged).Append("A1"));
        var newText = string.Join("\n", new[] { "B0a", "B0b" }.Concat(unchanged).Append("B1"));

        var stats = SimpleDiff.Unified(oldText, newText);
        Assert.Equal(
            "@@ -1,4 +1,5 @@\n-A0\n+B0a\n+B0b\n k1\n k2\n k3\n" +
            "@@ -9,4 +10,4 @@\n k8\n k9\n k10\n-A1\n+B1",
            stats.Diff);
        Assert.Equal(3, stats.Additions);
        Assert.Equal(2, stats.Deletions);
    }

    [Theory] // Text.swift:92 (over the 200000-byte cap only) bounds suffix against BOTH sides' remaining
    // length so prefix and suffix cannot overlap; a shared oversized trailing line pushes both texts over
    // the cap without changing which lines are common. This exercises the over-cap head/tail-trim branch
    // specifically, not the exact-diff path (contrast Tie_break_fixture_keeps_the_first_of_two_identical_lines_as_context).
    [Trait("Req", "PR-STORE-110")] // Text.swift:91-103
    [InlineData("a\na", "a", 0, 1)]
    [InlineData("a", "a\na", 1, 0)]
    public void Over_cap_prefix_and_suffix_candidates_stop_at_the_shorter_side(
        string oldLines, string newLines, int additions, int deletions)
    {
        var pad = new string('z', SimpleDiff.DiffLimit + 1);
        var stats = SimpleDiff.Unified(oldLines + "\n" + pad, newLines + "\n" + pad);
        Assert.Null(stats.Diff);
        Assert.Equal(additions, stats.Additions);
        Assert.Equal(deletions, stats.Deletions);
    }
}
