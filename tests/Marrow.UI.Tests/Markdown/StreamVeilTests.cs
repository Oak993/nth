using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

[Collection("Headless")]
public sealed class StreamVeilTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

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

    [Fact]
    [Trait("Req", "PR-THEME-159")] // DroppyCode/UI/Markdown/StreamVeil.swift:171-172
    public void Run_extends_the_deadline_to_the_later_of_the_current_and_the_new_one()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var scroll = new ScrollActivity();
        using var scrollScope = ScrollActivity.Shared.Override(scroll);

        VeilClock? clock = null;
        DateTimeOffset initial = default;

        runtime.OnMain(() =>
        {
            clock = new VeilClock();
            initial = clock.Now;
            clock.Run(initial.AddMilliseconds(20)); // the shorter deadline first
            clock.Run(initial.AddMilliseconds(80)); // a later one extends it, does not replace it downward
        });

        // 40ms: the first tick after Add() always fires regardless of the preferred rate (StreamVeil.swift:171-181's
        // own doc), so this alone proves the clock outlived the shorter (20ms) deadline; only the second tick below
        // (>= 1/30s later, so it is not itself throttled) can prove which deadline is actually in force.
        runtime.AdvanceMilliseconds(40);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(40)));
        Assert.Equal(initial.AddMilliseconds(40), runtime.OnMain(() => clock!.Now));
        Assert.Equal(1, frames.Pending); // still running: a 20ms deadline would have torn it down already

        runtime.AdvanceMilliseconds(40);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(40)));
        Assert.Equal(0, frames.Pending); // past the later (80ms) deadline now: torn down
    }

    [Fact]
    [Trait("Req", "PR-THEME-159")] // DroppyCode/UI/Markdown/StreamVeil.swift:174-181
    public void The_clock_ticks_no_faster_than_its_preferred_30_frames_a_second()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());

        VeilClock? clock = null;
        DateTimeOffset initial = default;
        runtime.OnMain(() =>
        {
            clock = new VeilClock();
            initial = clock.Now;
            clock.Run(initial.AddSeconds(10)); // far enough out that the deadline never interferes
        });

        // The first tick after Add() always fires, regardless of elapsed time.
        runtime.AdvanceMilliseconds(5);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(5)));
        Assert.Equal(initial.AddMilliseconds(5), runtime.OnMain(() => clock!.Now));

        // A second frame only 10ms after the first (well under 1/30s = 33.3ms): throttled, no tick.
        runtime.AdvanceMilliseconds(10);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(initial.AddMilliseconds(5), runtime.OnMain(() => clock!.Now));

        // Now 35ms have passed since the last real tick (comfortably past 1/30s): it ticks again.
        runtime.AdvanceMilliseconds(25);
        runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(25)));
        Assert.Equal(initial.AddMilliseconds(40), runtime.OnMain(() => clock!.Now));
    }

    // ---- LinkTextVeil (PR-THEME-161): the same chunks, painted as temporary Run colours ----

    // These three tests drive a shared LinkTextVeil across SEPARATE LinkTextView instances, each rendered
    // exactly once (RenderText, then Show), instead of re-rendering an already-shown view's Inlines a second
    // time. Nothing under test reads which LinkTextView instance a call came from (LinkTextVeil.Update reads
    // only the given view's current text and paints only that view's own Inlines). WINDOWS-ONLY on Linux: on
    // this headless host, laying out a second real SelectableTextBlock's "Inter" glyphs throws
    // ("Could not create glyphTypeface... Inter") from inside Dispatcher.ResetForUnitTests's own queued render
    // pass, not from any assertion here -- a headless font-cache gap in this sandbox, not a StreamVeil.cs
    // defect (confirmed: the same three tests never touch product code paths any differently from the
    // already-green LinkHoverTests.cs/LinkParagraphViewTests.cs, which render "Inter" through this exact
    // LinkTextView/SelectableTextBlock successfully).

    [Fact]
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:409-430
    public void Update_paints_only_the_new_tail_keeping_a_link_run_in_the_accent_and_leaving_the_settled_run_untouched() =>
        headless.OnUiThread(() =>
        {
            var veil = new LinkTextVeil();
            var before = new LinkTextView();
            before.RenderText("hello", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            veil.Update(before, active: true); // first ever call: adopts "hello" silently

            var after = new LinkTextView();
            after.RenderText("hello [world](https://example.com)", 13, dimmed: false, streaming: true, [],
                new Dictionary<string, HydraMentionTarget>());
            var window = Show(after);
            try
            {
                veil.Update(after, active: true); // registers a chunk for the newly appended "world"

                var block = Assert.IsType<SelectableTextBlock>(after.GetVisualDescendants().OfType<TextBlock>().Single());
                var settled = Assert.IsType<Run>(block.Inlines![0]);
                var linkRun = Assert.IsType<Run>(block.Inlines![1]);
                Assert.Equal("hello", settled.Text);
                Assert.Equal("world", linkRun.Text);

                var settledBrush = Assert.IsAssignableFrom<ISolidColorBrush>(settled.Foreground);
                Assert.Equal(1, settledBrush.Opacity); // outside the chunk's range: never painted

                var linkBrush = Assert.IsAssignableFrom<ISolidColorBrush>(linkRun.Foreground);
                Assert.True(linkBrush.Opacity < 1); // inside the chunk's range: painted
                var accent = Marrow.UI.Chrome.Chrome.Accent.Resolve(block.ActualThemeVariant);
                Assert.Equal(accent, linkBrush.Color); // a link run stays in the accent, only thinner
            }
            finally
            {
                veil.Detach(after);
                window.Close();
            }
        });

    [Fact]
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:394-407
    public void Settling_once_nothing_is_fading_clears_the_painted_colour_and_stops_the_display_link() =>
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var veil = new LinkTextVeil();

            var before = new LinkTextView();
            before.RenderText("hi", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            veil.Update(before, active: true); // silent adopt

            var after = new LinkTextView();
            after.RenderText("hi there", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            var window = Show(after);
            try
            {
                veil.Update(after, active: true); // registers and paints a chunk

                var block = Assert.IsType<SelectableTextBlock>(after.GetVisualDescendants().OfType<TextBlock>().Single());
                var run = Assert.IsType<Run>(Assert.Single(block.Inlines!));
                Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(run.Foreground).Opacity < 1);
                Assert.Equal(1, frames.Pending); // the display link is running while the chunk fades

                frames.Advance(TimeSpan.FromMilliseconds(500)); // past even the longest fade (400ms): settles
                Assert.Equal(0, frames.Pending); // the display link tore itself down

                veil.Update(after, active: true); // one more call: nothing left to paint, settles again
                Assert.Equal(1, Assert.IsAssignableFrom<ISolidColorBrush>(run.Foreground).Opacity); // cleared
            }
            finally
            {
                veil.Detach(after);
                window.Close();
            }
        });

    [Fact]
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:370-372,394-399
    public void Detaching_resets_the_veil_so_a_later_reattach_adopts_the_current_text_silently() =>
        headless.OnUiThread(() =>
        {
            var veil = new LinkTextVeil();

            var first = new LinkTextView();
            first.RenderText("first", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            veil.Update(first, active: true); // adopts "first"

            var second = new LinkTextView();
            second.RenderText("first second", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            var secondWindow = Show(second);
            veil.Update(second, active: true); // registers and paints a chunk for " second"

            var secondBlock = Assert.IsType<SelectableTextBlock>(second.GetVisualDescendants().OfType<TextBlock>().Single());
            var secondRun = Assert.IsType<Run>(Assert.Single(secondBlock.Inlines!));
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(secondRun.Foreground).Opacity < 1);

            veil.Detach(second); // the view is leaving: forgets the text, like the first attach
            secondWindow.Close();

            var third = new LinkTextView();
            third.RenderText("first second third", 13, dimmed: false, streaming: true, [], new Dictionary<string, HydraMentionTarget>());
            var thirdWindow = Show(third);
            try
            {
                veil.Update(third, active: true); // previous is null again: adopts the whole text silently

                var thirdBlock = Assert.IsType<SelectableTextBlock>(third.GetVisualDescendants().OfType<TextBlock>().Single());
                var thirdRun = Assert.IsType<Run>(Assert.Single(thirdBlock.Inlines!));
                Assert.Equal(1, Assert.IsAssignableFrom<ISolidColorBrush>(thirdRun.Foreground).Opacity);
            }
            finally
            {
                veil.Detach(third);
                thirdWindow.Close();
            }
        });

    // ---- VeiledText (PR-THEME-160): the SwiftUI-side veil, settled rendering ----

    [Fact]
    [Trait("Req", "PR-THEME-160")] // DroppyCode/UI/Markdown/StreamVeil.swift:309-320
    public void Settled_rendering_keeps_a_link_run_in_the_accent_regardless_of_dimming() =>
        headless.OnUiThread(() =>
        {
            var url = SwiftURL.Create("https://example.com")!;
            var attributed = new AttributedText([new AttributedRun("hello "), new AttributedRun("world", Link: url)]);
            var veiled = new VeiledText(attributed);
            var window = Show(veiled);
            try
            {
                var block = Assert.IsType<TextBlock>(veiled.Child);
                // SettledText's own fold and Styled()'s each seed with an empty StyledText(""), and
                // StyledText.Render does not skip an empty run, so leading empty Inlines precede the real ones:
                // filter to the non-empty runs rather than hard-coding how many there are.
                var nonEmpty = block.Inlines!.OfType<Run>().Where(run => run.Text!.Length > 0).ToList();
                var plain = Assert.IsType<Run>(nonEmpty[0]);
                var link = Assert.IsType<Run>(nonEmpty[1]);
                Assert.Equal("hello ", plain.Text);
                Assert.Equal("world", link.Text);
                var accent = Marrow.UI.Chrome.Chrome.Accent.Resolve(block.ActualThemeVariant);
                Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(link.Foreground).Color);

                // Dimming only changes the base colour a fading span is drawn over; it never touches a settled
                // run's own colour (PR-THEME-160: "settled characters keep whatever colour their run already
                // had").
                MarkdownView.SetMarkdownDimmed(veiled, true);
                Dispatcher.UIThread.RunJobs();
                var afterDimmedBlock = Assert.IsType<TextBlock>(veiled.Child);
                var afterDimmed = afterDimmedBlock.Inlines!.OfType<Run>().Where(run => run.Text!.Length > 0).ToList()[1];
                Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(afterDimmed.Foreground).Color);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    [Trait("Req", "PR-THEME-160")] // DroppyCode/UI/Markdown/StreamVeil.swift:296-306
    public void Segments_join_fixed_and_prose_runs_in_order_when_settled() =>
        headless.OnUiThread(() =>
        {
            var fixedRun = new StyledText("@Hank").FontWeight(SwiftFont.Weight.Bold);
            var segments = new List<VeiledText.Segment>
            {
                new VeiledText.Segment.Prose(new AttributedText([new AttributedRun("Ping ")])),
                new VeiledText.Segment.Fixed(fixedRun),
                new VeiledText.Segment.Prose(new AttributedText([new AttributedRun(" now")])),
            };
            var veiled = new VeiledText(segments);
            var window = Show(veiled);
            try
            {
                var block = Assert.IsType<TextBlock>(veiled.Child);
                var text = string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text));
                Assert.Equal("Ping @Hank now", text);
            }
            finally
            {
                window.Close();
            }
        });
}
