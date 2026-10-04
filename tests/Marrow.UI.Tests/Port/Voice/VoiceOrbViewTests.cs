using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Voice.Orb;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T52 and V-T53 (V-11.3, V-11.5): the orb as a control on the composition thread: frames only while the view is drawn and something moves, one still without a GPU
// and under reduce motion, a swap that crossfades. Headless Skia is the software rasterizer, where the orb draws one still unless a test turns the motion on (VoiceOrbMotion).
[Collection("Headless")]
public sealed class VoiceOrbViewTests(HeadlessSession headless)
{
    private static (Window Window, VoiceOrbView View) Show(bool reduce = false, VoiceOrbKind kind = VoiceOrbKind.Siri)
    {
        var view = new VoiceOrbView { Kind = kind, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (reduce) EnvironmentValues.SetAccessibilityReduceMotion(view, true);
        var window = new Window
        {
            Width = 120, Height = 120, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)),
            Content = new Panel { Children = { view } },
        };
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

    private static int Lit(byte[] pixels, int width, int x0, int y0, int x1, int y1)
    {
        var count = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var o = (y * width + x) * 4;
                if (Math.Abs(pixels[o] - 0x1c) > 3 || Math.Abs(pixels[o + 1] - 0x1c) > 3 || Math.Abs(pixels[o + 2] - 0x1c) > 3) count++;
            }
        return count;
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void The_view_is_72_px_square_and_draws_the_ball_at_its_centre() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            Assert.Equal((72.0, 72.0), (view.Bounds.Width, view.Bounds.Height));
            Assert.Equal(24, view.Bounds.X);
            var pixels = Pixels(window);
            Assert.True(Lit(pixels, 120, 24 + 20, 24 + 20, 24 + 52, 24 + 52) > 600, "the ball (32 px) lights the middle of the view");
            Assert.Equal(0, Lit(pixels, 120, 0, 0, 120, 10));
            Assert.True(view.Handler!.Frames > 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void A_visible_orb_asks_for_a_frame_after_every_frame_since_it_always_moves() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            Assert.True(view.Handler!.IsAnimating);
            var frames = view.Handler.Frames;
            for (var i = 0; i < 5; i++) { clock.Advance(TimeSpan.FromMilliseconds(16)); Tick(); }
            Assert.True(view.Handler.Frames > frames + 3, $"{view.Handler.Frames - frames} frames in five ticks");
            Assert.True(view.Handler.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void A_hidden_orb_asks_for_no_frames_and_turns_again_once_shown() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var view = new VoiceOrbView();
        var column = new Panel { IsVisible = false, Children = { view } };
        var window = new Window { Width = 120, Height = 120, RequestedThemeVariant = ThemeVariant.Dark, Content = new Panel { Children = { column } } };
        window.Show();
        try
        {
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.NotNull(view.Handler);
            Assert.False(view.Handler!.IsAnimating, "a hidden orb keeps the compositor ticking");
            Assert.Equal(0, view.Handler.Frames);
            column.IsVisible = true;
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.True(view.Handler!.IsAnimating);
            Assert.True(view.Handler.Frames > 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void Without_a_gpu_the_orb_is_one_still_frame() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.False(view.Handler!.IsAnimating);
            var first = Pixels(window);
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Tick();
            Assert.Equal(first, Pixels(window));
            Assert.True(Lit(first, 120, 44, 44, 76, 76) > 600, "the still is drawn");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void Under_reduce_motion_the_orb_is_one_still_frame_with_the_phase_stopped() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(reduce: true);
        try
        {
            Assert.False(view.Handler!.IsAnimating);
            var first = Pixels(window);
            clock.Advance(TimeSpan.FromSeconds(2.2));
            Tick();
            Assert.Equal(first, Pixels(window));
            Assert.Equal(0, view.Handler.Scene!.Runtime(VoiceOrbKind.Siri)!.Phase);
            Assert.True(Lit(first, 120, 44, 44, 76, 76) > 600);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T52")]
    public void Leaving_the_tree_stops_the_handler_and_releases_the_surface() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var (window, view) = Show();
        var handler = view.Handler!;
        ((Panel)window.Content!).Children.Clear();
        Tick();
        Assert.True(handler.IsStopped);
        Assert.False(handler.IsAnimating);
        Assert.Null(view.Handler);
        window.Close();
    });

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T53")]
    public void Choosing_the_other_orb_crossfades_the_two_views_and_hands_over_the_phase() => headless.OnUiThread(() =>
    {
        using var motion = new VoiceOrbMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show();
        try
        {
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(16)); Tick(); }
            var scene = view.Handler!.Scene!;
            var phase = scene.Runtime(VoiceOrbKind.Siri)!.Phase;
            Assert.True(phase > 0.05);
            view.Kind = VoiceOrbKind.Membrane;
            Tick();
            clock.Advance(TimeSpan.FromMilliseconds(80));
            Tick();
            Assert.True(scene.Fade.Visible(VoiceOrbKind.Siri) && scene.Fade.Visible(VoiceOrbKind.Membrane), "both views while they cross");
            Assert.True(scene.Runtime(VoiceOrbKind.Membrane)!.Phase >= phase, "the new orb went on from the old one's phase");
            for (var i = 0; i < 60; i++) { clock.Advance(TimeSpan.FromMilliseconds(16)); Tick(); }
            Assert.False(scene.Fade.Visible(VoiceOrbKind.Siri));
            Assert.Equal(1, scene.Opacity(VoiceOrbKind.Membrane));
        }
        finally { window.Close(); }
    });
}
