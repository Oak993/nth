using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// spawnDelegatedHeads(for:) (App/Runtime/ThreadRuntime.swift:2795-2890, B3), the paths a reply takes when its block was not
// read while it streamed: the row it picks, an unreadable block, an empty one, a spent request, and the normal send-out.
// The lead is a Hydra lead with no pair; a cap of one with a busy head leaves no room, so tasks stay waiting.
// A readable block is normally read by scanStreamedDelegations (:2847), which opens a stream the streamed branch then finishes
// (ThreadRuntimeStreamedBlockFinishTests). The send-out path below it is reached when that scan did nothing: here, a stream
// left over from another turn (rig.StaleStream) makes the scan return at its gate and the streamed branch skip, as in the source.
public sealed class ThreadRuntimeSpawnDelegatedHeadsTests
{
    private const string TwoLineNotePrefix = "Not every task went out\n";

    private static string Sent(string block) => block.Replace("```hydra\n", "```hydra-sent\n", StringComparison.Ordinal);

    [Fact]
    [Trait("Req", "PR-RUNTIME-223")] // :2868-2878 chargeRound, markingDelegationBlockSent, a fresh empty batch, the first 32 queued
    [Trait("Req", "PR-RUNTIME-224")] // :2879-2885 a 40-task block writes the two-line note from the block counts
    [Trait("Req", "PR-RUNTIME-225")] // :2886-2890 headsOut while something is waiting
    [Trait("Req", "PR-RUNTIME-258")] // :2878 each queued tuple carries the time it was queued
    [Trait("Req", "UI-RUNTIME-022")] // :2879-2885 the exact note text for a 40-task block
    public void A_forty_task_block_queues_thirty_two_against_one_new_batch_charges_one_round_and_names_the_rest()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        var block = ThreadRuntimeDelegationRig.Block(40, "Sending heads.\n\n");
        rig.Reply("reply-1", block);
        rig.EndTurn();
        var stale = rig.StaleStream();
        rig.H.Main.Advance(TimeSpan.FromSeconds(5));
        var queuedAt = rig.OnMain(() => SwiftDate.Now);

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(stale, rig.Stream); // this path never touches a stream
        Assert.Equal(1, rig.Budget.Rounds); // chargeRound, once, though only 32 of 40 go
        var batch = Assert.Single(rig.Batches);
        Assert.Empty(batch.Pending); // pending is filled only as each head actually goes out
        var waiting = rig.Waiting;
        Assert.Equal(32, waiting.Length);
        Assert.All(waiting, task => Assert.Equal(batch.Id, task.BatchID));
        Assert.Equal(Enumerable.Range(1, 32).Select(index => "Task " + index), waiting.Select(task => task.Delegation.Task));
        Assert.All(waiting, task => Assert.Equal(queuedAt, task.QueuedAt));
        Assert.Equal(32, rig.OnMain(() => rig.Runtime.HydraWaitingCount));
        Assert.Empty(rig.Heads); // the pair has no room: nothing went out
        Assert.Equal(Sent(block), rig.Text("reply-1")); // the block stays in the reply, in its sent state
        var note = Assert.Single(rig.Notes());
        Assert.Equal(
            "Not every task went out\n32 of the 40 tasks in that block went to heads. The last 8 did not: ask for them again once these report back.",
            note);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-224")] // :2879 strictly greater than 32: exactly 32 writes nothing, 33 writes "The last 1 did not"
    [Trait("Req", "UI-RUNTIME-022")] // :2879-2885 written only for a block of 33 or more
    public void A_block_of_exactly_thirty_two_writes_no_note_and_thirty_three_writes_the_last_one_did_not()
    {
        using var exact = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = exact.BeginTurn();
        exact.Reply("reply-1", ThreadRuntimeDelegationRig.Block(32));
        exact.EndTurn();
        exact.StaleStream();
        exact.SpawnDelegatedHeads(turn);
        Assert.Equal(32, exact.Waiting.Length);
        Assert.Empty(exact.Notes());

        using var over = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        turn = over.BeginTurn();
        over.Reply("reply-1", ThreadRuntimeDelegationRig.Block(33));
        over.EndTurn();
        over.StaleStream();
        over.SpawnDelegatedHeads(turn);
        Assert.Equal(32, over.Waiting.Length);
        Assert.Equal(
            "Not every task went out\n32 of the 33 tasks in that block went to heads. The last 1 did not: ask for them again once these report back.",
            Assert.Single(over.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-225")] // :2886-2890 a cap of one with two tasks sends one, leaves one waiting and returns headsOut
    public void A_two_task_block_with_room_for_one_sends_one_and_leaves_the_other_waiting()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1);
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", ThreadRuntimeDelegationRig.Block(2));
        rig.EndTurn();
        rig.StaleStream();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        var head = Assert.Single(rig.Heads);
        Assert.Equal("Task 1", head.Hydra!.Task);
        var waiting = Assert.Single(rig.Waiting);
        Assert.Equal("Task 2", waiting.Delegation.Task);
        var batch = Assert.Single(rig.Batches); // not settled: a head is out and a task is waiting
        Assert.Equal(waiting.BatchID, batch.Id);
        Assert.Equal(head.Hydra.BatchID, batch.Id);
        Assert.Equal([head.Id], batch.Pending);
        Assert.Equal(Sent(ThreadRuntimeDelegationRig.Block(2)), rig.Text("reply-1"));
        Assert.Empty(rig.Notes());
    }

    [Theory]
    [InlineData("", "Done, with no heads.")]
    [InlineData("All of it is in place.\n\n", "All of it is in place.")]
    [Trait("Req", "PR-RUNTIME-221")] // :2848-2858 an empty block: the lead did the work itself
    [Trait("Req", "UI-RUNTIME-023")] // :2856 the one-line note
    [Trait("Req", "UI-RUNTIME-028")] // :2852 the emptied reply reads "Done, with no heads."
    public void An_empty_block_leaves_the_reply_notes_that_the_lead_did_it_itself_and_spends_no_turn(string before, string expected)
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", before + "```hydra\n[]\n```");
        rig.EndTurn();

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(expected, rig.Text("reply-1"));
        Assert.Equal("No heads went out: the lead did this itself.", Assert.Single(rig.Notes()));
        Assert.Empty(rig.Batches); // no batch, no round, no refusal
        Assert.Empty(rig.Waiting);
        Assert.Equal(0, rig.Budget.Rounds);
        Assert.Equal(0, rig.Budget.Refusals);
        Assert.Empty(rig.Heads);
    }

