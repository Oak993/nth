// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2): the glow's layers, for drawing some of them.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>The glow's layers in the order the stylesheet stacks them (voice-glow.js <c>Ya</c>, z-index 1 to 4).</summary>
[Flags]
internal enum VoiceGlowLayers
{
    None = 0,

    /// <summary>The inner light (<c>::before</c>) and its warp mirror.</summary>
    Inner = 1,

    /// <summary>The 1 px stroke ring (<c>::after</c>).</summary>
    Stroke = 2,

    /// <summary>The bloom and its warp mirror.</summary>
    Bloom = 4,

    /// <summary>The band canvas (<c>Sn</c>).</summary>
    Band = 8,

    /// <summary>The epicentre (light themes).</summary>
    Core = 16,

    All = Inner | Stroke | Bloom | Band | Core,
}
