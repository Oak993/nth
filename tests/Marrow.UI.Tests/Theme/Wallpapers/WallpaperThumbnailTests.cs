using Avalonia;
using Avalonia.Controls;
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
    private sealed class Client(int rank) : IWallpaperThumbnailClient
    {
        public bool InView = true, Animated = true, Reduced, Live;
        public double Moved;
        public int Admissions;
        public bool WantsAdmission => !Live && InView;
        public void Admit() { Live = true; Admissions++; }
        public bool Animating => Live && InView && Animated && !Reduced;
        public bool Easing => false;
        public int Rank { get; set; } = rank;
        public void Advance(double seconds) => Moved += seconds;
    }

    private static void Frames(ManualFrameSource frames, int count) { for (var i = 0; i < count; i++) frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); }

    [Fact]
    public void Offscreen_and_reduced_thumbnails_do_not_tick_and_the_ticker_sleeps_when_nothing_moves()
    {
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        var ticker = new WallpaperThumbnailTicker();
        var seen = new Client(0); var below = new Client(1) { InView = false }; var still = new Client(2) { Animated = false };
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
        var clients = Enumerable.Range(0, WallpaperThumbnailTicker.MaxAnimating + 4).Select(i => new Client(i)).ToList();
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
        var ticker = new WallpaperThumbnailTicker(); var c = new Client(0) { Live = true }; ticker.Add(c);
        frames.Advance(TimeSpan.FromSeconds(1.0 / 144));
        var steps = 0; var last = c.Moved;
        for (var i = 0; i < 144; i++) { frames.Advance(TimeSpan.FromSeconds(1.0 / 144)); if (c.Moved != last) { steps++; last = c.Moved; } }
        Assert.InRange(steps, 45, 75); Assert.InRange(c.Moved, 0.95, 1.05);
    }
}

[Collection("Headless")]
[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-2")]
public sealed class WallpaperThumbnailViewTests(HeadlessSession headless)
{
    [Fact]
    public void A_real_thumbnail_ticks_only_in_view_and_freezes_under_Reduce_Motion()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
            var ticker = new WallpaperThumbnailTicker();
            var seen = new WallpaperThumbnail(WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis"), new(WallpaperKind.Animated, "borealis"), ticker) { Width = 64, Height = 40 };
            var below = new WallpaperThumbnail(WallpaperCatalogue.Get(WallpaperKind.Animated, "molten"), new(WallpaperKind.Animated, "molten"), ticker) { Width = 64, Height = 40, Order = 1 };
            Canvas.SetTop(below, 4000);
            var host = new Canvas { Children = { seen, below } };
            var window = new Window { Width = 200, Height = 120, Content = host };
            try
            {
                window.Show(); for (var i = 0; i < 3; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(2, ticker.Count); Assert.True(seen.InView); Assert.False(below.InView);
                for (var i = 0; i < 30; i++) { frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
                Assert.True(seen.Live); Assert.True(seen.Ticked > 0.3); Assert.False(below.Live); Assert.Equal(0, below.Ticked);
                EnvironmentValues.SetAccessibilityReduceMotion(host, true); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var at = seen.Ticked;
                for (var i = 0; i < 30; i++) { frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
                Assert.Equal(at, seen.Ticked); Assert.Equal(0, below.Ticked); Assert.False(ticker.Running);
                window.Content = null; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, ticker.Count); Assert.False(seen.Live);
            }
            finally { window.Close(); }
        });
    }
}
