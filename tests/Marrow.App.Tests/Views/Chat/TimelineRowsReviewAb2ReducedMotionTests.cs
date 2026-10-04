using Avalonia;
using Avalonia.Controls;
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
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// The three closures of the second review round (TimelineRowsReviewAb2Tests: the work group's steps fold open, a checked step's strike comes in, the delegation card's
// width eases) each move along the source's animation, and each is the Mac's own animation (ED-264 (2)): the work group's `withAnimation(.snappy(duration: 0.24))`
// (TimelineRows.swift:1469-1477) and the checklist's `.animation(.snappy(duration: 0.2), value: steps)` (:2372) read no reduced-motion setting, so they run the same
// with it on (ED-285, fifth round (5), (6)); the delegation card's count change follows Chrome.panelSlide, which the Mac itself turns into
// `.easeOut(duration: 0.18)` under reduced motion (Chrome.swift:100-104), so it eases along that curve (ED-285 fourth round (3)).
[Collection("Headless")]
public sealed class TimelineRowsReviewAb2ReducedMotionTests(HeadlessSession headless)
{
    private static void Run(TestRuntime main, ManualFrameSource frames, AvaloniaWindow window, double milliseconds)
    {
        for (var elapsed = 10.0; elapsed <= milliseconds + 0.001; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
            main.OnMain(() => Settle(window));
        }
    }

