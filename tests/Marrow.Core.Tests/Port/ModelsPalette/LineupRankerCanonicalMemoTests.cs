using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Tests.Port.ModelsPalette;

// ED-NEW-perf-startup-structure-4: Canonical is a pure function of (id, keepSpeed) that costs about 30 regex calls; the maker resolver and the palette scan the whole boundary
// per model, so its answers are kept, by both inputs, up to a cap, and a kept answer is the computed one.
public sealed class LineupRankerCanonicalMemoTests
{
    private static string[] Ids() => PaletteSourcePack.Default.Baseline.Select(f => f.Id)
        .Concat(["Anthropic/Claude-Sonnet-4-5-20250929", "gpt-5.1-codex-mini-fast", "deepseek-chat-v3-0324:free", "~google/gemini-2.5-pro-preview-05-06", "claude-opus-4-1[1m]", "x-ai/grok-4-fast-thinking", "o3-pro-2025-06-10"]).ToArray();

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-4")] // ED-NEW-perf-startup-structure-4
    public void Every_kept_answer_is_the_computed_one_for_both_speed_modes_first_call_and_repeat()
    {
        foreach (var id in Ids())
            foreach (var stripSpeed in new[] { true, false })
            {
                var computed = LineupRanker.ComputeCanonicalFacts(id, stripSpeed);
                Assert.Equal(computed, LineupRanker.CanonicalFacts(id, stripSpeed));
                Assert.Equal(computed, LineupRanker.CanonicalFacts(id, stripSpeed)); // the repeat comes from the memo
                Assert.Equal(computed.Canon, LineupRanker.Canonical(id, keepSpeed: !stripSpeed));
            }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-4")] // ED-NEW-perf-startup-structure-4
    public void A_repeated_id_is_answered_without_computing_again()
    {
        var memo = new CanonicalMemo(16);
        var computes = 0;
        (string Canon, bool Stripped, string? Date) Compute(string id, bool stripSpeed) { computes++; return LineupRanker.ComputeCanonicalFacts(id, stripSpeed); }
        var first = memo.Get("Vendor/Model-X-20250101", true, Compute);
        for (var i = 0; i < 1000; i++) Assert.Equal(first, memo.Get("Vendor/Model-X-20250101", true, Compute));
        Assert.Equal(1, computes);
        Assert.Equal(1, memo.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-4")] // ED-NEW-perf-startup-structure-4
    public void The_two_speed_modes_of_one_id_are_kept_apart()
    {
        var memo = new CanonicalMemo(16);
        var stripped = memo.Get("model-fast", true, LineupRanker.ComputeCanonicalFacts);
        var kept = memo.Get("model-fast", false, LineupRanker.ComputeCanonicalFacts);
        Assert.Equal("model", stripped.Canon);
        Assert.Equal("model-fast", kept.Canon);
        Assert.Equal(2, memo.Count);
        Assert.Equal(stripped, memo.Get("model-fast", true, (_, _) => throw new InvalidOperationException("kept")));
        Assert.Equal(kept, memo.Get("model-fast", false, (_, _) => throw new InvalidOperationException("kept")));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-4")] // ED-NEW-perf-startup-structure-4
    public void The_memo_stops_growing_at_its_cap_and_an_id_past_it_is_still_answered_correctly()
    {
        var memo = new CanonicalMemo(3);
        var computes = 0;
        (string Canon, bool Stripped, string? Date) Compute(string id, bool stripSpeed) { computes++; return LineupRanker.ComputeCanonicalFacts(id, stripSpeed); }
        for (var i = 0; i < 50; i++) Assert.Equal(LineupRanker.ComputeCanonicalFacts($"Maker/Model-{i}-20250101", true), memo.Get($"Maker/Model-{i}-20250101", true, Compute));
        Assert.Equal(3, memo.Count);
        Assert.Equal(50, computes);
        memo.Get("Maker/Model-0-20250101", true, Compute); // kept: no new compute
        Assert.Equal(50, computes);
        memo.Get("Maker/Model-49-20250101", true, Compute); // past the cap: computed again, as before the memo
        Assert.Equal(51, computes);
        Assert.Equal(3, memo.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-perfstartup-4")] // ED-NEW-perf-startup-structure-4
    public void The_cap_holds_while_many_threads_add_distinct_ids_at_once()
    {
        var memo = new CanonicalMemo(40);
        Parallel.For(0, 400, i => memo.Get($"Maker/Model-{i}", true, LineupRanker.ComputeCanonicalFacts));
        Assert.Equal(40, memo.Count);
    }
}
