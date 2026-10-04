// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1, V-13.2).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>
/// One frame of the driver's outputs: the values voice-glow.js writes as the <c>--vb-*</c> custom properties, the distortion's scale and
/// offset, and the band polyline. The driver reuses one instance (no per-frame allocation); copy what must outlive the next step.
/// </summary>
public sealed class VoiceGlowFrame
{
    public const int LobeCount = 7;

    /// <summary>The points of the band polyline (<c>xn</c>).</summary>
    public const int BandPointCount = 57;

    /// <summary>The smoothed level (<c>--vb-level</c>).</summary>
    public double Level { get; internal set; }

    /// <summary>The three band followers (low, mid, high).</summary>
    public double[] Bands { get; } = new double[3];

    /// <summary>The flow phase in px, wrapped to the lobe period.</summary>
    public double Phase { get; internal set; }

    /// <summary>The processing scan's strength (0 to 1, eased) and its clock.</summary>
    public double ScanA { get; internal set; }

    public double ScanT { get; internal set; }

    /// <summary>The driver's clock in seconds (the sum of the frame deltas).</summary>
    public double Time { get; internal set; }

    /// <summary>The warp follower (1 at rest, 0 while processing).</summary>
    public double Warp { get; internal set; }

    /// <summary>The smoothstepped scan strength (<c>corner</c> in the band maths).</summary>
    public double Corner { get; internal set; }

    /// <summary><c>--vb-cx</c>: the scan's horizontal travel in px.</summary>
    public double Cx { get; internal set; }

    /// <summary><c>--vb-mw</c>.</summary>
    public double Mw { get; internal set; }

    /// <summary><c>--vb-bh</c> (the bend height in px, floored at 0).</summary>
    public double Bh { get; internal set; }

    /// <summary><c>--vb-bendA</c>.</summary>
    public double BendA { get; internal set; }

    /// <summary><c>--vb-cy</c>: the glow's drop at the corners, px.</summary>
    public double Cy { get; internal set; }

    /// <summary><c>--vb-glow</c>.</summary>
    public double Glow { get; internal set; }

    /// <summary><c>--vb-h</c>.</summary>
    public double H { get; internal set; }

    /// <summary><c>--vb-w</c>.</summary>
    public double W { get; internal set; }

    /// <summary><c>--vb-hue</c> in degrees.</summary>
    public double Hue { get; internal set; }

    /// <summary>True when the config distorts and the warp is above its off threshold (0.03): the displacement applies.</summary>
    public bool DisplaceActive { get; internal set; }

    /// <summary>True when the config distorts and the warp is below 0.03 (the page sets <c>data-voice-warp="off"</c>).</summary>
    public bool WarpOff { get; internal set; }

    /// <summary>The displacement map's scale; valid when <see cref="DisplaceActive"/>.</summary>
    public double DisplaceScale { get; internal set; }

    /// <summary>The noise offset (<c>feOffset</c> dx, dy); valid when <see cref="DisplaceActive"/>.</summary>
    public double NoiseDx { get; internal set; }

    public double NoiseDy { get; internal set; }

    /// <summary><c>--vb-x0</c> to <c>--vb-x6</c>: each lobe's offset in px.</summary>
    public double[] LobeX { get; } = new double[LobeCount];

    /// <summary><c>--vb-l0</c> to <c>--vb-l6</c>: each lobe's level.</summary>
    public double[] LobeL { get; } = new double[LobeCount];

    /// <summary><c>--vb-y0</c> to <c>--vb-y6</c>: each lobe's drop at the corners, px.</summary>
    public double[] LobeY { get; } = new double[LobeCount];

    /// <summary>True when the polyline was computed (the element has a size).</summary>
    public bool HasBand { get; internal set; }

    /// <summary>The band polyline's x of each of its 57 points.</summary>
    public double[] BandX { get; } = new double[BandPointCount];

    /// <summary>The band polyline's y of each of its 57 points.</summary>
    public double[] BandY { get; } = new double[BandPointCount];
}
