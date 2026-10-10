using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

[Collection("Headless")]
public sealed class CaptionHitRegionRegressionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-063")]
    public void Every_caption_box_takes_priority_over_the_drag_background() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 600, Height = 300 };
        window.Content = new Panel { Children = { new WindowDragArea(), WindowChrome.CaptionLayer(window) } };
        WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        try
        {
            var buttons = WindowChrome.Buttons(window);
            foreach (var (button, hit) in new[] { (buttons.Miniaturize, 8), (buttons.Zoom, 9), (buttons.Close, 20) })
                foreach (var local in new[] { new Point(1, 1), new Point(45, 1), new Point(1, 39), new Point(45, 39), new Point(23, 20) })
                    Assert.Equal(hit, WindowChrome.CaptionHitTest(window, button.TranslatePoint(local, window)!.Value));
            window.CanResize = false;
            Assert.Equal(1, WindowChrome.CaptionHitTest(window, buttons.Zoom.TranslatePoint(new Point(1, 1), window)!.Value));
            Assert.Equal(0, WindowChrome.CaptionHitTest(window, new Point(20, 20)));
        }
        finally { window.Close(); }
    });
}
