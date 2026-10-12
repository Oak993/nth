using Avalonia;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Shell;

/// <summary>What the message hook needs from the window (the real window, or a test's stand-in).</summary>
public interface ICaptionSurface
{
    /// <summary>A screen point in physical pixels, in the window's client space in DIP.</summary>
    Point ScreenToClient(PixelPoint screen);

    /// <summary>A client point in physical pixels (a mouse message's lParam), in DIP.</summary>
    Point ClientPixelsToClient(PixelPoint client);

    double ClientWidth { get; }

    bool ClosePaused { get; }

    void SetCaptionHover(int hit);

    void SetCaptionPressed(int hit);

    /// <summary>A try to close while Close is paused (the line brightens).</summary>
    void CloseRefused();

    /// <summary>Windows' settings may have changed: read them again.</summary>
    void SettingsMayHaveChanged();
}

/// <summary>The few Win32 calls the hook makes, so its logic can be tested without a window.</summary>
public interface ICaptionCalls
{
    void PostSystemCommand(nint window, int command);

    /// <summary>Asks for WM_NCMOUSELEAVE once the pointer leaves the non-client area. True when asked.</summary>
    bool TrackNonClientLeave(nint window);

    void Capture(nint window);

    void ReleaseCapture();
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Custom caption" and "Close"; unit U1, ED-NEW-inst-u1-1). The setup window's message
/// hook. It answers WM_NCHITTEST by hand (<see cref="CaptionHitTest"/>), draws the caption buttons' hover and press from
/// the non-client mouse messages, and performs a click on release over the same button by posting WM_SYSCOMMAND
/// (SC_MINIMIZE or SC_CLOSE), so a Close click goes the same way as Alt+F4, the taskbar and the system menu. Maximise never
/// happens (SC_MAXIMIZE and a caption double-click are swallowed: Aero Snap and the snap layout stay off). While Close is
/// paused, SC_CLOSE and WM_QUERYENDSESSION are refused (sign-out and shutdown wait, with the reason set by the guard) and
/// the Close-paused line brightens. Settings messages make the window read Windows' settings again.
/// </summary>
public sealed class SetupWindowHooks(ICaptionSurface surface, ICaptionCalls calls)
{
    private int pressed;
    private bool trackingLeave;

    public int Pressed => pressed;

    public nint WndProc(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case Native.WM_NCHITTEST:
            {
                var hit = CaptionHitTest.At(surface.ScreenToClient(Point(lParam)), surface.ClientWidth);
                handled = true;
                return hit;
            }
            case Native.WM_NCMOUSEMOVE:
            {
                var hit = (int)wParam;
                if (pressed == 0) surface.SetCaptionHover(CaptionHitTest.IsButton(hit) ? hit : 0);
                if (!trackingLeave) trackingLeave = calls.TrackNonClientLeave(window);
                return 0;
            }
            case Native.WM_NCMOUSELEAVE:
                trackingLeave = false;
                if (pressed == 0) surface.SetCaptionHover(0);
                return 0;
            case Native.WM_NCLBUTTONDOWN when CaptionHitTest.IsButton((int)wParam):
                pressed = (int)wParam;
                surface.SetCaptionPressed(pressed);
                surface.SetCaptionHover(pressed);
                calls.Capture(window);
                handled = true;
                return 0;
            case Native.WM_NCLBUTTONDBLCLK when (int)wParam == Native.HTCAPTION || CaptionHitTest.IsButton((int)wParam):
                handled = true; // no maximise from the caption, no second click on a button
                return 0;
            case Native.WM_MOUSEMOVE when pressed != 0:
            {
                var over = CaptionHitTest.At(surface.ClientPixelsToClient(Point(lParam)), surface.ClientWidth);
                surface.SetCaptionHover(over == pressed ? pressed : 0);
                surface.SetCaptionPressed(over == pressed ? pressed : 0);
                handled = true;
                return 0;
            }
            case Native.WM_LBUTTONUP when pressed != 0:
                Release(window, CaptionHitTest.At(surface.ClientPixelsToClient(Point(lParam)), surface.ClientWidth));
                handled = true;
                return 0;
            case Native.WM_NCLBUTTONUP when pressed != 0:
                Release(window, (int)wParam);
                handled = true;
                return 0;
            case Native.WM_CAPTURECHANGED when pressed != 0 && lParam != window:
                pressed = 0;
                surface.SetCaptionPressed(0);
                surface.SetCaptionHover(0);
                return 0;
            case Native.WM_SYSCOMMAND:
            {
                var command = (int)(wParam & 0xFFF0);
                if (command == Native.SC_MAXIMIZE)
                {
                    handled = true;
                    return 0;
                }
                if (command == Native.SC_CLOSE && surface.ClosePaused)
                {
                    surface.CloseRefused();
                    handled = true;
                    return 0;
                }
                return 0;
            }
            case Native.WM_QUERYENDSESSION when surface.ClosePaused:
                surface.CloseRefused();
                handled = true;
                return 0; // FALSE: sign-out and shutdown wait while nth is put in place
            default:
                if (SystemSettings.IsSettingMessage(message)) surface.SettingsMayHaveChanged();
                return 0;
        }
    }

    private void Release(nint window, int over)
    {
        var was = pressed;
        pressed = 0;
        calls.ReleaseCapture();
        surface.SetCaptionPressed(0);
        surface.SetCaptionHover(over == was ? was : 0);
        if (over != was) return;
        calls.PostSystemCommand(window, was == Native.HTCLOSE ? Native.SC_CLOSE : Native.SC_MINIMIZE);
    }

    /// <summary>A mouse message's point: signed 16-bit x and y.</summary>
    public static PixelPoint Point(nint lParam)
    {
        var value = (long)lParam;
        return new PixelPoint(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    /// <summary>The lParam Windows would send for a point.</summary>
    public static nint Pack(int x, int y) => (nint)((unchecked((ushort)(short)y) << 16) | unchecked((ushort)(short)x));
}

/// <summary>The real calls.</summary>
public sealed class WindowsCaptionCalls : ICaptionCalls
{
    public void PostSystemCommand(nint window, int command) => _ = Native.PostMessage(window, Native.WM_SYSCOMMAND, command, 0);

    public unsafe bool TrackNonClientLeave(nint window)
    {
        var track = new Native.TrackMouseEventInfo { Size = (uint)sizeof(Native.TrackMouseEventInfo), Flags = Native.TME_LEAVE | Native.TME_NONCLIENT, Window = window };
        return Native.TrackMouseEvent(ref track);
    }

    public void Capture(nint window) => _ = Native.SetCapture(window);

    public void ReleaseCapture() => _ = Native.ReleaseCapture();
}
