// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.3, 2.7 "Borrow sequence"). The Windows side of a
// borrow: the virtual desktop and scan codes, the user's cursor, foreground, focus, the target's Z-order neighbours and
// placement, the key state cross-check, GetLastInputInfo, the window under a point, a caption point hit-tested HTCAPTION,
// raising without activating, SendInput with the borrow's tag in dwExtraInfo, and putting everything back (the cursor
// clamped to a monitor that still exists). Never used by automated tests: they bind a fake IRealInputHost.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

/// <param name="clock">The lease's clock (GetLastInputInfo is moved onto it).</param>
/// <param name="focusControl">UI Automation focus on a control window (the user's control, at the restore); null skips it.</param>
public sealed class RealInputHost(IPcClock clock, Func<nint, bool>? focusControl = null) : IRealInputHost
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint GW_HWNDPREV = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOWMINNOACTIVE = 7;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MaxAbove = 64;

    public PcRect Desktop() => new(
        Win32.GetSystemMetrics(SM_XVIRTUALSCREEN), Win32.GetSystemMetrics(SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(SM_CXVIRTUALSCREEN), Win32.GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public ushort ScanCode(ushort vk) => unchecked((ushort)MapVirtualKey(vk, 0));

    public PcBorrowSaved Save(nint target)
    {
        using var dpi = Dpi.PerMonitorV2();
        Win32.GetCursorPos(out var cursor);
        var (foreground, focus) = Focus();
        var above = new List<nint>();
        for (var window = Win32.GetWindow(target, GW_HWNDPREV); window != 0 && above.Count < MaxAbove; window = Win32.GetWindow(window, GW_HWNDPREV))
        {
            if (Win32.IsWindowVisible(window)) above.Add(window);
        }
        return new PcBorrowSaved(new PcPoint(cursor.X, cursor.Y), foreground, focus)
        {
            Neighbour = Win32.GetWindow(target, GW_HWNDPREV),
            Above = above,
            Minimized = Win32.IsIconic(target),
        };
    }

    public (nint Foreground, nint Focus) Focus()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == 0) return (0, 0);
        var thread = Win32.GetWindowThreadProcessId(foreground, out _);
        var info = new Win32.GUITHREADINFO { Size = Marshal.SizeOf<Win32.GUITHREADINFO>() };
        return (foreground, thread != 0 && Win32.GetGUIThreadInfo(thread, ref info) ? info.Focus : 0);
    }

    public bool Held()
    {
        for (var key = 1; key < 0xFF; key++)
        {
            // VK_PACKET is never physically down; the high bit is "down now".
            if (key == 0xE7) continue;
            if ((Win32.GetAsyncKeyState(key) & 0x8000) != 0) return true;
        }
        return false;
    }

    public long? LastInputMs()
    {
        var last = new Win32.LASTINPUTINFO { Size = Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        if (!Win32.GetLastInputInfo(ref last)) return null;
        return clock.NowMs - (long)unchecked(Win32.GetTickCount() - last.Time);
    }

    public nint WindowAt(PcPoint point)
    {
        using var dpi = Dpi.PerMonitorV2();
        var under = Win32.WindowFromPoint(new Win32.POINT { X = point.X, Y = point.Y });
        return under == 0 ? 0 : Win32.GetAncestor(under, Win32.GA_ROOT);
    }

    public PcPoint? Caption(nint target)
    {
        using var dpi = Dpi.PerMonitorV2();
        if (!Win32.GetWindowRect(target, out var rect)) return null;
        var width = rect.Right - rect.Left;
        // Points along the top band, from the middle out: the first one the window itself calls its caption.
        foreach (var dy in new[] { 8, 14, 20, 4 })
        {
            foreach (var fraction in new[] { 0.5, 0.4, 0.6, 0.3, 0.7 })
            {
                var point = new PcPoint(rect.Left + (int)(width * fraction), rect.Top + dy);
                if (WindowAt(point) != target) continue;
                var lParam = (nint)((point.Y << 16) | (point.X & 0xFFFF));
                if (Win32.SendMessageTimeout(target, WM_NCHITTEST, 0, lParam, Win32.SMTO_ABORTIFHUNG | Win32.SMTO_BLOCK, 100, out var hit) != 0 && hit == HTCAPTION)
                    return point;
            }
        }
        return null;
    }

    public void Raise(nint target) => Win32.SetWindowPos(target, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public bool SetForeground(nint window) => Win32.SetForegroundWindow(window);

    public int Send(IReadOnlyList<PcInputEvent> events, ulong tag)
    {
        var inputs = new Win32.INPUT[events.Count];
        var extra = unchecked((nint)tag);
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            inputs[i] = e.Kind == PcInputKind.Mouse
                ? new Win32.INPUT { Type = Win32.INPUT_MOUSE, Union = new Win32.INPUTUNION { Mouse = new Win32.MOUSEINPUT { Dx = e.Dx, Dy = e.Dy, MouseData = e.MouseData, Flags = e.Flags, ExtraInfo = extra } } }
                : new Win32.INPUT { Type = Win32.INPUT_KEYBOARD, Union = new Win32.INPUTUNION { Keyboard = new Win32.KEYBDINPUT { Vk = e.Vk, Scan = e.Scan, Flags = e.Flags, ExtraInfo = extra } } };
        }
        return (int)Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
    }

    public bool Restore(nint window, nint focus)
    {
        if (window == 0 || !Win32.IsWindow(window)) return false;
        var front = Win32.SetForegroundWindow(window);
        if (front && focus != 0 && focus != window && Win32.IsWindow(focus)) focusControl?.Invoke(focus);
        return front;
    }

    public bool RestoreZOrder(nint target, PcBorrowSaved saved)
    {
        if (!Win32.IsWindow(target)) return false;
        var after = saved.Neighbour != 0 && Win32.IsWindow(saved.Neighbour)
            ? saved.Neighbour
            : saved.Above.FirstOrDefault(Win32.IsWindow) is var nearest && nearest != 0 ? nearest : saved.Foreground;
        if (after == 0 || after == target) return after == 0;
        return Win32.SetWindowPos(target, after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void RestorePlacement(nint target, PcBorrowSaved saved)
    {
        if (saved.Minimized && Win32.IsWindow(target) && !Win32.IsIconic(target)) Win32.ShowWindow(target, SW_SHOWMINNOACTIVE);
    }

    public PcPoint Clamp(PcPoint point)
    {
        var monitor = MonitorFromPoint(new Win32.POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        if (monitor == 0 || !Win32.GetMonitorInfo(monitor, ref info)) return point;
        var r = info.Monitor;
        return new PcPoint(Math.Clamp(point.X, r.Left, r.Right - 1), Math.Clamp(point.Y, r.Top, r.Bottom - 1));
    }

    public void SetCursor(PcPoint point)
    {
        using var dpi = Dpi.PerMonitorV2();
        SetCursorPos(point.X, point.Y);
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Win32.POINT point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
}
