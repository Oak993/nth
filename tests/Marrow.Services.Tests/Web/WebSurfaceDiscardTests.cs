using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.3: a tab not shown for 10 minutes is discarded to its snapshot plus URL and restored by URL when shown;
// model-driven views are never discarded. ED-NEW-web-W7-2: a private tab is never discarded (its InPrivate session
// would end with it).
[Trait("Decision", "ED-NEW-web-2")]
[Trait("Feature", "W-7")]
public sealed class WebSurfaceDiscardTests
{
    private static WebSurfaceView HiddenAfterUse(WebRig rig, WebSurfaceKind kind = WebSurfaceKind.VisusTab)
    {
        var view = rig.OpenLive(kind, shown: true);
        WebRig.Fake(view).RaiseNavigationCompleted("https://a.test/page#top");
        view.SetShown(false);
        return view;
    }

    [Fact]
    public void A_tab_hidden_for_ten_minutes_is_discarded_to_its_snapshot_and_address()
    {
        using var rig = new WebRig();
        var view = HiddenAfterUse(rig);
        var fake = WebRig.Fake(view);
        rig.Surface.Cache.Put(view, fake.CannedFrame()!);

        rig.Time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        rig.Surface.Tick();
        Assert.Equal(WebLiveness.Live, view.Liveness);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Surface.Tick();

        Assert.Equal(WebLiveness.Discarded, view.Liveness);
        Assert.True(fake.IsDisposed);
        Assert.Null(view.Engine);
        Assert.NotNull(view.LastSnapshot);
        Assert.Equal("https://a.test/page#top", view.Url);
    }

    [Fact]
    public void Showing_a_discarded_tab_creates_a_new_view_and_restores_its_address()
    {
        using var rig = new WebRig();
        var view = HiddenAfterUse(rig);
        rig.Time.Advance(TimeSpan.FromMinutes(11));
        rig.Surface.Tick();
        Assert.Equal(WebLiveness.Discarded, view.Liveness);

        view.SetShown(true);

        Assert.Equal(WebLiveness.Pending, view.Liveness);
        var fresh = WebRig.Fake(view);
        Assert.Equal(2, fresh.Number);
        view.AttachNative(3, 4); // the host made a new platform view
        Assert.Equal(WebLiveness.Live, view.Liveness);
        Assert.Contains("Navigate https://a.test/page#top", fresh.Log);
    }

    [Fact]
    public void A_private_tab_is_never_discarded()
    {
        using var rig = new WebRig();
        var view = HiddenAfterUse(rig, WebSurfaceKind.VisusPrivateTab);
        rig.Time.Advance(TimeSpan.FromHours(2));
        rig.Surface.Tick();
        Assert.Equal(WebLiveness.Live, view.Liveness);
        Assert.False(WebRig.Fake(view).IsDisposed);
    }

    [Fact]
    public void A_tab_a_model_step_drives_is_never_discarded()
    {
        using var rig = new WebRig();
        var view = HiddenAfterUse(rig);
        using var step = view.BeginModelStep();
        rig.Time.Advance(TimeSpan.FromMinutes(30));
        rig.Surface.Tick();
        Assert.Equal(WebLiveness.Live, view.Liveness);
    }

    [Fact]
    public void A_shown_tab_is_never_discarded()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        rig.Time.Advance(TimeSpan.FromHours(1));
        rig.Surface.Tick();
        Assert.Equal(WebLiveness.Live, view.Liveness);
    }

    [Fact]
    public void Closing_a_view_disposes_it_and_forgets_its_frames()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive();
        var fake = WebRig.Fake(view);
        rig.Surface.Cache.Put(view, fake.CannedFrame()!);

        rig.Surface.Close(view);

        Assert.Equal(WebLiveness.Closed, view.Liveness);
        Assert.True(fake.IsDisposed);
        Assert.Null(rig.Surface.Cache.Newest(view));
        Assert.Empty(rig.Surface.Views);
    }

    [Fact]
    public void A_platform_view_that_arrives_after_a_discard_is_released_not_used()
    {
        using var rig = new WebRig();
        var view = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");
        rig.Surface.Close(view);

        view.AttachNative(7, 8);

        Assert.Equal([7, 8], rig.Engine.ReleasedNative);
    }
}
