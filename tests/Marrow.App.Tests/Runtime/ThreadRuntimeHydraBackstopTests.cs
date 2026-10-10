using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// The out-of-turn backstop of a Droppy-run head: ThreadRuntime.swift:1230-1245 (B3 1.7.0; the record cites hydra-prompt-corpus.md
// section 6, same text) with the constants at Hydra.swift:186-201 (maxSeconds 35 minutes, reportSeconds 3 minutes). The allowance is
// reportSeconds for a report turn, otherwise maxSeconds plus 90 on an API-key provider; on expiry it marks the budget spent
// (a report turn does not) and interrupts. Turns run through the real StartTurn over an offline scripted session.
public sealed class ThreadRuntimeHydraBackstopTests
{
    private const string FinalNote = "[nth] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.";

    private static ChatThread AsHead(ChatThread thread) =>
        thread with
        {
            Hydra = new HydraHeadInfo(1, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated)
            {
                HydraHeadInfoStatus = HydraHeadInfo.Status.Running,
            },
        };

    /// <summary>The session takes a turn and never answers it; an interrupt ends the turn as interrupted.</summary>
    private static void HoldUntilInterrupted(ThreadRuntimeTurnRig rig)
    {
        rig.Session.SendScript = _ => new TaskCompletionSource().Task;
        rig.Session.InterruptScript = () =>
        {
            rig.Session.Emit(new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null));
            return Task.CompletedTask;
        };
    }

    private static void WaitForSends(ThreadRuntimeTurnRig rig, int count) =>
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= count, AppModelHarness.Bound);

    private static void Advance(ThreadRuntimeTurnRig rig, double seconds) => rig.H.Main.Advance(TimeSpan.FromSeconds(seconds));

    private static void Settle(ThreadRuntimeTurnRig rig)
    {
        for (var round = 0; round < 40; round++)
        {
            rig.H.Main.RunUntilIdle();
            Thread.Sleep(25);
        }
    }

    [Theory]
    [InlineData(ProviderKind.Claude, 2100)]
    [InlineData(ProviderKind.Cursor, 2100)]
    [InlineData(ProviderKind.Deepseek, 2190)]
    [Trait("Req", "PR-HYDRA-016")] // :1237-1245 maxSeconds + (isAPIKeyBased ? 90 : 0); on expiry headBudgetSpent = true and interrupt()
    public void A_turns_backstop_allows_2100_seconds_on_a_CLI_head_and_2190_on_an_API_key_head_then_marks_the_budget_spent_and_interrupts(
        ProviderKind provider, int allowance)
    {
        using var rig = new ThreadRuntimeTurnRig(provider, AsHead);
        HoldUntilInterrupted(rig);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Advance(rig, allowance - 1);
        Assert.Equal(0, rig.Session.InterruptCalls);
        Assert.Single(rig.Session.Inputs);
        Advance(rig, 1);

        Assert.Equal(1, rig.Session.InterruptCalls);
        // The budget was marked spent: the interrupted turn is followed by the head's one report turn (PR-HYDRA-016 + finishTurn).
        WaitForSends(rig, 2);
        // (A CLI head whose first send never returned has not spent its anti-slop delivery, so the note may follow that prefix.)
        Assert.EndsWith(FinalNote, rig.Session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.True(rig.Session.Inputs[1].IsFinalReport);
    }

    [Theory]
    [InlineData(ProviderKind.Claude)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Deepseek)]
    [Trait("Req", "PR-HYDRA-016")] // :1233-1237 isFinalReport ? reportSeconds : ... - 180 s on any provider, no +90 for an API-key head
    public void A_report_turns_backstop_allows_180_seconds_on_every_provider_and_marks_no_further_report(ProviderKind provider)
    {
        using var rig = new ThreadRuntimeTurnRig(provider, AsHead);
        HoldUntilInterrupted(rig);

        _ = rig.StartTurn("work");
        WaitForSends(rig, 1);
        Advance(rig, provider == ProviderKind.Deepseek ? 2190 : 2100);
        WaitForSends(rig, 2);
        Assert.Equal(1, rig.Session.InterruptCalls);

        Advance(rig, 179);
        Assert.Equal(1, rig.Session.InterruptCalls);
        Advance(rig, 1);

        Assert.Equal(2, rig.Session.InterruptCalls);
        // headBudgetSpent = !isFinalReport = false: the report turn's expiry does not start another report.
        rig.H.Main.Loop.RunUntil(() => rig.Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
        Settle(rig);
        Assert.Equal(2, rig.Session.Inputs.Count);
        Assert.Equal(1, rig.Runtime.AutoStartedTurnToken);
    }
}
