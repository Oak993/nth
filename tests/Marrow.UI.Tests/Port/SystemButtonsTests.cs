using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// SystemButtons (PORT-ONLY, proposal ED-229 (4)): .bordered / .borderedProminent buttons.
[Collection("Headless")]
public sealed class SystemButtonsTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned control templates (no theme package)
    public void A_bordered_button_is_drawn_clicks_and_dims_when_its_parent_is_disabled() => headless.OnUiThread(() =>
    {
        var clicks = 0;
        var button = SystemButtons.Bordered("Cancel", () => clicks++);
        var parent = new StackPanel { Children = { button } };
        var window = new Window { Content = parent, Width = 300, Height = 100 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            var card = button.GetVisualDescendants().OfType<Border>().First();
            var ring = button.GetVisualDescendants().OfType<Border>().Last();
            Assert.False(ring.IsVisible); // no focus ring until keyboard focus
            var resting = card.Background;
            window.MouseMove(button.TranslatePoint(new Avalonia.Point(5, 5), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(resting, card.Background); // a lighter fill under the pointer
            window.MouseMove(new Avalonia.Point(290, 90));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(resting, card.Background);
            window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused && ring.IsVisible, "keyboard focus shows the ring");
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicks);
            var drawn = (Panel)card.GetVisualParent()!; // the template's root: the card and the ring
            button.Opacity = 0.5; // a site's own opacity (a fading row) is kept through the dim
            parent.IsEnabled = false; // the button's own IsEnabled stays true
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.4, drawn.Opacity, 3);
            Assert.Equal(0.5, button.Opacity, 3);
            parent.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, drawn.Opacity, 3);
            Assert.Equal(0.5, button.Opacity, 3);
        }
        finally { window.Close(); }
    });
}
