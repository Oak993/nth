// PORT-ONLY: Tactus pill v2, the monitors the bands live on (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Displays", "Which
// monitor", 10.6). Each monitor has an identity that survives a restart and a re-plug: the device path from
// QueryDisplayConfig (DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath), with the GDI device name as the fallback (the
// last rect is the caller's last resort). The map turns screen pixels into a monitor's DIP and back (rounded outward), and
// compares two readings: a monitor added or removed, and a monitor whose DPI, bounds or work area changed (WM_DPICHANGED,
// WM_DISPLAYCHANGE, WM_SETTINGCHANGE with SPI_SETWORKAREA). Reading the monitors happens on those messages only, never
// per frame.
using System.Runtime.InteropServices;
using Avalonia;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>One monitor: its identity, bounds and work area in physical pixels, and its DPI.</summary>
public sealed record OverlayMonitor(string Id, string Name, PcRect Bounds, PcRect WorkArea, int Dpi, bool Primary)
{
    public double Scale => Dpi / 96.0;

    /// <summary>The work area in this monitor's DIP (origin at the monitor's top-left).</summary>
    public PillArea WorkDip => new((WorkArea.X - Bounds.X) / Scale, (WorkArea.Y - Bounds.Y) / Scale, WorkArea.Width / Scale, WorkArea.Height / Scale);

    /// <summary>The band's place on this monitor: its top-left at the work area's top-left.</summary>
    public OverlayBandGeometry Band => new(new PixelPoint(WorkArea.X, WorkArea.Y), new Point(WorkDip.Left, WorkDip.Top), Scale);

    /// <summary>A rect in this monitor's DIP on screen, physical pixels rounded outward.</summary>
    public PixelRect ToScreen(PillRect rect)
    {
        var left = (int)Math.Floor(Bounds.X + (rect.X * Scale));
        var top = (int)Math.Floor(Bounds.Y + (rect.Y * Scale));
        var right = (int)Math.Ceiling(Bounds.X + (rect.Right * Scale));
        var bottom = (int)Math.Ceiling(Bounds.Y + (rect.Bottom * Scale));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>A screen rect (physical pixels) in this monitor's DIP.</summary>
    public PillRect ToDip(PcRect rect) => new((rect.X - Bounds.X) / Scale, (rect.Y - Bounds.Y) / Scale, rect.Width / Scale, rect.Height / Scale);

    public bool Contains(PcPoint point) => point.X >= Bounds.X && point.X < Bounds.Right && point.Y >= Bounds.Y && point.Y < Bounds.Bottom;
}

/// <summary>What changed between two readings of the monitors.</summary>
public sealed record MonitorChange(IReadOnlyList<OverlayMonitor> Added, IReadOnlyList<OverlayMonitor> Removed, IReadOnlyList<OverlayMonitor> Changed)
{
    public static MonitorChange None { get; } = new([], [], []);

    public bool Any => Added.Count + Removed.Count + Changed.Count > 0;
}

/// <summary>Reads the monitors. The real one never runs in tests.</summary>
public interface IOverlayMonitors
{
    IReadOnlyList<OverlayMonitor> Read();
}

public sealed class MonitorMap(IOverlayMonitors source)
{
    private IReadOnlyList<OverlayMonitor> monitors = [];

    public IReadOnlyList<OverlayMonitor> Monitors => monitors;

    /// <summary>The main monitor (or the first, when none says it is).</summary>
    public OverlayMonitor? Main => monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();

    public OverlayMonitor? ById(string? id) => id is null ? null : monitors.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The monitor that holds the screen point, else the nearest one (MonitorFromPoint, nearest).</summary>
    public OverlayMonitor? At(PcPoint point) =>
        monitors.FirstOrDefault(m => m.Contains(point)) ?? monitors.OrderBy(m => Distance(m.Bounds, point)).FirstOrDefault();

    /// <summary>Reads the monitors again and says what changed (a display or work-area change, a DPI change, an unplug).</summary>
    public MonitorChange Refresh()
    {
        var next = source.Read();
        var change = Compare(monitors, next);
        monitors = next;
        return change;
    }

