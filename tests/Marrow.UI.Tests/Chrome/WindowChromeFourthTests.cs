using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/WindowChrome.swift (B3 52bc792), the probe's resize handling (:40-95).
// DEVIATION OD-0044 (OD-0046) and ED-224, tested as the C# records them: the window buttons are Windows caption buttons
// at the top right (their placement only makes sure they exist and are shown), the caption band is set once to the
// titlebar height and is never re-tiled by the platform, and the App publishes the native live-resize start and end
// through WindowLiveResizeSignal, which the probe watches on its current window only.
// PR-WIN-025 (autorecalculatesKeyViewLoop, :121-131) has no test: it is a hang-prevention setting of AppKit's key view
// loop and Avalonia has no property that holds it (its tab order is computed when Tab is processed), so nothing headless
// can tell "ported" from "nothing to port".
[Collection("Headless")]
public sealed class WindowChromeFourthTests(HeadlessSession headless)
{
    private static Window ShownWindow(Control? content = null)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-WIN-029")] // WindowChrome.swift:89-95; ED-224. The end of a live resize places the buttons once, unanimated, and leaves the caption band at 44
    public void When_a_live_resize_ends_the_buttons_are_placed_unanimated_and_the_caption_band_is_the_titlebar_height() => headless.OnUiThread(() =>
    {
        var probe = new WindowChromeProbe();
        var window = ShownWindow(probe);
        try
        {
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            var buttons = new[] { close, miniaturize, zoom };
            WindowLiveResizeSignal.SetIsActive(window, true);
            foreach (var button in buttons) button.IsVisible = false;
            Assert.All(buttons, button => Assert.False(button.IsVisible)); // the start of a live resize places nothing

            WindowLiveResizeSignal.SetIsActive(window, false);

            Assert.All(buttons, button => Assert.True(button.IsVisible)); // :93 the final placement
            Assert.All(buttons, button => Assert.Null(button.Transitions)); // unanimated
            Assert.Equal(WindowChrome.TitlebarHeight, window.ExtendClientAreaTitleBarHeightHint); // the refit (:94): the band is the titlebar height
            Assert.Equal(44, window.ExtendClientAreaTitleBarHeightHint);
            Assert.Equal(WindowChrome.CaptionTrailingInset, Canvas.GetRight(WindowChrome.CaptionCluster(window))); // OD-0044: fixed at the top right
            Assert.Equal(WindowChrome.CaptionTop, Canvas.GetTop(WindowChrome.CaptionCluster(window)));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-029")] // WindowChrome.swift:53-57; ED-224. The observer is the probe's current window's, and moving the probe drops the old one
    public void Only_the_end_of_a_live_resize_of_the_probes_current_window_places_the_buttons() => headless.OnUiThread(() =>
    {
        var probe = new WindowChromeProbe();
        var first = ShownWindow(probe);
        var second = ShownWindow();
        var bystander = ShownWindow();
        try
        {
            var (firstClose, _, _) = WindowChrome.Buttons(first);
            var (secondClose, _, _) = WindowChrome.Buttons(second);
            var (bystanderClose, _, _) = WindowChrome.Buttons(bystander);
            firstClose.IsVisible = false;
            bystanderClose.IsVisible = false;
            WindowLiveResizeSignal.SetIsActive(first, true);
            WindowLiveResizeSignal.SetIsActive(bystander, true);
            WindowLiveResizeSignal.SetIsActive(bystander, false); // some other window's resize ends
            Assert.False(firstClose.IsVisible);
            Assert.False(bystanderClose.IsVisible);

            first.Content = null; // the probe leaves its window ...
            second.Content = probe; // ... and joins another
            secondClose.IsVisible = false;
            WindowLiveResizeSignal.SetIsActive(first, false); // the old window's resize ends: nobody is listening
            Assert.False(firstClose.IsVisible);
            Assert.False(secondClose.IsVisible); // and it did not place the probe's new window either
            WindowLiveResizeSignal.SetIsActive(second, true);
            WindowLiveResizeSignal.SetIsActive(second, false);
            Assert.True(secondClose.IsVisible);
            Assert.False(firstClose.IsVisible);
        }
        finally { first.Close(); second.Close(); bystander.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-028")] // WindowChrome.swift:58-87; OD-0044, ED-224. No platform re-tiling: a burst of frame changes queues no refit and moves nothing
    public void A_burst_of_window_size_changes_leaves_the_caption_band_and_buttons_untouched() => headless.OnUiThread(() =>
    {
        var probe = new WindowChromeProbe();
        var window = ShownWindow(probe);
        try
        {
            var cluster = WindowChrome.CaptionCluster(window);
            var (close, miniaturize, zoom) = WindowChrome.Buttons(window);
            Assert.Equal(44, window.ExtendClientAreaTitleBarHeightHint);
            var changes = new List<string>();
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.ExtendClientAreaTitleBarHeightHintProperty) changes.Add("hint");
            };
            cluster.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty) changes.Add("cluster");
            };
            var tops = new List<double>();

            for (var step = 0; step < 8; step++)
            {
                window.Width = 420 + step * 10;
                window.Height = 310 + step * 10;
                window.UpdateLayout();
                tops.Add(Canvas.GetTop(cluster));
            }

            Assert.Empty(changes); // nothing was refitted or re-placed
            Assert.Equal(44, window.ExtendClientAreaTitleBarHeightHint);
            Assert.All(tops, top => Assert.Equal(WindowChrome.CaptionTop, top));
            Assert.Equal(WindowChrome.CaptionTrailingInset, Canvas.GetRight(cluster));
            var (sameClose, sameMiniaturize, sameZoom) = WindowChrome.Buttons(window);
            Assert.Same(close, sameClose);
            Assert.Same(miniaturize, sameMiniaturize);
            Assert.Same(zoom, sameZoom);
        }
        finally { window.Close(); }
    });
}
