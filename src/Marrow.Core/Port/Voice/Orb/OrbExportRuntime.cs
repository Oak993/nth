// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment; first-release voice typing, VT-4, voice-SPEC V-11.1).
// The export's runtime, function by function from voice-orbs.js:38-76 (the site's "Copy code" export): audioRules, audioFlowStrengths,
// applyAudioUniforms, srgbToLinear, linearToSrgb, mixSrgb, sampleTransition. Arithmetic is double, stores are float32, as a JavaScript
// Float32Array does.
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>One row of the export's <c>audioRules</c>: <c>[index, band, additive, proportional, ceiling]</c>.</summary>
public readonly record struct OrbAudioRule(int Index, OrbBand Band, double Additive, double Proportional, double Ceiling);

/// <summary>The export runtime (V-11.1).</summary>
public static class OrbExportRuntime
{
    /// <summary>The export's <c>audioRules</c>, in its order.</summary>
    public static IReadOnlyList<OrbAudioRule> AudioRules { get; } =
    [
        new(3, OrbBand.All, 0, 0.7, 5),
        new(6, OrbBand.Mid, 0.85, 0, 7),
        new(21, OrbBand.Low, 0.075, 0, 1),
        new(10, OrbBand.High, 0.16, 0, 2),
        new(14, OrbBand.All, 0, 0.12, 4),
    ];

    /// <summary>The export's <c>audioFlowStrengths</c> for a flow style (<c>values[15]</c>); 0 for a style it does not list.</summary>
    public static double AudioFlowStrength(int style) => style switch
    {
        9 => 0.8,
        10 => 0.65,
        11 => 0.65,
        14 => 0.75,
        19 => 1,
        21 => 0.7,
        _ => 0,
    };

    /// <summary>Takes the bands into the uniforms (<c>applyAudioUniforms</c>); a style with no strength leaves them untouched.</summary>
    public static void ApplyAudioUniforms(Span<float> values, OrbBands bands)
    {
        // Math.round(values[15]) in JavaScript is floor(x + 0.5)
        var styleValue = Math.Floor(values[15] + 0.5);
        var style = styleValue is >= int.MinValue and <= int.MaxValue ? (int)styleValue : int.MinValue;
        var strength = AudioFlowStrength(style);
        if (strength == 0) return;
        // an indexed loop: foreach over the IReadOnlyList would box an enumerator every Listening frame (V-T51: 0 B per frame)
        for (var i = 0; i < AudioRules.Count; i++)
        {
            var rule = AudioRules[i];
            var input = bands[rule.Band];
            var level = (double.IsFinite(input) ? Math.Max(0, Math.Min(1, input)) : 0) * strength;
            if (level == 0) continue;
            var current = (double)values[rule.Index];
            values[rule.Index] = (float)Math.Min(Math.Max(rule.Ceiling, current), current * (1 + rule.Proportional * level) + rule.Additive * level);
        }
    }

    /// <summary><c>srgbToLinear</c>.</summary>
    public static double SrgbToLinear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary><c>linearToSrgb</c>.</summary>
    public static double LinearToSrgb(double value) => value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

    /// <summary><c>mixSrgb</c>: a mix of two sRGB components done in linear light.</summary>
    public static double MixSrgb(double from, double to, double progress) =>
        LinearToSrgb(SrgbToLinear(from) + (SrgbToLinear(to) - SrgbToLinear(from)) * progress);

    /// <summary>
    /// <c>sampleTransition</c> with the progress handed in: indexes 3 and up; colours (index &gt;= 40 and (index - 40) % 4 &lt; 3) mixed in
    /// linear light, the rest linearly. Indexes 0 to 2 (size, time) are the caller's.
    /// </summary>
    public static void SampleTransition(ReadOnlySpan<float> from, ReadOnlySpan<float> target, double progress, Span<float> displayed)
    {
        for (var index = 3; index < displayed.Length; index++)
        {
            double a = from[index], b = target[index];
            var colorComponent = index >= 40 && (index - 40) % 4 < 3;
            displayed[index] = (float)(colorComponent ? MixSrgb(a, b, progress) : a + (b - a) * progress);
        }
    }
}
