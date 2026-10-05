// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10): where a composer gets its voice session.
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Makes the <see cref="VoiceSession"/> of one composer. The process default builds it on the ambient seams (fail closed until the app's start-up
/// composition binds the production ones, V-2.5); a test overrides <see cref="Current"/> with a factory over the scripted seams and a hub of its own.
/// </summary>
public sealed class VoiceSessionFactory(Func<Func<VoiceSessionSettings>, Action<VoiceTypingSettings>, VoiceSession> create)
{
    public static Ambient<VoiceSessionFactory> Current { get; } = new(new VoiceSessionFactory((settings, store) => new VoiceSession(settings, store)));

    public VoiceSession Create(Func<VoiceSessionSettings> settings, Action<VoiceTypingSettings> storeTyping) => create(settings, storeTyping);
}
