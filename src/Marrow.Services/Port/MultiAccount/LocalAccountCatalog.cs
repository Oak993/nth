using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.Local;
using Marrow.Core.Models;

namespace Marrow.Services.Port.MultiAccount;

public static class LocalAccountCatalog
{
    public static void Synchronize(AccountStore accounts, IReadOnlyList<LocalEntry> entries)
    {
        if (accounts.SaveBlocked) return;
        foreach (var model in entries.SelectMany(entry => entry.Models))
        {
            var rows = accounts.ForService(ProviderKind.Local);
            var account = rows.FirstOrDefault(row => row.Defaults.Model == model.SelectionId)
                ?? rows.FirstOrDefault(row => row.UsesLegacyCredentials && row.Defaults.Model is null)
                ?? accounts.Add(ProviderKind.Local);
            if (account.Defaults.Model != model.SelectionId)
                accounts.SetDefaults(account.Id, account.Defaults with { Model = model.SelectionId });
            if (!account.HasSignedIn) accounts.MarkSignedIn(account.Id);
        }
    }
}
