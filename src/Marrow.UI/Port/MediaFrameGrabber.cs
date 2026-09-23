// PORT-ONLY: the Windows equivalent of AVFoundation's track loading and AVAssetImageGenerator that
// AttachmentPreview.swift's PreviewVideos.thumbnail (:153-165) and AttachmentLargePreview.prefetchVideoSize
// (:266-279) read through: a video's natural frame size and rotation (Media Foundation's IMFSourceReader native
// media type), and one decoded frame near a given time, rotation applied. Source: draft C, in the same raw-vtable
// COM style as WicImageDecoder.cs (Marrow.Core.Port), because IMFSourceReader/IMFMediaType/IMFSample/IMFMediaBuffer
// carry no .NET wrapper in this tree and no package may be added (section 1.3).
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>A video's natural pixel size with its rotation already applied, both sides positive (AttachmentPreview.swift:274-275).</summary>
public readonly record struct VideoNaturalSize(double Width, double Height);

public static class MediaFrameGrabber
{
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_VIDEO_ROTATION = new("c380465d-2271-428c-9b83-ecea3b4a85c1");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    private const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;

    /// <summary>The first video track's natural size (Media Foundation's native media type, stream 0), rotation
    /// applied by swapping the sides for a 90 or 270 degree rotation, both sides made positive. Null when the
    /// file has no readable video track, matching a failed <c>loadTracks(withMediaType:)</c>/<c>load(...)</c>.</summary>
    public static VideoNaturalSize? NaturalSize(string path)
    {
        return WithSourceReader(path, reader =>
        {
            var native = IntPtr.Zero;
            try
            {
                if (GetNativeMediaType(reader, 0, out native) < 0 || native == IntPtr.Zero) return (VideoNaturalSize?)null;
                if (!TryGetFrameSize(native, out var width, out var height)) return (VideoNaturalSize?)null;
                var rotation = GetRotation(native);
                return rotation is 90 or 270
                    ? new VideoNaturalSize(height, width)
                    : new VideoNaturalSize(width, height);
            }
            finally
            {
                Release(native);
            }
        });
    }

    /// <summary>One frame near <paramref name="atSeconds"/> into the video, rotation applied, downscaled (never
    /// upscaled) to fit inside a <paramref name="maxPixels"/> square: the Windows counterpart of
    /// AVAssetImageGenerator's maximumSize cap with appliesPreferredTrackTransform (AttachmentPreview.swift:158-163).
    /// Null on any failure, so the caller's placeholder stays, exactly as a failed <c>try? await generator.image</c>.</summary>
    public static DecodedBitmap? Frame(string path, long maxPixels, double atSeconds)
    {
        return WithSourceReader(path, reader =>
        {
            var rotation = 0u;
            var native = IntPtr.Zero;
            try
            {
                if (GetNativeMediaType(reader, 0, out native) == 0 && native != IntPtr.Zero) rotation = GetRotation(native);
            }
            finally
            {
                Release(native);
            }

            var outputType = IntPtr.Zero;
            try
            {
                if (MFCreateMediaType(out outputType) < 0) return (DecodedBitmap?)null;
                var major = MFMediaType_Video;
                var subtype = MFVideoFormat_RGB32;
                Attr_SetGUID(outputType, MF_MT_MAJOR_TYPE, ref major);
                Attr_SetGUID(outputType, MF_MT_SUBTYPE, ref subtype);
                if (SetCurrentMediaType(reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, IntPtr.Zero, outputType) < 0) return (DecodedBitmap?)null;
            }
            finally
            {
                Release(outputType);
            }

            var current = IntPtr.Zero;
            int width, height, stride;
            try
            {
                if (GetCurrentMediaType(reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, out current) < 0 || current == IntPtr.Zero) return (DecodedBitmap?)null;
                if (!TryGetFrameSize(current, out width, out height)) return (DecodedBitmap?)null;
                stride = Attr_GetUINT32(current, MF_MT_DEFAULT_STRIDE, out var s) ? unchecked((int)s) : width * 4;
                if (stride < 0) stride = -stride; // a negative default stride marks a bottom-up layout; the buffer itself is still read top-down below
            }
            finally
            {
                Release(current);
            }

            if (!Seek(reader, atSeconds)) return (DecodedBitmap?)null;
            var buffer = ReadFrameBuffer(reader, out var byteCount);
            if (buffer is null) return (DecodedBitmap?)null;

            var expected = checked(stride * height);
            if (byteCount < expected) return (DecodedBitmap?)null;
            var frame = new DecodedBitmap(width, height, stride, buffer);
            frame = rotation switch
            {
                90 => Rotate90(frame),
                180 => Rotate180(frame),
                270 => Rotate270(frame),
                _ => frame,
            };
            return DownscaleToFit(frame, maxPixels);
        });
    }

