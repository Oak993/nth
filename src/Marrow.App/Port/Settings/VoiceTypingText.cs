// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-17, Q-V-19 B; first-release voice typing, VT-13): the words of the Voice typing page's first section (voice-SPEC V-16.3.1, V-16.3.5).
namespace Marrow.App.Port.Settings;

/// <summary>The page's words, one place each. The Language row's words are <c>VoiceLanguageRow</c>'s (Core); the Speech model section's are VT-14's.</summary>
internal static class VoiceTypingText
{
    public const string Section = "Voice typing";
    public const string EngineTitle = "Engine";
    public const string EngineLocal = "On this PC";
    public const string EngineCloud = "OpenAI cloud";
    public const string EngineLocalDetail = "Your voice never leaves this computer";
    public const string EngineCloudDetail = "Sent to OpenAI with your own key, about half a US cent a minute";
    public const string KeyTitle = "OpenAI key";
    public const string KeyDetail = "Cloud voice typing uses your own key, kept in Windows’ credential store";
    public const string KeyAdd = "Add key";
    public const string KeyCancel = "Cancel";
    public const string KeyPlaceholder = "Paste your OpenAI key";
    public const string KeyRefusal = "Credential Manager did not accept this key, so it was not saved.";
    public const string LanguageTitle = "Language";
    public const string MicrophoneTitle = "Microphone";
    public const string OrbTitle = "Orb and after voice input";
    public const string OrbDetail = "How the orb looks, and whether the words are sent";
    public const string OrbButton = "Customization";
    public const string PrivacyLocal = "Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.";
    public const string PrivacyCloud = "Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.";
}
