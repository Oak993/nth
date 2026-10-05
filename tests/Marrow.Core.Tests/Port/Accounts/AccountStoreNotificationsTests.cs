using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountStoreNotificationsTests
{
    [Fact]
    public void Connecting_a_second_account_notifies_live_observers_after_the_complete_new_snapshot_is_available()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var second = store.Add(ProviderKind.Claude);
        var observations = new List<bool>();
        store.Changed += () => observations.Add(store.Get(second.Id).HasSignedIn);
        store.MarkSignedIn(second.Id);
        Assert.Equal(new[] { true }, observations);
    }

    [Fact]
    public void Turning_a_row_off_notifies_live_routing_before_it_can_be_chosen_again()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var row = store.ForService(ProviderKind.Deepseek)[0];
        var observations = new List<Guid?>();
        store.Changed += () => observations.Add(store.Choose(ProviderKind.Deepseek)?.Id);
        store.SetEnabled(row.Id, false);
        Assert.Equal(new Guid?[] { null }, observations);
    }
}
