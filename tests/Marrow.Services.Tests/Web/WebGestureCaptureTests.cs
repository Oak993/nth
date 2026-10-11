using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.5: every gesture takes a one-shot capture of each affected live view, and the spring starts when it returns.
// Over the capture budget (planning value 30 ms) the intent cache's frame starts the spring and the fresh capture
// cross-fades in when it lands. Exit waits for two page frames, at most 120 ms.
[Trait("Decision", "ED-NEW-web-3")]
[Trait("Feature", "W-7")]
public sealed class WebGestureCaptureTests
{
    private static WebSnapshot Frame(WebRig rig, uint color) =>
        new(FakeWebEngine.OnePixelPng, 1, 1, 1, rig.Time.GetUtcNow(), color, WebSnapshotSource.Fake);

    [Fact]
    public void A_capture_inside_the_budget_is_the_gesture_frame()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.HeldCapture = new TaskCompletionSource<WebSnapshot?>();
        var fresh = Frame(rig, 0xFF112233);

        var gesture = view.CaptureForGestureAsync(CancellationToken.None);
        rig.Time.Advance(TimeSpan.FromMilliseconds(29));
        Assert.False(gesture.IsCompleted);
        fake.HeldCapture.SetResult(fresh);

        Assert.True(gesture.IsCompletedSuccessfully);
        Assert.Same(fresh, WebRig.Done(gesture).Snapshot);
        Assert.Null(WebRig.Done(gesture).Fresher);
        Assert.True(WebRig.Done(gesture).IsFresh);
        Assert.Same(fresh, view.LastSnapshot);
    }

    [Fact]
    public void Over_the_budget_the_cached_frame_starts_the_gesture_and_the_fresh_one_follows()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        var cached = Frame(rig, 0xFF000000);
        rig.Surface.Cache.Put(view, cached);
        rig.Time.Advance(TimeSpan.FromMilliseconds(400));
        fake.HeldCapture = new TaskCompletionSource<WebSnapshot?>();

        var gesture = view.CaptureForGestureAsync(CancellationToken.None);
        rig.Time.Advance(TimeSpan.FromMilliseconds(30));

        Assert.True(gesture.IsCompletedSuccessfully);
        Assert.Same(cached, WebRig.Done(gesture).Snapshot);
        var fresher = Assert.IsAssignableFrom<Task<WebSnapshot?>>(WebRig.Done(gesture).Fresher);
        Assert.False(fresher.IsCompleted);
        var fresh = Frame(rig, 0xFFFFFFFF);
        fake.HeldCapture.SetResult(fresh);
        Assert.Same(fresh, WebRig.Done(fresher));
        Assert.Same(fresh, view.LastSnapshot);
    }

    [Fact]
    public void Over_the_budget_with_no_cached_frame_the_gesture_waits_for_the_capture()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        fake.HeldCapture = new TaskCompletionSource<WebSnapshot?>();

        var gesture = view.CaptureForGestureAsync(CancellationToken.None);
        rig.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.False(gesture.IsCompleted);
        var fresh = Frame(rig, 0xFF445566);
        fake.HeldCapture.SetResult(fresh);

        Assert.Same(fresh, WebRig.Done(gesture).Snapshot);
        Assert.Null(WebRig.Done(gesture).Fresher);
    }

    [Fact]
    public void A_view_that_is_not_live_gives_its_last_frame_without_capturing()
    {
        using var rig = new WebRig();
        var views = Enumerable.Range(0, 5).Select(_ => rig.OpenLive()).ToList();
        var suspended = views[0];
        Assert.Equal(WebLiveness.Suspended, suspended.Liveness);
        var last = Frame(rig, 0xFF010203);
        rig.Surface.Cache.Put(suspended, last);
        var captures = WebRig.Fake(suspended).Captures;

        var gesture = suspended.CaptureForGestureAsync(CancellationToken.None);

        Assert.Same(last, WebRig.Done(gesture).Snapshot);
        Assert.Equal(captures, WebRig.Fake(suspended).Captures);
    }

    [Fact]
    public void Settling_waits_for_two_page_frames_at_most_120_ms()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);

        Assert.True(WebRig.Done(view.WaitForFramesAsync(2, TimeSpan.FromMilliseconds(120), CancellationToken.None)));
        Assert.Contains("Frames 2", fake.Log);

        fake.AnswersFrames = false;
        var slow = view.WaitForFramesAsync(2, TimeSpan.FromMilliseconds(120), CancellationToken.None);
        rig.Time.Advance(TimeSpan.FromMilliseconds(119));
        Assert.False(slow.IsCompleted);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(slow.IsCompletedSuccessfully);
        Assert.False(WebRig.Done(slow));

        var answered = view.WaitForFramesAsync(2, TimeSpan.FromMilliseconds(120), CancellationToken.None);
        fake.AnswerFrames();
        Assert.True(WebRig.Done(answered));
    }

    [Fact]
    public void Intent_refreshes_the_cache_only_while_it_is_on()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);

        view.NoteIntent();
        Assert.Equal(0, fake.Captures);

        rig.Surface.Cache.Enabled = true;
        view.NoteIntent();
        Assert.Equal(1, fake.Captures);
        Assert.NotNull(view.LastSnapshot);
    }

    [Fact]
    public void With_the_cache_on_a_visible_view_never_keeps_a_frame_older_than_a_second()
    {
        using var rig = new WebRig();
        rig.Surface.Cache.Enabled = true;
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);
        rig.Surface.Tick();
        Assert.Equal(1, fake.Captures);

        rig.Time.Advance(TimeSpan.FromMilliseconds(900));
        rig.Surface.Tick();
        Assert.Equal(1, fake.Captures);

        rig.Time.Advance(TimeSpan.FromMilliseconds(200));
        rig.Surface.Tick();
        Assert.Equal(2, fake.Captures);

        // Navigation finished: a refresh 500 ms later, even in a window that is not focused.
        rig.Surface.SetWindowFocused("w1", false);
        fake.RaiseNavigationCompleted("https://a.test/next");
        rig.Time.Advance(TimeSpan.FromMilliseconds(499));
        rig.Surface.Tick();
        Assert.Equal(2, fake.Captures);
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        rig.Surface.Tick();
        Assert.Equal(3, fake.Captures);
    }
}