    private static T? WithSourceReader<T>(string path, Func<IntPtr, T?> body)
    {
        if (!OperatingSystem.IsWindows()) return default;
        var comInit = CoInitializeEx(IntPtr.Zero, 0);
        if (comInit < 0 && comInit != unchecked((int)0x80010106)) return default;
        var mfStarted = MFStartup(0x20070 /* MF_VERSION */, 0) >= 0;
        IntPtr reader = IntPtr.Zero;
        try
        {
            if (!mfStarted) return default;
            if (MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader) < 0 || reader == IntPtr.Zero) return default;
            return body(reader);
        }
        catch (Exception error) when (error is COMException or OverflowException or OutOfMemoryException)
        {
            return default;
        }
        finally
        {
            Release(reader);
            if (mfStarted) MFShutdown();
            if (comInit >= 0) CoUninitialize();
        }
    }

    private static bool TryGetFrameSize(IntPtr mediaType, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!Attr_GetUINT64(mediaType, MF_MT_FRAME_SIZE, out var packed)) return false;
        width = (int)(packed >> 32);
        height = (int)(packed & 0xFFFFFFFF);
        return width > 0 && height > 0;
    }

    private static uint GetRotation(IntPtr mediaType) => Attr_GetUINT32(mediaType, MF_MT_VIDEO_ROTATION, out var rotation) ? rotation : 0;

    private static bool Seek(IntPtr reader, double seconds)
    {
        var position = new PropVariantI8 { vt = 20 /* VT_I8 */, hVal = (long)(seconds * 10_000_000.0) };
        var guidNull = Guid.Empty;
        return SetCurrentPosition(reader, ref guidNull, ref position) >= 0;
    }

    private static byte[]? ReadFrameBuffer(IntPtr reader, out int byteCount)
    {
        byteCount = 0;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var sample = IntPtr.Zero;
            try
            {
                var hr = ReadSample(reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, 0, out _, out var flags, out _, out sample);
                if (hr < 0) return null;
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0) return null;
                if (sample == IntPtr.Zero) continue;
                return CopySampleBytes(sample, out byteCount);
            }
            finally
            {
                Release(sample);
            }
        }
        return null;
    }

    private static byte[]? CopySampleBytes(IntPtr sample, out int byteCount)
    {
        byteCount = 0;
        var mediaBuffer = IntPtr.Zero;
        try
        {
            if (ConvertToContiguousBuffer(sample, out mediaBuffer) < 0 || mediaBuffer == IntPtr.Zero) return null;
            if (Buffer_Lock(mediaBuffer, out var scan0, out _, out var currentLength) < 0) return null;
            try
            {
                var bytes = new byte[currentLength];
                Marshal.Copy(scan0, bytes, 0, (int)currentLength);
                byteCount = (int)currentLength;
                return bytes;
            }
            finally
            {
                Buffer_Unlock(mediaBuffer);
            }
        }
        finally
        {
            Release(mediaBuffer);
        }
    }

    // ENGINEERING DECISION: the source reader is left at the native frame size (no forced SetCurrentMediaType frame
    // size), so the pixel cap AVAssetImageGenerator.maximumSize applies at decode time is applied here afterwards,
    // by nearest-neighbour downscaling in managed code; the observable result (a frame no larger than the box) is
    // the same, only the place the resize happens differs.
    private static DecodedBitmap DownscaleToFit(DecodedBitmap image, long maxPixels)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= maxPixels || maxPixels <= 0) return image;
        var scale = (double)maxPixels / longest;
        var targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var stride = targetWidth * 4;
        var pixels = new byte[stride * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(image.Height - 1, (int)((long)y * image.Height / targetHeight));
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(image.Width - 1, (int)((long)x * image.Width / targetWidth));
                Array.Copy(image.Pixels, sourceY * image.BytesPerRow + sourceX * 4, pixels, y * stride + x * 4, 4);
            }
        }
        return new DecodedBitmap(targetWidth, targetHeight, stride, pixels);
    }

    private static DecodedBitmap Rotate90(DecodedBitmap image)
    {
        var stride = image.Height * 4;
        var pixels = new byte[stride * image.Width];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var destX = image.Height - 1 - y;
                var destY = x;
                Array.Copy(image.Pixels, y * image.BytesPerRow + x * 4, pixels, destY * stride + destX * 4, 4);
            }
        return new DecodedBitmap(image.Height, image.Width, stride, pixels);
    }

    private static DecodedBitmap Rotate270(DecodedBitmap image)
    {
        var stride = image.Height * 4;
        var pixels = new byte[stride * image.Width];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var destX = y;
                var destY = image.Width - 1 - x;
                Array.Copy(image.Pixels, y * image.BytesPerRow + x * 4, pixels, destY * stride + destX * 4, 4);
            }
        return new DecodedBitmap(image.Height, image.Width, stride, pixels);
    }

    private static DecodedBitmap Rotate180(DecodedBitmap image)
    {
        var stride = image.Width * 4;
        var pixels = new byte[stride * image.Height];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var destX = image.Width - 1 - x;
                var destY = image.Height - 1 - y;
                Array.Copy(image.Pixels, y * image.BytesPerRow + x * 4, pixels, destY * stride + destX * 4, 4);
            }
        return new DecodedBitmap(image.Width, image.Height, stride, pixels);
    }

    private static void Release(IntPtr instance)
    {
        if (instance == IntPtr.Zero) return;
        Method<ReleaseCom>(instance, 2)(instance);
    }

    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    // IMFAttributes (IMFMediaType's base): 7 GetUINT32, 8 GetUINT64, 21 SetUINT32, 22 SetUINT64, 24 SetGUID.
    private static bool Attr_GetUINT32(IntPtr attributes, Guid key, out uint value)
    {
        var v = 0u;
        var hr = Method<GetUINT32Del>(attributes, 7)(attributes, ref key, out v);
        value = v;
        return hr >= 0;
    }

    private static bool Attr_GetUINT64(IntPtr attributes, Guid key, out ulong value)
    {
        var v = 0UL;
        var hr = Method<GetUINT64Del>(attributes, 8)(attributes, ref key, out v);
        value = v;
        return hr >= 0;
    }

    private static void Attr_SetGUID(IntPtr attributes, Guid key, ref Guid value) =>
        Method<SetGUIDDel>(attributes, 24)(attributes, ref key, ref value);

    // IMFMediaBuffer: 3 Lock, 4 Unlock.
    private static int Buffer_Lock(IntPtr buffer, out IntPtr scan0, out uint maxLength, out uint currentLength) =>
        Method<LockDel>(buffer, 3)(buffer, out scan0, out maxLength, out currentLength);

    private static void Buffer_Unlock(IntPtr buffer) => Method<UnlockDel>(buffer, 4)(buffer);

    // IMFSourceReader: 5 GetNativeMediaType, 6 GetCurrentMediaType, 7 SetCurrentMediaType, 8 SetCurrentPosition, 9 ReadSample.
    private static int GetNativeMediaType(IntPtr reader, uint index, out IntPtr mediaType) =>
        Method<GetMediaTypeDel>(reader, 5)(reader, MF_SOURCE_READER_FIRST_VIDEO_STREAM, index, out mediaType);

    private static int GetCurrentMediaType(IntPtr reader, uint streamIndex, out IntPtr mediaType) =>
        Method<GetCurrentMediaTypeDel>(reader, 6)(reader, streamIndex, out mediaType);

    private static int SetCurrentMediaType(IntPtr reader, uint streamIndex, IntPtr reserved, IntPtr mediaType) =>
        Method<SetCurrentMediaTypeDel>(reader, 7)(reader, streamIndex, reserved, mediaType);

    private static int SetCurrentPosition(IntPtr reader, ref Guid timeFormat, ref PropVariantI8 position) =>
        Method<SetCurrentPositionDel>(reader, 8)(reader, ref timeFormat, ref position);

    private static int ReadSample(IntPtr reader, uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out IntPtr sample) =>
        Method<ReadSampleDel>(reader, 9)(reader, streamIndex, controlFlags, out actualStreamIndex, out streamFlags, out timestamp, out sample);

    // IMFSample: 41 ConvertToContiguousBuffer.
    private static int ConvertToContiguousBuffer(IntPtr sample, out IntPtr buffer) =>
        Method<ConvertToContiguousBufferDel>(sample, 41)(sample, out buffer);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariantI8
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long hVal;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseCom(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUINT32Del(IntPtr self, ref Guid key, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUINT64Del(IntPtr self, ref Guid key, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGUIDDel(IntPtr self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockDel(IntPtr self, out IntPtr scan0, out uint maxLength, out uint currentLength);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UnlockDel(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetMediaTypeDel(IntPtr self, uint streamIndex, uint typeIndex, out IntPtr mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCurrentMediaTypeDel(IntPtr self, uint streamIndex, out IntPtr mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentMediaTypeDel(IntPtr self, uint streamIndex, IntPtr reserved, IntPtr mediaType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentPositionDel(IntPtr self, ref Guid timeFormat, ref PropVariantI8 position);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSampleDel(IntPtr self, uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ConvertToContiguousBufferDel(IntPtr self, out IntPtr buffer);

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr mediaType);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr sourceReader);
}
