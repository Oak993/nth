using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY: the top-level windows of a process, for Tactus's native tests (ED-NEW-pcctl-30). It only reads window
/// handles, rects and DPI; it never moves, styles or activates a window.
/// </summary>
public static class PcTopWindows
{
    /// <summary>The visible top-level windows <paramref name="process"/> owns, in z-order.</summary>
    public static IReadOnlyList<nint> Of(int process)
    {
        var found = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == process && IsWindowVisible(window)) found.Add(window);
            return true;
        }, 0);
        return found;
    }

    /// <summary>The window's rect in physical pixels (left, top, width, height), or null.</summary>
    public static (int X, int Y, int Width, int Height)? Rect(nint window)
    {
        var rect = new int[4];
        return GetWindowRect(window, rect) ? (rect[0], rect[1], rect[2] - rect[0], rect[3] - rect[1]) : null;
    }

    /// <summary>The window's DPI (96 at 100 %).</summary>
    public static uint Dpi(nint window) => GetDpiForWindow(window);

    private delegate bool EnumProc(nint window, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc callback, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out int process);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, [Out] int[] rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);
}
