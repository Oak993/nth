// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.3). Every Win32 number PC control reads or sends is in
// physical pixels: the PC worker thread runs per-monitor DPI aware v2 for the length of each call, and puts the thread's
// old context back afterwards.
namespace Marrow.Services.Port.PcControl.Native;

public static class Dpi
{
    /// <summary>Runs the scope's thread per-monitor DPI aware v2 until disposed.</summary>
    public static Scope PerMonitorV2() => new(Win32.SetThreadDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

    /// <summary>A window's DPI (96 = 100 %), or 96 when it cannot be read.</summary>
    public static int ForWindow(nint handle)
    {
        var dpi = (int)Win32.GetDpiForWindow(handle);
        return dpi > 0 ? dpi : 96;
    }

    /// <summary>The display scale of a DPI, as a factor (120 is 1.25).</summary>
    public static double Scale(int dpi) => dpi <= 0 ? 1.0 : dpi / 96.0;

    public readonly struct Scope(nint previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous != 0) Win32.SetThreadDpiAwarenessContext(previous);
        }
    }
}
