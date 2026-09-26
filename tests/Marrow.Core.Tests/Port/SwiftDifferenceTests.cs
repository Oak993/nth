using System.Globalization;
using System.Runtime.CompilerServices;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// swift-6.4.0-RELEASE stdlib/public/core/Diffing.swift's `_linearSpaceMyers`, ported at
// src/Marrow.Core/Port/SwiftDifference.cs. No PR/UI requirement names this port-only foundation file, so
// every test carries a Decision or Oracle trait per R-TEST-1, following SwiftStringTests.cs. decisions_draft.md
// section E3 is the ENGINEERING DECISION (proposed ED-193, not yet recorded) to port the swift-6.3-RELEASE and
// swift-6.4.0-RELEASE linear-space tie-break, the one current macOS (26.4+, 27) runs; a fixture whose expected
// offsets depend on that tie carries [Trait("Oracle", "O-67")] instead, per the draft's own instruction.
public sealed class SwiftDifferenceTests
{
    private static bool Eq(string a, string b) => a == b;

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Both_sequences_empty_gives_no_changes()
    {
        var result = SwiftDifference.Difference<string>([], [], Eq);
        Assert.Empty(result.Removals);
        Assert.Empty(result.Insertions);
    }

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Emptying_a_sequence_removes_every_element_by_offset()
    {
        var result = SwiftDifference.Difference<string>(["a", "b", "c"], [], Eq);
        Assert.Equal([0, 1, 2], result.Removals.Select(r => r.Offset));
        Assert.Equal(["a", "b", "c"], result.Removals.Select(r => r.Element));
        Assert.Empty(result.Insertions);
    }

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Filling_an_empty_sequence_inserts_every_element_by_offset()
    {
        var result = SwiftDifference.Difference<string>([], ["a", "b", "c"], Eq);
        Assert.Empty(result.Removals);
        Assert.Equal([0, 1, 2], result.Insertions.Select(i => i.Offset));
        Assert.Equal(["a", "b", "c"], result.Insertions.Select(i => i.Element));
    }

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Identical_sequences_give_no_changes()
    {
        var result = SwiftDifference.Difference<string>(["a", "b", "c"], ["a", "b", "c"], Eq);
        Assert.Empty(result.Removals);
        Assert.Empty(result.Insertions);
    }

    // decisions_draft.md E3's own fixture: "a\na" -> "a" removes the second "a" (offset 1), and this row is
    // "same" between the swift-6.2-RELEASE and swift-6.3+/6.4.0-RELEASE tie-breaks, so it carries no Oracle
    // trait.
    [Fact]
    [Trait("Decision", "ED-193")]
    public void Aa_to_a_removes_offset_1_not_offset_0()
    {
        var result = SwiftDifference.Difference<string>(["a", "a"], ["a"], Eq);
        Assert.Equal([1], result.Removals.Select(r => r.Offset));
        Assert.Empty(result.Insertions);
    }

    // decisions_draft.md E3: "a\nb" -> "b\na" is also "same" between the two tie-breaks: remove old[0]="a",
    // keep "b" as context, insert new[1]="a".
    [Fact]
    [Trait("Decision", "ED-193")]
    public void Ab_to_ba_removes_offset_0_and_inserts_offset_1()
    {
        var result = SwiftDifference.Difference<string>(["a", "b"], ["b", "a"], Eq);
        Assert.Equal([0], result.Removals.Select(r => r.Offset));
        Assert.Equal([1], result.Insertions.Select(i => i.Offset));
    }

