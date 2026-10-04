// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-2): the four audio values of voice-SPEC V-5.3.
namespace Marrow.Core.Port.Voice.Dsp;

/// <summary>
/// The design's audio response (orb-audio.ts <c>read()</c>): the overall level <see cref="All"/> and the three band means, each 0..1.
/// </summary>
public readonly record struct LevelBands(double All, double Low, double Mid, double High)
{
    public static LevelBands Zero => default;
}
