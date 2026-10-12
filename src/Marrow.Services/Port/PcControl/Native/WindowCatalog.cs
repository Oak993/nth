// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 3.1). The Win32 window list: EnumWindows, skipping hidden,
// cloaked, tool and empty windows; process exe and AUMID; DWM extended frame bounds in physical pixels; monitors; hit
// tests by root owner; activation; the quiet peek of a minimized window (ED-NEW-pcctl-46: restored without activation at
// the bottom of the Z-order, then minimized again in its exact placement). A packaged app's window belongs to
// ApplicationFrameHost, so its identity is read from the hosted core window's process. Never used by automated tests.
using System.Runtime.InteropServices;
using System.Text;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class WindowCatalog : IWindowCatalog
{
    public IReadOnlyList<PcWindowInfo> Windows()
    {
        using var dpi = Dpi.PerMonitorV2();
        var handles = new List<nint>();
        Win32.EnumWindows((handle, _) =>
        {
            handles.Add(handle);
            return true;
        }, 0);
        var monitors = Monitors();
        var windows = new List<PcWindowInfo>();
        foreach (var handle in handles)
        {
            if (!Listed(handle)) continue;
            if (Describe(handle, monitors) is { } info && (!info.Frame.IsEmpty || info.Minimized)) windows.Add(info);
        }
        return windows;
    }

    public PcWindowInfo? Window(nint handle)
    {
        using var dpi = Dpi.PerMonitorV2();
        return Win32.IsWindow(handle) ? Describe(handle, Monitors()) : null;
    }

    public IReadOnlyList<PcMonitor> Monitors()
    {
        using var dpi = Dpi.PerMonitorV2();
        var monitors = new List<PcMonitor>();
        Win32.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Win32.RECT _, nint _) =>
        {
            var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>(), Device = "" };
            if (!Win32.GetMonitorInfo(monitor, ref info)) return true;
            var monitorDpi = Win32.GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? (int)x : 96;
            monitors.Add(new PcMonitor(monitors.Count, Win32.Rect(info.Monitor), Win32.Rect(info.Work), monitorDpi, (info.Flags & Win32.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, 0);
        return monitors;
    }

    public PcRect VirtualDesktop()
    {
        using var dpi = Dpi.PerMonitorV2();
        return new PcRect(
            Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
            Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));
    }

    public nint RootAt(PcPoint point)
    {
        using var dpi = Dpi.PerMonitorV2();
        var hit = Win32.WindowFromPoint(new Win32.POINT { X = point.X, Y = point.Y });
        return hit == 0 ? 0 : Win32.GetAncestor(hit, Win32.GA_ROOTOWNER);
    }

    public nint Foreground()
    {
        var foreground = Win32.GetForegroundWindow();
        return foreground == 0 ? 0 : Win32.GetAncestor(foreground, Win32.GA_ROOTOWNER);
    }

    // Tactus never calls this (critique C9: no AttachThreadInput, no foreground switch). It stays for the older callers and
    // only asks Windows; when the foreground lock refuses, the answer is false.
    public bool Activate(nint handle)
    {
        if (!Win32.IsWindow(handle)) return false;
        if (Win32.IsIconic(handle)) Win32.ShowWindow(handle, Win32.SW_RESTORE);
        if (Foreground() == handle) return true;
        Win32.SetForegroundWindow(handle);
        return Foreground() == handle;
    }

    public bool Show(nint handle)
    {
        if (!Win32.IsWindow(handle)) return false;
        if (Win32.IsIconic(handle)) Win32.ShowWindow(handle, Win32.SW_SHOWNOACTIVATE);
        return Win32.SetWindowPos(handle, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public IDisposable? Peek(nint handle)
    {
        if (!Win32.IsWindow(handle) || !Win32.IsIconic(handle)) return null;
        var saved = new Win32.WINDOWPLACEMENT { Length = Marshal.SizeOf<Win32.WINDOWPLACEMENT>() };
        if (!Win32.GetWindowPlacement(handle, ref saved)) return null;
        Win32.ShowWindow(handle, Win32.SW_SHOWNOACTIVATE);
        Win32.SetWindowPos(handle, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        return Win32.IsIconic(handle) ? null : new Peeked(handle, saved);
    }

    // The peek's end: minimized again without activation, then the exact placement it had (its restored rect and whether it
    // restores maximized), applied while it stays minimized.
    private sealed class Peeked(nint handle, Win32.WINDOWPLACEMENT saved) : IDisposable
    {
        private int ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0 || !Win32.IsWindow(handle)) return;
            Win32.ShowWindow(handle, Win32.SW_SHOWMINNOACTIVE);
            var placement = saved;
            placement.ShowCmd = Win32.SW_SHOWMINNOACTIVE;
            Win32.SetWindowPlacement(handle, ref placement);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Versions = new(StringComparer.OrdinalIgnoreCase);

    // The exe's file version, read once per path.
    private static string FileVersion(string exe) =>
        exe.Length == 0 ? "" : Versions.GetOrAdd(exe, path =>
        {
            try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? ""; }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        });

    // The process start time in UTC ticks; 0 when it cannot be read.
    private static long StartTime(int processId)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return 0;
        try
        {
            return Win32.GetProcessTimes(process, out var created, out _, out _, out _) ? created.Ticks : 0;
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }

    // A window the user sees in Alt+Tab: visible, not cloaked, not a tool window unless it asks to be an app window.
    private static bool Listed(nint handle)
    {
        if (!Win32.IsWindowVisible(handle)) return false;
        if (Win32.DwmGetWindowAttribute(handle, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var style = (long)Win32.GetWindowLongPtr(handle, Win32.GWL_EXSTYLE);
        if ((style & Win32.WS_EX_TOOLWINDOW) != 0 && (style & Win32.WS_EX_APPWINDOW) == 0) return false;
        return true;
    }

    private static PcWindowInfo? Describe(nint handle, IReadOnlyList<PcMonitor> monitors)
    {
        Win32.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return null;
        var exe = ExePath(processId) ?? "";
        var aumid = Aumid(processId);
        // A UWP window is hosted by ApplicationFrameHost; its app is the process of the hosted CoreWindow.
        if (string.Equals(Path.GetFileName(exe), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
            && HostedProcess(handle, processId) is { } hosted)
        {
            processId = hosted;
            exe = ExePath(hosted) ?? exe;
            aumid = Aumid(hosted);
        }
        var frame = Win32.DwmGetWindowAttribute(handle, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.RECT bounds, Marshal.SizeOf<Win32.RECT>()) == 0
            ? Win32.Rect(bounds)
            : Win32.GetWindowRect(handle, out var rect) ? Win32.Rect(rect) : default;
        var centre = frame.Center;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(centre))?.Index ?? 0;
        return new PcWindowInfo(handle, Win32.WindowText(handle), Win32.ClassName(handle), processId, exe)
        {
            Aumid = aumid,
            Frame = frame,
            Minimized = Win32.IsIconic(handle),
            Owner = Win32.GetWindow(handle, Win32.GW_OWNER),
            Monitor = monitor,
            StartTime = StartTime(processId),
            Version = FileVersion(exe),
        };
    }

    private static int? HostedProcess(nint frame, int frameProcess)
    {
        int? hosted = null;
        Win32.EnumChildWindows(frame, (child, _) =>
        {
            Win32.GetWindowThreadProcessId(child, out var pid);
            if (pid != 0 && pid != frameProcess)
            {
                hosted = pid;
                return false;
            }
            return true;
        }, 0);
        return hosted;
    }

    internal static string? ExePath(int processId)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return null;
        try
        {
            var size = 1024;
            var name = new StringBuilder(size);
            return Win32.QueryFullProcessImageName(process, 0, name, ref size) ? name.ToString() : null;
        }
        finally { Win32.CloseHandle(process); }
    }

    private static string? Aumid(int processId)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return null;
        try
        {
            var length = 0;
            // ERROR_INSUFFICIENT_BUFFER (122) when the process has an AUMID; APPMODEL_ERROR_NO_APPLICATION otherwise.
            if (Win32.GetApplicationUserModelId(process, ref length, null) != 122 || length <= 0) return null;
            var id = new StringBuilder(length);
            return Win32.GetApplicationUserModelId(process, ref length, id) == 0 ? id.ToString() : null;
        }
        finally { Win32.CloseHandle(process); }
    }
}
