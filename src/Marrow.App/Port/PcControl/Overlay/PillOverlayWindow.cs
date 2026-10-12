// PORT-ONLY: Tactus pill v2, the pill's input window on one monitor (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-20; DESIGN.md
// 10.6 "One overlay per monitor"). A band across the top of the monitor's work area: its full width, so horizontal travel
// never moves the window, and the height its state needs, changed with one SetWindowPos at a state change, never per
// frame. It is a TactusOverlayWindow in the Input role (topmost, no taskbar button, no Alt+Tab, never activates on its
// own, excluded from capture, WM_MOUSEACTIVATE answered first) with the extended styles of the chosen hit test: not layered
// for the region test, layered for the per-pixel test, layered and click-through for the style flip. It reports display
// changes (WM_DISPLAYCHANGE, WM_DPICHANGED, WM_SETTINGCHANGE with SPI_SETWORKAREA) so the bands re-read the monitors.
// The pill view (P26) is its content. Never shown by automated tests.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Port.PcControl.Overlay;

public sealed class PillOverlayWindow : TactusOverlayWindow
{
    public const uint WmDisplayChange = 0x007E;
    public const uint WmSettingChange = 0x001A;
    public const uint WmDpiChanged = 0x02E0;
    public const nint SpiSetWorkArea = 0x002F;

    public PillOverlayWindow(IOverlayHitTest hitTest, IOverlayNative? native = null)
        : base(OverlayRole.Input, hitTest.InputStyles, native)
    {
        HitTest = hitTest;
        Title = "Tactus";
        AutomationProperties.SetName(this, "Tactus");
        NativeWindowHooks.Add(this, (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
        {
            if (IsDisplayMessage(message, wp)) DisplayChanged?.Invoke();
            return 0;
        });
    }

    public IOverlayHitTest HitTest { get; }

    /// <summary>The monitors, a DPI or a work area changed.</summary>
    public event Action? DisplayChanged;

    public static bool IsDisplayMessage(uint message, nint wParam) =>
        message is WmDisplayChange or WmDpiChanged || (message == WmSettingChange && wParam == SpiSetWorkArea);
}

/// <summary>The real band calls: SetWindowRgn, the WS_EX_TRANSPARENT flip and SetWindowPos. Never run by automated tests.</summary>
public sealed class Win32OverlayBandNative : IOverlayBandNative
{
    private const int GwlExStyle = -20;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public void SetRegion(nint window, PixelRect? roundRect, int corner)
    {
        var region = roundRect is { } r
            ? CreateRoundRectRgn(r.X, r.Y, r.Right + 1, r.Bottom + 1, corner, corner)
            : CreateRectRgn(0, 0, 0, 0);
        // The window owns the region after a successful call; on failure it is ours to delete.
        if (region != 0 && SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }

    public void SetClickThrough(nint window, bool clickThrough)
    {
        var style = (uint)GetWindowLongPtr(window, GwlExStyle);
        var next = clickThrough ? style | OverlayStyles.Transparent : style & ~OverlayStyles.Transparent;
        if (next != style) SetWindowLongPtr(window, GwlExStyle, (nint)next);
    }

    public void SetBounds(nint window, PixelRect rect) =>
        SetWindowPos(window, 0, rect.X, rect.Y, rect.Width, rect.Height, SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
