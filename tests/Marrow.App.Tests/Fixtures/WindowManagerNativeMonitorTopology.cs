using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Window.Native;

// Real OS observations, never a screen override.
// CCD source names join active target paths to EnumDisplayMonitors/GetMonitorInfoW desktop regions.
// A clone can have several physical targets but only one independently movable desktop region.
internal static class WindowManagerNativeMonitorTopology
{
    internal static NativePlacementTopology Read()
    {
        try { return ReadCore(); }
        catch (Exception error)
        {
            throw new InvalidOperationException("NATIVE_PRECONDITION: actual monitor topology could not be read consistently.", error);
        }
    }

    private static NativePlacementTopology ReadCore()
    {
        WindowManagerNativePlacementGeometry.RequirePerMonitorThread();
        Assert.Equal(104, Marshal.SizeOf<NativePlacementMonitorInfo>());
        Assert.Equal(72, Marshal.SizeOf<NativePlacementPath>());
        Assert.Equal(64, Marshal.SizeOf<NativePlacementMode>());
        Assert.Equal(84, Marshal.SizeOf<NativePlacementSourceName>());
        Assert.Equal(420, Marshal.SizeOf<NativePlacementTargetName>());
        var targets = ReadTargets();
        var monitors = new List<NativePlacementMonitor>();
        Exception? callbackFailure = null;
        NativePlacementMonitorCallback callback = (handle, _, _, _) =>
        {
            try
            {
                var info = new NativePlacementMonitorInfo { Size = 104, Device = string.Empty };
                if (!GetMonitorInfo(handle, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var bounds = info.Bounds.Copy();
                var work = info.Work.Copy();
                var paths = targets.Where(path => string.Equals(path.SourceDevice, info.Device, StringComparison.OrdinalIgnoreCase)).ToArray();
                monitors.Add(new(handle.ToInt64(), info.Device, (info.Flags & 1) != 0, bounds, work,
                    paths.Length, paths.Count(path => path.PhysicalConnectorReported)));
                return true;
            }
            catch (Exception error) { callbackFailure = error; return false; }
        };
        var enumerated = EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        if (callbackFailure is not null) throw new InvalidOperationException("NATIVE_TOPOLOGY: monitor enumeration callback failed.", callbackFailure);
        if (!enumerated) throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors");
        return new(monitors.OrderBy(item => item.Device, StringComparer.Ordinal).ToArray(), targets,
            GetSystemMetrics(80)); // SM_CMONITORS excludes mirroring pseudo-monitors.
    }

    internal static void AssertUsable(NativePlacementTopology topology)
    {
        try { AssertTopology(topology); }
        catch (Exception error)
        {
            throw new InvalidOperationException("NATIVE_PRECONDITION: actual desktop/CCD topology did not satisfy the native proof prerequisites.", error);
        }
    }

    private static void AssertTopology(NativePlacementTopology topology)
    {
        Assert.NotEmpty(topology.Monitors);
        Assert.Equal(topology.SystemMonitorCount, topology.Monitors.Length);
        Assert.Equal(topology.Monitors.Length, topology.Monitors.Select(item => item.Handle).Distinct().Count());
        Assert.Equal(topology.Monitors.Length, topology.Monitors.Select(item => item.Device).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(topology.Monitors, item => item.Primary);
        Assert.NotEmpty(topology.Targets);
        Assert.Equal(topology.Targets.Length, topology.Targets.Select(item => item.TargetKey).Distinct(StringComparer.Ordinal).Count());
        foreach (var monitor in topology.Monitors)
        {
            Assert.True(monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0);
            Assert.True(monitor.Work.Width > 0 && monitor.Work.Height > 0);
            Assert.True(monitor.Bounds.Contains(monitor.Work), "Native work area is outside its monitor: " + monitor.Device);
            Assert.True(monitor.ActiveTargetCount > 0, "No active CCD target maps to native monitor " + monitor.Device);
            foreach (var target in topology.Targets.Where(item => string.Equals(item.SourceDevice, monitor.Device, StringComparison.OrdinalIgnoreCase)))
                Assert.Equal(monitor.Bounds, target.SourceBounds);
        }
        // No 2-monitor assertion here: a single-monitor run still exercises its real geometry.
        // Physical targets that share a source are reported as clones, never counted as two regions.
        Assert.True(topology.PhysicalDesktopCount > 0, "NATIVE_TOPOLOGY: no desktop region backed by a reported physical connector is available.");
    }

    internal static void AssertUnchanged(NativePlacementTopology before, NativePlacementTopology after)
    {
        Assert.Equal(before.SystemMonitorCount, after.SystemMonitorCount);
        Assert.Equal(before.Monitors, after.Monitors);
        Assert.Equal(before.Targets, after.Targets);
    }

    private static NativePlacementTarget[] ReadTargets()
    {
        // No virtual-mode flags: the source ModeInfoIndex has the ordinary whole-index layout.
        // Retry only an observed topology/buffer race, never a failing placement assertion.
        const uint onlyActive = 2;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            CheckCode(GetDisplayConfigBufferSizes(onlyActive, out var pathCount, out var modeCount), "GetDisplayConfigBufferSizes");
            if (pathCount == 0 || modeCount == 0) throw new InvalidOperationException("NATIVE_TOPOLOGY: active CCD paths/modes are empty.");
            var paths = new NativePlacementPath[checked((int)pathCount)];
            var modes = new NativePlacementMode[checked((int)modeCount)];
            var code = QueryDisplayConfig(onlyActive, ref pathCount, paths, ref modeCount, modes, 0);
            if (code == 122) continue; // ERROR_INSUFFICIENT_BUFFER: re-query capacities.
            CheckCode(code, "QueryDisplayConfig");
            var targets = new List<NativePlacementTarget>();
            foreach (var path in paths.Take(checked((int)pathCount)))
            {
                if ((path.Flags & 1) == 0 || path.Target.Available == 0)
                    throw new InvalidOperationException("NATIVE_TOPOLOGY: an active CCD target disappeared while reading topology.");
                if ((path.Flags & 8) != 0 || path.Source.ModeIndex >= modeCount)
                    throw new InvalidOperationException("NATIVE_TOPOLOGY: an unexpected virtual-mode index needs explicit decoding.");
                var mode = modes[checked((int)path.Source.ModeIndex)];
                if (mode.Kind != 1 || mode.Id != path.Source.Id || mode.Adapter != path.Source.Adapter)
                    throw new InvalidOperationException("NATIVE_TOPOLOGY: source-mode identity does not match its CCD path.");
                var source = new NativePlacementSourceName
                {
                    Header = new() { Kind = 1, Size = 84, Adapter = path.Source.Adapter, Id = path.Source.Id },
                    Device = string.Empty,
                };
                CheckCode(GetSourceName(ref source), "DisplayConfigGetDeviceInfo source name");
                var target = new NativePlacementTargetName
                {
                    Header = new() { Kind = 2, Size = 420, Adapter = path.Target.Adapter, Id = path.Target.Id },
                    FriendlyName = string.Empty,
                    DevicePath = string.Empty,
                };
                CheckCode(GetTargetName(ref target), "DisplayConfigGetDeviceInfo target name");
                var technology = path.Target.Technology;
                // This is an OS connector classification, not proof against a deceptive driver.
                // OTHER, Miracast, indirect-wired/virtual and unknown values remain unclassified.
                var wired = technology is >= 0 and <= 6 or >= 8 and <= 14 or 18 or int.MinValue;
                var physical = wired && (path.Target.Status & 0x3C) == 0; // exclude forced/HMD targets
                targets.Add(new(
                    string.Create(CultureInfo.InvariantCulture, $"{path.Target.Adapter:X16}:{path.Target.Id}"),
                    source.Device, target.FriendlyName, target.DevicePath, technology, physical,
                    new(mode.X, mode.Y, checked(mode.X + (int)mode.Width), checked(mode.Y + (int)mode.Height))));
            }
            return targets.OrderBy(item => item.TargetKey, StringComparer.Ordinal).ToArray();
        }
        throw new InvalidOperationException("NATIVE_TOPOLOGY: active display topology changed during all four CCD reads.");
    }

    private static void CheckCode(int code, string operation)
    {
        if (code != 0) throw new Win32Exception(code, operation);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, NativePlacementMonitorCallback callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref NativePlacementMonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] NativePlacementPath[] paths, ref uint modeCount, [Out] NativePlacementMode[] modes, nint topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref NativePlacementSourceName request);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref NativePlacementTargetName request);
}

