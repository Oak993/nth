using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Reads the straight-alpha BGRA pixels of a rendered taskbar badge (ED-219).</summary>
public static class BadgePixels
{
    /// <summary>The pixel at (x, y), row 0 at the top, as (alpha, red, green, blue).</summary>
    public static (byte A, byte R, byte G, byte B) At(TaskbarOverlayIcon icon, int x, int y)
    {
        var offset = (y * icon.Size + x) * 4;
        return (icon.Bgra[offset + 3], icon.Bgra[offset + 2], icon.Bgra[offset + 1], icon.Bgra[offset]);
    }

    /// <summary>Fully opaque and nearer white than the badge red (green and blue past the midpoint of 59 and 255): a pixel
    /// the count's text covers by half or more.</summary>
    public static bool IsWhite(TaskbarOverlayIcon icon, int x, int y)
    {
        var (a, _, g, b) = At(icon, x, y);
        return a == 255 && g >= 150 && b >= 150;
    }

    /// <summary>Whether some pixel is pure white: a stroke of the text covers it whole, in the text's own colour.</summary>
    public static bool HasPureWhite(TaskbarOverlayIcon icon)
    {
        for (var y = 0; y < icon.Size; y++)
            for (var x = 0; x < icon.Size; x++)
                if (At(icon, x, y) == (255, 255, 255, 255)) return true;
        return false;
    }

    /// <summary>Fully opaque and exactly the badge red: a pixel of the disc away from the text and the rim.</summary>
    public static bool IsRed(TaskbarOverlayIcon icon, int x, int y) => At(icon, x, y) == (255, 0xFF, 0x3B, 0x30);

    /// <summary>Every white pixel, as (x, y).</summary>
    public static List<(int X, int Y)> WhitePixels(TaskbarOverlayIcon icon)
    {
        var white = new List<(int, int)>();
        for (var y = 0; y < icon.Size; y++)
            for (var x = 0; x < icon.Size; x++)
                if (IsWhite(icon, x, y)) white.Add((x, y));
        return white;
    }

    /// <summary>The height in pixels of the rows that hold white.</summary>
    public static int TextHeight(TaskbarOverlayIcon icon)
    {
        var white = WhitePixels(icon);
        return white.Count == 0 ? 0 : white.Max(p => p.Y) - white.Min(p => p.Y) + 1;
    }

    /// <summary>The width in pixels of the columns that hold white.</summary>
    public static int TextWidth(TaskbarOverlayIcon icon)
    {
        var white = WhitePixels(icon);
        return white.Count == 0 ? 0 : white.Max(p => p.X) - white.Min(p => p.X) + 1;
    }
}
