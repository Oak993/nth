using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>ED-252 review: DXGI's adapter 0, the adapter Avalonia 12.1.2's ANGLE makes its D3D11 device on
/// (AngleWin32EglDisplay: EnumAdapters1(0) when no GraphicsAdapterSelectionCallback is set), read the way
/// GpuProgramCache reads each adapter. Read-only.</summary>
public static class DxgiAdapterZero
{
    /// <summary>Adapter 0's "vendor:device:subsystem:revision" in GpuProgramCache's form (null when DXGI cannot say).</summary>
    public static string? Ids()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (CreateDXGIFactory1(new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), out var factory) < 0 || factory == 0) return null;
        try
        {
            if (Method<EnumAdapters1>(factory, 12)(factory, 0, out var adapter) < 0 || adapter == 0) return null;
            try
            {
                if (Method<GetDesc1>(adapter, 10)(adapter, out var desc) < 0) return null;
                return $"{desc.VendorId:x4}:{desc.DeviceId:x4}:{desc.SubSysId:x8}:{desc.Revision:x2}";
            }
            finally { Marshal.Release(adapter); }
        }
        finally { Marshal.Release(factory); }
    }

    private static T Method<T>(nint self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * nint.Size));

    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1(nint self, out AdapterDesc1 desc);

    // DXGI_ADAPTER_DESC1
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }
}
