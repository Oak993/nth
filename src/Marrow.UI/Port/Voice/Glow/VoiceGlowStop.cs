// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13): the view left the tree.
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>Handler message: the view left the tree; the handler stops and releases its painter on the render thread.</summary>
internal sealed class VoiceGlowStop
{
    public static readonly VoiceGlowStop Instance = new();
    private VoiceGlowStop() { }
}
