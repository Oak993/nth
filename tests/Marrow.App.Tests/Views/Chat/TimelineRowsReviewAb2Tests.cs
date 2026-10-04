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
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW, second round (adversarial re-check of unit blitz-timelinerows-ab after its review fix 9c27d7c98..e934affac). Source: DroppyCode/App/Views/Chat/
// TimelineRows.swift (B3 52bc792); OD-0076: nothing on screen changes by a cut (rows that appear fold open, a card that changes size eases to it, content that
// swaps crossfades). Each test reads the state part way through the source's own animation.
[Collection("Headless")]
public sealed class TimelineRowsReviewAb2Tests(HeadlessSession headless)
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

    // TimelineRows.swift:2357 `.strikethrough(step.status == .done, color: .secondary)` under :2372 `.animation(.snappy(duration: 0.2), value: steps)`: checking a
    // step off changes its text's strike under the card's animation, so the strike is transitioned with everything else in the card, never cut in; OD-0076.
    // The port sets TextBlock.TextDecorations at once (TimelineRows.cs TodoListRow.Build): the strike is whole while the colours are still moving.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-114: "checking an item off animates the same list"
    public void REVIEW2_checking_a_step_off_brings_its_strike_in_along_the_animation_rather_than_at_once() => headless.OnUiThread(() =>
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
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(new Border { Child = row, Width = 600 });
            Assert.Equal(0, Strike());
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
            // 50 ms into the snappy 0.2 s change the curve is about 0.5 of its way: the strike may be on its way in, not whole.
            Assert.True(mid < 0.9 * end, $"the strike cut in: {mid:F3} of it is drawn 50 ms into the 0.2 s change, {end:F3} once settled");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string WalterLong = "{\"task\": \"Write the tests for every silent path of the merge coordinator, one by one\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
    private const string OneOpen = "[\n" + Hank + ",\n{\"task\": \"Wri";
    private const string TwoClosedLong = "[\n" + Hank + ",\n" + WalterLong + "\n]";

    // TimelineRows.swift:652-704 the card hugs its contents (a VStack in the pill's background, no maxWidth) and `.animation(Chrome.panelSlide, value:
    // delegations.count)` animates every change the new count brings, the card's frame included: a head whose task is longer than the card was widens it along
    // the spring; OD-0076: a card that changes size eases to it. The port's HeightSlot eases the height only and reports the content's width at once.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-149: the card's changes animate with Chrome.panelSlide
    public void REVIEW2_the_delegation_card_widens_along_the_panel_slide_when_a_longer_head_arrives() => headless.OnUiThread(() =>
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
            card = new HydraDelegationBlock(OneOpen);
            window = Show(card);
            before = Pill().Bounds.Width;
            card.Update(TwoClosedLong, sent: false);
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
            Assert.True(after > before + 60, $"the card went from {before:F1} to {after:F1} wide");
            // 50 ms into spring(0.32, 0.9) the curve is about a quarter of its way.
            Assert.True(mid <= before + 0.75 * (after - before), $"the card's width cut: {mid:F1} wide 50 ms into the panel slide, from {before:F1} to {after:F1}");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // TimelineRows.swift:1466-1501 the group is a VStack holding the header and, once open, WorkSteps; the open is `withAnimation(.snappy(duration: 0.24))`
    // (:1470-1472), so the stack's height grows along that curve and what follows the group moves down with it, as the working line's body grows under the same
    // animation (:2107-2119, the port's GrowingBody); OD-0076: rows that appear fold open. The port inserts the steps at their full height (fading in), so what
    // follows the group jumps down at once.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-022: "click: toggles over 0.24 seconds with the snappy curve"
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1466-1477: the fold's height on open (what follows moves with it) along the snappy 0.24 s curve
    public void REVIEW2_opening_the_work_group_folds_its_steps_open_along_the_curve_rather_than_pushing_what_follows_down_at_once() => headless.OnUiThread(() =>
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
            var mid = 0.0;
            h.Main.OnMain(() => mid = Below());
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Below());
            Assert.True(after > before + 20, $"what follows the group went from {before:F1} to {after:F1}");
            // 50 ms into the snappy 0.24 s curve it is about 0.4 of its way.
            Assert.True(mid <= before + 0.75 * (after - before), $"the group's height cut open: what follows it is at {mid:F1} 50 ms into the 0.24 s open, from {before:F1} to {after:F1}");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
