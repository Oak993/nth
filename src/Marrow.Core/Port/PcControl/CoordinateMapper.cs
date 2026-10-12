// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.3). Maps between the model's grid (the screenshot's pixels,
// window-relative) and physical screen pixels, and from screen pixels to SendInput's normalised absolute values over the
// whole virtual desktop. Everything Win32 hands over is physical (the PC worker is per-monitor DPI aware v2), so mixed
// DPI monitors and negative origins need no special case.
namespace Marrow.Core.Port.PcControl;

/// <summary>The model's grid for one observation: its size and the scale (model px per physical px, at most 1).</summary>
public readonly record struct PcGrid(int Width, int Height, double Scale);

/// <summary>How a window's frame changed since an observation.</summary>
public enum PcFrameChange
{
    Same,
    Moved,
    Resized,
}

public static class CoordinateMapper
{
    /// <summary>The model grid for a window of the given physical size: long edge at most <paramref name="longEdge"/>,
    /// at most <paramref name="maxPixels"/> pixels in all, never upscaled.</summary>
    public static PcGrid Grid(int physicalWidth, int physicalHeight, int longEdge = 1280, double maxPixels = 1_000_000)
    {
        if (physicalWidth <= 0 || physicalHeight <= 0) return new PcGrid(0, 0, 1.0);
        var scale = Math.Min(1.0, (double)longEdge / Math.Max(physicalWidth, physicalHeight));
        scale = Math.Min(scale, Math.Sqrt(maxPixels / ((double)physicalWidth * physicalHeight)));
        var width = Math.Max(1, (int)Math.Round(physicalWidth * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(physicalHeight * scale, MidpointRounding.AwayFromZero));
        // Round so the model grid never exceeds the caps after rounding.
        while ((width > longEdge || (double)width * height > maxPixels) && width > 1 && height > 1)
        {
            scale *= 0.999;
            width = Math.Max(1, (int)Math.Round(physicalWidth * scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (int)Math.Round(physicalHeight * scale, MidpointRounding.AwayFromZero));
        }
        return new PcGrid(width, height, scale);
    }

    /// <summary>Whether a model point lies on the grid.</summary>
    public static bool OnGrid(PcGrid grid, double x, double y) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height;

    /// <summary>screen = frameOrigin + round(model / scale).</summary>
    public static PcPoint ToScreen(PcRect frame, double scale, double x, double y)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        return new PcPoint(
            frame.X + (int)Math.Round(x / scale, MidpointRounding.AwayFromZero),
            frame.Y + (int)Math.Round(y / scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>The model point of a physical screen point (for reporting where an element sits in a screenshot).</summary>
    public static (int X, int Y) ToModel(PcRect frame, double scale, PcPoint screen) => (
        (int)Math.Round((screen.X - frame.X) * scale, MidpointRounding.AwayFromZero),
        (int)Math.Round((screen.Y - frame.Y) * scale, MidpointRounding.AwayFromZero));

    /// <summary>A model-grid region in physical screen pixels (for pc_zoom), clipped to the frame.</summary>
    public static PcRect RegionToScreen(PcRect frame, double scale, int x, int y, int width, int height)
    {
        var a = ToScreen(frame, scale, x, y);
        var b = ToScreen(frame, scale, x + width, y + height);
        var left = Math.Clamp(a.X, frame.X, frame.Right);
        var top = Math.Clamp(a.Y, frame.Y, frame.Bottom);
        return new PcRect(left, top, Math.Clamp(b.X, frame.X, frame.Right) - left, Math.Clamp(b.Y, frame.Y, frame.Bottom) - top);
    }

    /// <summary>
    /// SendInput's MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK value for a screen point: 0 to 65535 over the virtual
    /// desktop, so that 0 is its left (top) pixel and 65535 its right (bottom) pixel.
    /// </summary>
    public static (int Dx, int Dy) ToAbsolute(PcPoint screen, PcRect virtualDesktop) =>
        (Normalise(screen.X - virtualDesktop.X, virtualDesktop.Width), Normalise(screen.Y - virtualDesktop.Y, virtualDesktop.Height));

    /// <summary>The screen pixel an absolute value lands on (Windows' own mapping: floor(value * size / 65536)).</summary>
    public static PcPoint FromAbsolute(int dx, int dy, PcRect virtualDesktop) => new(
        virtualDesktop.X + (int)((long)dx * virtualDesktop.Width / 65536),
        virtualDesktop.Y + (int)((long)dy * virtualDesktop.Height / 65536));

    private static int Normalise(int offset, int size)
    {
        if (size <= 1) return 0;
        // The centre of the target pixel, so Windows' floor mapping lands on that pixel for every desktop size.
        var value = (int)((((long)offset * 65536) + 32768) / size);
        return Math.Clamp(value, 0, 65535);
    }

    /// <summary>How the live frame differs from the observed one.</summary>
    public static PcFrameChange Compare(PcRect observed, PcRect live) =>
        observed.Width != live.Width || observed.Height != live.Height ? PcFrameChange.Resized
        : observed.X != live.X || observed.Y != live.Y ? PcFrameChange.Moved
        : PcFrameChange.Same;
}
