// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-4, voice-SPEC V-13.1).
namespace Marrow.Core.Port.Voice.Glow;

/// <summary>The prop resolution of <c>mountVoiceGlow</c> (voice-glow.js:1018-1150): theme and type presets, clamps, colours.</summary>
public static class VoiceGlowResolver
{
    /// <summary>The lobes <c>we</c>: x 0, +-36, +-72, +-108; w 74/54/48/42; h 46/40/32/26 (V-13.1).</summary>
    public static IReadOnlyList<VoiceGlowLobe> Lobes { get; } =
    [
        new(0, 74, 46, 0),
        new(-36, 54, 40, 1),
        new(36, 54, 40, 1),
        new(-72, 48, 32, 2),
        new(72, 48, 32, 2),
        new(-108, 42, 26, 1),
        new(108, 42, 26, 1),
    ];

    /// <summary>Resolves a prop set as <c>mountVoiceGlow</c>'s <c>resolve()</c> does.</summary>
    public static VoiceGlowResolved Resolve(VoiceGlowProps props) => throw new NotImplementedException();
}
