// PORT-ONLY: Tactus pill v2, the card's keyboard hand-back on Windows (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "It never
// steals focus"). The window parts of CardFocus's rules: the foreground window, its process and class (section 2.1's
// identity, so a reused handle is not taken for the saved window), minimized or cloaked, and SetForegroundWindow. The
// focused element inside the window is not read here: SetForegroundWindow gives the window back the focus its own thread
// kept, and the UIA element restore is left (ED-NEW-pcctl-26 item 9). Never run by automated tests; NP-18 checks it.
using System.Runtime.InteropServices;

namespace Marrow.App.Port.PcControl;

public sealed class CardFocusWin32 : ICardFocusNative
{
    private const int DwmwaCloaked = 14;

    public nint Foreground() => GetForegroundWindow();

    public CardFocusTarget? Describe(nint window)
    {
        if (window == 0 || !IsWindow(window)) return null;
        _ = GetWindowThreadProcessId(window, out var process);
        return new CardFocusTarget(window, (int)process, ClassOf(window), null);
    }

    public bool Same(CardFocusTarget target)
    {
        if (!IsWindow(target.Window)) return false;
        _ = GetWindowThreadProcessId(target.Window, out var process);
        return (int)process == target.ProcessId && string.Equals(ClassOf(target.Window), target.ClassName, StringComparison.Ordinal);
    }

    public bool Hidden(nint window) =>
        IsIconic(window) || (DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0);

    public bool SetForeground(nint window) => SetForegroundWindow(window);

    public bool FocusIs(CardFocusTarget target) => true;

    public void Focus(CardFocusTarget target)
    {
    }

    private static string ClassOf(nint window)
    {
        var buffer = new char[256];
        var length = GetClassNameW(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint window, [Out] char[] name, int count);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
