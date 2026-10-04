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
// width eases) each move along the source's animation; with the system asking for reduced motion each reaches its end state on the first frame after the change.
// The same changes under motion are read part way by the REVIEW2 tests, so these say only that reduced motion removes the motion and nothing else.
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
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-022 under reduced motion: the steps are there whole on the first frame, and what follows has moved the whole way
    public void REVIEW2_with_reduced_motion_opening_the_work_group_shows_its_steps_whole_on_the_first_frame() => headless.OnUiThread(() =>
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
            Run(h.Main, frames, window, 10);
            var first = 0.0;
            var shown = 0.0;
            h.Main.OnMain(() => { first = Below(); shown = Effective(Assert.Single(Mounted<WorkSteps>(group))); });
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Below());
            Assert.True(after > before + 20, $"what follows the group went from {before:F1} to {after:F1}");
            Assert.Equal(after, first, 0.5);
            Assert.Equal(1, shown, 0.01);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-114 under reduced motion: the strike is whole on the first frame
    public void REVIEW2_with_reduced_motion_checking_a_step_off_strikes_it_through_whole_on_the_first_frame() => headless.OnUiThread(() =>
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
            Run(h.Main, frames, window, 10);
            var first = 0.0;
            h.Main.OnMain(() => first = Strike());
            Run(h.Main, frames, window, 1000);
            var end = 0.0;
            h.Main.OnMain(() => end = Strike());
            Assert.True(end > 0.1, $"the done step is not struck through once settled ({end:F3})");
            Assert.Equal(end, first, 0.01);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-CHAT-149 under reduced motion: the card is its new width on the first frame
    public void REVIEW2_with_reduced_motion_the_delegation_card_is_its_new_width_on_the_first_frame() => headless.OnUiThread(() =>
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
            Run(h.Main, frames, window, 10);
            var first = 0.0;
            h.Main.OnMain(() => first = Pill().Bounds.Width);
            Run(h.Main, frames, window, 1000);
            var after = 0.0;
            h.Main.OnMain(() => after = Pill().Bounds.Width);
            Assert.True(after > before + 60, $"the card went from {before:F1} to {after:F1} wide");
            Assert.Equal(after, first, 0.5);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
