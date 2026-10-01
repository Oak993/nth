using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// The streamed branch of spawnDelegatedHeads(for:) (App/Runtime/ThreadRuntime.swift:2845-2879, B3): the block was read while
// the reply streamed, and the end of the turn finishes it from the stream rather than reading it again. The lead is a Hydra
// lead with no pair; a cap of one with a busy head leaves the pair no room, so what is queued stays waiting.
public sealed class ThreadRuntimeStreamedBlockFinishTests
{
    private static string Entry(string task, string prompt = "Do it.") =>
        "{\"task\":\"" + task + "\",\"prompt\":\"" + prompt + "\"}";

    private static string Entries(int from, int to) =>
        string.Join(",", Enumerable.Range(from, to - from + 1).Select(index => Entry("Task " + index)));

    private static string Sent(string block) => block.Replace("```hydra\n", "```hydra-sent\n", StringComparison.Ordinal);

    private static HydraDelegationBudget Rounds(int rounds)
    {
        var budget = new HydraDelegationBudget();
        for (var index = 0; index < rounds; index++) budget = budget.ChargeRound();
        return budget;
    }

    private const string NoteOf10 =
        "Not every task went out\n10 of the 40 tasks in that block went to heads. The last 30 did not: ask for them again once these report back.";

    [Fact]
    [Trait("Req", "PR-RUNTIME-215")] // :2847 the stream's own entry id is scanned when a stream exists; nothing is sent twice
    [Trait("Req", "PR-RUNTIME-216")] // :2848-2854 the block is marked sent and the stream is cleared; the parser stays
    [Trait("Req", "PR-RUNTIME-261")] // :2849 the streamed branch sets hydraStream = nil directly and leaves delegationParser in place
    public void The_end_of_a_streamed_turn_finishes_the_block_from_the_stream_without_sending_a_task_twice()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        var text = "Sending two.\n```hydra\n[" + Entries(1, 2) + "]\n```";
        rig.Delta("reply-1", text);
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(Sent(text), rig.Text("reply-1"));
        Assert.Null(rig.Stream);
        // The scan of the whole text made a fresh parser (isDelta false), and it lives on until the next startTurn.
        var parser = Assert.IsType<HydraDelegationStreamParser>(rig.Parser);
        Assert.Equal("reply-1", parser.EntryID);
        Assert.Equal(2, parser.Delegations.Count);
        Assert.Equal(["Task 1", "Task 2"], rig.Waiting.Select(task => task.Delegation.Task)); // each went out once
        Assert.Equal(1, rig.Budget.Rounds);
        Assert.Empty(rig.Notes());
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-215")] // :2847 a stream pinned to another row than the resolved one: that row's text is scanned under the stream's entry id
    [Trait("Req", "PR-RUNTIME-216")] // :2848 the branch works on the stream's entry, which is marked sent; the resolved row is left alone
    public void A_stream_pinned_to_an_earlier_row_scans_the_resolved_rows_text_under_its_own_entry_id()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        var streamed = "```hydra\n[" + Entry("A1") + ",";
        rig.Delta("reply-a", streamed); // one task out, the stream pinned to reply-a
        var later = "```hydra\n[" + Entry("B1") + "," + Entry("B2") + "," + Entry("B3") + "]\n```";
        rig.Delta("reply-b", later); // another row: read by nobody while the stream is pinned
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        // The last row holding a block is reply-b; its text goes in under reply-a's id, so the first (already sent) task is skipped.
        Assert.Equal(["A1", "B2", "B3"], rig.Waiting.Select(task => task.Delegation.Task));
        Assert.Equal("reply-a", rig.Parser!.EntryID);
        Assert.Equal(Sent(streamed), rig.Text("reply-a"));
        Assert.Equal(later, rig.Text("reply-b"));
        Assert.Null(rig.Stream);
        Assert.Empty(rig.Notes()); // the re-read of reply-a's block has one task, and three went out
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-215")] // :2847 with no stream the resolved row id is used and a fresh parser reads its text from the top
    public void With_no_stream_the_resolved_row_is_scanned_under_its_own_id()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Earlier words.");
        var text = "```hydra\n[" + Entries(1, 3) + "]\n```";
        rig.Reply("reply-2", text);
        rig.EndTurn();
        Assert.Null(rig.Stream);

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal("reply-2", rig.Parser!.EntryID);
        Assert.Equal(turn, rig.Parser.TurnID); // the finished turn
        Assert.Equal(["Task 1", "Task 2", "Task 3"], rig.Waiting.Select(task => task.Delegation.Task));
        Assert.Equal(Sent(text), rig.Text("reply-2"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-216")] // :2845 no sent > 0 test here: a stream that sent nothing still takes the branch
    public void A_stream_that_sent_nothing_still_marks_the_row_sent_and_is_not_read_as_an_empty_block()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        var text = "Nothing to send.\n```hydra\n[]\n```";
        rig.Reply("reply-1", text);
        rig.EndTurn();
        rig.SetStream(new DelegationStreamState(turn, "reply-1", Guid.NewGuid(), 0));

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(Sent(text), rig.Text("reply-1")); // the streamed branch marks it; the empty-block path would have stripped it
        Assert.Empty(rig.Notes()); // and would have said "No heads went out: the lead did this itself."
        Assert.Null(rig.Stream);
        Assert.Equal(0, rig.Budget.Rounds);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-216")] // :2851 total is the re-read block's count, which can exceed what the last streaming read sent
    [Trait("Req", "PR-RUNTIME-218")] // :2870-2875 the note from the stream's counts: sent after the catch-up, the re-read total, the difference
    [Trait("Req", "UI-RUNTIME-021")] // :2870-2875 the exact two-line note
    public void A_block_cut_off_after_forty_tasks_is_re_read_for_its_total_and_names_what_did_not_go_out()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        // Forty closed tasks, then the reply ends with no closing bracket or fence: the streaming reader counts them, and a
        // reader that wants the whole block does not.
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 40));
        Assert.Equal(32, rig.Stream!.Sent); // the ceiling of a block
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(
            "Not every task went out\n32 of the 40 tasks in that block went to heads. The last 8 did not: ask for them again once these report back.",
            Assert.Single(rig.Notes()));
        Assert.Equal(32, rig.Waiting.Length);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-217")] // :2858-2868 what the last read missed goes out against the same batch, clamped to the 32-task ceiling
    [Trait("Req", "PR-RUNTIME-218")] // :2870-2875 the note counts sent after the top-up
    [Trait("Req", "PR-RUNTIME-258")] // :2863 the top-up stamps queuedAt: Date.now
    [Trait("Req", "UI-RUNTIME-021")] // :2870-2875 the exact two-line note
    public void A_block_with_forty_tasks_and_ten_streamed_tops_up_to_the_ceiling_and_names_the_rest()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 10) + ",");
        var batch = rig.Stream!.BatchID;
        // The rest of the block closes after the last flush (nothing is reading the reply at that moment).
        rig.SetLeadHydra(false);
        rig.Delta("reply-1", Entries(11, 40) + "]\n```");
        rig.SetLeadHydra(true);
        // The resolved row is a later one, so the scan reads that row and only the top-up sees reply-1's whole block.
        rig.Reply("reply-2", "```hydra\n[]\n```");
        rig.EndTurn();
        rig.H.Main.Advance(TimeSpan.FromSeconds(9));
        var now = rig.OnMain(() => SwiftDate.Now);
        Assert.Equal(10, rig.Waiting.Length);

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        var waiting = rig.Waiting;
        Assert.Equal(Enumerable.Range(1, 32).Select(index => "Task " + index), waiting.Select(task => task.Delegation.Task)); // 10 + min(30, 32 - 10)
        Assert.All(waiting, task => Assert.Equal(batch, task.BatchID)); // the batch the stream already owned
        Assert.All(waiting.Skip(10), task => Assert.Equal(now, task.QueuedAt));
        Assert.Single(rig.Batches);
        Assert.Equal(1, rig.Budget.Rounds); // not charged again
        Assert.Equal(new DelegationStreamState(turn, "reply-1", batch, 32), rig.Stream); // reassigned with the new count
        Assert.Equal(
            "Not every task went out\n32 of the 40 tasks in that block went to heads. The last 8 did not: ask for them again once these report back.",
            Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-217")] // :2858 the same block at the round cap: nothing is topped up, and the note names ten of forty
    [Trait("Req", "PR-RUNTIME-218")] // :2870-2875 the record's own example: 10 of the 40, the last 30
    [Trait("Req", "UI-RUNTIME-021")] // :2870-2875 the exact two-line note
    public void A_block_with_forty_tasks_and_ten_streamed_is_not_topped_up_once_the_request_has_no_rounds_left()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 10) + ",");
        rig.SetLeadHydra(false);
        rig.Delta("reply-1", Entries(11, 40) + "]\n```");
        rig.SetLeadHydra(true);
        rig.Reply("reply-2", "```hydra\n[]\n```");
        rig.EndTurn();
        rig.Budget = Rounds(3);

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(10, rig.Waiting.Length);
        Assert.Null(rig.Stream); // a top-up that took nothing leaves the stream cleared
        Assert.Equal(NoteOf10, Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-217")] // :2859 the prefix is clamped at zero: a stream that already sent 32 takes nothing more, and the stream is not reassigned
    public void A_stream_that_already_sent_thirty_two_takes_nothing_more_and_is_not_reassigned()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 32) + ",");
        Assert.Equal(32, rig.Stream!.Sent);
        rig.SetLeadHydra(false);
        rig.Delta("reply-1", Entries(33, 40) + "]\n```");
        rig.SetLeadHydra(true);
        rig.Reply("reply-2", "```hydra\n[]\n```");
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        Assert.Equal(32, rig.Waiting.Length);
        Assert.Null(rig.Stream);
        Assert.Equal(
            "Not every task went out\n32 of the 40 tasks in that block went to heads. The last 8 did not: ask for them again once these report back.",
            Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-219")] // :2877-2879 settleBatches runs before the emptiness test: a block whose heads are all back returns .none
    public void A_block_whose_heads_all_reported_during_the_turn_settles_and_returns_none()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 1) + "]\n```");
        var stream = rig.Stream!;
        var report = ThreadRuntimeDelegationRig.Report(7);
        rig.SetBatch(stream.BatchID, [], [report]); // the one head came back before the turn ended
        rig.EndTurn();

        Assert.Equal("None", rig.SpawnDelegatedHeads(turn));

        Assert.Empty(rig.Batches);
        Assert.Equal([report], rig.PendingReports); // handed over, so finishTurn flushes them in the same breath
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-219")] // :2879 with a head still out the lead is parked (headsOut)
    public void A_block_with_a_head_still_out_returns_heads_out_and_keeps_the_batch()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 1) + "]\n```");
        rig.EndTurn();

        Assert.Equal("HeadsOut", rig.SpawnDelegatedHeads(turn));

        var head = Assert.Single(rig.Heads);
        Assert.Equal([head.Id], Assert.Single(rig.Batches).Pending);
        Assert.Empty(rig.PendingReports);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-219")] // :2876 holdSentDelegationBlock is started on the streamed entry id
    [Trait("Req", "PR-RUNTIME-229")] // :2953 the card holds, fades, and a reply that was only the block reads "Sent out heads."
    [Trait("Req", "UI-RUNTIME-030")] // :2953 exactly "Sent out heads."
    public void The_sent_card_leaves_a_reply_that_was_only_the_block_reading_sent_out_heads()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entries(1, 1) + "]\n```");
        rig.EndTurn();

        rig.SpawnDelegatedHeads(turn);

        Assert.Contains("```hydra-sent\n", rig.Text("reply-1"), StringComparison.Ordinal);
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Entries.Single(entry => entry.Id == "reply-1").Item.TimelineItemContent
            is TimelineItem.Content.Assistant { Value.Text: "Sent out heads." }, AppModelHarness.Bound);
    }
}
