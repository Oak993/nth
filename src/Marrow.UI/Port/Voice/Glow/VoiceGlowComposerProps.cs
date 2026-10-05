// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.3): the configuration the composer gives the glow.
using Marrow.Core.Port.Voice.Glow;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>V-13.3's configuration (voiceopts.js:194): <c>type 'default'</c>, <c>scale 0.7</c>, <c>glowWidth 1.6</c>, <c>lobeSpacing 2.1</c>, <c>rangeWidth 1.9</c>, <c>processingTravel 2.05</c>.</summary>
public static class VoiceGlowComposerProps
{
    /// <summary>The props for the composer: the rest is the package's defaults for <c>default</c> and the theme.</summary>
    public static VoiceGlowProps For(bool dark, bool processing, bool reduceMotion, double radius = VoiceGlowInput.ComposerRadius) => new()
    {
        Type = VoiceGlowType.Default,
        Theme = dark ? VoiceGlowTheme.Dark : VoiceGlowTheme.Light,
        Scale = 0.7,
        GlowWidth = 1.6,
        LobeSpacing = 2.1,
        RangeWidth = 1.9,
        ProcessingTravel = 2.05,
        BorderRadius = radius,
        Processing = processing,
        ReducedMotion = reduceMotion,
    };
}
