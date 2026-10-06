using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App;

// PORT-ONLY: OD-0114; explicit account selection has independent context/pin ownership.
public sealed partial class AppModel
{
    public async Task<bool> TryPinThreadAccountAsync(Guid threadId, Guid? accountId, Bring? bring, CancellationToken ct = default)
    {
        MainActor.AssumeIsolated();
        ct.ThrowIfCancellationRequested();
        if (Thread(threadId) is not { } thread || Settings.Accounts.SaveBlocked || ApiKeys.AwaitingChoice(threadId)
            || accountId is not null && accountRouting?.ManualSelectionPending(threadId) == true) return false;
        if (accountId is null || thread.Provider.IsAPIKeyBased) return TryPinThreadAccount(threadId, accountId);
        var row = Settings.Accounts.Accounts.FirstOrDefault(row => row.Id == accountId);
        if (row is not { Enabled: true } || row.Service != thread.Provider || !IsAccountConnected(row) || !CanUseAccount(row.Id)) return false;
        var runtime = Runtime(threadId);
        await runtime.EnsureLoaded();
        ct.ThrowIfCancellationRequested();
        if (!runtime.TryHandshakeAccountId(out var source) || Thread(threadId) != thread) return false;
        if (source == row.Id)
        {
            UpdateThread(threadId, current => current with { ProviderAccountPinID = row.Id });
            return true;
        }
        // The UI resolves/asks this choice; absence never silently buys a transfer on another account.
        if (bring is not { } selected || !Enum.IsDefined(selected)) return false;
        return await AccountRouting.PinManualAsync(runtime, row.Id, selected, ct);
    }
}
