// PORT-ONLY: MA-3b-E1. Private checking/opinion heads cannot rotate to an unselected account or grant automatic move consent.
using Marrow.Core.Accounts.Keys;

namespace Marrow.Services.Port.MultiAccount;

internal sealed class PinnedConversationKeyRotation(IApiKeyRotation inner, Guid account) : IApiKeyRotation, IDisposable
{
    public string? StopReason => inner.StopReason;
    public IApiKeyBudget? Budget => inner.Budget;
    public string? ContinuedOn => null;
    public void AllowMoveOnce() => throw new InvalidOperationException("A checking or opinion head cannot move to another account.");
    public KeyChoice? Current()
    {
        var choice = inner.Current();
        if (choice is not null && (!Guid.TryParse(choice.Id, out var selected) || selected != account))
            throw new InvalidOperationException("The private API head attempted to use a different account.");
        return choice;
    }
    public KeyChoice? OnFailure(int status, TimeSpan? retryAfter)
    {
        // Record the actual rejection in the shared pool, but never resend this head's conversation on another key.
        _ = inner.OnFailure(status, retryAfter);
        return null;
    }
    public void RecordSpend(double usd) => inner.RecordSpend(usd);
    public void Dispose() { if (inner is IDisposable lifetime) lifetime.Dispose(); }
}
