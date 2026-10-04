// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment; first-release voice typing, VT-4, voice-SPEC V-11.1, V-5.5).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>The four bands the export's audio rules read (voice-orbs.js <c>audioRules</c>: <c>all</c>, <c>low</c>, <c>mid</c>, <c>high</c>).</summary>
public enum OrbBand { All, Low, Mid, High }

/// <summary>The smoothed band levels (V-5.5) the orb takes while Listening, each nominally 0 to 1.</summary>
public readonly record struct OrbBands(double Low, double Mid, double High, double All)
{
    /// <summary>The level of one band.</summary>
    public double this[OrbBand band] => band switch
    {
        OrbBand.All => All,
        OrbBand.Low => Low,
        OrbBand.Mid => Mid,
        OrbBand.High => High,
    };
}
