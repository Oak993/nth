// PORT-ONLY: OD-0114; ED-403. An actual API tool-round suspension owns a temporary safe boundary.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private AccountStepPause? accountStepPause;
    private bool OwnsAccountStepPause => accountStepPause is { } pause && ReferenceEquals(session, pause.Session)
        && pause.Epoch == sessionEpoch && pause.Turn == currentTurnID && Phase == RuntimePhase.Running
        && pause.Session.IsRunning && Turns.Any(turn => turn.Id == pause.Turn && turn.Status == TurnStatus.Running);
    internal bool HandshakeReplyInFlight => limitedReplyTurnID is not null || Phase == RuntimePhase.Running && currentTurnID is not null;

    private async Task<bool> ContinueAfterAccountStepAsync(OpenAICompatibleSession provider, long epoch, CancellationToken ct)
    {
        MainActor.AssumeIsolated();
        if (!ReferenceEquals(session, provider) || sessionEpoch != epoch) return false;
        if (ct.IsCancellationRequested || currentTurnID is not { } turn || Phase != RuntimePhase.Running
            || App is not { } app || !app.AccountRouting.CanSwitchAtAccountStep(ThreadID)) return true;
        var pause = new AccountStepPause(provider, epoch, turn);
        accountStepPause = pause;
        try
        {
            if (!IsAccountSwitchSafe) return true;
            await app.AccountRouting.SwitchAtAccountStepAsync(ThreadID, ct);
            return ReferenceEquals(session, provider) && sessionEpoch == epoch;
        }
        finally { if (ReferenceEquals(accountStepPause, pause)) accountStepPause = null; }
    }
}
