// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the words (D-VN set A, byte for byte), how long a note stays and the surface rule.
namespace Marrow.App.Port.Voice.Notes;

/// <summary>
/// The eleven notes of D-VN's set A (the owner's pick, OD-0106) and the locked Microphone blocked note (V-14.6). Each says what happened, then gives one link that names the
/// place that fixes it. The strings are the page's (voice-notes.html, "vndata"), with its right single quotation marks (U+2019) kept.
/// </summary>
public static class VoiceNotes
{
    private static readonly VoiceNote[] Set =
    [
        new(VoiceNoteCase.NoMicrophone, "No microphone found. Set one up in ", "System > Sound", VoiceNoteDestination.SoundSettings),
        new(VoiceNoteCase.MicrophoneUnplugged, "Microphone disconnected. Choose another in ", "Voice typing > Microphone", VoiceNoteDestination.VoiceTypingMicrophone),
        new(VoiceNoteCase.MicrophoneBusy, "Microphone in use by another app. Choose another in ", "Voice typing > Microphone", VoiceNoteDestination.VoiceTypingMicrophone),
        new(VoiceNoteCase.ModelStillDownloading, "Speech model still downloading. See it in ", "Voice typing > Speech model", VoiceNoteDestination.VoiceTypingSpeechModel),
        new(VoiceNoteCase.ModelFailedToLoad, "Speech model didn\u2019t load. Choose a model in ", "Voice typing > Speech model", VoiceNoteDestination.VoiceTypingSpeechModel),
        new(VoiceNoteCase.NoAvx2, "This processor can\u2019t run the speech model. Use OpenAI in ", "Voice typing > Engine", VoiceNoteDestination.VoiceTypingEngine),
        new(VoiceNoteCase.EngineFailure, "Voice typing stopped on an error. Check it in ", "Settings > Voice typing", VoiceNoteDestination.VoiceTypingPage),
        new(VoiceNoteCase.CloudNoKey, "No OpenAI key yet. Add one in ", "Voice typing > OpenAI key", VoiceNoteDestination.VoiceTypingOpenAiKey),
        new(VoiceNoteCase.CloudKeyRefused, "OpenAI refused the key. Replace it in ", "Voice typing > OpenAI key", VoiceNoteDestination.VoiceTypingOpenAiKey),
        new(VoiceNoteCase.CloudOutOfQuota, "This key reached its OpenAI limit. Switch to this PC in ", "Voice typing > Engine", VoiceNoteDestination.VoiceTypingEngine),
        new(VoiceNoteCase.CloudNoNetwork, "Can\u2019t reach OpenAI: no internet. Switch to this PC in ", "Voice typing > Engine", VoiceNoteDestination.VoiceTypingEngine),
    ];

    /// <summary>V-14.6's locked note: "Microphone blocked. Allow Nth in " and the link "Privacy &amp; security &gt; Microphone" (ms-settings:privacy-microphone).</summary>
    public static VoiceNote Blocked { get; } = new(VoiceNoteCase.Blocked, "Microphone blocked. Allow Nth in ", "Privacy & security > Microphone", VoiceNoteDestination.PrivacyMicrophone);

    /// <summary>The eleven notes of set A, in the page's order.</summary>
    public static IReadOnlyList<VoiceNote> All { get; } = Set;

    /// <summary>
    /// How long a note stays: the locked note's 5.2 s (V-14.6) for all twelve. The page proposed it for the others ("the lead proposes the same 5.2 s"); ENGINEERING DECISION
    /// (ED-351): a link needs time to be read and reached, and one time keeps every note alike.
    /// </summary>
    public static TimeSpan DisplayLength { get; } = TimeSpan.FromSeconds(5.2);

    public static VoiceNote For(VoiceNoteCase noteCase) => noteCase == VoiceNoteCase.Blocked
        ? Blocked
        : Set.FirstOrDefault(n => n.Case == noteCase) ?? throw new ArgumentOutOfRangeException(nameof(noteCase), noteCase, "no such note");

    /// <summary>V-14.6: an empty draft shows the note in the field; a draft that holds text shows it in the composer's notice line, so the user's text is never hidden.</summary>
    public static VoiceNoteSurface SurfaceFor(string? draft) => string.IsNullOrEmpty(draft) ? VoiceNoteSurface.Field : VoiceNoteSurface.NoticeLine;

    /// <summary>
    /// What a screen reader hears as the note appears (a polite announcement). The locked note's is V-14.5's own string; another note's is the note read aloud, its
    /// greater-than signs as commas (as V-14.5 reads the same path) and a full stop at the end. ENGINEERING DECISION (ED-351): derived from the picked words,
    /// so the spoken version cannot drift from the shown one and no new wording is invented.
    /// </summary>
    public static string Announcement(VoiceNote note) => note.Case == VoiceNoteCase.Blocked
        ? "The microphone is blocked. Allow Nth in Windows Settings, Privacy and security, Microphone."
        : note.Text.Replace(" > ", ", ", StringComparison.Ordinal) + ".";
}