    private static double Effective(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string WalterLong = "{\"task\": \"Write the tests for every silent path of the merge coordinator, one by one\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-022 under reduced motion: TimelineRows.swift:1469-1477 `withAnimation(.snappy(duration: 0.24))` has no reduced-motion gate (ED-264 (2))
    [Trait("Req", "UI-CHAT-022")] // the open, under reduced motion
    public void REVIEW2_with_reduced_motion_opening_the_work_group_still_folds_its_steps_open_along_the_snappy_curve() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        WorkGroup group = null!;
        StackPanel host = null!;
        Border below = null!;
        AvaloniaWindow window = null!;
        double Below() => below.TranslatePoint(default, host)!.Value.Y;
        var before = 0.0;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: true);
            below = new Border { Height = 30, Width = 100, Background = Brushes.Gray };
            host = new StackPanel { Children = { group, below } };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => { } });
            window = Show(host);
            before = Below();
            Click(window, Assert.Single(Mounted<Button>(group)));
        });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 50);
            var early = 0.0;
            var shown = 0.0;
            h.Main.OnMain(() => { early = Below(); shown = Effective(Assert.Single(Mounted<WorkSteps>(group))); });
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Below());
            Assert.True(after > before + 20, $"what follows the group went from {before:F1} to {after:F1}");
            // 50 ms into the snappy 0.24 s curve it is about 0.4 of its way, as with reduced motion off (TimelineRowsReviewAb3Tests).
            Assert.InRange((early - before) / (after - before), 0.2, 0.8);
            Assert.InRange(shown, 0.05, 0.95);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1475-1477 the close is `withAnimation(.snappy(duration: 0.24))` too, no reduced-motion gate (ED-264 (2)): what follows the group rises along the curve, not at once or at the end
    public void With_reduced_motion_closing_the_work_group_still_folds_its_steps_shut_along_the_snappy_curve() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        WorkGroup group = null!;
        StackPanel host = null!;
        Border below = null!;
        AvaloniaWindow window = null!;
        double Below() => below.TranslatePoint(default, host)!.Value.Y;
        var closed = 0.0;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: true);
            below = new Border { Height = 30, Width = 100, Background = Brushes.Gray };
            host = new StackPanel { Children = { group, below } };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => { } });
            window = Show(host);
            closed = Below();
            Click(window, Assert.Single(Mounted<Button>(group)));
        });
        try
        {
            // Open and let it finish.
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 1000);
            var open = 0.0;
            h.Main.OnMain(() =>
            {
                open = Below();
                Assert.True(open > closed + 20, $"what follows the group went from {closed:F1} to {open:F1}");
                Click(window, Assert.Single(Mounted<Button>(group))); // close it
            });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 50);
            var early = 0.0;
            var shown = 0.0;
            h.Main.OnMain(() => { early = Below(); shown = Effective(Assert.Single(Mounted<WorkSteps>(group))); });
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Below());
            Assert.Equal(closed, after, 0.5);
            // 50 ms into the snappy 0.24 s curve it is about 0.4 of its way back, as with reduced motion off (TimelineRowsReviewAb3Tests): not at once, not at the end.
            Assert.InRange((open - early) / (open - closed), 0.2, 0.8);
            Assert.InRange(shown, 0.05, 0.95);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-114 under reduced motion: :2372 `.animation(.snappy(duration: 0.2), value: steps)` has no reduced-motion gate (ED-264 (2))
    public void REVIEW2_with_reduced_motion_checking_a_step_off_still_strikes_it_through_along_the_snappy_curve() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        TimelineEntry entry = null!;
        TodoListRow row = null!;
        AvaloniaWindow window = null!;
        double Strike()
        {
            var presence = 0.0;
            foreach (var text in Mounted<TextBlock>(row).Where(text => text.Text == "One"))
                foreach (var decoration in text.TextDecorations ?? [])
                {
                    if (decoration.Location != TextDecorationLocation.Strikethrough) continue;
                    var alpha = decoration.Stroke is ISolidColorBrush solid ? solid.Color.A / 255.0 * solid.Opacity : 1;
                    presence = Math.Max(presence, Effective(text) * alpha);
                }
            return presence;
        }
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Pending)) };
        });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 50);
            var mid = 0.0;
            h.Main.OnMain(() => mid = Strike());
            Run(h.Main, frames, window, 1000);
            var end = 0.0;
            h.Main.OnMain(() => end = Strike());
            Assert.True(end > 0.1, $"the done step is not struck through once settled ({end:F3})");
            // 50 ms into the snappy 0.2 s change the curve is about 0.5 of its way: the strike is on its way in, as with reduced motion off.
            Assert.InRange(mid / end, 0.1, 0.9);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // SwiftUI's `.easeOut(duration:)`: the cubic Bezier (0, 0) (0.58, 1), its progress at a fraction of the duration (solved for the curve's parameter by bisection).
    private static double EaseOut(double fraction)
    {
        if (fraction <= 0) return 0;
        if (fraction >= 1) return 1;
        double low = 0, high = 1;
        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2;
            var x = 3 * mid * mid * (1 - mid) * 0.58 + mid * mid * mid;
            if (x < fraction) low = mid; else high = mid;
        }
        var s = (low + high) / 2;
        return 3 * s * s * (1 - s) + s * s * s;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-149 under reduced motion: Chrome.swift:100-104 panelSlide is `.easeOut(duration: 0.18)`, and :703-704 animates the card's frame with it
    public void REVIEW2_with_reduced_motion_the_delegation_card_eases_to_its_new_width_along_panel_slides_0_18_second_ease_out() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        Border Pill() => Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });
        var before = 0.0;
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock("[\n" + Hank + ",\n{\"task\": \"Wri");
            window = Show(card);
            before = Pill().Bounds.Width;
            card.Update("[\n" + Hank + ",\n" + WalterLong + "\n]", sent: false);
        });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var widths = new List<(double At, double Width)>();
            for (var at = 10; at <= 300; at += 10)
            {
                Run(h.Main, frames, window, 10);
                h.Main.OnMain(() => widths.Add((at, Pill().Bounds.Width)));
            }
            var after = widths[^1].Width;
            Assert.True(after > before + 60, $"the card went from {before:F1} to {after:F1} wide");
            foreach (var (at, width) in widths)
            {
                var expected = before + (after - before) * EaseOut(at / 180.0);
                Assert.True(Math.Abs(width - expected) <= 0.06 * (after - before),
                    $"at {at} ms the card is {width:F1} wide; panelSlide's reduced-motion .easeOut(duration: 0.18) has {expected:F1} (from {before:F1} to {after:F1})");
            }
            Assert.True(widths[0].Width < after - 0.5 * (after - before), $"the card is {widths[0].Width:F1} wide 10 ms in: its size cut");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
