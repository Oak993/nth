using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light as a control. OD-0089: drawn on the frame clock on
// the composition thread, a bounded per-frame cost, stilled under reduce motion; the same control renders a preview from
// any settings value (Settings > Customization's live composer). Frames are asked for only while something moves (the
// board's port notes: "frames only while something moves", "Off draws nothing").
[Collection("Headless")]
public sealed class ComposerLightViewTests(HeadlessSession headless)
{
    private static (Window Window, ComposerLightView View) Show(ComposerLightSettings settings, bool reduce = false, ThemeVariant? theme = null)
    {
        var view = new ComposerLightView { Settings = settings, ComposerRect = new Rect(40, 40, 820, 52) };
        if (reduce) EnvironmentValues.SetAccessibilityReduceMotion(view, true);
        var window = new Window
        {
            Width = 900, Height = 132, RequestedThemeVariant = theme ?? ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)),
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
                if (Math.Abs(pixels[o] - 0x17) > 3 || Math.Abs(pixels[o + 1] - 0x17) > 3 || Math.Abs(pixels[o + 2] - 0x17) > 3) count++;
            }
        return count;
    }

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: Border beam by default, drawn on the composition thread inside the composer
    public void The_default_light_is_border_beam_inside_the_composer() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            Assert.NotNull(handler.Light);
            Assert.Equal(ComposerLightStyle.Beam, handler.Light!.Settings.Style);
            Assert.True(handler.IsAnimating);                     // the beam turns
            clock.Advance(TimeSpan.FromSeconds(0.9));
            Tick();
            var pixels = Pixels(window);
            Assert.True(Lit(pixels, 900, 40, 40, 860, 92) > 200, "the beam lights the composer's edge");
            Assert.Equal(0, Lit(pixels, 900, 0, 0, 900, 38));     // nothing above the composer
            Assert.Equal(0, Lit(pixels, 900, 0, 94, 900, 132));   // nor below it
            Assert.True(handler.Frames > 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0076 / gen.py: a change of style fades (.5 s out, .6 s in), never a cut; Off asks for no frames once faded
    public void A_change_of_settings_transitions_and_off_stops_the_frames() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var light = view.Handler!.Light!;
            view.Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura };
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.3));
            Tick();
            Assert.InRange(light.Aura.GroupAlpha, 0.05, 0.95);   // mid fade
            Assert.Equal(1, light.DrawnCount);                   // the beam still fading out
            var during = Pixels(window);
            Assert.True(Lit(during, 900, 0, 0, 900, 36) > 0, "the halo is already coming in above the composer");
            view.Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off };
            Tick();
            clock.Advance(TimeSpan.FromSeconds(1));
            Tick();
            Assert.False(view.Handler!.IsAnimating);
            var off = Pixels(window);
            Assert.Equal(0, Lit(off, 900, 0, 0, 900, 132));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089 / logic.js tick(): reduce motion stills the light: one still frame, no animation
    public void Reduce_motion_draws_one_still() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default, reduce: true);
        try
        {
            Assert.False(view.Handler!.IsAnimating);
            var first = Pixels(window);
            clock.Advance(TimeSpan.FromSeconds(2.2));
            Tick();
            Assert.Equal(first, Pixels(window));
            Assert.True(Lit(first, 900, 40, 40, 860, 92) > 100);  // still drawn
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: a live preview renders the same light from a settings value apart from the saved one
    public void Two_views_render_their_own_settings() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var a = new ComposerLightView { Settings = ComposerLightSettings.Default, ComposerRect = new Rect(40, 40, 820, 52) };
        var b = new ComposerLightView { Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, ComposerRect = new Rect(40, 172, 820, 52) };
        var window = new Window { Width = 900, Height = 264, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)), RequestedThemeVariant = ThemeVariant.Dark, Content = new Panel { Children = { a, b } } };
        window.Show();
        try
        {
            Tick();
            Assert.Equal(ComposerLightStyle.Beam, a.Handler!.Light!.Settings.Style);
            Assert.Equal(ComposerLightStyle.Aura, b.Handler!.Light!.Settings.Style);
            Assert.True(b.Handler.Light.AuraVisible);
            Assert.False(a.Handler.Light.AuraVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // ENGINEERING DECISION: without a GPU the light is one still (its shaders are too slow for 60 Hz in software)
    public void Without_a_gpu_the_light_is_a_still() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.False(view.Handler!.IsAnimating);
            var first = Pixels(window);
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Tick();
            Assert.Equal(first, Pixels(window));
            Assert.True(Lit(first, 900, 40, 40, 860, 92) > 100);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // REVIEW: the board's port notes, "frames only while something moves": a light nobody sees (a kept, hidden chat column, RootView's IsVisible = false) asks for no frames, and turns again once shown
    public void A_hidden_light_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var view = new ComposerLightView { Settings = ComposerLightSettings.Default, ComposerRect = new Rect(40, 40, 820, 52) };
        var column = new Panel { IsVisible = false, Children = { view } };
        var window = new Window { Width = 900, Height = 132, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)), Content = new Panel { Children = { column } } };
        window.Show();
        try
        {
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.NotNull(view.Handler);
            Assert.False(view.Handler!.IsAnimating, "a hidden composer's light keeps the compositor ticking");
            column.IsVisible = true;
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.5));
            Tick();
            Assert.True(view.Handler!.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // REVIEW: OD-0083's bounded per-frame cost: Border beam draws inside the composer (its renderer's reach is 1 px), so the region it dirties every frame is the composer, not the Uiverse halo's reach (HaloSize + 3 HaloBlur + 4 = 96 px) of a light that is not shown
    public void The_default_beam_dirties_only_the_composer_each_frame() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Tick();
            var bounds = view.Handler!.GetRenderBounds();
            Assert.True(bounds.Width <= 820 + 2 * 4 && bounds.Height <= 52 + 2 * 4, $"the beam's frames dirty {bounds} around the composer (40, 40, 820, 52)");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // REVIEW: as ComposerLightSlot hosts it (no ComposerRect until LayoutUpdated, so the first snapshot is the unlaid 0 x 0 bounds), the Line light draws once laid out
    public void A_line_light_attached_before_layout_draws_once_laid_out() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var view = new ComposerLightView { Settings = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Line) } };
        var window = new Window { Width = 900, Height = 132, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)), Content = new Panel { Children = { view } } };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        window.Show();
        try
        {
            Tick();
            // the first frames draw at once (no render-thread stall on the unlaid size)
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"the first frames took {watch.Elapsed.TotalSeconds:F1} s");
            view.ComposerRect = new Rect(40, 40, 820, 52);
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromSeconds(0.05)); Tick(); } // 1.5 s of beam time: Line is a quarter into its travel, its edge fade open
            var light = view.Handler!.Light!;
            Assert.True(double.IsFinite(light.Clock.LineX) && light.Clock.LineEdge > 0, $"Line x {light.Clock.LineX}, edge {light.Clock.LineEdge}");
            Assert.True(Lit(Pixels(window), 900, 40, 40, 860, 92) > 50, "the Line beam shows on the composer");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // the composer's size and the theme reach the light (a growing pill; a switch to the light theme cross-fades Auto to the light sheet)
    public void The_composers_size_and_the_theme_reach_the_light() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        try
        {
            var light = view.Handler!.Light!;
            Assert.Equal((820.0, 52.0), (light.Aura.W, light.Aura.H));
            view.ComposerRect = new Rect(40, 30, 700, 72);
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.1));
            Tick();
            Assert.Equal((700.0, 72.0), (light.Aura.W, light.Aura.H));
            Assert.True(light.SlotKey(light.CurrentSlot)!.Value.Dark);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Tick();
            clock.Advance(TimeSpan.FromSeconds(0.1));
            Tick();
            Assert.False(light.SlotKey(light.CurrentSlot)!.Value.Dark);    // Border beam's Auto theme follows
            clock.Advance(TimeSpan.FromSeconds(1));
            Tick();
            Assert.Equal(0.4 * 1.2, light.Aura.HaloAlpha, 6);              // --au-tm 1.2 in the light theme
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // review D6: leaving the tree stops the handler and releases its renderer on the render thread
    public void Leaving_the_tree_stops_the_handler() => headless.OnUiThread(() =>
    {
        var (window, view) = Show(ComposerLightSettings.Default);
        var handler = view.Handler!;
        ((Panel)window.Content!).Children.Clear();
        Tick();
        Assert.True(handler.IsStopped);
        Assert.False(handler.IsAnimating);
        Assert.Null(view.Handler);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-251")] // review D1: a composer with no size yet is never sent to the render thread; the light starts once it has one
    public void A_composer_with_no_size_is_not_lit_until_it_has_one() => headless.OnUiThread(() =>
    {
        var view = new ComposerLightView { Settings = ComposerLightSettings.Default, Width = 0, Height = 0 };
        var window = new Window { Width = 900, Height = 132, Content = new Panel { Children = { view } } };
        window.Show();
        try
        {
            Tick();
            Assert.NotNull(view.Handler);
            Assert.Null(view.Handler!.Light);
            view.ComposerRect = new Rect(40, 40, 820, 52);
            Tick();
            Assert.Equal(820, view.Handler!.Light!.Aura.W);
        }
        finally { window.Close(); }
    });
}
