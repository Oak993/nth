// PORT-ONLY, owner-ruled addition (OD-0091, OD-0099 Q-V-7 A and Q-V-8 A, OD-0106; first-release voice typing, VT-10, voice-SPEC V-14.6, V-16.3.5): the words and the
// times of the composer's notes.
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The notes the composer shows in its field (DESIGN voice-composer.js:211-219, 438-458). Cancelled and no speech are the design's, 1.6 s each, no link. Blocked is
/// the locked note: the warning mark, the text and one link to the Windows page that fixes it, 5.2 s. OD-0106's set A (Plain) gives every other error one link each;
/// VT-17 builds them, so until then <see cref="For"/> answers null for those kinds (ENGINEERING DECISION VT-10: a note whose words are not built is not made up).
/// The privacy line of an engine is Q-V-8 A: the Voice typing page's own line (V-16.3.5), about 5 s, once per engine.
/// </summary>
public static class VoiceNotes
{
    /// <summary>V-14.6: where the blocked note's link goes.</summary>
    public const string BlockedDestination = "ms-settings:privacy-microphone";

    public static readonly TimeSpan CancelledLength = TimeSpan.FromSeconds(1.6);
    public static readonly TimeSpan NoSpeechLength = TimeSpan.FromSeconds(1.6);
    public static readonly TimeSpan PrivacyLength = TimeSpan.FromSeconds(5);

    private static readonly VoiceNoteContent Cancelled = new("cancelled", "Voice input cancelled", null, null, CancelledLength, false);
    private static readonly VoiceNoteContent NoSpeech = new("noSpeech", "No speech was heard", null, null, NoSpeechLength, false);

    private static readonly VoiceNoteContent Blocked = new("blocked", "Microphone blocked. Allow nth in ", "Privacy & security > Microphone",
        BlockedDestination, VoiceSession.BlockedLength, true);

    private static readonly VoiceNoteContent LocalPrivacy = new("privacy.local",
        "Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.", null, null, PrivacyLength, false);

    private static readonly VoiceNoteContent CloudPrivacy = new("privacy.cloud",
        "Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.",
        null, null, PrivacyLength, false);

    /// <summary>The note a session's <paramref name="kind"/> raises, or null when its words are not built yet (VT-17).</summary>
    public static VoiceNoteContent? For(VoiceNoteKind kind) => kind switch
    {
        VoiceNoteKind.Cancelled => Cancelled,
        VoiceNoteKind.NoSpeech => NoSpeech,
        VoiceNoteKind.Blocked => Blocked,
        _ => null,
    };

    /// <summary>The privacy line of <paramref name="engine"/> (V-16.3.5).</summary>
    public static VoiceNoteContent Privacy(VoiceEngine engine) => engine == VoiceEngine.Cloud ? CloudPrivacy : LocalPrivacy;
}
