using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// VT-3 test fixture: one deterministic runtime, the scripted seams, a hub and a journal of what a session reported. Settings are read at each
// click, as a real session reads them; the seams are looked up at use so a test can swap the VAD or the engine after building the rig.
public sealed class VoiceSessionRig : IDisposable
{
    public VoiceSessionRig()
    {
        Seams = new VoiceSeamSet(() => Capture, () => Vad, () => Engine, () => Microphone, () => Resampler);
        Session = NewSession();
    }

    public TestRuntime Runtime { get; } = new();

    public ScriptedCapture Capture { get; set; } = new();

    public ScriptedMicrophone Microphone { get; set; } = new();

    public ScriptedVad Vad { get; set; } = ScriptedVad.Silence();

    public ScriptedEngine Engine { get; set; } = ScriptedEngine.Always(Hypothesis.Empty);

    public IStreamingResampler Resampler { get; set; } = new FailClosedStreamingResampler();

    public VoiceSeamSet Seams { get; }

    public VoiceSessionHub Hub { get; } = new();

    public VoiceOrbSettings Orb { get; set; } = VoiceOrbSettings.Default;

    public VoiceTypingSettings Typing { get; set; } = VoiceTypingSettings.Default;

    public string? WindowsUiLanguage { get; set; } = "en-US";

    public SpeechModel? Model { get; set; } = new("tiny-q5_1", "model-tiny-q5_1.bin");

    public List<VoiceTypingSettings> Stored { get; } = [];

    public VoiceSession Session { get; }

    public List<(VoiceSessionState From, VoiceSessionState To)> States { get; } = [];

    public List<TranscriptUpdate> Updates { get; } = [];

    public List<VoiceNoteKind> Notes { get; } = [];

    public List<SendIntent> Sends { get; } = [];

    public int CountdownStarts { get; private set; }

    /// <summary>Another session on the same hub and seams (another composer's orb).</summary>
    public VoiceSession NewSession()
    {
        var session = new VoiceSession(() => new VoiceSessionSettings(Orb, Typing, WindowsUiLanguage, Model),
            typing => { Stored.Add(typing); Typing = typing; }, Seams, Hub);
        session.StateChanged += (from, to) => States.Add((from, to));
        session.TranscriptChanged += Updates.Add;
        session.NoteRaised += Notes.Add;
        session.SendRequested += Sends.Add;
        session.CapCountdownStarted += () => CountdownStarts += 1;
        return session;
    }

    /// <summary>Makes the speaker say <paramref name="script"/>: its signal, its speech spans, its hypotheses.</summary>
    public void Speak(VoiceScript script, TimeSpan? engineLatency = null)
    {
        Capture.PlayOnStart(script.Signal(16000), TimeSpan.FromSeconds(script.SpeechEnd + 120));
        Vad = script.CreateVad();
        Engine = script.CreateEngine(engineLatency ?? TimeSpan.Zero);
    }

    /// <summary>The capture delivers the room's noise (never exactly zero) for the whole session.</summary>
    public void QuietRoom() => Capture.PlayOnStart(ScriptedSignals.RoomNoise, TimeSpan.FromSeconds(400));

    public void Advance(double seconds) => Runtime.Advance(TimeSpan.FromSeconds(seconds));

    /// <summary>Runs what the session posted or spawned for the current instant.</summary>
    public void Settle() => Runtime.RunUntilIdle();

    /// <summary>The script used by most session tests: four words over 2.5 s, "theme" first heard as "team" (V-14.8's first guess).</summary>
    public static VoiceScript SwitchTheme(string? language = null) => new(
    [
        new ScriptedWord("switch", 0.5, 0.9),
        new ScriptedWord("the", 1.0, 1.2),
        new ScriptedWord("theme", 1.3, 1.8, FirstGuess: "team"),
        new ScriptedWord("please.", 2.0, 2.5),
    ], language);

    public IReadOnlyList<VoiceSessionState> Visited => States.Select(s => s.To).ToList();

    public string FinalText => string.Join(" ", Session.Words.Select(w => w.Text));

    public void Dispose() => Runtime.Dispose();
}
