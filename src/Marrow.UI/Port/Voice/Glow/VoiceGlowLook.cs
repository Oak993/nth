// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2, V-13.3): everything about the glow that does not change
// from frame to frame, in one value: the driver's config (VT-4), the stylesheet recipe of Ya as numbers, the wrapper's strength and the distortion's noise.
using Marrow.Core.Port.Voice.Glow;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>The glow's look for one resolved prop set (<see cref="VoiceGlowResolver.Resolve"/>); equal props give an equal look.</summary>
internal sealed class VoiceGlowLook
{
    private VoiceGlowLook(VoiceGlowResolved resolved, VoiceGlowRecipe recipe)
    {
        Resolved = resolved;
        Recipe = recipe;
        // mountVoiceGlow: the filter's feTurbulence baseFrequency = (0.012 * Nt).toFixed(4) (0.05 * Nt).toFixed(4), Nt = distortionDetail / scale
        NoiseFrequencyX = VoiceGlowCss.Fixed(0.012 * resolved.Detail, 4);
        NoiseFrequencyY = VoiceGlowCss.Fixed(0.05 * resolved.Detail, 4);
    }

    public VoiceGlowResolved Resolved { get; }

    public VoiceGlowConfig Config => Resolved.Config;

    public VoiceGlowRecipe Recipe { get; }

    /// <summary><c>--voice-strength</c>, 0 to 1.</summary>
    public double Strength => Resolved.Strength;

    public double NoiseFrequencyX { get; }

    public double NoiseFrequencyY { get; }

    public static VoiceGlowLook From(VoiceGlowResolved resolved) => new(resolved, VoiceGlowRecipe.From(resolved.Style));

    public static VoiceGlowLook From(VoiceGlowProps props) => From(VoiceGlowResolver.Resolve(props));
}
