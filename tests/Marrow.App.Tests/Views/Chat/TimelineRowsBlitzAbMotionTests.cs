using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792). The motion of the timeline rows, read from the Swift code (OD-0098: where no reference frame exists the
// pinned source is the proof): the curves and rates it names, checked against the closed form of SwiftUI's spring, never against the port's own Animation type.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbMotionTests(HeadlessSession headless)
{
    // SwiftUI's `.spring(response:dampingFraction:)` for a given response and damping ratio (the unit-step response of a damped oscillator), written out here
    // so that a duration or a curve of the port is compared with the source's own numbers. `.snappy(duration: d)` is `.spring(duration: d, bounce: 0.15)`:
    // response d, damping ratio 1 - 0.15; `.smooth(duration:)` has bounce 0.
    private static double Spring(double response, double damping, double seconds)
    {
        if (seconds <= 0) return 0;
        var omega = 2 * Math.PI / response;
        var damped = omega * Math.Sqrt(1 - damping * damping);
        return 1 - Math.Exp(-damping * omega * seconds) * (Math.Cos(damped * seconds) + damping * omega / damped * Math.Sin(damped * seconds));
    }

    private static double Snappy(double duration, double seconds) => Spring(duration, 0.85, seconds);

    private static void Run(TestRuntime main, ManualFrameSource frames, double milliseconds, Action<double>? each = null, double step = 10)
    {
        for (var elapsed = step; elapsed <= milliseconds + 0.001; elapsed += step)
        {
            main.AdvanceMilliseconds(step);
            frames.Advance(TimeSpan.FromMilliseconds(step));
            each?.Invoke(elapsed);
        }
    }

    // The steps' opacity as drawn: their own times that of the body they fold open in.
    private static double Effective(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);

    private static double Angle(Control chevron) => Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle;

    // ---- UI-CHAT-022: the work group opens and closes over 0.24 seconds on the snappy curve ----

    [Fact]
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1468-1479 withAnimation(.snappy(duration: 0.24)) on open and on close, with the end reveal on open
    public void The_group_opens_and_closes_over_a_snappy_0_24_seconds_the_chevron_turn_and_the_fade_of_the_steps_following_that_curve() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        WorkGroup group = null!;
        AvaloniaWindow window = null!;
        var reveals = 0;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: true);
            var host = new Border { Child = group };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
            window = Show(host);
            Click(window, Assert.Single(Mounted<Button>(group)));
        });
        try
        {
            Control Chevron() => Assert.Single(Mounted<SwiftImage>(group), image => image.SymbolName == "chevron.right");
            // Open: the chevron is at 90 degrees times how far the snappy curve has got, the steps fade in by the same fraction; every sample is read
            // against the source's number, so a different duration or curve is a different line.
            h.Main.OnMain(() => Assert.Equal(1, reveals)); // :1473 opening asks the timeline to reveal its end, once
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, 240, elapsed =>
            {
                var expected = Snappy(0.24, elapsed / 1000);
                var turned = Angle(Chevron()) / 90;
                var shown = Effective(Assert.Single(Mounted<WorkSteps>(group)));
                Assert.True(Math.Abs(turned - expected) <= 0.02, $"open at {elapsed} ms: the chevron has turned {turned:F3} of its way, the snappy 0.24 s curve has {expected:F3}");
                Assert.True(Math.Abs(shown - expected) <= 0.02, $"open at {elapsed} ms: the steps are {shown:F3} in, the snappy 0.24 s curve has {expected:F3}");
            });
            Run(h.Main, frames, 600);
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(90, Angle(Chevron()));
                Assert.Equal(1, Effective(Assert.Single(Mounted<WorkSteps>(group))));
                Click(window, Assert.Single(Mounted<Button>(group)));
            });
            frames.Advance(TimeSpan.Zero);
            // Close: the same curve back.
            Run(h.Main, frames, 240, elapsed =>
            {
                var expected = Snappy(0.24, elapsed / 1000);
                var turned = Angle(Chevron()) / 90;
                var shown = Effective(Mounted<WorkSteps>(group).Single());
                Assert.True(Math.Abs(turned - (1 - expected)) <= 0.02, $"close at {elapsed} ms: the chevron has {turned:F3} of its turn left, the snappy 0.24 s curve has {1 - expected:F3}");
                Assert.True(Math.Abs(shown - (1 - expected)) <= 0.02, $"close at {elapsed} ms: the steps are {shown:F3} showing, the snappy 0.24 s curve has {1 - expected:F3}");
            });
            Run(h.Main, frames, 600);
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(0, Angle(Chevron()));
                Assert.Empty(Mounted<WorkSteps>(group));
                Assert.Equal(1, reveals); // :1475-1477 closing reveals nothing
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-148: the shimmer is redrawn thirty times a second ----

    [Fact]
    [Trait("Req", "PR-CHAT-148")] // TimelineRows.swift:495-499 TimelineView(.animation(minimumInterval: 1.0 / 30.0, ...)): "Thirty frames a second is the rate"
    public void The_shimmer_band_is_redrawn_thirty_times_a_second_and_never_faster() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        Control shimmer = null!;
        AvaloniaWindow window = null!;
        double Offset() => Assert.IsType<TranslateTransform>(Mounted<Border>(shimmer).Single(border => border.Background is LinearGradientBrush).RenderTransform).X;
        h.Main.OnMain(() =>
        {
            shimmer = new HydraShimmer().Body(new TextBlock { Text = "Pushing the branch to the remote", Width = 300 });
            window = Show(shimmer);
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            // The display ticks every 5 ms here (a frame rate far above both 30 and 60): the band moves only when the schedule says a thirtieth of a second is up.
            var drawn = new List<double>();
            var last = 0.0;
            h.Main.OnMain(() => last = Offset());
            Run(h.Main, frames, 2000, elapsed =>
            {
                var now = 0.0;
                h.Main.OnMain(() => now = Offset());
                if (now != last) { drawn.Add(elapsed); last = now; }
            }, step: 5);
            // Two seconds at thirty a second is sixty redraws, spaced a thirtieth of a second (33 ms, so 30 or 35 ms on a 5 ms beat) apart; sixty frames a second would be one hundred and twenty.
            Assert.InRange(drawn.Count, 58, 62);
            var gaps = drawn.Zip(drawn.Skip(1), (a, b) => b - a).ToList();
            Assert.True(gaps.All(gap => gap is >= 30 and <= 35), $"the band's redraws are {string.Join(", ", gaps.Distinct().OrderBy(gap => gap))} ms apart");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-HYDRAMERGE-089: the merging pill that has nothing to say goes on the glide spring ----

    // Chrome.swift:963-988 SettleGlideModifier at a progress p: the opacity is 1 less smoothstep(0.45, 0.92, p), the pill sinks 22 points and shrinks by 4 percent
    // about its top edge. :994-1001 settleGlide takes it from identity to that on `.spring(Chrome.glideSpring)`, response 0.6 and damping ratio 0.86 (:108).
    private static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // TimelineRows.swift:937-952 the flag going down with no outcome note takes the pill away; Chrome.swift:994-1001, :108 on the glide spring
    public void The_merging_pill_leaves_on_the_glide_spring_sinking_shrinking_and_fading_as_the_settle_glide_says() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow merging = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStage = "Pushing the branch";
            merging = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            window = Show(merging);
            Assert.NotEmpty(Mounted<HydraMergeTrack>(merging));
            h.Runtime.IsHydraMerging = false; // no outcome note takes the pill's place
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            var host = Assert.IsAssignableFrom<Panel>(merging.Child);
            var seen = 0;
            Run(h.Main, frames, 700, elapsed =>
            {
                var p = Spring(0.6, 0.86, elapsed / 1000);
                var pill = Assert.Single(host.Children);
                var group = Assert.IsType<TransformGroup>(pill.RenderTransform);
                var sink = Assert.IsType<TranslateTransform>(group.Children[0]).Y;
                var scale = Assert.IsType<ScaleTransform>(group.Children[1]).ScaleX;
                Assert.True(Math.Abs(sink - 22 * p) <= 0.2, $"at {elapsed} ms the pill has sunk {sink:F2} points, the glide spring has {22 * p:F2}");
                Assert.True(Math.Abs(scale - (1 - 0.04 * p)) <= 0.002, $"at {elapsed} ms the pill is at {scale:F4}, the glide spring has {1 - 0.04 * p:F4}");
                Assert.True(Math.Abs(pill.Opacity - (1 - Smoothstep(0.45, 0.92, p))) <= 0.02, $"at {elapsed} ms the pill is {pill.Opacity:F3} opaque, the glide spring has {1 - Smoothstep(0.45, 0.92, p):F3}");
                seen++;
            });
            Assert.Equal(70, seen);
            // Settled on the spring (0.77 s): the pill is gone and the row is empty.
            Run(h.Main, frames, 300);
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Empty(host.Children);
                Assert.Empty(Mounted<HydraMergeTrack>(merging));
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-114: checking an item off animates the same list ----

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static string Layers(OpacityCrossfade fade) =>
        string.Join("|", fade.Layers.Select(layer => (layer.View is TextBlock text ? text.Text : ((SwiftImage)layer.View).SymbolName) + "@" + layer.Opacity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // TimelineRows.swift:2352-2358, :2372 .animation(.snappy(duration: 0.2), value: steps) on the whole card; OD-0076: swapped content crossfades, a card that changes size eases to it
    public void Checking_an_item_off_crossfades_its_glyph_and_the_count_and_eases_the_cards_size_along_the_snappy_0_2_second_curve() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        TimelineEntry entry = null!;
        TodoListRow row = null!;
        AvaloniaWindow window = null!;
        SizeSlot slot = null!;
        Size before = default;
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            slot = Assert.Single(Mounted<SizeSlot>(row));
            before = slot.Presented;
            Assert.True(before.Width > 0 && before.Height > 0);
            // One is checked off, Two becomes the active step, and a third, longer one is added: the card is taller and wider.
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active), ("Three, a step a good deal longer than the others were", TodoStep.Status.Pending)) };
        });
        try
        {
            h.Main.OnMain(() => { });
            h.Main.OnMain(() => Settle(window)); // the layout pass that reads the new size under the animation it was made in
            frames.Advance(TimeSpan.Zero);
            Size after = default;
            var steps = new List<(double At, Size Card, string Count, string One, string Two)>();
            Run(h.Main, frames, 230, elapsed =>
            {
                h.Main.OnMain(() => Settle(window));
                h.Main.OnMain(() => { });
                var rows = Mounted<TimelineBaselineRow>(row).ToList();
                string Glyphs(int step) => Layers((OpacityCrossfade)rows[step].Children[0]);
                steps.Add((elapsed, slot.Presented, Layers(Mounted<OpacityCrossfade>(row).First(fade => fade.Current is TextBlock)), Glyphs(0), Glyphs(1)));
            });
            h.Main.OnMain(() => { Settle(window); after = slot.Presented; });
            Assert.True(after.Height > before.Height + 15 && after.Width > before.Width + 30, $"the card went from {before} to {after}");
            foreach (var (at, card, count, one, two) in steps)
            {
                var p = Clamp(Snappy(0.2, at / 1000));
                var rawP = Snappy(0.2, at / 1000);
                // The card: from its old size to the new along the curve (a spring may overshoot, the size follows it).
                Assert.True(Math.Abs(card.Height - (before.Height + (after.Height - before.Height) * rawP)) <= 1.5, $"at {at} ms the card is {card.Height:F1} tall, the snappy 0.2 s curve has {before.Height + (after.Height - before.Height) * rawP:F1}");
                Assert.True(Math.Abs(card.Width - (before.Width + (after.Width - before.Width) * rawP)) <= 1.5, $"at {at} ms the card is {card.Width:F1} wide, the snappy 0.2 s curve has {before.Width + (after.Width - before.Width) * rawP:F1}");
                // The count: "0 of 2 done" fades out as "1 of 3 done" fades in; a step's glyph: the old symbol out, the new one in, by the same fraction.
                // (The old one is gone once the curve has reached the end: a spring that overshoots takes it away at that first crossing.)
                if (rawP < 1)
                {
                    Assert.Equal($"0 of 2 done@{1 - p:F3}|1 of 3 done@{p:F3}", count);
                    Assert.Equal($"circle.dotted.circle@{1 - p:F3}|checkmark.circle.fill@{p:F3}", one);
                    Assert.Equal($"circle@{1 - p:F3}|circle.dotted.circle@{p:F3}", two);
                }
                else
                {
                    Assert.Equal("1 of 3 done@1.000", count);
                    Assert.Equal("checkmark.circle.fill@1.000", one);
                    Assert.Equal("circle.dotted.circle@1.000", two);
                }
            }
            // Settled: one glyph a step, one count, nothing left of what went.
            h.Main.OnMain(() =>
            {
                Assert.Equal(["1 of 3 done@1.000"], Mounted<OpacityCrossfade>(row).Where(fade => fade.Current is TextBlock).Select(Layers));
                Assert.Equal(["checkmark.circle.fill@1.000", "circle.dotted.circle@1.000", "circle@1.000"], Mounted<TimelineBaselineRow>(row).Select(line => Layers((OpacityCrossfade)line.Children[0])));
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-149: the delegation card's changes arrive by animation ----

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string Walter = "{\"task\": \"Write the tests\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
    private const string OneOpen = "[\n" + Hank + ",\n{\"task\": \"Wri";
    private const string TwoClosed = "[\n" + Hank + ",\n" + Walter + "\n]";

    // :703-704 `.animation(Chrome.panelSlide, ...)`: with no reduced motion `.spring(response: 0.32, dampingFraction: 0.9)`.
    private static double Panel(double seconds) => Spring(0.32, 0.9, seconds);

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:663-675 the title `.contentTransition(.opacity)` and the check, :694-701 the wave `.transition(.opacity)`, all under :703-704 Chrome.panelSlide
    public void Sending_the_card_crossfades_its_title_and_brings_in_the_check_and_the_wave_along_the_panel_slide_spring() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock(TwoClosed);
            window = Show(card);
            Assert.Empty(Mounted<DotFieldSweep>(card));
            Assert.Equal(["Summoning 2 heads@1.000"], Mounted<OpacityCrossfade>(card).Select(Layers));
            card.Update(TwoClosed, sent: true);
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            var seen = 0;
            Run(h.Main, frames, 380, elapsed =>
            {
                var raw = Panel(elapsed / 1000);
                var p = Clamp(raw);
                // The title: the old words out, the new in.
                var title = Assert.Single(Mounted<OpacityCrossfade>(card));
                if (raw < 1) Assert.Equal($"Summoning 2 heads@{1 - p:F3}|Summoned 2 heads@{p:F3}", Layers(title));
                else Assert.Equal("Summoned 2 heads@1.000", Layers(title));
                // The check comes in by opacity and scale, the wave by opacity.
                var check = Assert.Single(Mounted<SwiftImage>(card), image => image.SymbolName == "checkmark.circle.fill");
                Assert.True(Math.Abs(check.Opacity - p) <= 0.01, $"at {elapsed} ms the check is {check.Opacity:F3} in, the panel slide has {p:F3}");
                var wave = Assert.Single(Mounted<DotFieldSweep>(card));
                Assert.True(Math.Abs(wave.Opacity - p) <= 0.01, $"at {elapsed} ms the wave is {wave.Opacity:F3} in, the panel slide has {p:F3}");
                seen++;
            });
            Assert.Equal(38, seen);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:677-692 a head row comes in and the "Writing the next brief…" line goes, as views of a stack do (opacity), under :703-704 Chrome.panelSlide
    public void A_head_row_fades_in_and_the_next_brief_line_fades_out_when_the_brief_closes_along_the_panel_slide_spring() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock(OneOpen);
            window = Show(card);
            Assert.Single(Mounted<TextBlock>(card), text => text.Text == "Writing the next brief…");
            card.Update(TwoClosed, sent: false);
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            var seen = 0;
            Run(h.Main, frames, 380, elapsed =>
            {
                var raw = Panel(elapsed / 1000);
                var p = Clamp(raw);
                var second = Assert.Single(Mounted<TextBlock>(card), text => text.Text == "Write the tests");
                Assert.True(Math.Abs(((Control)second.GetVisualParent()!).Opacity - p) <= 0.01, $"at {elapsed} ms the new head row is {((Control)second.GetVisualParent()!).Opacity:F3} in, the panel slide has {p:F3}");
                var line = Mounted<TextBlock>(card).Where(text => text.Text == "Writing the next brief…").ToList();
                if (raw < 1)
                {
                    var gone = Assert.Single(line);
                    Assert.True(Math.Abs(((Control)gone.GetVisualParent()!).Opacity - (1 - p)) <= 0.01, $"at {elapsed} ms the next-brief line is {((Control)gone.GetVisualParent()!).Opacity:F3} left, the panel slide has {1 - p:F3}");
                }
                else Assert.True(line.Count == 0 || ((Control)line[0].GetVisualParent()!).Opacity <= 0.001, "the next-brief line is still showing after the curve has run out"); // it stays mounted, unseen, until the spring has settled
                seen++;
            });
            Assert.Equal(38, seen);
            Run(h.Main, frames, 200);
            h.Main.OnMain(() => Assert.DoesNotContain(Mounted<TextBlock>(card), text => text.Text == "Writing the next brief…")); // and then it is gone
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:681-686 the "Writing the next brief…" line comes in as a view of a stack does (opacity), under :703-704 Chrome.panelSlide
    public void The_next_brief_line_fades_in_when_a_block_that_had_read_to_its_end_is_streaming_again_along_the_panel_slide_spring() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock(TwoClosed);
            window = Show(card);
            Assert.DoesNotContain(Mounted<TextBlock>(card), text => text.Text == "Writing the next brief…");
            card.Update(OneOpen, sent: false); // the card is handed a block that is still streaming, with one head read
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, 380, elapsed =>
            {
                var p = Clamp(Panel(elapsed / 1000));
                var line = Assert.Single(Mounted<TextBlock>(card), text => text.Text == "Writing the next brief…");
                var opacity = ((Control)line.GetVisualParent()!).Opacity;
                Assert.True(Math.Abs(opacity - p) <= 0.01, $"at {elapsed} ms the next-brief line is {opacity:F3} in, the panel slide has {p:F3}");
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // TimelineRows.swift:676 `if !delegations.isEmpty`: a block with no head to list has no list, so no row gap
    public void A_block_that_reads_no_heads_after_reading_some_leaves_no_empty_list_and_no_gap_under_its_title() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!, bare = null!;
        AvaloniaWindow window = null!;
        const string unreadable = "[\n{\"task\": \"Ti";
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock(OneOpen);
            bare = new HydraDelegationBlock(unreadable);
            window = Show(new StackPanel { Spacing = 20, Children = { card, bare } });
            Assert.Single(Mounted<TextBlock>(card), text => text.Text == "Tidy the popover");
            card.Update(unreadable, sent: false); // not a continuation of what was shown: the list starts over, and there is nothing in it
        });
        try
        {
            Run(h.Main, frames, 1200, _ => h.Main.OnMain(() => Settle(window)), step: 50); // the list fades out, then the card eases down onto its title
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.DoesNotContain(Mounted<TextBlock>(card), text => text.Text is "Tidy the popover" or "Writing the next brief…");
                Assert.DoesNotContain(Mounted<StackPanel>(card), stack => stack.Spacing == 5); // no list at all
                Assert.True(Math.Abs(bare.Bounds.Height - card.Bounds.Height) <= 0.5, $"the card is {card.Bounds.Height} tall, one that never had a head {bare.Bounds.Height}"); // as tall as a card that never had a head
                Assert.Equal(["Summoning heads@1.000"], Mounted<OpacityCrossfade>(card).Select(Layers));
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ---- PR-CHAT-147: the lead's brief to a head ----

    [Fact]
    [Trait("Req", "PR-CHAT-147")] // TimelineRows.swift:552-582 one Button and one `.badgePopover(isPresented:)` for the life of the row; the title re-read as the head takes steps (:540-546)
    public void A_brief_opened_while_the_head_is_cooking_stays_open_on_the_same_button_as_the_pill_turns_into_the_brief() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraBriefRow row = null!;
        AvaloniaWindow window = null!;
        Button button = null!;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            h.App.UpdateThread(h.Thread.Id, thread => thread with { Hydra = new HydraHeadInfo(1, "the task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) });
            h.Runtime.RehearseTurn("Build the thing, in full");
            var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
            row = new HydraBriefRow(((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value, entry, h.Runtime);
            window = Show(row);
            button = Assert.Single(Mounted<Button>(row));
            // Nothing on the brief's turn yet: the head is still cooking, and the pill says so.
            Assert.Contains(Mounted<TextBlock>(row), text => text.Text?.EndsWith("is starting to cook…", StringComparison.Ordinal) == true);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                Assert.NotEmpty(LogicalQuery.SelfAndDescendants<HydraBriefPopover>(shown.Popup.Child!));
                h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                // The same button, the same popover: still the one presented, never dismissed and shown again.
                Assert.Same(button, Assert.Single(Mounted<Button>(row)));
                Assert.DoesNotContain(presenter.Calls, call => !call.Presented);
                Assert.Single(presenter.Calls, call => call.Presented);
                // And the pill has moved on from cooking to the brief.
                Assert.DoesNotContain(Mounted<TextBlock>(row), text => text.Text?.EndsWith("is starting to cook…", StringComparison.Ordinal) == true);
                Assert.Contains(Mounted<TextBlock>(row), text => text.Text?.EndsWith("'s brief", StringComparison.Ordinal) == true);
                // Tapping again closes it, as a toggle does.
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            });
            h.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Single(presenter.Calls, call => !call.Presented);
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
