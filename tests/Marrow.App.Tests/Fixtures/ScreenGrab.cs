using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A block of the real, composed screen (native runs only), as the user sees it: BitBlt from the screen DC with
/// CAPTUREBLT, so the layered and DWM-composited windows on top are in it. Used to prove what a glass window really
/// shows of the pattern behind it (GlassWindowNativeTests).
/// </summary>
public sealed class ScreenGrab
{
    private readonly byte[] bgra;

    private ScreenGrab(int width, int height, byte[] bgra)
    {
        Width = width;
        Height = height;
        this.bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Copies the physical-pixel rectangle (x, y, width, height) of the screen.</summary>
    public static ScreenGrab Of(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        try
        {
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var section = CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
            var previous = SelectObject(memory, section);
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 | 0x40000000))
                    throw new InvalidOperationException("BitBlt from the screen failed: " + Marshal.GetLastWin32Error());
                var copy = new byte[width * height * 4];
                Marshal.Copy(bits, copy, 0, copy.Length);
                return new ScreenGrab(width, height, copy);
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(section);
            }
        }
        finally
        {
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The colour at (x, y) in the block as (red, green, blue).</summary>
    public (byte R, byte G, byte B) At(int x, int y)
    {
        var o = (y * Width + x) * 4;
        return (bgra[o + 2], bgra[o + 1], bgra[o]);
    }

    /// <summary>Luma (0.2126 R + 0.7152 G + 0.0722 B) at (x, y).</summary>
    public double Luma(int x, int y)
    {
        var (r, g, b) = At(x, y);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>The mean luma of every pixel of the block.</summary>
    public double MeanLuma()
    {
        double sum = 0;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++) sum += Luma(x, y);
        return sum / (Width * Height);
    }

    /// <summary>
    /// The local contrast of vertical stripes: the mean absolute luma step between horizontally adjacent pixels (every
    /// third row). Black and white stripes six pixels wide give about 255 / 6 = 42.5; a blurred or flat block gives about 0.
    /// </summary>
    public double StripeContrast()
    {
        double steps = 0;
        var count = 0;
        for (var y = 0; y < Height; y += 3)
            for (var x = 1; x < Width; x++)
            {
                steps += Math.Abs(Luma(x, y) - Luma(x - 1, y));
                count++;
            }
        return steps / count;
    }

    /// <summary>Writes the block as a PNG (for the evidence a developer looks at).</summary>
    public void SavePng(string path)
    {
        var raw = new byte[(Width * 3 + 1) * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var (r, g, b) = At(x, y);
                var o = y * (Width * 3 + 1) + 1 + x * 3;
                raw[o] = r;
                raw[o + 1] = g;
                raw[o + 2] = b;
            }
        using var stream = File.Create(path);
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        Chunk(stream, "IHDR", header);
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        Chunk(stream, "IDAT", packed.ToArray());
        Chunk(stream, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        stream.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
