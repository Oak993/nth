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

    // PR-CHAT-190 (Core.Models.Timeline): DroppyCode/Core/Models/Timeline.swift:113-137 downscaledJPEG. Reuses the
    // orientation and scaler machinery above; nothing is written to disk (an in-memory IStream both ways), matching
    // the Swift side_effects note that the image is decoded and re-encoded only in memory.
    private static readonly Guid ContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    private static readonly Guid Bgr24 = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");

    public static byte[]? DownscaleToJpeg(byte[] data, long maxPixelSize, double quality)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) return null;
        IntPtr factory = IntPtr.Zero, inputStream = IntPtr.Zero, decoder = IntPtr.Zero, frame = IntPtr.Zero;
        IntPtr reader = IntPtr.Zero, oriented = IntPtr.Zero, scaled = IntPtr.Zero, converter = IntPtr.Zero;
        IntPtr outputStream = IntPtr.Zero, encoder = IntPtr.Zero, frameEncode = IntPtr.Zero, propertyBag = IntPtr.Zero;
        var namePtr = IntPtr.Zero;
        var debug = Environment.GetEnvironmentVariable("WIC_DEBUG") == "1";
        bool Fail(string step, int hr) { if (debug) throw new InvalidOperationException($"{step} hr=0x{hr:X8}"); return false; }
        try
        {
            var clsid = FactoryClass;
            var iid = FactoryInterface;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out factory);
            if (hr < 0) { Fail("CoCreateInstance", hr); return null; }

            inputStream = SHCreateMemStream(data, (uint)data.Length);
            if (inputStream == IntPtr.Zero) { Fail("SHCreateMemStream(input)", 0); return null; }
            hr = Method<CreateDecoderFromStream>(factory, 4)(factory, inputStream, IntPtr.Zero, 0, out decoder);
            if (hr < 0) { Fail("CreateDecoderFromStream", hr); return null; }
            hr = Method<GetFrame>(decoder, 13)(decoder, 0, out frame);
            if (hr < 0) { Fail("GetFrame", hr); return null; }
            hr = Method<GetSize>(frame, 3)(frame, out var rawWidth, out var rawHeight);
            if (hr < 0 || rawWidth == 0 || rawHeight == 0) { Fail("GetSize(frame)", hr); return null; }
            // :125 max(width, height) > maxPixelSize, on the raw (pre-orientation) dimensions; the maximum is the
            // same whichever side a 90-degree rotation would swap.
            if (Math.Max(rawWidth, rawHeight) <= maxPixelSize) return null;

            var orientation = ReadOrientation(frame, ref reader);
            IntPtr source = frame;
            var transform = orientation switch
            {
                2 => 0x8u, 3 => 0x2u, 4 => 0x10u, 5 => 0x9u,
                6 => 0x1u, 7 => 0xBu, 8 => 0x3u, _ => 0u
            };
            if (transform != 0)
            {
                hr = Method<CreateComponent>(factory, 13)(factory, out oriented);
                if (hr < 0) { Fail("CreateComponent(flip)", hr); return null; }
                hr = Method<InitializeFlip>(oriented, 8)(oriented, source, transform);
                if (hr < 0) { Fail("InitializeFlip", hr); return null; }
                source = oriented;
            }

            hr = Method<GetSize>(source, 3)(source, out var width, out var height);
            if (hr < 0) { Fail("GetSize(source)", hr); return null; }
            var longest = Math.Max(width, height);
            if (longest > maxPixelSize)
            {
                var targetWidth = Math.Max(1u, checked((uint)Math.Round((double)width * maxPixelSize / longest, MidpointRounding.AwayFromZero)));
                var targetHeight = Math.Max(1u, checked((uint)Math.Round((double)height * maxPixelSize / longest, MidpointRounding.AwayFromZero)));
                hr = Method<CreateComponent>(factory, 11)(factory, out scaled);
                if (hr < 0) { Fail("CreateComponent(scaler)", hr); return null; }
                hr = Method<InitializeScaler>(scaled, 8)(scaled, source, targetWidth, targetHeight, 4);
                if (hr < 0) { Fail("InitializeScaler", hr); return null; }
                source = scaled;
                width = targetWidth; height = targetHeight;
            }

            // JPEG needs an explicit pixel format before WriteSource; WriteSource alone left the frame in the
            // wrong state for Commit (WINCODEC_ERR_WRONGSTATE) when no SetPixelFormat call had been made.
            hr = Method<CreateComponent>(factory, 10)(factory, out converter);
            if (hr < 0) { Fail("CreateComponent(converter)", hr); return null; }
            var pixelFormat = Bgr24;
            hr = Method<InitializeConverter>(converter, 8)(converter, source, ref pixelFormat, 0, IntPtr.Zero, 0, 0);
            if (hr < 0) { Fail("InitializeConverter", hr); return null; }
            source = converter;

            outputStream = SHCreateMemStream(null!, 0);
            if (outputStream == IntPtr.Zero) { Fail("SHCreateMemStream(output)", 0); return null; }
            var containerFormat = ContainerFormatJpeg;
            hr = Method<CreateEncoder>(factory, 8)(factory, ref containerFormat, IntPtr.Zero, out encoder);
            if (hr < 0) { Fail("CreateEncoder", hr); return null; }
            hr = Method<EncoderInitialize>(encoder, 3)(encoder, outputStream, 2 /* WICBitmapEncoderNoCache */);
            if (hr < 0) { Fail("Encoder.Initialize", hr); return null; }
            hr = Method<CreateNewFrame>(encoder, 10)(encoder, out frameEncode, out propertyBag);
            if (hr < 0) { Fail("CreateNewFrame", hr); return null; }

            if (propertyBag != IntPtr.Zero)
            {
                namePtr = Marshal.StringToCoTaskMemUni("ImageQuality");
                var option = new PropBag2 { VariantType = 4 /* VT_R4 */, Name = namePtr }; // :134 kCGImageDestinationLossyCompressionQuality
                var value = new Variant { VariantType = 4 /* VT_R4 */, Float = (float)quality };
                Method<PropertyBagWrite>(propertyBag, 4)(propertyBag, 1, ref option, ref value);
            }

            hr = Method<FrameInitialize>(frameEncode, 3)(frameEncode, propertyBag);
            if (hr < 0) { Fail("Frame.Initialize", hr); return null; }
            hr = Method<FrameSetSize>(frameEncode, 4)(frameEncode, width, height);
            if (hr < 0) { Fail("Frame.SetSize", hr); return null; }
            var framePixelFormat = Bgr24;
            hr = Method<FrameSetPixelFormat>(frameEncode, 6)(frameEncode, ref framePixelFormat);
            if (hr < 0) { Fail("Frame.SetPixelFormat", hr); return null; }
            hr = Method<WriteSource>(frameEncode, 11)(frameEncode, source, IntPtr.Zero);
            if (hr < 0) { Fail("Frame.WriteSource", hr); return null; }
            hr = Method<SimpleCall>(frameEncode, 12)(frameEncode);
            if (hr < 0) { Fail("Frame.Commit", hr); return null; }
            hr = Method<SimpleCall>(encoder, 11)(encoder);
            if (hr < 0) { Fail("Encoder.Commit", hr); return null; }

            hr = Method<StreamSeek>(outputStream, 5)(outputStream, 0, 0, out _);
            if (hr < 0) { Fail("Stream.Seek", hr); return null; }
            using var result = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                hr = Method<StreamRead>(outputStream, 3)(outputStream, buffer, (uint)buffer.Length, out var read);
                if (hr < 0) { Fail("Stream.Read", hr); return null; }
                if (read == 0) break;
                result.Write(buffer, 0, (int)read);
            }
            return result.ToArray();
        }
        catch (Exception error) when (error is COMException or OverflowException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (namePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(namePtr);
            Release(ref propertyBag); Release(ref frameEncode); Release(ref encoder); Release(ref outputStream);
            Release(ref converter); Release(ref scaled); Release(ref oriented); Release(ref reader); Release(ref frame);
            Release(ref decoder); Release(ref inputStream); Release(ref factory);
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

    // OAIdl.h PROPBAG2, natural x64 layout (40 bytes: 4+2+pad2+4+4+pad4+8+16).
    [StructLayout(LayoutKind.Sequential)]
    private struct PropBag2
    {
        public uint Type;
        public ushort VariantType;
        public uint ClipFormat;
        public uint Hint;
        public IntPtr Name;
        public Guid Clsid;
    }

    // oaidl.h VARIANT, the header (vt + 3 reserved ushorts = 8 bytes) then the union at offset 8; 16 bytes on x64.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public float Float;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseCom(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int CreateDecoderFromFilename(IntPtr self, string filename, IntPtr vendor, uint access, uint options, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDecoderFromStream(IntPtr self, IntPtr stream, IntPtr vendor, uint metadataOptions, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFrame(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSize(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateComponent(IntPtr self, out IntPtr component);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeFlip(IntPtr self, IntPtr source, uint transform);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeScaler(IntPtr self, IntPtr source, uint width, uint height, uint interpolation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeConverter(IntPtr self, IntPtr source, ref Guid format, uint dither, IntPtr palette, double alphaThreshold, uint paletteType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CopyPixels(IntPtr self, IntPtr rectangle, uint stride, uint size, [Out] byte[] pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetMetadataReader(IntPtr self, out IntPtr reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetMetadataByName(IntPtr self, string query, ref PropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateEncoder(IntPtr self, ref Guid containerFormat, IntPtr vendor, out IntPtr encoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EncoderInitialize(IntPtr self, IntPtr stream, uint cacheOption);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateNewFrame(IntPtr self, out IntPtr frameEncode, out IntPtr propertyBag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SimpleCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FrameInitialize(IntPtr self, IntPtr propertyBag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FrameSetSize(IntPtr self, uint width, uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FrameSetPixelFormat(IntPtr self, ref Guid pixelFormat);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSource(IntPtr self, IntPtr source, IntPtr rectangle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PropertyBagWrite(IntPtr self, uint count, ref PropBag2 propBag, ref Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StreamSeek(IntPtr self, long move, uint origin, out ulong newPosition);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StreamRead(IntPtr self, [Out] byte[] buffer, uint count, out uint read);

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shlwapi.dll")] private static extern IntPtr SHCreateMemStream(byte[]? init, uint size);
}
