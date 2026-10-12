using System.Text.Json.Nodes;
using Marrow.App.Views.Browser;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: W8c's per-site zoom file (ED-NEW-web-w8c-9, LEAN-TESTS 10): a version, unknown members kept, a newer file left alone, a cap.
[Trait("Decision", "ED-NEW-web-w8c-9")]
public sealed class VisusSiteZoomStoreTests
{
    private const string Site = "https://example.test/";

    [Fact]
    public void The_file_carries_its_version_and_reads_back()
    {
        var store = new VisusSiteZoom();
        store.Set(WebProfiles.BrowseProfile, Site, 1.25);
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(store.ToJson()!));
        Assert.Equal(VisusSiteZoom.FormatVersion, (int)root["version"]!);
        Assert.Equal(1.25, VisusSiteZoom.FromJson(store.ToJson()).Get(WebProfiles.BrowseProfile, Site));
    }

    [Fact]
    public void Members_this_version_does_not_know_survive_a_load_and_save()
    {
        var text = "{\"version\":1,\"later\":{\"keep\":[1,2,3]},\"profiles\":{\"" + WebProfiles.BrowseProfile + "\":{\"https://example.test:443\":1.5}}}";
        var saved = JsonNode.Parse(VisusSiteZoom.FromJson(text).ToJson()!)!.AsObject();
        Assert.Equal("[1,2,3]", saved["later"]!["keep"]!.ToJsonString());
        Assert.Equal(1.5, VisusSiteZoom.FromJson(saved.ToJsonString()).Get(WebProfiles.BrowseProfile, Site));
    }

    [Fact]
    public void A_file_from_a_newer_version_reads_as_empty_and_is_never_written_over()
    {
        var text = "{\"version\":2,\"profiles\":{\"" + WebProfiles.BrowseProfile + "\":{\"https://example.test:443\":1.5}}}";
        var store = VisusSiteZoom.FromJson(text);
        Assert.Equal(1, store.Get(WebProfiles.BrowseProfile, Site));
        store.Set(WebProfiles.BrowseProfile, Site, 2);
        Assert.Null(store.ToJson());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":1,\"profiles\":[]}")]
    [InlineData("{\"version\":1,\"profiles\":{\"browse\":5}}")]
    public void A_file_that_does_not_read_is_empty_and_the_next_change_writes_a_fresh_one(string text)
    {
        var store = VisusSiteZoom.FromJson(text);
        Assert.Equal(1, store.Get(WebProfiles.BrowseProfile, Site));
        store.Set(WebProfiles.BrowseProfile, Site, 1.1);
        Assert.Equal(1.1, VisusSiteZoom.FromJson(store.ToJson()).Get(WebProfiles.BrowseProfile, Site));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("5.5")]
    [InlineData("\"wide\"")]
    public void A_record_outside_the_ladder_is_not_read(string factor)
    {
        var text = "{\"version\":1,\"profiles\":{\"" + WebProfiles.BrowseProfile + "\":{\"https://example.test:443\":" + factor + ",\"https://ok.test:443\":2}}}";
        var store = VisusSiteZoom.FromJson(text);
        Assert.Equal(1, store.Get(WebProfiles.BrowseProfile, Site));
        Assert.Equal(2, store.Get(WebProfiles.BrowseProfile, "https://ok.test/"));
    }

    [Fact]
    public void Resetting_a_site_to_100_percent_drops_its_record_and_says_so()
    {
        var store = new VisusSiteZoom();
        var changes = new List<string>();
        store.Changed += (_, origin) => changes.Add(origin);
        store.Set(WebProfiles.BrowseProfile, Site, 1.5);
        store.Set(WebProfiles.BrowseProfile, Site, 1);
        store.Set(WebProfiles.BrowseProfile, Site, 1);
        Assert.False(store.Contains(WebProfiles.BrowseProfile, Site));
        Assert.DoesNotContain("example.test", store.ToJson(), StringComparison.Ordinal);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_profile_keeps_its_newest_sites_and_a_site_set_again_is_the_newest()
    {
        var store = new VisusSiteZoom();
        store.Set(WebProfiles.BrowseProfile, "https://first.test/", 1.5);
        for (var i = 0; i < VisusSiteZoom.MaxSitesPerProfile - 1; i++) store.Set(WebProfiles.BrowseProfile, $"https://site{i}.test/", 1.1);
        store.Set(WebProfiles.BrowseProfile, "https://first.test/", 1.25);
        store.Set(WebProfiles.BrowseProfile, "https://extra.test/", 1.1);
        Assert.Equal(1.25, store.Get(WebProfiles.BrowseProfile, "https://first.test/"));
        Assert.False(store.Contains(WebProfiles.BrowseProfile, "https://site0.test/"));
        Assert.True(store.Contains(WebProfiles.BrowseProfile, "https://extra.test/"));
        var saved = JsonNode.Parse(store.ToJson()!)!["profiles"]![WebProfiles.BrowseProfile]!.AsObject();
        Assert.Equal(VisusSiteZoom.MaxSitesPerProfile, saved.Count);
    }

    [Fact]
    public void The_private_set_is_capped_the_same_way_and_never_written()
    {
        var store = new VisusSiteZoom();
        for (var i = 0; i < VisusSiteZoom.MaxSitesPerProfile + 5; i++) store.Set(WebProfiles.PrivateProfile, $"https://p{i}.test/", 1.1);
        Assert.False(store.Contains(WebProfiles.PrivateProfile, "https://p0.test/"));
        Assert.True(store.Contains(WebProfiles.PrivateProfile, $"https://p{VisusSiteZoom.MaxSitesPerProfile + 4}.test/"));
        Assert.DoesNotContain("p0.test", store.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("p504.test", store.ToJson(), StringComparison.Ordinal);
    }
}
