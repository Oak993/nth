// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-5; DESIGN.md 3.1). The GDI fallbacks
// behind Windows.Graphics.Capture: PrintWindow(PW_RENDERFULLCONTENT) into a 32-bit top-down DIB of the window rectangle,
// cropped to the DWM frame; else, only when the window is in front, a copy of its rectangle from the screen. Never used
// by automated tests.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class GdiCapture(bool fromScreen) : IWindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 2;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    public string Name => fromScreen ? "screen" : "printwindow";

    public Task<PcFrame?> Capture(nint window, PcRect frame, CancellationToken cancellationToken)
    {
        using var dpi = Dpi.PerMonitorV2();
        if (frame.IsEmpty || !Win32.IsWindow(window)) return Task.FromResult<PcFrame?>(null);
        if (fromScreen)
        {
            // A screen copy shows whatever is on top, so it is used only for the window that is in front.
            if (Win32.GetAncestor(Win32.GetForegroundWindow(), Win32.GA_ROOTOWNER) != window) return Task.FromResult<PcFrame?>(null);
            return Task.FromResult(Grab(frame.Width, frame.Height, dc =>
            {
                var screen = GetDC(0);
                try { return BitBlt(dc, 0, 0, frame.Width, frame.Height, screen, frame.X, frame.Y, SRCCOPY | CAPTUREBLT); }
                finally { ReleaseDC(0, screen); }
            }));
        }
        if (!Win32.GetWindowRect(window, out var rect)) return Task.FromResult<PcFrame?>(null);
        var whole = Win32.Rect(rect);
        var shot = Grab(whole.Width, whole.Height, dc => PrintWindow(window, dc, PW_RENDERFULLCONTENT));
        // GetWindowRect includes the invisible resize borders; the model sees the DWM frame.
        return Task.FromResult(shot is null ? null : PcFrames.Crop(shot, new PcRect(frame.X - whole.X, frame.Y - whole.Y, frame.Width, frame.Height)));
    }

    private static PcFrame? Grab(int width, int height, Func<nint, bool> draw)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return null;
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var info = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
        ReleaseDC(0, screen);
        if (bitmap == 0 || dc == 0)
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (dc != 0) DeleteDC(dc);
            return null;
        }
        var old = SelectObject(dc, bitmap);
        try
        {
            if (!draw(dc)) return null;
            GdiFlush();
            var bytes = new byte[width * height * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return PcFrame.Packed(bytes, width, height);
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SelectObject(nint dc, nint item);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint item);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint dc, uint flags);
}
