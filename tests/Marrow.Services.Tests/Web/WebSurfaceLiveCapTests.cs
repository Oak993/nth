using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.3: at most four live web views across every window; the least recently used beyond that are suspended and
// shown as snapshots; a view a model step drives is never suspended. DESIGN 2.5: a shown view's snapshot is on screen
// before the live view hides.
[Trait("Decision", "ED-NEW-web-w7-2")]
[Trait("Feature", "W-7")]
public sealed class WebSurfaceLiveCapTests
{
    private static List<WebSurfaceView> OpenFour(WebRig rig)
    {
        var views = new List<WebSurfaceView>();
        for (var i = 0; i < 4; i++)
        {
            views.Add(rig.OpenLive(window: i % 2 == 0 ? "w1" : "w2", url: $"https://a.test/{i}"));
            rig.Time.Advance(TimeSpan.FromSeconds(1));
        }

        return views;
    }

    [Fact]
    public void The_fifth_live_view_suspends_the_least_recently_used_across_windows()
    {
        using var rig = new WebRig();
        var views = OpenFour(rig);
        using (views[0].BeginModelStep()) { } // the first view was used most recently now
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        var fifth = rig.OpenLive(window: "w3");

        Assert.Equal(4, rig.Surface.LiveCount);
        Assert.Equal(WebLiveness.Suspended, views[1].Liveness);
        Assert.All(new[] { views[0], views[2], views[3], fifth }, v => Assert.Equal(WebLiveness.Live, v.Liveness));
        // Suspending needs the view hidden and at low memory: the step before Suspend is Memory Low, and the last
        // visibility call before it hid the view (a view never shown was hidden when it opened).
        var log = WebRig.Fake(views[1]).Log;
        var suspend = log.IndexOf("Suspend");
        Assert.True(suspend > 0 && log.LastIndexOf("Suspend") == suspend, string.Join(", ", log));
        Assert.Equal("Memory Low", log[suspend - 1]);
        Assert.Equal("SetVisible False", log.Take(suspend).Last(e => e.StartsWith("SetVisible", StringComparison.Ordinal)));
        Assert.True(WebRig.Fake(views[1]).IsSuspended);
    }

    [Fact]
    public void A_view_a_model_step_drives_is_never_suspended()
    {
        using var rig = new WebRig();
        var views = OpenFour(rig);
        using var step = views[0].BeginModelStep();
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        // views[0] is not the least recent any more either way; make it so, then check it is still skipped.
        views[1].SetShown(true);
        views[2].SetShown(true);
        views[3].SetShown(true);
        foreach (var view in views.Skip(1)) view.SetLiveVisible(false); // hosts already showing snapshots for them

        rig.OpenLive(window: "w2");

        Assert.Equal(WebLiveness.Live, views[0].Liveness);
        Assert.DoesNotContain("Suspend", WebRig.Fake(views[0]).Log);
        Assert.Equal(4, rig.Surface.LiveCount);
    }

