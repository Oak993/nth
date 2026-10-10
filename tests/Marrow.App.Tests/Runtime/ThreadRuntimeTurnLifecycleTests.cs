using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The turn's start, its stop and its head budget: ThreadRuntime.swift:1175-1300 (startTurn), :1691-1720 (interrupt),
// :2313-2330 and :2477-2490 (finishTurn). The records cite B2 line numbers; the B3 text is the same. Turns run through the
// real StartTurn over an offline scripted session (Fixtures/ThreadRuntimeScriptedSession.cs).
public sealed class ThreadRuntimeTurnLifecycleTests
{
    private const string FinalNote = "[Nth] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.";

    private static ChatThread AsHead(ChatThread thread, HydraHeadInfo.Status status = HydraHeadInfo.Status.Running, DateTimeOffset? finishedAt = null) =>
        thread with
        {
            Hydra = new HydraHeadInfo(1, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated)
            {
                HydraHeadInfoStatus = status,
                FinishedAt = finishedAt,
            },
        };

    /// <summary>The session takes a turn and never answers it; an interrupt makes it end the turn with <paramref name="onInterrupt"/>.</summary>
    private static void Hold(ThreadRuntimeTurnRig rig, TurnStatus? onInterrupt = TurnStatus.Interrupted)
    {
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Session.InterruptScript = () =>
        {
            if (onInterrupt is { } status) rig.Session.Emit(new ProviderEvent.TurnCompleted(status, null));
            return Task.CompletedTask;
        };
    }

