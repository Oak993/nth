using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Local;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LocalAccountRegistryTests
{
    [Fact]
    public async Task Live_local_discovery_creates_real_account_rows_and_refresh_preserves_identity_order_and_off_state()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var local = new LocalDiscovery([server.BaseURL]);
        var settings = new InMemorySettingsStore();
        var accounts = new AccountStore(settings);
        var registry = runtime.OnMain(() => new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path, local));
        server.Reply("/v1/models", """{"data":[{"id":"first"},{"id":"second"}]}""");
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Local), LoopbackHttpServer.Guard));
        var models = registry.Models(ProviderKind.Local);
        var first = Assert.Single(accounts.ForService(ProviderKind.Local), account => account.Defaults.Model == models[0].Id);
        var second = Assert.Single(accounts.ForService(ProviderKind.Local), account => account.Defaults.Model == models[1].Id);
        Assert.True(first.HasSignedIn);
        Assert.True(second.HasSignedIn);
        Assert.NotEqual(first.Id, second.Id);
        accounts.Reorder(ProviderKind.Local, [second.Id, first.Id]);
        accounts.SetEnabled(first.Id, false);
        accounts.Rename(second.Id, "My local model");
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Local), LoopbackHttpServer.Guard));
        var restarted = new AccountStore(settings);
        Assert.Equal(new[] { second.Id, first.Id }, restarted.ForService(ProviderKind.Local).Select(account => account.Id));
        Assert.False(restarted.Get(first.Id).Enabled);
        Assert.Equal("My local model", restarted.Get(second.Id).CustomName);
        Assert.All(server.Requests, request => Assert.DoesNotContain("completions", request.Target, StringComparison.Ordinal));
    }
}
