using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Marrow.Services.Web;

// PORT-ONLY: DESIGN 5.7; ED-NEW-web-w9-2. A dedicated WebView2 child HWND is moved and rect-clipped, never
// resized per device update. Controller.Bounds is relative to ParentWindow (Microsoft's ICoreWebView2Controller
// reference). Native checks, deliberately not run in W9, prove the adapter's actual child and clipping.
public interface IPreviewViewport
{
    void PlacePreview(double viewportWidth, double viewportHeight, double dpi, double x, double y, double width, double height);
    void ClearPreview();
    PreviewRegionInfo? PreviewRegion { get; }
    void OpenPreviewDevTools();
}

public sealed record PreviewRegionInfo(bool IsRectangular, int InitialWidth, int InitialHeight, int CurrentWidth, int CurrentHeight);

internal sealed class PreviewViewport(CoreWebView2Controller controller) : IDisposable
{
    private nint child;
    private Rectangle? original;
    private int initialWidth;
    private int initialHeight;
    private double viewportWidth;
    private double viewportHeight;
    private double scale;

    public void Place(double viewportWidth, double viewportHeight, double dpi, double x, double y, double width, double height)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || dpi <= 0 || !double.IsFinite(dpi)) return;
        if (child == 0)
        {
            // Avalonia's HWND adapter gives each view its own parent; WebView2 creates its child in that parent.
            child = GetWindow(controller.ParentWindow, 5); // GW_CHILD
            if (child == 0 || GetWindow(child, 2) != 0) // GW_HWNDNEXT: ambiguity is a native precondition failure.
                throw new InvalidOperationException("The preview needs a dedicated WebView2 child window.");
            original = controller.Bounds;
        }
        if (this.viewportWidth != viewportWidth || this.viewportHeight != viewportHeight || scale != dpi)
        {
            this.viewportWidth = viewportWidth;
            this.viewportHeight = viewportHeight;
            scale = dpi;
            var origin = original!.Value;
            controller.Bounds = new Rectangle(origin.X, origin.Y, Pixels(viewportWidth, dpi), Pixels(viewportHeight, dpi));
            if (!GetClientRect(child, out var rect)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            initialWidth = rect.Right - rect.Left;
            initialHeight = rect.Bottom - rect.Top;
        }
        var baseline = original!.Value;
        if (!SetWindowPos(child, 0, baseline.X + Pixels(x, dpi), baseline.Y + Pixels(y, dpi), 0, 0, 0x0001 | 0x0004 | 0x0010)) // NOSIZE, NOZORDER, NOACTIVATE
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var region = CreateRectRgn(0, 0, Pixels(width, dpi), Pixels(height, dpi));
        if (region == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (SetWindowRgn(child, region, true) == 0)
        {
            DeleteObject(region); // On success Windows owns the region.
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        controller.NotifyParentWindowPositionChanged();
    }

    public PreviewRegionInfo? Info
    {
        get
        {
            if (child == 0 || !GetClientRect(child, out var rect)) return null;
            var region = CreateRectRgn(0, 0, 0, 0);
            try { return new PreviewRegionInfo(GetWindowRgn(child, region) == 2, initialWidth, initialHeight, rect.Right - rect.Left, rect.Bottom - rect.Top); }
            finally { DeleteObject(region); }
        }
    }

    public void Dispose()
    {
        if (child == 0) return;
        SetWindowRgn(child, 0, true);
        if (original is { } rect) controller.Bounds = rect;
        child = 0;
        original = null;
        viewportWidth = viewportHeight = scale = 0;
    }

    private static int Pixels(double value, double dpi) => checked((int)Math.Round(value * dpi, MidpointRounding.AwayFromZero));

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
}
