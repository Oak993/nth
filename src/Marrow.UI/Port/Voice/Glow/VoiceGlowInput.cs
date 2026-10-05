// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.3): what the host tells the glow scene.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// The glow's inputs (V-13.3): <c>active</c> = Listening or Processing, <c>processing</c> = Processing, the app's appearance, the system's reduce motion, the level the
/// session reads, and the size of the composer's rounded shape the glow is drawn in.
/// </summary>
public sealed record VoiceGlowInput(bool Active, bool Processing, VoiceGlowLevel Level, bool Dark, bool ReduceMotion, double Width, double Height, double Radius = VoiceGlowInput.ComposerRadius)
{
    /// <summary>The composer's corner (V-13.3 <c>borderRadius 22</c>).</summary>
    public const double ComposerRadius = 22;
}
