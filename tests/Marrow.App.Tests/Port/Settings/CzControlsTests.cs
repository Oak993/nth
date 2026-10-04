using Avalonia;
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
    private void With(Control control, Action<Window> body)
    {
        using var runtime = new TestRuntime();
        var window = new Window { Width = 700, Height = 500, Content = control };
        window.Show(); window.UpdateLayout();
        try { body(window); } finally { window.Close(); }
    }

    [Fact]
    [Trait("Rule", "CZ-T25")]
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
            Assert.Single(group.Buttons.Where(b => b.Focusable));
        });
    });

    [Theory]
    [InlineData(Key.Left, 8)] [InlineData(Key.Down, 8)] [InlineData(Key.Right, 6)] [InlineData(Key.Up, 6)]
    [InlineData(Key.PageDown, 8)] [InlineData(Key.PageUp, 6)] [InlineData(Key.Home, 12)] [InlineData(Key.End, 2)]
    [Trait("Rule", "CZ-T9")]
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
    [Trait("Rule", "CZ-12.2")]
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
    [Trait("Rule", "CZ-T13")]
    public void Eight_named_swatches_use_the_four_border_stops_at_the_locked_offsets() => headless.OnUiThread(() =>
    {
        var row = CzSwatchRow.BeamPalettes("colorful");
        Assert.Equal(new[] { "Colourful", "Mono", "Ocean", "Sunset", "Forest", "Candy", "Ice", "Gold" }, row.Buttons.Select(b => AutomationProperties.GetName(b)));
        foreach (var b in row.Buttons)
        {
            var gradient = Assert.IsType<LinearGradientBrush>(b.SwatchFill);
            Assert.Equal(new[] { 0.0, .4, .7, 1.0 }, gradient.GradientStops.Select(s => s.Offset));
            Assert.Equal(24, b.Width); Assert.Equal(24, b.Height);
        }
    });

    [Fact]
    [Trait("Rule", "CZ-T42")]
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
    [Trait("Rule", "CZ-T43")]
    public void Control_peers_expose_names_radio_selection_range_toggle_and_expand_collapse() => headless.OnUiThread(() =>
    {
        var slider = new CzSlider("Intensity", 0, 1, .01, .5, format: v => Marrow.Core.Port.Settings.CzText.Fmt(v * 100, 0) + "%");
        Assert.Equal("Intensity", AutomationProperties.GetName(slider.Rail));
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider.Rail));
        Assert.Equal((0.0, 1.0, .5), (range.Minimum, range.Maximum, range.Value));
        var advanced = new CzAdvancedButton("Advanced", "Colour recipe, glow layers and corner");
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(advanced));
        expand.Expand(); Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
        expand.Collapse(); Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        var group = new CzSegmentedControl("Motion", [("rotate", "Rotate"), ("pulse", "Pulse")], "rotate");
        var radio = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(group.Buttons[1]));
        radio.Select(); Assert.Equal("pulse", group.SelectedId); Assert.True(radio.IsSelected);
        var sw = new CzSwitch("Follow the composer", false, "Softer at rest, fuller as you type and while a turn runs");
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(sw));
        toggle.Toggle(); Assert.Equal(ToggleState.On, toggle.ToggleState);
    });

    [Fact]
    [Trait("Rule", "CZ-T92")]
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
            menu.OpenMenu(); w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(menu.IsOpen); Assert.True(menu.IsFocused); Assert.True(w.IsVisible);
            menu.OpenMenu(); menu.LightDismiss(); Assert.False(menu.IsOpen);
        });
    });

    [Fact]
    [Trait("Rule", "CZ-T92")]
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
    [Trait("Rule", "CZ-12.8")]
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
}
