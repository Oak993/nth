using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Local;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Owner scope: OD-0114, multi-account-NOTES Local/no-cap items, FEASIBILITY section 5; ED-410.
[Trait("Rule", "R-TEST-10")]
[Trait("Feature", "MA-3")]
public sealed class LocalDiscoveryTests
{
    [Fact]
    public async Task Default_candidates_are_only_loopback_on_the_four_requested_ports_and_use_no_key()
    {
        await using var server = new LocalProviderLoopback();
        Assert.Equal([11434, 1234, 8080, 8000], LocalDiscovery.DefaultAddresses.Select(uri => uri.Port));
        Assert.All(LocalDiscovery.DefaultAddresses, uri => Assert.True(uri.IsLoopback));
        server.Reply("/v1/models", """{"data":[{"id":"test"}]}""");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        Assert.Equal(ProviderKind.Local, entry.Server.Provider);
        Assert.Equal("local", ProviderKind.Local.Id);
        Assert.Equal("Local", ProviderKind.Local.DisplayName);
        Assert.False(ProviderKind.Local.IsAPIKeyBased);
        Assert.False(ProviderKind.Local.AcceptsAPIKey);
        Assert.Null(ProviderKind.Local.ApiKeyEnvVar);
        Assert.False(entry.Server.HasLimit);
        Assert.Equal("No limit, stays on this PC", entry.Server.LimitSummary);
        Assert.All(server.Requests, request => Assert.Empty(request.HeaderValues("Authorization")));
    }

    [Fact]
    public async Task Any_number_of_servers_keep_separate_entries_even_when_the_model_ids_match()
    {
        var servers = Enumerable.Range(0, 12).Select(_ => new LocalProviderLoopback()).ToArray();
        try
        {
            using var discovery = new LocalDiscovery([]);
            foreach (var server in servers)
            {
                server.Reply("/v1/models", """{"data":[{"id":"same","context_length":8192,"supports_tool_calling":true}]}""");
                discovery.AddAddress(server.BaseURL.Authority); // host:port without a scheme
            }
            var entries = await discovery.Discover();
            Assert.Equal(12, entries.Count);
            Assert.Equal(12, entries.Select(entry => entry.Server.Id).Distinct().Count());
            Assert.Equal(12, entries.SelectMany(entry => entry.Models).Select(model => model.SelectionId).Distinct().Count());
            Assert.All(entries, entry => Assert.Equal("same", Assert.Single(entry.Models).Id));
            Assert.All(servers, server => Assert.Contains(server.Requests, request => request.Target == "/v1/models"));
        }
        finally { foreach (var server in servers) await server.DisposeAsync(); }
    }

    [Fact]
    public async Task Addresses_include_LAN_DNS_and_IPv6_without_scanning_the_network_and_normalize_v1()
    {
        await using var server = new LocalProviderLoopback();
        using var discovery = new LocalDiscovery([]);
        var lan = discovery.AddAddress("192.168.1.50:9000");
        var dns = discovery.AddAddress("other-pc:1234");
        var ipv6 = discovery.AddAddress("http://[fd00::2]:8080/v1/");
        var remote = discovery.AddAddress("https://remote.example:8443/v1");
        Assert.Equal("http://192.168.1.50:9000/v1/", lan.BaseURL.AbsoluteUri);
        Assert.Equal("http://other-pc:1234/v1/", dns.BaseURL.AbsoluteUri);
        Assert.Equal("http://[fd00::2]:8080/v1/", ipv6.BaseURL.AbsoluteUri);
        Assert.Equal("No limit", lan.LimitLabel);
        Assert.Equal("Stays on your network", lan.PrivacyLabel);
        Assert.Equal("Uses remote.example", remote.PrivacyLabel);
        var first = discovery.AddAddress(server.BaseURL.Authority);
        Assert.Equal(first, discovery.AddAddress(server.BaseURL + "v1/"));
        Assert.Equal(5, discovery.Addresses.Count); // duplicates do not create an account
        Assert.Empty(server.Requests); // adding addresses never sends a request
    }

