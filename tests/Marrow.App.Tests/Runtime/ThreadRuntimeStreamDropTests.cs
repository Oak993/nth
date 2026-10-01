using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// dropDelegationBlock(for:status:) (App/Runtime/ThreadRuntime.swift:2800-2829, B3), the branches that depend on a delegation
// stream: a stopped or failed turn whose stream had already sent heads, and one whose stream had sent none. The branch with
// no stream is covered by ThreadRuntimeDropDelegationBlockTests.
public sealed class ThreadRuntimeStreamDropTests
{
    private static string Entry(string task) => "{\"task\":\"" + task + "\",\"prompt\":\"Do it.\"}";

    [Theory]
    [InlineData(1L, TurnStatus.Interrupted, "Hydra sent one head before the turn was stopped; the rest of the block never went out. Ask again to send them.")]
    [InlineData(1L, TurnStatus.Failed, "Hydra sent one head before the turn failed; the rest of the block never went out. Ask again to send them.")]
    [InlineData(3L, TurnStatus.Interrupted, "Hydra sent 3 heads before the turn was stopped; the rest of the block never went out. Ask again to send them.")]
    [InlineData(3L, TurnStatus.Failed, "Hydra sent 3 heads before the turn failed; the rest of the block never went out. Ask again to send them.")]
    [Trait("Req", "PR-RUNTIME-209")] // :2811 the note names the head count (one head / n heads) and whether the turn was stopped or failed
    [Trait("Req", "UI-RUNTIME-019")] // :2811 the exact text, semicolon before "the rest"
    public void The_note_for_a_block_that_was_partly_sent_names_the_count_and_how_the_turn_ended(long sent, TurnStatus status, string expected)
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Sending.\n```hydra\n[" + Entry("a") + "]\n```");
        rig.SetStream(new DelegationStreamState(turn, "reply-1", Guid.NewGuid(), sent));

        rig.DropDelegationBlock(turn, status);

        Assert.Equal(expected, Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-208")] // :2800-2812 the first branch: stream cleared with its parser, block removed, row saved, batch settled, note, and no drop notice
    [Trait("Req", "UI-RUNTIME-026")] // :2805 a reply that was only the block reads "Asked for heads."
    public void A_stopped_turn_with_heads_already_out_rewrites_the_streamed_row_settles_the_batch_and_keeps_the_heads()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "```hydra\n[" + Entry("Task 1") + ",");
        var head = Assert.Single(rig.Heads);
        var stream = rig.Stream!;
        Assert.NotNull(rig.Parser);
        // That head's report is already in, so the batch has nothing more to wait for once the block is dropped.
        var report = ThreadRuntimeDelegationRig.Report(1);
        rig.SetBatch(stream.BatchID, [], [report]);
        var revision = rig.OnMain(() => ThreadRuntimeReflection.Get<long>(rig.Runtime, "saveRevision"));

        rig.DropDelegationBlock(turn, TurnStatus.Interrupted);

        Assert.Equal("Asked for heads.", rig.Text("reply-1")); // only the block was there
        Assert.Null(rig.Stream);
        Assert.Null(rig.Parser); // endDelegationStream clears both
        Assert.True(rig.OnMain(() => ThreadRuntimeReflection.Get<long>(rig.Runtime, "saveRevision")) > revision);
        Assert.Empty(rig.Batches); // settleBatches ran: the batch whose tail will never go out hands its reports over
        Assert.Equal([report], rig.PendingReports);
        Assert.Equal(
            "Hydra sent one head before the turn was stopped; the rest of the block never went out. Ask again to send them.",
            Assert.Single(rig.Notes()));
        Assert.Null(rig.Budget.DropNotice); // this branch queues no drop notice for the lead
        Assert.Equal(head.Id, Assert.Single(rig.Heads).Id); // the heads already sent are left alone
        Assert.Equal(HydraHeadInfo.Status.Running, rig.OnMain(() => rig.App.Thread(head.Id)!.Hydra!.HydraHeadInfoStatus));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-208")] // :2802 the text the lead wrote around the block is kept; the block goes
    public void The_text_around_a_streamed_block_is_kept()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Delta("reply-1", "Sending a head.\n\n```hydra\n[" + Entry("Task 1") + ",");

