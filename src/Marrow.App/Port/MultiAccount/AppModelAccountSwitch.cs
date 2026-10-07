// PORT-ONLY: OD-0114; ED-403. Explicit switches retain their validated cross-service binding atomically.
using Marrow.Core.Accounts.Pills;
using Marrow.Services.Providers;

namespace Marrow.App;

public sealed partial class AppModel
{
    public void ApplyHandshakeAccount(Guid threadId, SessionConfiguration configuration, string? sessionId, PillThreadState? relay = null, bool pinAccount = false, bool deferred = false)
    {
        if (Thread(threadId) is null || configuration.AccountId is not { } id
            || Settings.Accounts.Choose(configuration.Provider, id) is null || (deferred ? sessionId is not null : string.IsNullOrWhiteSpace(sessionId)))
            throw new InvalidOperationException("The destination account is no longer available.");
        UpdateThreadValue(threadId, current => (current with
        {
            Provider = configuration.Provider, ProviderAccountID = id, ProviderSessionID = sessionId, ProviderResumeAt = null,
            Model = configuration.Model, Effort = configuration.Effort, FastMode = configuration.FastMode,
            ProviderAccountPinID = pinAccount ? id : current.ProviderAccountPinID,
        }).WithPillRelay(relay ?? current.PillRelay), accountSwitch: true);
    }
}
