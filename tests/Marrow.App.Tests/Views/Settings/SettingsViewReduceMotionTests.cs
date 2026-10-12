using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// OD-0076: "the system's reduce-motion setting stills every transition". The unit built these on AnimatedScalar (ED-280 (13)): the
// disabled accent glass dim, the window backdrop's scrim glide, the sidebar row's press dim, the picker tile's press dim and the switch's press
// widening and the transparency slider's held knob. (The search clear fade and the settle row dim are held by SettingsViewBlitzReviewTests.) Each test sets the Chrome platform's
// reduce motion and the window's environment explicitly and runs no frame, so none depends on the PC's own Windows "Animation effects" switch,
// and what it reads at once is the stilled value.
[Collection("Headless")]
public sealed class SettingsViewReduceMotionTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static IDisposable ReduceMotion() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-016: the disabled accent glass pill (Marks Edit) takes its 0.4 dim and loses it at once
    public void Under_reduce_motion_the_accent_glass_pill_loses_its_disabled_dim_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            var marks = Assert.Single(Nodes<ProjectMarksRow>(view)); var edit = Assert.Single(Nodes<Button>(marks));
            var surface = Assert.Single(edit.GetVisualDescendants().OfType<Border>(), border => SwiftGlass.GetEffect(border) is not null);
            Assert.False(edit.IsEnabled); Assert.Equal(0.4, surface.Opacity);
            model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("One").Replace('\\', '/'))); Drain(w);
            Assert.True(edit.IsEnabled); Assert.Equal(1, surface.Opacity); // no frame has run
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-018: the transparency reset takes the window backdrop's scrim to stock at once, where without reduce motion it glides over the snappy 0.2 s
    public void Under_reduce_motion_the_reset_takes_the_window_backdrop_to_stock_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            Click(w, Nodes<SidebarRow>(view).Single(row => Text(row).Contains("Appearance"))); var settings = model.Settings; var backdrop = Assert.Single(Nodes<WindowBackdrop>(view));
            var reset = Assert.Single(Nodes<Button>(Assert.Single(Nodes<BackdropOpacitySlider>(view)))); var isDark = PlainControls.IsDark(backdrop.ActualThemeVariant);
            settings.BackdropOpacity = 0.9; Drain(w); SettingsBackdropAssertions.GlassOpacity(backdrop, WindowBackdrop.Scrim(0.9, isDark));
            Click(w, reset); Assert.Equal(0.5, settings.BackdropOpacity);
            SettingsBackdropAssertions.GlassOpacity(backdrop, WindowBackdrop.Scrim(0.5, isDark)); // no frame has run
        }, height: 1100);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-004: a sidebar row held down dims its content to 0.8 and lets go of it at once
    public void Under_reduce_motion_a_pressed_sidebar_row_dims_and_restores_its_content_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            var hydra = RowButton(view, "Hydra"); var content = Assert.IsAssignableFrom<Control>(Assert.Single(Nodes<TextBlock>(hydra)).Parent); var at = hydra.TranslatePoint(new Point(10, 10), w)!.Value;
            w.MouseMove(at); Drain(w); Assert.Equal(1, content.Opacity);
            w.MouseDown(at, MouseButton.Left); Drain(w); Assert.Equal(0.8, content.Opacity, 6); // no frame has run
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.Equal(1, content.Opacity, 6);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-065: a picker tile held down dims its content to 0.8 and lets go of it at once
    public void Under_reduce_motion_a_pressed_picker_tile_dims_and_restores_its_content_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new GeneralSettingsPage(), (view, _, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            var tile = Nodes<ChromeVisualTile>(Row(view, "Done with a thread"))[0]; var content = Assert.Single(Nodes<StackPanel>(tile)); var at = tile.TranslatePoint(new Point(20, 20), w)!.Value;
            w.MouseMove(at); Drain(w); Assert.Equal(1, content.Opacity);
            w.MouseDown(at, MouseButton.Left); Drain(w); Assert.Equal(0.8, content.Opacity, 6); // no frame has run
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.Equal(1, content.Opacity, 6);
        }, height: 2600);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-012 (Nocta, OD-0143): a switch held down is the dome scaled 1.08 at once, and 22 across again on release
    public void Under_reduce_motion_a_pressed_switch_scales_its_dome_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            model.Settings.ShowReasoning = false; Drain(w);
            var toggle = Assert.Single(Nodes<SmallSwitch>(Row(view, "Show thinking"))); var at = At(toggle, w);
            w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w);
            Assert.Equal(new Size(22 * SmallSwitch.PressScale, 22 * SmallSwitch.PressScale), toggle.KnobNow().Size); Assert.Equal(13, toggle.KnobNow().Center.X, 6); // no frame has run
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.True(model.Settings.ShowReasoning);
            Assert.Equal(new Rect(22, 2, 22, 22), toggle.KnobNow());
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // OD-0076 for UI-SETTINGS-018: the transparency slider's knob held is the glass lens at once, and the white capsule again on release
    public void Under_reduce_motion_the_held_slider_knob_is_its_glass_lens_at_once()
    {
        using var motion = ReduceMotion();
        With(() => new AppearanceSettingsPage(), (view, model, h, w) =>
        {
            EnvironmentValues.SetAccessibilityReduceMotion(w, true);
            var slider = Assert.Single(Nodes<Slider>(view)); var knob = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>()); var face = Assert.Single(knob.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius.TopLeft >= 7 && b.BoxShadow.Count > 0);
            Assert.Equal(new Size(18, 14), knob.Bounds.Size);
            var at = At(knob, w); w.MouseMove(at); w.MouseDown(at, MouseButton.Left); w.MouseMove(at + new Vector(10, 0)); Drain(w);
            Assert.Equal(new Size(23, 20), knob.Bounds.Size); Assert.Equal(0x30, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color.A); // no frame has run
            w.MouseUp(at + new Vector(10, 0), MouseButton.Left); Drain(w);
            Assert.Equal(new Size(18, 14), knob.Bounds.Size); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
        });
    }
}
