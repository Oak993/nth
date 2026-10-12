// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-5, ED-NEW-pcctl-9; DESIGN.md 3.1 item 5). The
// hand-written Direct3D 11 and WinRT interop under WgcWindowCapture (the repo carries no D3D wrapper). COM methods are called
// through their vtable slots; every GUID and slot number below was checked against the Windows SDK 10.0.26100 headers
// (d3d11.h, d3d10.h, dxgi.h, windows.graphics.capture.h, windows.graphics.capture.interop.h,
// windows.graphics.directx.direct3d11.interop.h). Only native runs reach this file; tests never do.
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace Marrow.App.Port.PcControl;

internal static class WgcInterop
{
    public static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IidD3D10Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");
    public static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IidDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    public static readonly Guid IidCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    public static readonly Guid IidCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    public static readonly Guid IidCaptureSession3 = new("f2cdd966-22ae-5ea1-9596-3a289344c3be"); // IsBorderRequired
    public static readonly Guid IidCaptureSession5 = new("67c0ea62-1f85-5061-925a-239be0ac09cb"); // MinUpdateInterval
    public static readonly Guid IidCaptureAccessStatics = new("743ed370-06ec-5040-a58a-901f0f757095");
    public static readonly Guid IidAsyncInfo = new("00000036-0000-0000-c000-000000000046");

    public const int DriverTypeHardware = 1;
    public const int DriverTypeWarp = 5;
    public const uint CreateBgraSupport = 0x20;
    public const uint SdkVersion = 7;
    public const uint FormatB8G8R8A8 = 87;
    public const uint UsageStaging = 3;
    public const uint CpuAccessRead = 0x20000;
    public const int MapRead = 1;

