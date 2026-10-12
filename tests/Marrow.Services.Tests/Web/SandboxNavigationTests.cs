using Marrow.Core.Models;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5 "Channels CSP does not close": navigation and new windows; 8.1). A
// sandboxed view never leaves its own site, top level or frame, whoever starts it; a user's click on a web link is offered
// to the link setting as "Open link from the artifact", and a script's attempt is cancelled silently.
[Trait("Decision", "ED-NEW-web-w13-1")]
public sealed class SandboxNavigationTests
{
    [Fact]
    public void Leaving_the_site_is_cancelled_and_only_a_users_web_link_is_offered()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, "<a href=\"https://example.com\">x</a>"));
        var offered = new List<string>();
        mount.LinkRequested += offered.Add;

        Assert.True(view.RaiseNavigationStarting("https://example.com/docs", userInitiated: true).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://tracker.example/?d=secret", userInitiated: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("https://evil.example/frame", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting(ArtifactRuntime.Address(ArtifactRuntimeRig.OtherChat, "weather"), userInitiated: true).Cancel);
        Assert.True(view.RaiseNavigationStarting("file:///C:/Users/me/secret.txt", userInitiated: true).Cancel);
        Assert.True(view.RaiseNavigationStarting("javascript:alert(1)", userInitiated: true).Cancel);

        Assert.False(view.RaiseNavigationStarting(mount.Address + "page2.html", userInitiated: true).Cancel);
        Assert.False(view.RaiseNavigationStarting("about:blank", userInitiated: false, mainFrame: false).Cancel);
        Assert.False(view.RaiseNavigationStarting("blob:" + mount.Origin + "/0b8d9c1e", userInitiated: false, mainFrame: false).Cancel);
        Assert.True(view.RaiseNavigationStarting("blob:https://evil.example/0b8d9c1e", userInitiated: false).Cancel);

        Assert.Equal(["https://example.com/docs", ArtifactRuntime.Address(ArtifactRuntimeRig.OtherChat, "weather")], offered);
    }

    [Fact]
    public void New_windows_are_always_denied_and_a_users_click_is_offered_as_a_link()
    {
        using var rig = new ArtifactRuntimeRig();
        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, "<a target=_blank href=\"https://example.com\">x</a>"));
        var offered = new List<string>();
        mount.LinkRequested += offered.Add;

        Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(view.RaiseNewWindow("https://example.com/", userInitiated: true)));
        Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(view.RaiseNewWindow("https://popup.example/", userInitiated: false)));
        Assert.Equal(WebNewWindowDecision.Block, WebRig.Decided(view.RaiseNewWindow(mount.Address, userInitiated: true)));
        Assert.Equal(["https://example.com/", mount.Address], offered);
    }

    [Fact]
    public void Only_sandboxed_kinds_take_a_sandbox_and_a_disposed_mount_lets_go()
    {
        using var rig = new ArtifactRuntimeRig();
        var tab = rig.Web.OpenLive(WebSurfaceKind.VisusTab);
        Assert.Throws<InvalidOperationException>(() => rig.Runtime.Mount(tab, ArtifactRuntimeRig.Chat, "weather"));

        var (mount, view) = rig.Mount(rig.Create(ArtifactKind.Html, "<p>x</p>"));
        var offered = new List<string>();
        mount.LinkRequested += offered.Add;
        mount.Dispose();
        ArtifactRuntimeRig.Send(mount, view, new { nth = 1, s = "d", id = 1, type = "theme.get" });
        Assert.Empty(ArtifactRuntimeRig.Posts(view));
        Assert.True(view.RaiseNavigationStarting("https://example.com/", userInitiated: true).Cancel); // the view stays sandboxed
        Assert.Empty(offered);
    }
}
