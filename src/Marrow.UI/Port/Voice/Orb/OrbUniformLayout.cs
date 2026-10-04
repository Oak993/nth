// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-8, voice-SPEC V-11.3): the 136-float uniform block of the export (the WGSL `Uniforms` struct, 544
// bytes) as the SkSL's `uniform float4 u[35]`: u[0] to u[33] are the block, u[34] the Marrow term mrw. V-11.3 leaves the choice ("SkSL `uniform float4 u[34]` or named
// members"); one array means one uniform set a frame (SkiaSharp copies every set it is given, so named members would copy 64 times a frame). The values the export's runtime
// writes (seeds, audio rules, framing) are copied by index, so the table below names where each WGSL member sits; a layout test holds it to the struct and to the SkSL.
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>One member of the block: its name, first float and width in floats (1, 2 or 4).</summary>
internal readonly record struct OrbUniform(string Name, int Offset, int Width)
{
    /// <summary>Where the SkSL reads it: <c>u[offset / 4]</c> and the swizzle for a float or a vec2 (<c>u.time</c> is <c>u[0].z</c>, <c>u.size</c> <c>u[0].xy</c>, <c>u.colorA</c> <c>u[10]</c>).</summary>
    public string Access => Width == 4 ? $"u[{Offset / 4}]" : $"u[{Offset / 4}].{"xyzw".Substring(Offset % 4, Width)}";
}

internal static class OrbUniformLayout
{
    /// <summary>The floats in the block.</summary>
    public const int Floats = 136;

    /// <summary>The floats a frame sets: the block and the Marrow term (<c>mrw</c>: lift, desat, dark, 0).</summary>
    public const int FrameFloats = Floats + 4;

    /// <summary>The SkSL uniform's name.</summary>
    public const string Uniform = "u";

    /// <summary>The members, in the struct's order (voice-orbs.js FRAG, effect.wgsl <c>struct Uniforms</c>).</summary>
    public static IReadOnlyList<OrbUniform> Members { get; } =
    [
        new("size", 0, 2),
        new("time", 2, 1),
        new("speed", 3, 1),
        new("radius", 4, 1),
        new("zoom", 5, 1),
        new("warp", 6, 1),
        new("ridgeAmt", 7, 1),
        new("sharp", 8, 1),
        new("shade", 9, 1),
        new("sheen", 10, 1),
        new("gloss", 11, 1),
        new("shellMidAlpha", 12, 1),
        new("shellEdgeAlpha", 13, 1),
        new("exposure", 14, 1),
        new("style", 15, 1),
        new("edgeSoftness", 16, 1),
        new("edgeGlow", 17, 1),
        new("paletteCount", 18, 1),
        new("glassEnabled", 19, 1),
        new("glassOpacity", 20, 1),
        new("contourDeform", 21, 1),
        new("bandDensity", 22, 1),
        new("chromaticShift", 23, 1),
        new("metalScale", 24, 1),
        new("metalStretch", 25, 1),
        new("metalAngle", 26, 1),
        new("metalOffset", 27, 1),
        new("metalPhase", 28, 1),
        new("metalEvolution", 29, 1),
        new("metalRoughness", 30, 1),
        new("metalDepth", 31, 1),
        new("particleDensity", 32, 1),
        new("ribbonCount", 33, 1),
        new("ribbonWidth", 34, 1),
        new("ribbonTwist", 35, 1),
        new("ribbonFold", 36, 1),
        new("ribbonBreath", 37, 1),
        new("particleSize", 38, 1),
        new("particleBloom", 39, 1),
        new("colorA", 40, 4),
        new("colorB", 44, 4),
        new("colorC", 48, 4),
        new("colorD", 52, 4),
        new("highlightColor", 56, 4),
        new("shellInner", 60, 4),
        new("shellMid", 64, 4),
        new("shellEdge", 68, 4),
        new("sheenColor", 72, 4),
        new("specColor", 76, 4),
        new("canvasColor", 80, 4),
        new("glowColor", 84, 4),
        new("paletteStop0", 88, 4),
        new("paletteStop1", 92, 4),
        new("paletteStop2", 96, 4),
        new("paletteStop3", 100, 4),
        new("paletteStop4", 104, 4),
        new("paletteStop5", 108, 4),
        new("paletteStop6", 112, 4),
        new("paletteStop7", 116, 4),
        new("paletteStop8", 120, 4),
        new("paletteStop9", 124, 4),
        new("paletteStop10", 128, 4),
        new("paletteStop11", 132, 4),
    ];

    /// <summary>
    /// Copies the block and the Marrow term into <paramref name="frame"/> (a reused array of <see cref="FrameFloats"/>) and sets the one uniform from it. Nothing is
    /// allocated here; SkiaSharp copies the array it is handed.
    /// </summary>
    public static void Write(SKRuntimeEffectUniforms uniforms, float[] frame, ReadOnlySpan<float> values, float lift, float desat, float dark)
    {
        values.CopyTo(frame);
        frame[Floats] = lift;
        frame[Floats + 1] = desat;
        frame[Floats + 2] = dark;
        frame[Floats + 3] = 0;
        uniforms[Uniform] = frame;
    }
}
