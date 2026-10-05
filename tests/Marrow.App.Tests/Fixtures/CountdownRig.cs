using Marrow.App.Port.Voice.Countdown;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: one deterministic runtime, a session on scripted seams listening to the room's noise, and the countdown line over it. Time moves only by Advance.
public sealed class CountdownRig : IDisposable
{
    public CountdownRig()
    {
        var seams = new VoiceSeamSet(() => Capture, () => ScriptedVad.Silence(), () => ScriptedEngine.Always(Hypothesis.Empty), () => new ScriptedMicrophone(), () => new FailClosedStreamingResampler());
        Session = new VoiceSession(() => new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("tiny-q5_1", "model-tiny-q5_1.bin")), _ => { }, seams, new VoiceSessionHub());
        Line = new CapCountdownLine(Session, () => ReduceMotion, () => Palette);
        Line.Announced += Announcements.Add;
        Session.StateChanged += (_, to) => States.Add(to);
        Capture.PlayOnStart(ScriptedSignals.RoomNoise, TimeSpan.FromSeconds(400));
    }

    public TestRuntime Runtime { get; } = new();

    public ScriptedCapture Capture { get; } = new();

    public VoiceSession Session { get; }

    public CapCountdownLine Line { get; }

    public bool ReduceMotion { get; set; }

    public CountdownPalette Palette { get; set; } = CountdownPalette.Dark;

    public List<string> Announcements { get; } = [];

    public List<VoiceSessionState> States { get; } = [];

    public void Start()
    {
        Session.Activate();
        Runtime.RunUntilIdle();
    }

    public void Advance(double seconds) => Runtime.Advance(TimeSpan.FromSeconds(seconds));

    public void Dispose()
    {
        Line.Dispose();
        Runtime.Dispose();
    }
}
