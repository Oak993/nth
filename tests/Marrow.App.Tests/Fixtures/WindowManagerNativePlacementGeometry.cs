using System.ComponentModel;
using System.Runtime.InteropServices;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Expected coordinates consume only native readings.
// GetWindowRect includes the invisible resize border. Avalonia 12.1.2 WindowImpl.cs:253-271,
// :523-539 uses it for FrameSize/Position and GetClientRect for ClientSize. DWM visible bounds are
// diagnostic only and must not silently replace the frame whose placement is being verified.
internal static class WindowManagerNativePlacementGeometry
{
    internal static void RequirePerMonitorThread()
    {
        var awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        if (awareness != 2)
            throw new InvalidOperationException("NATIVE_PRECONDITION: native geometry requires the existing per-monitor-aware STA, without DPI virtualization.");
    }

    internal static NativePlacementSnapshot Read(nint window)
    {
        RequirePerMonitorThread();
        N.RequireOwner(window);
        var thread = N.GetWindowThreadProcessId(window, out var process);
        if (thread != N.GetCurrentThreadId())
            throw new InvalidOperationException("NATIVE_PRECONDITION: read native placement on the HWND's own STA.");
        var awareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
        if (awareness != 2)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the actual factory HWND is not per-monitor DPI aware.");
        var dpi = GetDpiForWindow(window);
        if (dpi == 0) throw new InvalidOperationException("GetDpiForWindow returned zero for the owned HWND.");
        N.Check(N.GetWindowRect(window, out var frame), "GetWindowRect for placement");
        N.Check(GetClientRect(window, out var client), "GetClientRect for placement");
        var monitor = MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST, as PR-WIN-011 specifies.
        if (monitor == 0) throw new InvalidOperationException("MonitorFromWindow found no native monitor.");
        if (frame.Width <= 0 || frame.Height <= 0 || client.Right <= client.Left || client.Bottom <= client.Top)
            throw new InvalidOperationException("The actual factory frame/client is empty.");
        return new(window.ToInt64(), thread, process, monitor.ToInt64(), dpi, awareness,
            new(frame.Left, frame.Top, frame.Right, frame.Bottom), client.Copy(),
            N.IsWindowVisible(window), N.IsIconic(window));
    }

    internal static NativePlacementDwmReading ReadVisibleBounds(nint window)
    {
        N.RequireOwner(window);
        var status = DwmGetWindowAttribute(window, 9, out var bounds, Marshal.SizeOf<NativePlacementRectInterop>());
        return new(status, status == 0 ? bounds.Copy() : null);
    }

    internal static void Move(nint window, NativePlacementPoint origin)
    {
        _ = Read(window); // verifies thread, actual HWND ownership and DPI context immediately before moving.
        if (!SetWindowPos(window, 0, origin.X, origin.Y, 0, 0, 0x0015)) // NOSIZE | NOZORDER | NOACTIVATE
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Move only the owned fixture HWND");
    }

    internal static void ResizeMain(nint window, int width, int height)
    {
        _ = Read(window);
        if (!SetWindowPos(window, 0, 0, 0, width, height, 0x0016)) // NOMOVE | NOZORDER | NOACTIVATE
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Resize only the owned main HWND for a rounding case");
    }

    internal static NativePlacementExpectation Expected(NativePlacementSnapshot main,
        NativePlacementSnapshot settingsBeforeShow, NativePlacementRect primaryBounds,
        NativePlacementRect mainWorkArea, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        // WindowManager.swift:46-49: frame geometry in points, then two independent whole-point rounds.
        // The settings frame is measured at its OWN pre-call DPI, including when it is still on
        // another real monitor. No cached main DPI is substituted for the settings measurement.
        var mainScale = main.Dpi / 96.0;
        var settingsScale = settingsBeforeShow.Dpi / 96.0;
        var parentWidth = main.Frame.Width / mainScale;
        var parentHeight = main.Frame.Height / mainScale;
        var width = settingsBeforeShow.Frame.Width / settingsScale;
        var height = settingsBeforeShow.Frame.Height / settingsScale;
        var upwardOrigin = primaryBounds.Bottom / mainScale;
        var parentBottom = upwardOrigin - (main.Frame.Top / mainScale + parentHeight);
        var unroundedX = main.Frame.Left / mainScale + parentWidth / 2 - width / 2;
        var unroundedY = parentBottom + parentHeight / 2 - height / 2;
        var roundedX = Math.Round(unroundedX, rounding);
        var roundedY = Math.Round(unroundedY, rounding);

        // WindowManager.swift:50-52. Preserve min(max(...), far-size), including inverted limits.
        // AppKit's lower y is the Windows work area's BOTTOM, not its top. Do not round a
        // Windows-downward center instead: half points on opposite sides of zero can differ.
        var left = mainWorkArea.Left / mainScale;
        var right = mainWorkArea.Right / mainScale;
        var bottom = upwardOrigin - mainWorkArea.Bottom / mainScale;
        var top = upwardOrigin - mainWorkArea.Top / mainScale;
        var x = Math.Min(Math.Max(roundedX, left), right - width);
        var y = Math.Min(Math.Max(roundedY, bottom), top - height);
        var windowsTop = upwardOrigin - (y + height);
        var origin = new NativePlacementPoint(
            checked((int)Math.Round(x * mainScale, rounding)),
            checked((int)Math.Round(windowsTop * mainScale, rounding)));
        return new(origin, unroundedX, unroundedY, roundedX, roundedY, x, y,
            width, height, mainScale, settingsScale, x != roundedX, y != roundedY);
    }

    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint window, out NativePlacementRectInterop rectangle);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativePlacementRectInterop value, int size);
}

internal sealed record NativePlacementPoint(int X, int Y);

internal sealed record NativePlacementSnapshot(long Handle, uint ThreadId, uint ProcessId, long Monitor,
    uint Dpi, int DpiAwareness, NativePlacementRect Frame, NativePlacementRect Client, bool Visible, bool Minimized)
{
    public NativePlacementPoint Origin => new(Frame.Left, Frame.Top);
}

internal sealed record NativePlacementDwmReading(int HResult, NativePlacementRect? VisibleBounds);

internal sealed record NativePlacementExpectation(NativePlacementPoint Origin,
    double UnroundedX, double UnroundedY, double RoundedX, double RoundedY, double ClampedX, double ClampedY,
    double SettingsFrameWidthPoints, double SettingsFrameHeightPoints, double MainScale, double SettingsScale,
    bool XClampExercised, bool YClampExercised);
