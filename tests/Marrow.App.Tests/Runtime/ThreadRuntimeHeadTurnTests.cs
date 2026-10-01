using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// startTurn on a Droppy-run head (App/Runtime/ThreadRuntime.swift:1217-1228, B3): every turn of a head clears the head record's
// finish and landing times, and a later turn restarts its clock. The head is a real head record of a Hydra lead; its own
// session never starts (the loop is not pumped past the first suspension of the turn).
public sealed class ThreadRuntimeHeadTurnTests
{
    private static readonly DateTimeOffset Old = new(2020, 1, 1, 8, 0, 0, TimeSpan.Zero);

    // The rig's one busy head: a Droppy-run head record whose own session has not started (a head made by spawnDroppyHead
    // starts its brief turn as soon as the loop drains).
    private static ChatThread TheHead(ThreadRuntimeDelegationRig rig) => rig.OnMain(() => rig.App.HydraHeads(rig.Lead.Id).Single());

    private static void Stamp(ThreadRuntimeDelegationRig rig, ChatThread head) => rig.OnMain(() =>
        rig.App.UpdateThread(head.Id, thread => thread with
        {
            Hydra = thread.Hydra! with
            {
                StartedAt = Old,
                QueuedAt = Old.AddMinutes(1),
                ExecutionStartedAt = Old.AddMinutes(2),
                ExecutionFinishedAt = Old.AddMinutes(3),
                LandingStartedAt = Old.AddMinutes(4),
                LandingFinishedAt = Old.AddMinutes(5),
            },
        }));

    private static HydraHeadInfo Info(ThreadRuntimeDelegationRig rig, ChatThread head) => rig.OnMain(() => rig.App.Thread(head.Id)!.Hydra!);

    /// <summary>Starts a turn and returns the head record as it stood when the turn's first suspension was reached (the rest
    /// of the turn, which fails closed for want of an executable, runs when the call ends).</summary>
    private static HydraHeadInfo StartTurn(ThreadRuntimeDelegationRig rig, ThreadRuntime runtime, ChatThread head, string text) =>
        rig.OnMain(() =>
        {
            _ = ThreadRuntimeReflection.Call<Task>(runtime, "StartTurn", text, Array.Empty<Attachment>(), null, false);
            return rig.App.Thread(head.Id)!.Hydra!;
        });

    [Fact]
    [Trait("Req", "PR-RUNTIME-257")] // :1217-1228 the first turn keeps startedAt/queuedAt/executionStartedAt; every turn clears the finish and landing times; a later turn restarts the clock
    public void A_heads_turns_clear_its_finish_and_landing_times_and_a_later_turn_restarts_its_clock()
    {
        using var rig = new ThreadRuntimeDelegationRig(busyHeads: 1);
        var head = TheHead(rig);
        var runtime = rig.OnMain(() => rig.App.Runtime(head.Id));
        rig.H.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        Stamp(rig, head);

        var first = StartTurn(rig, runtime, head, "First.");

        Assert.Equal(Old, first.StartedAt); // the head's first turn keeps what it was created with
        Assert.Equal(Old.AddMinutes(1), first.QueuedAt);
        Assert.Equal(Old.AddMinutes(2), first.ExecutionStartedAt);
        Assert.Null(first.ExecutionFinishedAt); // but the finish and landing times go
        Assert.Null(first.LandingStartedAt);
        Assert.Null(first.LandingFinishedAt);

        // The turn ends, the head is told more, and its next turn begins later.
        rig.OnMain(() =>
        {
            ThreadRuntimeReflection.Set(runtime, "phase", RuntimePhase.Idle);
            ThreadRuntimeReflection.Set(runtime, "currentTurnID", null);
        });
        Stamp(rig, head);
        rig.H.Main.Advance(TimeSpan.FromMinutes(30));
        var now = rig.OnMain(() => SwiftDate.Now);

        var second = StartTurn(rig, runtime, head, "Second.");

        Assert.Equal(now, second.StartedAt);
        Assert.Equal(now, second.ExecutionStartedAt);
        Assert.Null(second.QueuedAt);
        Assert.Null(second.ExecutionFinishedAt);
        Assert.Null(second.LandingStartedAt);
        Assert.Null(second.LandingFinishedAt);
        Assert.Equal(2, rig.OnMain(() => runtime.Turns.Count));
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-257")] // :1217 only a Droppy-run head: the lead's own turns leave every head time alone
    public void A_turn_of_the_lead_itself_changes_no_head_record()
    {
        using var rig = new ThreadRuntimeDelegationRig(busyHeads: 1);
        var head = TheHead(rig);
        Stamp(rig, head);
        var before = Info(rig, head);

        var after = StartTurn(rig, rig.Runtime, head, "The lead's turn.");

        Assert.Equal(before, after);
    }
}
