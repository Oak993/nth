using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// What a view whose id stands keeps when the block's value or context changes under it (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792: :332-334 `.id(block.id)` on an
// `.equatable()` row, :1896-1936 DisplayBlockView's body, :1980-1989 the turn's ForEach and its `if let heads`; TimelineRows.swift:1569-1576 HydraHeadsList's ForEach(entries),
// :2499-2509 TurnFinishedBlock's @State). Every kind of block is handed its new value in place; only a block whose branch changed is a different view. The fixer's tests of the
// first adversarial round (ThreadTimelineReviewTests) cover the finished turn's fold, the pills' row and the running turn's heads; these cover what else a pass hands again.
// ED-NEW-parity-threadtimeline (3) (the placeholder the lead numbers at the merge).
[Collection("Headless")]
public sealed class ThreadTimelineKeepTests(HeadlessSession headless)
{
    private static readonly Guid TurnA = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid TurnB = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static void Inputs(TimelineMount mount, string? project, bool supportsRewind) =>
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, [project, null, supportsRewind, 480.0, true]);

    private static void SetTurns(TimelineMount mount, params TurnRecord[] turns) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(mount.Runtime, new ValueList<TurnRecord>(turns));

    private static TimelineEntry EndWith(string id, Guid turn, double duration, int files) => Entry(id, new TimelineItem.Content.TurnEnd(new TurnSummary
    {
        TurnID = turn, Status = TurnStatus.Completed, Duration = duration, FilesChanged = files, Additions = 40, Deletions = 7,
        Changes = files == 0 ? null : new FileChangeSummary(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, files).Select(i => new FileChangeSummary.File("src/file" + i + ".cs", i, 1, false))), 40, 7),
    }), turn);

    private static bool Offers(Control row, string tip) => LogicalQuery.SelfAndDescendants<Button>(row).Any(button => ToolTip.GetTip(button) as string == tip);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-035 and PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:167-172,332-334; TimelineRows.swift:2599-2602 ForEach(userEntries), :2688-2699 the file card's `canUndo`: the rewind flag flips at every turn start and end, and the rows it reaches are the same views
    public void A_finished_turns_message_and_file_card_stay_the_same_views_while_the_rewind_flag_flips_and_show_what_it_decides() => ThreadTimelineRig.Run(headless, mount =>
    {
        Inputs(mount, "Project", true);
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed });
        var entries = new List<TimelineEntry> { User("p1", TurnA, "first question"), Reply("final", TurnA, "Done."), EndWith("e1", TurnA, 2, 2) };
        mount.SetEntries(entries); mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var user = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row)); var card = Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(folded.Row));
        Assert.True(Offers(user, "Edit message")); Assert.True(Offers(card, "Revert this turn's files and conversation")); // nothing runs: the turn may be reverted
        // The reader sends the next message: while a turn runs no row offers to revert.
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed }, new TurnRecord { Id = TurnB, Index = 1 });
        entries.Add(User("p2", TurnB, "next question")); mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(600);
        var now = mount.Blocks().Single(block => block.Id == folded.Id);
        Assert.Same(folded.Row, now.Row);
        Assert.Same(user, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(now.Row))); Assert.Same(card, Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(now.Row)));
        Assert.False(Offers(user, "Edit message"), "the message offers to edit while a turn runs"); Assert.False(Offers(card, "Revert this turn's files and conversation"), "the card offers to revert while a turn runs");
        // The turn ends: they may again.
        entries.Add(Reply("r2", TurnB, "answer")); entries.Add(EndWith("e2", TurnB, 3, 0)); mount.SetEntries(entries); mount.SetPhase(RuntimePhase.Idle); mount.Advance(600);
        Assert.Same(folded.Row, mount.Blocks().Single(block => block.Id == folded.Id).Row);
        Assert.Same(user, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row))); Assert.Same(card, Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(folded.Row)));
        Assert.True(Offers(user, "Edit message")); Assert.True(Offers(card, "Revert this turn's files and conversation"));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-104 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:332-334, TimelineRows.swift:2610 the header's line: a turn's summary handed again changes what its header says, and the fold stays as the reader left it
    public void A_finished_turns_summary_handed_again_changes_its_header_line_and_not_its_views_or_its_fold() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("p1", TurnA, "question"), Reply("said", TurnA, "I will run it"), ToolEntry("run", TurnA), Reply("final", TurnA, "Done."), EndWith("e1", TurnA, 2, 0) };
        mount.SetEntries(entries); mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var header = LogicalQuery.SelfAndDescendants<Button>(folded.Row).Single(button => ToolTip.GetTip(button) as string == "Show this turn's steps");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        var steps = Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(folded.Row));
        Assert.Contains("Worked for 2s", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(folded.Row).Select(text => text.Text)), StringComparison.Ordinal);
        // The turn's end marker is replaced by one that says another duration.
        entries[^1] = EndWith("e1b", TurnA, 5, 0); mount.SetEntries(entries); mount.Advance(400);
        var now = mount.Blocks().Single(block => block.Id == folded.Id);
        Assert.Equal(5, Assert.IsType<DisplayBlock.Turn>(now.Block).Summary!.Duration); // the block says what the new marker says
        Assert.Same(folded.Row, now.Row);
        Assert.Contains("Worked for 5s",string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(now.Row).Select(text => text.Text)), StringComparison.Ordinal);
        Assert.Same(steps, Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(now.Row))); // still opened, the steps the same views
        Assert.NotNull(LogicalQuery.SelfAndDescendants<Button>(now.Row).SingleOrDefault(button => ToolTip.GetTip(button) as string == "Hide this turn's steps"));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:2039-2071 TimelineGroupView, TimelineRows.swift:1572 ForEach(entries): a group of a run with no turn that grows under its id is the same row
    public void A_work_group_that_grows_under_its_id_is_the_same_row_and_the_same_group_with_its_steps_following() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { ToolEntry("t1"), ToolEntry("t2") };
        mount.SetEntries(entries); mount.Settle();
        var group = mount.Blocks().Single(); var work = Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(group.Row));
        var first = LogicalQuery.SelfAndDescendants<ToolRow>(work).ToList(); Assert.Equal(2, first.Count);
        // A third step: the group shows the last two and says how many it holds back (TimelineRows.swift:1524-1545), and the row that stays is the same view.
        entries.Add(ToolEntry("t3")); mount.SetEntries(entries); mount.Advance(400);
        var now = mount.Blocks().Single(); Assert.Equal(group.Id, now.Id);
        Assert.Same(group.Row, now.Row); Assert.Same(work, Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(now.Row)));
        var rows = LogicalQuery.SelfAndDescendants<ToolRow>(work).ToList(); Assert.Equal(2, rows.Count);
        Assert.Same(first[1], rows[0]); // the step that was there and stays is the same view
        Assert.Contains("1 earlier step", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(work).Select(text => text.Text)), StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:1985-1989 `if let heads` (the branch is the view's identity, not the first head's id), TimelineRows.swift:1569-1576 `ForEach(entries)`: the list is made of the rows of the heads it holds, one that leaves takes its row, the rest stay
    public void A_head_leaving_the_heads_list_takes_its_row_and_the_others_stay() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", TurnA, "send out heads"), ToolEntry("hank", TurnA, head: true), ToolEntry("walter", TurnA, head: true) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var row = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row; var list = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row));
        var rows = LogicalQuery.SelfAndDescendants<ToolRow>(list).ToList(); Assert.Equal(2, rows.Count);
        entries.RemoveAt(1); mount.SetEntries(entries); mount.Advance(400);
        Assert.Same(list, Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row)));
        Assert.Same(rows[1], Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(list)));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-121 and UI-CHAT-043 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:2261-2274 (WorkingBlockView hands WorkingIndicator its startedAt and finishedAt), :2075-2092,2107-2119 (@State isExpanded, which no change of a `let` disturbs)
    public void An_opened_running_line_that_becomes_an_earlier_segment_stays_the_same_line_and_stays_open() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", TurnA, "a question"), ToolEntry("ran", TurnA) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var first = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(first.Row));
        LogicalQuery.SelfAndDescendants<Button>(line).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        Assert.Equal("Hide these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(line).First())); // opened
        // A Hydra note lands in the thread and the turn goes on after it: the run so far is an earlier segment, its line finished (a stopped clock, no live bar).
        entries.Add(Entry("note", new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Info, Message = "a note between the runs" })));
        entries.Add(Reply("after", TurnA, "and on it goes")); mount.SetEntries(entries); mount.Advance(600);
        var now = mount.Blocks().Single(block => block.Id == first.Id);
        Assert.Same(first.Row, now.Row); Assert.False(Assert.IsType<DisplayBlock.Turn>(now.Block).ShowsWorking);
        Assert.Same(line, Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(now.Row))); // the same line ...
        Assert.Equal("Hide these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(line).First())); // ... still open ...
        Assert.Equal(entries[0].Item.Date, line.StartedAt); // ... and now counting from the segment's own first entry, an earlier segment's line
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-026 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:136-141, NewThreadPrompt.swift:15-55: the line's other branch (a name, no name) is other words in the same prompt, which does not appear again
    public void The_empty_threads_prompt_goes_between_asking_in_a_project_and_asking_alone_without_appearing_again() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)); mount.Advance(500);
        string Words() => string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Select(text => text.Text));
        Assert.Contains("What should we build in ", Words(), StringComparison.Ordinal);
        Inputs(mount, null, false); mount.Advance(20);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
        Assert.Equal("What should we build?", Words()); Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        Inputs(mount, "Another", false); mount.Advance(20);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
        Assert.Contains("Another", Words(), StringComparison.Ordinal); Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        Inputs(mount, "A third", false); mount.Advance(20); // a name for a name: the same button, its words changed
        Assert.Contains("A third", Words(), StringComparison.Ordinal); Assert.DoesNotContain("Another", Words(), StringComparison.Ordinal);
    });
}
