using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.3 (profiles, processes), 5.8 (DevTools), 13.2 (profiles: environment options identical across surfaces).
[Trait("Decision", "ED-NEW-web-w7-2")]
[Trait("Feature", "W-7")]
public sealed class WebProfilesTests
{
    private static readonly WebProfiles Profiles = new(WebRig.UserData, "en-US");

    [Theory]
    [InlineData(WebSurfaceKind.VisusTab, WebProfileKind.Browse, "browse", false)]
    [InlineData(WebSurfaceKind.VisusPrivateTab, WebProfileKind.Private, "private", true)]
    [InlineData(WebSurfaceKind.PreviewTab, WebProfileKind.Preview, "preview", false)]
    [InlineData(WebSurfaceKind.Artifact, WebProfileKind.Artifacts, "artifacts", true)]
    [InlineData(WebSurfaceKind.InlineWidget, WebProfileKind.Artifacts, "artifacts", true)]
    [InlineData(WebSurfaceKind.McpApp, WebProfileKind.Artifacts, "artifacts", true)]
    public void Each_surface_gets_its_profile(WebSurfaceKind kind, WebProfileKind profile, string name, bool inPrivate)
    {
        var options = Profiles.For(kind);
        Assert.Equal(profile, options.Kind);
        Assert.Equal(name, options.ProfileName);
        Assert.Equal(inPrivate, options.IsInPrivate);
    }

    [Fact]
    public void Visus_surfaces_create_their_environment_with_identical_options()
    {
        // One environment, one browser process group: WebView2 refuses a second environment on the same folder with
        // different options, so only the controller options (profile, InPrivate) differ. ED-NEW-web-sec-res-1: the sandboxed
        // kinds (artifacts, inline widgets, MCP Apps) have an environment of their own, on a folder of their own
        // (WebSandboxEnvironmentTests); every other kind shares this one, unchanged.
        var sandboxed = new[] { WebSurfaceKind.Artifact, WebSurfaceKind.InlineWidget, WebSurfaceKind.McpApp };
        var keys = Enum.GetValues<WebSurfaceKind>().Except(sandboxed).Select(kind => Profiles.For(kind, @"C:\threads\one").EnvironmentKey).Distinct().ToList();
        var key = Assert.Single(keys);
        // DESIGN 6.5 (ED-NEW-web-w13-1): the shared arguments are exactly site isolation and the WebRTC UDP block.
        Assert.Equal((WebRig.UserData, "en-US", (string?)"--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp"), key);
        Assert.Equal(4, Enum.GetValues<WebSurfaceKind>().Except(sandboxed).Count());
    }

    [Fact]
    public void Developer_tools_open_only_in_preview_tabs()
    {
        foreach (var kind in Enum.GetValues<WebSurfaceKind>())
            Assert.Equal(kind == WebSurfaceKind.PreviewTab, Profiles.For(kind, @"C:\threads\one").EnableDevTools);
    }

    [Fact]
    public void An_agent_tab_gets_a_profile_of_its_own_thread_folder()
    {
        var one = Profiles.For(WebSurfaceKind.AgentTab, @"C:\threads\one");
        var again = Profiles.For(WebSurfaceKind.AgentTab, @"C:\THREADS\One\");
        var other = Profiles.For(WebSurfaceKind.AgentTab, @"C:\threads\two");
        Assert.Equal(WebProfileKind.Agent, one.Kind);
        Assert.False(one.IsInPrivate);
        Assert.Matches("^agent-[0-9a-f]{16}$", one.ProfileName);
        Assert.Equal(one.ProfileName, again.ProfileName);
        Assert.NotEqual(one.ProfileName, other.ProfileName);
        Assert.True(WebProfiles.IsAgentProfile(one.ProfileName));
        Assert.False(WebProfiles.IsAgentProfile("browse"));
    }

    [Fact]
    public void An_agent_tab_without_a_thread_folder_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => Profiles.For(WebSurfaceKind.AgentTab));
    }

    [Fact]
    public void Profile_names_stay_inside_what_WebView2_accepts()
    {
        // ProfileName: at most 64 characters of letters, digits and #@$()+-_~. and space, not ending in a period or space.
        foreach (var kind in Enum.GetValues<WebSurfaceKind>())
        {
            var name = Profiles.For(kind, @"C:\threads\a very long folder name that keeps going and going and going").ProfileName;
            Assert.InRange(name.Length, 1, 64);
            Assert.Matches(@"^[A-Za-z0-9#@$()+\-_~. ]*[A-Za-z0-9#@$()+\-_~]$", name);
        }
    }

    [Fact]
    public void The_default_user_data_folder_is_under_local_app_data()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nth", "WebView2");
        Assert.Equal(expected, WebProfiles.DefaultUserDataFolder);
    }
}
