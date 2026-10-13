// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 16.2; unit AS-19; ED-NEW-dextra-as19-9, -11). No Swift twin.
// What a teach recording uses on a real PC: the hooks, UI Automation, the screen, and the owner's own voice seams for narration. Each recording
// gets a new set. Narration is the local speech engine only: it uses the voice seams the app bound for dictation (the microphone capture, the
// voice detector, the local recogniser) and the speech model the owner chose for dictation; when the owner has none, narration is off and the
// recording says so. No cloud engine is ever reached from here.
using Marrow.App.Port.Voice;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Teach;

public static class WindowsTeachPlatform
{
    /// <summary>A new set of the Windows parts for one recording. <paramref name="browser"/> is Visus's steps when Visus is bound.</summary>
    public static TeachPlatform Create(IBrowserSteps? browser = null) => new(new WindowsTeachInput(), new UiaElementProbe(), new ScreenFrames())
    {
        Narration = (clock, heard, noted) => new TeachNarrator(VoiceSeamSet.Ambient, LocalModel, Language, clock, heard, noted),
        Browser = browser,
        Source = TeachSource.Direct,
    };

    // The model the owner chose for dictation, when he has one on this PC. The engine behind the voice seams is the local one; the cloud
    // engine is a separate object that nothing here touches. A model the engine cannot load fails there, and narration says so and goes off.
    private static SpeechModel? LocalModel() =>
        VoiceComposition.StoredTyping().Model is { Length: > 0 } id ? new SpeechModel(id, null) : null;

    // The language the owner set for dictation when it is a plain language code; else the engine finds the language itself.
    private static string Language() =>
        VoiceComposition.StoredTyping().Lang is { Length: > 0 and <= 8 } code && code.All(c => char.IsLetter(c) || c == '-') ? code : "auto";
}
