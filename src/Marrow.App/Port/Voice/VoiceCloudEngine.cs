// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-15; VT-16): cloud opt-in stays separate from the local engine seam (voice-SPEC V-2.5, V-18.4).
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.App.Port.Voice;

/// <summary>Bound at production start-up; tests override with an HTTP-fake cloud adapter. The unbound default cannot send a request.</summary>
internal static class VoiceCloudEngine
{
    public static Ambient<ISpeechEngine> Current { get; } = new(new FailClosedSpeechEngine());
}