    // Vtable slots (IUnknown is 0 to 2; IInspectable adds 3 to 5).
    public const int SlotQueryInterface = 0;
    public const int SlotDeviceCreateTexture2D = 5;
    public const int SlotContextMap = 14;
    public const int SlotContextUnmap = 15;
    public const int SlotContextCopySubresourceRegion = 46;
    public const int SlotTextureGetDesc = 10;
    public const int SlotMultithreadSetProtected = 5;
    public const int SlotDxgiAccessGetInterface = 3;
    public const int SlotItemInteropCreateForWindow = 3;
    public const int SlotPutFirstProperty = 7; // a WinRT interface's first property: get at 6, put at 7
    public const int SlotAccessStaticsRequestAccess = 6;
    public const int SlotAsyncInfoStatus = 7;
    public const int SlotAsyncOperationGetResults = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Box
    {
        public uint Left;
        public uint Top;
        public uint Front;
        public uint Right;
        public uint Bottom;
        public uint Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MappedSubresource
    {
        public nint Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int QueryInterfaceFn(nint self, ref Guid iid, out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int CreateTexture2DFn(nint self, ref Texture2DDesc desc, nint initialData, out nint texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int MapFn(nint self, nint resource, uint subresource, int mapType, uint flags, out MappedSubresource mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void UnmapFn(nint self, nint resource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void CopySubresourceRegionFn(nint self, nint destination, uint destinationSubresource, uint x, uint y, uint z, nint source, uint sourceSubresource, ref Box box);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void GetDescFn(nint self, out Texture2DDesc desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetProtectedFn(nint self, int protect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetInterfaceFn(nint self, ref Guid iid, out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int CreateForWindowFn(nint self, nint window, ref Guid iid, out nint result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PutBooleanFn(nint self, byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int PutTimeSpanFn(nint self, long ticks);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int RequestAccessFn(nint self, int kind, out nint operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetInt32Fn(nint self, out int value);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int WindowsDeleteString(nint hstring);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);

    /// <summary>The delegate in a COM object's vtable slot.</summary>
    public static T Slot<T>(nint self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));

    /// <summary>QueryInterface; zero when the object does not have the interface.</summary>
    public static nint Query(nint self, Guid iid)
    {
        if (self == 0) return 0;
        return Slot<QueryInterfaceFn>(self, SlotQueryInterface)(self, ref iid, out var result) >= 0 ? result : 0;
    }

    public static void Release(ref nint pointer)
    {
        if (pointer != 0) Marshal.Release(pointer);
        pointer = 0;
    }

    /// <summary>A runtime class's activation factory, as the given interface.</summary>
    public static nint Factory(string runtimeClass, Guid iid)
    {
        Check(WindowsCreateString(runtimeClass, runtimeClass.Length, out var name));
        try
        {
            return RoGetActivationFactory(name, ref iid, out var factory) >= 0 ? factory : 0;
        }
        finally
        {
            _ = WindowsDeleteString(name);
        }
    }

    /// <summary>The ABI pointer behind a projected WinRT object (no reference added).</summary>
    public static nint Abi(object projected) => ((WinRT.IWinRTObject)projected).NativeObject.ThisPtr;

    public static void Check(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    /// <summary>
    /// The shared Direct3D 11 device: hardware, else WARP; multithread-protected because capture pools of several windows
    /// use it from their own threads. Calls on the immediate context go through <see cref="Context"/>'s lock.
    /// </summary>
    public sealed class Device : IDisposable
    {
        private nint device;
        private nint context;
        private readonly MapFn map;
        private readonly UnmapFn unmap;
        private readonly CopySubresourceRegionFn copy;
        private readonly CreateTexture2DFn createTexture;

        private Device(nint device, nint context, IDirect3DDevice winrt)
        {
            this.device = device;
            this.context = context;
            Winrt = winrt;
            map = Slot<MapFn>(context, SlotContextMap);
            unmap = Slot<UnmapFn>(context, SlotContextUnmap);
            copy = Slot<CopySubresourceRegionFn>(context, SlotContextCopySubresourceRegion);
            createTexture = Slot<CreateTexture2DFn>(device, SlotDeviceCreateTexture2D);
        }

        /// <summary>The same device as WinRT's IDirect3DDevice, for the frame pools.</summary>
        public IDirect3DDevice Winrt { get; }

        /// <summary>The lock every immediate-context call holds.</summary>
        public object Context { get; } = new();

        public static Device Create()
        {
            var hr = D3D11CreateDevice(0, DriverTypeHardware, 0, CreateBgraSupport, 0, 0, SdkVersion, out var device, out _, out var context);
            if (hr < 0) Check(D3D11CreateDevice(0, DriverTypeWarp, 0, CreateBgraSupport, 0, 0, SdkVersion, out device, out _, out context));
            var multithread = Query(device, IidD3D10Multithread);
            if (multithread != 0)
            {
                _ = Slot<SetProtectedFn>(multithread, SlotMultithreadSetProtected)(multithread, 1);
                Release(ref multithread);
            }
            var dxgi = Query(device, IidDxgiDevice);
            nint inspectable = 0;
            try
            {
                if (dxgi == 0) throw new InvalidOperationException("The Direct3D device has no DXGI device.");
                Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
                var winrt = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                return new Device(device, context, winrt);
            }
            catch
            {
                Release(ref context);
                Release(ref device);
                throw;
            }
            finally
            {
                Release(ref inspectable);
                Release(ref dxgi);
            }
        }

        /// <summary>A CPU-readable staging texture of the given size.</summary>
        public nint Staging(int width, int height)
        {
            var desc = new Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = FormatB8G8R8A8,
                SampleCount = 1,
                Usage = UsageStaging,
                CpuAccessFlags = CpuAccessRead,
            };
            Check(createTexture(device, ref desc, 0, out var texture));
            return texture;
        }

        /// <summary>
        /// The top-left width x height of <paramref name="source"/> copied into <paramref name="staging"/> on the GPU, then
        /// read back as packed top-down BGRA rows.
        /// </summary>
        public byte[] Read(nint source, nint staging, int width, int height)
        {
            var bytes = new byte[width * height * 4];
            var box = new Box { Right = (uint)width, Bottom = (uint)height, Back = 1 };
            lock (Context)
            {
                copy(context, staging, 0, 0, 0, 0, source, 0, ref box);
                Check(map(context, staging, 0, MapRead, 0, out var mapped));
                try
                {
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(mapped.Data + (nint)(y * (long)mapped.RowPitch), bytes, y * width * 4, width * 4);
                }
                finally
                {
                    unmap(context, staging, 0);
                }
            }
            return bytes;
        }

        public void Dispose()
        {
            (Winrt as IDisposable)?.Dispose();
            Release(ref context);
            Release(ref device);
        }
    }
}
