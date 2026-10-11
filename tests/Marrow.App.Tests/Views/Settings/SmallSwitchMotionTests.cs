using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// UI-SETTINGS-012, SettingsView.swift:296-305 (the system switch, controlSize .small, accent tint), drawn as the owner's Nocta (DEVIATION OD-0143,
// ED-NEW-nocta-1; design 7 of work/design/toggles/index.html): a 46 x 26 black glass track; a 22 obsidian dome 2 in from every edge that slides its
// 20 points on spring(0.38, 0.82); a 5 point lume at the dome's centre, an empty ring off and a lit dot on in the accent made light, with the
// dome's inner light and the track's aura; lights crossing over 0.28 s (0.14 s under Reduce Motion, where the knob jumps); hover outlines the dome,
// hover and press deepen its shadow, and a press scales it 1.08 on spring(0.22, 0.9). These replace the Mac measurement tests of ED-280 (5) with
// equal-or-stricter ones: every number is exact, and the states are read from rendered pixels as well as from the switch.
[Collection("Headless")]
public sealed class SmallSwitchMotionTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static SmallSwitch Switch(Control view, string row) => Assert.Single(Nodes<SmallSwitch>(Row(view, row)));
    private static readonly Rect Off = new(2, 2, 22, 22), On = new(22, 2, 22, 22);

    // The spring as SwiftUI and the design page define it (index.html spring(): x(t) = 1 - e^(-z w t)(cos wd t + (z w / wd) sin wd t), w = 2 pi / response).
    private static double SpringAt(double response, double damping, double t)
    {
        var w = 2 * Math.PI / response; var wd = w * Math.Sqrt(1 - damping * damping);
        return 1 - Math.Exp(-damping * w * t) * (Math.Cos(wd * t) + damping * w / wd * Math.Sin(wd * t));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012 / OD-0143: the geometry and the slide; clicking, the pointer rules and the disabled row are proven in SettingsViewFinishGeneralTests
    public void Switch_is_46_by_26_with_a_22_dome_2_in_that_springs_its_20_points_while_the_lights_cross_over_0_28_seconds()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking");
            Assert.Equal(46, toggle.Bounds.Width, 6); Assert.Equal(26, toggle.Bounds.Height, 6);
            Assert.Equal(Off, toggle.KnobNow()); Assert.Equal(0, toggle.LookNow().Lights); Assert.Equal(1, toggle.LookNow().Scale);
            Assert.Equal(20, SmallSwitch.Travel); Assert.Equal(1.08, SmallSwitch.PressScale);

            // Space commits with no press, so the slide is read alone.
            Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            Assert.Equal(Off, toggle.KnobNow()); Assert.Equal(0, toggle.LookNow().Lights); // nothing has moved before the first frame
            double[] times = [20, 50, 90, 140, 200, 260, 330]; var elapsed = 0.0; var peak = 0.0;
            foreach (var t in times)
            {
                Tick(h, t - elapsed); elapsed = t; var look = toggle.LookNow(); var knob = toggle.KnobNow();
                Assert.Equal(SpringAt(0.38, 0.82, t / 1000), look.Position, 6); // spring(0.38, 0.82), exactly
                Assert.Equal(SmallSwitch.Slide.Progress(t / 1000), look.Position, 9);
                Assert.Equal(2 + 20 * look.Position, knob.X, 6); Assert.Equal(new Size(22, 22), knob.Size); Assert.Equal(2, knob.Y, 6);
                Assert.Equal(SmallSwitch.Light.Progress(t / 1000), look.Lights, 9); // the lights cross over on their own 0.28 s curve
                if (t <= 200) Assert.InRange(look.Lights, 0.01, 0.9999);
                peak = Math.Max(peak, look.Position);
            }
            Assert.True(peak > 1, "the spring carries the knob a little past its end before it settles (damping 0.82)");
            Assert.Equal(1, toggle.LookNow().Lights); // 0.28 s
            Tick(h, 900); Assert.Equal(On, toggle.KnobNow()); Assert.Equal(1, toggle.LookNow().Position); // the right edge is 2 in from the track's: 46 - 2 = 22 + 22

            // And back: the knob returns and the lights go out.
            Press(w, Key.Space); Assert.False(s.ShowReasoning); Tick(h, 100); Assert.InRange(toggle.LookNow().Position, 0.01, 0.99); Assert.InRange(toggle.LookNow().Lights, 0.01, 0.99);
            Tick(h, 1200); Assert.Equal(Off, toggle.KnobNow()); Assert.Equal(0, toggle.LookNow().Lights);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: on and off read without colour (the knob's side, and an empty ring against a lit dot)
    public void The_lume_is_an_empty_ring_when_off_and_a_lit_dot_when_on_readable_without_colour()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking");
            using (var off = NoctaProbe.Snapshot(toggle, 3))
            {
                var centre = NoctaProbe.Luma(NoctaProbe.At(off, 3, 13, 13)); var ring = NoctaProbe.Luma(NoctaProbe.At(off, 3, 13 + 2.1, 13));
                Assert.InRange(centre, 0, 0.2); // the ring is empty: the obsidian shows through it
                Assert.True(ring - centre > 0.12, $"the off lume is a ring brighter than its middle ({ring:F3} against {centre:F3})");
            }
            Assert.False(NoctaProbe.DrawsLitLume(toggle, toggle.LumeColor()));
            s.ShowReasoning = true; Drain(w); Tick(h, 1200);
            using (var on = NoctaProbe.Snapshot(toggle, 3))
            {
                var centre = NoctaProbe.Luma(NoctaProbe.At(on, 3, 33, 13)); var offSide = NoctaProbe.Luma(NoctaProbe.At(on, 3, 13, 13));
                Assert.InRange(centre, 0.85, 1); // the lit dot's white core
                Assert.InRange(offSide, 0, 0.25); // the left of the track holds no knob now
            }
            Assert.True(NoctaProbe.DrawsLitLume(toggle, toggle.LumeColor()));
            // the dome's inner light and the track's aura are the lume colour, faded (0.42 and 0.10 inside the dome; 0.30 in the track)
            var radial = NoctaProbe.Record(toggle).Brushes.OfType<IRadialGradientBrush>().ToArray(); var lume = toggle.LumeColor();
            Assert.Contains(radial, b => b.GradientStops.Count == 2 && b.GradientStops[0].Color == NoctaLook.Faded(lume, .30) && b.GradientStops[1].Offset == .78 && b.GradientStops[1].Color.A == 0);
            Assert.Contains(radial, b => b.GradientStops.Count == 3 && b.GradientStops[0].Color == NoctaLook.Faded(lume, .42) && b.GradientStops[1].Color == NoctaLook.Faded(lume, .10) && b.GradientStops[2].Color.A == 0);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: hover outlines the dome and deepens its shadow; a press scales it 1.08 about its centre and commits only on release
    public void Hover_outlines_and_a_press_scales_the_dome_1_08_about_its_centre_deepening_its_shadow_and_commits_only_on_release()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking"); var at = At(toggle, w);
            using var rest = NoctaProbe.Snapshot(toggle, 3);
            w.MouseMove(at); Drain(w); Assert.True(toggle.IsPointerOver);
            Tick(h, 100); Assert.Equal(SmallSwitch.Light.Progress(0.1), toggle.LookNow().Hover, 9); Assert.Equal(SmallSwitch.Light.Progress(0.1), toggle.LookNow().Deep, 9);
            Tick(h, 200); Assert.Equal(1, toggle.LookNow().Hover); Assert.Equal(1, toggle.LookNow().Deep);
            using (var hovered = NoctaProbe.Snapshot(toggle, 3))
            {
                var edge = NoctaProbe.Luma(NoctaProbe.At(hovered, 3, 13 - 10.6, 13)) - NoctaProbe.Luma(NoctaProbe.At(rest, 3, 13 - 10.6, 13));
                Assert.True(edge > 0.1, $"hover outlines the dome ({edge:F3})");
                var shadow = NoctaProbe.At(hovered, 3, 13, 29.5).Alpha - NoctaProbe.At(rest, 3, 13, 29.5).Alpha;
                Assert.True(shadow > 8, $"hover deepens the shadow under the dome ({shadow})");
            }

            w.MouseDown(at, MouseButton.Left); Drain(w);
            Assert.False(s.ShowReasoning); // the value commits on release, not on the press
            Tick(h, 50); Assert.Equal(1 + 0.08 * SmallSwitch.Press.Progress(0.05), toggle.LookNow().Scale, 9); Assert.Equal(1 + 0.08 * SpringAt(0.22, 0.9, 0.05), toggle.LookNow().Scale, 6);
            Tick(h, 600); var held = toggle.KnobNow();
            Assert.Equal(22 * 1.08, held.Width, 6); Assert.Equal(22 * 1.08, held.Height, 6); Assert.Equal(13, held.Center.X, 6); Assert.Equal(13, held.Center.Y, 6); // about its centre
            Assert.Equal(1, toggle.LookNow().Deep); Assert.False(s.ShowReasoning);
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
            Tick(h, 1200); Assert.Equal(On, toggle.KnobNow()); Assert.Equal(1, toggle.LookNow().Scale);

            // On: a press grows about the new centre too; released away from the switch, nothing changes (PR-SETTINGS-026).
            w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 600); var pressed = toggle.KnobNow(); Assert.Equal(22 * 1.08, pressed.Width, 6); Assert.Equal(33, pressed.Center.X, 6);
            w.MouseMove(new Point(at.X + 200, at.Y)); w.MouseUp(new Point(at.X + 200, at.Y), MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
            Tick(h, 1200); Assert.Equal(On, toggle.KnobNow()); Assert.Equal(0, toggle.LookNow().Hover); Assert.Equal(0, toggle.LookNow().Deep);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: .tg::before (inset -8px -6px) is the zone that hovers and presses the switch, and a press released in it commits
    public void Hover_and_press_reach_6_beyond_the_track_at_either_side_and_8_above_and_below_and_no_further()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking"); var away = At(toggle, w, new Point(-30, 13));
            Assert.Equal(new Rect(-6, -8, 58, 42), NoctaLook.HitZone);
            bool Over(double x, double y)
            {
                w.MouseMove(away); Drain(w); Assert.False(toggle.IsPointerOver);
                w.MouseMove(At(toggle, w, new Point(x, y))); Drain(w); return toggle.IsPointerOver;
            }
            foreach (var (x, y) in new[] { (-5.5, 13.0), (51.5, 13.0), (13.0, -7.5), (13.0, 33.5), (33.0, -7.5), (33.0, 33.5), (-5.5, -7.5), (51.5, 33.5) })
                Assert.True(Over(x, y), $"({x}, {y}) is inside the zone");
            foreach (var (x, y) in new[] { (-7.0, 13.0), (53.0, 13.0), (13.0, -9.0), (13.0, 35.0), (-7.0, -9.0), (53.0, 35.0) })
                Assert.False(Over(x, y), $"({x}, {y}) is outside the zone");

            // At the zone's left edge, 4 off the track: hover outlines and deepens; a press scales the dome and commits on release there.
            var side = At(toggle, w, new Point(-4, 13));
            w.MouseMove(side); Drain(w); Tick(h, 400); Assert.Equal(1, toggle.LookNow().Hover); Assert.Equal(1, toggle.LookNow().Deep);
            w.MouseDown(side, MouseButton.Left); Drain(w); Tick(h, 600); Assert.Equal(1.08, toggle.LookNow().Scale, 6); Assert.False(s.ShowReasoning);
            w.MouseUp(side, MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
            // Pressed on the track and released 7 below it, still in the zone: it commits.
            var centre = At(toggle, w); var below = At(toggle, w, new Point(13, 33));
            w.MouseMove(centre); w.MouseDown(centre, MouseButton.Left); Drain(w); w.MouseMove(below); w.MouseUp(below, MouseButton.Left); Drain(w); Assert.False(s.ShowReasoning);
            // Released 10 below it, out of the zone: nothing changes.
            var out_ = At(toggle, w, new Point(13, 36));
            w.MouseMove(centre); w.MouseDown(centre, MouseButton.Left); Drain(w); w.MouseMove(out_); w.MouseUp(out_, MouseButton.Left); Drain(w); Assert.False(s.ShowReasoning);
            Tick(h, 1200); Assert.Equal(Off, toggle.KnobNow()); Assert.Equal(1, toggle.LookNow().Scale);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: disabled mid-press (a disabled ancestor), the press ends: the dome springs back, the deeper shadow goes out, the release does nothing
    public void A_switch_disabled_while_pressed_ends_the_press_and_its_release_changes_nothing()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var row = Row(view, "Show thinking"); var toggle = Switch(view, "Show thinking"); var at = At(toggle, w);
            w.MouseMove(at); w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 600);
            Assert.Equal(1.08, toggle.LookNow().Scale, 6); Assert.Equal(1, toggle.LookNow().Deep);
            row.IsEnabled = false; Drain(w); Assert.False(toggle.IsEffectivelyEnabled);
            Tick(h, 1200);
            Assert.Equal(1, toggle.LookNow().Scale); Assert.Equal(0, toggle.LookNow().Deep); Assert.Equal(0, toggle.LookNow().Hover); Assert.Equal(Off, toggle.KnobNow());
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.False(s.ShowReasoning);
            row.IsEnabled = true; Drain(w); Tick(h, 1200);
            Assert.Equal(1, toggle.LookNow().Scale); Assert.False(s.ShowReasoning);
            // enabled again, a fresh press and release turns it as before
            w.MouseMove(at); w.MouseDown(at, MouseButton.Left); Drain(w); w.MouseUp(at, MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: .inner, .lumeoff and .lume are each their own layer with their own opacity (.tg i), laid in the design's order
    public void Mid_crossover_the_inner_light_the_off_ring_and_the_lume_each_fade_on_their_own_in_the_designs_order()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking"); Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            Tick(h, 90); var lights = toggle.LookNow().Lights; Assert.InRange(lights, 0.3, 0.9);
            var layers = NoctaProbe.Layers(toggle); var lumeColour = toggle.LumeColor();
            var inner = Assert.Single(layers, l => l.Brush is IRadialGradientBrush b && b.GradientStops.Count == 3 && b.GradientStops[0].Color == NoctaLook.Faded(lumeColour, .42));
            var ring = Assert.Single(layers, l => ReferenceEquals(l.Pen, NoctaLook.LumeOffRing));
            var lume = layers.Where(l => l.Brush is IRadialGradientBrush b && b.GradientStops.Count == 2 && b.GradientStops[0].Color == Colors.White).ToArray();
            Assert.Equal(2, lume.Length); // the dot with its two glows
            Assert.True(inner.Order < ring.Order && ring.Order < lume[0].Order, "dome, inner light, off ring, lume: the design's order");
            Assert.NotSame(inner.Group, lume[0].Group); Assert.NotSame(inner.Group, ring.Group); Assert.Same(lume[0].Group, lume[1].Group);
            Assert.Single(inner.Group.Children); Assert.Equal(2, lume[0].Group.Children.Count); Assert.Single(ring.Group.Children);
            Assert.Equal(lights, inner.Group.Opacity, 9); Assert.Equal(lights, lume[0].Group.Opacity, 9); Assert.Equal(1 - lights, ring.Group.Opacity, 9);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012, OD-0076 with OD-0143: under Reduce Motion the knob and the press jump; the lights cross over in 0.14 s
    public void Under_reduce_motion_the_knob_jumps_and_the_lights_cross_over_in_0_14_seconds()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking"); Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            Assert.Equal(On, toggle.KnobNow()); Assert.Equal(0, toggle.LookNow().Lights); // the knob is there before any frame; the lights have not begun
            Tick(h, 70); Assert.Equal(SmallSwitch.ReducedLight.Progress(0.07), toggle.LookNow().Lights, 9); Assert.InRange(toggle.LookNow().Lights, 0.2, 0.9);
            Tick(h, 80); Assert.Equal(1, toggle.LookNow().Lights); // 0.14 s
            Assert.Equal(0.14, SmallSwitch.ReducedLight.Duration, 9); Assert.Equal(0.28, SmallSwitch.Light.Duration, 9);
            var at = At(toggle, w); w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w);
            Assert.Equal(1.08, toggle.LookNow().Scale); // the press jumps too
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.False(s.ShowReasoning); Assert.Equal(Off, toggle.KnobNow()); Assert.Equal(1, toggle.LookNow().Scale);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: the lume is the user's accent made light, and follows an accent change live
    public void The_lume_is_the_accent_made_light_and_follows_an_accent_change_live()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = true; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking");
            try
            {
                var system = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant);
                Assert.Equal(NoctaLook.Lume(system), toggle.LumeColor()); Assert.True(NoctaProbe.DrawsLitLume(toggle, NoctaLook.Lume(system)));
                s.Theme = AppTheme.Nord; Drain(w); Tick(h, 600);
                var nord = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant); Assert.Equal(SwiftColor.Hex("88C0D0").Resolve(toggle.ActualThemeVariant), nord);
                Assert.Equal(NoctaLook.Lume(nord), toggle.LumeColor()); Assert.NotEqual(NoctaLook.Lume(system), toggle.LumeColor());
                Assert.True(NoctaProbe.DrawsLitLume(toggle, NoctaLook.Lume(nord))); Assert.False(NoctaProbe.DrawsLitLume(toggle, NoctaLook.Lume(system)));
            }
            finally { s.Theme = AppTheme.System; }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the accent brush outlives the switch: a detached switch no longer hears it, and hears it again on attaching
    public void A_detached_switch_stops_listening_to_the_accent_and_takes_it_again_on_attaching()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = true; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking");
            try
            {
                var system = NoctaLook.Lume(Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant));
                Assert.Equal(system, toggle.LumeColor());
                var content = w.Content; w.Content = null; Drain(w); Tick(h, 100);
                s.Theme = AppTheme.Nord; Drain(w); Tick(h, 600);
                Assert.Equal(system, toggle.LumeColor()); // detached: not recoloured
                w.Content = content; Drain(w); Tick(h, 600);
                var nord = NoctaLook.Lume(Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant));
                Assert.NotEqual(system, nord); Assert.Equal(nord, toggle.LumeColor()); // attached again: current
            }
            finally { s.Theme = AppTheme.System; }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: --acc-l = color-mix(in oklab, accent 55%, #fff), the port's Oklab mix (ED-286) to the byte
    public void The_lume_colour_is_the_accent_045_of_the_way_to_white_in_oklab()
    {
        foreach (var hex in new[] { "9C9C9C", "4F9DFF", "FF9B7E", "59D6B9", "948FFF", "F7821B", "0078D7", "000000", "FFFFFF" })
        {
            var accent = SwiftColor.Hex(hex).Resolve(Avalonia.Styling.ThemeVariant.Dark);
            var expected = SwiftColor.Hex(hex).MixPerceptual(SwiftColor.White, 0.45).Resolve(Avalonia.Styling.ThemeVariant.Dark);
            Assert.Equal(expected, NoctaLook.Lume(accent));
        }
        Assert.Equal(Colors.White, NoctaLook.Lume(Colors.White));
        Assert.Equal(Color.FromArgb(51, 1, 2, 3), NoctaLook.Faded(Color.FromRgb(1, 2, 3), .20));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012: Space turns it, Return is left to the default action; the toggle pattern reads and turns it; disabled, nothing does
    public void Space_turns_it_Return_does_not_and_the_toggle_pattern_reads_and_turns_it()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w);
            var toggle = Switch(view, "Show thinking"); var peer = ControlAutomationPeer.CreatePeerForElement(toggle);
            var provider = Assert.IsAssignableFrom<IToggleProvider>(peer); Assert.Equal(AutomationControlType.CheckBox, peer.GetAutomationControlType());
            Assert.Equal(ToggleState.Off, provider.ToggleState);
            Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning); Assert.Equal(ToggleState.On, provider.ToggleState);
            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }; toggle.RaiseEvent(enter);
            Assert.True(s.ShowReasoning); Assert.False(enter.Handled); // Return goes on to the window's default action (TimelineRows.swift:2791)
            Press(w, Key.Space); Assert.False(s.ShowReasoning); Assert.Equal(ToggleState.Off, provider.ToggleState); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            provider.Toggle(); Assert.False(s.ShowReasoning); Assert.Equal(ToggleState.Off, provider.ToggleState); Drain(w); Tick(h, 1200); Assert.Equal(Off, toggle.KnobNow());
            provider.Toggle(); Assert.True(s.ShowReasoning); Drain(w); Tick(h, 1200); Assert.Equal(On, toggle.KnobNow());
            toggle.IsEnabled = false; Drain(w);
            provider.Toggle(); Assert.True(s.ShowReasoning);
            toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Assert.True(s.ShowReasoning);
            toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); Assert.True(s.ShowReasoning);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0143: precomputed brushes and shapes; a frame makes none
    public void A_frame_draws_with_the_same_brushes_and_pens_and_reading_the_state_allocates_nothing()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 600);
            var toggle = Switch(view, "Show thinking"); var at = At(toggle, w);
            w.MouseMove(at); Drain(w); Assert.True(toggle.Focus()); Press(w, Key.Space); // sliding, lights crossing, hover coming up
            Tick(h, 40); var first = NoctaProbe.Record(toggle); var look1 = toggle.LookNow();
            Tick(h, 60); var second = NoctaProbe.Record(toggle); var look2 = toggle.LookNow();
            Assert.NotEqual(look1, look2); Assert.InRange(look2.Lights, 0.01, 0.99);
            Assert.Equal(first.Brushes.Count, second.Brushes.Count); Assert.Equal(first.Pens.Count, second.Pens.Count);
            for (var i = 0; i < first.Brushes.Count; i++) Assert.Same(first.Brushes[i], second.Brushes[i]);
            for (var i = 0; i < first.Pens.Count; i++) Assert.Same(first.Pens[i], second.Pens[i]);
            Tick(h, 1200); var settled = NoctaProbe.Record(toggle);
            Assert.All(settled.Brushes, b => Assert.Contains(b, first.Brushes.Concat(second.Brushes))); // nothing new at rest either
            for (var i = 0; i < 1000; i++) _ = toggle.LookNow();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { _ = toggle.LookNow(); _ = toggle.KnobNow(); _ = toggle.LumeColor(); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        });
    }

    // Not a check of the app: renders the switch in each state at 1x and 3x for the owner's comparison with the design page. It writes to
    // NOCTA_PREVIEW_DIR when that is set (E:\Droppy\work\design\toggles\nocta-app), else to a throwaway folder; it never ships.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Preview", "Nocta")]
    public void Preview_renders_off_on_hover_press_and_focus_at_1x_and_3x()
    {
        var folder = Environment.GetEnvironmentVariable("NOCTA_PREVIEW_DIR") is { Length: > 0 } set ? set : Path.Combine(Path.GetTempPath(), "nocta-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var ground = Color.FromRgb(0x18, 0x18, 0x1A); // the design page's row (.rowbg)
        var written = new List<string>();
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            w.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Drain(w);
            var s = model.Settings; var toggle = Switch(view, "Show thinking"); var at = At(toggle, w); var away = new Point(at.X + 300, at.Y + 300);
            void Shot(string name)
            {
                Tick(h, 1200); Drain(w);
                foreach (var scale in new[] { 1, 3 })
                {
                    using var pixels = NoctaProbe.Snapshot(toggle, scale, ground);
                    var file = Path.Combine(folder, $"nocta-{name}@{scale}x.png");
                    using (var stream = File.Create(file)) pixels.Encode(stream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                    written.Add(file);
                }
            }
            foreach (var on in new[] { false, true })
            {
                var state = on ? "on" : "off";
                w.MouseMove(away); Drain(w); s.ShowReasoning = on; Drain(w); w.FocusManager!.Focus(null); Shot(state);
                w.MouseMove(at); Drain(w); Shot(state + "-hover");
                w.MouseDown(at, MouseButton.Left); Drain(w); Shot(state + "-press");
                w.MouseMove(away); w.MouseUp(away, MouseButton.Left); Drain(w); Assert.Equal(on, s.ShowReasoning);
                w.MouseMove(away); Drain(w); w.FocusManager!.Focus(null); Drain(w); Assert.True(toggle.Focus(NavigationMethod.Tab)); Shot(state + "-focus"); // from no focus, as a Tab arrives
            }
        });
        Assert.Equal(16, written.Count);
        foreach (var file in written) { using var back = SkiaSharp.SKBitmap.Decode(file); Assert.NotNull(back); Assert.True(back.Width >= 62); }
    }
}
