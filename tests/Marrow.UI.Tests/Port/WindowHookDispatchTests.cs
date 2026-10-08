using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class WindowHookDispatchTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(0x02a2u)]
    [InlineData(0x02a3u)]
    [Trait("Decision", "ED-063")]
    public void A_proxy_leave_filter_keeps_the_caption_hover_before_observers_run(uint leave) => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var window = new Window { Width = 600, Height = 300 };
        window.Content = new Panel { Children = { Marrow.UI.Chrome.WindowChrome.CaptionLayer(window) } };
        Marrow.UI.Chrome.WindowChrome.PlaceTrafficLights(window, false, false);
        window.Show(); window.UpdateLayout();
        try
        {
            var button = Marrow.UI.Chrome.WindowChrome.Buttons(window).Miniaturize;
            var point = button.TranslatePoint(new Avalonia.Point(1, 39), window)!.Value;
            var observed = 0;
            NativeWindowHooks.Add(window, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
            { if (message == leave) { observed++; Marrow.UI.Chrome.WindowChrome.SetCaptionHover(window, 0); } return 0; });
            NativeWindowHooks.AddFirst(window, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
            { if (message == leave && Marrow.UI.Chrome.WindowChrome.CaptionHitTest(window, point) == 8) handled = true; return 0; });
            Marrow.UI.Chrome.WindowChrome.SetCaptionHover(window, 8);
            var handled = false;
            NativeWindowHooks.Dispatch(window, 0, leave, 0, 0, ref handled);
            Assert.True(handled); Assert.True(button.IsHovered); Assert.Equal(0, observed);
            point = new Avalonia.Point(20, 100);
            handled = false;
            NativeWindowHooks.Dispatch(window, 0, leave, 0, 0, ref handled);
            Assert.False(handled); Assert.False(button.IsHovered); Assert.Equal(1, observed);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-063")]
    public void A_later_observer_cannot_erase_a_consumed_caption_hit_test() => headless.OnUiThread(() =>
    {
        var window = new Window(); var observed = 0;
        NativeWindowHooks.Add(window, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
        { if (message != 0x84) return 0; handled = true; return 9; });
        NativeWindowHooks.Add(window, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) => { observed++; return 0; });
        var handled = false;
        Assert.Equal((nint)9, NativeWindowHooks.Dispatch(window, 0, 0x84, 0, 0, ref handled));
        Assert.True(handled); Assert.Equal(0, observed);
        handled = false;
        Assert.Equal((nint)0, NativeWindowHooks.Dispatch(window, 0, 0x214, 0, 0, ref handled));
        Assert.False(handled); Assert.Equal(1, observed);
    });
}
