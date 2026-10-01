using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PortControlThemes.SliderTheme (PORT-ONLY, proposal ED-229 (2)): the slider follows the Slider's own direction settings,
// dims when disabled and rings its knob under keyboard focus.
[Collection("Headless")]
public sealed class SliderThemeTests(HeadlessSession headless)
{
    private static (Window Window, Slider Slider, StackPanel Parent) Host(Slider slider)
    {
        var parent = new StackPanel { Children = { new Button { Content = "Before" }, slider } };
        var window = new Window { Content = parent, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, slider, parent);
    }

    private static Panel Root(Slider slider) => (Panel)slider.GetVisualChildren().Single();

    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned control templates (no theme package)
    public void A_slider_disabled_by_its_parent_dims_and_comes_back() => headless.OnUiThread(() =>
    {
        var (window, slider, parent) = Host(new Slider { Width = 200, Minimum = 0, Maximum = 1, Value = 0.5 });
        try
        {
            Assert.Equal(1, Root(slider).Opacity, 3);
            parent.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.4, Root(slider).Opacity, 3);
            parent.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Root(slider).Opacity, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Keyboard_focus_rings_the_knob() => headless.OnUiThread(() =>
    {
        var (window, slider, parent) = Host(new Slider { Width = 200, Minimum = 0, Maximum = 1, Value = 0.5 });
        try
        {
            var knob = slider.GetVisualDescendants().OfType<Thumb>().Single();
            var ring = knob.GetVisualDescendants().OfType<Border>().Last();
            Assert.False(ring.IsVisible);
            ((Button)parent.Children[0]).Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(slider.IsFocused, "Tab moves focus to the slider");
            Assert.True(ring.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_reversed_slider_fills_from_the_right() => headless.OnUiThread(() =>
    {
        var (window, slider, _) = Host(new Slider { Width = 200, Minimum = 0, Maximum = 100, Value = 25, IsDirectionReversed = true });
        try
        {
            var filled = slider.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_DecreaseButton");
            var empty = slider.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_IncreaseButton");
            Assert.True(filled.Bounds.X > empty.Bounds.X, $"filled {filled.Bounds}, empty {empty.Bounds}");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_vertical_slider_stands_its_track_and_knob_on_end() => headless.OnUiThread(() =>
    {
        var (window, slider, _) = Host(new Slider { Height = 150, Minimum = 0, Maximum = 1, Value = 0.5, Orientation = Avalonia.Layout.Orientation.Vertical });
        try
        {
            var knob = slider.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.Equal(new Size(14, 18), knob.Bounds.Size);
            var bar = slider.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_DecreaseButton")
                .GetVisualDescendants().OfType<Border>().Single();
            Assert.Equal(4, bar.Bounds.Width);
            Assert.True(bar.Bounds.Height > 4);
        }
        finally { window.Close(); }
    });
}
