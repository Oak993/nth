// PORT-ONLY: Tactus, the no-activate overlay host (OD-0144, OD-0145, ED-NEW-pcctl-20; DESIGN.md 3.1, 4.1, 4.3, 10.4 "It
// never steals focus", 10.6). Every Tactus surface (the pill and its card, their shadow band, the Tactus pointers) is an
// Avalonia window that is topmost, has no taskbar button and no Alt+Tab entry (WS_EX_TOOLWINDOW), never activates on its
// own (WS_EX_NOACTIVATE, shown with ShowActivated false) and is excluded from capture (WDA_EXCLUDEFROMCAPTURE), so the
// model never sees it and the user's typing never reaches it. Windows that only draw (the shadow band, the pointers) are
// also layered and click-through. The input window answers WM_MOUSEACTIVATE with MA_NOACTIVATE everywhere except on the
// composer's field (OverlayActivation), so a button fires without activation and only a click into the field gives the
// card the keyboard; that click is the user's own input, so the foreground-lock rules allow it. The topmost order is set
// again only when asked (the pill shows, an ask arrives, another topmost window covers it), never per frame. The Win32
// calls sit behind IOverlayNative so the rules are tested on a fake; no test creates or styles a real window.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>What a Tactus overlay window is for.</summary>
public enum OverlayRole
{
    /// <summary>The pill and card's input window: takes clicks where its hit test lets it, never activates on its own.</summary>
    Input,

    /// <summary>The click-through band beside it that draws the shadow and rubber-band room: never takes input.</summary>
    Shadow,

    /// <summary>A Tactus pointer: draws only, never takes input.</summary>
    Pointer,
}

/// <summary>The Win32 constants and rules of a Tactus overlay window.</summary>
public static class OverlayStyles
{
    public const uint Topmost = 0x00000008;
    public const uint Transparent = 0x00000020;
    public const uint ToolWindow = 0x00000080;
    public const uint Layered = 0x00080000;
    public const uint NoActivate = 0x08000000;

    /// <summary>WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 and later).</summary>
    public const uint ExcludeFromCapture = 0x00000011;

    public const uint MouseActivateMessage = 0x0021;
    public const nint MaActivate = 1;
    public const nint MaNoActivate = 3;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;

    /// <summary>The topmost re-assert: HWND_TOPMOST without moving, sizing or activating.</summary>
    public const uint TopmostFlags = SwpNoSize | SwpNoMove | SwpNoActivate;

    public static readonly nint HwndTopmost = -1;

    /// <summary>The extended styles a role adds to its window.</summary>
    public static uint Extended(OverlayRole role) => role switch
    {
        OverlayRole.Input => Topmost | ToolWindow | NoActivate,
        _ => Topmost | ToolWindow | NoActivate | Layered | Transparent,
    };

    /// <summary>WM_MOUSEACTIVATE's answer: activate only for a click on the composer's field; the click arrives either way.</summary>
    public static nint MouseActivate(bool onActivatingField) => onActivatingField ? MaActivate : MaNoActivate;
}

/// <summary>The Win32 calls a Tactus overlay makes on its own window. Implementations other than fakes never run in tests.</summary>
public interface IOverlayNative
{
    uint ExtendedStyle(nint window);

    void SetExtendedStyle(nint window, uint style);

    /// <summary>Hides the window from every capture; false when Windows refused (older than 2004, or no composition).</summary>
    bool ExcludeFromCapture(nint window);

    /// <summary>Puts the window on top of the topmost band without moving, sizing or activating it.</summary>
    void BringToTop(nint window);

    /// <summary>The cursor in the window's client area, physical pixels; null when unknown.</summary>
    PixelPoint? CursorInClient(nint window);
}

/// <summary>What setting a window up gave.</summary>
public sealed record OverlaySetup(bool HiddenFromCapture);

public static class OverlayWindowSetup
{
    /// <summary>Adds the role's styles to the window, hides it from capture and puts it on top. Nothing activates it.</summary>
    public static OverlaySetup Apply(nint window, OverlayRole role, IOverlayNative native) => Apply(window, OverlayStyles.Extended(role), native);

    /// <summary>Adds <paramref name="extended"/> (a role's styles, or the input window's for a hit-test mode), hides the window from capture and puts it on top.</summary>
    public static OverlaySetup Apply(nint window, uint extended, IOverlayNative native)
    {
        native.SetExtendedStyle(window, native.ExtendedStyle(window) | extended);
        var hidden = native.ExcludeFromCapture(window);
        native.BringToTop(window);
        return new OverlaySetup(hidden);
    }

