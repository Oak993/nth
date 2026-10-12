using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.1 runtime check (GetAvailableCoreWebView2BrowserVersionString against the SDK's minimum) and DESIGN 2.7: a
// missing or too old runtime gives the install or update card; artifacts and widgets keep their static fallback.
[Trait("Decision", "ED-NEW-web-w7-6")]
[Trait("Feature", "W-7")]
public sealed class WebRuntimeTests
{
    [Fact]
    public void A_missing_runtime_gives_a_Visus_pane_the_install_card_and_no_engine_view()
    {
        using var rig = new WebRig(installed: null);
        Assert.Equal(WebRuntimeState.Missing, rig.Surface.Runtime.State);

        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");

        Assert.Null(view.Engine);
        Assert.Empty(rig.Engine.Views);
        Assert.Equal(WebLiveness.Closed, view.Liveness);
        var card = Assert.IsType<WebFailureCard>(view.Failure);
        Assert.Equal(WebFailureKind.RuntimeMissing, card.Kind);
        Assert.Equal("Visus needs the WebView2 runtime.", card.Title);
        Assert.Equal("It is a 2 MB download from Microsoft. nth installs it when you say so.", card.Body);
        Assert.Equal([WebFailureAction.InstallRuntime], card.Buttons.Select(b => b.Action));
        Assert.Equal(["Install"], card.Buttons.Select(b => b.Title));
        Assert.True(card.ShowsCard);
        Assert.False(card.Dimmed);
    }

    [Fact]
    public void Sandboxed_surfaces_keep_their_static_fallback_without_a_card()
    {
        using var rig = new WebRig(installed: null);
        foreach (var kind in new[] { WebSurfaceKind.Artifact, WebSurfaceKind.InlineWidget, WebSurfaceKind.McpApp })
        {
            var view = rig.Surface.Open(kind, "w1");
            Assert.Equal(WebFailureKind.RuntimeMissing, view.Failure?.Kind);
            Assert.False(view.Failure!.ShowsCard);
        }
    }

    [Fact]
    public void A_runtime_older_than_the_minimum_gives_the_update_card()
    {
        using var rig = new WebRig(installed: "100.0.1185.36");
        var view = rig.Surface.Open(WebSurfaceKind.PreviewTab, "w1");
        Assert.Equal(WebRuntimeState.TooOld, rig.Surface.Runtime.State);
        Assert.Equal("100.0.1185.36", rig.Surface.Runtime.Version);
        Assert.Equal(WebFailureKind.RuntimeTooOld, view.Failure?.Kind);
        Assert.Equal("Visus needs a newer WebView2 runtime.", view.Failure!.Title);
        Assert.Equal([WebFailureAction.UpdateRuntime], view.Failure.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void Install_and_update_go_to_the_runtime_flow()
    {
        using var rig = new WebRig(installed: null);
        var asked = new List<WebFailureAction>();
        rig.Surface.RuntimeActionRequested += asked.Add;
        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal([WebFailureAction.InstallRuntime], asked);
    }

    [Fact]
    public void Once_the_runtime_is_installed_waiting_views_get_their_engine()
    {
        using var rig = new WebRig(installed: null);
        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");
        rig.Engine.InstalledVersion = "141.0.3537.71";

        Assert.Equal(WebRuntimeState.Ready, rig.Surface.RefreshRuntime().State);

        Assert.Null(view.Failure);
        Assert.Equal(WebLiveness.Pending, view.Liveness);
        view.AttachNative(1, 2);
        Assert.Contains("Navigate https://a.test/", WebRig.Fake(view).Log);
    }

    [Fact]
    public void A_platform_view_that_is_not_WebView2_counts_as_a_missing_runtime()
    {
        using var rig = new WebRig();
        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");
        var fake = WebRig.Fake(view);

        view.AttachNative(0, 9);

        Assert.Equal([9], rig.Engine.ReleasedNative);
        Assert.True(fake.IsDisposed);
        Assert.Equal(WebLiveness.Closed, view.Liveness);
        Assert.Equal(WebFailureKind.RuntimeMissing, view.Failure?.Kind);
        Assert.Equal(WebRuntimeState.Missing, rig.Surface.Runtime.State);
    }

    [Theory]
    [InlineData("141.0.3537.71", "112.0.1722.39", 1)]
    [InlineData("112.0.1722.39", "112.0.1722.39", 0)]
    [InlineData("112.0.1722.38", "112.0.1722.39", -1)]
    [InlineData("112.0.1722.39 dev", "112.0.1722.39", 0)]
    [InlineData("112.0", "112.0.0.0", 0)]
    [InlineData("112.0.1722.100", "112.0.1722.39", 1)]
    [InlineData("x.0.0.0", "1.0.0.0", -1)]
    public void Versions_compare_part_by_part_as_numbers(string left, string right, int sign)
    {
        Assert.Equal(sign, Math.Sign(WebRuntimeStatus.CompareVersions(left, right)));
    }

    [Fact]
    public void The_fake_reports_the_real_engines_minimum()
    {
        Assert.Equal(WebView2Engine.MinimumVersion, new FakeWebEngine().MinimumRuntimeVersion);
        Assert.Equal(WebRuntimeState.Ready, WebRuntimeStatus.Evaluate(new FakeWebEngine { InstalledVersion = WebView2Engine.MinimumVersion }).State);
        Assert.Equal(WebRuntimeState.Missing, WebRuntimeStatus.Evaluate(new FakeWebEngine { InstalledVersion = "  " }).State);
    }
}