    // decisions_draft.md E3: "a" -> "b\na\na" is the row whose 6.3+ column differs from 6.2's: 6.2 gives +b,
    // context "a", +a (i0, i2); the linear-space Myers ported here gives +b, +a, context "a" (i0, i1). The
    // draft says this row "still needs a run of the real stdlib to confirm it", so it is UNVERIFIED (O-67) and
    // carries no Req trait.
    [Fact]
    [Trait("Decision", "ED-193")]
    [Trait("Oracle", "O-67")]
    public void A_to_baa_inserts_offsets_0_and_1_under_the_linear_space_tie_break()
    {
        var result = SwiftDifference.Difference<string>(["a"], ["b", "a", "a"], Eq);
        Assert.Empty(result.Removals);
        Assert.Equal([0, 1], result.Insertions.Select(i => i.Offset));
    }

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Custom_comparer_is_used_case_insensitively()
    {
        var caseInsensitive = SwiftDifference.Difference<string>(["A", "b"], ["a", "B"], StringComparer.OrdinalIgnoreCase.Equals);
        Assert.Empty(caseInsensitive.Removals);
        Assert.Empty(caseInsensitive.Insertions);

        // The negative control (R-TEST-4): swap in ordinal equality and every element counts as different.
        var ordinal = SwiftDifference.Difference<string>(["A", "b"], ["a", "B"], (x, y) => string.Equals(x, y, StringComparison.Ordinal));
        Assert.Equal(2, ordinal.Removals.Count);
        Assert.Equal(2, ordinal.Insertions.Count);
    }

    [Fact]
    [Trait("Decision", "ED-193")]
    public void Removals_and_insertions_come_back_sorted_by_offset()
    {
        var result = SwiftDifference.Difference<string>(["a", "b", "c", "d"], ["d", "b"], Eq);
        var removalOffsets = result.Removals.Select(r => r.Offset).ToArray();
        var insertionOffsets = result.Insertions.Select(i => i.Offset).ToArray();
        Assert.Equal(removalOffsets.OrderBy(o => o), removalOffsets);
        Assert.Equal(insertionOffsets.OrderBy(o => o), insertionOffsets);
        Assert.NotEmpty(removalOffsets);
        Assert.NotEmpty(insertionOffsets);
    }

    // decisions_draft.md E3: over all 14,641 pairs of sequences of length 0 to 4 over {a,b,c}, checked against
    // a COPY of the independent JavaScript oracle diffsim.js's `linear` function (Fixtures/
    // generate_swiftdifference_exhaustive.cjs; read and hand-checked line for line against
    // Diffing-swift-6.4.0-RELEASE.swift while implementing SwiftDifference.cs, with no disagreement found).
    // The generator also confirmed every oracle result is a valid, size-minimal edit script; this test only
    // checks that the port picks the same offsets the oracle does.
    [Fact]
    [Trait("Decision", "ED-193")]
    [Trait("Oracle", "O-67")]
    public void Matches_the_linear_space_oracle_over_every_pair_up_to_length_4()
    {
        var lines = File.ReadAllLines(FixturePath());
        Assert.Equal(14_641, lines.Length);

        var mismatches = new List<string>();
        foreach (var line in lines)
        {
            var fields = line.Split('|');
            var old = fields[0].Select(c => c.ToString()).ToArray();
            var @new = fields[1].Select(c => c.ToString()).ToArray();
            var expectedRemovals = ParseOffsets(fields[2]);
            var expectedInsertions = ParseOffsets(fields[3]);

            var result = SwiftDifference.Difference<string>(old, @new, Eq);
            var actualRemovals = result.Removals.Select(r => r.Offset).ToArray();
            var actualInsertions = result.Insertions.Select(i => i.Offset).ToArray();

            if (!actualRemovals.SequenceEqual(expectedRemovals) || !actualInsertions.SequenceEqual(expectedInsertions))
            {
                mismatches.Add(
                    $"{fields[0]}->{fields[1]}: expected r=[{fields[2]}] i=[{fields[3]}], " +
                    $"got r=[{string.Join(',', actualRemovals)}] i=[{string.Join(',', actualInsertions)}]");
            }
        }
        Assert.Empty(mismatches);
    }

    private static int[] ParseOffsets(string field) =>
        field.Length == 0 ? [] : Array.ConvertAll(field.Split(','), s => int.Parse(s, CultureInfo.InvariantCulture));

    /// <summary>This file is tests/Marrow.Core.Tests/Port/SwiftDifferenceTests.cs.</summary>
    private static string FixturePath([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "Fixtures", "SwiftDifferenceExhaustive.txt");
}
