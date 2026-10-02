using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// DEVIATION OD-0044 (OD-0046): WindowChrome.swift's traffic lights become Windows caption buttons at the top right, in the
// design the owner picked, variant N "Turn pulse" (porting/owner_decisions.md OD-0044 gives every value below):
//   cluster at the window's top right, 6 pt in, top 8 (centred on the chrome capsules' line at 12 + 32 / 2 = 28);
//   order minimise, maximise, close; buttons 46 x 40, 2 pt apart; Windows hit-test values MinButton, MaxButton, Close;
//   restore glyph and the name Restore while maximised; maximise greyed and a plain client hit when the window cannot resize;
//   hovered: two rings, scale 0.45 -> 1.55, opacity 0.9 -> 0 on cubic-bezier(.2, .6, .3, 1) over 1.3 s, the second 0.45 s
//   later; the glyph ticks 1 -> 0.86 (12 %) -> 1.06 (30 %) -> 1; close's rings rose rgb(255, 107, 139) on dark (OD-0068: a deeper rose on light; CaptionContrastTests).
// The sidebar no longer moves them and they never glide (the records PR-WIN-031 to PR-WIN-036 and UI-WIN-001 to UI-WIN-003
// describe the traffic lights these replace; each test names the record whose subject it now covers).
[Collection("Headless")]
public sealed class CaptionButtonTests(HeadlessSession headless)
{
    private static Window Shown()
    {
        var window = new Window { Width = 600, Height = 300 };
        window.Content = new Panel { Children = { new Border(), WindowChrome.CaptionLayer(window) } }; // as WindowManager lays it
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-WIN-031")] // WindowChrome.swift:183-199 the three buttons; OD-0044 their Windows order and size
    [Trait("Req", "UI-WIN-001")]
    [Trait("Req", "UI-WIN-002")]
    [Trait("Req", "UI-WIN-003")]
    public void The_cluster_sits_at_the_top_right_in_Windows_order_minimise_maximise_close() => headless.OnUiThread(() =>
    {
        var window = Shown();
        try
        {
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var cluster = Assert.IsType<StackPanel>(WindowChrome.CaptionCluster(window));
            Assert.Equal(6, Canvas.GetRight(cluster));
            Assert.Equal(8, Canvas.GetTop(cluster));
            Assert.Equal(Avalonia.Layout.Orientation.Horizontal, cluster.Orientation);
            Assert.Equal(2, cluster.Spacing);
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            Assert.Equal(new Control[] { miniaturize, zoom, close }, cluster.Children);
            Assert.Equal([CaptionButton.Kind.Minimize, CaptionButton.Kind.Maximize, CaptionButton.Kind.Close], cluster.Children.Cast<CaptionButton>().Select(b => b.ButtonKind));
            foreach (var button in new[] { close, miniaturize, zoom })
            {
                Assert.Equal(46, button.Width);
                Assert.Equal(40, button.Height);
                Assert.True(button.IsVisible);
            }
            Assert.Equal(Win32Properties.Win32HitTestValue.MinButton, Win32Properties.GetNonClientHitTestResult(miniaturize));
            Assert.Equal(Win32Properties.Win32HitTestValue.MaxButton, Win32Properties.GetNonClientHitTestResult(zoom));
            Assert.Equal(Win32Properties.Win32HitTestValue.Close, Win32Properties.GetNonClientHitTestResult(close));
            Assert.Same(WindowChrome.CaptionLayer(window), cluster.Parent); // window chrome, over the content, not in it
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(600 - 6, cluster.Bounds.Right, 3); // flush to the right edge, 6 pt in
            Assert.Equal(46 * 3 + 2 * 2, cluster.Bounds.Width, 3);
            Assert.Equal(WindowChrome.CaptionTrailingInset + WindowChrome.CaptionClusterWidth + WindowChrome.CaptionClearance, WindowChrome.CaptionTrailingReserve);
            Assert.Equal(156, WindowChrome.CaptionTrailingReserve);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-032")] // WindowChrome.swift:178-179 the sidebar decided the place; OD-0044 it no longer does
    [Trait("Req", "PR-WIN-033")]
    [Trait("Req", "PR-WIN-034")] // :188-212 the same three buttons are kept
    [Trait("Req", "PR-WIN-035")] // :185-186 every placement unhides them
    [Trait("Req", "PR-WIN-036")] // :220-229 OD-0044: no glide, animated or not
    public void A_placement_reuses_unhides_and_never_moves_or_animates_the_buttons() => headless.OnUiThread(() =>
    {
        var window = new Window();
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true);
        var first = WindowChrome.Buttons(window);
        foreach (var button in new[] { first.Close, first.Miniaturize, first.Zoom }) button.IsVisible = false;
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: true);
        var second = WindowChrome.Buttons(window);
        Assert.Same(first.Close, second.Close);
        Assert.Same(first.Miniaturize, second.Miniaturize);
        Assert.Same(first.Zoom, second.Zoom);
        foreach (var button in new[] { second.Close, second.Miniaturize, second.Zoom })
        {
            Assert.True(button.IsVisible);
            Assert.Null(button.Transitions);
        }
        var cluster = WindowChrome.CaptionCluster(window);
        Assert.Equal(6, Canvas.GetRight(cluster));
        Assert.Equal(8, Canvas.GetTop(cluster));
        Assert.Null(cluster.Transitions);
    });

    [Fact]
    [Trait("Req", "UI-WIN-003")] // WindowManager.swift:81 a fixed-size window: OD-0046 the Windows greyed maximise
    public void Maximise_shows_restore_while_maximised_and_is_greyed_on_a_window_that_cannot_resize() => headless.OnUiThread(() =>
    {
        var window = Shown();
        try
        {
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            Assert.False(zoom.IsMaximized);
            Assert.Equal("Maximize", Avalonia.Automation.AutomationProperties.GetName(zoom));
            Assert.Equal("Minimize", Avalonia.Automation.AutomationProperties.GetName(miniaturize));
            Assert.Equal("Close", Avalonia.Automation.AutomationProperties.GetName(close));
            window.WindowState = WindowState.Maximized;
            Assert.True(zoom.IsMaximized);
            Assert.Equal("Restore", Avalonia.Automation.AutomationProperties.GetName(zoom));
            window.WindowState = WindowState.Normal;
            Assert.False(zoom.IsMaximized);
            Assert.True(zoom.IsEnabled);
            window.CanResize = false;
            Assert.False(zoom.IsEnabled);
            Assert.Equal(Win32Properties.Win32HitTestValue.Client, Win32Properties.GetNonClientHitTestResult(zoom));
            window.CanResize = true;
            Assert.True(zoom.IsEnabled);
            Assert.Equal(Win32Properties.Win32HitTestValue.MaxButton, Win32Properties.GetNonClientHitTestResult(zoom));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-117")] // the pulse runs on the frame-clock seam; its curve and timing are OD-0044's
    public void The_turn_pulse_rings_and_glyph_tick_follow_the_picked_curve()
    {
        Assert.Null(CaptionButton.Ring(1, 0.44)); // the second ring leaves 0.45 s after the first
        var start = CaptionButton.Ring(0, 0)!.Value;
        Assert.Equal(0.45, start.Scale, 6);
        Assert.Equal(0.9, start.Opacity, 6);
        var mid = CaptionButton.Ring(0, 0.65)!.Value; // half-way through the 1.3 s pulse
        var eased = CometEasing.Bezier(0.5, .2, .6, .3, 1).Value;
        Assert.Equal(0.45 + 1.1 * eased, mid.Scale, 6);
        Assert.Equal(0.9 * (1 - eased), mid.Opacity, 6);
        var end = CaptionButton.Ring(0, 1.2999)!.Value;
        Assert.InRange(end.Scale, 1.54, 1.55);
        Assert.InRange(end.Opacity, 0, 0.01);
        Assert.Equal(0.45, CaptionButton.Ring(0, 1.3)!.Value.Scale, 6); // it repeats
        Assert.Equal(CaptionButton.Ring(0, 0.2)!.Value, CaptionButton.Ring(1, 0.65)!.Value);
        Assert.Equal(1, CaptionButton.GlyphScale(0), 6);
        Assert.Equal(0.86, CaptionButton.GlyphScale(0.12 * 1.3), 6);
        Assert.Equal(1.06, CaptionButton.GlyphScale(0.30 * 1.3), 6);
        Assert.Equal(1, CaptionButton.GlyphScale(1.2999), 3);
    }

    [Fact]
    [Trait("Decision", "ED-117")] // frames are the only wake-ups; hover starts the pulse, leaving stops it
    public void Hovering_runs_the_pulse_from_the_frame_clock_and_leaving_stops_it() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var button = new CaptionButton(CaptionButton.Kind.Close);
        Assert.Equal(0, frames.Pending);
        button.IsHovered = true;
        Assert.Equal(1, frames.Pending);
        time.Advance(TimeSpan.FromSeconds(0.4));
        frames.Advance(TimeSpan.FromSeconds(0.4));
        Assert.Equal(0.4, button.PulseTime, 6);
        Assert.Equal(1, frames.Pending); // keeps asking while hovered
        button.IsHovered = false;
        Assert.Equal(0, button.PulseTime);
        Assert.Equal(0, frames.Pending);
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        button.IsHovered = true; // reduce motion: a still ring, no frames
        Assert.Equal(0, frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void The_window_hook_maps_hit_test_values_to_hover_and_press() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var window = new Window();
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
        var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
        WindowChrome.SetCaptionHover(window, 20); // HTCLOSE
        Assert.True(close.IsHovered);
        Assert.False(miniaturize.IsHovered);
        WindowChrome.SetCaptionHover(window, 8); // HTMINBUTTON
        Assert.False(close.IsHovered);
        Assert.True(miniaturize.IsHovered);
        WindowChrome.SetCaptionHover(window, 9); // HTMAXBUTTON
        Assert.True(zoom.IsHovered);
        WindowChrome.SetCaptionHover(window, 0);
        Assert.False(zoom.IsHovered);
        WindowChrome.SetCaptionPressed(window, 20);
        Assert.True(close.IsPressed);
        Assert.False(zoom.IsPressed);
        WindowChrome.SetCaptionPressed(window, 0);
        Assert.False(close.IsPressed);
    });

    [Fact]
    [Trait("Decision", "ED-117")]
    public void A_hovered_close_button_draws_its_rose_ring_and_a_resting_one_draws_only_the_glyph() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var button = new CaptionButton(CaptionButton.Kind.Close);
        // OD-0044's rose is the dark appearance's (OD-0068 gives a light one a deeper rose): a dark window, as the black says.
        var window = new Window { Width = 46, Height = 40, Content = button, Background = Brushes.Black, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            // 0.35 s in, the first ring is well out and still bright: sample its left edge on the centre line.
            var r = CaptionButton.Ring(0, 0.35)!.Value;
            var x = (int)Math.Round(23 - (CaptionButton.RingDiameter / 2 * r.Scale - CaptionButton.RingStroke / 2));
            Assert.Equal(Colors.Black, Capture(window, x, 20)); // at rest: nothing but the glyph
            button.IsHovered = true;
            time.Advance(TimeSpan.FromSeconds(0.35));
            frames.Advance(TimeSpan.FromSeconds(0.35));
            Dispatcher.UIThread.RunJobs();
            var ring = Capture(window, x, 20);
            Assert.True(ring.R > 20 && ring.R > ring.G * 1.5 && ring.R > ring.B, $"expected a rose ring pixel, got {ring}");
        }
        finally { window.Close(); }
    });

    private static Color Capture(Window window, int x, int y)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var bitmap = new WriteableBitmap(frame.PixelSize, frame.Dpi, frame.Format ?? Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = bitmap.Lock()) frame.CopyPixels(new PixelRect(frame.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        using var locked = bitmap.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(locked.Address + y * locked.RowBytes + x * 4, bytes, 0, 4);
        return locked.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
