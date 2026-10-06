namespace Marrow.Core.Accounts;
// PORT-ONLY: NOTES context fallback. Optional capability; never substitutes a mechanical summary for AI.
public interface IIncomingBriefingHost
{
    Task<string?> RequestIncomingBriefingAsync(SwitchTarget target, HandoffSummary summary, CancellationToken ct);
}
