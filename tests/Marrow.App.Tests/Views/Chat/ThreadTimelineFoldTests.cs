using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// A turn that sent heads out, from running to finished, inside the timeline (DroppyCode/App/Views/Chat/ThreadTimeline.swift:1907-1926,1955-1958,1985-1989 and TimelineRows.swift:2589-2715):
// the end marker lands and the block that showed the running turn shows the folded one in the same pass, with the heads where they were; the turn's header then opens the steps
// without the heads among them, asking the timeline to reveal the end only when it opens (CATALOGUE.md C 125.743-125.78: in one frame).
[Collection("Headless")]
public sealed class ThreadTimelineFoldTests(HeadlessSession headless)
{
    private static readonly Guid Turn = Guid.Parse("12345678-abcd-1234-abcd-123456789abc");

    private static double Top(TimelineMount mount, Control control) => control.TranslatePoint(default, mount.Column)!.Value.Y;

    private static string[] Texts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(text => text.Text ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-155")] // ThreadTimeline.swift:1955-1958,1985-1989 (the running turn's order), TimelineRows.swift:1564-1577,2580-2584,2652-2654,2682-2686 (the heads list under the response, never among the steps); map/CATALOGUE.md C 125.743-125.78 (the turn ends in one frame, "Sent out a head" below the paragraph)
    public void A_turn_that_sent_heads_out_folds_in_one_pass_with_the_heads_under_the_answer_and_never_among_the_steps() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", Turn, "send out two heads"), Reply("said", Turn, "I will send two heads out"), ToolEntry("run", Turn) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var running = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        Assert.Null(Assert.IsType<DisplayBlock.Turn>(running.Block).Summary);
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(running.Row)); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraHeadsList>(running.Row));
        // The heads go out while it runs: the list comes after the turn's rows and before the working line that was already last (:1985-1989), the prompt first.
        entries.Add(ToolEntry("hank", Turn, head: true)); entries.Add(ToolEntry("walter", Turn, head: true)); mount.SetEntries(entries); mount.Advance(400);
        Assert.Same(running.Row, mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row);
        var runningHeads = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(running.Row)); Assert.Same(line, Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(running.Row)));
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(runningHeads).Count());
        Assert.True(Top(mount, LogicalQuery.SelfAndDescendants<UserMessageRow>(running.Row).Single()) < Top(mount, runningHeads), "the prompt, then the heads");
        Assert.True(Top(mount, runningHeads) + runningHeads.Bounds.Height <= Top(mount, line), "the heads above the working line");
        var identity = running.Id;
        // The turn ends: its answer lands, its marker lands and nothing runs, in one pass. The block keeps its identity and shows the folded turn at once.
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Entries))!.SetValue(mount.Runtime, new ValueList<TimelineEntry>(entries.Append(Reply("final", Turn, "Both heads are out and working.")).Append(End("end", Turn)).ToArray()));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(mount.Runtime, RuntimePhase.Idle);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        Assert.Equal(identity, folded.Id); Assert.NotNull(Assert.IsType<DisplayBlock.Turn>(folded.Block).Summary);
        Assert.Single(LogicalQuery.SelfAndDescendants<TurnFinishedBlock>(folded.Row));
        Assert.Empty(LogicalQuery.SelfAndDescendants<WorkingIndicator>(folded.Row)); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkGroup>(folded.Row)); Assert.Empty(LogicalQuery.SelfAndDescendants<TurnRunningBlock>(folded.Row));
        // Folded: the prompt, the summary line, the answer, and the heads under the answer, 8 points apart.
        var answer = Assert.Single(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(folded.Row)); var heads = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(folded.Row));
        Assert.Contains(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.MarkdownView>(answer), view => view.Text.Contains("Both heads are out", StringComparison.Ordinal));
        Assert.True(Top(mount, answer) + answer.Bounds.Height <= Top(mount, heads), "the heads under the answer");
        var badges = LogicalQuery.SelfAndDescendants<ToolRow>(heads).ToList(); Assert.Equal(2, badges.Count);
        Assert.Equal(8, Top(mount, badges[1]) - (Top(mount, badges[0]) + badges[0].Bounds.Height), 1);
        Assert.Empty(LogicalQuery.SelfAndDescendants<ToolRow>(folded.Row).Except(badges)); // the one command is a step, and no step is shown folded
        // The header opens the steps: the command is there, the heads are not among them, and they stay once, below.
        var box = mount.Field<TimelineRevealBox>("revealBox"); var reveals = 0; var original = box.Action; box.Action = () => { reveals++; original(); };
        var header = LogicalQuery.SelfAndDescendants<Button>(folded.Row).Single(button => ToolTip.GetTip(button) as string == "Show this turn's steps");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        Assert.Equal(1, reveals);
        var card = Assert.Single(LogicalQuery.SelfAndDescendants<Border>(folded.Row), border => border.Padding == new Thickness(14));
        Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(card));
        Assert.DoesNotContain(Texts(card), text => text.StartsWith("Sent out", StringComparison.Ordinal)); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraHeadsList>(card));
        var below = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(folded.Row));
        Assert.True(Top(mount, card) + card.Bounds.Height <= Top(mount, below) + 0.5, "the heads below the steps card");
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(below).Count());
        // Closing it asks for nothing.
        header = LogicalQuery.SelfAndDescendants<Button>(folded.Row).Single(button => ToolTip.GetTip(button) as string == "Hide this turn's steps");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        Assert.Equal(1, reveals); Assert.Empty(LogicalQuery.SelfAndDescendants<WorkGroup>(folded.Row)); Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(folded.Row));
    });
    [Fact]
    [Trait("Req", "PR-CHAT-155")] // ThreadTimeline.swift:1985-1989 (`if let heads { TurnRow(group: heads...) }`: the list is the same view while heads join it, and a view whose value changed is not brought in again)
    public void A_head_joining_the_list_a_running_turn_shows_does_not_bring_the_whole_list_in_again() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", Turn, "send out heads"), ToolEntry("hank", Turn, head: true) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var row = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row;
        var list = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row)); var turnRow = Assert.IsAssignableFrom<Control>(list.Parent!.Parent); Assert.IsType<TurnRow>(turnRow);
        Assert.Equal(1, turnRow.Opacity); Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(list));
        // A second head goes out: the list shows both, and it is the same row, still on screen at full strength (no soft appear over it again).
        entries.Add(ToolEntry("walter", Turn, head: true)); mount.SetEntries(entries);
        var now = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row));
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(now).Count());
        Assert.Same(turnRow, Assert.Single(LogicalQuery.SelfAndDescendants<TurnRow>(row), candidate => LogicalQuery.SelfAndDescendants<HydraHeadsList>(candidate).Any()));
        Assert.Equal(1, turnRow.Opacity);
    });
}
