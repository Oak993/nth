using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Accounts;

public static class PillStepAccountResolver
{
    public static AccountInfo? Resolve(IAccountStore store, PillStep step) => store is IPillStepAccountStore extended
        ? extended.ResolvePillStep(step) : Resolve(store.Accounts, step);

    public static AccountInfo? Resolve(IReadOnlyList<AccountInfo> accounts, PillStep step)
    {
        var anchor = accounts.FirstOrDefault(account => string.Equals(account.Id, step.AccountId, StringComparison.OrdinalIgnoreCase));
        if (step.KeyId is null) return anchor;
        if (anchor is null || SwiftEnum<ProviderKind>.FromRawValue(anchor.Service) is not { IsAPIKeyBased: true }) return null;
        return accounts.FirstOrDefault(account => string.Equals(account.Id, step.KeyId, StringComparison.OrdinalIgnoreCase)
            && account.Service == anchor.Service);
    }
}