    /// <summary>
    /// The input window's WM_MOUSEACTIVATE handler: handled, with MA_ACTIVATE only when the cursor (in DIP) is on an
    /// activating field. Other messages pass on.
    /// </summary>
    public static nint OnMessage(uint message, Func<Point?> cursorDip, Func<Point, bool> activates, ref bool handled)
    {
        if (message != OverlayStyles.MouseActivateMessage) return 0;
        handled = true;
        return OverlayStyles.MouseActivate(cursorDip() is { } point && activates(point));
    }
}

/// <summary>
/// A Tactus overlay window: borderless, transparent, topmost, no taskbar button, shown without activation, its extended
/// styles set at creation and its capture exclusion set once it has a handle. The pill (P26) and the pointers (P14) put
/// their content in it.
/// </summary>
public class TactusOverlayWindow : Avalonia.Controls.Window
{
    private readonly IOverlayNative native;

    public TactusOverlayWindow(OverlayRole role, IOverlayNative? native = null)
        : this(role, OverlayStyles.Extended(role), native)
    {
    }

    /// <summary>A window of <paramref name="role"/> with <paramref name="extended"/> styles (the input window in a hit-test mode).</summary>
    public TactusOverlayWindow(OverlayRole role, uint extended, IOverlayNative? native = null)
    {
        Role = role;
        ExtendedStyles = extended;
        this.native = native ?? new Win32OverlayNative();
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Background = null;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Win32Properties.AddWindowStylesCallback(this, (style, styles) => (style, styles | extended));
        if (role == OverlayRole.Input)
            NativeWindowHooks.AddFirst(this, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
            {
                var result = OverlayWindowSetup.OnMessage(message, () => CursorDip(hwnd), point => OverlayActivation.Hit(this, point), ref handled);
                if (handled && result == OverlayStyles.MaActivate) Activating?.Invoke();
                return result;
            });
    }

    public OverlayRole Role { get; }

    /// <summary>
    /// A click on an activating field was answered with MA_ACTIVATE, before Windows activates this window: the card saves
    /// the window the keyboard comes from (CardFocus.Activating, ED-NEW-pcctl-26).
    /// </summary>
    public event Action? Activating;

    /// <summary>The extended styles this window adds at creation and once it has a handle.</summary>
    public uint ExtendedStyles { get; }

    /// <summary>The window's handle once it is open; 0 before.</summary>
    public nint Handle => TryGetPlatformHandle()?.Handle ?? 0;

    /// <summary>False when Windows refused to hide this window from capture (it is then never shown over a capture target).</summary>
    public bool HiddenFromCapture { get; private set; }

    /// <summary>Puts the window on top again (the pill shows, an ask arrives, another topmost window covers it).</summary>
    public void Reassert()
    {
        if (TryGetPlatformHandle()?.Handle is { } handle && handle != 0) native.BringToTop(handle);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
            HiddenFromCapture = OverlayWindowSetup.Apply(handle, ExtendedStyles, native).HiddenFromCapture;
    }

    private Point? CursorDip(nint hwnd) =>
        native.CursorInClient(hwnd) is { } pixels ? new Point(pixels.X / RenderScaling, pixels.Y / RenderScaling) : null;
}

/// <summary>The real Win32 calls. Never run by automated tests.</summary>
public sealed class Win32OverlayNative : IOverlayNative
{
    private const int GwlExStyle = -20;

    public uint ExtendedStyle(nint window) => unchecked((uint)GetWindowLongPtr(window, GwlExStyle));

    public void SetExtendedStyle(nint window, uint style) => SetWindowLongPtr(window, GwlExStyle, (nint)style);

    public bool ExcludeFromCapture(nint window) => SetWindowDisplayAffinity(window, OverlayStyles.ExcludeFromCapture);

    public void BringToTop(nint window) => SetWindowPos(window, OverlayStyles.HwndTopmost, 0, 0, 0, 0, OverlayStyles.TopmostFlags);

    public PixelPoint? CursorInClient(nint window)
    {
        if (!GetCursorPos(out var point) || !ScreenToClient(window, ref point)) return null;
        return new PixelPoint(point.X, point.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
}
