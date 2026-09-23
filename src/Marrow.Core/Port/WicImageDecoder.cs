// PORT-ONLY: Windows Imaging Component decodes Core thumbnails into an eager, UI-neutral BGRA pixel buffer.
using System.Runtime.InteropServices;

namespace Marrow.Core.Port;

public sealed record DecodedBitmap(int Width, int Height, int BytesPerRow, byte[] Pixels);

public static class WicImageDecoder
{
    private static readonly Guid FactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid FactoryInterface = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid Pbgra32 = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    public static DecodedBitmap? Decode(string path, long requestedPixels, bool fillingSquare)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) return null;
        IntPtr factory = IntPtr.Zero, decoder = IntPtr.Zero, frame = IntPtr.Zero;
        IntPtr reader = IntPtr.Zero, oriented = IntPtr.Zero, scaled = IntPtr.Zero, converter = IntPtr.Zero;
        try
        {
            var clsid = FactoryClass;
            var iid = FactoryInterface;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out factory) < 0)
                return null;
            if (Method<CreateDecoderFromFilename>(factory, 3)(factory, path, IntPtr.Zero, 0x80000000, 0, out decoder) < 0)
                return null;
            if (Method<GetFrame>(decoder, 13)(decoder, 0, out frame) < 0) return null;
            if (Method<GetSize>(frame, 3)(frame, out var width, out var height) < 0 || width == 0 || height == 0)
                return null;

            var targetPixels = requestedPixels;
            if (fillingSquare)
            {
                var ratioPixels = Math.Ceiling((double)requestedPixels * Math.Max(width, height) / Math.Min(width, height));
                targetPixels = (long)Math.Min(2048d, ratioPixels);
            }

            var orientation = ReadOrientation(frame, ref reader);
            IntPtr source = frame;
            var transform = orientation switch
            {
                2 => 0x8u, 3 => 0x2u, 4 => 0x10u, 5 => 0x9u,
                6 => 0x1u, 7 => 0xBu, 8 => 0x3u, _ => 0u
            };
            if (transform != 0)
            {
                if (Method<CreateComponent>(factory, 13)(factory, out oriented) < 0) return null;
                if (Method<InitializeFlip>(oriented, 8)(oriented, source, transform) < 0) return null;
                source = oriented;
            }

            if (Method<GetSize>(source, 3)(source, out width, out height) < 0) return null;
            var longest = Math.Max(width, height);
            if (targetPixels < longest)
            {
                var targetWidth = Math.Max(1u, checked((uint)Math.Round((double)width * targetPixels / longest, MidpointRounding.AwayFromZero)));
                var targetHeight = Math.Max(1u, checked((uint)Math.Round((double)height * targetPixels / longest, MidpointRounding.AwayFromZero)));
                if (Method<CreateComponent>(factory, 11)(factory, out scaled) < 0) return null;
                if (Method<InitializeScaler>(scaled, 8)(scaled, source, targetWidth, targetHeight, 4) < 0) return null;
                source = scaled;
            }

            if (Method<CreateComponent>(factory, 10)(factory, out converter) < 0) return null;
            var format = Pbgra32;
            if (Method<InitializeConverter>(converter, 8)(converter, source, ref format, 0, IntPtr.Zero, 0, 0) < 0)
                return null;
            if (Method<GetSize>(converter, 3)(converter, out width, out height) < 0) return null;
            var stride = checked((int)width * 4);
            var pixels = new byte[checked(stride * (int)height)];
            if (Method<CopyPixels>(converter, 7)(converter, IntPtr.Zero, (uint)stride, (uint)pixels.Length, pixels) < 0)
                return null;
            return new DecodedBitmap((int)width, (int)height, stride, pixels);
        }
        catch (Exception error) when (error is COMException or OverflowException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            Release(ref converter); Release(ref scaled); Release(ref oriented); Release(ref reader);
            Release(ref frame); Release(ref decoder); Release(ref factory);
            if (initialized >= 0) CoUninitialize();
        }
    }

    private static ushort ReadOrientation(IntPtr frame, ref IntPtr reader)
    {
        if (Method<GetMetadataReader>(frame, 8)(frame, out reader) < 0) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            var value = new PropVariant();
            try
            {
                if (Method<GetMetadataByName>(reader, 5)(reader, query, ref value) >= 0 && value.VariantType == 18)
                    return value.UnsignedShort;
            }
            finally { PropVariantClear(ref value); }
        }
        return 1;
    }

    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    private static void Release(ref IntPtr instance)
    {
        if (instance == IntPtr.Zero) return;
        Method<ReleaseCom>(instance, 2)(instance);
        instance = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public ushort UnsignedShort;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseCom(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int CreateDecoderFromFilename(IntPtr self, string filename, IntPtr vendor, uint access, uint options, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFrame(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSize(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateComponent(IntPtr self, out IntPtr component);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeFlip(IntPtr self, IntPtr source, uint transform);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeScaler(IntPtr self, IntPtr source, uint width, uint height, uint interpolation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeConverter(IntPtr self, IntPtr source, ref Guid format, uint dither, IntPtr palette, double alphaThreshold, uint paletteType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CopyPixels(IntPtr self, IntPtr rectangle, uint stride, uint size, [Out] byte[] pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetMetadataReader(IntPtr self, out IntPtr reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetMetadataByName(IntPtr self, string query, ref PropVariant value);

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}
