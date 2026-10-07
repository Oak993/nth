using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.Local;
using Marrow.Services.Port.MultiAccount;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LocalAccountCatalogTests
{
    [Fact]
    public void Equal_model_names_on_two_servers_have_distinct_persistent_account_rows_and_off_rows_stay_off()
    {
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var a = new LocalServer("http://127.0.0.1:11434");
        var b = new LocalServer("http://127.0.0.1:1234");
        var am = new LocalModel(a, "same", "Same model", new());
        var bm = new LocalModel(b, "same", "Same model", new());
        LocalEntry[] entries = [new(a, [am]), new(b, [bm])];
        LocalAccountCatalog.Synchronize(accounts, entries);
        var first = Assert.Single(accounts.ForService(ProviderKind.Local), row => row.Defaults.Model == am.SelectionId);
        var second = Assert.Single(accounts.ForService(ProviderKind.Local), row => row.Defaults.Model == bm.SelectionId);
        Assert.NotEqual(first.Id, second.Id);
        accounts.SetEnabled(first.Id, false);
        accounts.Rename(second.Id, "My model");
        var restarted = new AccountStore(settings);
        LocalAccountCatalog.Synchronize(restarted, entries);
        Assert.Equal(2, restarted.ForService(ProviderKind.Local).Count);
        Assert.False(restarted.Get(first.Id).Enabled);
        Assert.Equal("My model", restarted.Get(second.Id).CustomName);
        Assert.True(restarted.Get(second.Id).HasSignedIn);
    }
}
