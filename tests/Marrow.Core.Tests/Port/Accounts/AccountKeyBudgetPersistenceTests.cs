using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountKeyBudgetPersistenceTests
{
    [Fact]
    public void Unreported_holds_survive_metadata_reload_and_settlement_atomically_replaces_the_ceiling_with_real_cost()
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var account = accounts.ForService(ProviderKind.Deepseek)[0];
        var key = account.Id.ToString("D");
        var request = Guid.NewGuid();
        var store = new AccountKeyStore(accounts, settings, _ => "fake-key");
        var sentAt = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        store.Hold(request, key, 0.7, sentAt);
        var restart = new AccountKeyStore(new(settings), settings, _ => "fake-key");
        Assert.False(restart.SaveBlocked);
        Assert.Equal(0.7, restart.SpendSince(key, DateTimeOffset.MinValue));
        // Unknown requests remain held even after the local month changes.
        Assert.Equal(0.7, restart.SpendSince(key, sentAt.AddMonths(1)));
        restart.Settle(request, 0.2);
        restart.Settle(request, 0.9);
        var final = new AccountKeyStore(new(settings), settings, _ => "fake-key");
        Assert.Equal(0.2, final.SpendSince(key, DateTimeOffset.MinValue));
        Assert.Equal(0, final.SpendSince(key, sentAt.AddMonths(1)));
    }
}
