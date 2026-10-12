using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// Lean pass L14 (ED-NEW-lean-14), the equivalence guard for the opened working line's thinking (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792,
// :2156-2168 `ForEach` of the steps, MarkdownView.swift:21-23 its `==`): after every change the line shows one MarkdownView per step, in order, with the step's
// text and streaming flag, which is what remaking them all drew; a step that did not change keeps its view, and a streamed delta remakes the last step alone.
[Collection("Headless")]
public sealed class ThreadTimelineThinkingStepsTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("3f6a2c9e-71b4-4d0a-8e15-9b2c4d6e8f01");

    private static TimelineEntry Reasoning(string id, string text, bool streaming) =>
        Entry(id, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text, IsStreaming = streaming }), Turn);

    private static MarkdownView[] Steps(TimelineMount mount)
    {
        var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();
        var stack = LogicalQuery.SelfAndDescendants<StackPanel>(line).Single(panel => panel.Children.Count > 0 && panel.Children.All(child => child is MarkdownView));
        return stack.Children.Cast<MarkdownView>().ToArray();
    }

    private static void AssertShows(TimelineMount mount, params (string Text, bool Streaming)[] steps)
    {
        var views = Steps(mount);
        Assert.Equal(steps, views.Select(view => (view.Text, view.IsStreaming)).ToArray());
    }

    private static void Stream(TimelineMount mount, TimelineEntry entry, string text, bool streaming = true)
    {
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = text, IsStreaming = streaming }) };
        mount.Advance(50);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-14")]
    public void The_opened_thinking_shows_every_step_as_remade_and_keeps_the_view_of_each_step_that_did_not_change() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Model.Settings.ShowReasoning = true;
        var first = Reasoning("r1", "First I look at the workflow.", streaming: false);
        var second = Reasoning("r2", "Then the cache.", streaming: false);
        var third = Reasoning("r3", "Now the", streaming: true);
        var entries = new List<TimelineEntry> { User("prompt", Turn, "Please make the build faster"), first, second, third };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var line = LogicalQuery.SelfAndDescendants<WorkingIndicator>(mount.View).Single();
        LogicalQuery.SelfAndDescendants<Button>(line).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        AssertShows(mount, ("First I look at the workflow.", false), ("Then the cache.", false), ("Now the", true));
        var before = Steps(mount);

        // A streamed delta: the last step alone is made again; the two above it are the same views.
        Stream(mount, third, "Now the build");
        AssertShows(mount, ("First I look at the workflow.", false), ("Then the cache.", false), ("Now the build", true));
        var after = Steps(mount);
        Assert.Same(before[0], after[0]); Assert.Same(before[1], after[1]); Assert.NotSame(before[2], after[2]);

        // The step finishes streaming: same text, another flag, so it is made again.
        Stream(mount, third, "Now the build", streaming: false);
        AssertShows(mount, ("First I look at the workflow.", false), ("Then the cache.", false), ("Now the build", false));
        Assert.NotSame(after[2], Steps(mount)[2]); Assert.Same(before[0], Steps(mount)[0]);

        // A new step arrives at the end: the three before it stay.
        var kept = Steps(mount);
        var fourth = Reasoning("r4", "And the tests.", streaming: true);
        entries.Add(fourth); mount.SetEntries(entries); mount.Advance(50);
        AssertShows(mount, ("First I look at the workflow.", false), ("Then the cache.", false), ("Now the build", false), ("And the tests.", true));
        Assert.Equal(kept, Steps(mount).Take(3));

        // A step in the middle changes (its text emptied leaves the list): the steps after it move up, each shown with its own text, and the list is one shorter.
        Stream(mount, second, "", streaming: false);
        AssertShows(mount, ("First I look at the workflow.", false), ("Now the build", false), ("And the tests.", true));
        Assert.Same(kept[0], Steps(mount)[0]);
    });
}
