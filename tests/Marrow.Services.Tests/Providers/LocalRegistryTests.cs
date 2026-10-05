using Marrow.Core.Models;
using Marrow.Services.Port.Local;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-10")]
[Trait("Feature", "MA-3")]
public sealed class LocalRegistryTests
{
    [Fact]
    public async Task Discovery_flows_into_existing_status_and_model_plumbing_and_remembers_user_addresses()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var local = new LocalDiscovery([]);
        var settings = new FakeProviderSettings();
        settings.Enabled.Add(ProviderKind.Local);
        var registry = runtime.OnMain(() => new ProviderRegistry(settings, local));
        Assert.DoesNotContain(ProviderKind.Local, registry.AvailableProviders);
        runtime.OnMain(() => registry.AddLocalAddress(server.BaseURL.Authority));
        server.Reply("/v1/models", """{"data":[{"id":"test","context_length":8192,"supports_tool_calling":true}]}""");
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Local), LoopbackHttpServer.Guard));
        Assert.Contains(ProviderKind.Local, registry.AvailableProviders);
        Assert.True(registry.Status(ProviderKind.Local).IsInstalled);
        Assert.False(registry.Status(ProviderKind.Local).ApiKeyConfigured);
        Assert.Equal("No limit, stays on this PC", registry.Status(ProviderKind.Local).Summary);
        Assert.Equal(Assert.Single(local.Entries).Models[0].SelectionId, Assert.Single(registry.Models(ProviderKind.Local)).Id);
        using var fresh = new LocalDiscovery([]);
        runtime.OnMain(() => new ProviderRegistry(settings, fresh));
        Assert.Equal(server.BaseURL.Port, Assert.Single(fresh.Addresses).BaseURL.Port);
        Assert.NotNull(local.Resolve(Assert.Single(registry.Models(ProviderKind.Local)).Id));
    }

    [Fact]
    public async Task A_malformed_saved_address_does_not_discard_valid_servers_or_crash_the_registry()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var local = new LocalDiscovery([]);
        runtime.Defaults.Set("localServerAddresses", System.Text.Encoding.UTF8.GetBytes("[null,12,\"\",\"file:///tmp\",\"" + server.BaseURL + "\"]"));
        runtime.OnMain(() => new ProviderRegistry(new FakeProviderSettings(), local));
        Assert.Equal(server.BaseURL.Port, Assert.Single(local.Addresses).BaseURL.Port);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Concurrent_catalogue_callers_await_one_discovery_instead_of_returning_before_models_arrive()
    {
        await using var server = new LocalProviderLoopback();
        using var runtime = new TestRuntime();
        using var local = new LocalDiscovery([server.BaseURL]);
        var registry = runtime.OnMain(() => new ProviderRegistry(new FakeProviderSettings(), local));
        server.Stall("/v1/models");
        var first = runtime.OnMain(() => registry.LoadCatalog(ProviderKind.Local));
        var exchange = await server.NextRequest();
        var second = runtime.OnMain(() => registry.LoadCatalog(ProviderKind.Local));
        Assert.False(second.IsCompleted);
        await exchange.Respond(200, "Fake", System.Text.Encoding.UTF8.GetBytes("""{"data":[{"id":"test"}]}"""));
        await Task.Run(() => runtime.Loop.Run(() => Task.WhenAll(first, second), LoopbackHttpServer.Guard));
        Assert.Single(registry.Models(ProviderKind.Local));
        Assert.Single(server.Requests, request => request.Target == "/v1/models");
    }
}
