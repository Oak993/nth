using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

internal static class GpuBlobNativeCall
{
    internal static void Put(GpuProgramBlobStore store, byte[] data)
    {
        var key = Marshal.AllocHGlobal(8); var value = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, key, 8); Marshal.Copy(data, 0, value, data.Length);
            store.GetType().GetMethod("Put", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [key, (nint)8, value, (nint)data.Length]);
        }
        finally { Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(value); }
    }
    internal static byte[] Take(GpuProgramBlobStore store)
    {
        var key = Marshal.AllocHGlobal(8); var value = Marshal.AllocHGlobal(256);
        try
        {
            Marshal.Copy(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 0, key, 8);
            var count = (nint)store.GetType().GetMethod("Take", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [key, (nint)8, value, (nint)256])!;
            var bytes = new byte[(int)count]; Marshal.Copy(value, bytes, 0, bytes.Length); return bytes;
        }
        finally { Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(value); }
    }
}
