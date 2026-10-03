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
    private sealed class Host
    {
        public readonly Decorator Decorator = new();
        public readonly MarkdownVeils Veils = new();
        public Window Window = null!;

        public Host(TestRuntime runtime, string first, bool dimmed = false)
        {
            runtime.OnMain(() =>
            {
                Decorator.Child = View(first, dimmed);
                Window = Show(Decorator);
            });
        }

        public MarkdownView View(string text, bool dimmed)
        {
            var view = new MarkdownView(text, isStreaming: true, Veils.Root);
            if (dimmed) MarkdownView.SetMarkdownDimmed(view, true);
            return view;
        }

        // What a row does when a flush changes its text: a new MarkdownView replaces the old one.
        public void Flush(TestRuntime runtime, string text, bool dimmed = false) =>
            runtime.OnMain(() =>
            {
                Decorator.Child = View(text, dimmed);
                Pump();
            });
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 600, Content = new StackPanel { Children = { content } } };
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

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
    [Trait("Req", "PR-THEME-153")] // DroppyCode/UI/Markdown/StreamVeil.swift:4-24
    public void Every_kind_of_block_veils_what_a_flush_appends_at_its_own_place_and_settles_to_plain_text() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new Host(runtime, "# Title\n\nHello\n\n- one\n\n```\ncode\n```\n");
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
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:409-430
    public void A_link_paragraph_fades_its_appended_tail_through_the_rebuilds_keeping_its_link_in_the_accent() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new Host(runtime, $"Read [notes]({Local}) now");
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
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:370-372
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
    [Trait("Req", "PR-THEME-160")] // DroppyCode/UI/Markdown/StreamVeil.swift:309-338
    public void An_arriving_run_is_painted_in_the_primary_colour_at_its_alpha_and_in_the_dimmed_primary_when_the_reply_is_dimmed(bool dimmed) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var host = new Host(runtime, "Hello", dimmed);
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
        using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var veils = new MarkdownVeils();
        var scope = veils.Root.Child("b0");
        runtime.OnMain(() =>
        {
            var older = new VeiledText("Hello", scope);
            var newer = new VeiledText("Hello there", scope); // the next flush's text takes the place over
            var window = Show(new StackPanel { Children = { newer, older } });
            try
            {
                StreamVeil.SetMarkdownVeiled(newer, true); // the environment arrives: the newer text is the baseline
                Pump();
                Assert.Equal(["Hello there"], Texts(Runs(newer)));
                // The older one is told the environment too (it is still in the tree while the swap is under way), with its shorter text:
                // it would rewrite the veil's text and make the newer one's next evaluation register " there" as arriving.
                StreamVeil.SetMarkdownVeiled(older, true);
                MarkdownView.SetMarkdownDimmed(newer, true); // the newer one evaluates again
                Pump();
                Assert.Equal(["Hello there"], Texts(Runs(newer))); // one settled run: nothing arrived
            }
            finally
            {
                window.Close();
            }
        });
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
        var host = new Host(runtime, lead);
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
    [Trait("Req", "PR-THEME-161")] // DroppyCode/UI/Markdown/StreamVeil.swift:376-392
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
    [Trait("Req", "UI-THEME-047")] // DroppyCode/UI/Markdown/StreamVeil.swift:40, :73-80 (a fast stream: the cadence EMA, clamped to 120 ms at the bottom)
    [Trait("Req", "PR-THEME-154")] // DroppyCode/UI/Markdown/StreamVeil.swift:40-44,73-81
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
    [Trait("Req", "PR-THEME-154")] // DroppyCode/UI/Markdown/StreamVeil.swift:40-44,73-81
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
}
