using System.Runtime.InteropServices;
using Avalonia.Platform;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

// PORT-ONLY (ENGINEERING DECISION, ED-284; WindowManager.swift:67-69, :82, :43, :46-54): AppKit's NSWindow.frame
// is the window as the user sees it, and a window made with .fullSizeContentView has no title bar above its content, so the
// frame is the content rect (:93-96). A Windows window of the factory's chrome (ED-063: WindowDecorations.Full with the client
// area extended under the title bar) is drawn the same way, but its HWND is larger than what shows: Windows keeps the
// resize border (SM_CXSIZEFRAME + SM_CXPADDEDBORDER at the window's DPI, 8 px at 100 %) as an invisible strip on the left,
// the right and the bottom of the client area and none above it. MEASURED on this PC (WindowManagerNativeTests, real HWNDs,
// Avalonia 12.1.2): outer 896 x 599 for a client of 880 x 591, the same 16 and 8 for the fixed Settings window, and DWM's
// visible bounds one pixel outside the client on the left, right and bottom. Centring a window by its HWND, as Avalonia
// reports it before the window is first shown (the standard overlapped frame, 39 px of caption on top), put the visible
// window 11 to 15 px too high.
internal static class WindowInvisibleBorder
{
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CXPADDEDBORDER = 92;

    /// <summary>The thickness, in pixels, of the invisible strip an HWND keeps on its left, right and bottom at
    /// <paramref name="scaling"/> (the DPI scale it will have): zero for a window that has no HWND.</summary>
    internal static Ambient<Func<AvaloniaWindow, double, int>> Pixels { get; } = new(() => FromSystem);

    private static int FromSystem(AvaloniaWindow window, double scaling)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle() is not { Handle: not 0 } handle || handle.HandleDescriptor != "HWND") return 0;
        var dpi = (uint)Math.Max(1, (int)Math.Round(96 * scaling));
        return GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
