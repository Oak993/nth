// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2): the band program's uniform block, reused every frame.
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>The arrays the band program (<see cref="VoiceGlowShaders.BandSource"/>) is given: made once, filled every frame.</summary>
internal sealed class GlowBandUniforms : IDisposable
{
    /// <summary>The polyline's 57 y values, padded to 60 (15 vec4).</summary>
    public float[] Ys { get; } = new float[60];

    /// <summary>First x, the x step between points, the reach of the strokes beyond the line.</summary>
    public float[] Geo { get; } = new float[4];

    /// <summary>The four distinct offsets of the strokes (none, above, mid, below) as (x, y, 0, 0).</summary>
    public float[] Offsets { get; } = new float[16];

    /// <summary>Per stroke (17): the offset index, half the line width, the blur sigma, the alpha.</summary>
    public float[] Strokes { get; } = new float[68];

    /// <summary>Per stroke (17): the colour as (r, g, b, 0).</summary>
    public float[] Colors { get; } = new float[68];

    /// <summary>The canvas gradient: first x, last x, the fade fraction at each end.</summary>
    public float[] Ramp { get; } = new float[4];

    public SKRuntimeEffectUniforms Uniforms { get; } = new(VoiceGlowShaders.Band);

    public void Dispose() => Uniforms.Dispose();
}
