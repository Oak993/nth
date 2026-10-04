// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the real DXGI, Win32 and registry reads (voice-SPEC V-10.1).
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// <see cref="IHardwareSource"/> on a Windows machine. UNVERIFIED until the Windows verification run (its tests and the voice-evidence tool are Windows-only): the COM
/// declarations, the structure layouts and the registry matching are written from the Windows headers and have not run here.
/// </summary>
public sealed class WindowsHardwareSource : IHardwareSource
{
    private const uint SoftwareAdapterFlag = 2;
    private const int NotFound = unchecked((int)0x887A0002);
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public string ProcessorName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
            ?? throw new InvalidOperationException("The processor's registry key is missing.");
        return key.GetValue("ProcessorNameString") as string ?? throw new InvalidOperationException("ProcessorNameString is missing.");
    }

    public int PhysicalCores()
    {
        uint length = 0;
        Win32Native.GetLogicalProcessorInformationEx(Win32Native.RelationProcessorCore, 0, ref length);
        if (length == 0) throw new InvalidOperationException("GetLogicalProcessorInformationEx gave no size.");
        var buffer = Marshal.AllocHGlobal((nint)length);
        try
        {
            if (!Win32Native.GetLogicalProcessorInformationEx(Win32Native.RelationProcessorCore, buffer, ref length))
                throw new InvalidOperationException("GetLogicalProcessorInformationEx failed (" + Marshal.GetLastWin32Error() + ").");
            var cores = 0;
            for (var offset = 0; offset < length;)
            {
                if (Marshal.ReadInt32(buffer, offset) == Win32Native.RelationProcessorCore) cores += 1;
                offset += Marshal.ReadInt32(buffer, offset + 4);
            }
            return cores;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public long TotalPhysicalBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!Win32Native.GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("GlobalMemoryStatusEx failed (" + Marshal.GetLastWin32Error() + ").");
        return checked((long)status.TotalPhys);
    }

    public IReadOnlyList<GpuAdapterInfo> Adapters()
    {
        var hr = DxgiNative.CreateDXGIFactory1(DxgiNative.FactoryId, out var factoryObject);
        if (hr < 0) throw new InvalidOperationException("CreateDXGIFactory1 failed (0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ").");
        var factory = (IDxgiFactory1)factoryObject;
        var adapters = new List<GpuAdapterInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var result = factory.EnumAdapters1(index, out var adapter);
                if (result == NotFound) break;
                if (result < 0) throw new InvalidOperationException("EnumAdapters1 failed (0x" + result.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ").");
                try
                {
                    adapters.Add(Describe(adapter));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return adapters;
    }

    // The driver version is the adapter's "DriverVersion" value under the display class key whose "MatchingDeviceId" names the adapter's PCI vendor and device ids. An adapter
    // with no such key gives "" (ENGINEERING DECISION: the row never shows a driver, only the hardware key and the profile carry it).
    public string DriverVersion(GpuAdapterInfo adapter)
    {
        using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (classKey is null) return "";
        var id = "ven_" + adapter.VendorId.ToString("x4", System.Globalization.CultureInfo.InvariantCulture) + "&dev_" + adapter.DeviceId.ToString("x4", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var name in classKey.GetSubKeyNames())
        {
            using var key = classKey.OpenSubKey(name);
            if (key?.GetValue("MatchingDeviceId") is string matching && matching.Contains(id, StringComparison.OrdinalIgnoreCase)
                && key.GetValue("DriverVersion") is string version) return version;
        }
        return "";
    }

    private static GpuAdapterInfo Describe(IDxgiAdapter1 adapter)
    {
        var hr = adapter.GetDesc1(out var desc);
        if (hr < 0) throw new InvalidOperationException("GetDesc1 failed (0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ").");
        long free = 0;
        if (adapter is IDxgiAdapter3 memory && memory.QueryVideoMemoryInfo(0, 0, out var info) >= 0)
            free = checked((long)(info.Budget > info.CurrentUsage ? info.Budget - info.CurrentUsage : 0));
        return new GpuAdapterInfo(desc.Description, desc.VendorId, desc.DeviceId, checked((long)desc.DedicatedVideoMemory), free, (desc.Flags & SoftwareAdapterFlag) != 0);
    }
}
