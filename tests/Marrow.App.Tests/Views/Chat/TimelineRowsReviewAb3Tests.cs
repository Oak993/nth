using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW, third round (scoped re-check of unit blitz-timelinerows-ab after its REVIEW2 fix e934affac..eb564241e). Source: DroppyCode/App/Views/Chat/
// TimelineRows.swift (B3 52bc792); OD-0076: nothing on screen changes by a cut (a card that changes size eases to it, rows that go fold shut). Each test reads
// the state part way through the source's own animation, in the direction the REVIEW2 tests do not read.
[Collection("Headless")]
public sealed class TimelineRowsReviewAb3Tests(HeadlessSession headless)
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

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string WalterLong = "{\"task\": \"Write the tests for every silent path of the merge coordinator, one by one\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
    private const string TwoClosedLong = "[\n" + Hank + ",\n" + WalterLong + "\n]";
    private const string OneClosedShort = "[\n" + Hank + "\n]";

    // TimelineRows.swift:652-704 the card hugs its contents (the pill is the VStack's background) and `.animation(Chrome.panelSlide, value: delegations.count)`
    // animates every change the new count brings, the background's frame included, in either direction: a block that now reads fewer, shorter heads narrows the
    // card along the spring; OD-0076: a card that changes size eases to it. The port's SizeSlot eases its own width, but the pill inside it is left-aligned, so it
    // is arranged at its new (narrower) desired width at once: the card's drawn edge cuts in while the slot is still wide.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-149: the card's changes animate with Chrome.panelSlide
    public void REVIEW3_the_delegation_card_narrows_along_the_panel_slide_when_its_heads_get_shorter() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        Border Pill() => Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });
        var before = 0.0;
        h.Main.OnMain(() =>
        {
            card = new HydraDelegationBlock(TwoClosedLong);
            window = Show(card);
            before = Pill().Bounds.Width;
            card.Update(OneClosedShort, sent: false);
        });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 50);
            var mid = 0.0;
            h.Main.OnMain(() => mid = Pill().Bounds.Width);
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Pill().Bounds.Width);
            Assert.True(before > after + 60, $"the card went from {before:F1} to {after:F1} wide");
            // 50 ms into spring(0.32, 0.9) the curve is about a quarter of its way.
            Assert.True(mid >= before - 0.75 * (before - after), $"the card's width cut: {mid:F1} wide 50 ms into the panel slide, from {before:F1} to {after:F1}");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // TimelineRows.swift:1466-1501 the close is `withAnimation(.snappy(duration: 0.24))` (:1475-1477) and removes WorkSteps from the VStack, so the stack's height
    // shrinks along that curve and what follows the group moves up with it; OD-0076: rows that go fold shut.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-022: "click: toggles over 0.24 seconds with the snappy curve"
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1466-1477: the fold's height on close (what follows moves with it) along the snappy 0.24 s curve
    public void REVIEW3_closing_the_work_group_folds_its_steps_shut_along_the_curve_rather_than_pulling_what_follows_up_at_the_end() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        WorkGroup group = null!;
        StackPanel host = null!;
        Border below = null!;
        AvaloniaWindow window = null!;
        double Below() => below.TranslatePoint(default, host)!.Value.Y;
        var before = 0.0;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: false);
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
            h.Main.OnMain(() => early = Below());
            Run(h.Main, frames, window, 130);
            var late = 0.0;
            h.Main.OnMain(() => late = Below());
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Below());
            Assert.True(before > after + 20, $"what follows the group went from {before:F1} to {after:F1}");
            // 50 ms into the snappy 0.24 s curve it is about 0.4 of its way; 180 ms in, well past half.
            Assert.True(early <= before - 0.2 * (before - after), $"what follows the group has not moved 50 ms into the 0.24 s close: {early:F1}, from {before:F1} to {after:F1}");
            Assert.True(late <= before - 0.6 * (before - after), $"what follows the group is still at {late:F1} 180 ms into the 0.24 s close, from {before:F1} to {after:F1}");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    private static double Effective(Visual visual) => visual.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);

    // TimelineRows.swift:2357 `.strikethrough(step.status == .done, color: .secondary)` under :2372 `.animation(.snappy(duration: 0.2), value: steps)`: un-checking
    // a done step takes its strike out under the card's animation, so it is still partly drawn part way through, never cut out; OD-0076.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-114: "checking an item off animates the same list"
    public void REVIEW3_unchecking_a_step_takes_its_strike_out_along_the_animation_rather_than_at_once() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
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
        var start = 0.0;
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            start = Strike();
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending)) };
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
            Assert.True(start > 0.1, $"the done step is not struck through to begin with ({start:F3})");
            Assert.Equal(0, end, 0.001);
            // 50 ms into the snappy 0.2 s change the curve is about 0.5 of its way: the strike is on its way out, not gone.
            Assert.True(mid > 0.1 * start, $"the strike cut out: {mid:F3} of it is drawn 50 ms into the 0.2 s change, {start:F3} before it");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
