using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// VT-5 test fixture: a real VoiceSession (VT-3) over the production WasapiCapture (VT-5) and the real polyphase resampler (VT-2), with the scripted endpoint host,
// VAD, engine and microphone permission. The microphone is always the fake host's device; the capture's processing step is run by hand after each push
// (runThread: false), so everything moves with the manual clock.
public sealed class CaptureSessionRig : IDisposable
{
    public CaptureSessionRig(DeviceFormat? format = null, string? pick = null)
    {
        Host = new ScriptedCaptureHost(("mic-a", "Microphone Array"), ("mic-b", "USB Headset"));
        if (format is { } f) Host.SetFormat("mic-a", f);
        Capture = new WasapiCapture(Host, () => pick, runThread: false);
        Seams = new VoiceSeamSet(() => Capture, () => Vad, () => Engine, () => Microphone, () => Resampler);
        Session = new VoiceSession(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", Model), _ => { }, Seams, new VoiceSessionHub());
        Session.NoteRaised += Notes.Add;
        Session.StateChanged += (_, to) => Visited.Add(to);
    }

    public TestRuntime Runtime { get; } = new();

    public ScriptedCaptureHost Host { get; }

    public WasapiCapture Capture { get; }

    public ScriptedMicrophone Microphone { get; set; } = new();

    public ScriptedVad Vad { get; set; } = ScriptedVad.Silence();

    public ScriptedEngine Engine { get; set; } = ScriptedEngine.Always(Hypothesis.Empty);

    public PolyphaseResampler Resampler { get; } = new();

    public SpeechModel Model { get; } = new("tiny-q5_1", "model-tiny-q5_1.bin");

    public VoiceSeamSet Seams { get; }

    public VoiceSession Session { get; }

    public List<VoiceNoteKind> Notes { get; } = [];

    public List<VoiceSessionState> Visited { get; } = [];

    public ScriptedCaptureDevice Device => Host.LastDevice ?? throw new InvalidOperationException("The capture has not opened a device.");

    /// <summary>The speaker says <paramref name="script"/>: its VAD and its engine; its signal is what <see cref="Feed"/> is given.</summary>
    public void Speak(VoiceScript script)
    {
        Vad = script.CreateVad();
        Engine = script.CreateEngine();
    }

    /// <summary>One 10 ms step: the device pushes <paramref name="signal"/> from the session's clock, the processing step runs, the main loop runs, the clock moves 10 ms.</summary>
    public void Feed(Func<long, float> signal, double seconds, bool silent = false)
    {
        var steps = (int)Math.Round(seconds * 100);
        var rate = Device.Format.SampleRate;
        var frames = rate / 100;
        for (var i = 0; i < steps; i++)
        {
            var samples = new float[frames * Device.Format.Channels];
            for (var f = 0; f < frames; f++)
            {
                var value = signal(Device.FramesPushed + f);
                for (var c = 0; c < Device.Format.Channels; c++) samples[f * Device.Format.Channels + c] = value;
            }
            Device.Push(samples, silent);
            Capture.Pump();
            Runtime.AdvanceMilliseconds(10);
        }
    }

    public void Advance(double seconds) => Runtime.Advance(TimeSpan.FromSeconds(seconds));

    public void Dispose()
    {
        Capture.Stop();
        Runtime.Dispose();
    }
}
