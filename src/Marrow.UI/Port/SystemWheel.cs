// PORT-ONLY (OD-0071, ED-NEW-softscroll-2). How far a wheel notch scrolls: Windows' own "Roll the mouse wheel to scroll
// ... lines at a time" setting (SPI_GETWHEELSCROLLLINES, read only, never written) times 33 1/3 points a line, so the
// default 3 lines is 100 points a notch, the step Chrome and Edge take. "One screen at a time" (WHEEL_PAGESCROLL, read as
// -1) is a viewport's length; 0 lines scrolls nothing. Read at every notch, so a change in Settings applies at once. A
// precision touchpad's stream keeps ED-246's own weight (ScrollPhysics.WheelLine). Only the app installs the Windows
// reader (MarrowApplication); headless tests get Windows' default, 3 lines, so the PC's own setting never reaches them.
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The wheel's lines setting and the distance a notch scrolls (see the file note).</summary>
public sealed class SystemWheel
{
    /// <summary>Points per line: 100 points a notch at Windows' default of 3 lines.</summary>
    public const double LinePoints = 100.0 / 3;

    /// <summary>Windows' default lines per notch.</summary>
    public const int DefaultLines = 3;

    /// <summary>WHEEL_PAGESCROLL: a notch scrolls one screen.</summary>
    public const int PageScroll = -1;

    private readonly Func<int> read;

    /// <param name="read">Reads the setting: lines per notch, or <see cref="PageScroll"/>.</param>
    public SystemWheel(Func<int> read) => this.read = read;

    /// <summary>The app's instance (<see cref="Install"/> makes it read Windows; until then Windows' default). A test
    /// overrides it for its own flow.</summary>
    public static Ambient<SystemWheel> Shared { get; } = new(new SystemWheel(() => DefaultLines));

    private static bool installed;

    /// <summary>App start-up: notches follow the Windows setting from now on.</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Shared.SetProcessDefault(new SystemWheel(ReadWheelScrollLines));
    }

    /// <summary>Lines per notch, or <see cref="PageScroll"/>.</summary>
    public int Lines => read();

    /// <summary>The points one notch scrolls along an axis whose viewport is <paramref name="viewport"/> points long.</summary>
    public double Notch(double viewport)
    {
        var lines = Lines;
        return lines == PageScroll ? Math.Max(0, viewport) : Math.Max(0, lines) * LinePoints;
    }

    // SPI_GETWHEELSCROLLLINES: a UINT, WHEEL_PAGESCROLL (UINT_MAX) for a screen at a time; Windows' default if unreadable.
    private static int ReadWheelScrollLines()
    {
        if (!OperatingSystem.IsWindows() || !SystemParametersInfo(0x0068, 0, out var lines, 0)) return DefaultLines;
        return lines == uint.MaxValue ? PageScroll : (int)Math.Min(lines, int.MaxValue);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out uint value, uint update);
}
