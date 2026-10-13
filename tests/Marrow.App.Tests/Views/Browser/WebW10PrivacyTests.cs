using System.Text.Json.Nodes;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W10, Settings > Visus > "Clear browsing data" (per profile) and "Forget agent data" (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.1, 13.4; ED-NEW-web-w10-6). What nth kept of a profile goes at once (visits, kept site
// answers, remembered app answers, site zooms); the browser's own data is cleared through a page of the profile now, or written down to be cleared before the profile's next page. The other profile and private tabs are untouched.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-6")]
public sealed class WebW10PrivacyTests(HeadlessSession headless)
{
    private static string Folder()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        var folder = Path.Combine(data, "VisusPrivacy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string ClearLog(FakeWebView view) => string.Join("|", view.Log.Where(line => line.StartsWith("ClearProfileData", StringComparison.Ordinal)));

    private static void Seed(VisusRig visus)
    {
        var now = visus.Time.GetUtcNow();
        VisusHost.History.Record(VisusTabKind.Browse, "https://browse.example.org/a", "Browse page", now);
        VisusHost.History.Record(VisusTabKind.Preview, "http://localhost:5173/", "Preview page", now);
        VisusHost.Permissions.Set(WebProfileKind.Browse, "https://browse.example.org", SitePermissionKind.Camera, SitePermissionState.Allow);
        VisusHost.Permissions.Set(WebProfileKind.Preview, "http://localhost:5173", SitePermissionKind.Location, SitePermissionState.Block);
        VisusHost.SiteZoom.Set(WebProfiles.BrowseProfile, "https://browse.example.org/", 1.5);
        VisusHost.SiteZoom.Set(WebProfiles.PreviewProfile, "http://localhost:5173/", 1.25);
    }

    [Fact]
    public void Clearing_the_browse_profile_removes_what_nth_kept_of_it_at_once_and_writes_the_rest_down_when_no_page_is_open() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var folder = Folder();
        var before = VisusPrivacy.DataFolder;
        VisusPrivacy.DataFolder = () => folder;
        try
        {
            Seed(visus);
            Assert.False(VisusHost.IsStarted);
            var said = UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Browse));
            Assert.Equal("Cleared the history and saved answers of Visus tabs. Cookies, cache and site storage go before its next page opens.", said);

            Assert.DoesNotContain(VisusHost.History.Entries, entry => entry.Profile == WebProfileKind.Browse);
            Assert.Contains(VisusHost.History.Entries, entry => entry.Profile == WebProfileKind.Preview);
            Assert.DoesNotContain(VisusHost.Permissions.All, entry => entry.Profile == WebProfileKind.Browse);
            Assert.Contains(VisusHost.Permissions.All, entry => entry.Profile == WebProfileKind.Preview);
            Assert.Equal(1, VisusHost.SiteZoom.Get(WebProfiles.BrowseProfile, "https://browse.example.org/"));
            Assert.Equal(1.25, VisusHost.SiteZoom.Get(WebProfiles.PreviewProfile, "http://localhost:5173/"));

            var file = Path.Combine(folder, "nth", WebProfileClears.FileName);
            Assert.True(File.Exists(file));
            var saved = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.NotNull(saved["pending"]!["browse"]);
            Assert.Null(saved["pending"]!["preview"]);
        }
        finally
        {
            VisusPrivacy.DataFolder = before;
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void Clearing_the_preview_profile_leaves_the_browse_profile_alone() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var folder = Folder();
        var before = VisusPrivacy.DataFolder;
        VisusPrivacy.DataFolder = () => folder;
        try
        {
            Seed(visus);
            var said = UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Preview));
            Assert.StartsWith("Cleared the history and saved answers of Project previews.", said, StringComparison.Ordinal);
            Assert.Contains(VisusHost.History.Entries, entry => entry.Profile == WebProfileKind.Browse);
            Assert.DoesNotContain(VisusHost.History.Entries, entry => entry.Profile == WebProfileKind.Preview);
            Assert.Contains(VisusHost.Permissions.All, entry => entry.Profile == WebProfileKind.Browse);
            Assert.Equal(1.5, VisusHost.SiteZoom.Get(WebProfiles.BrowseProfile, "https://browse.example.org/"));
            var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "nth", WebProfileClears.FileName)))!;
            Assert.NotNull(saved["pending"]!["preview"]);
            Assert.Null(saved["pending"]!["browse"]);
        }
        finally
        {
            VisusPrivacy.DataFolder = before;
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void With_a_page_of_the_profile_open_the_browser_data_is_cleared_through_it_now() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Engine.ProfileClears = new WebProfileClears();
        var browse = visus.Page("https://browse.example.org/a");
        var preview = visus.Page("http://localhost:5173/", VisusTabKind.Preview);
        var privateTab = visus.Page("https://private.example.org/", VisusTabKind.Private);
        Seed(visus);
        var said = UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Browse));
        Assert.Equal("Cleared the browsing data of Visus tabs.", said);
        Assert.Equal("ClearProfileData browse", ClearLog(VisusRig.Live(browse)));
        Assert.Equal("", ClearLog(VisusRig.Live(preview)));
        Assert.Equal("", ClearLog(VisusRig.Live(privateTab)));
    });

    [Fact]
    public void Forgetting_agent_data_clears_the_grants_and_the_open_agent_pages() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Engine.ProfileClears = new WebProfileClears();
        var grants = new WebAgentGrants();
        grants.GrantSite("https://shop.example.org");
        var before = VisusAgentStartup.Grants;
        VisusAgentStartup.Grants = grants;
        try
        {
            Assert.True(grants.Site("https://shop.example.org"));
            var agent = visus.Page("https://shop.example.org/", VisusTabKind.Agent);
            var browse = visus.Page("https://browse.example.org/");
            var said = UiAwait.Of(VisusPrivacy.ForgetAgentDataAsync());
            Assert.Equal("Forgot the agent data. The sites you always allowed are gone too.", said);
            Assert.False(grants.Site("https://shop.example.org"));
            Assert.StartsWith("ClearProfileData agent-", ClearLog(VisusRig.Live(agent)), StringComparison.Ordinal);
            Assert.Equal("", ClearLog(VisusRig.Live(browse)));
        }
        finally
        {
            VisusAgentStartup.Grants = before;
        }
    });

    [Fact]
    public void Forgetting_agent_data_with_no_page_open_says_what_is_left_for_the_next_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var folder = Folder();
        var before = VisusPrivacy.DataFolder;
        VisusPrivacy.DataFolder = () => folder;
        try
        {
            var said = UiAwait.Of(VisusPrivacy.ForgetAgentDataAsync());
            Assert.Equal("Forgot the sites you always allowed. What the model's pages kept goes before each one's next page opens.", said);
            var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "nth", WebProfileClears.FileName)))!;
            Assert.Equal(1, saved["agents"]!["generation"]!.GetValue<int>());
        }
        finally
        {
            VisusPrivacy.DataFolder = before;
            Directory.Delete(folder, recursive: true);
        }
    });

    [Fact]
    public void A_profile_that_keeps_nothing_is_not_cleared_and_says_nothing() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        Assert.Equal("", UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Private)));
        Assert.Equal("", UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Artifacts)));
        Assert.Equal("", UiAwait.Of(VisusPrivacy.ClearBrowsingDataAsync(WebProfileKind.Agent)));
    });
}
