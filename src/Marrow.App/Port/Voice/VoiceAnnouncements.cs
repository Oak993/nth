// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10, voice-SPEC V-14.5): what the composer announces as polite live-region text
// (DESIGN voice-composer.js say()).
namespace Marrow.App.Port.Voice;

/// <summary>The announcements of a voice session, word for word. The orb's accessible names and these keep their wording for every pill word set (V-12.9).</summary>
public static class VoiceAnnouncements
{
    public const string Listening = "Listening. Your words are written into the composer as you speak. Click the orb again to finish, or press Escape to cancel.";

    public const string Finishing = "Finishing the text.";

    public const string Cancelled = "Voice input cancelled; the words were removed.";

    /// <summary>The language question opening (V-12.11, DESIGN voice-options.js <c>say()</c>).</summary>
    public const string LanguageQuestion = "Which language will you speak? English, Hindi, Urdu or Auto-detect.";

    public const string Blocked = "The microphone is blocked. Allow Nth in Windows Settings, Privacy and security, Microphone.";

    /// <summary>"Done: {text}", then what happens to the text: it is sent, or it waits in the composer for the user.</summary>
    public static string Done(string text, bool sending) =>
        "Done: " + text + (sending ? " Sending." : " The text is in the composer; send it when you are ready.");
}
