// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2, V-19): how the band canvas is drawn.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>How <see cref="VoiceGlowPainter"/> draws the band canvas (voice-glow.js <c>Sn</c>).</summary>
public enum VoiceGlowBandMode
{
    /// <summary>The analytic pass on a GPU, the canvas's own strokes on the CPU raster (where the analytic pass costs more than seventeen blurred strokes).</summary>
    Auto,

    /// <summary>As the canvas draws it: seventeen strokes of the polyline, each blurred on its own.</summary>
    Strokes,

    /// <summary>One shader pass: the polyline's distance field, every stroke's Gaussian edge and the canvas's compositing in floating point.</summary>
    Analytic,
}
