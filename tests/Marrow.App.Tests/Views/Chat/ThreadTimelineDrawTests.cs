using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// What a pass of the timeline's body keeps (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792, :332-334 `.id(block.id)`, :388-391 the stack of rows,
// :357-359 the one scroll view): a row whose id survives is the same view, in the same place, and is never taken out of the tree and put back, so a popover
// anchored in it, a selection, a hover and the focus stay. ED-NEW-parity-threadtimeline (the placeholder the lead numbers at the merge).
[Collection("Headless")]
public sealed class ThreadTimelineDrawTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 and UI-CHAT-001 (the rows sit in one plain stack inside the one scroll view); ThreadTimeline.swift:332-334,388-391; the engineering decision ED-NEW-parity-threadtimeline (a pass diffs the stack, it does not rebuild it)
    public void A_pass_of_the_body_keeps_every_surviving_row_in_the_tree_in_its_stack_and_in_its_place() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(6, sentences: 6).ToList();
        mount.SetEntries(entries); mount.Settle();
        var before = TimelineRowWatch.Of(mount);
        Assert.Equal(6, before.Rows.Count);
        // A new message at the end.
        entries.AddRange(Turns(1, "n1-", sentences: 6)); mount.SetEntries(entries); mount.Advance(400);
        Assert.Equal(7, mount.Blocks().Length); before.AssertKept(mount, "a block appended");
        // A setting the body reads, and the body asked to run again.
        mount.Model.Settings.ShowReasoning = !mount.Model.Settings.ShowReasoning; mount.Advance(200);
        before.AssertKept(mount, "a setting the body reads");
        mount.Field<Action>("redraw")(); mount.Advance(100);
        before.AssertKept(mount, "the body run again");
        // A turn starts: the working line arrives under the rows, and goes when the turn is over.
        mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(400);
        Assert.Equal(8, mount.Blocks().Length); before.AssertKept(mount, "a turn started");
        mount.SetPhase(RuntimePhase.Idle); mount.Advance(400);
        Assert.Equal(7, mount.Blocks().Length); before.AssertKept(mount, "the turn is over");
        // A question badge comes and goes at the end of the stack, under the rows.
        var request = new QuestionRequest("q", new([new QuestionRequest.Question("choice", "Header", "Choose", new([]), false, false, false)]));
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(mount.Runtime, new ValueList<QuestionRequest>(new[] { request })); mount.Advance(400);
        before.AssertKept(mount, "a question arrived");
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(mount.Runtime, new ValueList<QuestionRequest>([])); mount.Advance(400);
        before.AssertKept(mount, "a question was answered");
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 (the thread opens at once with its rows in place; map/CATALOGUE.md C 164.452-164.640: the thread opens in one frame); ThreadTimeline.swift:317-321,388-391; ED-NEW-parity-threadtimeline
    public void A_thread_opens_with_its_rows_already_there_and_a_row_that_joins_it_later_arrives_softly() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Enumerable.Range(0, 3).Select(i => User("note-" + i, null, "a note " + i)).ToList(); // rows with the soft appear: a finished turn's block has none
        mount.SetEntries(entries);
        // The first pass places its rows with the stack, as a parent brings its children when it appears: no arrival animation on any of them.
        Assert.Equal(3, mount.Blocks().Length);
        Assert.All(mount.Blocks(), block => { Assert.Equal(1, block.Row.Opacity); Assert.Equal(0, Assert.IsType<Avalonia.Media.TranslateTransform>(block.Row.RenderTransform).Y); });
        // A row joining the stack once it is on screen arrives with the soft appear (a finished turn's block is the one kind that has no transition at all, :307-311).
        entries.Add(User("late", null, "a note that comes later")); mount.SetEntries(entries);
        var arriving = mount.Blocks()[^1].Row;
        Assert.Equal(0, arriving.Opacity); Assert.Equal(4, Assert.IsType<Avalonia.Media.TranslateTransform>(arriving.RenderTransform).Y);
        mount.Advance((int)(Animation.SoftAppear.Duration * 1000) + 50);
        Assert.Equal(1, arriving.Opacity);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-026")] // ThreadTimeline.swift:136-141 (`.onAppear` runs when the prompt appears, not at every pass of the body while it stays)
    public void An_empty_thread_keeps_its_prompt_through_every_pass_of_the_body_and_clears_the_chrome_and_the_jump_button_when_it_appears() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        mount.Advance(500);
        Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        // Anything the body reads running it again (a setting, the body asked to run) leaves the prompt as it is, with its fade over and its on-appear effects not run again.
        mount.State.ShowsJumpButton = true; mount.Chrome.Update(100);
        mount.Model.Settings.ShowReasoning = !mount.Model.Settings.ShowReasoning; mount.Advance(100);
        mount.Field<Action>("redraw")(); mount.Advance(100);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
        Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        Assert.True(mount.State.ShowsJumpButton); Assert.True(mount.Chrome.Progress > 0);
        // The prompt leaving and appearing again is a new appearance: its effects run.
        mount.SetEntries(Turns(1).ToList()); mount.Advance(400); // the prompt fades out over 0.3 s (OD-0076)
        Assert.Empty(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        mount.State.ShowsJumpButton = true; mount.Chrome.Update(100);
        mount.SetEntries([]);
        var again = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        Assert.NotSame(prompt, again); Assert.False(mount.State.ShowsJumpButton); Assert.Equal(0, mount.Chrome.Progress);
        // :50-53 it fades and scales in from 0.97 over .easeOut(duration: 0.3): invisible at the start, on its way, then there.
        var content = Assert.IsAssignableFrom<Control>(again.Child);
        Assert.Equal(0, content.Opacity); Assert.Equal(0.97, Assert.IsType<Avalonia.Media.ScaleTransform>(content.RenderTransform).ScaleX, 6);
        mount.Advance(150);
        Assert.InRange(content.Opacity, 0.2, 0.99); Assert.InRange(Assert.IsType<Avalonia.Media.ScaleTransform>(content.RenderTransform).ScaleX, 0.97, 0.999);
        mount.Advance(300);
        Assert.Equal(1, content.Opacity); Assert.Equal(1, Assert.IsType<Avalonia.Media.ScaleTransform>(content.RenderTransform).ScaleX, 6);
    });

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status, DateTimeOffset started, DateTimeOffset? finished) =>
        new(index, "task " + index, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = status, StartedAt = started, FinishedAt = finished };

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-035 (the finished heads' row stands above a running turn and after it once it is over, under the one id); ThreadTimeline.swift:1608-1617,332-334; ED-NEW-parity-threadtimeline
    public void A_row_that_changes_its_place_among_the_rows_moves_in_the_stack_and_is_the_same_view() => ThreadTimelineRig.RunWithTeam(headless, mount =>
    {
        var s = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        // Head 1 is still at work, 2 and 4 finished before the second turn began: their row shows above that turn while it runs.
        var one = mount.Model.Children(mount.Runtime.ThreadID).First(thread => thread.Hydra!.Index == 1);
        mount.Model.UpdateThread(one.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running, FinishedAt = null } });
        var turns = new[] { User("p1", first), Reply("a1", first), End("e1", first), User("p2", second), Reply("a2", second) };
        mount.SetEntries(turns); mount.SetTurnStartedAt(s.AddSeconds(300)); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var blocks = mount.Blocks();
        var ids = blocks.Select(block => block.Id).ToList();
        var pills = blocks.Single(block => block.Id == "hydra-heads-working").Row;
        var pillsBlock = blocks.Single(block => block.Id == "hydra-heads-working").Block;
        Assert.True(ids.IndexOf("hydra-heads-working") < ids.Count - 1, "the finished heads stand above the running turn: " + string.Join(", ", ids));
        var before = TimelineRowWatch.Of(mount); before.AssertInOrder(mount); // the heads arrived after the turn while it was idle, and the turn running put them above it
        // The turn stops running and nothing finished in it: the same heads, now after the turn (its end marker follows a moment later).
        mount.SetPhase(RuntimePhase.Idle); mount.Advance(400);
        blocks = mount.Blocks(); ids = blocks.Select(block => block.Id).ToList();
        Assert.Equal("hydra-heads-working", ids[^1]);
        Assert.Equal(pillsBlock, blocks[^1].Block);
        Assert.Same(pills, blocks[^1].Row);
        mount.SetEntries(turns.Append(End("e2", second)).ToArray()); mount.Advance(400);
        Assert.Same(pills, mount.Blocks()[^1].Row);
        Assert.DoesNotContain(before.Detached, id => id.StartsWith("hydra-heads-working", StringComparison.Ordinal));
        before.AssertInOrder(mount);
    }, [Head(1, HydraHeadInfo.Status.Running, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), null), Head(2, HydraHeadInfo.Status.Completed, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_000_100)), Head(4, HydraHeadInfo.Status.Completed, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_000_200))]);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 and UI-CHAT-001 (what is anchored in a row stays while the thread changes: the popover and the tooltip of a row, ThreadTimeline.swift:332-334); ED-NEW-parity-threadtimeline
    public void A_tooltip_open_on_a_row_stays_open_while_new_entries_arrive_and_the_body_runs_again() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = Turns(4, sentences: 6).ToList();
        mount.SetEntries(entries); mount.Settle();
        var text = mount.Rows[1].GetVisualDescendants().OfType<TextBlock>().First();
        ToolTip.SetTip(text, "A tooltip on a row"); ToolTip.SetIsOpen(text, true); mount.Advance(300);
        Assert.True(ToolTip.GetIsOpen(text)); Assert.Contains(mount.Window.GetVisualDescendants().OfType<TextBlock>(), shown => shown.Text == "A tooltip on a row");
        // A message arrives, a setting the body reads changes, the body is run again: the row it is on never left the tree, so the tooltip is still there.
        entries.AddRange(Turns(1, "late-", sentences: 6)); mount.SetEntries(entries); mount.Advance(300);
        mount.Model.Settings.ShowReasoning = !mount.Model.Settings.ShowReasoning; mount.Advance(200); mount.Field<Action>("redraw")(); mount.Advance(200);
        Assert.True(ToolTip.GetIsOpen(text), "the tooltip is still open"); Assert.Contains(mount.Window.GetVisualDescendants().OfType<TextBlock>(), shown => shown.Text == "A tooltip on a row");
        ToolTip.SetIsOpen(text, false);
    }, layers: true);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 (a page of older blocks lands above the viewport and the rows below it stay); ThreadTimeline.swift:257-268,332-334; ED-NEW-parity-threadtimeline
    public void A_page_of_older_blocks_arriving_above_leaves_the_rows_already_there_where_they_are() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var entries = Turns(60, sentences: 6).ToList();
        mount.SetEntries(entries); mount.Advance(2500);
        Assert.Equal(24, mount.Blocks().Length);
        var before = TimelineRowWatch.Of(mount);
        var pill = mount.Scroll.GetVisualDescendants().OfType<Button>().Single(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text?.StartsWith("Show ", StringComparison.Ordinal) == true));
        pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(400);
        Assert.Equal(48, mount.Blocks().Length);
        before.AssertKept(mount, "a page of older blocks");
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-045 (a followed run past ninety-six blocks lets the oldest rows go); ThreadTimeline.swift:703-707; ED-NEW-parity-threadtimeline
    public void Rows_dropped_from_the_window_leave_and_the_rows_that_stay_are_not_touched() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var entries = Turns(100, sentences: 4).ToList();
        mount.SetEntries(entries); mount.Settle();
        for (var page = 0; page < 5 && mount.Blocks().Length < 100; page++) { mount.Scroll.Offset = default; mount.Advance(400); }
        Assert.Equal(100, mount.Blocks().Length);
        mount.Scroll.Offset = new Vector(0, Math.Max(0, mount.Scroll.Extent.Height - mount.Scroll.Viewport.Height)); mount.Advance(400);
        var before = TimelineRowWatch.Of(mount);
        entries.AddRange(Turns(1, "new-", sentences: 4)); mount.SetEntries(entries); mount.Advance(400);
        Assert.Equal(24, mount.Blocks().Length);
        // The 77 rows above the new window left, and the 23 that were in it are the same views.
        var gone = before.Rows.Keys.Take(before.Rows.Count - 23).ToArray();
        Assert.Equal(gone.Length, before.Detached.Count(id => !id.EndsWith(" (text)", StringComparison.Ordinal)));
        before.Detached.Clear();
        before.AssertKept(mount, "a followed run past the window", gone);
    });
}
