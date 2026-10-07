// ED-NEW-design-gaps-5: locked settings-rise/SPEC.md:68-75 and locked canvas:107-109.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Window;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

internal static class DesignGapsLockedClose
{
    // Transcribed from the locked source, not calculated from production timing/easing.
    private static readonly double[] Alpha = [.980, .929, .854, .761, .652, .531, .398, .254, .099];

    public static void CompleteAndCheckCancellation(Avalonia.Controls.Window window, WindowManager manager, SettingsRiseRig rig)
    {
        var content = Assert.IsAssignableFrom<Control>(window.Content);
        var start = SwiftClock.Now;
        Assert.True(window.IsVisible);
        Assert.Equal(1, content.Opacity);
        Assert.Equal(RelativePoint.Center, content.RenderTransformOrigin);
        CheckSamples();

        // Re-open at C+150 ms, while the closing frame is still visible.
        manager.ShowSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.Equal(1, content.Opacity);
        Assert.Null(content.RenderTransform);
        Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
        rig.Frame(.200); // the cancelled C+160 deadline must never hide the window
        Assert.True(window.IsVisible);
        Assert.Equal(1, content.Opacity);
        Assert.Null(content.RenderTransform);
        Assert.DoesNotContain("hidden", rig.Events);

        // Observe the final pose on hide, before the close driver releases its overrides.
        double? hiddenAlpha = null, hiddenScaleX = null, hiddenScaleY = null;
        double? hiddenAt = null;
        void OnVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty || window.IsVisible) return;
            hiddenAt = (SwiftClock.Now - start).TotalSeconds;
            hiddenAlpha = content.Opacity;
            var scale = Assert.IsType<ScaleTransform>(content.RenderTransform);
            hiddenScaleX = scale.ScaleX;
            hiddenScaleY = scale.ScaleY;
        }
        window.PropertyChanged += OnVisibility;
        try
        {
            start = SwiftClock.Now;
            window.Close();
            Dispatcher.UIThread.RunJobs();
            CheckSamples();
            At(.1599999); // one 100 ns manual-clock tick before C+160 ms
            Assert.True(window.IsVisible);
            Assert.Null(hiddenAt);
            At(.160);
            Assert.False(window.IsVisible);
            Assert.Equal(.160, hiddenAt);
            Assert.Equal(0, hiddenAlpha);
            Assert.Equal(.985, hiddenScaleX);
            Assert.Equal(.985, hiddenScaleY);
            Assert.Equal(1, content.Opacity);
            Assert.Null(content.RenderTransform);
            Assert.Equal(0, rig.PendingFor(manager.SettingsClosing));
        }
        finally { window.PropertyChanged -= OnVisibility; }

        void At(double seconds)
        {
            var target = start + TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            rig.Frame((target - SwiftClock.Now).TotalSeconds);
        }

        void CheckSamples()
        {
            for (var i = 0; i < Alpha.Length; i++)
            {
                At((i + 1) / 60.0);
                Assert.True(window.IsVisible);
                Assert.InRange(content.Opacity, Alpha[i] - .0005, Alpha[i] + .0005);
                var expected = 1 - Ease((SwiftClock.Now - start).TotalSeconds / .160);
                Assert.Equal(expected, content.Opacity, 5);
                var scale = Assert.IsType<ScaleTransform>(content.RenderTransform);
                Assert.Equal(1 - .015 * (1 - expected), scale.ScaleX, 5);
                Assert.Equal(scale.ScaleX, scale.ScaleY);
                Assert.Equal(RelativePoint.Center, content.RenderTransformOrigin);
            }
        }
    }

    // Independent bisection of cubic-bezier(.4,0,1,1), rather than the port's easing helper.
    private static double Ease(double x)
    {
        double low = 0, high = 1;
        for (var i = 0; i < 80; i++)
        {
            var u = (low + high) / 2;
            var at = 3 * .4 * u * (1 - u) * (1 - u) + 3 * u * u * (1 - u) + u * u * u;
            if (at < x) low = u; else high = u;
        }
        var v = (low + high) / 2;
        return 3 * v * v * (1 - v) + v * v * v;
    }
}
