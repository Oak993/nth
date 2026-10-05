using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Accessibility;

[Collection("Headless")]
public sealed class A11yFocusAppearanceTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "WL-1")]
    public void Plain_focus_template_preserves_resting_pixels_layout_and_pointer_focus(bool dark, bool capsule) => headless.OnUiThread(() =>
    {
        Border Surface()
        {
            var surface = new Border { Width = 100, Height = 34, Background = Brushes.Gray };
            if (capsule) surface.Capsule(RoundedCornerStyle.Circular);
            else surface.RoundedRectangle(14, RoundedCornerStyle.Continuous);
            return surface;
        }
        var button = PlainControls.Button(Surface(), () => { });
        var window = new Avalonia.Controls.Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border { Padding = new Thickness(20), Child = button }, Width = 240, Height = 140 };
        try
        {
            window.Show();
            var before = A11yReview.Pixels(window);
            var bounds = button.Bounds;
            // Templates are installed before mounting in production. Use a fresh, identical label so the original
            // ContentPresenter never shares its visual child with a replacement template during teardown.
            button = PlainControls.Button(Surface(), () => { }).KeyboardFocus(capsule ? null : 14);
            window.Content = new Border { Padding = new Thickness(20), Child = button };
            Assert.Equal(before, A11yReview.Pixels(window));
            Assert.Equal(bounds, button.Bounds);
            A11yReview.VisibleFocus(window, button);
            window.FocusManager!.Focus(null);
            Assert.Equal(before, A11yReview.Pixels(window));
            Assert.True(button.Focus(NavigationMethod.Pointer));
            Assert.Equal(before, A11yReview.Pixels(window));
            button.IsEnabled = false;
            Assert.False(button.Focus(NavigationMethod.Tab));
            Assert.Equal(before, A11yReview.Pixels(window));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "CZ-4")]
    public void Small_switch_focus_leaves_the_track_and_knob_unchanged_at_rest(bool dark) => headless.OnUiThread(() =>
    {
        var value = false;
        var toggle = new SmallSwitch(new Binding<bool>(() => value, next => value = next));
        var window = new Avalonia.Controls.Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border { Padding = new Thickness(20), Child = toggle }, Width = 180, Height = 100 };
        try
        {
            window.Show();
            var before = A11yReview.Pixels(window);
            var knob = toggle.KnobNow();
            A11yReview.VisibleFocus(window, toggle);
            Assert.Equal(knob, toggle.KnobNow());
            window.FocusManager!.Focus(null);
            Assert.Equal(before, A11yReview.Pixels(window));
            Assert.True(toggle.Focus(NavigationMethod.Pointer));
            Assert.Equal(before, A11yReview.Pixels(window));
            Assert.False(value);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "CZ-4")]
    public void Actual_small_switch_pointer_press_keeps_keyboard_ring_hidden() => headless.OnUiThread(() =>
    {
        using var preference = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var value = false;
        var toggle = new SmallSwitch(new Binding<bool>(() => value, next => value = next));
        var window = new Avalonia.Controls.Window { Content = new Border { Padding = new Thickness(20), Child = toggle }, Width = 180, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var point = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
            window.FocusManager!.Focus(null);
            window.MouseDown(point, MouseButton.Left);
            Assert.Same(toggle, window.FocusManager.GetFocusedElement());
            var held = A11yReview.Pixels(window);
            window.FocusManager.Focus(null);
            Assert.Equal(held, A11yReview.Pixels(window));
        }
        finally
        {
            window.MouseUp(default, MouseButton.Left);
            window.Close();
        }
    });
}
