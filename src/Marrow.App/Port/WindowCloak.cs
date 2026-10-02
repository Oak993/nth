// PORT-ONLY (ED-236, OD-0041). DWM cloaking: a cloaked top-level window is shown (IsVisible, composed by DWM, its
// Acrylic backdrop set up) but not displayed, so the launch can show the main window early and reveal it once its
// backdrop is ready. Probe P14 (porting/probes/p14/RESULT.md): a window shown cloaked and then uncloaked has no frame
// of tint over sharp content, which an ordinary Show has for about 230 ms (P13).
using System.Runtime.InteropServices;
using Avalonia.Platform;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal static class WindowCloak
{
    private const int DwmwaCloak = 13; // DWMWA_CLOAK

    /// <summary>Cloaks or uncloaks <paramref name="window"/>. False when it cannot (no Win32 window yet, as in a
    /// headless run, or DWM refuses), in which case the window is unchanged.</summary>
    internal static bool Set(AvaloniaWindow window, bool cloaked)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (window.TryGetPlatformHandle() is not { Handle: not 0 } handle || handle.HandleDescriptor != "HWND") return false;
        var value = cloaked ? 1 : 0;
        return DwmSetWindowAttribute(handle.Handle, DwmwaCloak, ref value, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
