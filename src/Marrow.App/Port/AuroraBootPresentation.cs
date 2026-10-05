using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

// OD-0113: clip the HWND itself, including Acrylic, to B's one growing rectangle. At completion DWM owns the 8 DIP corners again.
internal sealed class AuroraBootPresentation(AvaloniaWindow main)
{
    private long? originalStyle;
    private PixelRect? regionFrame;
    private Rect? lastClip;
    private double lastCorner;
    private byte? lastAlpha;
    private bool regionApplied;
    private PixelRect? lastOuter;
    private double lastScale;
    private nint Handle => OperatingSystem.IsWindows() && main.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } h ? h.Handle : 0;
    internal bool OpacitySupported { get; private set; }
    internal PixelRect? VisibleFrame()
    {
        var handle = Handle;
        return handle != 0 && DwmGetWindowAttribute(handle, 9, out var r, Marshal.SizeOf<Bounds>()) == 0 && r.Right > r.Left && r.Bottom > r.Top
            ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }
    internal void Apply(Rect? clip, double corner, double opacity)
    {
        using var work = Marrow.UI.Port.AuroraBootTrace.Begin("Boot: UI HWND region/alpha");
        var handle = Handle;
        if (handle == 0) return;
        var scale = main.RenderScaling;
        PixelRect? outerFrame = GetWindowRect(handle, out var outer) ? new PixelRect(outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top) : null;
        var frameChanged = lastOuter != outerFrame || lastScale != scale;
        if (frameChanged) regionFrame = null;
        if ((!regionApplied || clip != lastClip || corner != lastCorner || frameChanged) && clip is { } rect && (regionFrame ??= VisibleFrame()) is { } visible && outerFrame is not null)
        {
            var x = visible.X - outer.Left; var y = visible.Y - outer.Top;
            var region = CreateRoundRectRgn(x + (int)Math.Round(rect.X * scale), y + (int)Math.Round(rect.Y * scale),
                x + (int)Math.Round(rect.Right * scale) + 1, y + (int)Math.Round(rect.Bottom * scale) + 1, (int)Math.Round(corner * scale * 2), (int)Math.Round(corner * scale * 2));
            if (region != 0 && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
            else if (region != 0) { lastClip = clip; lastCorner = corner; regionApplied = true; lastOuter = outerFrame; lastScale = scale; } // Windows owns a successfully applied region.
        }
        else if (clip is null && (!regionApplied || lastClip is not null))
        { SetWindowRgn(handle, 0, true); lastClip = null; regionApplied = true; }
        if (opacity >= 1)
        {
            RestoreOpacity(handle); // Layered windows omit DWM's invisible border; restore it at the exact final frame.
            return;
        }
        // Fade the whole HWND (Acrylic and pixels), rather than fading only the app-drawn glass.
        if (originalStyle is null)
        { originalStyle = GetWindowLongPtr(handle, -20); SetWindowLongPtr(handle, -20, originalStyle.Value | 0x80000); } // WS_EX_LAYERED
        var alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        if (lastAlpha != alpha)
        { OpacitySupported = SetLayeredWindowAttributes(handle, 0, alpha, 2); if (OpacitySupported) lastAlpha = alpha; }
    }
    internal void Behind(AvaloniaWindow stage)
    {
        stage.Topmost = false;
        if (Handle != 0 && stage.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } h)
            SetWindowPos(h.Handle, Handle, 0, 0, 0, 0, 0x0013); // NOSIZE | NOMOVE | NOACTIVATE, directly beneath the main window.
    }
    internal void Restore()
    {
        var handle = Handle;
        if (handle == 0) return;
        SetWindowRgn(handle, 0, true);
        RestoreOpacity(handle);
        regionFrame = null; regionApplied = false; lastAlpha = null;
    }
    private void RestoreOpacity(nint handle)
    {
        if (originalStyle is { } style)
        {
            SetLayeredWindowAttributes(handle, 0, 255, 2);
            SetWindowLongPtr(handle, -20, (GetWindowLongPtr(handle, -20) & ~0x80000L) | (style & 0x80000L));
            originalStyle = null; lastAlpha = null;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, int attribute, out Bounds bounds, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out Bounds bounds);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint h, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern long SetWindowLongPtr(nint h, int index, long value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int width, int height, uint flags);
}
