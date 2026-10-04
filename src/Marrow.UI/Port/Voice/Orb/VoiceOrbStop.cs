// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): the view left the tree (V-11.3).
namespace Marrow.UI.Port.Voice.Orb;

/// <summary>Handler message: the view left the tree; the handler stops and releases its renderer on the render thread.</summary>
internal sealed class VoiceOrbStop
{
    public static readonly VoiceOrbStop Instance = new();
    private VoiceOrbStop() { }
}