    private static void WaitForSends(ThreadRuntimeTurnRig rig, int count) =>
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= count, AppModelHarness.Bound);

    private static void WaitIdle(ThreadRuntimeTurnRig rig) =>
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);

    private static void Settle(ThreadRuntimeTurnRig rig)
    {
        for (var round = 0; round < 40; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(25);
        }
    }

    private static void Advance(ThreadRuntimeTurnRig rig, double seconds) => rig.H.Main.Advance(TimeSpan.FromSeconds(seconds));

    // ---- the head budget (PR-RUNTIME-058, PR-RUNTIME-126) ----

    [Theory]
    [Trait("Req", "PR-RUNTIME-058")] // :1237-1245 maxSeconds 2100, plus 90 for an API-key provider
    [InlineData(ProviderKind.Claude, 2100)]
    [InlineData(ProviderKind.Deepseek, 2190)]
    public void A_droppy_run_head_is_interrupted_when_its_turn_budget_is_spent(ProviderKind provider, int allowance)
    {
        using var rig = new ThreadRuntimeTurnRig(provider, thread => AsHead(thread));
        Hold(rig, onInterrupt: null);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Advance(rig, allowance - 1);
        Assert.Equal(0, rig.Session.InterruptCalls);
        Advance(rig, 1);

        Assert.Equal(1, rig.Session.InterruptCalls);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-058")] // :1235 only a Droppy-run head has the budget
    public void A_chat_that_is_not_a_droppy_run_head_has_no_turn_budget()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: null);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Advance(rig, 3000);

        Assert.Equal(0, rig.Session.InterruptCalls);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-126")] // :2480-2490 a head stopped for its budget starts one more turn with the final note
    [Trait("Req", "PR-RUNTIME-058")] // :1233-1237 the final report is allowed 180 seconds, and does not arm another report
    public void A_head_stopped_for_its_budget_writes_its_report_as_one_more_turn_with_a_shorter_allowance()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => AsHead(thread));
        Hold(rig);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Assert.Equal(0, rig.Runtime.AutoStartedTurnToken);
        Advance(rig, 2100);
        WaitForSends(rig, 2);

        Assert.Equal(FinalNote, rig.Session.Inputs[1].Text);
        Assert.True(rig.Session.Inputs[1].IsFinalReport);
        Assert.False(rig.Session.Inputs[0].IsFinalReport);
        Assert.Equal(1, rig.Runtime.AutoStartedTurnToken);
        Assert.Contains(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.User user && user.Value.Text == FinalNote);

        Advance(rig, 179);
        Assert.Equal(1, rig.Session.InterruptCalls);
        Advance(rig, 1);
        Assert.Equal(2, rig.Session.InterruptCalls);
        WaitIdle(rig);
        Settle(rig);
        Assert.Equal(2, rig.Session.Inputs.Count);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-126")] // :2482 `status == .interrupted`: a budget stop that ended some other way starts no report turn
    public void A_budget_stop_that_ends_as_a_failure_starts_no_report_turn()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => AsHead(thread));
        Hold(rig, onInterrupt: TurnStatus.Failed);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Advance(rig, 2100);
        WaitIdle(rig);
        Settle(rig);

        Assert.Single(rig.Session.Inputs);
        Assert.Equal(0, rig.Runtime.AutoStartedTurnToken);

        // headBudgetSpent was cleared whatever happened: an ordinary stop of the next turn starts no report turn.
        // (The failed head's session was let go with its report, so the next turn runs on a new one.)
        rig.Install(new ThreadRuntimeScriptedSession());
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Session.InterruptScript = () =>
        {
            rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            return Task.CompletedTask;
        };
        _ = rig.StartTurn("again");
        WaitForSends(rig, 1);
        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        WaitIdle(rig);
        Settle(rig);
        Assert.Single(rig.Session.Inputs);
    }

    // ---- stopping a turn (PR-RUNTIME-130, PR-RUNTIME-066, PR-RUNTIME-132) ----

    [Fact]
    [Trait("Req", "PR-RUNTIME-130")] // :1696-1704 phase == .starting: drop the message, finish as interrupted, never ask the provider
    [Trait("Req", "PR-RUNTIME-066")] // :1286-1287 the start's guard abandons the send silently
    public void Stopping_a_turn_that_is_still_starting_sends_nothing_and_closes_it_as_interrupted_without_a_notice()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        RuntimePhase? phaseAtStop = null;

        var gate = new TaskCompletionSource();
        var task = rig.StartTurn("never goes", whileStarting: () =>
        {
            phaseAtStop = rig.Runtime.Phase;
            // The close of the stopped turn waits on a slow command diff, so the start gets to its own guard first.
            ThreadRuntimeReflection.Get<Dictionary<string, TaskHandle>>(rig.Runtime, "commandSettles")!["slow"] =
                MainActor.Spawn(async () => await gate.Task);
            rig.Runtime.Interrupt();
        });
        rig.Finish(task);
        Assert.Empty(rig.Session.Inputs);
        Advance(rig, 3);
        WaitIdle(rig);
        Advance(rig, 30);

        Assert.Equal(RuntimePhase.Starting, phaseAtStop);
        Assert.Empty(rig.Session.Inputs);
        Assert.Equal(0, rig.Session.InterruptCalls);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(rig.Runtime.Turns).Status);
        Assert.Single(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        Assert.DoesNotContain(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice);
        Assert.Null(rig.Runtime.TurnStartedAt);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-132")] // :1713-1718 eight seconds without the turn closing: release the session, finish as interrupted
    public void A_session_that_does_not_end_an_interrupted_turn_within_eight_seconds_is_stopped_and_the_turn_closed()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: null);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);

        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        Assert.Equal(1, rig.Session.InterruptCalls);
        Advance(rig, 7);
        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Equal(RuntimePhase.Running, rig.Runtime.Phase);
        Advance(rig, 1);
        WaitIdle(rig);

        Assert.Equal(1, rig.Session.StopCalls);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(rig.Runtime.Turns).Status);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-132")] // failure/cancellation: a second interrupt cancels the first watchdog and arms its own
    public void A_second_interrupt_restarts_the_eight_second_wait()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: null);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);

        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        Advance(rig, 5);
        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        Advance(rig, 5);
        Assert.Equal(0, rig.Session.StopCalls);
        Advance(rig, 3);
        WaitIdle(rig);

        Assert.Equal(1, rig.Session.StopCalls);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-132")] // :1714 guard `currentTurnID == turnID`: a turn the session did end is left alone
    public void A_turn_the_session_ends_in_time_never_has_its_session_stopped()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);

        rig.H.Main.OnMain(() => rig.Runtime.Interrupt());
        WaitIdle(rig);
        Advance(rig, 60);

        Assert.Equal(0, rig.Session.StopCalls);
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(rig.Runtime.Turns).Status);
    }

    // ---- finishTurn without a turn (PR-RUNTIME-109) ----

    private static void FinishTurn(ThreadRuntimeTurnRig rig, TurnStatus status, Guid? turnID) =>
        rig.H.Main.Loop.Run(() => ThreadRuntimeReflection.Call<Task>(rig.Runtime, "FinishTurn", status, turnID)!, AppModelHarness.Bound);

    [Theory]
    [Trait("Req", "PR-RUNTIME-109")] // :2271-2281 no such turn: idle, clock cleared, no summary row, no notification
    [InlineData(false)]
    [InlineData(true)]
    public void Finishing_when_the_turn_is_gone_only_idles_the_runtime(bool unknownExplicitTurn)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: null);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        if (!unknownExplicitTurn) ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", null);

        FinishTurn(rig, TurnStatus.Completed, unknownExplicitTurn ? Guid.NewGuid() : null);

        Assert.Equal(RuntimePhase.Idle, rig.Runtime.Phase);
        Assert.Null(rig.Runtime.TurnStartedAt);
        Assert.DoesNotContain(rig.Runtime.Entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.TurnEnd);
        Assert.Equal(TurnStatus.Running, Assert.Single(rig.Runtime.Turns).Status);
        Assert.Equal(TurnStatus.Running, rig.Thread.LastStatus);
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-109")] // :2278-2281 drainPendingSend() on the fallback path: the Return-captured message still goes
    public void Finishing_when_the_turn_is_gone_still_sends_the_message_captured_by_return()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Hold(rig, onInterrupt: null);
        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.Draft = new ComposerDraft { Text = "then this" };
            rig.Runtime.InterruptAndSend();
            ThreadRuntimeReflection.Set(rig.Runtime, "currentTurnID", null);
        });
        rig.Session.SendScript = null;

        FinishTurn(rig, TurnStatus.Completed, null);
        WaitForSends(rig, 2);

        Assert.Equal("then this", rig.Session.Inputs[1].Text);
    }
}