    [Theory]
    [InlineData("file:///tmp/models")]
    [InlineData("http://user:secret@localhost:11434")]
    [InlineData("http://localhost:11434/v1?key=secret")]
    [InlineData("http://localhost:11434/#fragment")]
    public async Task Invalid_or_credential_bearing_addresses_are_rejected_before_HTTP(string address)
    {
        await using var server = new LocalProviderLoopback();
        using var discovery = new LocalDiscovery([server.BaseURL]);
        Assert.Throws<ArgumentException>(() => discovery.AddAddress(address));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Ollama_tags_and_show_supply_tools_and_effective_context_without_loading_a_model()
    {
        await using var server = new LocalProviderLoopback();
        server.Reply("/v1/models", "{}", 404);
        server.Reply("/api/tags", """{"models":[{"name":"tiny:latest"},{"model":"text:latest"}]}""");
        server.Reply("/api/show", """{"capabilities":["completion","tools"],"parameters":"num_ctx 8192","model_info":{"llama.context_length":131072}}""");
        server.Reply("/api/show", """{"capabilities":["completion"],"model_info":{"llama.context_length":32768}}""");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var entry = Assert.Single(await discovery.Discover());
        Assert.Equal(2, entry.Models.Count);
        Assert.True(entry.Models[0].Capabilities.ToolCalling);
        Assert.Equal(8192, entry.Models[0].Capabilities.ContextWindow);
        Assert.True(entry.Models[0].Capabilities.ContextReported);
        Assert.False(entry.Models[1].Capabilities.ToolCalling);
        Assert.Equal(4096, entry.Models[1].Capabilities.ContextWindow); // max is not loaded context
        Assert.False(entry.Models[1].Capabilities.ContextReported);
        Assert.All(server.Requests.Where(request => request.Target == "/api/show"), request =>
            Assert.Contains("\"model\"", System.Text.Encoding.UTF8.GetString(request.Body), StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, request => request.Target.Contains("pull", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/v0/models", """{"data":[{"id":"test","loaded_context_length":16384,"max_context_length":131072,"capabilities":{"function_calling":true}}]}""", 16384)]
    [InlineData("/props", """{"default_generation_settings":{"n_ctx":8192},"supports_tool_calling":true}""", 8192)]
    public async Task Server_metadata_supplements_a_plain_OpenAI_list(string path, string json, int context)
    {
        await using var server = new LocalProviderLoopback();
        server.Reply("/v1/models", """{"data":[{"id":"test"}]}""");
        server.Reply(path, json);
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var model = Assert.Single(Assert.Single(await discovery.Discover()).Models);
        Assert.True(model.Capabilities.ToolCalling);
        Assert.Equal(context, model.Capabilities.ContextWindow);
        Assert.True(model.Capabilities.ContextReported);
    }

    [Fact]
    public async Task Vllm_metadata_and_unknown_capabilities_do_not_invent_model_abilities()
    {
        await using var server = new LocalProviderLoopback();
        server.Reply("/v1/models", """{"data":[{"id":"vllm","max_model_len":24576,"supports_tool_calling":false},{"id":"unknown","context_length":-1},{"id":2},{"id":""}]}""");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var models = Assert.Single(await discovery.Discover()).Models;
        Assert.Equal(2, models.Count);
        Assert.False(models[0].Capabilities.ToolCalling);
        Assert.Equal(24576, models[0].Capabilities.ContextWindow);
        Assert.Null(models[1].Capabilities.ToolCalling);
        Assert.Equal(4096, models[1].Capabilities.ContextWindow);
        Assert.False(models[1].Capabilities.ContextReported);
        Assert.Contains("Tool support unknown", models[1].Option.Detail, StringComparison.Ordinal);
        Assert.Contains("4096 context fallback", models[1].Option.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(500, "{\"data\":[{\"id\":\"test\"}]}")]
    [InlineData(200, "not json")]
    [InlineData(200, "{\"data\":[]}")]
    public async Task Failed_or_malformed_listings_do_not_advertise_a_server(int status, string body)
    {
        await using var server = new LocalProviderLoopback();
        server.Reply("/v1/models", body, status);
        using var discovery = new LocalDiscovery([server.BaseURL]);
        Assert.Empty(await discovery.Discover());
    }

    [Fact]
    public async Task One_stalled_server_is_bounded_and_does_not_hide_a_healthy_one()
    {
        await using var stalled = new LocalProviderLoopback();
        await using var healthy = new LocalProviderLoopback();
        stalled.Stall("/v1/models");
        healthy.Reply("/v1/models", """{"data":[{"id":"test","context_length":8192,"supports_tool_calling":true}]}""");
        var clock = new ManualTimeProvider();
        using var discovery = new LocalDiscovery([stalled.BaseURL, healthy.BaseURL], TimeSpan.FromMilliseconds(150), clock);
        var task = discovery.Discover();
        await stalled.NextRequest();
        await healthy.NextRequest();
        await healthy.NextRequest(); // tags: the healthy model listing was already read
        clock.Advance(TimeSpan.FromMilliseconds(150));
        var entry = Assert.Single(await task.WaitAsync(LoopbackHttpServer.Guard));
        Assert.Equal(healthy.BaseURL.Port, entry.Server.BaseURL.Port);
        Assert.Single(stalled.Requests);
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        await using var server = new LocalProviderLoopback();
        server.Stall("/v1/models");
        using var discovery = new LocalDiscovery([server.BaseURL]);
        using var cancel = new CancellationTokenSource();
        var task = discovery.Discover(cancel.Token);
        await server.NextRequest();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Redirects_never_discover_an_unchosen_destination()
    {
        await using var source = new LocalProviderLoopback();
        await using var destination = new LocalProviderLoopback();
        source.Reply("/v1/models", "{}", 302, ("Location", destination.BaseURL + "v1/models"));
        destination.Reply("/v1/models", """{"data":[{"id":"test"}]}""");
        using var discovery = new LocalDiscovery([source.BaseURL]);
        Assert.Empty(await discovery.Discover());
        Assert.Empty(destination.Requests);
    }

    [Fact]
    public async Task Ollama_show_also_supplements_a_successful_v1_list_and_identity_survives_rediscovery()
    {
        await using var server = new LocalProviderLoopback();
        using var discovery = new LocalDiscovery([server.BaseURL]);
        var selections = new List<string>();
        for (var pass = 0; pass < 2; pass++)
        {
            server.Reply("/v1/models", """{"data":[{"id":"tiny:latest"}]}""");
            server.Reply("/api/tags", """{"models":[{"name":"tiny:latest"}]}""");
            server.Reply("/api/show", """{"capabilities":["tools"],"parameters":"num_ctx 8192"}""");
            var model = Assert.Single(Assert.Single(await discovery.Discover()).Models);
            Assert.True(model.Capabilities.ToolCalling);
            Assert.Equal(8192, model.Capabilities.ContextWindow);
            selections.Add(model.SelectionId);
        }
        Assert.Equal(selections[0], selections[1]);
        using var relaunched = new LocalDiscovery([new Uri(server.BaseURL + "v1/")]);
        Assert.Equal(Assert.Single(discovery.Addresses).Id, Assert.Single(relaunched.Addresses).Id);
    }
}
