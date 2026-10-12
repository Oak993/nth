// PORT-ONLY: where a click may activate the no-activate overlay (OD-0144, OD-0145, ED-NEW-pcctl-20; DESIGN.md 10.4 "It
// never steals focus"): only on the field marked Activates (the mini composer's) or a part inside it; never on a button,
// on empty card, or on a disabled or hidden field.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-20")]
public sealed class OverlayActivationTests(HeadlessSession headless)
{
    [Fact]
    public void Only_a_click_on_the_marked_field_activates_the_overlay() => headless.OnUiThread(() =>
    {
        var (window, field, button) = Card();
        try
        {
            Assert.True(OverlayActivation.Hit(window, new Point(60, 30)));
            Assert.Same(button, window.InputHitTest(new Point(260, 30)));
            Assert.False(OverlayActivation.Hit(window, new Point(260, 30)));
            Assert.False(OverlayActivation.Hit(window, new Point(150, 150)));

            field.IsEnabled = false;
            Settle();
            Assert.False(OverlayActivation.Hit(window, new Point(60, 30)));

            field.IsEnabled = true;
            field.IsVisible = false;
            Settle();
            Assert.False(OverlayActivation.Hit(window, new Point(60, 30)));
            Assert.False(OverlayActivation.GetActivates(button));
        }
        finally
        {
            window.Close();
        }
    });

    // Hit testing reads the composed scene, so the frame is rendered first.
    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // The field stands in for the composer's text box (a part inside it is hit first); the button for Send.
    private static (Window Window, Border Field, Border Button) Card()
    {
        var field = new Border { Width = 200, Height = 40, Background = Brushes.White, Child = new Border { Margin = new Thickness(8), Background = Brushes.Gray } };
        OverlayActivation.SetActivates(field, true);
        var button = new Border { Width = 80, Height = 40, Background = Brushes.Gray };
        Canvas.SetLeft(field, 0);
        Canvas.SetTop(field, 10);
        Canvas.SetLeft(button, 220);
        Canvas.SetTop(button, 10);
        var window = new Window { Width = 320, Height = 200, Content = new Canvas { Background = Brushes.Transparent, Children = { field, button } } };
        window.Show();
        Settle();
        return (window, field, button);
    }
}
