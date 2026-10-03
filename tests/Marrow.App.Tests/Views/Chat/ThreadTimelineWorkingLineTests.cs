using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// The running turn's working line (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792, :2075-2197), mounted at the end of a real timeline in a
// shown window: the card or the badge, the label, the chevron and the body it opens, with the reader's place in the thread watched alongside.
[Collection("Headless")]
public sealed class ThreadTimelineWorkingLineTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");

    private static TimelineEntry Reasoning(string id, string text, bool streaming = false) =>
        Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text, IsStreaming = streaming }), Turn);

    // A running turn with a prompt, two lines of thinking and three commands, and a long thread above it so the reader can be somewhere else.
    private static void RunningThread(TimelineMount mount, int steps = 3, bool thinking = true)
    {
        mount.Model.Settings.ShowReasoning = true;
        var entries = Turns(5, "old-", sentences: 10).ToList();
        entries.Add(User("prompt", Turn, "Please make the build faster"));
        if (thinking) { entries.Add(Reasoning("r1", "First I look at the workflow.")); entries.Add(Reasoning("r2", "Then the cache.", streaming: true)); }
        for (var i = 0; i < steps; i++) entries.Add(ToolEntry("tool-" + i, Turn));
        mount.SetEntries(entries);
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running);
        mount.Settle();
    }

    private static WorkingIndicator Line(TimelineMount mount) => LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();

    private static Border Frame(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Child is StackPanel { Spacing: 10 });

    private static Panel Header(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Panel>(line).Single(panel => panel.GetType().Name == "BaselineHeader");

    private static Button Chevron(WorkingIndicator line) => LogicalQuery.SelfAndDescendants<Button>(line).First();

    private static void Click(WorkingIndicator line) => Chevron(line).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    // The baseline of a header child's first line of text, in the header's coordinates.
    private static double BaselineY(Control child, Control header) =>
        child.TranslatePoint(default, header)!.Value.Y + LogicalQuery.SelfAndDescendants<TextBlock>(child).First().TextLayout.TextLines[0].Baseline;

    private static double AngleOf(WorkingIndicator line) =>
        Assert.IsType<Avalonia.Media.RotateTransform>(LogicalQuery.SelfAndDescendants<SwiftImage>(line).Single(image => image.SymbolName == "chevron.right").RenderTransform).Angle;

    private static double Height(WorkingIndicator line) => line.Bounds.Height;

    // The line's chevron angle and its height, every 20 ms until a second has passed.
    private static List<(double Angle, double Height)> Sample(TimelineMount mount, WorkingIndicator line, int milliseconds)
    {
        var samples = new List<(double, double)> { (AngleOf(line), Height(line)) };
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 20) { mount.Advance(20); samples.Add((AngleOf(line), Height(line))); }
        return samples;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-117")] // ThreadTimeline.swift:2096-2097,2106-2149,2281; capture clause: the card (the cousin's 1:28-1:34, map/CATALOGUE.md static.thread-timeline) and the badge (setting off: no video)
    public void The_line_is_a_380_point_card_or_a_word_hugging_badge_and_opens_and_closes_over_one_animation_following_the_end_only_on_opening() => ThreadTimelineRig.Run(headless, mount =>
    {
        RunningThread(mount);
        var line = Line(mount); var frame = Frame(line); var header = Header(line);
        var title = header.Children[0]; var time = header.Children[1]; var chevron = header.Children[2];
        // The card (:2144-2147): 380 wide, 14 leading and trailing, 11 above and below, a continuous rounded rectangle at the pill's radius for the hit shape.
        Assert.Equal(380, frame.Bounds.Width, 3); Assert.Equal(new Thickness(14, 11), frame.Padding); Assert.Equal(TimelineMetrics.PillRadius, frame.CornerRadius.TopLeft);
        Assert.NotNull(frame.Background);
        // :2122 an HStack aligned on the first text baseline, spacing 8; :2124 the title takes the full width, so the time and the chevron sit at the trailing edge.
        Assert.Equal(frame.Bounds.Width - 28, header.Bounds.Width, 3);
        Assert.Equal(title.Bounds.Right + 8, time.Bounds.Left, 1); Assert.Equal(time.Bounds.Right + 8, chevron.Bounds.Left, 1); Assert.Equal(header.Bounds.Width, chevron.Bounds.Right, 1);
        Assert.Equal(BaselineY(title, header), BaselineY(time, header), 0.3);
        // :2121 the inner column has spacing 10: the bar sits 10 under the header row.
        var bar = LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single();
        Assert.Equal(10, bar.TranslatePoint(default, frame)!.Value.Y - (header.TranslatePoint(default, frame)!.Value.Y + header.Bounds.Height), 1);
        var cardWidth = line.Bounds.Width;
        // :2144-2147 the badge (the setting off): the pill's own insets, no fixed width, no bar, the title as wide as its words.
        mount.Model.Settings.ShowsWorkingCard = false; mount.Advance(300);
        Assert.Same(line, Line(mount));
        frame = Frame(line); header = Header(line); title = header.Children[0]; time = header.Children[1]; chevron = header.Children[2];
        Assert.True(double.IsNaN(frame.Width)); Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), frame.Padding);
        Assert.Empty(LogicalQuery.SelfAndDescendants<HydraProgressBar>(line));
        Assert.True(frame.Bounds.Width < 380 - 100, $"the badge hugs its words: {frame.Bounds.Width}");
        Assert.Equal(title.Bounds.Right + 8, time.Bounds.Left, 1); Assert.Equal(time.Bounds.Right + 8, chevron.Bounds.Left, 1);
        Assert.Equal(frame.Padding.Left + header.Bounds.Width + frame.Padding.Right, frame.Bounds.Width, 1);
        Assert.Equal(BaselineY(title, header), BaselineY(time, header), 0.3);
        mount.Model.Settings.ShowsWorkingCard = true; mount.Advance(300);
        Assert.Equal(cardWidth, line.Bounds.Width, 3);
        // :2107-2119 the press opens it over .snappy(duration: 0.24) and asks the timeline to follow the end; pressing it again closes it over the same animation and asks nothing.
        var tracking = mount.Field<TimelineScrollTracking>("tracking");
        mount.Scroll.Offset = new Vector(0, 100); mount.Advance(400); tracking.IsPinnedToBottom = false; tracking.EndActivity();
        var before = mount.Scroll.Offset.Y;
        Click(line);
        Assert.True(tracking.IsPinnedToBottom, "opening follows the end");
        var open = Sample(mount, line, 700);
        Assert.Equal(90, open[^1].Angle, 1); Assert.True(open[^1].Height > open[0].Height + 20);
        Assert.True(mount.Scroll.Offset.Y > before + 50, "the timeline went to the end with it");
        tracking.IsPinnedToBottom = false; mount.Scroll.Offset = new Vector(0, 100); mount.Advance(400); tracking.EndActivity(); var held = mount.Scroll.Offset.Y;
        Click(line);
        var close = Sample(mount, line, 700);
        Assert.Equal(0, close[^1].Angle, 1); Assert.Equal(open[0].Height, close[^1].Height, 1);
        Assert.False(tracking.IsPinnedToBottom, "closing follows nothing");
        Assert.Equal(held, mount.Scroll.Offset.Y, 3);
        // The one animation both ways: the chevron's turn and the card's height follow the same curve forwards and backwards.
        var steps = Math.Min(open.Count, close.Count);
        for (var i = 0; i < steps; i++)
        {
            Assert.Equal(open[i].Angle / 90, 1 - close[i].Angle / 90, 0.02);
            Assert.Equal((open[i].Height - open[0].Height) / (open[^1].Height - open[0].Height), 1 - (close[i].Height - open[0].Height) / (open[^1].Height - open[0].Height), 0.03);
        }
        Assert.True(open.Count(sample => sample.Angle is > 1 and < 89) >= 3, "an animation, not a cut");
    });

    private static string[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "").ToArray();

    private static double DistanceFromEnd(TimelineMount mount) => mount.Scroll.Extent.Height - mount.Scroll.Offset.Y - mount.Scroll.Viewport.Height;

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // ThreadTimeline.swift:2107-2119,2152-2176,813-829 (the reveal that bypasses the jump-button guard); the cousin's 1:34-1:50 working line opened (no video of the open body)
    public void Opening_the_line_follows_the_end_unconditionally_opens_the_thinking_and_the_steps_and_rests_the_motion_while_it_glides() => ThreadTimelineRig.Run(headless, mount =>
    {
        RunningThread(mount);
        var line = Line(mount); var tracking = mount.Field<TimelineScrollTracking>("tracking");
        // Closed to start (:2091, the chronological flag is false), with the tooltip and the hint that name what a press does.
        Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line)); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
        var button = Chevron(line);
        Assert.Equal("Show these steps", ToolTip.GetTip(button)); Assert.Equal("Shows what the agent is doing", Avalonia.Automation.AutomationProperties.GetHelpText(button));
        // The reader has scrolled far up and the jump button is showing: the guarded reveal (:813-816) holds back there, the one the working line uses (:822-829) does not.
        mount.Scroll.Offset = new Vector(0, 50); mount.Advance(400); mount.State.ShowsJumpButton = true; tracking.IsPinnedToBottom = false; tracking.EndActivity();
        Assert.True(DistanceFromEnd(mount) > mount.Scroll.Viewport.Height);
        var box = ThreadTimeline.GetRevealTimelineEnd(line)!;
        box.Action(); mount.Advance(400);
        Assert.False(tracking.IsPinnedToBottom); Assert.True(DistanceFromEnd(mount) > mount.Scroll.Viewport.Height, "the guarded reveal left the reader where they were");
        Click(line);
        // :822-829 pins to the end, anchors growth to it, scrolls there and notes the movement: the shimmers and the card's staves rest while it glides.
        Assert.True(tracking.IsPinnedToBottom); Assert.True(tracking.IsReaderScrolling); Assert.True(ScrollActivity.Shared.Current.IsScrolling);
        // :2155-2176 the thinking and the steps arrive under the line with the soft appear, padded 14 at the sides and 11 below.
        var markdown = LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(line); Assert.Equal(2, markdown.Count());
        var thinking = Assert.IsType<StackPanel>(markdown.First().Parent); var steps = LogicalQuery.SelfAndDescendants<WorkSteps>(line).Single();
        Assert.Equal(new Thickness(24, 0, 0, 0), thinking.Margin); Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown.First())); Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(markdown.First()));
        Assert.Equal(0, thinking.Opacity); Assert.Equal(0, steps.Opacity);
        var body = LogicalQuery.SelfAndDescendants<Border>(line).Single(border => border.Padding == new Thickness(14, 0, 14, 11));
        Assert.Contains(thinking, LogicalQuery.SelfAndDescendants<StackPanel>(body)); Assert.Contains(steps, LogicalQuery.SelfAndDescendants<WorkSteps>(body));
        Assert.Equal("Hide these steps", ToolTip.GetTip(button));
        mount.Advance(1000);
        Assert.Equal(1, thinking.Opacity); Assert.Equal(1, steps.Opacity);
        Assert.True(DistanceFromEnd(mount) < 2, "the timeline followed the end to show what opened");
        Assert.False(tracking.IsReaderScrolling); Assert.False(ScrollActivity.Shared.Current.IsScrolling);
        // :2091 the expansion is the line's own state: a step landing in the turn hands the line new work and leaves it open (the same line, its bar still the same bar).
        var bar = LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single(); var rows = LogicalQuery.SelfAndDescendants<ToolRow>(line).Count();
        mount.SetEntries(mount.Runtime.Entries.Append(ToolEntry("tool-new", Turn)).ToList()); mount.Advance(600);
        Assert.Same(line, Line(mount)); Assert.Same(bar, LogicalQuery.SelfAndDescendants<HydraProgressBar>(line).Single());
        Assert.Equal(90, AngleOf(line), 1); Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(line)); Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(line).Count());
        Assert.True(DistanceFromEnd(mount) < 2, "and the reader following it stays at the end");
    });

    [Fact]
    [Trait("Req", "PR-CHAT-121")] // ThreadTimeline.swift:2152-2153 (the tooltip and the hint are empty when nothing can open)
    public void A_running_line_with_nothing_to_open_has_no_chevron_tooltip_or_hint_and_its_time_sits_at_the_cards_edge() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = true;
        mount.SetEntries(new[] { User("prompt", Turn, "hello") }); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = Line(mount); var header = Header(line); var button = Chevron(line);
        Assert.Equal(2, header.Children.Count);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        Assert.Equal("", ToolTip.GetTip(button)); Assert.Equal("", Avalonia.Automation.AutomationProperties.GetHelpText(button));
        // The time sits at the card's trailing padding (:2128 the empty slot a chevron would hold is not kept).
        Assert.Equal(header.Bounds.Width, header.Children[1].Bounds.Right, 1);
        // A press does nothing (:2108).
        var height = line.Bounds.Height; Click(line); mount.Advance(500);
        Assert.Equal(height, line.Bounds.Height, 3); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(line));
        // The first tool call gives it something to open: the chevron fades in and the time glides over to make room (:2128-2135, :2183).
        mount.SetEntries(new[] { User("prompt", Turn, "hello"), ToolEntry("tool-0", Turn) }); mount.Advance(20);
        var time = header.Children[1]; var chevron = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(line), image => image.SymbolName == "chevron.right");
        Assert.Equal(0, chevron.Opacity);
        mount.Advance(60); Assert.InRange(chevron.Opacity, 0.001, 0.999);
        mount.Advance(800);
        Assert.Equal(1, chevron.Opacity); Assert.Equal(header.Bounds.Width, chevron.Bounds.Right, 1); Assert.Equal(chevron.Bounds.Left - 8, time.Bounds.Right, 1);
        Assert.Equal("Show these steps", ToolTip.GetTip(Chevron(line)));
    });
}
