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

// REVIEW (adversarial round 1 of unit parity-threadtimeline): what SwiftUI keeps when the timeline's body runs again (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792).
// A view whose identity stands keeps its @State whatever its inputs do (:332-334 `.id(block.id)` on an `.equatable()` row; :1980-1989 the turn's ForEach and its `if let heads`;
// TimelineRows.swift:1569-1576 HydraHeadsList's ForEach(entries)), and the children of a view that appears come with it, with no transition of their own (the principle
// ED-NEW-parity-threadtimeline applies to the stack's first pass).
[Collection("Headless")]
public sealed class ThreadTimelineReviewTests(HeadlessSession headless)
{
    private static readonly Guid TurnA = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid TurnB = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static void Rewind(TimelineMount mount, bool supportsRewind) =>
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, ["Project", null, supportsRewind, 480.0, true]);

    private static void SetTurns(TimelineMount mount, params TurnRecord[] turns) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(mount.Runtime, new ValueList<TurnRecord>(turns));

    private static Button Header(Control row, string tip) => LogicalQuery.SelfAndDescendants<Button>(row).Single(button => ToolTip.GetTip(button) as string == tip);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-155 / the TurnRow.Update fix (e58d00915): ThreadTimeline.swift:1985-1989 `if let heads { TurnRow(group: heads) }` and TimelineRows.swift:1569-1576 ForEach(entries): a head joining the list keeps the rows already in it, with their state
    public void REVIEW_a_head_joining_a_running_turns_list_keeps_the_earlier_heads_row_and_its_state() => ThreadTimelineRig.Run(headless, mount =>
    {
        // hank carries output of its own and no linked head, so its row expands in place (TimelineRows.swift:1695-1700, :1708-1711).
        var hank = Entry("hank", new TimelineItem.Content.Tool(new ToolCall { Title = "hank", ToolCallKind = ToolCall.Kind.Agent, Output = "hank's output line" }), TurnA);
        var entries = new List<TimelineEntry> { User("prompt", TurnA, "send out heads"), hank };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var row = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row;
        var hankRow = Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row))));
        LogicalQuery.SelfAndDescendants<Button>(hankRow).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(400);
        Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(hankRow)); // opened
        // A second head goes out while hank's row is open.
        entries.Add(ToolEntry("walter", TurnA, head: true)); mount.SetEntries(entries); mount.Advance(400);
        var rows = LogicalQuery.SelfAndDescendants<ToolRow>(Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row))).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Same(hankRow, rows[0]); // the same row (its @State isExpanded, its head popover, its anchor) ...
        Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(rows[0])); // ... still open
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW ED-NEW-parity-threadtimeline (3) "a row whose block id survives is the same view": ThreadTimeline.swift:167-172 (rewindable is empty while a turn runs), :325-341 (the row's context), :332-334 `.id(block.id)`; TimelineRows.swift:2499-2509 TurnFinishedBlock's @State isExpanded
    public void REVIEW_an_opened_finished_turn_stays_open_and_is_the_same_row_when_the_next_turn_starts_on_a_provider_that_rewinds() => ThreadTimelineRig.Run(headless, mount =>
    {
        // A provider that rewinds (Claude, Codex, Copilot: Provider.SupportsRewind), with the finished turn's record, so its row may offer to revert while nothing runs.
        Rewind(mount, true);
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed });
        var entries = new List<TimelineEntry> { User("p1", TurnA, "first question"), Reply("said", TurnA, "I will run a command"), ToolEntry("run", TurnA), Reply("final", TurnA, "Done."), End("e1", TurnA) };
        mount.SetEntries(entries); mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        Header(folded.Row, "Show this turn's steps").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        Header(folded.Row, "Hide this turn's steps"); // opened
        // The reader sends the next message: a new turn runs, and while it does no row offers to revert (:167-172).
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed }, new TurnRecord { Id = TurnB, Index = 1 });
        entries.Add(User("p2", TurnB, "next question")); mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(600);
        var now = mount.Blocks().Single(block => block.Id == folded.Id);
        Assert.Same(folded.Row, now.Row); // the same view under the same id ...
        Header(now.Row, "Hide this turn's steps"); // ... still open
        // And when that turn ends the rows may offer to revert again: still the same, still open.
        mount.SetEntries(entries.Append(Reply("r2", TurnB, "answer")).Append(End("e2", TurnB)).ToArray()); mount.SetPhase(RuntimePhase.Idle); mount.Advance(600);
        Assert.Same(folded.Row, mount.Blocks().Single(block => block.Id == folded.Id).Row);
        Header(folded.Row, "Hide this turn's steps");
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW ED-NEW-parity-threadtimeline (3) "the first pass of a timeline places its views" (map/CATALOGUE.md C 164.452-164.640): ThreadTimeline.swift:1979-2001, the rows of a running turn come with its block
    public void REVIEW_a_thread_that_opens_on_a_running_turn_shows_the_turns_rows_at_once() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.SetTurnStartedAt(SwiftDate.Now);
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(mount.Runtime, RuntimePhase.Running);
        mount.SetEntries(new[] { User("prompt", TurnA, "a question"), Reply("said", TurnA, "an answer so far") });
        var block = mount.Blocks().Single(candidate => candidate.Block is DisplayBlock.Turn).Row;
        Assert.Equal(1, block.Opacity); // the block is placed with the stack (the unit's fix) ...
        var inner = LogicalQuery.SelfAndDescendants<TurnRow>(block).ToList(); Assert.NotEmpty(inner);
        Assert.All(inner, row => Assert.True(row.Opacity == 1, "a row of the running turn fades in as the thread opens: its opacity is " + row.Opacity)); // nothing in a thread that opens fades in
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingBlockView>(block));
        Assert.True(line.Opacity == 1, "the working line fades in as the thread opens: its opacity is " + line.Opacity);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW: ThreadTimeline.swift:303-321 (a turn that is only starting "arrives like a row": its block takes rowTransition), :1979-2001 (the rows inside it are the block's content, not views joining a stack on screen)
    public void REVIEW_a_turn_that_starts_arrives_as_one_row_with_its_prompt_inside_it_not_fading_in_a_second_time() => ThreadTimelineRig.Run(headless, mount =>
    {
        var history = Turns(2, sentences: 2).ToList();
        mount.SetEntries(history); mount.Settle();
        mount.SetTurnStartedAt(SwiftDate.Now);
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(mount.Runtime, RuntimePhase.Running);
        mount.SetEntries(history.Concat(new[] { User("prompt", TurnB, "the next question") }).ToList());
        var block = mount.Blocks().Single(candidate => candidate.Block is DisplayBlock.Turn { Summary: null }).Row;
        Assert.True(block.Opacity == 0, "the block arrives with the soft appear: its opacity is " + block.Opacity);
        var prompt = LogicalQuery.SelfAndDescendants<TurnRow>(block).First();
        Assert.True(prompt.Opacity == 1, "the prompt inside the arriving block fades in a second time: its own opacity is " + prompt.Opacity); // no second fade multiplied over the block's
        Assert.True(prompt.RenderTransform is null || prompt.RenderTransform.Value.M32 == 0, "no second 4-point rise inside the block's own");
    });

    private static HydraHeadInfo FinishedHead(long index, DateTimeOffset started, DateTimeOffset finished) =>
        new(index, "task " + index, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, StartedAt = started, FinishedAt = finished };

    private static void SetHeadStatus(TimelineMount mount, long index, HydraHeadInfo.Status status, DateTimeOffset? finished)
    {
        var head = mount.Model.Children(mount.Runtime.ThreadID).First(thread => thread.Hydra!.Index == index);
        mount.Model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = status, FinishedAt = finished } });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-035 ("both id literals ... are the identity the list animates across"): ThreadTimeline.swift:332-334 `.id(block.id)`, :1615-1617; TimelineRows.swift:408-415 (ForEach(finished, id: \.index) with .transition(.settleGlide) under .animation(.smooth(duration: 0.3), value: finished.map(\.index)), :441 the pill's @State isShowingReport)
    public void REVIEW_a_head_finishing_joins_the_pill_row_under_the_same_id_as_the_same_row_and_glides_in_beside_the_pills_already_there()
    {
        var s = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var heads = new[] { FinishedHead(1, s, s.AddSeconds(1000)), FinishedHead(2, s, s.AddSeconds(100)), FinishedHead(3, s, s.AddSeconds(400)), FinishedHead(4, s, s.AddSeconds(200)) };
        ThreadTimelineRig.RunWithTeam(headless, mount =>
        {
            SetHeadStatus(mount, 1, HydraHeadInfo.Status.Running, null); SetHeadStatus(mount, 3, HydraHeadInfo.Status.Running, null);
            var turn = Guid.NewGuid();
            mount.SetEntries(new[] { User("p1", turn), Reply("a1", turn), End("e1", turn) }); mount.Settle();
            var row = mount.Blocks().Single(block => block.Id == "hydra-heads-working").Row;
            var pills = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(row).ToList(); Assert.Equal(2, pills.Count);
            // Head 3 finishes: it joins the row under the same id.
            SetHeadStatus(mount, 3, HydraHeadInfo.Status.Completed, s.AddSeconds(400)); mount.Advance(20);
            var now = mount.Blocks().Single(block => block.Id == "hydra-heads-working");
            Assert.Equal(new[] { 2, 4, 3 }, Assert.IsType<DisplayBlock.HeadsWorking>(now.Block).Heads);
            Assert.Same(row, now.Row); // the same row ...
            var after = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(now.Row).ToList(); Assert.Equal(3, after.Count);
            Assert.Same(pills[0], after[0]); Assert.Same(pills[1], after[1]); // ... and the pills already there are the same views (an open report stays open), the new one gliding in beside them
        }, heads);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // REVIEW PR-CHAT-026 / PR-CHAT-160: ThreadTimeline.swift:136-141 (NewThreadPrompt(threadID:projectName:) with no .id: a new name is the same view, its .onAppear does not run again), NewThreadPrompt.swift:50-53 (@State isVisible stays true)
    public void REVIEW_a_new_project_name_on_the_empty_threads_prompt_changes_its_words_without_the_prompt_appearing_again() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        mount.Advance(500); Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        // The reader picks another project in the prompt's picker: the chat hands the timeline the new name.
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, ["Renamed", null, false, 480.0, true]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        var now = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.Contains("Renamed", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(now).Select(text => text.Text)), StringComparison.Ordinal);
        var content = Assert.IsAssignableFrom<Control>(now.Child);
        Assert.True(content.Opacity == 1, "the prompt fades in again from nothing on a project pick: its opacity is " + content.Opacity);
    });
}
