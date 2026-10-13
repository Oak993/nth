using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-18: a Dextra host over a real controller (a temp store) whose voice is built from doubles: a chord thread that registers nothing, a capture that opens no
/// microphone, a synth and speakers that make no sound, and a window "brought forward" that is only a line in <see cref="Log"/>. Made inside the harness' main loop.
/// </summary>
internal sealed class DextraVoiceRig : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly AssistantStore store;

    public DextraVoiceRig(InMemorySettingsStore? defaults = null)
    {
        Harness = new AppModelHarness();
        var model = Harness.MakeModel();
        Harness.PrepareBootstrap(model);
        store = AssistantStore.Open(temp.Roots, new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted });
        var settings = defaults ?? new InMemorySettingsStore();
        new DextraSettings(settings).Value = DextraPreferences.Default with { FirstRunDone = true };
        var engine = ScriptedEngine.Always(new Hypothesis([new HypothesisWord("hello", 0, 0.3)]));
        Parts = new DextraVoiceParts
        {
            ChordHost = () => Chord,
            Capture = () => Capture,
            Vad = () => new EnergyVad(),
            Resampler = () => new Marrow.Core.Port.Voice.Dsp.PolyphaseResampler(),
            Engine = () => engine,
            Microphone = () => new ScriptedMicrophone(),
            Synth = () => Synth,
            Player = () => Player,
            Model = _ => new SpeechModel("test", null),
            Typing = () => VoiceTypingSettings.Default,
            PreemptDictation = () => Log.Add("dictation ended"),
            BringForward = _ => Log.Add("window forward"),
            Post = action => action(),
        };
        // the scope the controller is given is the one the voice binds, as the app's own start does it
        var scope = new VoiceStopScope();
        Host = DextraHosts.Make(model, settings, (app, _, id) => app.StartDextra(store, _ => { }, () => [new AssistantProfile(id.ToString("N"), "Dextra")], stopScopes: [scope]),
            (app, dextra, _) => DextraVoice.Create(app, dextra, scope, Parts));
    }

    public AppModelHarness Harness { get; }

    public DextraHost Host { get; }

    public FakeStopChordHost Chord { get; } = new();

    public ScriptedCapture Capture { get; } = new();

    public ScriptedSpeechSynth Synth { get; } = new();

    public ScriptedSpeechPlayer Player { get; } = new();

    public DextraVoiceParts Parts { get; }

    public List<string> Log { get; } = [];

    public void Dispose()
    {
        Host.Dispose();
        store.Dispose();
        temp.Dispose();
        Harness.Dispose();
    }
}
