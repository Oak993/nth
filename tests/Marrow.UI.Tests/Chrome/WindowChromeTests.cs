using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/WindowChrome.swift (B2 1fbf81e). Part 1 of 2 (this file): the window
// configuration, the titlebar geometry and the traffic-light placement/animation (PR-WIN-022 through PR-WIN-037).
// SidebarLayout (PR-WIN-038 through PR-WIN-045) is WindowChromeSecondTests, which needs TestRuntime's manual
// main loop rather than the headless UI thread and so cannot share this fixture.
// Constants transcribed from Chrome.swift and WindowChrome.swift, as the records cite them:
//   Chrome.swift:15-19 trafficLightDiameter 14, trafficLightSpacing 9, pitch 23, leading 18, top 16
//   Chrome.swift:31-38 chromeHorizontalPadding 14, chromeTopPadding 12, capsuleHeight 32, sheetInset 0
//   WindowChrome.swift:136-138 collapsedTrafficLightTop = 0 + 12 + (32 - 14) / 2 = 21
//   WindowChrome.swift:148-150 titlebarHeight = 0 + 12 + 32 = 44
//   WindowChrome.swift:178-179 leading/top by sidebarVisible; :183-187 order close, miniaturize, zoom, x = leading + index * 23
// PR-WIN-025 (autorecalculatesKeyViewLoop) is not tested: WindowChrome.cs documents there is no Avalonia property
// whose absence would prove it (Avalonia's tab order is already computed on demand, never eagerly during layout),
// so no assertion here could distinguish "ported" from "nothing to port" (report, not a Req test).
// PR-WIN-027, PR-WIN-028 and PR-WIN-029 (the AppKit titlebar-container fit and its re-tiling/live-resize
// coalescing) have no container to fit in this design (ExtendClientAreaTitleBarHeightHint sets the band once
// and nothing re-tiles it): TitlebarBandNeverShrinks below is their observable result, the never-shrunk half of
// PR-WIN-027 restated for a design with no container to shrink.
[Collection("Headless")]
public sealed class WindowChromeTests(HeadlessSession headless)
{
    private static Window NewWindow() => new();

