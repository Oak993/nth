// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): IDXGIAdapter3 (V-10.1 free memory).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>IDXGIAdapter3, declared with the slots of IDXGIObject (4), IDXGIAdapter (3), IDXGIAdapter1 (1) and IDXGIAdapter2 (1) before its own. UNVERIFIED until the Windows run.</summary>
[ComImport, Guid("645967a4-1392-4310-a798-8053ce3e93fd"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiAdapter3
{
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    [PreserveSig] int EnumOutputs();
    [PreserveSig] int GetDesc();
    [PreserveSig] int CheckInterfaceSupport();
    [PreserveSig] int GetDesc1();
    [PreserveSig] int GetDesc2();
    [PreserveSig] int RegisterHardwareContentProtectionTeardownStatusEvent();
    [PreserveSig] int UnregisterHardwareContentProtectionTeardownStatus();

    /// <param name="nodeIndex">0.</param>
    /// <param name="segmentGroup">DXGI_MEMORY_SEGMENT_GROUP_LOCAL = 0.</param>
    [PreserveSig]
    int QueryVideoMemoryInfo(uint nodeIndex, int segmentGroup, out DxgiVideoMemoryInfo info);
}
