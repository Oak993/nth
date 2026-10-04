// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): DXGI_ADAPTER_DESC1 (V-10.1 Graphics row).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>DXGI_ADAPTER_DESC1. <see cref="Flags"/> bit 2 is DXGI_ADAPTER_FLAG_SOFTWARE.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DxgiAdapterDesc1
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public uint LuidLow;
    public int LuidHigh;
    public uint Flags;
}
