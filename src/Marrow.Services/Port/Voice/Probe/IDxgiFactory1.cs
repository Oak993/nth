// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): IDXGIFactory1 (V-10.1).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>IDXGIFactory1 with IDXGIObject's four slots and IDXGIFactory's five before <c>EnumAdapters1</c>. UNVERIFIED until the Windows run.</summary>
[ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiFactory1
{
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    [PreserveSig] int EnumAdapters();
    [PreserveSig] int MakeWindowAssociation();
    [PreserveSig] int GetWindowAssociation();
    [PreserveSig] int CreateSwapChain();
    [PreserveSig] int CreateSoftwareAdapter();

    /// <summary>S_OK, or DXGI_ERROR_NOT_FOUND (0x887A0002) past the last adapter.</summary>
    [PreserveSig]
    int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1 adapter);
}