    [Fact]
    public void A_shown_view_is_hidden_by_its_host_before_it_is_suspended()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 3; i++) rig.OpenLive(shown: true);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        var fake = WebRig.Fake(first);

        rig.OpenLive(shown: true);

        // A current frame first, then the host is asked (Suspended) and the live view is still visible.
        Assert.Equal(WebLiveness.Suspended, first.Liveness);
        Assert.Contains("Capture", fake.Log);
        Assert.NotNull(first.LastSnapshot);
        Assert.True(fake.IsVisible);
        Assert.DoesNotContain("Suspend", fake.Log);

        first.SetLiveVisible(false); // the host: the snapshot is on screen, the live view hides

        Assert.True(fake.IsSuspended);
        var log = fake.Log;
        Assert.True(log.IndexOf("Capture") < log.IndexOf("SetVisible False") && log.IndexOf("SetVisible False") < log.IndexOf("Suspend"), string.Join(", ", log));
    }

    [Fact]
    public void Without_a_host_answer_a_shown_view_hides_itself_after_the_hide_wait()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 4; i++) rig.OpenLive(shown: true);
        var fake = WebRig.Fake(first);
        Assert.DoesNotContain("Suspend", fake.Log);

        rig.Time.Advance(rig.Surface.Options.HideWait - TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain("Suspend", fake.Log);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(fake.IsSuspended);
        Assert.False(first.IsLiveVisible);
    }

    [Fact]
    public void A_view_that_cannot_be_suspended_stays_live_and_the_next_one_is_suspended()
    {
        using var rig = new WebRig();
        var views = OpenFour(rig);
        WebRig.Fake(views[0]).SuspendSucceeds = false;

        rig.OpenLive();

        Assert.Equal(WebLiveness.Live, views[0].Liveness);
        Assert.Equal(WebLiveness.Suspended, views[1].Liveness);
        Assert.Equal(4, rig.Surface.LiveCount);
    }

    [Fact]
    public void Showing_a_suspended_view_resumes_it_and_suspends_the_next_least_recent()
    {
        using var rig = new WebRig();
        var views = OpenFour(rig);
        rig.OpenLive();
        Assert.Equal(WebLiveness.Suspended, views[0].Liveness);
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        views[0].SetShown(true);

        Assert.Equal(WebLiveness.Live, views[0].Liveness);
        Assert.Contains("Resume", WebRig.Fake(views[0]).Log);
        Assert.Equal(WebMemoryLevel.Normal, WebRig.Fake(views[0]).MemoryLevel);
        Assert.Equal(WebLiveness.Suspended, views[1].Liveness);
        Assert.Equal(4, rig.Surface.LiveCount);
    }

    [Fact]
    public void The_cap_is_four_and_the_setting_keeps_it_between_two_and_eight()
    {
        var options = new WebSurfaceOptions();
        Assert.Equal(4, options.MaxLiveViews);
        options.MaxLiveViews = 1;
        Assert.Equal(2, options.MaxLiveViews);
        options.MaxLiveViews = 20;
        Assert.Equal(8, options.MaxLiveViews);
        options.MaxLiveViews = 3;
        Assert.Equal(3, options.MaxLiveViews);
    }

    [Fact]
    public void Eight_shown_views_stay_live_and_the_ninth_hidden_view_is_suspended()
    {
        using var rig = new WebRig();
        rig.Surface.Options.MaxLiveViews = 8;
        var shown = new List<WebSurfaceView>();
        for (var i = 0; i < 8; i++)
        {
            shown.Add(rig.OpenLive(shown: true));
            rig.Time.Advance(TimeSpan.FromSeconds(1));
        }
        var hidden = rig.OpenLive();
        Assert.Equal(WebLiveness.Suspended, hidden.Liveness);
        Assert.All(shown, v => Assert.Equal(WebLiveness.Live, v.Liveness));
        Assert.All(shown, v => Assert.DoesNotContain("Suspend", WebRig.Fake(v).Log));
    }

    [Fact]
    public void Adding_a_hidden_view_with_nine_shown_views_does_not_freeze_an_additional_shown_view()
    {
        using var rig = new WebRig();
        rig.Surface.Options.MaxLiveViews = 8;
        var shown = new List<WebSurfaceView>();
        for (var i = 0; i < 9; i++)
        {
            shown.Add(rig.OpenLive(shown: true));
            foreach (var view in shown.Where(v => v.Liveness == WebLiveness.Suspended)) view.SetLiveVisible(false);
        }
        var before = shown.Select(v => v.Liveness).ToArray();
        var suspends = shown.Select(v => WebRig.Fake(v).Log.Count(l => l == "Suspend")).ToArray();
        var hidden = rig.OpenLive();
        Assert.Equal(WebLiveness.Suspended, hidden.Liveness);
        Assert.Equal(before, shown.Select(v => v.Liveness));
        Assert.Equal(suspends, shown.Select(v => WebRig.Fake(v).Log.Count(l => l == "Suspend")));
        Assert.Equal(8, rig.Surface.LiveCount);
    }

    [Fact]
    public void Content_arriving_during_capture_cancels_that_suspension_attempt()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        var engine = WebRig.Fake(first);
        engine.HeldCapture = new TaskCompletionSource<WebSnapshot?>();
        for (var i = 0; i < 4; i++) rig.OpenLive(shown: true);
        first.Navigate("https://a.test/new");
        engine.HeldCapture.SetResult(engine.CannedFrame());
        Assert.Equal(WebLiveness.Live, first.Liveness);
        Assert.DoesNotContain("Suspend", engine.Log);
    }

    [Fact]
    public void Content_arriving_during_engine_suspend_resumes_again_after_the_suspend_finishes()
    {
        using var rig = new WebRig();
        var first = rig.OpenLive(shown: true);
        var engine = WebRig.Fake(first);
        engine.HeldSuspend = new TaskCompletionSource<bool>();
        for (var i = 0; i < 4; i++) rig.OpenLive(shown: true);
        first.SetLiveVisible(false);
        Assert.Contains("Suspend", engine.Log);
        first.Navigate("https://a.test/new");
        engine.HeldSuspend.SetResult(true);
        Assert.Equal(WebLiveness.Live, first.Liveness);
        Assert.False(engine.IsSuspended);
        Assert.Contains("Navigate https://a.test/new", engine.Log);
    }

    [Fact]
    public void Four_shown_views_are_not_frozen_when_the_only_hidden_view_cannot_suspend()
    {
        using var rig = new WebRig();
        var shown = Enumerable.Range(0, 4).Select(_ => rig.OpenLive(shown: true)).ToList();
        var hidden = rig.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/audio");
        WebRig.Fake(hidden).SuspendSucceeds = false;
        hidden.AttachNative(1, 2);
        Assert.All(shown, view => Assert.Equal(WebLiveness.Live, view.Liveness));
        Assert.Equal(WebLiveness.Live, hidden.Liveness);
    }

    [Fact]
    public void Hidden_views_run_at_low_memory_and_stop_painting_unless_a_model_step_drives_them()
    {
        using var rig = new WebRig();
        var plain = rig.OpenLive(shown: true);
        var driven = rig.OpenLive(shown: true);
        Assert.Equal(WebMemoryLevel.Normal, WebRig.Fake(plain).MemoryLevel);
        using var step = driven.BeginModelStep();

        plain.SetShown(false);
        driven.SetShown(false);

        Assert.Equal(WebMemoryLevel.Low, WebRig.Fake(plain).MemoryLevel);
        Assert.False(WebRig.Fake(plain).IsVisible);
        Assert.Equal(WebMemoryLevel.Low, WebRig.Fake(driven).MemoryLevel);
        Assert.True(WebRig.Fake(driven).IsVisible);
    }
}
