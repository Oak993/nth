// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): IDXGIAdapter1 (V-10.1).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// IDXGIAdapter1. A COM interface does not inherit its base's slots in .NET, so the base methods are declared in vtable order: IDXGIObject (4), IDXGIAdapter (3), then GetDesc1.
/// UNVERIFIED until the Windows run.
/// </summary>
[ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiAdapter1
{
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    [PreserveSig] int EnumOutputs();
    [PreserveSig] int GetDesc();
    [PreserveSig] int CheckInterfaceSupport();

    [PreserveSig]
    int GetDesc1(out DxgiAdapterDesc1 description);
}
