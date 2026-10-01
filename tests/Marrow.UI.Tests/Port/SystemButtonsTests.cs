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
            Assert.IsType<Border>(System.Linq.Enumerable.Single(button.GetVisualChildren()));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicks);
            parent.IsEnabled = false; // the button's own IsEnabled stays true
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.4, button.Opacity, 3);
            parent.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, button.Opacity, 3);
        }
        finally { window.Close(); }
    });
}
