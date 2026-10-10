using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the points store and its GitHub-only feed, against a loopback stand-in feed
// (R-TEST-10). The flow's HTTP wire is a fail-closed handler, so a request to any host but the loopback feed would be
// refused and listed; every test asserts that list stays empty: no model provider is ever called.
public sealed class IgnisPointsStoreTests
{
    private const string FeedPath = "/ignis/model-points.json";

    private static string WithVersion(string version) =>
        IgnisPointsData.Json.Replace("\"version\": \"2026.10.10\"", "\"version\": \"" + version + "\"", StringComparison.Ordinal);

    private static FakeResponse Json(string body) => new(200, [("Content-Type", "application/json")], Encoding.UTF8.GetBytes(body));

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task An_unpublished_feed_keeps_the_bundled_points_and_says_up_to_date()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        await using var feed = new FakeUpstreamServer { Handler = _ => new FakeResponse(404, [], []) };
        var store = new IgnisPointsStore(folder.Path, new Uri(feed.BaseUrl + FeedPath));
        Assert.Equal(IgnisPointsOrigin.Bundled, store.Origin);
        Assert.Null(store.CheckedAt);

        var check = await store.CheckAsync(install: true, CancellationToken.None);

        Assert.Equal(new IgnisPointsCheck("2026.10.10", null, false), check);
        Assert.True(check.UpToDate);
        Assert.Equal(runtime.Time.GetUtcNow(), store.CheckedAt);
        Assert.False(File.Exists(store.StoredPath));
        Assert.Equal(FeedPath, Assert.Single(feed.Received).Path);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_newer_valid_file_is_installed_kept_on_disk_and_used_after_a_restart()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        var newer = WithVersion("2026.10.20");
        await using var feed = new FakeUpstreamServer { Handler = _ => Json(newer) };
        var store = new IgnisPointsStore(folder.Path, new Uri(feed.BaseUrl + FeedPath));
        var changed = 0;
        store.Changed += () => changed++;

        var look = await store.CheckAsync(install: false, CancellationToken.None);
        Assert.Equal(new IgnisPointsCheck("2026.10.10", "2026.10.20", false), look);
        Assert.False(look.UpToDate);
        Assert.Equal(0, changed);

        var check = await store.CheckAsync(install: true, CancellationToken.None);
        Assert.Equal(new IgnisPointsCheck("2026.10.20", "2026.10.20", true), check);
        Assert.True(check.UpToDate);
        Assert.Equal(1, changed);
        Assert.Equal(IgnisPointsOrigin.Stored, store.Origin);
        Assert.Equal("2026.10.20", store.Current.Version);
        Assert.Equal(Path.Combine(folder.Path, "ignis", "model-points.json"), store.StoredPath);
        Assert.Equal(Encoding.UTF8.GetBytes(newer), File.ReadAllBytes(store.StoredPath));
        Assert.Equal("Opus 5.5", store.Find(ProviderKind.Claude, "opus")?.DisplayName);

        var restarted = new IgnisPointsStore(folder.Path, new Uri(feed.BaseUrl + FeedPath));
        Assert.Equal(IgnisPointsOrigin.Stored, restarted.Origin);
        Assert.Equal("2026.10.20", restarted.Current.Version);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task An_older_fetched_file_keeps_the_bundled_points()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        await using var feed = new FakeUpstreamServer { Handler = _ => Json(WithVersion("2026.10.01")) };
        var store = new IgnisPointsStore(folder.Path, new Uri(feed.BaseUrl + FeedPath));

        var check = await store.CheckAsync(install: true, CancellationToken.None);

        Assert.Equal(new IgnisPointsCheck("2026.10.10", "2026.10.01", false), check);
        Assert.True(check.UpToDate);
        Assert.Equal(IgnisPointsOrigin.Bundled, store.Origin);
        Assert.False(File.Exists(store.StoredPath));
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public async Task A_malformed_or_failing_feed_reports_an_error_and_changes_nothing()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        var answer = Json("{\"schemaVersion\":1,\"version\":\"2026.12.01\",\"models\":[{\"provider\":\"claude\"}]}");
        await using var feed = new FakeUpstreamServer { Handler = _ => answer };
        var store = new IgnisPointsStore(folder.Path, new Uri(feed.BaseUrl + FeedPath));

        var malformed = await store.CheckAsync(install: true, CancellationToken.None);
        Assert.Equal("2026.10.10", malformed.Installed);
        Assert.False(malformed.Updated);
        Assert.False(malformed.UpToDate);
        Assert.StartsWith("The feed's model points are invalid: models[0]", malformed.Error, StringComparison.Ordinal);
        Assert.Null(store.CheckedAt);

        answer = new FakeResponse(500, [], []);
        var failing = await store.CheckAsync(install: true, CancellationToken.None);
        Assert.Equal("The feed answered 500.", failing.Error);
        Assert.Equal(IgnisPointsOrigin.Bundled, store.Origin);
        Assert.False(File.Exists(store.StoredPath));
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_stored_copy_that_is_corrupt_or_not_newer_than_the_bundled_one_is_not_used()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "ignis"));
        var stored = Path.Combine(folder.Path, "ignis", "model-points.json");

        File.WriteAllText(stored, "{ broken");
        Assert.Equal(IgnisPointsOrigin.Bundled, new IgnisPointsStore(folder.Path).Origin);

        File.WriteAllText(stored, WithVersion("2026.10.10"));
        Assert.Equal(IgnisPointsOrigin.Bundled, new IgnisPointsStore(folder.Path).Origin);

        File.WriteAllText(stored, WithVersion("2026.10.09"));
        var store = new IgnisPointsStore(folder.Path);
        Assert.Equal(IgnisPointsOrigin.Bundled, store.Origin);
        Assert.Equal("2026.10.10", store.Current.Version);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("https://api.anthropic.com/v1/messages")]
    [InlineData("https://api.openai.com/v1/responses")]
    [InlineData("https://chatgpt.com/backend-api/codex")]
    [InlineData("http://raw.githubusercontent.com/Oak993/marrow-releases/main/ignis/model-points.json")]
    [InlineData("https://raw.githubusercontent.com.evil.example/x.json")]
    public async Task The_feed_refuses_every_host_but_GitHub_without_sending_a_request(string address)
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var offline = new InProcessHttpHandler();
        using var wire = URLSession.Transport.Override(offline);
        Assert.False(IgnisPointsFeed.IsAllowed(new Uri(address)));

        var check = await new IgnisPointsStore(folder.Path, new Uri(address)).CheckAsync(install: true, CancellationToken.None);

        Assert.Equal("The model points feed must be on GitHub.", check.Error);
        Assert.Empty(offline.Received);
        Assert.True(IgnisPointsFeed.IsAllowed(new Uri(IgnisPointsFeed.Url)));
        Assert.Equal("https://raw.githubusercontent.com/Oak993/marrow-releases/main/ignis/model-points.json", IgnisPointsFeed.Url);
    }
}