    [Theory]
    [InlineData("", "Asking for heads.")]
    [InlineData("I will ask for heads.\n\n", "I will ask for heads.")]
    [Trait("Req", "PR-RUNTIME-220")] // :2839-2846 a block that cannot be read: stripped, "Asking for heads.", the lead is told in a turn
    [Trait("Req", "UI-RUNTIME-027")] // :2842 the emptied reply reads "Asking for heads."
    public void An_unreadable_block_is_stripped_and_the_lead_is_told_in_a_turn_of_its_own(string before, string expected)
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", before + "```hydra\nthis is not json\n```");
        rig.EndTurn();

        Assert.Equal("TurnStarted", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(expected, rig.Text("reply-1"));
        Assert.Equal(1, rig.Budget.Refusals);
        Assert.Equal(0, rig.Budget.Rounds); // the attempt costs no round
        Assert.Empty(rig.Heads);
        var told = HydraPrompts.UnreadableBlockMessage(null); // reason is always nil at this call site
        rig.H.Main.Loop.RunUntilIdle();
        var row = rig.OnMain(() => rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>().Single(user => user.Value.Text == told).Value);
        Assert.Empty(row.HydraHeads!); // a turn of the lead's own, not the user's words
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-220")] // :2840 a row with no block at all returns .none untouched
    public void A_reply_with_no_block_is_left_untouched()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Just an answer.");
        rig.EndTurn();

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("Just an answer.", rig.Text("reply-1"));
        Assert.Empty(rig.Notes());
        Assert.Equal(0, rig.Budget.Refusals);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-220")] // :2846 the dropped sentence of an unreadable block, past the refusal cap
    [Trait("Req", "UI-RUNTIME-024")] // :2926 line one "No heads went out", line two the sentence and "The block was dropped from the reply."
    public void An_unreadable_block_past_the_refusal_cap_is_dropped_with_a_note_and_a_notice_for_the_next_turn()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "```hydra\nnot json\n```");
        rig.EndTurn();
        rig.Budget = Exhaust(new HydraDelegationBudget());

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("Asking for heads.", rig.Text("reply-1"));
        Assert.Equal(
            "No heads went out\nIts delegation block could not be read, and it had been told so already. The block was dropped from the reply.",
            Assert.Single(rig.Notes()));
        Assert.Equal("[Hydra] " + HydraPrompts.UnreadableBlockMessage(null) + "\n\n---\n\n", rig.Budget.DropNotice);
    }

    private static HydraDelegationBudget Exhaust(HydraDelegationBudget budget)
    {
        for (var index = 0; index < HydraDelegationBudget.MaxRefusals; index++) budget = budget.Refuse("earlier").Budget;
        return budget;
    }

    private static HydraDelegationBudget WithRounds(HydraDelegationBudget budget, int rounds)
    {
        for (var index = 0; index < rounds; index++) budget = budget.ChargeRound();
        return budget;
    }

    [Theory]
    [InlineData("", "Asking for more heads.")]
    [InlineData("Heads are out.\n\n", "Heads are out.")]
    [Trait("Req", "PR-RUNTIME-222")] // :2859-2867 a request that has had all its rounds: stripped, "Asking for more heads.", the lead is told
    [Trait("Req", "UI-RUNTIME-029")] // :2862 the emptied reply reads "Asking for more heads."
    public void A_request_with_no_rounds_left_sends_no_heads_and_tells_the_lead_why(string before, string expected)
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", ThreadRuntimeDelegationRig.Block(2, before));
        rig.EndTurn();
        rig.Budget = WithRounds(new HydraDelegationBudget(), 3);

        Assert.Equal("TurnStarted", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(expected, rig.Text("reply-1"));
        Assert.Empty(rig.Heads);
        Assert.Empty(rig.Batches); // no batch is made
        Assert.Empty(rig.Waiting);
        Assert.Equal(3, rig.Budget.Rounds); // not charged again
        Assert.Equal(1, rig.Budget.Refusals);
        var told = HydraPrompts.HeldBackMessage(2); // count: delegations.count, not the round cap
        rig.H.Main.Loop.RunUntilIdle();
        var row = rig.OnMain(() => rig.Runtime.Entries.Select(entry => entry.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>().Single(user => user.Value.Text == told).Value);
        Assert.Empty(row.HydraHeads!);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-222")] // :2866 past the refusal cap: the note carries maxDelegationRounds, and the refusal rides on the next turn
    [Trait("Req", "UI-RUNTIME-024")] // :2926 the round-cap variant of the dropped note
    public void A_request_with_no_rounds_left_past_the_refusal_cap_is_dropped_with_the_round_cap_note()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", ThreadRuntimeDelegationRig.Block(1));
        rig.EndTurn();
        rig.Budget = WithRounds(Exhaust(new HydraDelegationBudget()), 3);

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("Asking for more heads.", rig.Text("reply-1"));
        Assert.Equal(
            "No heads went out\nThis request has had all 3 of its rounds of heads, and the lead had been told so already. The block was dropped from the reply.",
            Assert.Single(rig.Notes()));
        Assert.Equal("[Hydra] " + HydraPrompts.HeldBackMessage(1) + "\n\n---\n\n", rig.Budget.DropNotice);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-222")] // :2860 the comparison is strictly less than 3: the third round is allowed and the fourth refused
    public void The_third_round_of_a_request_is_allowed_and_the_fourth_is_refused()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.Budget = WithRounds(new HydraDelegationBudget(), 2);
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", ThreadRuntimeDelegationRig.Block(1));
        rig.EndTurn();
        rig.StaleStream(); // the send-out path, which holds the round gate
        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));
        Assert.Equal(3, rig.Budget.Rounds);

        rig.OnMain(() => ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", null));
        var next = rig.BeginTurn("More.");
        rig.Reply("reply-2", ThreadRuntimeDelegationRig.Block(1));
        rig.EndTurn();
        Assert.Equal("TurnStarted", rig.SpawnDelegatedHeads(next));
        Assert.Equal("Asking for more heads.", rig.Text("reply-2"));
        Assert.Equal(3, rig.Budget.Rounds);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-214")] // :2795-2802 the LAST reply row holding a block wins; earlier rows and rows after it are left alone
    public void The_last_reply_row_holding_a_block_is_the_one_sent_out()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        var first = ThreadRuntimeDelegationRig.Block(1, "First.\n\n");
        var middle = ThreadRuntimeDelegationRig.Block(2, "Second.\n\n");
        rig.Reply("reply-1", first);
        rig.Reply("reply-2", middle);
        rig.Reply("reply-3", "No block here.");
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(first, rig.Text("reply-1"));
        Assert.Equal(Sent(middle), rig.Text("reply-2"));
        Assert.Equal("No block here.", rig.Text("reply-3"));
        Assert.Equal(["Task 1", "Task 2"], rig.Waiting.Select(task => task.Delegation.Task));
        Assert.Equal("reply-2", rig.Parser!.EntryID); // the scan read the row that was picked
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-214")] // :2795-2802 a block in the last row
    public void A_block_in_the_last_reply_row_is_sent_out()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Thinking aloud.");
        var last = ThreadRuntimeDelegationRig.Block(1, "Done.\n\n");
        rig.Reply("reply-2", last);
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("Thinking aloud.", rig.Text("reply-1"));
        Assert.Equal(Sent(last), rig.Text("reply-2"));
        Assert.Equal("reply-2", rig.Parser!.EntryID);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-214")] // :2795-2802 with no block anywhere the row is the last reply row of THIS turn, and nothing comes of it
    public void With_no_block_the_last_reply_row_of_the_turn_is_the_row_that_is_read()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var earlier = rig.BeginTurn();
        rig.Reply("old-reply", "From before.");
        rig.EndTurn();
        var turn = rig.BeginTurn("Next.");
        rig.Reply("reply-1", "One.");
        rig.Reply("reply-2", "Two.");
        rig.Reply("reply-3", "Three.");
        rig.EndTurn();

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("reply-3", rig.Parser!.EntryID); // the fallback: the last reply row of the turn
        Assert.Equal(turn, rig.Parser.TurnID);
        Assert.NotEqual(earlier, turn);
        Assert.Empty(rig.Notes());
        Assert.Equal(0, rig.Budget.Rounds);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-214")] // :2795 no Hydra launch (a chat with no Hydra) never delegates: no side effects at all
    public void A_chat_with_no_hydra_never_delegates()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        var block = ThreadRuntimeDelegationRig.Block(2);
        rig.Reply("reply-1", block);
        rig.EndTurn();
        rig.SetLeadHydra(false);

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(block, rig.Text("reply-1")); // the block is not marked, the budget not charged
        Assert.Equal(0, rig.Budget.Rounds);
        Assert.Empty(rig.Batches);
        Assert.Empty(rig.Waiting);
        Assert.Empty(rig.Heads);
        Assert.Null(rig.Parser);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-214")] // :2795 a turn with no reply row at all returns .none
    public void A_turn_with_no_reply_row_does_nothing()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.EndTurn();

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));
        Assert.Empty(rig.Notes());
        Assert.Equal(0, rig.Budget.Rounds);
    }
}
