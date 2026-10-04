using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.Testing;
using Marrow.UI.Port.Voice.Glow;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T62 (voice-SPEC V-13.3): the glow draws inside the composer's rounded shape, above the card and under the content, on the render thread, frames only while it is
// active or fading. The headless renderer is software, where the glow is one still unless the test asks for the motion (VoiceGlowMotion).
[Collection("Headless")]
public sealed class VoiceGlowViewTests(HeadlessSession headless)
{
    private const int WindowW = 800, WindowH = 160, Left = 40, Top = 30, ShapeW = 720, ShapeH = 96;
    private static readonly Color Card = Color.FromRgb(0x1c, 0x1c, 0x1c);

    private static (Window Window, VoiceGlowView View) Show(ThemeVariant? theme = null, bool reduce = false, Control? above = null, Color? card = null)
    {
        var view = new VoiceGlowView { Width = ShapeW, Height = ShapeH, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Left, Top, 0, 0) };
        if (reduce) EnvironmentValues.SetAccessibilityReduceMotion(view, true);
        var panel = new Panel { Children = { view } };
        if (above is not null) panel.Children.Add(above);
        var window = new Window { Width = WindowW, Height = WindowH, RequestedThemeVariant = theme ?? ThemeVariant.Dark, Background = new SolidColorBrush(card ?? Card), Content = panel };
        window.Show();
        Tick();
        return (window, view);
    }

    private static void Tick()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(ManualTimeProvider clock, double seconds)
    {
        var frames = (int)Math.Round(seconds * 60);
        for (var i = 0; i < frames; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1.0 / 60));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Tick();
    }

    private static byte[] Pixels(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }

    // pixels in a region that differ from the card by more than `threshold` in any channel (the bitmap is BGRA or RGBA: all three colour channels are compared)
    private static int Lit(byte[] p, int x0, int y0, int x1, int y1, int threshold = 3)
    {
        var count = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var o = (y * WindowW + x) * 4;
                if (Math.Abs(p[o] - 0x1c) > threshold || Math.Abs(p[o + 1] - 0x1c) > threshold || Math.Abs(p[o + 2] - 0x1c) > threshold) count++;
            }
        return count;
    }

    private static long Sum(byte[] p, int x0, int y0, int x1, int y1)
    {
        long sum = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var o = (y * WindowW + x) * 4;
                sum += Math.Max(Math.Abs(p[o] - 0x1c), Math.Max(Math.Abs(p[o + 1] - 0x1c), Math.Abs(p[o + 2] - 0x1c)));
            }
        return sum;
    }

    private static bool IsCard(byte[] p, int x, int y)
    {
        var o = (y * WindowW + x) * 4;
        return p[o] == 0x1c && p[o + 1] == 0x1c && p[o + 2] == 0x1c;
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void An_inactive_glow_draws_nothing_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            Assert.NotNull(view.Handler);
            Run(clock, 1);
            Assert.False(view.Handler!.IsAnimating);
            Assert.Equal(0, Lit(Pixels(window), 0, 0, WindowW, WindowH, 0));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void An_active_glow_lights_the_bottom_of_the_shape_and_nothing_outside_the_rounded_shape() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            view.Level.Value = 0.6;
            view.Active = true;
            Tick();
            Run(clock, 2.5);
            var p = Pixels(window);
            Assert.True(view.Handler!.IsAnimating);
            Assert.True(view.Handler.Frames > 30);
            Assert.True(Lit(p, Left, Top + ShapeH / 2, Left + ShapeW, Top + ShapeH) > 3000, "the bottom half is lit");
            Assert.True(Lit(p, Left, Top, Left + ShapeW, Top + 18) == 0, "the top is not (the glow stays low)");
            Assert.True(Lit(p, 0, 0, WindowW, Top) == 0, "nothing above the shape");
            Assert.True(Lit(p, 0, Top + ShapeH, WindowW, WindowH) == 0, "nothing below it");
            Assert.True(Lit(p, 0, Top, Left, Top + ShapeH) == 0, "nor to its left");
            Assert.True(Lit(p, Left + ShapeW, Top, WindowW, Top + ShapeH) == 0, "nor to its right");
            // the shape's rounded corners (radius 22): the pixels just inside the square corner stay the card
            foreach (var (x, y) in new[] { (Left + 1, Top + ShapeH - 2), (Left + ShapeW - 2, Top + ShapeH - 2), (Left + 2, Top + 2), (Left + ShapeW - 3, Top + 2) })
                Assert.True(IsCard(p, x, y), $"({x}, {y}) is outside the rounded shape");
        }
        finally { window.Close(); }
    });

    // the glow's light over the shape and the row of its highest lit pixel, after three seconds at a constant level
    private static (long Sum, int Top) Measure(double level)
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            view.Level.Value = level;
            view.Active = true;
            Tick();
            Run(clock, 3);
            var p = Pixels(window);
            var top = Enumerable.Range(Top, ShapeH).First(y => Lit(p, Left + 100, y, Left + ShapeW - 100, y + 1, 6) > 0);
            return (Sum(p, Left, Top, Left + ShapeW, Top + ShapeH), top);
        }
        finally { window.Close(); }
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void The_voice_lifts_the_glow() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var quiet = Measure(0);
        var loud = Measure(0.9);
        Assert.True(loud.Sum > quiet.Sum * 2, $"loud {loud.Sum} vs quiet {quiet.Sum}");
        Assert.True(loud.Top < quiet.Top, $"the loud glow reaches higher ({loud.Top} vs {quiet.Top})");
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void Processing_sweeps_the_glow_along_the_bottom_edge() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            view.Level.Value = 0.6;
            view.Active = true;
            Tick();
            Run(clock, 2.5);
            var listening = Pixels(window);
            view.Level.Value = 0;
            view.Processing = true;
            Tick();
            var centroids = new List<double>();
            for (var i = 0; i < 6; i++)
            {
                Run(clock, 0.3);
                var p = Pixels(window);
                double sx = 0, sw = 0;
                for (var y = Top + ShapeH / 2; y < Top + ShapeH; y++)
                    for (var x = Left; x < Left + ShapeW; x++)
                    {
                        var o = (y * WindowW + x) * 4;
                        var w = Math.Max(Math.Abs(p[o] - 0x1c), Math.Max(Math.Abs(p[o + 1] - 0x1c), Math.Abs(p[o + 2] - 0x1c)));
                        sx += x * w;
                        sw += w;
                    }
                centroids.Add(sx / sw - (Left + ShapeW / 2.0));
            }
            // the blob travels: the mass sits to one side, and the sides alternate over the run
            Assert.True(centroids.Max() - centroids.Min() > 120, "the sweep moves the mass sideways: " + string.Join(", ", centroids.Select(c => c.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.True(Lit(listening, Left, Top + 40, Left + ShapeW, Top + ShapeH) > 2000);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void Deactivating_fades_the_glow_out_and_the_frames_stop() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            view.Level.Value = 0.6;
            view.Active = true;
            Tick();
            Run(clock, 2);
            var full = Sum(Pixels(window), Left, Top, Left + ShapeW, Top + ShapeH);
            view.Active = false;
            Tick();
            Run(clock, 0.2);
            var mid = Sum(Pixels(window), Left, Top, Left + ShapeW, Top + ShapeH);
            Assert.InRange(mid, full * 0.15, full * 0.95);        // mid fade: neither gone nor cut
            Run(clock, 1);
            Assert.Equal(0, Lit(Pixels(window), 0, 0, WindowW, WindowH, 0));
            Assert.False(view.Handler!.IsAnimating);
            var frames = view.Handler.Frames;
            Run(clock, 1);
            Assert.Equal(frames, view.Handler.Frames);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void A_hidden_glow_asks_for_no_frames_and_runs_again_once_shown() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var view = new VoiceGlowView { Width = ShapeW, Height = ShapeH, Active = true };
        var column = new Panel { IsVisible = false, Children = { view } };
        var window = new Window { Width = WindowW, Height = WindowH, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Card), Content = new Panel { Children = { column } } };
        window.Show();
        try
        {
            Tick();
            Run(clock, 0.5);
            Assert.NotNull(view.Handler);
            Assert.False(view.Handler!.IsAnimating, "a hidden composer's glow keeps the compositor ticking");
            column.IsVisible = true;
            Tick();
            Run(clock, 0.5);
            Assert.True(view.Handler!.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    public void Without_a_gpu_the_glow_is_a_still_drawn_at_once() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            view.Level.Value = 0.6;
            view.Active = true;
            Tick();
            Run(clock, 0.5);
            Assert.False(view.Handler!.IsAnimating);
            var first = Pixels(window);
            Assert.True(Lit(first, Left, Top + 40, Left + ShapeW, Top + ShapeH) > 1000, "the still shows at once, without the fade");
            Run(clock, 1.3);
            Assert.Equal(first, Pixels(window));
            view.Active = false;
            Tick();
            Assert.Equal(0, Lit(Pixels(window), 0, 0, WindowW, WindowH, 0));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    public void Under_reduce_motion_the_glow_comes_at_once_and_still_follows_the_voice() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(reduce: true);
        try
        {
            view.Level.Value = 0.8;
            view.Active = true;
            Tick();
            Run(clock, 0.1);
            Assert.True(Lit(Pixels(window), Left, Top + 40, Left + ShapeW, Top + ShapeH) > 500, "no fade: it is there at once");
            Assert.True(view.Handler!.IsAnimating, "the voice still lifts it");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void The_glow_is_under_the_content() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var content = new Border
        {
            Width = 200, Height = 30, Background = new SolidColorBrush(Color.FromRgb(255, 0, 0)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Left + 260, Top + ShapeH - 40, 0, 0),
        };
        var (window, view) = Show(above: content);
        try
        {
            view.Level.Value = 0.9;
            view.Active = true;
            Tick();
            Run(clock, 2.5);
            var p = Pixels(window);
            var o = ((Top + ShapeH - 30) * WindowW + Left + 360) * 4;
            Assert.Equal((255, 0, 0), (p[o], p[o + 1], p[o + 2]));   // RGBA: the red block is untouched by the glow beneath it
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void The_theme_reaches_the_glow_and_leaving_the_tree_stops_the_handler() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceGlowMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        var handler = view.Handler!;
        try
        {
            view.Active = true;
            Tick();
            Run(clock, 0.2);
            Assert.True(handler.Scene!.Look!.Recipe.Dark);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Tick();
            Run(clock, 0.2);
            Assert.False(handler.Scene.Look!.Recipe.Dark);
            ((Panel)window.Content!).Children.Clear();
            Tick();
            Assert.True(handler.IsStopped);
            Assert.False(handler.IsAnimating);
            Assert.Null(view.Handler);
        }
        finally { window.Close(); }
    });
}
