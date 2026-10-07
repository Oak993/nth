using Marrow.App.Port.Voice.Notes;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: one deterministic runtime, the scripted voice seams, a session on them and the note bridge over the session. Every note is
// driven by making a seam fail (the microphone, the capture, the engine, the processor), as the real seam would.
public sealed class VoiceNoteRig : IDisposable
{
    public VoiceNoteRig(IProcessorFeatures? processor = null)
    {
        Seams = new VoiceSeamSet(() => Capture, () => Vad, () => Engine, () => Microphone, () => new FailClosedStreamingResampler());
        Session = new VoiceSession(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", Model), _ => { }, Seams, Hub);
        Bridge = new VoiceNoteBridge(Session, () => Draft, () => ModelDownloading, () => PillShowsDownload, processor ?? Processor);
        Bridge.Changed += Shown.Add;
    }

    public TestRuntime Runtime { get; } = new();

    public ScriptedCapture Capture { get; } = new();

    public ScriptedMicrophone Microphone { get; } = new();

    public ScriptedVad Vad { get; set; } = ScriptedVad.Silence();

    public ScriptedEngine Engine { get; set; } = ScriptedEngine.Always(Hypothesis.Empty);

    public ScriptedProcessorFeatures Processor { get; } = new(true);

    public VoiceSessionHub Hub { get; } = new();

    public VoiceSeamSet Seams { get; }

    public VoiceSession Session { get; }

    public VoiceNoteBridge Bridge { get; }

    public SpeechModel? Model { get; set; } = new("tiny-q5_1", "model-tiny-q5_1.bin");

    public string? Draft { get; set; }

    public bool ModelDownloading { get; set; }

    public bool PillShowsDownload { get; set; }

    /// <summary>Every change of the bridge's current note, in order (null is "gone").</summary>
    public List<VoiceNotePresentation?> Shown { get; } = [];

    public VoiceNoteCase? Case => Bridge.Current?.Note.Case;

    /// <summary>Makes the speaker say the V-14.8 script: its signal, its speech spans, its hypotheses (the engine answers after <paramref name="engineLatency"/>).</summary>
    public void Speak(TimeSpan? engineLatency = null)
    {
        var script = new VoiceScript(
        [
            new ScriptedWord("switch", 0.5, 0.9),
            new ScriptedWord("the", 1.0, 1.2),
            new ScriptedWord("theme", 1.3, 1.8),
            new ScriptedWord("please.", 2.0, 2.5),
        ]);
        Capture.PlayOnStart(script.Signal(16000), TimeSpan.FromSeconds(script.SpeechEnd + 120));
        Vad = script.CreateVad();
        Engine = script.CreateEngine(engineLatency ?? TimeSpan.Zero);
    }

    /// <summary>The capture delivers the room's noise (never exactly zero) so the all-zero check does not fire.</summary>
    public void QuietRoom() => Capture.PlayOnStart(ScriptedSignals.RoomNoise, TimeSpan.FromSeconds(400));

    public void Advance(double seconds) => Runtime.Advance(TimeSpan.FromSeconds(seconds));

    public void Settle() => Runtime.RunUntilIdle();

    public void Dispose()
    {
        Bridge.Dispose();
        Runtime.Dispose();
    }
}
