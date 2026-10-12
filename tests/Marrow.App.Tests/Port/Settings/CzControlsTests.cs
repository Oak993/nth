using System.Text.Json;
using Avalonia;
using HostWindow = Avalonia.Controls.Window;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
public sealed class CzControlsTests(HeadlessSession headless)
{
    private void With(Control control, Action<HostWindow> body)
    {
        using var runtime = new TestRuntime();
        using var presenter = Presentation.Presenter.Override(new ReviewOverlayPresenter(Presentation.Presenter.Current));
        var window = new HostWindow { Width = 700, Height = 500, Content = control };
        SettingsProviderPopupLayers.Install(window);
        window.Show(); window.UpdateLayout();
        try { body(window); } finally { window.Close(); }
    }

    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/expected.json")));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T25
    [Trait("Spec", "CZ-T25")]
    public void Segments_measure_the_actual_button_and_arrow_keys_wrap_and_refocus() => headless.OnUiThread(() =>
    {
        var group = new CzSegmentedControl("Shape", [("md", "Large"), ("line", "A very long shape label"), ("sm", "Small")], "md", small: true);
        With(group, w =>
        {
            group.Buttons[0].Focus(NavigationMethod.Tab);
            w.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal("sm", group.SelectedId);
            Assert.True(group.Buttons[2].IsFocused);
            w.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
            w.UpdateLayout();
            Assert.Equal("line", group.SelectedId);
            Assert.Equal(group.Buttons[1].Bounds.Width, group.Pill.Width);
            Assert.Single(group.Buttons, b => b.Focusable);
        });
    });

