using System.IO.Compression;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis price catalogue in the points store (OD-0134, ED-NEW-ignis-data), against loopback stand-ins for GitHub (R-TEST-10).
// The flow's HTTP wire is a fail-closed handler, so a request to any host but the loopback stand-in would be refused and
// listed; every test asserts that list stays empty: no model provider is ever called. The clock is the manual TestRuntime one.
public sealed class IgnisPricesStoreTests
{
    private const string FeedPath = "/ignis/model-points.json";
    private const string CataloguePath = "/BerriAI/litellm/main/model_prices_and_context_window.json";

    private static FakeResponse Json(string body, params (string, string)[] headers) =>
        new(200, [("Content-Type", "application/json"), .. headers], Encoding.UTF8.GetBytes(body));

    private static IgnisPointsStore Store(string folder, FakeUpstreamServer server) =>
        new(folder, new Uri(server.BaseUrl + FeedPath), IgnisCatalogueFixture.Points, new Uri(server.BaseUrl + CataloguePath));

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_fetched_catalogue_is_kept_on_disk_found_after_the_curated_points_and_used_after_a_restart()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        await using var server = new FakeUpstreamServer { Handler = _ => Json(IgnisCatalogueFixture.LiteLlm, ("ETag", "\"v1\"")) };
        var store = Store(folder.Path, server);
        Assert.Equal((null, null), (store.PricesDay, store.PricesCheckedAt));
        Assert.Empty(store.PricesWarnings);
        Assert.Null(store.Find(ProviderKind.Claude, "claude-mythos-5"));
        var changed = 0;
        store.Changed += () => changed++;

        var check = await store.CheckPricesAsync(force: false, CancellationToken.None);

        Assert.Equal(new IgnisPricesCheck("2026-09-22", true, false), check);
        Assert.Equal(1, changed);
        var request = Assert.Single(server.Received);
        Assert.Equal(("GET", CataloguePath), (request.Method, request.Path));
        Assert.False(request.Headers.ContainsKey("If-None-Match"));
        Assert.Equal("gzip", request.Headers["Accept-Encoding"]);
        Assert.Equal(Path.Combine(folder.Path, "ignis", "prices.json"), store.PricesPath);
        Assert.True(File.Exists(store.PricesPath));
        Assert.True(new FileInfo(store.PricesPath).Length < IgnisCatalogueFixture.LiteLlm.Length);
        Assert.Equal(runtime.Time.GetUtcNow(), store.PricesCheckedAt);
        Assert.Equal(3, store.PricesWarnings.Count);
        Assert.Equal("2026.09.20", store.Current.Version); // the curated points are untouched

        // Curated first: Opus 5.5 is fresh (2 days old), so its curated prices stay; Haiku 5.5 is 52 days old, so it takes the
        // catalogue's prices and keeps its curated rules; GPT-6 Sol is old but 10x apart, so it stays curated.
        Assert.Equal(store.Current.Models[0], store.Find(ProviderKind.Claude, "opus"));
        var haiku = store.Find(ProviderKind.Claude, "haiku")!;
        Assert.Equal((0.1m, 900_000L, "Haiku 5.5"), (haiku.Prices.Input, haiku.TidyAt, haiku.DisplayName));
        Assert.Equal(20m, store.Find(ProviderKind.Codex, "gpt-6-sol")!.Prices.Input);
        // Then the catalogue, under the provider's rule.
        var mythos = store.Find(ProviderKind.Claude, "claude-mythos-5")!;
        Assert.Equal((10m, new IgnisCachePoints(3600, 300, true)), (mythos.Prices.Input, mythos.Cache));
        Assert.Equal(1.4m, store.Find(ProviderKind.Zai, "glm-5.3")!.Prices.Input);
        // Unknown models stay unknown.
        Assert.Null(store.Find(ProviderKind.Claude, "claude-unknown-9"));
        Assert.Null(store.Find(ProviderKind.Copilot, "gpt-6-sol"));

        var restarted = Store(folder.Path, server);
        Assert.Equal(("2026-09-22", "\"v1\""), (restarted.PricesDay, restarted.Catalogue!.ETag));
        Assert.Equal(store.Catalogue!.Models, restarted.Catalogue.Models);
        Assert.Equal(10m, restarted.Find(ProviderKind.Claude, "claude-mythos-5")!.Prices.Input);
        Assert.Single(server.Received);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task An_automatic_check_asks_at_most_once_a_day_and_an_unchanged_catalogue_costs_one_304()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        await using var server = new FakeUpstreamServer
        {
            Handler = request => request.Headers.TryGetValue("If-None-Match", out var tag) && tag == "\"v1\""
                ? new FakeResponse(304, [("ETag", "\"v1\"")], [])
                : Json(IgnisCatalogueFixture.LiteLlm, ("ETag", "\"v1\"")),
        };
        var store = Store(folder.Path, server);
        Assert.True((await store.CheckPricesAsync(force: false, CancellationToken.None)).Updated);
        var changed = 0;
        store.Changed += () => changed++;

        runtime.Advance(TimeSpan.FromHours(23));
        Assert.Equal(new IgnisPricesCheck("2026-09-22", false, true), await store.CheckPricesAsync(force: false, CancellationToken.None));
        Assert.Single(server.Received);

