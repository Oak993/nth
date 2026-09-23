// PORT-ONLY: the two Windows-side image sources AttachmentPreview.swift reads through AppKit with no Core twin yet:
// a BGRA pixel buffer realised as something Avalonia can draw (Image(decorative:scale:) has no direct counterpart),
// and a file's shell icon (NSWorkspace.shared.icon(forFile:), AttachmentPreview.swift:118). Source: draft C
// (WicImageDecoder's raw-vtable/P-Invoke style, reused here for GDI and the shell icon).
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>Realises a <see cref="DecodedBitmap"/> (WIC's BGRA output, and the shell icon and video frame buffers
/// below, which share its layout) as an Avalonia bitmap, standing in for SwiftUI's <c>Image(decorative:scale:)</c>.</summary>
public static class DecodedBitmapImages
{
    public static Bitmap ToBitmap(this DecodedBitmap image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var rowBytes = Math.Min(buffer.RowBytes, image.BytesPerRow);
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * image.BytesPerRow, buffer.Address + y * buffer.RowBytes, rowBytes);
        return bitmap;
    }
}

/// <summary>
/// A file's shell icon (AttachmentPreview.swift:118 <c>NSWorkspace.shared.icon(forFile: path)</c>), read through
/// SHGetFileInfo, GetIconInfo and GetDIBits: the large (32 point) associated icon, generated from the extension
/// alone when the path does not exist, matching AppKit's own fallback for a missing file.
/// </summary>
public static class ShellFileIcons
{
    public static DecodedBitmap? Icon(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var info = new SHFILEINFO();
        var exists = File.Exists(path) || Directory.Exists(path);
        var flags = SHGFI_ICON | SHGFI_LARGEICON;
        uint attributes = FILE_ATTRIBUTE_NORMAL;
        if (!exists) flags |= SHGFI_USEFILEATTRIBUTES;
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            return FromIcon(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static DecodedBitmap? FromIcon(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return null;
            var bitmap = new BITMAP();
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0) return null;
            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            if (width <= 0 || height <= 0) return null;
            var stride = checked(width * 4);
            var pixels = new byte[checked(stride * height)];
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            var screenDC = GetDC(IntPtr.Zero);
            try
            {
                var memoryDC = CreateCompatibleDC(screenDC);
                try
                {
                    var scanLines = GetDIBits(memoryDC, iconInfo.hbmColor, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS);
                    if (scanLines == 0) return null;
                }
                finally
                {
                    DeleteDC(memoryDC);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDC);
            }
            return new DecodedBitmap(width, height, stride, pixels);
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
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
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, ref BITMAP lpvObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);
}
