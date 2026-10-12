using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W10, "Clear browsing data" and "Forget agent data" at the engine (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.1, 13.4; ED-NEW-web-w10-6).
// A profile with a page open is cleared through that page now; a profile with none is cleared before its next page navigates; once per request, and a
// refused clear stays waiting.
[Trait("Decision", "ED-NEW-web-w10-6")]
public sealed class WebProfileClearEngineTests
{
    private const string Thread = @"C:\threads\one";

    private static string ClearLog(FakeWebView view) => string.Join("|", view.Log.Where(line => line.StartsWith("ClearProfileData", StringComparison.Ordinal)));

    [Fact]
    public void A_view_of_a_profile_with_a_waiting_clear_clears_it_before_it_can_navigate_and_then_the_request_is_done()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        rig.Engine.ProfileClears.Request(WebProfiles.BrowseProfile);

        var view = rig.OpenLive(WebSurfaceKind.VisusTab);
        var fake = WebRig.Fake(view);
        Assert.Equal("ClearProfileData browse", ClearLog(fake));
        Assert.True(fake.Log.IndexOf("ClearProfileData browse") > fake.Log.IndexOf("Attach"));
        Assert.False(rig.Engine.ProfileClears.IsPending(WebProfiles.BrowseProfile));

        // the next view of the profile finds nothing waiting
        var again = WebRig.Fake(rig.OpenLive(WebSurfaceKind.VisusTab));
        Assert.Equal("", ClearLog(again));
    }

    [Fact]
    public void A_profile_nobody_asked_about_is_left_alone()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        rig.Engine.ProfileClears.Request(WebProfiles.BrowseProfile);
        Assert.Equal("", ClearLog(WebRig.Fake(rig.OpenLive(WebSurfaceKind.PreviewTab, url: "http://localhost:5173/"))));
        Assert.Equal("", ClearLog(WebRig.Fake(rig.OpenLive(WebSurfaceKind.VisusPrivateTab))));
        Assert.True(rig.Engine.ProfileClears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public void A_clear_the_browser_refused_stays_waiting_for_the_next_page()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        rig.Engine.ProfileClears.Request(WebProfiles.PreviewProfile);
        rig.Engine.ClearSucceeds = false;
        _ = rig.OpenLive(WebSurfaceKind.PreviewTab, url: "http://localhost:5173/");
        Assert.True(rig.Engine.ProfileClears.IsPending(WebProfiles.PreviewProfile));
        rig.Engine.ClearSucceeds = true;
        var next = WebRig.Fake(rig.OpenLive(WebSurfaceKind.PreviewTab, url: "http://localhost:5173/"));
        Assert.Equal("ClearProfileData preview", ClearLog(next));
        Assert.False(rig.Engine.ProfileClears.IsPending(WebProfiles.PreviewProfile));
    }

    [Fact]
    public void Forgetting_agent_data_clears_each_agent_profile_before_its_first_page_after_the_request_and_once()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        var before = WebRig.Fake(rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread));
        Assert.Equal("", ClearLog(before));

        rig.Engine.ProfileClears.RequestAllAgents();
        var first = WebRig.Fake(rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread));
        var name = WebProfiles.AgentProfileName(Thread);
        Assert.Equal("ClearProfileData " + name, ClearLog(first));
        Assert.Equal("", ClearLog(WebRig.Fake(rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread))));

        var other = WebRig.Fake(rig.OpenLive(WebSurfaceKind.AgentTab, folder: @"C:\threads\two"));
        Assert.Equal("ClearProfileData " + WebProfiles.AgentProfileName(@"C:\threads\two"), ClearLog(other));
        Assert.Equal("", ClearLog(WebRig.Fake(rig.OpenLive(WebSurfaceKind.VisusTab))));
    }

    [Fact]
    public async Task Clearing_now_goes_through_one_page_of_each_matching_profile_and_marks_the_request_done()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        var one = rig.OpenLive(WebSurfaceKind.VisusTab);
        var two = rig.OpenLive(WebSurfaceKind.VisusTab, url: "https://b.test/");
        var preview = rig.OpenLive(WebSurfaceKind.PreviewTab, url: "http://localhost:5173/");
        rig.Engine.ProfileClears.Request(WebProfiles.BrowseProfile);

        var cleared = await rig.Surface.ClearProfilesNowAsync(creation => creation.ProfileName == WebProfiles.BrowseProfile);

        Assert.Equal(1, cleared);
        var calls = new[] { one, two }.Select(view => ClearLog(WebRig.Fake(view))).Where(line => line.Length > 0).ToList();
        Assert.Equal(["ClearProfileData browse"], calls);
        Assert.Equal("", ClearLog(WebRig.Fake(preview)));
        Assert.False(rig.Engine.ProfileClears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public async Task Clearing_now_with_no_page_open_clears_nothing_and_leaves_the_request_waiting()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        rig.Engine.ProfileClears.Request(WebProfiles.BrowseProfile);
        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/", null); // opened, never attached to a window
        _ = view;
        Assert.Equal(0, await rig.Surface.ClearProfilesNowAsync(creation => creation.Kind == WebProfileKind.Browse));
        Assert.True(rig.Engine.ProfileClears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public async Task A_clear_the_browser_refused_is_not_counted_and_not_marked_done()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        _ = rig.OpenLive(WebSurfaceKind.VisusTab);
        rig.Engine.ProfileClears.Request(WebProfiles.BrowseProfile);
        rig.Engine.ClearSucceeds = false;
        Assert.Equal(0, await rig.Surface.ClearProfilesNowAsync(creation => creation.ProfileName == WebProfiles.BrowseProfile));
        Assert.True(rig.Engine.ProfileClears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public async Task Forgetting_agent_data_now_reaches_every_open_agent_profile_once_and_no_other_kind()
    {
        using var rig = new WebRig();
        rig.Engine.ProfileClears = new WebProfileClears();
        var a = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var a2 = rig.OpenLive(WebSurfaceKind.AgentTab, folder: Thread);
        var b = rig.OpenLive(WebSurfaceKind.AgentTab, folder: @"C:\threads\two");
        var browse = rig.OpenLive(WebSurfaceKind.VisusTab);
        rig.Engine.ProfileClears.RequestAllAgents();
        var cleared = await rig.Surface.ClearProfilesNowAsync(creation => creation.Kind == WebProfileKind.Agent);
        Assert.Equal(2, cleared);
        Assert.Equal(1, new[] { a, a2 }.Count(view => ClearLog(WebRig.Fake(view)).Length > 0));
        Assert.NotEqual("", ClearLog(WebRig.Fake(b)));
        Assert.Equal("", ClearLog(WebRig.Fake(browse)));
        Assert.False(rig.Engine.ProfileClears.IsPending(WebProfiles.AgentProfileName(Thread)));
        Assert.False(rig.Engine.ProfileClears.IsPending(WebProfiles.AgentProfileName(@"C:\threads\two")));
    }
}