internal sealed record NativePlacementTopology(NativePlacementMonitor[] Monitors, NativePlacementTarget[] Targets, int SystemMonitorCount)
{
    public int PhysicalTargetCount => Targets.Count(item => item.PhysicalConnectorReported);
    public int PhysicalDesktopCount => Monitors.Count(item => item.PhysicalTargetCount > 0);
    public int UnclassifiedTargetCount => Targets.Count(item => !item.PhysicalConnectorReported);
}

internal sealed record NativePlacementMonitor(long Handle, string Device, bool Primary, NativePlacementRect Bounds,
    NativePlacementRect Work, int ActiveTargetCount, int PhysicalTargetCount);

internal sealed record NativePlacementTarget(string TargetKey, string SourceDevice, string FriendlyName, string DevicePath,
    int OutputTechnology, bool PhysicalConnectorReported, NativePlacementRect SourceBounds);

internal sealed record NativePlacementRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
    public bool Contains(NativePlacementRect value) => value.Left >= Left && value.Top >= Top && value.Right <= Right && value.Bottom <= Bottom;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate bool NativePlacementMonitorCallback(nint monitor, nint dc, nint rectangle, nint data);

[StructLayout(LayoutKind.Sequential)]
internal struct NativePlacementRectInterop
{
    public int Left, Top, Right, Bottom;
    internal readonly NativePlacementRect Copy() => new(Left, Top, Right, Bottom);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativePlacementMonitorInfo
{
    public uint Size;
    public NativePlacementRectInterop Bounds, Work;
    public uint Flags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
}

// LUID is represented as its complete 8 bytes with pack 4, preserving native member alignment.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NativePlacementPathSource
{
    public ulong Adapter;
    public uint Id, ModeIndex, Status;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NativePlacementPathTarget
{
    public ulong Adapter;
    public uint Id, ModeIndex;
    public int Technology;
    public uint Rotation, Scaling, RefreshNumerator, RefreshDenominator, Scanline;
    public int Available;
    public uint Status;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NativePlacementPath
{
    public NativePlacementPathSource Source;
    public NativePlacementPathTarget Target;
    public uint Flags;
}

// DISPLAYCONFIG_MODE_INFO's union occupies 48 bytes. Only SOURCE_MODE members are read,
// and only after checking infoType/source identity; the target/desktop unions remain opaque.
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct NativePlacementMode
{
    [FieldOffset(0)] public uint Kind;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public ulong Adapter;
    [FieldOffset(16)] public uint Width;
    [FieldOffset(20)] public uint Height;
    [FieldOffset(24)] public uint PixelFormat;
    [FieldOffset(28)] public int X;
    [FieldOffset(32)] public int Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct NativePlacementDeviceHeader
{
    public uint Kind, Size;
    public ulong Adapter;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
internal struct NativePlacementSourceName
{
    public NativePlacementDeviceHeader Header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
internal struct NativePlacementTargetName
{
    public NativePlacementDeviceHeader Header;
    public uint Flags;
    public int Technology;
    public ushort EdidManufacturer, EdidProduct;
    public uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
}
