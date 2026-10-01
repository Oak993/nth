using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// A delegation block read while its reply streams, and finished at the end of the turn: scanStreamedDelegations (App/Runtime/
// ThreadRuntime.swift:2985-3015, B3) and the streamed branch of spawnDelegatedHeads (:2848-2879). A block is read from a delta
// as it flushes, heads go out as their entries close, and the end of the turn finishes the block from the same stream.
public sealed class ThreadRuntimeStreamedDelegationTests
{
    private static string Entry(string task, string prompt = "Do it.") =>
        "{\"task\":\"" + task + "\",\"prompt\":\"" + prompt + "\"}";

    private static string Task(HydraDelegation delegation) => delegation.Task;

    [Fact]
    [Trait("Req", "PR-RUNTIME-227")] // :3001-3014 the first streamed read opens one batch and charges one round; later reads only extend it
    [Trait("Req", "PR-RUNTIME-261")] // :2990-2998 deltas of one row across three flushes feed one parser
    [Trait("Req", "PR-RUNTIME-258")] // :3011 each queued tuple carries the time it was queued
    public void A_block_streamed_in_three_flushes_opens_one_batch_spends_one_round_and_feeds_one_parser()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1); // no room: the tasks stay waiting
        var turn = rig.BeginTurn();

        rig.Delta("reply-1", "Sending heads.\n```hydra\n[" + Entry("Task 1") + ",");
        var opened = rig.OnMain(() => SwiftDate.Now);
        var stream = rig.Stream!;
        var parser = rig.Parser;
        Assert.Equal(new DelegationStreamState(turn, "reply-1", stream.BatchID, 1), stream);
        Assert.Equal(1, rig.Budget.Rounds); // charged on the first read, once
        var batch = Assert.Single(rig.Batches);
        Assert.Equal(stream.BatchID, batch.Id);
        Assert.Empty(batch.Pending);

        rig.H.Main.Advance(TimeSpan.FromSeconds(7));
        rig.Delta("reply-1", Entry("Task 2") + ",");
        rig.Delta("reply-1", Entry("Task 3") + "]\n```");

        Assert.Equal(3, rig.Stream!.Sent);
        Assert.Equal(stream.BatchID, rig.Stream.BatchID); // the batch is reused, nothing re-initialised
        Assert.Equal(1, rig.Budget.Rounds); // still one round for the block read five times over
        Assert.Single(rig.Batches);
        Assert.Same(parser, rig.Parser); // one parser walked all three flushes
        Assert.Equal(["Task 1", "Task 2", "Task 3"], rig.Waiting.Select(task => Task(task.Delegation)));
        Assert.All(rig.Waiting, task => Assert.Equal(stream.BatchID, task.BatchID));
        Assert.Equal(opened, rig.Waiting[0].QueuedAt);
        Assert.Equal(opened + TimeSpan.FromSeconds(7), rig.Waiting[1].QueuedAt); // stamped when queued, not when spawned
        Assert.Equal(rig.Waiting[1].QueuedAt, rig.Waiting[2].QueuedAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-227")] // :3011-3014 with room, the tasks go out while the reply still streams, into the one batch
    public void A_streamed_task_goes_out_at_once_when_the_pair_has_room()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();

        rig.Delta("reply-1", "```hydra\n[" + Entry("Task 1") + "," + Entry("Task 2") + ",");

        var heads = rig.Heads;
        Assert.Equal(["Task 1", "Task 2"], heads.Select(head => head.Hydra!.Task));
        var batch = Assert.Single(rig.Batches);
        Assert.Equal(heads.Select(head => head.Id).OrderBy(id => id), batch.Pending.OrderBy(id => id));
        Assert.All(heads, head => Assert.Equal(batch.Id, head.Hydra!.BatchID));
        Assert.Empty(rig.Waiting);
        Assert.Equal(turn, rig.Stream!.TurnID);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-227")] // :3010 the round cap is tested last, and only while no stream exists
    public void With_no_round_left_a_first_streamed_read_queues_nothing()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.Budget = Rounds(3);
        rig.BeginTurn();

        rig.Delta("reply-1", "```hydra\n[" + Entry("Task 1") + ",");

        Assert.Null(rig.Stream);
        Assert.Empty(rig.Batches);
        Assert.Empty(rig.Waiting);
        Assert.Equal(3, rig.Budget.Rounds);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-227")] // :3009-3010 nothing fresh: no round is spent for a reading that holds no closed task
    public void A_read_with_no_closed_task_spends_no_round()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.BeginTurn();

        rig.Delta("reply-1", "Thinking.\n```hydra\n[{\"task\":\"Task 1\",\"prompt\":\"Not yet");

        Assert.Null(rig.Stream);
        Assert.Equal(0, rig.Budget.Rounds);
        Assert.Empty(rig.Batches);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-227")] // :3010 a block that already owns a stream keeps queuing after its opening read spent the last round
    public void Tasks_that_close_after_the_opening_read_still_go_out_on_a_requests_last_round()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.Budget = Rounds(2);
        rig.BeginTurn();

        rig.Delta("reply-1", "```hydra\n[" + Entry("Task 1") + ",");
        Assert.Equal(3, rig.Budget.Rounds);
        Assert.False(rig.Budget.CanDelegate);
        rig.Delta("reply-1", Entry("Task 2") + "]\n```");

        Assert.Equal(["Task 1", "Task 2"], rig.Waiting.Select(task => Task(task.Delegation)));
        Assert.Equal(2, rig.Stream!.Sent);
        Assert.Equal(3, rig.Budget.Rounds);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-261")] // :2987-2988 a delta for another row while a stream is pinned to the first leaves the parser in place
    public void A_delta_for_another_row_while_a_stream_is_pinned_leaves_the_parser_alone()
    {
        using var rig = new ThreadRuntimeDelegationRig(maxHeads: 1, busyHeads: 1);
        rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entry("Task 1") + ",");
        var parser = rig.Parser;
        var stream = rig.Stream;

        rig.Delta("reply-2", "A second row, with its own words.");

        Assert.Same(parser, rig.Parser);
        Assert.Equal("reply-1", rig.Parser!.EntryID);
        Assert.Equal(stream, rig.Stream);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-261")] // :2989-2998 with no stream pinned, another row's delta starts a fresh parser, and so does a non-delta call
    public void A_delta_for_another_row_with_no_stream_pinned_and_a_non_delta_call_start_a_fresh_parser()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "First row.");
        var first = rig.Parser!;
        Assert.Equal("reply-1", first.EntryID);

        rig.Delta("reply-1", " More of it.");
        Assert.Same(first, rig.Parser); // the same row's next delta continues the held parser

        rig.Delta("reply-2", "Second row.");
        var second = rig.Parser!;
        Assert.NotSame(first, second);
        Assert.Equal("reply-2", second.EntryID);

        rig.Delta("reply-1", " Back to the first.");
        Assert.NotSame(second, rig.Parser); // a return to the first row starts a fresh parser, which reads only the newest delta
        Assert.Equal("reply-1", rig.Parser!.EntryID);

        var held = rig.Parser;
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "scanStreamedDelegations", "reply-1", turn, "Whole text.", false));
        Assert.NotSame(held, rig.Parser); // isDelta false: a fresh parser reads the text from the top
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-261")] // :2987-2988 the gates leave delegationParser untouched: no Hydra launch, entry of another turn
    public void A_scan_that_fails_its_gates_leaves_the_parser_untouched()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "First row.");
        var held = rig.Parser;

        rig.SetLeadHydra(false); // no Hydra launch
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "scanStreamedDelegations", "reply-1", turn, "x", false));
        Assert.Same(held, rig.Parser);

        rig.SetLeadHydra(true); // an entry that is not in that turn
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "scanStreamedDelegations", "reply-1", Guid.NewGuid(), "x", false));
        Assert.Same(held, rig.Parser);
        rig.OnMain(() => ThreadRuntimeReflection.Call<object>(rig.Runtime, "scanStreamedDelegations", "no-such-row", turn, "x", false));
        Assert.Same(held, rig.Parser);
    }

    private static HydraDelegationBudget Rounds(int rounds)
    {
        var budget = new HydraDelegationBudget();
        for (var index = 0; index < rounds; index++) budget = budget.ChargeRound();
        return budget;
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-261")] // :1204 startTurn ends the delegation stream and its parser, so the next block is read from where it starts
    public void Starting_a_turn_ends_the_stream_and_clears_the_parser()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Codex);
        ThreadRuntimeReflection.Set(rig.Runtime, "hydraStream", (Guid.NewGuid(), "entry", Guid.NewGuid(), 4L));
        ThreadRuntimeReflection.Set(rig.Runtime, "delegationParser", new HydraDelegationStreamParser(Guid.NewGuid(), "entry"));
        object? stream = new object(), parser = new object();

        rig.Finish(rig.StartTurn("Go.", whileStarting: () =>
        {
            stream = ThreadRuntimeReflection.Get<object>(rig.Runtime, "hydraStream");
            parser = ThreadRuntimeReflection.Get<object>(rig.Runtime, "delegationParser");
        }));

        Assert.Null(stream);
        Assert.Null(parser);
    }
}
