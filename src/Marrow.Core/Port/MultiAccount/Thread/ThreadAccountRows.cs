// PORT-ONLY: multi-account steps 3b-5, job 0. The accounts of a service in the store's order, as the thread's views draw them: the signed-in ones only (a row for an account that was never signed in is not an account to move to), off ones kept so they can say "Off".
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Port.MultiAccount.Thread;

public static class ThreadAccountRows
{
    public static IReadOnlyList<ThreadAccount> Build(AccountStore accounts, ProviderKind service, Func<Account, bool> connected, Func<Guid, AccountUsageLine?> usage)
    {
        var rows = new List<ThreadAccount>();
        foreach (var account in accounts.ForService(service).Where(connected))
        {
            var name = account.CustomName ?? accounts.DefaultName(account.Id);
            rows.Add(new ThreadAccount(account.Id, service, rows.Count + 1, name, account.CustomName is not null, AccountMono.Of(name, account.CustomName is not null), account.Plan, account.Enabled, usage(account.Id), account.Defaults));
        }
        return rows;
    }
}
