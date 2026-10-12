using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

// WL-2 (ED-NEW-wltwo-1): the Settings library's live thumbnails share one ticker; only those in view, in a shown window and
// with Reduce Motion off move, at most MaxAnimating at once (the chosen one first), admitted one at a time.
[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-2")]
public sealed class WallpaperThumbnailTickerTests
{
    private static void Frames(ManualFrameSource frames, int count) { for (var i = 0; i < count; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); }

    [Fact]
    public void Offscreen_and_reduced_thumbnails_do_not_tick_and_the_ticker_sleeps_when_nothing_moves()
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        var ticker = new WallpaperThumbnailTicker();
        var seen = new FakeWallpaperThumbnailClient(0); var below = new FakeWallpaperThumbnailClient(1) { InView = false }; var still = new FakeWallpaperThumbnailClient(2) { Animated = false };
        ticker.Add(seen); ticker.Add(below); ticker.Add(still);
        Frames(frames, 30);
        Assert.True(seen.Live); Assert.True(still.Live); Assert.False(below.Live); // admitted only once in view
        Assert.InRange(seen.Moved, 0.3, 0.5); Assert.Equal(0, below.Moved); Assert.Equal(0, still.Moved); // a still wallpaper is drawn once, never ticked
        seen.Reduced = true; var at = seen.Moved;
        Frames(frames, 30);
        Assert.Equal(at, seen.Moved); Assert.False(ticker.Running); Assert.Equal(0, frames.Pending); // Reduce Motion: frozen, and no frame is asked for
        below.InView = true; ticker.Wake(); Frames(frames, 30);
        Assert.True(below.Live); Assert.InRange(below.Moved, 0.3, 0.5); Assert.Equal(at, seen.Moved);
        ticker.Remove(seen); ticker.Remove(below); ticker.Remove(still);
        Assert.False(ticker.Running); Assert.Equal(0, frames.Pending);
    }

    [Fact]
    public void Admissions_are_spread_out_and_the_animating_ones_are_capped_with_the_chosen_first()
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        var ticker = new WallpaperThumbnailTicker();
        var clients = Enumerable.Range(0, WallpaperThumbnailTicker.MaxAnimating + 4).Select(i => new FakeWallpaperThumbnailClient(i)).ToList();
        foreach (var c in clients) ticker.Add(c);
        Frames(frames, 1);
        Assert.Equal(1, clients.Count(c => c.Live)); Assert.True(clients[0].Live); // one shader compile per admission, top first
        Frames(frames, 2);
        Assert.Equal(1, clients.Count(c => c.Live)); // 50 ms apart
        Frames(frames, 120);
        Assert.All(clients, c => Assert.Equal(1, c.Admissions));
        clients[^1].Rank = -1; // the chosen one
        foreach (var c in clients) c.Moved = 0;
        Frames(frames, 10);
        Assert.Equal(WallpaperThumbnailTicker.MaxAnimating, clients.Count(c => c.Moved > 0)); Assert.Equal(WallpaperThumbnailTicker.MaxAnimating, ticker.LastAnimated);
        Assert.True(clients[^1].Moved > 0); Assert.Equal(0, clients[^2].Moved);
    }

    [Fact]
    public void A_fast_display_still_moves_the_thumbnails_at_60_Hz()
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        var ticker = new WallpaperThumbnailTicker(); var c = new FakeWallpaperThumbnailClient(0) { Live = true }; ticker.Add(c);
        frames.Advance(TimeSpan.FromSeconds(1.0 / 144));
        var steps = 0; var last = c.Moved;
        for (var i = 0; i < 144; i++) { frames.Advance(TimeSpan.FromSeconds(1.0 / 144)); if (c.Moved != last) { steps++; last = c.Moved; } }
        Assert.InRange(steps, 45, 75); Assert.InRange(c.Moved, 0.95, 1.05);
    }
}
