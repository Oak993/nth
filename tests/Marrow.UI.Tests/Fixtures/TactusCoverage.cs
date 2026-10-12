using SkiaSharp;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (ED-NEW-pcctl-11). An independent oracle for the mark's pixels: how much of a pixel a path covers, sampled on
/// a 16 x 16 grid of points (0 or 1 only when no sample disagrees), so the cut's thin tip is never missed between samples.
/// </summary>
internal static class TactusCoverage
{
    private const int Grid = 16;

    public static double Of(SKPath path, int x, int y)
    {
        var inside = 0;
        for (var j = 0; j < Grid; j++)
        {
            for (var i = 0; i < Grid; i++)
            {
                if (path.Contains(x + ((i + 0.5f) / Grid), y + ((j + 0.5f) / Grid))) inside++;
            }
        }
        // Edges along the pixel's own border count too: a shape that only grazes it still touches the pixel.
        var border = 0;
        var touched = 0;
        for (var k = 0; k <= Grid; k++)
        {
            var t = (float)k / Grid;
            foreach (var (px, py) in new[] { (x + t, y + 0.001f), (x + t, y + 0.999f), (x + 0.001f, y + t), (x + 0.999f, y + t) })
            {
                border++;
                if (path.Contains(px, py)) touched++;
            }
        }
        if (inside == Grid * Grid && touched == border) return 1;
        if (inside == 0 && touched == 0) return 0;
        return Math.Clamp(inside / (double)(Grid * Grid), 1e-6, 1 - 1e-6);
    }
}