        runtime.Advance(TimeSpan.FromHours(1));
        var confirmed = await store.CheckPricesAsync(force: false, CancellationToken.None);
        Assert.Equal(new IgnisPricesCheck("2026-09-23", false, false), confirmed);
        Assert.Equal(2, server.Received.Count);
        Assert.Equal("\"v1\"", server.Received[1].Headers["If-None-Match"]);
        Assert.Equal(0, changed);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero), store.Catalogue!.FetchedAt);
        Assert.Equal(runtime.Time.GetUtcNow(), store.PricesCheckedAt);
        Assert.Equal("2026-09-23", Store(folder.Path, server).PricesDay); // the confirmation is kept on disk

        // Update asks at once (force), and a 304 still installs nothing.
        Assert.Equal(new IgnisPricesCheck("2026-09-23", false, false), await store.CheckPricesAsync(force: true, CancellationToken.None));
        Assert.Equal(3, server.Received.Count);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_missing_malformed_or_failing_catalogue_reports_an_error_and_keeps_what_is_held()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        var answer = new FakeResponse(404, [], []);
        await using var server = new FakeUpstreamServer { Handler = _ => answer };
        var store = Store(folder.Path, server);

        Assert.Equal(new IgnisPricesCheck(null, false, false, "The price catalogue was not found."), await store.CheckPricesAsync(force: false, CancellationToken.None));
        answer = Json("{ broken");
        Assert.StartsWith("The price catalogue is not JSON", (await store.CheckPricesAsync(force: false, CancellationToken.None)).Error, StringComparison.Ordinal);
        answer = Json(IgnisCatalogueFixture.LiteLlm);
        Assert.True((await store.CheckPricesAsync(force: false, CancellationToken.None)).Updated);
        var held = store.Catalogue;

        answer = new FakeResponse(500, [], []);
        Assert.Equal(new IgnisPricesCheck("2026-09-22", false, false, "The feed answered 500."), await store.CheckPricesAsync(force: true, CancellationToken.None));
        answer = Json("{\"x\":{\"litellm_provider\":\"bedrock\",\"mode\":\"chat\"}}");
        Assert.Equal("The price catalogue has no model for the mapped providers.", (await store.CheckPricesAsync(force: true, CancellationToken.None)).Error);
        Assert.Same(held, store.Catalogue);
        Assert.Equal(held!.CheckedAt, store.PricesCheckedAt);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_catalogue_over_8_MB_is_refused_whether_declared_or_streamed()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        var big = new string(' ', IgnisPriceCatalogue.MaximumBytes) + "{}";
        var streamed = false;
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => streamed
                ? new FakeResponse(200, [("Content-Type", "application/json")], [], [big[..(big.Length / 2)], big[(big.Length / 2)..]])
                : Json(big),
        };
        var store = Store(folder.Path, server);

        Assert.Equal("The feed's file is too large.", (await store.CheckPricesAsync(force: true, CancellationToken.None)).Error);
        streamed = true;
        Assert.Equal("The feed's file is too large.", (await store.CheckPricesAsync(force: true, CancellationToken.None)).Error);
        Assert.Equal(2, server.Received.Count);
        Assert.Null(store.Catalogue);
        Assert.False(File.Exists(store.PricesPath));
        Assert.Equal(8 * 1024 * 1024, IgnisPriceCatalogue.MaximumBytes);
        Assert.Equal(TimeSpan.FromSeconds(60), IgnisPriceCatalogue.Timeout);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_gzip_answer_is_unpacked()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(IgnisCatalogueFixture.LiteLlm));
        await using var server = new FakeUpstreamServer { Handler = _ => new FakeResponse(200, [("Content-Type", "application/json"), ("Content-Encoding", "gzip")], packed.ToArray()) };
        var store = Store(folder.Path, server);

        Assert.True((await store.CheckPricesAsync(force: true, CancellationToken.None)).Updated);
        Assert.Equal(10, store.Catalogue!.Models.Count);
        Assert.Empty(offline.Received);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("https://api.anthropic.com/v1/models")]
    [InlineData("https://api.openai.com/v1/models")]
    [InlineData("https://openrouter.ai/api/v1/models")]
    [InlineData("http://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json")]
    [InlineData("https://raw.githubusercontent.com.evil.example/x.json")]
    public async Task The_catalogue_refuses_every_host_but_GitHub_without_sending_a_request(string address)
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        var store = new IgnisPointsStore(folder.Path, LoopbackHttpServer.RefusedUrl(FeedPath), IgnisCatalogueFixture.Points, new Uri(address));

        var check = await store.CheckPricesAsync(force: true, CancellationToken.None);

        Assert.Equal(new IgnisPricesCheck(null, false, false, "The price catalogue must be on GitHub."), check);
        Assert.Empty(offline.Received);
        Assert.True(IgnisPointsFeed.IsAllowed(new Uri(IgnisPriceCatalogue.Url)));
        Assert.Equal("https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json", IgnisPriceCatalogue.Url);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_damaged_stored_catalogue_is_not_used()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        File.WriteAllText(Path.Combine(folder.Path, "ignis", "prices.json"), "{ broken");
        var store = new IgnisPointsStore(folder.Path, null, IgnisCatalogueFixture.Points);
        Assert.Null(store.Catalogue);
        Assert.Null(store.PricesDay);
        Assert.Null(store.Find(ProviderKind.Claude, "claude-mythos-5"));
        Assert.Equal("Opus 5.5", store.Find(ProviderKind.Claude, "opus")?.DisplayName);
    }
}
