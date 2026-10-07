using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App;

// PORT-ONLY: OD-0114; an explicit API pin is scoped to one conversation and read at the next request boundary.
public sealed partial class AppModel
{
    public bool TryPinThreadAccount(Guid threadId, Guid? accountId)
    {
        MainActor.AssumeIsolated();
        if (Thread(threadId) is not { } thread || Settings.Accounts.SaveBlocked || ApiKeys.AwaitingChoice(threadId)) return false;
        if (accountRouting?.ManualSelectionPending(threadId) == true)
        {
            if (accountId is not null) return false;
            accountRouting.CancelManualSelection(threadId);
        }
        if (accountId is { } id)
        {
            var account = Settings.Accounts.Accounts.FirstOrDefault(row => row.Id == id);
            if (!thread.Provider.IsAPIKeyBased || account is not { Enabled: true } || account.Service != thread.Provider
                || !IsAccountConnected(account) || !CanUseAccount(id)) return false;
        }
        if (thread.ProviderAccountPinID != accountId)
            UpdateThread(threadId, current => current with { ProviderAccountPinID = accountId });
        return true;
    }
}
