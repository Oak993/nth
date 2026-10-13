// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 16.2; unit AS-19; ED-NEW-dextra-as19-1, -9). No Swift twin.
// A frame of the screen for a teach recording: the monitor the window in front is on, copied with GDI and kept as a JPEG no wider than
// 1280 pixels. The design names Windows.Graphics.Capture; that needs a Direct3D device and a capture item for each monitor, and Tactus
// (which has to capture the screen for its own streams) will bring it, so this is the seam's plain default: a copy of the pixels on screen.
// The recording strip excludes itself from capture (SetWindowDisplayAffinity), so it is not in a frame. A window that Windows keeps out
// of a screen copy (protected video) shows black; nothing here goes around that. Never used by automated tests; TeachFramesNativeTests is
// the lead's to run.
using System.Runtime.InteropServices;
using Marrow.Core.Port.Assistant;
using SkiaSharp;

namespace Marrow.App.Assistant.Teach;

public sealed class ScreenFrames : IFrameSource
{
    /// <summary>The JPEG quality of a frame: readable text at about 100 KB for a full screen.</summary>
    public const int Quality = 70;

    public byte[]? Capture()
    {
        var monitor = TeachNative.MonitorFromWindow(TeachNative.GetForegroundWindow(), TeachNative.MonitorDefaultToNearest);
        var info = new TeachNative.MonitorInfo { Size = (uint)Marshal.SizeOf<TeachNative.MonitorInfo>() };
        if (monitor == 0 || !TeachNative.GetMonitorInfo(monitor, ref info)) return null;
        var rect = info.Monitor;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        return Copy(rect.Left, rect.Top, width, height);
    }

    private static byte[]? Copy(int left, int top, int width, int height)
    {
        var screen = TeachNative.GetDC(0);
        if (screen == 0) return null;
        var memory = TeachNative.CreateCompatibleDC(screen);
        var bitmap = memory == 0 ? 0 : TeachNative.CreateCompatibleBitmap(screen, width, height);
        try
        {
            if (memory == 0 || bitmap == 0) return null;
            var previous = TeachNative.SelectObject(memory, bitmap);
            var copied = TeachNative.BitBlt(memory, 0, 0, width, height, screen, left, top, TeachNative.SrcCopy | TeachNative.CaptureBlt);
            if (!copied)
            {
                TeachNative.SelectObject(memory, previous);
                return null;
            }
            var header = new TeachNative.BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<TeachNative.BitmapInfoHeader>(),
                Width = width,
                Height = -height, // top-down rows
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new byte[checked(width * height * 4)];
            // The bitmap must not be selected into a device context when its bits are read.
            TeachNative.SelectObject(memory, previous);
            var lines = TeachNative.GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, 0);
            return lines == height ? Encode(pixels, width, height) : null;
        }
        finally
        {
            if (bitmap != 0) TeachNative.DeleteObject(bitmap);
            if (memory != 0) TeachNative.DeleteDC(memory);
            TeachNative.ReleaseDC(0, screen);
        }
    }

    /// <summary>The pixels (BGRA, rows from the top) as a JPEG, scaled down to the frame width when wider. Null if it cannot be made.</summary>
    public static byte[]? Encode(byte[] bgra, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var source = SKImage.FromPixels(info, handle.AddrOfPinnedObject(), width * 4);
            if (source is null) return null;
            var scaled = width > TeachLimits.FrameWidth
                ? ScaleDown(source, width, height)
                : null;
            using var image = scaled ?? source;
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, Quality);
            return data?.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private static SKImage? ScaleDown(SKImage source, int width, int height)
    {
        var target = new SKImageInfo(TeachLimits.FrameWidth, Math.Max(1, (int)((long)height * TeachLimits.FrameWidth / width)), SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(target);
        using var pixmap = bitmap.PeekPixels();
        return source.ScalePixels(pixmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ? SKImage.FromBitmap(bitmap)
            : null;
    }
}
