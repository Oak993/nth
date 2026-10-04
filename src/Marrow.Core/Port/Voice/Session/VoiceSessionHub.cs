// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): one voice session at a time (voice-SPEC V-7.11).
namespace Marrow.Core.Port.Voice.Session;

/// <summary>The sessions of one app: at most one is listening or processing; a click on another orb finishes it first (V-7.11).</summary>
public sealed class VoiceSessionHub
{
    public static Ambient<VoiceSessionHub> Shared { get; } = new(() => new VoiceSessionHub());

    /// <summary>The session that is Listening or Processing, if any.</summary>
    public VoiceSession? Active => throw new NotImplementedException();
}
