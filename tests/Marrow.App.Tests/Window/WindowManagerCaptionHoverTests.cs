using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

// OD-0044 (variant N, Turn pulse): the caption hook (WindowManager.HandleNativeWindowMessage, ED-063) drives the caption
// buttons' hover and press from the non-client messages Windows sends for them: WM_NCMOUSEMOVE names the hit-test value
// under the pointer (HTMINBUTTON 8, HTMAXBUTTON 9, HTCLOSE 20, anything else none), WM_NCMOUSELEAVE ends the hover, a
// press holds from WM_NCLBUTTONDOWN to the release. hWnd 0: no Win32 call is made (headless, R-TEST-7).
[Collection("Headless")]
public sealed class WindowManagerCaptionHoverTests(HeadlessSession headless)
{
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_NCMOUSELEAVE = 0x02A2;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;

    [Fact]
    [Trait("Decision", "ED-063")]
    public void Non_client_pointer_messages_hover_and_press_the_caption_buttons() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var launch = new LaunchHarness();
        launch.Harness.Main.OnMain(() => launch.Manager.ShowMain());
        Dispatcher.UIThread.RunJobs();
        var window = launch.Manager.MainWindow!;
        var (close, minimize, maximize) = (WindowChrome.Buttons(window).Close, WindowChrome.Buttons(window).Miniaturize, WindowChrome.Buttons(window).Zoom);
        void Send(uint message, nint hitTest)
        {
            var handled = false;
            launch.Manager.HandleNativeWindowMessage(window, 0, message, hitTest, 0, ref handled);
        }

        Send(WM_NCMOUSEMOVE, 20);
        Assert.True(close.IsHovered);
        Assert.False(minimize.IsHovered);
        Send(WM_NCMOUSEMOVE, 8);
        Assert.False(close.IsHovered);
        Assert.True(minimize.IsHovered);
        Send(WM_NCMOUSEMOVE, 9);
        Assert.True(maximize.IsHovered);
        Send(WM_NCMOUSEMOVE, 2); // HTCAPTION: over the window's drag area, none of the buttons
        Assert.False(maximize.IsHovered);
        Send(WM_NCMOUSEMOVE, 20);
        Send(WM_NCMOUSELEAVE, 0);
        Assert.False(close.IsHovered);

        Send(WM_NCMOUSEMOVE, 8);
        Send(WM_NCLBUTTONDOWN, 8);
        Assert.True(minimize.IsPressed);
        Send(WM_NCMOUSELEAVE, 0); // leaving while held keeps the hover until the release
        Assert.True(minimize.IsHovered);
        Send(WM_NCLBUTTONUP, 8);
        Assert.False(minimize.IsPressed);
    });
}
