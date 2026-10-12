using Avalonia;
using Avalonia.Controls;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

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
