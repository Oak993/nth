using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/MarkdownView.swift:86-97 (each block of a streaming reply is `MarkdownBlockView(...).transition(Self.transition(for: block))`, a `ForEach`
// over `blocks.enumerated()` with `id: \.offset`, so a block that a flush opens is an insertion), :51-56 (every block but a delegation card leaves with the soft appear, and
// every block arrives with it); DroppyCode/UI/Chrome/Chrome.swift:937-955 (softAppear: insertion `.modifier(active: SoftAppearModifier(isVisible: false), identity: ...)
// .animation(.softAppear)`, which is opacity 0 -> 1 and a 4 point offset -> 0, and :958-961 Animation.softAppear, smooth over 0.32 s); DroppyCode/UI/Markdown/StreamVeil.swift:
// 51-55 (the first call of a veil adopts its text as the baseline without fading it, so the words of a block that is new are not a veil's chunk: what dissolves them in is the
// block's own arrival); OD-0076 (owner ruling, 2026-10-02: nothing on screen changes by a cut).
//
// UI-THEME-047, round 4 of the review. The first batch of a block that a flush OPENS appeared whole, in the mid of a stream and at its end: the veil registers only appends to a
// place that holds text, and the port builds a reply's markdown again for every flush, so a new block's views were never inserted (SwiftTransition.Insert was never called) and
// the arrival of the Swift never ran. Only the block's arrival is under test here; what a flush appends to a block that is already there is StreamVeilFlowTests. Frame by frame
// on the manual frame and time sources.
[Collection("Headless")]
public sealed class MarkdownBlockArrivalTests(HeadlessSession headless)
{
    // The reply's block views, in order: the children of the veiled blocks' stack.
    private static Control[] Blocks(StreamVeilFlowHost host) =>
        Assert.IsType<StackPanel>(Assert.IsType<MarkdownView>(host.Decorator.Child).Child).Children.ToArray();

    private static double OffsetY(Control block) => block.RenderTransform is TranslateTransform translate ? translate.Y : 0;

    // Chrome.swift:958-961: the soft appear is smooth over 0.32 s; the transition's insertion runs it (opacity 0 -> 1, offset 4 -> 0).
    private static double Progress(double milliseconds) => Animation.SoftAppear.Progress(milliseconds / 1000);

    private static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
        // The time of the clock's last tick belongs to the test that ticked it: a tick that lands on the very instant a previous test left (every test's manual time
        // starts again at the same instant, and assigning an equal value tells nobody) would tell no paragraph and no closing view.
        typeof(VeilClock).GetField("now", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, DateTimeOffset.MinValue);
    }

    private static void Tick(TestRuntime runtime, ManualFrameSource frames, double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); StreamVeilFlowHost.Pump(); });
    }

    private static ParagraphTextPresenter ProseRun(Control root) =>
        Assert.Single(Assert.Single(root.GetVisualDescendants().OfType<ProseRunView>()).GetVisualDescendants().OfType<ParagraphTextPresenter>());

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:86-97, :51-56; Chrome.swift:937-961; StreamVeil.swift:51-55; OD-0076
    public void A_block_that_a_flush_opens_dissolves_in_frame_by_frame_while_the_blocks_already_there_stay_whole() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            // What is on screen when the veils attach is the baseline: nothing arrives, nothing ticks.
            var first = Assert.Single(Blocks(host));
            Assert.Equal(1, first.Opacity);
            Assert.Equal(0, frames.Pending);

            // The flush opens a second block. It is on screen at once, at nothing and 4 points low; the block that was there is untouched.
            host.Flush(runtime, "Hello there\n\nWorld begins");
            var blocks = Blocks(host);
            Assert.Equal(2, blocks.Length);
            Assert.Equal(1, blocks[0].Opacity);
            Assert.Equal(0, OffsetY(blocks[0]));
            Assert.Equal(0, blocks[1].Opacity, 0.001);
            Assert.Equal(4, OffsetY(blocks[1]), 0.001);

            // Frame by frame, every 20 ms: opacity and offset follow the soft appear's curve, never going back and never jumping more than a quarter of the way.
            var seen = new List<double> { blocks[1].Opacity };
            for (var at = 20; at <= 360; at += 20)
            {
                Tick(runtime, frames, 20);
                var block = Blocks(host)[1];
                Assert.Equal(Progress(at), block.Opacity, 0.01);
                Assert.Equal(4 * (1 - Progress(at)), OffsetY(block), 0.05);
                Assert.Equal(1, Blocks(host)[0].Opacity); // the first block never dissolved as a whole
                seen.Add(block.Opacity);
            }

            Assert.Equal(seen.OrderBy(value => value), seen);
            Assert.True(seen.Zip(seen.Skip(1)).All(pair => pair.Second - pair.First < 0.25), "no frame jumps a quarter of the way or more");
            Assert.True(seen.Count(value => value is > 0.02 and < 0.98) >= 8, "it took a good many frames, not one");
            var done = Blocks(host)[1];
            Assert.Equal(1, done.Opacity);
            Assert.Equal(0, OffsetY(done), 0.001);
            Tick(runtime, frames, 300); // the " there" chunk of the first block has settled by now too
            Assert.Equal(0, frames.Pending); // and nothing is left ticking
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:86-97 (a block keeps its identity through the next flush, id: \.offset, so its insertion carries on); Chrome.swift:949-955
    public void A_block_that_is_still_arriving_carries_on_from_where_it_was_through_the_next_flushes_and_a_second_new_block_starts_on_its_own() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            host.Flush(runtime, "Hello\n\nWorld");
            Tick(runtime, frames, 100);
            Assert.Equal(Progress(100), Blocks(host)[1].Opacity, 0.01);

            // The next flush builds the whole tree again; the arriving block's view is a new one, but it is the same place: it continues at 0.1 s into its arrival,
            // not at nothing (a restart) and not whole (a snap).
            host.Flush(runtime, "Hello\n\nWorld wide");
            var rebuilt = Blocks(host)[1];
            Assert.Equal(Progress(100), rebuilt.Opacity, 0.01);
            Assert.InRange(rebuilt.Opacity, 0.2, 0.95);
            Assert.Equal(4 * (1 - Progress(100)), OffsetY(rebuilt), 0.05);

            Tick(runtime, frames, 60);
            Assert.Equal(Progress(160), Blocks(host)[1].Opacity, 0.01);

            // A third block opens while the second is part way: it starts at nothing, the second carries on, the first is whole.
            host.Flush(runtime, "Hello\n\nWorld wide\n\nThird");
            var all = Blocks(host);
            Assert.Equal(3, all.Length);
            Assert.Equal(1, all[0].Opacity);
            Assert.Equal(Progress(160), all[1].Opacity, 0.01);
            Assert.Equal(0, all[2].Opacity, 0.001);
            Tick(runtime, frames, 100);
            all = Blocks(host);
            Assert.Equal(Progress(260), all[1].Opacity, 0.01);
            Assert.Equal(Progress(100), all[2].Opacity, 0.01);

            // The second has settled (its soft appear takes about 0.35 s from its arrival), the third is at 0.3 s; then nothing is left.
            Tick(runtime, frames, 200);
            all = Blocks(host);
            Assert.Equal(1, all[1].Opacity);
            Assert.Equal(Progress(300), all[2].Opacity, 0.01);
            Tick(runtime, frames, 1000);
            all = Blocks(host);
            Assert.All(all, block => Assert.Equal(1, block.Opacity));
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // StreamVeil.swift:18-19, :51-55 (what is on screen when a veil attaches is adopted, not dissolved); MarkdownView.swift:86-97 (the first evaluation inserts nothing: the whole stack is the row's own arrival)
    public void The_blocks_on_screen_when_the_reply_first_attaches_and_after_a_reset_do_not_dissolve_in() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "One\n\nTwo\n\n```\nthree\n```");
        try
        {
            Assert.All(Blocks(host), block => Assert.Equal(1, block.Opacity));
            Assert.Equal(0, frames.Pending);

            // A row that scrolls away and back adopts what it shows (MarkdownVeils.Reset): the block that came with the next flush while it was away is on screen at once.
            Assert.NotEqual(0, host.Veils.Arrivals);
            runtime.OnMain(host.Veils.Reset);
            Assert.Equal(0, host.Veils.Arrivals);
            host.Flush(runtime, "One\n\nTwo\n\n```\nthree\n```\n\nFour");
            Assert.Equal(4, Blocks(host).Length);
            Assert.All(Blocks(host), block => Assert.Equal(1, block.Opacity));
            Assert.Equal(0, frames.Pending);

            // The next block after that is an arrival again.
            host.Flush(runtime, "One\n\nTwo\n\n```\nthree\n```\n\nFour\n\nFive");
            Assert.Equal(0, Blocks(host)[4].Opacity, 0.001);
            Tick(runtime, frames, 100);
            Assert.Equal(Progress(100), Blocks(host)[4].Opacity, 0.01);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83, :86-97; Chrome.swift:949-955; OD-0076 (owner-ruled: no cut at the end of a stream)
    public void A_block_that_arrives_with_the_end_of_the_stream_dissolves_in_before_the_reply_is_drawn_as_prose_runs() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            host.Flush(runtime, "Hello there");
            Tick(runtime, frames, 600); // a quiet beat: everything has settled and nothing dissolves
            Assert.Equal(0, frames.Pending);

            // The provider completes the reply with a last batch that opens a new block (ThreadRuntime.swift:3248-3265: the final text and isStreaming false in one change).
            host.Finish(runtime, "Hello there\n\nWorld begins");
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>()); // not yet the prose runs: they would draw the new block whole
            var blocks = Blocks(host);
            Assert.Equal(2, blocks.Length);
            Assert.Equal(1, blocks[0].Opacity);
            Assert.Equal(0, blocks[1].Opacity, 0.001);
            Assert.Equal(4, OffsetY(blocks[1]), 0.001);

            for (var at = 20; at <= 300; at += 20)
            {
                Tick(runtime, frames, 20);
                Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());
                Assert.Equal(Progress(at), Blocks(host)[1].Opacity, 0.01);
            }

            // Past its settle (about 0.352 s): the Swift's prose runs, and nothing of the stream is kept, no place, no text, no clock.
            Tick(runtime, frames, 100);
            Assert.Contains("World begins", ProseRun(host.Decorator).Text.String, StringComparison.Ordinal);
            Assert.Equal(0, host.Veils.Places);
            Assert.Equal(0, host.Veils.Arrivals); // nothing is kept of how its blocks arrived either
            Assert.False(host.Veils.HoldsText);
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83, :86-97; StreamVeil.swift:10-12; OD-0076 (owner-ruled: no cut at the end of a stream)
    public void A_last_batch_that_both_extends_a_block_and_opens_one_waits_for_the_later_of_the_two_before_the_prose_runs() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            // " there" dissolves over 0.40 s (the seed cadence), the new block arrives over about 0.352 s: both start at the completion.
            host.Finish(runtime, "Hello there\n\nWorld begins");
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());
            Assert.Equal(0, Blocks(host)[1].Opacity, 0.001);

            Tick(runtime, frames, 380); // the block's arrival is over, the chunk's fade is not
            Assert.Equal(1, Blocks(host)[1].Opacity, 0.001);
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());

            Tick(runtime, frames, 100);
            Assert.Contains("World begins", ProseRun(host.Decorator).Text.String, StringComparison.Ordinal);
            Assert.Equal(0, host.Veils.Places);
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:86-97 (a block that goes and comes back through the parser re-deriving the reply is a removal then an insertion); Chrome.swift:949-955
    public void A_block_that_leaves_when_the_parser_re_derives_the_reply_shorter_arrives_afresh_when_it_comes_back() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            host.Flush(runtime, "Hello\n\nWorld");
            Tick(runtime, frames, 1000); // it has arrived, long since
            Assert.Equal(1, Blocks(host)[1].Opacity);

            // The reply is re-derived without its second block (a delegation block leaving it, a fence that does not close), then it returns.
            host.Flush(runtime, "Hello");
            Assert.Single(Blocks(host));
            host.Flush(runtime, "Hello\n\nWorld");
            Assert.Equal(0, Blocks(host)[1].Opacity, 0.001);
            Tick(runtime, frames, 100);
            Assert.Equal(Progress(100), Blocks(host)[1].Opacity, 0.01);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83, :86-97; Chrome.swift:949-955; OD-0076 (owner-ruled: no cut at the end of a stream)
    public void A_link_paragraph_that_a_batch_opens_dissolves_in_too_mid_stream_and_at_the_end(bool atTheEnd) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            var text = "Hello\n\nRead [notes](file:///C:/notes.md) now"; // a link with no host: nothing to fetch a favicon for
            if (atTheEnd) host.Finish(runtime, text);
            else host.Flush(runtime, text);
            var blocks = Blocks(host);
            Assert.Equal(2, blocks.Length);
            Assert.Equal(0, blocks[1].Opacity, 0.001);
            Tick(runtime, frames, 100);
            Assert.Equal(Progress(100), Blocks(host)[1].Opacity, 0.01);
            if (atTheEnd) Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());
            Tick(runtime, frames, 400);
            if (atTheEnd) Assert.Contains("Read notes now", ProseRun(host.Decorator).Text.String, StringComparison.Ordinal);
            else Assert.Equal(1, Blocks(host)[1].Opacity);
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83, :86-97; OD-0076 (owner-ruled: no cut at the end of a stream)
    public void A_reply_that_has_only_drawn_a_table_so_far_still_lets_the_block_that_ends_it_dissolve_in() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        const string table = "| left | right |\n|---|---|\n| one | two |";
        var host = new StreamVeilFlowHost(runtime, table);
        try
        {
            Assert.False(host.Veils.HoldsText, "a table keeps no veiled text: no place of this reply holds any");
            host.Finish(runtime, table + "\n\nDone");
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());
            Assert.Equal(0, Blocks(host)[1].Opacity, 0.001);
            Tick(runtime, frames, 100);
            Assert.Equal(Progress(100), Blocks(host)[1].Opacity, 0.01);
            Tick(runtime, frames, 400);
            Assert.Contains("Done", Assert.Single(host.Decorator.GetVisualDescendants().OfType<ProseRunView>()).GetVisualDescendants().OfType<ParagraphTextPresenter>().Single().Text.String, StringComparison.Ordinal);
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });
}
