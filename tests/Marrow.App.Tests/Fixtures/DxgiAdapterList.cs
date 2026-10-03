using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>ED-252: DXGI's graphics adapters as IDXGIFactory1 enumerates them, read independently of GpuProgramCache (here
/// through the runtime's COM interop and declared interfaces, there through raw vtable slots): each adapter DXGI can describe,
/// in DXGI's order, with its ids, its description and its user-mode driver's version (IDXGIAdapter::CheckInterfaceSupport
/// for IDXGIDevice; null where DXGI gives none).</summary>
public static class DxgiAdapterList
{
    public sealed record Adapter(uint Vendor, uint Device, uint SubSystem, uint Revision, string Description, long? Driver);

    private const int NotFound = unchecked((int)0x887A0002); // DXGI_ERROR_NOT_FOUND: no adapter at that index

    public static IReadOnlyList<Adapter> Read()
    {
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(typeof(IDxgiFactory1).GUID, out var made));
        var factory = (IDxgiFactory1)made;
        var found = new List<Adapter>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var result = factory.EnumAdapters1(index, out var adapter);
                if (result == NotFound) break;
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    if (adapter.GetDesc1(out var desc) < 0) continue; // not described: GpuProgramCache names it nowhere
                    var device = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
                    long? driver = adapter.CheckInterfaceSupport(in device, out var umd) >= 0 ? umd : null;
                    var length = Array.IndexOf(desc.Description, '\0');
                    var description = new string(desc.Description, 0, length < 0 ? desc.Description.Length : length).Trim();
                    found.Add(new Adapter(desc.VendorId, desc.DeviceId, desc.SubSysId, desc.Revision, description, driver));
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
        }
        finally { Marshal.ReleaseComObject(factory); }
        return found;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    // the interfaces in their vtable order, each with its bases' methods first (IDXGIObject, then IDXGIFactory or IDXGIAdapter)
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDxgiFactory1
    {
        [PreserveSig] int SetPrivateData(in Guid name, uint size, nint data);
        [PreserveSig] int SetPrivateDataInterface(in Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(in Guid name, ref uint size, nint data);
        [PreserveSig] int GetParent(in Guid riid, out nint parent);
        [PreserveSig] int EnumAdapters(uint index, out nint adapter);
        [PreserveSig] int MakeWindowAssociation(nint window, uint flags);
        [PreserveSig] int GetWindowAssociation(out nint window);
        [PreserveSig] int CreateSwapChain(nint device, nint desc, out nint swapChain);
        [PreserveSig] int CreateSoftwareAdapter(nint module, out nint adapter);
        [PreserveSig] int EnumAdapters1(uint index, out IDxgiAdapter1 adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDxgiAdapter1
    {
        [PreserveSig] int SetPrivateData(in Guid name, uint size, nint data);
        [PreserveSig] int SetPrivateDataInterface(in Guid name, nint unknown);
        [PreserveSig] int GetPrivateData(in Guid name, ref uint size, nint data);
        [PreserveSig] int GetParent(in Guid riid, out nint parent);
        [PreserveSig] int EnumOutputs(uint index, out nint output);
        [PreserveSig] int GetDesc(nint desc);
        [PreserveSig] int CheckInterfaceSupport(in Guid name, out long umdVersion);
        [PreserveSig] int GetDesc1(out AdapterDescription desc);
    }

    // DXGI_ADAPTER_DESC1 (its description WCHARs: two bytes each)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public char[] Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }
}
