using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrInlineTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-123")] // TimelineRows.swift:1473,1527-1528,1697-1698,2603-2608; ThreadTimeline.swift:2135-2139
    public void Five_real_open_actions_reveal_four_times_and_follow_only_for_working_without_revealing_on_close()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var calls = new List<(string Site, bool Animated, bool Follow)>();
            var site = "";
            var all = false;
            var turn = Guid.NewGuid();
            var tools = new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Read), Tool(ToolCall.Kind.Read), Tool(ToolCall.Kind.Read)]);
            var group = new WorkGroup(tools, h.Runtime, startsCollapsed: true);
            var steps = new WorkSteps(tools, h.Runtime, new Binding<bool>(() => all, value => all = value));
            var output = new ToolCall { ToolCallKind = ToolCall.Kind.Command, ToolCallStatus = ToolCall.Status.Completed, Title = "Command", Detail = "output" };
            var tool = new ToolRow(new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.Tool(output))), h.Runtime);
            var finished = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, tools, null, false, false);
            var working = new WorkingIndicator(h.Runtime, SwiftDate.Now, 1, new ValueList<ThinkingStep>([new("Thinking", false)]), ValueList<TimelineEntry>.Empty, ValueList<TimelineEntry>.Empty, false);
            var host = new StackPanel { Children = { group, steps, tool, finished, working } };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox
            {
                Action = () => calls.Add((site, Transaction.Current is not null, false)),
                Follow = () => calls.Add((site, Transaction.Current is not null, true)),
            });
            Avalonia.Controls.Window? window = null;
            try
            {
                h.Main.OnMain(() => { window = Show(host); window.Height = 2400; Settle(window); });
                h.Main.OnMain(() => { site = "group"; Click(window!, LogicalQuery.SelfAndDescendants<Button>(group).First()); });
                h.Main.OnMain(() => { site = "steps"; Click(window!, LogicalQuery.SelfAndDescendants<Button>(steps).Single(button => Words(button).Contains("1 earlier step"))); });
                h.Main.OnMain(() => { site = "tool"; Click(window!, LogicalQuery.SelfAndDescendants<Button>(tool).First()); });
                h.Main.OnMain(() => { site = "finished"; Click(window!, LogicalQuery.SelfAndDescendants<Button>(finished).Single(button => Equals(ToolTip.GetTip(button), "Show this turn's steps"))); });
                h.Main.OnMain(() => { site = "working"; Click(window!, LogicalQuery.SelfAndDescendants<Button>(working).Single(button => Equals(ToolTip.GetTip(button), "Show these steps"))); });
                h.Main.OnMain(() =>
                {
                    Assert.Equal([("group", false, false), ("steps", true, false), ("tool", true, false), ("finished", true, false), ("working", true, true)], calls);
                    site = "closing";
                    Click(window!, LogicalQuery.SelfAndDescendants<Button>(group).First());
                    Click(window!, LogicalQuery.SelfAndDescendants<Button>(tool).First());
                    Click(window!, LogicalQuery.SelfAndDescendants<Button>(finished).Single(button => Equals(ToolTip.GetTip(button), "Hide this turn's steps")));
                    Click(window!, LogicalQuery.SelfAndDescendants<Button>(working).Single(button => Equals(ToolTip.GetTip(button), "Hide these steps")));
                });
                h.Main.OnMain(() => Assert.Equal(5, calls.Count));
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }

    [Theory]
    [InlineData("Hydra merged !95.", true, true)]
    [InlineData("gaze-site: Hydra merged !95.", true, true)]
    [InlineData("Hydra opened !95 but could not merge it.", true, false)]
    [InlineData("Hydra merged !95.", false, false)]
    [Trait("Req", "PR-HYDRAMERGE-104")] // TimelineRows.swift:1020-1027,1036-1039
    public void Flourish_needs_a_previous_merge_and_the_exact_substring_and_resets_at_1100_even_after_detachment(string title, bool previous, bool success)
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            HydraMergeRow? row = null;
            Avalonia.Controls.Window? window = null;
            try
            {
                h.Main.OnMain(() =>
                {
                    h.Runtime.IsHydraMerging = previous;
                    row = new HydraMergeRow(h.Runtime, previous ? new HydraMergePhase.Merging() : new HydraMergePhase.Outcome(new UserMessage { Text = title }));
                    window = Show(row);
                    if (previous) { h.Runtime.IsHydraMerging = false; row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = title })); }
                });
                h.Main.OnMain(() =>
                {
                    Assert.Equal(success, Property<bool>(Field(row!, "state")!, "Flourish"));
                    Assert.Equal(success ? HydraMergeTrack.Mode.Done : HydraMergeTrack.Mode.Resting, Field(LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!).Single(), "mode"));
                });
                h.Main.AdvanceMilliseconds(1099);
                h.Main.OnMain(() =>
                {
                    Assert.Equal(success, Property<bool>(Field(row!, "state")!, "Flourish"));
                    window!.Content = null; // Swift's unstructured task survives disappearing.
                });
                h.Main.AdvanceMilliseconds(1);
                h.Main.OnMain(() => Assert.False(Property<bool>(Field(row!, "state")!, "Flourish")));
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }

    private static string[] Words(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "").ToArray();
}