    public static MonitorChange Compare(IReadOnlyList<OverlayMonitor> before, IReadOnlyList<OverlayMonitor> after)
    {
        var added = after.Where(a => before.All(b => !Same(a, b))).ToList();
        var removed = before.Where(b => after.All(a => !Same(a, b))).ToList();
        var changed = after.Where(a => before.FirstOrDefault(b => Same(a, b)) is { } b && (a.Bounds != b.Bounds || a.WorkArea != b.WorkArea || a.Dpi != b.Dpi)).ToList();
        return added.Count + removed.Count + changed.Count == 0 ? MonitorChange.None : new MonitorChange(added, removed, changed);
    }

    private static bool Same(OverlayMonitor a, OverlayMonitor b) => string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

    private static double Distance(PcRect rect, PcPoint point)
    {
        var dx = Math.Max(Math.Max(rect.X - point.X, 0), point.X - (rect.Right - 1));
        var dy = Math.Max(Math.Max(rect.Y - point.Y, 0), point.Y - (rect.Bottom - 1));
        return Math.Sqrt((dx * (double)dx) + (dy * (double)dy));
    }
}

/// <summary>The real monitors: EnumDisplayMonitors, GetMonitorInfo, GetDpiForMonitor and QueryDisplayConfig. Never run by tests.</summary>
public sealed class Win32OverlayMonitors : IOverlayMonitors
{
    private const uint MonitorInfoPrimary = 1;
    private const int MdtEffectiveDpi = 0;
    private const uint QdcOnlyActivePaths = 2;
    private const int DeviceInfoGetSourceName = 1;
    private const int DeviceInfoGetTargetName = 2;

    public IReadOnlyList<OverlayMonitor> Read()
    {
        var paths = DevicePaths();
        var list = new List<OverlayMonitor>();
        EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(handle, ref info)) return true;
            var dpi = GetDpiForMonitor(handle, MdtEffectiveDpi, out var x, out _) == 0 && x > 0 ? (int)x : 96;
            var gdi = info.Device ?? "";
            var (path, name) = paths.TryGetValue(gdi, out var target) ? target : (gdi, gdi);
            list.Add(new OverlayMonitor(
                path.Length > 0 ? path : gdi,
                name.Length > 0 ? name : gdi,
                new PcRect(info.Left, info.Top, info.Right - info.Left, info.Bottom - info.Top),
                new PcRect(info.WorkLeft, info.WorkTop, info.WorkRight - info.WorkLeft, info.WorkBottom - info.WorkTop),
                dpi,
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        }, 0);
        return list;
    }

    // The GDI device name of each active path's source, mapped to its target's device path and friendly name.
    private static Dictionary<string, (string Path, string Name)> DevicePaths()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return map;
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0) != 0) return map;
        for (var i = 0; i < pathCount; i++)
        {
            var source = new SourceDeviceName { Header = new DeviceInfoHeader { Type = DeviceInfoGetSourceName, Size = Marshal.SizeOf<SourceDeviceName>(), AdapterId = paths[i].SourceAdapterId, Id = paths[i].SourceId } };
            var target = new TargetDeviceName { Header = new DeviceInfoHeader { Type = DeviceInfoGetTargetName, Size = Marshal.SizeOf<TargetDeviceName>(), AdapterId = paths[i].TargetAdapterId, Id = paths[i].TargetId } };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0) continue;
            map[source.GdiDeviceName ?? ""] = (target.MonitorDevicePath ?? "", target.MonitorFriendlyDeviceName ?? "");
        }
        return map;
    }

    private delegate bool MonitorEnum(nint monitor, nint dc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
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

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    // DISPLAYCONFIG_PATH_INFO (72 bytes): source (adapter, id, mode index, flags), target (adapter, id, mode index,
    // technology, rotation, scaling, refresh rational, scanline ordering, available, flags), flags.
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId;
        public uint SourceModeInfoIdx;
        public uint SourceStatusFlags;
        public Luid TargetAdapterId;
        public uint TargetId;
        public uint TargetModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO (64 bytes): type, id, adapter, then a 48-byte union this code never reads.
    [StructLayout(LayoutKind.Sequential)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public ulong A;
        public ulong B;
        public ulong C;
        public ulong D;
        public ulong E;
        public ulong F;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public int Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? GdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? MonitorDevicePath;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out int paths, out int modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref int pathCount, [Out] PathInfo[] paths, ref int modeCount, [Out] ModeInfo[] modes, nint topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);
}