        rig.DropDelegationBlock(turn, TurnStatus.Failed);

        Assert.Equal("Sending a head.", rig.Text("reply-1"));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-208")] // :2800-2801 all five conditions: a stream of another turn does not take the branch
    [Trait("Req", "PR-RUNTIME-210")] // :2813 a stream belonging to a different turn is left in place
    [Trait("Req", "UI-RUNTIME-020")] // :2822 the second branch's note, stopped variant, exactly
    public void A_stream_of_another_turn_is_not_dropped_and_the_second_branch_runs()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Sending.\n```hydra\n[" + Entry("a") + "]\n```");
        var foreign = new DelegationStreamState(Guid.NewGuid(), "reply-1", Guid.NewGuid(), 2);
        rig.SetStream(foreign);

        rig.DropDelegationBlock(turn, TurnStatus.Interrupted);

        Assert.Equal(foreign, rig.Stream); // left in place
        Assert.Equal("Sending.", rig.Text("reply-1"));
        Assert.Equal(
            "Hydra sent no heads: the turn was stopped before they could go out. Ask again to send them.",
            Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-208")] // :2800-2801 the streamed entry must still resolve to an assistant message, else the first branch is skipped
    [Trait("Req", "UI-RUNTIME-020")] // :2822 the second branch's note, failed variant, exactly
    public void A_stream_whose_row_is_gone_does_not_take_the_first_branch()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Sending.\n```hydra\n[" + Entry("a") + "]\n```");
        rig.SetStream(new DelegationStreamState(turn, "no-such-row", Guid.NewGuid(), 2));

        rig.DropDelegationBlock(turn, TurnStatus.Failed);

        Assert.Equal("Hydra sent no heads: the turn failed before they could go out. Ask again to send them.", Assert.Single(rig.Notes()));
        Assert.NotNull(rig.Stream); // sent > 0, so the zero-sent clearing does not apply either
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-210")] // :2813 a zero-sent stream of this turn is cleared with its parser, with no note of its own, and the second branch still runs
    public void A_stream_that_sent_nothing_is_discarded_before_the_block_holding_row_is_looked_for()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Sending.\n```hydra\n[" + Entry("a") + "]\n```");
        rig.SetStream(new DelegationStreamState(turn, "reply-1", Guid.NewGuid(), 0));
        rig.SetParser(new HydraDelegationStreamParser(turn, "reply-1"));

        rig.DropDelegationBlock(turn, TurnStatus.Interrupted);

        Assert.Null(rig.Stream);
        Assert.Null(rig.Parser);
        // No return: the second branch went on to the row holding the block.
        Assert.Equal("Sending.", rig.Text("reply-1"));
        Assert.Equal(
            "Hydra sent no heads: the turn was stopped before they could go out. Ask again to send them.",
            Assert.Single(rig.Notes()));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-210")] // :2813 a zero-sent stream with no block-holding row is still cleared, and nothing else happens
    public void A_zero_sent_stream_is_cleared_even_when_no_row_holds_a_block()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Just words.");
        rig.SetStream(new DelegationStreamState(turn, "reply-1", Guid.NewGuid(), 0));

        rig.DropDelegationBlock(turn, TurnStatus.Failed);

        Assert.Null(rig.Stream);
        Assert.Empty(rig.Notes());
        Assert.Null(rig.Budget.DropNotice);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-210")] // :2813 a zero-sent stream of ANOTHER turn is left in place, parser and all
    public void A_zero_sent_stream_of_another_turn_is_left_in_place()
    {
        using var rig = new ThreadRuntimeDelegationRig();
        var turn = rig.BeginTurn();
        rig.Reply("reply-1", "Just words.");
        var foreign = new DelegationStreamState(Guid.NewGuid(), "reply-1", Guid.NewGuid(), 0);
        rig.SetStream(foreign);
        var parser = new HydraDelegationStreamParser(foreign.TurnID, "reply-1");
        rig.SetParser(parser);

        rig.DropDelegationBlock(turn, TurnStatus.Failed);

        Assert.Equal(foreign, rig.Stream);
        Assert.Same(parser, rig.Parser);
    }
}
