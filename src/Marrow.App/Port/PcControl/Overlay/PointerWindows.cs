// PORT-ONLY: Tactus pointer windows (OD-0144, ED-NEW-pcctl-21; DESIGN.md 3.1 overlay row, 4.5, critique C19). The real
// IPointerSurfaces: a TactusOverlayWindow in the Pointer role (layered, click-through, no-activate, topmost, excluded from
// capture) holding one TactusPointer whose badge is the acting app's icon (the shell's icon for its exe, else its first
// letter). And the real IPointerScreens: the catalog's monitors, and SHQueryUserNotificationState for exclusive
// fullscreen (the foreground window's monitor) and presentation mode (every monitor), both read at most every 500 ms.
// Never constructed by automated tests.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pointer;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

public sealed class PointerWindows : IPointerSurfaces
{
    private readonly Dictionary<string, IImage?> icons = new(StringComparer.OrdinalIgnoreCase);

    public IPointerSurface Open(PcPointerAim aim)
    {
        if (!icons.TryGetValue(aim.ExePath, out var icon))
        {
            icon = ShellFileIcons.Icon(aim.ExePath)?.ToBitmap();
            icons[aim.ExePath] = icon;
        }
        var pointer = new TactusPointer { BadgeImage = icon, BadgeLetter = icon is null && aim.AppName.Length > 0 ? aim.AppName[..1].ToUpperInvariant() : null };
        return new PointerWindow(pointer);
    }

    private sealed class PointerWindow(TactusPointer pointer) : IPointerSurface
    {
        private readonly TactusOverlayWindow window = new(OverlayRole.Pointer) { Content = pointer };
        private bool shown;

        public void Place(PixelRect rect, double scale)
        {
            window.Position = rect.Position;
            window.Width = rect.Width / scale;
            window.Height = rect.Height / scale;
            if (shown) return;
            shown = true;
            window.Show();
        }

        public void Draw(PointerPose pose, Point origin)
        {
            pointer.Origin = origin;
            pointer.Pose = pose;
        }

        public void Close() => window.Close();
    }
}

public sealed class Win32PointerScreens(IWindowCatalog catalog, Func<double> seconds) : IPointerScreens
{
    private const double FreshSeconds = 0.5;
    private const int RunningD3DFullScreen = 3;
    private const int PresentationMode = 4;
    private const uint DefaultToNearest = 2;

    private IReadOnlyList<PcMonitor> monitors = [];
    private double monitorsAt = double.NegativeInfinity;
    private (int State, int Monitor) blocked;
    private double blockedAt = double.NegativeInfinity;

    public IReadOnlyList<PcMonitor> Monitors()
    {
        var now = seconds();
        if (now - monitorsAt < FreshSeconds) return monitors;
        monitorsAt = now;
        return monitors = catalog.Monitors();
    }

    public bool NothingDrawnOn(int monitor)
    {
        var now = seconds();
        if (now - blockedAt >= FreshSeconds)
        {
            blockedAt = now;
            var state = SHQueryUserNotificationState(out var value) == 0 ? value : 0;
            blocked = (state, state == RunningD3DFullScreen ? ForegroundMonitor() : -1);
        }
        return blocked.State == PresentationMode || (blocked.State == RunningD3DFullScreen && blocked.Monitor == monitor);
    }

    // The index of the monitor that holds the foreground window, in the catalog's order.
    private int ForegroundMonitor()
    {
        var handle = MonitorFromWindow(GetForegroundWindow(), DefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == 0 || !GetMonitorInfo(handle, ref info)) return -1;
        var bounds = new Marrow.Core.Port.PcControl.PcRect(info.Left, info.Top, info.Right - info.Left, info.Bottom - info.Top);
        return Monitors().FirstOrDefault(m => m.Bounds == bounds)?.Index ?? -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
