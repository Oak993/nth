using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// DESIGN 2.5 intent cache: one recent frame per live view, refreshed on intent and 500 ms after navigation, never older
// than 1 s while the view is visible and the window focused. Off until spike (b) shows the capture budget needs it.
[Trait("Decision", "ED-NEW-web-w7-3")]
[Trait("Feature", "W-7")]
public sealed class SnapshotCacheTests
{
    private static WebSnapshot At(DateTimeOffset at) => new(FakeWebEngine.OnePixelPng, 1, 1, 1, at, null, WebSnapshotSource.Fake);

    [Fact]
    public void The_newest_frame_wins_whatever_order_they_arrive_in()
    {
        var time = new ManualTimeProvider();
        var cache = new SnapshotCache(time);
        var view = new object();
        var newer = At(time.GetUtcNow());
        var older = At(time.GetUtcNow() - TimeSpan.FromSeconds(1));
        cache.Put(view, newer);
        cache.Put(view, older);
        Assert.Same(newer, cache.Newest(view));
        Assert.Null(cache.Newest(new object()));
    }

    [Fact]
    public void A_frame_is_stale_after_one_second()
    {
        var time = new ManualTimeProvider();
        var cache = new SnapshotCache(time);
        var view = new object();
        Assert.True(cache.IsStale(view));
        cache.Put(view, At(time.GetUtcNow()));
        time.Advance(SnapshotCache.MaxAge);
        Assert.False(cache.IsStale(view));
        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(cache.IsStale(view));
    }

    [Fact]
    public void Refreshes_come_from_intent_navigation_and_age_only_while_enabled_and_one_at_a_time()
    {
        var time = new ManualTimeProvider();
        var cache = new SnapshotCache(time);
        var view = new object();
        Assert.False(cache.Enabled);
        Assert.False(cache.ShouldRefresh(view, intent: true, visibleAndFocused: true));

        cache.Enabled = true;
        Assert.True(cache.ShouldRefresh(view, intent: true, visibleAndFocused: false));
        Assert.True(cache.ShouldRefresh(view, intent: false, visibleAndFocused: true)); // nothing cached: stale
        Assert.False(cache.ShouldRefresh(view, intent: false, visibleAndFocused: false));

        cache.BeginRefresh(view);
        Assert.False(cache.ShouldRefresh(view, intent: true, visibleAndFocused: true));
        cache.EndRefresh(view, At(time.GetUtcNow()));
        Assert.False(cache.ShouldRefresh(view, intent: false, visibleAndFocused: true));

        cache.NoteNavigation(view);
        time.Advance(SnapshotCache.AfterNavigation - TimeSpan.FromTicks(1));
        Assert.False(cache.ShouldRefresh(view, intent: false, visibleAndFocused: false));
        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(cache.ShouldRefresh(view, intent: false, visibleAndFocused: false));
    }

    [Fact]
    public void Removing_a_view_forgets_its_frame()
    {
        var time = new ManualTimeProvider();
        var cache = new SnapshotCache(time);
        var view = new object();
        cache.Put(view, At(time.GetUtcNow()));
        cache.Remove(view);
        Assert.Null(cache.Newest(view));
    }
}
