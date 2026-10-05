// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): where the Speech model section's seams are bound (voice-SPEC V-2.4, V-2.5).
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>The ambient seams of the Speech model section. The defaults fail closed; <see cref="VoiceComposition"/> binds the production ones at start-up.</summary>
internal static class SpeechModelSeams
{
    /// <summary>The store and the downloads. The default lists everything as not installed and refuses every download.</summary>
    public static Ambient<ISpeechModelBackend> Backend { get; } = new(new FailClosedSpeechModelBackend());

    /// <summary>The self-test run by "Use recommended". The default times nothing.</summary>
    public static Ambient<ISpeechSelfTest> SelfTest { get; } = new(new NoSpeechSelfTest());
}
