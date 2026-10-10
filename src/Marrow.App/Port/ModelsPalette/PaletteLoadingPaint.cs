// PORT-ONLY: OD-0136 amendment 2, step 14 "Model colours loading" (ED-NEW-nth-motion), replacing locked v22 :58-62's loading overlay.
// The same tube clock; the drawing is the nth tube: one light sweep every 1.6 s clipped to the track (no radial glow) and three
// flat --mark dots (r 2, 11 px apart) that swell in turn. The track is drawn by the host, so only the light and the dots are
// painted. When the colours arrive (loading turns off) the light and the dots fade out in 0.2 s. The track renders on the render
// thread (LiquidTrack's draw operation), so this is drawn there and no UI-thread loop is involved.
using Marrow.UI.Port.Nth;
using SkiaSharp;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteLoadingPaint : IDisposable
{
    /// <summary>Seconds the light and the dots fade out once loading ends (step 14: "dots fade 0.2 s").</summary>
    internal const double FadeSeconds = .2;

    private readonly NthTubePainter tube = new();
    private bool wasLoading;
    private double endedAt = double.NaN;

    /// <summary>
    /// Draws the loading tube over <paramref name="rect"/> (at the origin). <paramref name="phase"/> is the tube clock in seconds
    /// since the palette was born; <paramref name="loading"/> false after a wait draws the 0.2 s fade, then nothing.
    /// </summary>
    internal void Draw(SKCanvas canvas, SKRect rect, bool loading, double phase, bool still, bool dark)
    {
        double? loaded = null;
        if (loading) { wasLoading = true; endedAt = double.NaN; }
        else
        {
            if (wasLoading) { wasLoading = false; endedAt = phase; }
            if (double.IsNaN(endedAt)) return;
            loaded = Math.Max(0, phase - endedAt);
            if (loaded >= FadeSeconds) { endedAt = double.NaN; return; }
        }
        tube.DrawTube(canvas, rect.Width, rect.Height, NthPalette.For(dark), dark, still, phase, loaded, null, false);
    }

    public void Dispose() => tube.Dispose();
}