    [Theory]
    [InlineData(Key.Left, 8)] [InlineData(Key.Down, 8)] [InlineData(Key.Right, 6)] [InlineData(Key.Up, 6)]
    [InlineData(Key.PageDown, 8)] [InlineData(Key.PageUp, 6)] [InlineData(Key.Home, 12)] [InlineData(Key.End, 2)]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T9
    [Trait("Spec", "CZ-T9")]
    public void An_inverted_slider_maps_the_knob_and_every_key_in_the_design_direction(Key key, double expected) => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Speed", 2, 12, 1, 7, inverted: true);
        With(slider, w =>
        {
            Assert.Equal(.5, slider.Fraction);
            slider.Rail.Focus(NavigationMethod.Tab);
            w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(expected, slider.Value);
            Assert.Equal(1 - (expected - 2) / 10, slider.Fraction, 10);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.2
    public void Auto_shows_the_resolved_value_and_pointer_movement_stores_a_snapped_value() => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Corner", 0, 26, 1, null, autoValue: 22);
        With(slider, w =>
        {
            Assert.True(slider.IsAuto); Assert.Equal(22, slider.ShownValue);
            slider.ToggleAuto(); Assert.Equal(22, slider.Value);
            slider.ToggleAuto(); Assert.Null(slider.Value);
            slider.SetFromPointer(9, 150); Assert.Equal(0, slider.Value);
            slider.SetFromPointer(141, 150); Assert.Equal(26, slider.Value);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T13
    [Trait("Spec", "CZ-T13")]
    public void Eight_named_swatches_use_the_four_border_stops_at_the_locked_offsets() => headless.OnUiThread(() =>
    {
        var row = CzSwatchRow.BeamPalettes("colorful");
        Assert.Equal(new[] { "Colourful", "Mono", "Ocean", "Sunset", "Forest", "Candy", "Ice", "Gold" }, row.Buttons.Select(b => AutomationProperties.GetName(b)));
        using var fixture = Fixture();
        var expected = fixture.RootElement.GetProperty("swatches").EnumerateArray().ToArray();
        for (var index = 0; index < row.Buttons.Count; index++)
        {
            var b = row.Buttons[index];
            var gradient = Assert.IsType<LinearGradientBrush>(b.SwatchFill);
            Assert.Equal(new[] { 0.0, .4, .7, 1.0 }, gradient.GradientStops.Select(s => s.Offset));
            Assert.Equal(24, b.Width); Assert.Equal(24, b.Height);
            var colors = expected[index].GetProperty("colors").EnumerateArray().Select(c => Marrow.Core.Models.ComposerLights.CssColor.Parse(c.GetString()!)).ToArray();
            for (var j = 0; j < 4; j++) Assert.Equal(Color.FromArgb((byte)Math.Round(colors[j].A * 255), (byte)colors[j].R, (byte)colors[j].G, (byte)colors[j].B), gradient.GradientStops[j].Color);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T42
    [Trait("Spec", "CZ-T42")]
    public void Dimmed_rows_make_the_slider_Auto_and_radio_controls_inert_and_unfocusable() => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Brightness", .2, 3, .05, null, autoValue: 1);
        slider.Dimmed = true;
        Assert.False(slider.Rail.Focusable); Assert.False(slider.AutoButton.Focusable);
        slider.SetFromPointer(141, 150); slider.ToggleAuto(); Assert.Null(slider.Value);
        slider.Dimmed = false; Assert.True(slider.Rail.Focusable); Assert.True(slider.AutoButton.Focusable);
        var group = new CzSegmentedControl("Motion", [("rotate", "Rotate"), ("pulse", "Pulse")], "rotate");
        group.Dimmed = true; group.Select("pulse"); Assert.Equal("rotate", group.SelectedId);
        Assert.All(group.Buttons, b => Assert.False(b.Focusable));
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T43
    [Trait("Spec", "CZ-T43")]
    public void Control_peers_expose_names_radio_selection_range_toggle_and_expand_collapse() => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Intensity", 0, 1, .01, .5, format: v => Marrow.Core.Port.Settings.CzText.Fmt(v * 100, 0) + "%");
        Assert.Equal("Intensity", AutomationProperties.GetName(slider.Rail));
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider.Rail));
        Assert.Equal((0.0, 1.0, .5), (range.Minimum, range.Maximum, range.Value));
        Assert.Equal("50%", Assert.IsAssignableFrom<IValueProvider>(range).Value);
        Assert.Equal("Automatic Intensity", AutomationProperties.GetName(slider.AutoButton));
        var advanced = new CzAdvancedButton("Advanced", "Colour recipe, glow layers and corner");
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(advanced));
        expand.Expand(); Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
        expand.Collapse(); Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        var group = new CzSegmentedControl("Motion", [("rotate", "Rotate"), ("pulse", "Pulse")], "rotate");
        var radio = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(group.Buttons[1]));
        radio.Select(); Assert.Equal("pulse", group.SelectedId); Assert.True(radio.IsSelected);
        var on = false;
        var sw = new CzSwitch(new Marrow.App.Views.Settings.SettingsSwitch(new Binding<bool>(() => on, v => on = v)), "Follow the composer", "Softer at rest, fuller as you type and while a turn runs");
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(sw));
        toggle.Toggle(); Assert.Equal(ToggleState.On, toggle.ToggleState);
        sw.Dimmed = true; toggle.Toggle(); Assert.True(on);
        Assert.False(((Marrow.App.Views.Settings.SettingsSwitch)sw.Child!).Child!.Focusable);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T92
    [Trait("Spec", "CZ-T92")]
    public void Menu_measures_widest_label_wraps_keys_picks_and_Esc_returns_focus_without_closing_the_window() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Pill words", [("one", "Ready"), ("two", "Listening in the chosen language"), ("three", "Working")], "one");
        With(menu, w =>
        {
            menu.OpenMenu(); w.UpdateLayout();
            Assert.True(menu.IsOpen); Assert.True(menu.Items[0].IsFocused);
            Assert.True(menu.MenuWidth >= 220);
            Assert.Equal(Math.Max(220, Math.Ceiling(menu.WidestLabel) + 48), menu.MenuWidth);
            w.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.True(menu.Items[2].IsFocused);
            w.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null);
            w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal("two", menu.SelectedId); Assert.False(menu.IsOpen); Assert.True(menu.IsFocused);
            menu.OpenMenu(); w.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.True(menu.Items[2].IsFocused);
            w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal("three", menu.SelectedId); Assert.False(menu.IsOpen); Assert.True(menu.IsFocused);
            menu.OpenMenu(); w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.True(menu.Items[0].IsFocused);
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(menu.IsOpen); Assert.True(menu.IsFocused); Assert.True(w.IsVisible);
            menu.OpenMenu(); menu.LightDismiss(); Assert.False(menu.IsOpen);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T92
    [Trait("Spec", "CZ-T92")]
    public void Menu_placement_aligns_by_row_kind_and_flips_above_when_space_is_short() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Spoken language", [("a", "Auto-detect"), ("b", "English")], "a");
        var button = new Rect(100, 100, 150, 28);
        var below = menu.Place(button, new Rect(0, 0, 500, 500), false);
        Assert.Equal(button.Right - menu.MenuWidth, below.X); Assert.Equal(134, below.Y);
        var above = menu.Place(button, new Rect(0, 0, 500, 160), true);
        Assert.Equal(button.Left, above.X); Assert.Equal(button.Top - 6 - above.Height, above.Y);
        Assert.False(Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(menu.Items[1])).IsSelected);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.8
    public void Fold_and_crossfade_use_the_frame_clock_and_reduce_motion_lands_at_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var bound = FrameClock.Source.Override(frames);
        var text = new CzCrossfadeText("Old"); text.SetText("New");
        Assert.Equal(0, text.IncomingOpacity); Assert.Equal(1, text.OutgoingOpacity);
        runtime.AdvanceMilliseconds(50); frames.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, text.IncomingOpacity); Assert.InRange(text.OutgoingOpacity, 0, 1);
        runtime.AdvanceMilliseconds(250); frames.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, text.IncomingOpacity); Assert.Equal(0, text.OutgoingOpacity);
        EnvironmentValues.SetAccessibilityReduceMotion(text, true); text.SetText("Still");
        Assert.Equal(1, text.IncomingOpacity); Assert.Equal(0, text.OutgoingOpacity);
        var fold = new CzFoldRow(new Border { Height = 40 }, false);
        fold.Measure(new Size(300, 100)); fold.Open = true;
        runtime.AdvanceMilliseconds(100); frames.Advance(TimeSpan.FromMilliseconds(100));
        fold.Measure(new Size(300, 100)); Assert.InRange(fold.DesiredSize.Height, .1, 39.99);
        runtime.AdvanceMilliseconds(400); frames.Advance(TimeSpan.FromMilliseconds(400));
        fold.Measure(new Size(300, 100)); Assert.Equal(40, fold.DesiredSize.Height);
    });
    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T43
    [Trait("Spec", "CZ-T43")]
    public void Style_cards_and_orb_tiles_are_named_radio_groups_with_one_tab_stop() => headless.OnUiThread(() =>
    {
        var cards = new CzStyleCards("beam");
        Assert.Equal("Light", AutomationProperties.GetName(cards));
        Assert.Equal(new[] { "Border beam", "Uiverse light", "Lumen", "Fluxa", "Lacus", "Caustica", "Borea", "Quies", "Nebula", "Serica", "Favilla", "Unda", "Argenta", "Vesper", "Neonis", "Crystallum", "Radiantia", "Nacrea", "Aurum", "Stellaris", "Velum", "Lamina", "Fulgor", "Off" }, cards.Buttons.Select(b => AutomationProperties.GetName(b)));
        cards.Select("aura"); Assert.Single(cards.Buttons, b => b.Focusable);
        Assert.Equal("A soft two-tone glow turns around it", AutomationProperties.GetHelpText(cards.Buttons[1]));
        Assert.Equal(3.4, cards.Glyphs[0].Period); Assert.Equal(9, cards.Glyphs[1].Period);
        // Production orb visuals are supplied by the voice renderer; this test supplies bounded fixture controls.
        var orbs = new CzOrbTiles([( "siri", "Siri Wave", (Control)new Border()), ("membrane", "Voice Membrane", (Control)new Border())], "siri");
        Assert.Equal("Orb style", AutomationProperties.GetName(orbs));
        Assert.Equal(96, orbs.Buttons[0].Width); orbs.Select("membrane");
        Assert.True(Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(orbs.Buttons[1])).IsSelected);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T92
    [Trait("Spec", "CZ-T92")]
    public void Menu_value_width_chevron_and_checks_follow_the_frame_clock_and_the_menu_has_no_tip() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Language", [("a", "Auto"), ("b", "A substantially longer language label")], "a");
        With(menu, w =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
            menu.OpenMenu();
            var popup = Assert.Single(((ReviewOverlayPresenter)Presentation.Presenter.Current).Popups);
            Assert.IsNotType<PopoverCard>(popup.Child);
            Assert.Equal(0, menu.MenuOpacity); Assert.Equal(.96, menu.MenuScale);
            runtime.AdvanceMilliseconds(100); frames.Advance(TimeSpan.FromMilliseconds(100));
            Assert.InRange(menu.MenuOpacity, .1, .99); Assert.InRange(menu.Chevron.Angle, 1, 179.9);
            runtime.AdvanceMilliseconds(300); frames.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, menu.MenuOpacity); Assert.Equal(180, menu.Chevron.Angle);
            var oldWidth = menu.ValueWidth; menu.Select("b");
            Assert.Equal(0, menu.ValueLabel.IncomingOpacity); Assert.True(menu.Items[1].Checked);
            Assert.Equal(oldWidth, menu.ValueWidth); Assert.Equal(0, menu.Items[1].Check.Opacity);
            runtime.AdvanceMilliseconds(100); frames.Advance(TimeSpan.FromMilliseconds(100));
            Assert.InRange(menu.Items[1].Check.Opacity, .001, .999);
            Assert.True(menu.ValueWidth > oldWidth); Assert.InRange(menu.ValueLabel.IncomingOpacity, 0.001, .99);
            runtime.AdvanceMilliseconds(300); frames.Advance(TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, menu.Items[1].Check.Opacity);
            Assert.Equal(1, menu.ValueLabel.IncomingOpacity); Assert.Equal(0, menu.Chevron.Angle); Assert.Equal(0, menu.MenuOpacity);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.6
    public void Colour_wells_keep_exact_names_normalize_RGB_and_delegate_to_the_real_picker_boundary() => headless.OnUiThread(() =>
    {
        CzColorWell? opened = null;
        var well = new CzColorWell("First colour", "One side", "#ff5500", w => opened = w);
        Assert.Equal("First colour", AutomationProperties.GetName(well)); well.Activate(); Assert.Same(well, opened);
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(well));
        well.SetColor("#00aaff"); Assert.Equal("#00AAFF", well.Hex);
        well.Dimmed = true; opened = null; well.Activate(); Assert.Null(opened);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.6 with OD-0099 Q-CZ-3 B: the picker stores a custom colour as #RRGGBBAA (red, green, blue, then alpha)
    public void Colour_wells_show_an_eight_digit_colour_as_RGB_then_alpha_not_alpha_first() => headless.OnUiThread(() =>
    {
        var well = new CzColorWell("First colour", "One side", "#ff550080", _ => { });
        Assert.Equal("#FF550080", well.Hex);
        Assert.Equal(Color.FromArgb(0x80, 0xFF, 0x55, 0x00), well.Fill);   // Avalonia's own Color.Parse reads eight digits as AARRGGBB
        well.SetColor("#00aaff40");
        Assert.Equal("#00AAFF40", well.Hex);
        Assert.Equal(Color.FromArgb(0x40, 0x00, 0xAA, 0xFF), well.Fill);
        well.SetColor("#00AAFF");                                            // six digits read as opaque
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xAA, 0xFF), well.Fill);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.8
    public void Visible_dividers_follow_shown_rows_and_never_follow_a_caption()
    {
        Assert.False(CzFoldRow.HasDivider(true, false, false));
        Assert.False(CzFoldRow.HasDivider(false, true, false));
        Assert.False(CzFoldRow.HasDivider(false, false, true));
        Assert.True(CzFoldRow.HasDivider(false, false, false));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.2.5
    public void Pointer_press_jumps_and_capture_keeps_dragging_outside_the_rail() => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Corner", 0, 26, 1, null, autoValue: 22);
        With(slider, w =>
        {
            var start = slider.Rail.TranslatePoint(new Point(9, 11), w)!.Value;
            w.MouseDown(start, MouseButton.Left);
            Assert.Equal(0, slider.Value); Assert.False(slider.IsAuto);
            w.MouseMove(start + new Vector(200, 80));
            Assert.Equal(26, slider.Value);
            w.MouseUp(start + new Vector(200, 80), MouseButton.Left);
            w.MouseMove(start);
            Assert.Equal(26, slider.Value);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-5.2
    public void Style_card_initial_ring_is_present_and_selection_fill_and_ring_ease() => headless.OnUiThread(() =>
    {
        var cards = new CzStyleCards("beam");
        With(cards, w =>
        {
            var first = Assert.IsType<Border>(cards.Buttons[0].Child);
            var next = Assert.IsType<Border>(cards.Buttons[1].Child);
            Assert.Equal(2, first.BorderThickness.Left);
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
            var old = Assert.IsType<SolidColorBrush>(next.Background).Color;
            cards.Select("aura"); Assert.Equal(old, Assert.IsType<SolidColorBrush>(next.Background).Color);
            runtime.AdvanceMilliseconds(100); frames.Advance(TimeSpan.FromMilliseconds(100));
            Assert.InRange(next.BorderThickness.Left, .01, 1.99);
            Assert.NotEqual(old, Assert.IsType<SolidColorBrush>(next.Background).Color);
            runtime.AdvanceMilliseconds(150); frames.Advance(TimeSpan.FromMilliseconds(150));
            Assert.Equal(2, next.BorderThickness.Left); Assert.Equal(0, first.BorderThickness.Left);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T92
    public void Menu_popup_flips_at_the_window_edge_and_an_actual_outside_click_dismisses_it() => headless.OnUiThread(() =>
    {
        var menu = new CzMenuButton("Spoken language", [("a", "Auto-detect"), ("b", "English")], "a")
        { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, StackedRow = true };
        With(menu, w =>
        {
            menu.OpenMenu(); w.UpdateLayout();
            var popup = Assert.Single(((ReviewOverlayPresenter)Presentation.Presenter.Current).Popups);
            Assert.Equal(Avalonia.Controls.PlacementMode.TopEdgeAlignedLeft, popup.Placement);
            Assert.Equal(-6, popup.VerticalOffset);
            w.MouseDown(new Point(690, 10), MouseButton.Left);
            w.MouseUp(new Point(690, 10), MouseButton.Left);
            Assert.False(menu.IsOpen); Assert.True(w.IsVisible);
        });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12.8
    public void Divider_visibility_fades_on_the_frame_clock_without_taking_layout_space_away() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var binding = FrameClock.Source.Override(frames);
        var divider = new CzDivider(true); divider.Shown = false;
        Assert.Equal(1, divider.Opacity); Assert.Equal(1, divider.Height); Assert.Equal(16, divider.Margin.Left);
        runtime.AdvanceMilliseconds(150); frames.Advance(TimeSpan.FromMilliseconds(150));
        Assert.InRange(divider.Opacity, .001, .999);
        runtime.AdvanceMilliseconds(200); frames.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, divider.Opacity);
        EnvironmentValues.SetAccessibilityReduceMotion(divider, true); divider.Shown = true;
        Assert.Equal(1, divider.Opacity);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T43
    [Trait("Spec", "CZ-T43")]
    public void Capsule_and_swatch_peers_expose_invoke_and_required_single_selection() => headless.OnUiThread(() =>
    {
        var capsule = new CzCapsuleButton("Reset to defaults", detail: "Border beam as nth ships it");
        var invoked = false; capsule.Invoked += () => invoked = true;
        var peer = ControlAutomationPeer.CreatePeerForElement(capsule)!;
        Assert.Equal("Reset to defaults", peer.GetName()); Assert.Equal("Border beam as nth ships it", peer.GetHelpText());
        Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke(); Assert.True(invoked);
        capsule.SetAtDefaults(true); invoked = false;
        Assert.IsAssignableFrom<IInvokeProvider>(peer).Invoke(); Assert.False(invoked); Assert.False(capsule.Focusable); Assert.Equal(.42, capsule.Opacity);
        var swatches = CzSwatchRow.BeamPalettes("colorful");
        var selection = Assert.IsAssignableFrom<ISelectionProvider>(ControlAutomationPeer.CreatePeerForElement(swatches));
        Assert.True(selection.IsSelectionRequired); Assert.False(selection.CanSelectMultiple); Assert.Single(selection.GetSelection());
        var option = ControlAutomationPeer.CreatePeerForElement(swatches.Buttons[1])!;
        Assert.Equal(AutomationControlType.RadioButton, option.GetAutomationControlType());
        Assert.IsAssignableFrom<ISelectionItemProvider>(option).Select(); Assert.Equal("mono", swatches.SelectedId);
    });

    [Theory]
    [InlineData(Key.Left, 8.05, 22)] [InlineData(Key.Down, 8.05, 22)]
    [InlineData(Key.Right, 7.95, 21)] [InlineData(Key.Up, 7.95, 21)]
    [InlineData(Key.PageDown, 9.35, 25)] [InlineData(Key.PageUp, 6.65, 18)]
    [InlineData(Key.Home, 14, 40)] [InlineData(Key.End, .5, 3)]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T9
    [Trait("Spec", "CZ-T9")]
    public void Both_shipped_inverted_speed_ranges_snap_every_keyboard_action(Key key, double beamExpected, double auraExpected) => headless.OnUiThread(() =>
    {
        var beam = new CzSlider("Speed", .5, 14, .05, 8, inverted: true);
        var aura = new CzSlider("Speed", 3, 40, .5, 21.5, inverted: true);
        foreach (var (slider, expected) in new[] { (beam, beamExpected), (aura, auraExpected) })
            With(slider, w =>
            {
                slider.Rail.Focus(NavigationMethod.Tab);
                w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.Equal(expected, slider.Value); Assert.Equal(1 - (expected - slider.Minimum) / (slider.Maximum - slider.Minimum), slider.Fraction, 10);
            });
    });

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-12
    public void Controls_resolve_the_actual_light_theme_after_being_attached_to_a_window() => headless.OnUiThread(() =>
    {
        var cards = new CzStyleCards("beam");
        var slider = new CzSlider("Corner", 0, 26, 1, null, autoValue: 22);
        var well = new CzColorWell("First colour", "One side", "#FF5500", _ => { });
        var menu = new CzMenuButton("Language", [("a", "Auto")], "a");
        var line = new StackPanel(); line.Children.Add(cards); line.Children.Add(slider); line.Children.Add(well); line.Children.Add(menu);
        With(line, w =>
        {
            w.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; w.UpdateLayout();
            var shell = Assert.IsType<Border>(cards.Buttons[0].Child);
            var title = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(shell.Child).Children[1]);
            Assert.Equal(Color.FromRgb(28, 28, 30), Assert.IsType<SolidColorBrush>(title.Foreground).Color);
            Assert.Equal(Color.FromArgb(173, 28, 28, 30), Assert.IsType<SolidColorBrush>(slider.ValueText.Foreground).Color);
            var label = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(well.Child).Children[1]);
            Assert.Equal(Color.FromArgb(173, 28, 28, 30), Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            menu.OpenMenu(); w.UpdateLayout();
            Assert.Equal(Color.FromRgb(28, 28, 30), Assert.IsType<SolidColorBrush>(menu.Items[0].Label.Foreground).Color);
        });
    });

}
