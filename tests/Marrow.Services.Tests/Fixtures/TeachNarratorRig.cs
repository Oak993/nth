using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-19: a narrator over the voice doubles (scripted capture, voice detector, recogniser and microphone), the lines it heard and the notes it made.</summary>
public sealed class TeachNarratorRig : IDisposable
{
    public TeachNarratorRig(ScriptedVad vad, ScriptedEngine engine, CaptureFormat? format = null, MicrophoneAccessStatus microphone = MicrophoneAccessStatus.Allowed, SpeechModel? model = null, bool resample = false)
    {
        Capture = new ScriptedCapture(format);
        Vad = vad;
        Engine = engine;
        Microphone = new ScriptedMicrophone(microphone);
        var seams = VoiceSeamSet.Of(Capture, vad, engine, Microphone, resample ? new PolyphaseResampler() : null);
        Narrator = new TeachNarrator(seams, () => ModelNow, () => "en", () => Clock, line => { lock (Lines) Lines.Add(line); }, note => { lock (Notes) Notes.Add(note); });
        ModelNow = model ?? new SpeechModel("small", null);
    }

    public ScriptedCapture Capture { get; }

    public ScriptedVad Vad { get; }

    public ScriptedEngine Engine { get; }

    public ScriptedMicrophone Microphone { get; }

    public TeachNarrator Narrator { get; }

    /// <summary>The recording's own time, which a test moves.</summary>
    public TimeSpan Clock { get; set; }

    public SpeechModel? ModelNow { get; set; }

    public List<Marrow.Core.Port.Assistant.TeachNarrationLine> Lines { get; } = [];

    public List<string> Notes { get; } = [];

    public static Hypothesis Said(string text) =>
        new([.. text.Split(' ').Select((word, i) => new HypothesisWord(word, i * 0.4, i * 0.4 + 0.3))]);

    public void Dispose() => Narrator.Dispose();
}
