using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Reads back an HICON through GDI (GetIconInfo, GetDIBits): the colour bitmap's size and its 32-bit BGRA pixels. It only
/// inspects an icon the test made; no window or taskbar is involved.
/// </summary>
public static class NativeIconProbe
{
    /// <summary>The colour bitmap of the icon: its width and height and its top-down BGRA bytes.</summary>
    public static (int Width, int Height, byte[] Bgra) ReadColor(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info)) throw new InvalidOperationException("GetIconInfo failed.");
        try
        {
            GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out var bitmap);
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bitmap.bmWidth,
                biHeight = -bitmap.bmHeight,
                biPlanes = 1,
                biBitCount = 32,
            };
            var bytes = new byte[bitmap.bmWidth * bitmap.bmHeight * 4];
            var screen = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(screen, info.hbmColor, 0, (uint)bitmap.bmHeight, bytes, ref header, 0) == 0)
                    throw new InvalidOperationException("GetDIBits failed.");
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
            return (bitmap.bmWidth, bitmap.bmHeight, bytes);
        }
        finally
        {
            DeleteObject(info.hbmColor);
            DeleteObject(info.hbmMask);
        }
    }

    public static void Destroy(IntPtr icon) => DestroyIcon(icon);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER header, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
