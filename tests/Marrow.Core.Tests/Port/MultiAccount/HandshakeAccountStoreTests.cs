using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeAccountStoreTests
{
    [Fact]
    public void Only_connected_identities_enter_the_engine_and_reordering_keeps_their_ids_and_defaults()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.ForService(ProviderKind.Claude)[0];
        store.SetPlan(first.Id, "Max (20x)");
        var second = store.Add(ProviderKind.Claude);
        store.MarkSignedIn(second.Id);
        store.SetEnabled(second.Id, false);
        store.SetDefaults(second.Id, new AccountDefaults { HandshakePoint = new(99) });
        store.Reorder(ProviderKind.Claude, [second.Id, first.Id]);
        var joined = new HandshakeAccountStore(store, a => a.HasSignedIn || a.Id == first.Id);
        Assert.Equal([second.Id.ToString("D"), first.Id.ToString("D")], joined.Accounts.Select(a => a.Id));
        Assert.False(joined.Accounts[0].Enabled);
        Assert.Equal(99, joined.Accounts[0].HandshakeAt!.Value.Percent);
        Assert.Equal(PlanTier.Large, joined.Accounts[1].Plan);
        Assert.Equal("claude", joined.Accounts[0].Service);
        store.SetEnabled(second.Id, true);
        Assert.True(joined.Accounts[0].Enabled);
    }
}
