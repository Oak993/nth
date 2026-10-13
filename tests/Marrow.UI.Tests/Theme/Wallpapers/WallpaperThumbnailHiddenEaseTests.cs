using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

[Collection("Headless")]
public sealed class WallpaperThumbnailHiddenEaseTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-wltwo-1")]
    public void A_thumbnail_in_an_unseen_window_holds_while_another_window_stays_seen(bool animated)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            WindowPresence.TestFactory = w => new WindowPresence(w, true);
            var ticker = new WallpaperThumbnailTicker();
            var kind = animated ? WallpaperKind.Animated : WallpaperKind.Static;
            var id = animated ? "borealis" : "riptide";
            var view = new WallpaperThumbnail(WallpaperCatalogue.Get(kind, id), new(kind, id), ticker) { Width = 64, Height = 40 };
            var window = new Window { Width = 200, Height = 120, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
            var other = new Window { Width = 100, Height = 100 };
            void Run(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    var step = TimeSpan.FromSeconds(1.0 / 60);
                    runtime.Advance(step); frames.Advance(step); Dispatcher.UIThread.RunJobs();
                }
            }
            try
            {
                other.Show(); window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Run(40);
                Assert.True(view.Live);
                window.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
                var presence = WindowPresence.For(view)!;
                presence.SetSeen(false);
                Assert.True(WindowPresence.For(other)!.Seen);
                Assert.True(WindowPresence.AppSeen);
                var held = view.Ticked; Run(120);
                Assert.Equal(held, view.Ticked);
                Assert.False(ticker.Running);
                presence.SetSeen(true); Run(50);
                Assert.True(view.Ticked > held);
                Assert.False(view.Easing);
            }
            finally { window.Close(); other.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null; }
            Assert.Equal(0, ticker.Count);
            Assert.Equal(0, frames.Pending);
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-wltwo-1")]
    public void A_hidden_or_minimised_thumbnail_holds_its_ease_and_resumes_when_shown()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var source = FrameClock.Source.Override(frames);
            var ticker = new WallpaperThumbnailTicker();
            var view = new WallpaperThumbnail(WallpaperCatalogue.Get(WallpaperKind.Static, "riptide"),
                new(WallpaperKind.Static, "riptide"), ticker) { Width = 64, Height = 40 };
            var window = new Window { Width = 200, Height = 120, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
            void Frames(int count)
            {
                for (var i = 0; i < count; i++) { frames.Advance(TimeSpan.FromSeconds(1.0 / 60)); Dispatcher.UIThread.RunJobs(); }
            }
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Frames(40);
                Assert.True(view.Live);
                window.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
                Assert.True(view.Easing);
                view.IsVisible = false; Dispatcher.UIThread.RunJobs();
                var held = view.Ticked;
                Frames(90);
                Assert.Equal(held, view.Ticked);
                Assert.False(ticker.Running);
                view.IsVisible = true; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Frames(10);
                Assert.True(view.Ticked > held);
                Assert.True(view.Easing);
                window.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
                held = view.Ticked; Frames(90);
                Assert.Equal(held, view.Ticked);
                Assert.False(ticker.Running);
                window.WindowState = WindowState.Normal; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Frames(50);
                Assert.True(view.Ticked > held);
                Assert.False(view.Easing);
                Assert.False(ticker.Running);
            }
            finally { window.Close(); }
            Assert.Equal(0, ticker.Count);
        });
    }
}
