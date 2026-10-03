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
using static Marrow.UI.Tests.Fixtures.StreamVeilFlowHost;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/StreamVeil.swift:4-24 (every flush is committed to layout at once and the characters that just arrived
// dissolve over the next beat), :26-140 StreamVeil, :200-339 VeiledText, :344-431 LinkTextVeil; DroppyCode/UI/Markdown/
// LinkParagraphView.swift:42, :55-57 (the veil is fed after every render); MarkdownView.swift:63-98 (the blocks of a streaming reply
// keep their identity, ForEach(id: \.offset), through every flush).
//
// UI-THEME-047. A row of the timeline builds its markdown again for every flush. SwiftUI keeps the veil of every VeiledText and of every
// link paragraph's text view by the view's place, so each flush's body sees the longer text and registers what was appended; the port
// built every flush's views new, each adopting its text as the baseline, and nothing was ever veiled. The rig is the host path of the
// real rows (AssistantMessageRow, the narration, the thinking steps): a Decorator that replaces its MarkdownView with a new one for each
// flush and hands it the MarkdownVeils it keeps. The real rows themselves are driven in Marrow.App.Tests (TimelineRowsStreamVeilTests).
[Collection("Headless")]
public sealed class StreamVeilFlowTests(HeadlessSession headless)
{
    // The text of every run under the control, with the opacity it is painted at: the brush's opacity times its colour's alpha, 1 for a run that
    // wears no brush of its own.
    private static (string Text, double Opacity)[] Runs(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .SelectMany(block => block.Inlines?.OfType<Run>() ?? [])
            .Where(run => !string.IsNullOrEmpty(run.Text))
            .Select(run => (run.Text!, run.Foreground is ISolidColorBrush brush ? brush.Opacity * brush.Color.A / 255.0 : 1.0))
            .ToArray();

    private static IEnumerable<string> Texts((string Text, double Opacity)[] runs) => runs.Select(run => run.Text);

    // StreamVeil.swift:42, :93: the text shows through at 1 - (1 - p)^1.6 after p of the chunk's fade.
    private static double Alpha(double progress) => 1 - Math.Pow(1 - progress, 1.6);

    // VeilClock.shared belongs to the whole process and its display link to the frame source current when it started (StreamVeil.swift:164): a link
    // another test left running would never be ticked by this test's frames, so each test starts the clock afresh.
    private static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
    }

    private static void Tick(TestRuntime runtime, ManualFrameSource frames, double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Pump(); });
    }

    private const string Local = "file:///C:/notes.md"; // a link with no host: nothing to fetch a favicon for

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:10-21, :273-279
    public void Every_kind_of_block_veils_what_a_flush_appends_at_its_own_place_and_settles_to_plain_text() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "# Title\n\nHello\n\n- one\n\n```\ncode\n```\n");
        try
        {
            // What is on screen when the veils attach is the baseline: a heading, a paragraph, a list item and a code block, none of them veiled.
            var first = Runs(host.Decorator);
            Assert.Equal(["Title", "Hello", "one", "code"], Texts(first));
            var settled = first[0].Opacity;
            Assert.All(first, run => Assert.Equal(settled, run.Opacity, 0.001));
            Assert.Equal(0, frames.Pending);

            // One flush appends to all four. Each place registers its own tail and nothing else moves.
            host.Flush(runtime, "# Title!\n\nHello there\n\n- one two\n\n```\ncode more\n```\n");
            var atFlush = Runs(host.Decorator);
            Assert.Equal(["Title", "!", "Hello", " there", "one", " two", "code", " more"], Texts(atFlush));
            for (var index = 0; index < atFlush.Length; index += 2)
            {
                Assert.Equal(settled, atFlush[index].Opacity, 0.001);
                Assert.Equal(0, atFlush[index + 1].Opacity, 0.001);
            }

            // The first chunk of each place fades over 0.40 s (seed cadence 0.16 s x 3, clamped): a quarter through at 0.1 s.
            Tick(runtime, frames, 100);
            var quarter = Runs(host.Decorator);
            Assert.Equal(["Title", "!", "Hello", " there", "one", " two", "code", " more"], Texts(quarter));
            for (var index = 0; index < quarter.Length; index += 2)
            {
                Assert.Equal(settled, quarter[index].Opacity, 0.001);
                Assert.Equal(settled * Alpha(0.25), quarter[index + 1].Opacity, 0.01);
            }

            // Past the last chunk's settle: plain text in the primary colour again, and the veil clock stops.
            Tick(runtime, frames, 400);
            var done = Runs(host.Decorator);
            Assert.Equal(["Title!", "Hello there", "one two", "code more"], Texts(done));
            Assert.All(done, run => Assert.Equal(settled, run.Opacity, 0.001));
            Assert.Equal(0, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:42, :55-57; DroppyCode/UI/Markdown/StreamVeil.swift:344-431
    public void A_link_paragraph_fades_its_appended_tail_through_the_rebuilds_keeping_its_link_in_the_accent() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, $"Read [notes]({Local}) now");
        try
        {
            var first = Runs(host.Decorator);
            Assert.Equal(["Read ", "notes", " now"], Texts(first));
            Assert.Equal(0, frames.Pending); // adopted silently: nothing to paint, nothing to tick

            host.Flush(runtime, $"Read [notes]({Local}) now please");
            var atFlush = Runs(host.Decorator);
            // The run " now please" is split at the chunk's boundary; the words already there keep their runs and colours.
            Assert.Equal(["Read ", "notes", " now", " please"], Texts(atFlush));
            Assert.Equal(0, atFlush[3].Opacity, 0.001); // :414 a chunk starts at nothing
            Assert.Equal(first[0].Opacity, atFlush[0].Opacity, 0.001);
            Assert.Equal(first[2].Opacity, atFlush[2].Opacity, 0.001);
            var accent = Marrow.UI.Chrome.Chrome.Accent.Resolve(host.Decorator.GetVisualDescendants().OfType<TextBlock>().First().ActualThemeVariant);
            Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(host.Decorator.GetVisualDescendants().OfType<TextBlock>().SelectMany(block => block.Inlines!.OfType<Run>()).Single(run => run.Text == "notes").Foreground).Color);

            Tick(runtime, frames, 100);
            var quarter = Runs(host.Decorator);
            Assert.Equal(["Read ", "notes", " now", " please"], Texts(quarter));
            Assert.Equal(Alpha(0.25), quarter[3].Opacity, 0.01);

            // A second flush while the first still dissolves: the view is new, the veil is the place's, so the first chunk carries on.
            host.Flush(runtime, $"Read [notes]({Local}) now please sir");
            var rolling = Runs(host.Decorator);
            Assert.Equal(["Read ", "notes", " now", " please", " sir"], Texts(rolling));
            Assert.Equal(Alpha(0.25), rolling[3].Opacity, 0.01);
            Assert.Equal(0, rolling[4].Opacity, 0.001);

            Tick(runtime, frames, 500);
            var done = Runs(host.Decorator);
            Assert.Equal(["Read ", "notes", " now please sir"], Texts(done)); // the pristine run, restored
            Assert.Equal(first[2].Opacity, done[2].Opacity, 0.001);
            Assert.Equal(0, frames.Pending); // the paint clock has stopped
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:73-77 (a view going away detaches the veil it drew)
    public void A_link_view_leaving_after_a_newer_one_took_the_kept_veil_over_leaves_the_veil_and_the_new_views_fade_alone() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        var scope = new MarkdownVeils().Root.Child("b0");
        var panel = new StackPanel();
        StreamVeil.SetMarkdownVeiled(panel, true);
        LinkParagraphView older = null!, newer = null!;
        Window window = null!;
        runtime.OnMain(() =>
        {
            older = new LinkParagraphView($"Read [notes]({Local}) now", streaming: true, veils: scope);
            panel.Children.Add(older);
            window = Show(panel); // the older view adopts what it shows
            newer = new LinkParagraphView($"Read [notes]({Local}) now please", streaming: true, veils: scope);
            panel.Children.Add(newer); // the newer view takes the veil over: " please" arrives
            Pump();
        });
        try
        {
            Assert.Equal(["Read ", "notes", " now", " please"], Texts(Runs(newer)));
            Assert.Equal(0, Runs(newer)[3].Opacity, 0.001);
            runtime.OnMain(() => { panel.Children.Remove(older); Pump(); }); // the view the flush replaced goes, after the new one is in
            Tick(runtime, frames, 100);
            Assert.Equal(Alpha(0.25), Runs(newer)[3].Opacity, 0.01); // the new view's fade carries on, undisturbed
            Assert.Equal(1, frames.Pending);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            window.Close();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:311, :319
    public void An_arriving_run_is_painted_in_the_primary_colour_at_its_alpha_and_in_the_dimmed_primary_when_the_reply_is_dimmed(bool dimmed) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello", dimmed);
        try
        {
            host.Flush(runtime, "Hello there", dimmed);
            Tick(runtime, frames, 200); // half way through the 0.40 s fade
            var block = host.Decorator.GetVisualDescendants().OfType<TextBlock>().Single();
            var arriving = block.Inlines!.OfType<Run>().Single(run => run.Text == " there");
            var settled = block.Inlines!.OfType<Run>().Single(run => run.Text == "Hello");
            // :311 `dimmed ? Color.primary.opacity(0.7) : Color.primary`, :319 `base.opacity(span.alpha)`, :93 alpha = 1 - (1 - p)^1.6.
            var expected = (dimmed ? SwiftColor.Primary.Opacity(0.7) : SwiftColor.Primary).Opacity(Alpha(0.5)).Brush(block);
            var actual = Assert.IsAssignableFrom<ISolidColorBrush>(arriving.Foreground);
            var wanted = Assert.IsAssignableFrom<ISolidColorBrush>(expected);
            Assert.Equal(wanted.Color, actual.Color);
            Assert.Equal(wanted.Opacity, actual.Opacity, 3);
            // Text that has arrived and faded keeps whatever colour its run had: nothing about it is veiled.
            Assert.True(settled.Foreground is null || Assert.IsAssignableFrom<ISolidColorBrush>(settled.Foreground).Opacity >= 0.99);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:245 (one veil per place), :273-279
    public void A_veiled_text_the_flush_replaced_feeds_the_veil_nothing_more() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var veils = new MarkdownVeils();
        var scope = veils.Root.Child("b0");
        VeiledText older = null!, newer = null!;
        Window window = null!;
        runtime.OnMain(() =>
        {
            // The older text is on screen with the place's veil: "Hello" is the baseline.
            older = new VeiledText("Hello", scope);
            var panel = new StackPanel { Children = { older } };
            window = Show(panel);
            StreamVeil.SetMarkdownVeiled(older, true);
            Pump();
            Assert.Equal(["Hello"], Texts(Runs(older)));
            // The next flush's text takes the place over while the older one is still in the tree (a thinking step leaving through a transition
            // while the step that replaced it is already there): " there" arrives.
            newer = new VeiledText("Hello there", scope);
            panel.Children.Add(newer);
            StreamVeil.SetMarkdownVeiled(newer, true);
            Pump();
        });
        try
        {
            var atFlush = Runs(newer);
            Assert.Equal(["Hello", " there"], Texts(atFlush));
            var settled = atFlush[0].Opacity;
            Assert.Equal(0, atFlush[1].Opacity, 0.001);
            Tick(runtime, frames, 100); // the first chunk fades over 0.40 s: a quarter through
            Assert.Equal(settled * Alpha(0.25), Runs(newer)[1].Opacity, 0.01);

            // The older text is told its environment again, with its shorter text: toggling \.markdownVeiled makes it evaluate. Were it to feed the veil,
            // " there" would be gone from the veil's text, and the newer text's next evaluation would register it as arriving all over again.
            runtime.OnMain(() =>
            {
                StreamVeil.SetMarkdownVeiled(older, false);
                Pump();
                StreamVeil.SetMarkdownVeiled(older, true);
                Pump();
            });
            Tick(runtime, frames, 100);
            // The fade carried on from where it was (half way through at 0.2 s); it did not start again from nothing.
            Assert.Equal(["Hello", " there"], Texts(Runs(newer)));
            Assert.Equal(settled * Alpha(0.5), Runs(newer)[1].Opacity, 0.01);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            runtime.OnMain(window.Close);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:291-295 (once the reply has finished the veil is emptied, a settled paragraph keeps no copy of itself); MarkdownView.swift:69-83 (it is prose runs then, a different view)
    public void A_reply_that_stops_streaming_keeps_no_copy_of_itself_in_the_row_that_held_its_veils(bool links) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var lead = links ? $"Read [notes]({Local}) now" : "Hello";
        var host = new StreamVeilFlowHost(runtime, lead);
        try
        {
            host.Flush(runtime, lead + " there");
            Tick(runtime, frames, 600); // everything has settled: a quiet beat between two flushes
            // While it still streams the baseline stays, or the next flush would arrive unveiled (StreamVeil.swift:291-292).
            Assert.True(host.Veils.HoldsText, "a streaming reply keeps its text for the next flush");
            Assert.True(host.Veils.Places > 0);

            // The reply is done: the row builds its markdown with isStreaming false, which draws prose runs, and the veils go with the views that kept them.
            runtime.OnMain(() => { host.Decorator.Child = new MarkdownView(lead + " there", isStreaming: false, host.Veils.Root); Pump(); });
            Assert.False(host.Veils.HoldsText, "no copy of the settled reply is kept");
            Assert.Equal(0, host.Veils.Places); // and no place keeps the views that drew it
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    // The end of a stream. MarkdownView.swift:69-83 draws a finished reply as prose runs, a different view, so on the Mac the words still dissolving
    // when the stream ends jump to the full colour, and words that arrive with the completion (ThreadRuntime.swift:3248-3265: completeMessage sets
    // the final text and isStreaming false in one change) never dissolve at all: "a token that popped in whole read as a glitch" (StreamVeil.swift:4-8).
    // OD-0076 (owner, 2026-10-02): nothing on screen changes by a cut, and where the Mac changes state without animation the port still transitions.
    private static ParagraphTextPresenter ProseRun(Control root) =>
        Assert.Single(Assert.Single(root.GetVisualDescendants().OfType<ProseRunView>()).GetVisualDescendants().OfType<ParagraphTextPresenter>());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // StreamVeil.swift:4-8, :102-107 (a veil that is no longer fed finishes the chunks in flight); MarkdownView.swift:69-83
    [Trait("Decision", "OD-0076")]
    public void A_reply_that_ends_while_its_tail_dissolves_carries_the_tail_on_and_draws_prose_runs_once_it_has_settled(bool links) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var lead = links ? $"Read [notes]({Local}) now" : "Hello";
        var host = new StreamVeilFlowHost(runtime, lead);
        try
        {
            var settled = links ? 1.0 : Runs(host.Decorator)[0].Opacity; // a link paragraph's painted run carries the alpha alone (see the link test above)
            host.Flush(runtime, lead + " there");
            Tick(runtime, frames, 100); // the first chunk fades over 0.40 s: a quarter through
            var before = Runs(host.Decorator);
            Assert.Equal(" there", before[^1].Text);
            Assert.Equal(settled * Alpha(0.25), before[^1].Opacity, 0.01);

            // The provider completes the reply: the stream ends with " there" a quarter through its fade. It carries on from where it was.
            host.Finish(runtime, lead + " there");
            var atEnd = Runs(host.Decorator);
            Assert.Equal(Texts(before), Texts(atEnd));
            Assert.Equal(settled * Alpha(0.25), atEnd[^1].Opacity, 0.01);
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>()); // not yet the prose runs: they would draw the tail whole
            Assert.True(host.Veils.HoldsText, "the places the tail dissolves in are kept while it dissolves");

            Tick(runtime, frames, 100);
            Assert.Equal(settled * Alpha(0.5), Runs(host.Decorator)[^1].Opacity, 0.01);

            // Past its settle (0.40 s): the reply is the Swift's prose runs, and nothing of the stream is kept, no place, no text, no clock.
            Tick(runtime, frames, 300);
            Assert.Equal((links ? "Read notes now" : "Hello") + " there", ProseRun(host.Decorator).Text.String);
            Assert.Equal(0, host.Veils.Places);
            Assert.False(host.Veils.HoldsText);
            Assert.Equal(0, frames.Pending);
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
    [Trait("Req", "UI-THEME-047")] // StreamVeil.swift:4-8, :10-12 (each append registers the new tail as a chunk); ThreadRuntime.swift:3248-3265
    [Trait("Decision", "OD-0076")]
    public void The_words_that_arrive_with_the_end_of_the_stream_dissolve_in_before_the_reply_is_drawn_as_prose_runs(bool links) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var lead = links ? $"Read [notes]({Local}) now" : "Hello";
        var host = new StreamVeilFlowHost(runtime, lead);
        try
        {
            var settled = links ? 1.0 : Runs(host.Decorator)[0].Opacity;
            // The last delta and the completion land together: " there" was never on screen while the reply streamed.
            host.Finish(runtime, lead + " there");
            var atEnd = Runs(host.Decorator);
            Assert.Equal(" there", atEnd[^1].Text);
            Assert.Equal(0, atEnd[^1].Opacity, 0.001); // a chunk starts at nothing
            Assert.Empty(host.Decorator.GetVisualDescendants().OfType<ProseRunView>());

            Tick(runtime, frames, 100);
            Assert.Equal(settled * Alpha(0.25), Runs(host.Decorator)[^1].Opacity, 0.01);

            Tick(runtime, frames, 400);
            Assert.Equal((links ? "Read notes now" : "Hello") + " there", ProseRun(host.Decorator).Text.String);
            Assert.Equal(0, host.Veils.Places);
            Assert.False(host.Veils.HoldsText);
            Assert.Equal(0, frames.Pending);
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
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83; StreamVeil.swift:245 (@State per view: each thinking step is a MarkdownView of its own, ThreadTimeline.swift:2158)
    [Trait("Decision", "OD-0076")]
    public void A_step_that_ends_with_nothing_in_flight_is_drawn_as_prose_runs_at_once_while_another_step_still_dissolves(bool otherIsLink) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var veils = new MarkdownVeils();
        var first = veils.Root.Child("s1");
        var second = veils.Root.Child("s10"); // a name the first one's is a prefix of
        var other = otherIsLink ? $"Read [notes]({Local}) now" : "There";
        Window window = null!;
        StackPanel panel = null!;
        runtime.OnMain(() =>
        {
            panel = new StackPanel { Children = { new MarkdownView("Hello", isStreaming: true, first), new MarkdownView(other, isStreaming: true, second) } };
            window = Show(panel);
        });
        try
        {
            // The second step (a link paragraph or a plain one) takes a flush: its words dissolve and its place keeps its text.
            runtime.OnMain(() => { panel.Children[1] = new MarkdownView(other + " please", isStreaming: true, second); Pump(); });
            Tick(runtime, frames, 100);
            Assert.Equal(2, veils.Places);

            // The first step ends with nothing of its own in flight: it is the prose runs at once, whatever the other step still does.
            runtime.OnMain(() =>
            {
                var ended = new MarkdownView("Hello", isStreaming: false, first);
                Assert.Single(ended.GetVisualDescendants().OfType<ProseRunView>());
                Assert.Equal(1, veils.Places); // its own place went, the other step's stays
                panel.Children[0] = ended;
                Pump();
            });
            var tail = Runs(panel.Children[1])[^1];
            Assert.Equal(" please", tail.Text);
            Assert.InRange(tail.Opacity, 0.01, 0.6); // and the other step's words go on dissolving (a quarter through: 0.37 of their colour)
        }
        finally
        {
            Tick(runtime, frames, 1000);
            runtime.OnMain(window.Close);
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // MarkdownView.swift:69-83 (a finished reply is prose runs); StreamVeil.swift:18-19 (a veil that attaches adopts what is on screen)
    [Trait("Decision", "OD-0076")]
    public void A_reply_that_ends_after_a_reset_has_nothing_to_carry_on_and_is_drawn_as_prose_runs_at_once() => headless.OnUiThread(() =>
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
            Tick(runtime, frames, 100);
            runtime.OnMain(() => host.Veils.Reset()); // the row left the screen with " there" in flight: what it shows when it is back is the baseline
            host.Finish(runtime, "Hello there and more");
            Assert.Equal("Hello there and more", ProseRun(host.Decorator).Text.String);
            Assert.Equal(0, host.Veils.Places);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:291-295 and :245 (@State per view), for the thinking steps: each step is a MarkdownView of its own (ThreadTimeline.swift:2158)
    public void A_step_that_stops_streaming_releases_its_own_places_and_those_of_no_other_step() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var veils = new MarkdownVeils();
        Window window = null!;
        StackPanel panel = null!;
        VeiledText Veiled(string step, string text)
        {
            var view = new VeiledText(text, veils.Root.Child(step).Child("b0"));
            panel.Children.Add(view);
            StreamVeil.SetMarkdownVeiled(view, true);
            return view;
        }
        runtime.OnMain(() =>
        {
            panel = new StackPanel();
            window = Show(panel);
            // Three steps, each with the baseline "Hello" in its place; the names are prefixes of one another.
            foreach (var step in new[] { "s1", "s10", "s2" }) Veiled(step, "Hello");
            Pump();
            Assert.Equal(3, veils.Places);
            Assert.True(veils.HoldsText);
        });
        try
        {
            runtime.OnMain(() =>
            {
                _ = new MarkdownView("Hello", isStreaming: false, veils.Root.Child("s1")); // the first of them has finished
                Assert.Equal(2, veils.Places); // s10 and s2 stay: the step named s1 is not the one named s10
                var s10 = Veiled("s10", "Hello there"); // the next flush of those that still stream: " there" arrives
                var s2 = Veiled("s2", "Hello there");
                Pump();
                Assert.Equal(["Hello", " there"], Texts(Runs(s10)));
                Assert.Equal(["Hello", " there"], Texts(Runs(s2)));
                Assert.Equal(0, Runs(s10)[1].Opacity, 0.001);
                Assert.Equal(0, Runs(s2)[1].Opacity, 0.001);
            });
        }
        finally
        {
            Tick(runtime, frames, 1000);
            runtime.OnMain(window.Close);
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:291-295 (a text that is not veiled empties its veil)
    public void A_text_that_stops_being_veiled_forgets_what_it_was_fed_so_a_later_stream_adopts_what_is_on_screen() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var veils = new MarkdownVeils();
        var scope = veils.Root.Child("b0");
        Window window = null!;
        runtime.OnMain(() =>
        {
            var first = new VeiledText("Hello", scope);
            var panel = new StackPanel { Children = { first } };
            window = Show(panel);
            StreamVeil.SetMarkdownVeiled(first, true); // the baseline: "Hello"
            Pump();
            Assert.True(veils.HoldsText);
            StreamVeil.SetMarkdownVeiled(first, false); // it is not veiled any more: nothing fades, so its veil is emptied
            Pump();
            Assert.False(veils.HoldsText, "a settled paragraph keeps no copy of itself");
            // The place is veiled again (a later stream): what is on screen is the baseline, " there" is not an arrival.
            var second = new VeiledText("Hello there", scope);
            panel.Children.Add(second);
            StreamVeil.SetMarkdownVeiled(second, true);
            Pump();
            Assert.Equal(["Hello there"], Texts(Runs(second)));
        });
        runtime.OnMain(window.Close);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:18-19 (what is on screen when a veil attaches is the baseline)
    public void Resetting_the_veils_makes_the_next_evaluation_a_first_one_that_adopts_the_text(bool links) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var lead = links ? $"Read [notes]({Local}) now" : "Hello";
        var shown = links ? "Read notes now" : "Hello"; // what the markdown reads as
        var host = new StreamVeilFlowHost(runtime, lead);
        try
        {
            host.Flush(runtime, lead + " there");
            Tick(runtime, frames, 600);
            runtime.OnMain(() => host.Veils.Reset()); // the row left the screen
            host.Flush(runtime, lead + " there and more");
            // The first evaluation after the reset adopts everything on screen: nothing is veiled.
            Assert.Equal(shown + " there and more", string.Concat(Texts(Runs(host.Decorator))));
            Assert.All(Runs(host.Decorator), run => Assert.True(run.Opacity > 0.5, $"'{run.Text}' is plain text"));
            // From then on the veil works as before.
            host.Flush(runtime, lead + " there and more still");
            Assert.True(Runs(host.Decorator)[^1].Opacity < 0.01);
            Assert.Equal(" still", Runs(host.Decorator)[^1].Text);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:376-392 (the clock holds its last frame through a scroll)
    public void The_link_text_veil_holds_its_frame_while_the_timeline_is_scrolling_and_resumes_after() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var scroll = new ScrollActivity();
        using var scrollScope = ScrollActivity.Shared.Override(scroll);
        var veil = new LinkTextVeil();
        var none = new Dictionary<string, HydraMentionTarget>();
        var before = new LinkTextView();
        before.RenderText("hi", 13, dimmed: false, streaming: true, [], none);
        var after = new LinkTextView();
        after.RenderText("hi there", 13, dimmed: false, streaming: true, [], none);
        var window = Show(after);
        try
        {
            runtime.OnMain(() => { veil.Update(before, active: true); veil.Update(after, active: true); }); // adopts "hi", paints the arriving " there"
            Assert.Equal(["hi", " there"], Texts(Runs(after)));
            Assert.Equal(0, Runs(after)[1].Opacity, 0.001);

            var timeline = Guid.NewGuid();
            runtime.OnMain(() => scroll.SetScrolling(true, timeline));
            Tick(runtime, frames, 100);
            Assert.Equal(0, Runs(after)[1].Opacity, 0.001); // the reader is scrolling: the frame is held, nothing is repainted

            runtime.OnMain(() => scroll.SetScrolling(false, timeline));
            Tick(runtime, frames, 100);
            Assert.True(Runs(after)[1].Opacity > 0.1, "the ramp resumes when the scroll ends");
        }
        finally
        {
            runtime.OnMain(() => veil.Detach(after));
            Pump();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:183-188 (the shared clock holds its last frame through a scroll, so the veiled paragraphs are not redrawn; the ramp resumes when the scroll ends)
    public void A_veiled_text_holds_its_frame_while_the_timeline_is_scrolling_and_resumes_after() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var scroll = new ScrollActivity();
        using var scrollScope = ScrollActivity.Shared.Override(scroll);
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "Hello");
        try
        {
            host.Flush(runtime, "Hello there");
            Tick(runtime, frames, 100);
            var held = Runs(host.Decorator)[1].Opacity;
            Assert.Equal(Alpha(0.25) * Runs(host.Decorator)[0].Opacity, held, 0.01);

            var timeline = Guid.NewGuid();
            runtime.OnMain(() => scroll.SetScrolling(true, timeline));
            Tick(runtime, frames, 100);
            Assert.Equal(held, Runs(host.Decorator)[1].Opacity, 0.001); // the reader is scrolling: no frame is drawn

            runtime.OnMain(() => scroll.SetScrolling(false, timeline));
            Tick(runtime, frames, 50);
            Assert.True(Runs(host.Decorator)[1].Opacity > held + 0.05, "the ramp resumes when the scroll ends, at the time it has reached");
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:40, :73-80 (a fast stream: the cadence EMA, clamped to 120 ms at the bottom)
    public void A_fast_stream_clamps_the_fade_to_120_milliseconds()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;
        veil.Advance("a", start); // adopted
        veil.Advance("ab", start); // the first append has no gap: the seed cadence 0.16 s, a 0.40 s chunk
        veil.Advance("abc", start); // 0 s gap: 0.16 x 0.7 = 0.112, 0.336 s
        veil.Advance("abcd", start); // 0.0784, 0.2352 s
        veil.Advance("abcde", start); // 0.05488, 0.1646 s
        veil.Advance("abcdef", start); // 0.038416 x 3 = 0.1152 s, below the 120 ms floor
        // Five chunks in flight speed each other up by 1 + 0.3 x (5 - 2) = 1.9. The last chunk is 0.12 s long (not 0.1152 s): at 30 ms it is
        // 30 x 1.9 / 120 of the way through; the 0.40 s one is only 30 x 1.9 / 400.
        var spans = veil.Spans(start.AddMilliseconds(30));
        Assert.Equal(5, spans.Count);
        Assert.Equal(Alpha(0.03 * 1.9 / 0.12), spans[4].Alpha, 6);
        Assert.Equal(Alpha(0.03 * 1.9 / 0.40), spans[0].Alpha, 6);
    }

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:44, :75 (a gap counts at most one second)
    public void A_long_gap_counts_as_one_second_in_the_cadence()
    {
        using var runtime = new TestRuntime();
        var veil = new StreamVeil();
        var start = SwiftDate.ReferenceDate;
        veil.Advance("a", start);
        veil.Advance("ab", start); // the first append has no gap: cadence 0.16
        var later = start.AddSeconds(5);
        veil.Advance("abc", later); // a 5 s gap counts as 1.0: 0.16 x 0.7 + 1.0 x 0.3 = 0.412 (a 0.40 s chunk with or without the ceiling)
        for (var index = 0; index < 4; index++) veil.Advance("abc" + new string('x', index + 1), later); // four 0 s gaps: 0.412 x 0.7^4 = 0.0989
        // The first chunk settled long ago. Five are in flight (boost 1.9), the last 0.0989 x 3 = 0.2967 s long. Had the 5 s counted in full the
        // cadence would be 1.612 x 0.7^4 = 0.387 by now and that chunk the 0.40 s ceiling.
        var spans = veil.Spans(later.AddMilliseconds(30));
        Assert.Equal(5, spans.Count);
        Assert.Equal(Alpha(0.03 * 1.9 / (0.412 * 0.7 * 0.7 * 0.7 * 0.7 * 3)), spans[4].Alpha, 6);
    }

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:311, :423-426 (a link run that arrives is drawn in the accent at the chunk's alpha)
    public void A_link_that_arrives_is_drawn_in_the_accent_at_the_chunks_alpha_and_the_prose_beside_it_in_the_primary_colour() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, $"Read [notes]({Local})");
        try
        {
            host.Flush(runtime, $"Read [notes]({Local}) and [more]({Local})");
            Tick(runtime, frames, 100); // a quarter through the 0.40 s fade of the first chunk
            var runs = host.Decorator.GetVisualDescendants().OfType<TextBlock>().SelectMany(block => block.Inlines!.OfType<Run>()).Where(run => !string.IsNullOrEmpty(run.Text)).ToArray();
            Assert.Equal(["Read ", "notes", " and ", "more"], runs.Select(run => run.Text));
            var theme = host.Decorator.GetVisualDescendants().OfType<TextBlock>().First().ActualThemeVariant;
            var accent = Marrow.UI.Chrome.Chrome.Accent.Resolve(theme);
            // The words that were already there are untouched, the old link in the accent at full strength.
            var old = Assert.IsAssignableFrom<ISolidColorBrush>(runs[1].Foreground);
            Assert.Equal(accent, old.Color);
            Assert.Equal(1, old.Opacity * old.Color.A / 255.0, 0.001);
            // :423-426 the link that arrived keeps the accent it is drawn in, only thinner: its alpha is the chunk's (:93, a quarter of the way through).
            var arriving = Assert.IsAssignableFrom<ISolidColorBrush>(runs[3].Foreground);
            Assert.Equal(Color.FromRgb(accent.R, accent.G, accent.B), Color.FromRgb(arriving.Color.R, arriving.Color.G, arriving.Color.B));
            Assert.Equal(Alpha(0.25), arriving.Opacity * arriving.Color.A / 255.0, 0.01);
            // The prose beside it dissolves in the same chunk, in the colour its run had (:424-425), at the same alpha.
            var prose = Assert.IsAssignableFrom<ISolidColorBrush>(runs[2].Foreground);
            Assert.Equal(Alpha(0.25), prose.Opacity * prose.Color.A / 255.0, 0.01);
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/MarkdownView.swift:87 (ForEach(..., id: \.offset): a block keeps its place), :334-353 (list items and their blocks), :359-364 (the blocks of a quote, dimmed)
    public void Every_paragraph_list_item_nested_item_and_quote_block_keeps_a_veil_of_its_own_through_the_flushes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new StreamVeilFlowHost(runtime, "First\n\nSecond\n\n- a\n- b\n  - deep\n  1. step\n\n> quoted\n>\n> then\n");
        try
        {
            Assert.Equal(["First", "Second", "a", "b", "deep", "step", "quoted", "then"], Texts(Runs(host.Decorator)));
            // One flush appends to both paragraphs, to the second item of the list and to both lists nested in it, and to both blocks of the quote
            // (dimmed: the quote's blocks get \.markdownDimmed before they are under the markdown view that says the reply is streaming): each tail
            // is its own chunk at its own place, and every place that did not grow shows nothing arriving.
            host.Flush(runtime, "First one\n\nSecond two\n\n- a\n- b more\n  - deep down\n  1. step two\n\n> quoted again\n>\n> then more\n");
            var runs = Runs(host.Decorator);
            Assert.Equal(["First", " one", "Second", " two", "a", "b", " more", "deep", " down", "step", " two", "quoted", " again", "then", " more"], Texts(runs));
            var arriving = new[] { 1, 3, 6, 8, 10, 12, 14 };
            for (var index = 0; index < runs.Length; index++)
            {
                if (arriving.Contains(index)) Assert.True(runs[index].Opacity < 0.01, $"'{runs[index].Text}' has just arrived");
                else Assert.True(runs[index].Opacity > 0.5, $"'{runs[index].Text}' was already there");
            }

            // A flush that grows only the FIRST item of the list leaves the other places' veils alone: their tails carry on from where they were.
            Tick(runtime, frames, 100);
            host.Flush(runtime, "First one\n\nSecond two\n\n- a, then\n- b more\n  - deep down\n  1. step two\n\n> quoted again\n>\n> then more\n");
            var next = Runs(host.Decorator);
            Assert.Equal(["First", " one", "Second", " two", "a", ", then", "b", " more", "deep", " down", "step", " two", "quoted", " again", "then", " more"], Texts(next));
            Assert.True(next[5].Opacity < 0.01, "the new tail of the first item has just arrived");
            foreach (var carried in new[] { 1, 3, 7, 9, 11, 13, 15 })
                Assert.True(next[carried].Opacity > 0.01 && next[carried].Opacity < next[0].Opacity, $"'{next[carried].Text}' is already dissolving");
        }
        finally
        {
            Tick(runtime, frames, 1000);
            host.Window.Close();
        }
    });
}
