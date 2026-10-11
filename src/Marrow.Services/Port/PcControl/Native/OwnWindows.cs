// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-2; legacy Marrow pcService.ts:54-57, :107-116). nth's own windows: every
// visible top-level window of this process, overlays included (a click-through pill is skipped by WindowFromPoint, so its
// rectangle is checked too). Suppress hides them while PC control captures or acts, through the app's hook.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class OwnWindows(Func<IDisposable>? suppress = null) : IOwnWindows
{
    private readonly int processId = Environment.ProcessId;

    public IReadOnlyList<PcRect> Rects()
    {
        using var dpi = Dpi.PerMonitorV2();
        var rects = new List<PcRect>();
        Win32.EnumWindows((handle, _) =>
        {
            if (Owns(handle) && Win32.IsWindowVisible(handle) && Win32.GetWindowRect(handle, out var rect)) rects.Add(Win32.Rect(rect));
            return true;
        }, 0);
        return rects.Where(r => !r.IsEmpty).ToList();
    }

    public bool Owns(nint handle)
    {
        if (handle == 0) return false;
        Win32.GetWindowThreadProcessId(handle, out var owner);
        return owner == processId;
    }

    public IDisposable Suppress() => suppress?.Invoke() ?? NoScope.Instance;

    private sealed class NoScope : IDisposable
    {
        public static NoScope Instance { get; } = new();
        public void Dispose() { }
    }
}