    [Fact]
    [Trait("Req", "PR-WIN-037")] // DroppyCode/UI/Chrome/WindowChrome.swift:28, :216-219; ED-224
    public void A_live_resize_suppresses_even_an_explicitly_requested_placement_animation() => headless.OnUiThread(() =>
    {
        var window = NewWindow();
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true);
        WindowLiveResizeSignal.SetIsActive(window, true);
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: true);
        var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
        foreach (var button in new[] { close, miniaturize, zoom }) Assert.Null(((Border)button).Transitions);
        Assert.Equal(14, Canvas.GetLeft(close));
        Assert.Equal(21, Canvas.GetTop(close));
    });

    [Fact]
    [Trait("Req", "PR-WIN-037")] // DroppyCode/UI/Chrome/WindowChrome.swift:28, :52, :93; ED-224
    public void The_probe_places_once_without_animation_at_the_end_of_a_live_resize() => headless.OnUiThread(() =>
    {
        var probe = new WindowChromeProbe();
        var window = new Window { Content = probe };
        window.Show();
        try
        {
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            Assert.Null(((Border)close).Transitions); // :52 initial placement.
            WindowLiveResizeSignal.SetIsActive(window, true);
            probe.SidebarVisible = false;
            Assert.Null(((Border)close).Transitions); // :28 while dragging.
            // Make the end placement observable even with unchanged target coordinates (:185-186 unhide).
            foreach (var button in new[] { close, miniaturize, zoom }) button.IsVisible = false;
            WindowLiveResizeSignal.SetIsActive(window, false);
            foreach (var button in new[] { close, miniaturize, zoom }) Assert.True(button.IsVisible);
            Assert.Null(((Border)close).Transitions);
            Assert.Equal(14, Canvas.GetLeft(close));
            Assert.Equal(21, Canvas.GetTop(close));
            close.IsVisible = false;
            WindowLiveResizeSignal.SetIsActive(window, false);
            Assert.False(close.IsVisible); // No second placement without a new end transition.
            probe.SidebarVisible = true;
            Assert.NotNull(((Border)close).Transitions); // Ordinary changes animate again after the drag.
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-037")] // DroppyCode/UI/Chrome/WindowChrome.swift:40-57, :93; ED-224
    public void Moving_a_probe_unregisters_the_previous_windows_resize_observer() => headless.OnUiThread(() =>
    {
        var probe = new WindowChromeProbe();
        var first = new Window { Content = probe };
        var second = NewWindow();
        first.Show();
        second.Show();
        try
        {
            WindowLiveResizeSignal.SetIsActive(first, true);
            first.Content = null;
            var (oldClose, _, _) = WindowChrome.Buttons(first);
            oldClose.IsVisible = false;
            second.Content = probe;
            WindowLiveResizeSignal.SetIsActive(second, true);
            var (newClose, _, _) = WindowChrome.Buttons(second);
            newClose.IsVisible = false;
            WindowLiveResizeSignal.SetIsActive(first, false);
            Assert.False(oldClose.IsVisible);
            Assert.False(newClose.IsVisible);
            WindowLiveResizeSignal.SetIsActive(second, false);
            Assert.True(newClose.IsVisible);
            Assert.Null(((Border)newClose).Transitions);
        }
        finally { first.Close(); second.Close(); }
    });

    [Trait("Req", "PR-WIN-022")] // WindowChrome.swift:104-110
    [Fact]
    public void Configure_is_idempotent_per_window()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            Assert.False(WindowChrome.IsConfigured(window));
            WindowChrome.Configure(window);
            Assert.True(WindowChrome.IsConfigured(window));
            var heightAfterFirst = window.ExtendClientAreaTitleBarHeightHint;
            window.ExtendClientAreaTitleBarHeightHint = -1; // prove a second Configure() call returns immediately
            WindowChrome.Configure(window);
            Assert.Equal(-1, window.ExtendClientAreaTitleBarHeightHint);
            Assert.NotEqual(heightAfterFirst, window.ExtendClientAreaTitleBarHeightHint);
        });
    }

    [Trait("Req", "PR-WIN-022")] // WindowChrome.swift:105, weak registry
    [Fact]
    public void Configure_tracks_two_windows_independently()
    {
        headless.OnUiThread(() =>
        {
            var a = NewWindow();
            var b = NewWindow();
            WindowChrome.Configure(a);
            Assert.True(WindowChrome.IsConfigured(a));
            Assert.False(WindowChrome.IsConfigured(b));
        });
    }

    [Trait("Req", "PR-WIN-023")] // WindowChrome.swift:111-115
    [Fact]
    public void Configure_extends_the_client_area_over_the_caption_with_no_system_decoration_drawn()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.Configure(window);
            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        });
    }

    [Trait("Req", "PR-WIN-024")] // WindowChrome.swift:116-120
    [Fact]
    public void Configure_leaves_the_window_transparent_with_the_system_shadow_kept()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.Configure(window);
            Assert.Same(Avalonia.Media.Brushes.Transparent, window.Background);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations); // keeps the DWM shadow, ED-063
        });
    }

    [Trait("Req", "PR-WIN-026")] // WindowChrome.swift:148-150
    [Fact]
    public void Titlebar_height_is_exactly_44()
    {
        Assert.Equal(44, WindowChrome.TitlebarHeight);
        Assert.Equal(Chrome.SheetInset + Chrome.ChromeTopPadding + Chrome.CapsuleHeight, WindowChrome.TitlebarHeight);
    }

    [Trait("Req", "PR-WIN-026")] // WindowChrome.swift:139-150
    [Trait("Req", "PR-WIN-027")] // WindowChrome.swift:152-172, never shrunk (see file header)
    [Fact]
    public void Configure_sets_the_titlebar_band_to_44_and_it_never_shrinks()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.Configure(window);
            Assert.Equal(44, window.ExtendClientAreaTitleBarHeightHint);
            // A second placement (any reason WindowChrome.swift would refit the container) leaves the band
            // exactly 44: there is no container for the platform to re-tile smaller in this design (PR-WIN-028,
            // PR-WIN-029), so nothing here can shrink it below the value Configure set.
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            Assert.Equal(44, window.ExtendClientAreaTitleBarHeightHint);
        });
    }

    [Trait("Req", "PR-WIN-030")] // WindowChrome.swift:97
    [Fact]
    public void Probe_never_takes_a_mouse_event()
    {
        var probe = new WindowChromeProbe();
        Assert.False(probe.IsHitTestVisible);
    }

    [Trait("Req", "PR-WIN-030")] // WindowChromeConfigurator.swift:15-18 updateNSView
    [Fact]
    public void Configurator_pushes_its_two_flags_onto_the_probe()
    {
        var configurator = new WindowChromeConfigurator { SidebarVisible = false, SidebarDragging = true };
        var probe = new WindowChromeProbe();
        configurator.Update(probe);
        Assert.False(probe.SidebarVisible);
        Assert.True(probe.SidebarDragging);
    }

    [Trait("Req", "PR-WIN-031")] // WindowChrome.swift:183-187, :197-198
    [Fact]
    public void Buttons_are_14_across_on_a_23_point_pitch_close_miniaturize_zoom_order()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            foreach (var button in new[] { close, miniaturize, zoom })
            {
                Assert.Equal(14, ((Border)button).Width);
                Assert.Equal(14, ((Border)button).Height);
            }
            Assert.Equal(18, Canvas.GetLeft((Border)close));
            Assert.Equal(41, Canvas.GetLeft((Border)miniaturize)); // 18 + 23
            Assert.Equal(64, Canvas.GetLeft((Border)zoom)); // 18 + 2 * 23
        });
    }

    [Trait("Req", "PR-WIN-032")] // WindowChrome.swift:178-179, Chrome.swift:18-19
    [Fact]
    public void With_the_sidebar_visible_the_buttons_sit_at_18_16()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (close, _, _) = WindowChrome.Buttons(window);
            Assert.Equal(18, Canvas.GetLeft((Border)close));
            Assert.Equal(16, Canvas.GetTop((Border)close));
        });
    }

    [Trait("Req", "PR-WIN-033")] // WindowChrome.swift:134-137, :178
    [Fact]
    public void With_the_sidebar_hidden_the_buttons_move_to_14_21()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
            var (close, _, _) = WindowChrome.Buttons(window);
            Assert.Equal(14, Canvas.GetLeft((Border)close));
            Assert.Equal(21, Canvas.GetTop((Border)close));
            Assert.Equal(21, WindowChrome.CollapsedTrafficLightTop);
        });
    }

    [Trait("Req", "PR-WIN-034")] // WindowChrome.swift:188-212
    [Fact]
    public void The_same_three_button_instances_are_reused_and_moved_across_placements()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var first = WindowChrome.Buttons(window);
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
            var second = WindowChrome.Buttons(window);
            Assert.Same(first.Close, second.Close);
            Assert.Same(first.Miniaturize, second.Miniaturize);
            Assert.Same(first.Zoom, second.Zoom);
            Assert.Equal(14, Canvas.GetLeft((Border)second.Close)); // moved, not recreated
        });
    }

    [Trait("Req", "PR-WIN-034")] // WindowChrome.swift:208 constraint.constant != target
    [Fact]
    public void A_placement_with_no_change_is_a_no_op()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true);
            var (close, _, _) = WindowChrome.Buttons(window);
            ((Border)close).Transitions = null; // clear the transitions the first, changed placement installed
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true); // same leading/top: no-op
            Assert.Null(((Border)close).Transitions); // never touched: no transitions were (re)installed
        });
    }

    [Trait("Req", "PR-WIN-035")] // WindowChrome.swift:185-186
    [Fact]
    public void Every_placement_unhides_all_three_buttons()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            foreach (var button in new[] { close, miniaturize, zoom }) ((Border)button).IsVisible = false;
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
            Assert.True(close.IsVisible);
            Assert.True(miniaturize.IsVisible);
            Assert.True(zoom.IsVisible);
        });
    }

    [Trait("Req", "PR-WIN-036")] // WindowChrome.swift:220-229
    [Theory]
    [InlineData(false, 0.32)]
    [InlineData(true, 0.18)]
    public void An_animated_placement_glides_over_032_seconds_or_018_with_reduce_motion(bool reduceMotion, double seconds)
    {
        headless.OnUiThread(() =>
        {
            using var _ = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: reduceMotion));
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true); // changed: unhidden buttons
            var (close, _, _) = WindowChrome.Buttons(window);
            var transitions = Assert.IsType<Avalonia.Animation.Transitions>(((Border)close).Transitions);
            Assert.Equal(2, transitions.Count);
            foreach (var transition in transitions)
                Assert.Equal(TimeSpan.FromSeconds(seconds), Assert.IsType<Avalonia.Animation.DoubleTransition>(transition).Duration);
        });
    }

    [Trait("Req", "PR-WIN-037")] // WindowChrome.swift:214-219
    [Fact]
    public void An_unanimated_placement_installs_no_transitions()
    {
        headless.OnUiThread(() =>
        {
            var window = NewWindow();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: true); // give it transitions once
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false); // then a changed, unanimated one
            var (close, _, _) = WindowChrome.Buttons(window);
            Assert.Null(((Border)close).Transitions);
            Assert.Equal(14, Canvas.GetLeft((Border)close)); // still applied immediately
        });
    }
}
