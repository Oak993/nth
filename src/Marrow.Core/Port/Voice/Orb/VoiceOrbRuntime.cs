// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.1, V-11.2).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>How a <see cref="VoiceOrbRuntime"/> is laid out (voice-orbs.js <c>make(canvas, opts)</c>).</summary>
public readonly record struct VoiceOrbRuntimeOptions(
    double SizePx,
    double BallPx,
    double DevicePixelRatio = 1,
    bool Dark = true,
    bool EdgeSource = false,
    bool Supersample = true,
    double Phase = 0);

/// <summary>
/// The CPU side of the design's <c>makeOrb().frame</c> (voice-orbs.js:479-575): the state sample, the audio rules, the integrated motion
/// phase, the framing, the glow. The result is the 136-float uniform block and the <c>mrw</c> uniform the renderer uploads.
/// </summary>
public sealed class VoiceOrbRuntime
{
    public VoiceOrbRuntime(VoiceOrbKind kind, VoiceOrbRuntimeOptions options) => throw new NotImplementedException();

    /// <summary>The motion phase (seconds of preset time); a swapped-in orb takes the old one's (V-11.5).</summary>
    public double Phase { get; set; }

    /// <summary>The device pixels of the slot (<c>dev</c>).</summary>
    public int DevicePx => throw new NotImplementedException();

    /// <summary>1, or 2 when the slot is under 128 device pixels and the shader runs into a 2x surface (V-11.3).</summary>
    public int RenderScale => throw new NotImplementedException();

    /// <summary>The edge of the render surface in pixels (<c>rdev</c>).</summary>
    public int RenderPx => throw new NotImplementedException();

    /// <summary>The uniform block after the last <see cref="Frame"/>.</summary>
    public ReadOnlySpan<float> Values => throw new NotImplementedException();

    /// <summary>The <c>mrw</c> uniform after the last <see cref="Frame"/>.</summary>
    public OrbMrw Mrw => throw new NotImplementedException();

    /// <summary>Lays the orb out for a slot (<c>resize(px, ball)</c>).</summary>
    public void Resize(double sizePx, double ballPx, double devicePixelRatio) => throw new NotImplementedException();

    /// <summary>The theme only changes the lift shadow's strength (V-11.6).</summary>
    public void SetDark(bool dark) => throw new NotImplementedException();

    /// <summary>Computes one frame at time <paramref name="t"/> (seconds).</summary>
    public void Frame(double t, in OrbFrameState state) => throw new NotImplementedException();
}
