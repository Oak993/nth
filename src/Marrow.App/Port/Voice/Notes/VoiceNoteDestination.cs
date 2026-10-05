// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the place a note's one link opens (the page's "Where each link goes" table, set A).
namespace Marrow.App.Port.Voice.Notes;

/// <summary>PrivacyMicrophone and SoundSettings are Windows Settings pages; the rest are pages of the app's own Settings.</summary>
public enum VoiceNoteDestination
{
    PrivacyMicrophone,
    SoundSettings,
    VoiceTypingMicrophone,
    VoiceTypingSpeechModel,
    VoiceTypingEngine,
    VoiceTypingOpenAiKey,
    VoiceTypingPage,
}
