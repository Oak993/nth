using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

public sealed class StreamVeilTests
{
    [Fact]
    [Trait("Req", "PR-THEME-153")] // DroppyCode/UI/Markdown/StreamVeil.swift:4-24
    [Trait("Req", "PR-THEME-157")] // DroppyCode/UI/Markdown/StreamVeil.swift:52-85
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:4-24
    public void Adoption_is_silent_and_a_later_append_uses_grapheme_offsets()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("A\U0001F600e\U00000301", start); // U+1F600 GRINNING FACE; U+0301 COMBINING ACUTE ACCENT
        Assert.Empty(veil.Spans(start));

        veil.Advance("A\U0001F600e\U00000301X", start.AddMilliseconds(10)); // U+1F600 GRINNING FACE; U+0301 COMBINING ACUTE ACCENT

        var span = Assert.Single(veil.Spans(start.AddMilliseconds(10)));
        Assert.Equal(3, span.Range.LowerBound);
        Assert.Equal(4, span.Range.UpperBound);
        Assert.Equal(0, span.Alpha);
    }

    [Fact]
    [Trait("Req", "PR-THEME-154")] // DroppyCode/UI/Markdown/StreamVeil.swift:40-44,73-81
    [Trait("Req", "PR-THEME-156")] // DroppyCode/UI/Markdown/StreamVeil.swift:42,88-95
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:40-44,88-95
    public void First_chunk_uses_the_seed_duration_and_the_1_6_reveal_curve()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        veil.Advance("x", start);

        var span = Assert.Single(veil.Spans(start.AddMilliseconds(200)));
        Assert.Equal(1 - Math.Pow(1 - 0.5, 1.6), span.Alpha, 10);
        Assert.True(veil.IsFading(start.AddMilliseconds(399)));
        Assert.False(veil.IsFading(start.AddMilliseconds(400)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-154")] // DroppyCode/UI/Markdown/StreamVeil.swift:73-81
    public void A_zero_gap_updates_the_cadence_without_changing_an_existing_chunks_duration()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        veil.Advance("a", start);
        veil.Advance("ab", start);

        var beforeFirstSettles = veil.Spans(start.AddMilliseconds(335));
        Assert.Equal(2, beforeFirstSettles.Count);

        var afterSecondSettles = veil.Spans(start.AddMilliseconds(337));
        Assert.Single(afterSecondSettles);
        Assert.Equal(0, afterSecondSettles[0].Range.LowerBound);
        Assert.Equal(1, afterSecondSettles[0].Range.UpperBound);

        Assert.False(veil.IsFading(start.AddMilliseconds(400)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-155")] // DroppyCode/UI/Markdown/StreamVeil.swift:88-95,124-132
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:88-95,124-132
    public void Draining_backlog_can_make_a_surviving_chunk_fainter()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        veil.Advance("a", start);
        veil.Advance("ab", start);
        veil.Advance("abc", start);

        var whileBackedUp = veil.Spans(start.AddMilliseconds(180));
        Assert.Equal(3, whileBackedUp.Count);

        var afterBacklogDrains = veil.Spans(start.AddMilliseconds(190));
        Assert.Equal(2, afterBacklogDrains.Count);

        Assert.True(whileBackedUp[0].Alpha > afterBacklogDrains[0].Alpha);
        Assert.Equal(1 - Math.Pow(1 - 0.585, 1.6), whileBackedUp[0].Alpha, 10);
        Assert.Equal(1 - Math.Pow(1 - 0.475, 1.6), afterBacklogDrains[0].Alpha, 10);
    }

    [Fact]
    [Trait("Req", "PR-THEME-157")] // DroppyCode/UI/Markdown/StreamVeil.swift:52-85
    public void A_rewrite_clips_existing_chunks_and_registers_only_the_changed_tail()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("abc", start);
        veil.Advance("abcdef", start.AddMilliseconds(10));
        veil.Advance("abX", start.AddMilliseconds(20));

        var span = Assert.Single(veil.Spans(start.AddMilliseconds(20)));
        Assert.Equal(2, span.Range.LowerBound);
        Assert.Equal(3, span.Range.UpperBound);
    }

    [Fact]
    [Trait("Req", "PR-THEME-157")] // DroppyCode/UI/Markdown/StreamVeil.swift:52-85
    public void Repeating_unchanged_text_does_not_register_another_chunk()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        veil.Advance("x", start);
        veil.Advance("x", start.AddMilliseconds(10));

        Assert.Single(veil.Spans(start.AddMilliseconds(10)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-157")] // DroppyCode/UI/Markdown/StreamVeil.swift:63-72
    public void A_rewrite_shorter_than_a_chunks_start_drops_that_chunk()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("abcdef", start);
        veil.Advance("abcdefX", start.AddMilliseconds(10));
        veil.Advance("ab", start.AddMilliseconds(20));

        Assert.Empty(veil.Spans(start.AddMilliseconds(20)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-158")] // DroppyCode/UI/Markdown/StreamVeil.swift:102-122
    public void Inactive_step_does_not_feed_text_and_reset_restores_silent_adoption()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        Assert.False(veil.Step(() => "x", active: false, at: start));
        Assert.Empty(veil.Spans(start));

        veil.Reset();
        veil.Advance("already visible", start.AddMilliseconds(10));

        Assert.Empty(veil.Spans(start.AddMilliseconds(10)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-158")] // DroppyCode/UI/Markdown/StreamVeil.swift:102-107,115-122
    public void A_settled_chunk_keeps_its_baseline_until_reset()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;

        veil.Advance("", start);
        veil.Advance("x", start);
        Assert.False(veil.Step(() => "x", active: true, at: start.AddMilliseconds(400)));

        veil.Advance("xy", start.AddMilliseconds(410));
        var nextChunk = Assert.Single(veil.Spans(start.AddMilliseconds(410)));
        Assert.Equal(1, nextChunk.Range.LowerBound);
        Assert.Equal(2, nextChunk.Range.UpperBound);
    }

    [Fact]
    [Trait("Decision", "ED-117")]
    [Trait("Oracle", "O-34")]
    public void The_shared_veil_clock_holds_while_scrolling_and_stops_after_its_deadline()
    {
        // The expected frame-driven timing and scroll gate use ED-117's port choice for unverified
        // SwiftUI and Core Animation behavior (O-34); this test does not verify a Req trait.
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var scroll = new ScrollActivity();
        using var scrollScope = ScrollActivity.Shared.Override(scroll);

        VeilClock? clock = null;
        DateTimeOffset initial = default;
        var timeline = Guid.NewGuid();

        runtime.OnMain(() =>
        {
            clock = new VeilClock();
            initial = clock.Now;
            scroll.SetScrolling(true, timeline);
            clock.Run(initial.AddMilliseconds(20));
        });

        runtime.AdvanceMilliseconds(40);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(16)));

        Assert.Equal(initial, runtime.OnMain(() => clock!.Now));
        Assert.Equal(1, frames.Pending);

        runtime.OnMain(() => scroll.SetScrolling(false, timeline));
        runtime.AdvanceMilliseconds(40);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(16)));

        Assert.True(runtime.OnMain(() => clock!.Now) > initial);
        Assert.Equal(0, frames.Pending);
    }
}
