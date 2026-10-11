// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.6 delta check, critique C16). Reads
// the user's side of the PC around each way: the foreground root, the real cursor, the foreground thread's focused
// control, whether Shift, Ctrl, Alt or Windows is down, and the age of the last input of the session. Reading only.
// Never used by automated tests.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class Foreground : IForeground
{
    private static readonly int[] Modifiers = [Win32.VK_SHIFT, Win32.VK_CONTROL, Win32.VK_MENU, Win32.VK_LWIN, Win32.VK_RWIN];

    public PcUserState Read()
    {
        using var dpi = Dpi.PerMonitorV2();
        var front = Win32.GetForegroundWindow();
        var root = front == 0 ? 0 : Win32.GetAncestor(front, Win32.GA_ROOTOWNER);
        var cursor = Win32.GetCursorPos(out var point) ? new PcPoint(point.X, point.Y) : default;
        nint focus = 0;
        if (front != 0)
        {
            var thread = Win32.GetWindowThreadProcessId(front, out _);
            var info = new Win32.GUITHREADINFO { Size = Marshal.SizeOf<Win32.GUITHREADINFO>() };
            if (thread != 0 && Win32.GetGUIThreadInfo(thread, ref info)) focus = info.Focus;
        }
        var down = Modifiers.Any(key => (Win32.GetAsyncKeyState(key) & 0x8000) != 0);
        var last = new Win32.LASTINPUTINFO { Size = Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        // The tick counts wrap every 49.7 days; unsigned subtraction keeps the age right across the wrap.
        var age = Win32.GetLastInputInfo(ref last) ? (long)unchecked(Win32.GetTickCount() - last.Time) : long.MaxValue;
        return new PcUserState(root == 0 ? front : root, cursor, focus, down, age);
    }
}
