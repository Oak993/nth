using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-18: a whole voice conversation on virtual time. The microphone is a scripted capture fed 10 ms at a time; what the owner says is a synthetic
/// voice; what Dextra says is a synthetic voice of its own, played by a scripted speaker whose clock moves with the microphone's, and heard back by the
/// microphone as an echo (a gain and a delay, with a reflection). The speech engine answers the texts the test queues, one for each final decode. Nothing
/// is opened, nothing sounds, and every wait is the manual clock.
/// </summary>
internal sealed class VoiceRig : IDisposable
{
    public const int Chunk = 160;

    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    private readonly Random room = new(5);

    public VoiceRig(Func<VoiceConversationOptions, VoiceConversationOptions>? change = null)
    {
        Time = new ManualTimeProvider();
        Player = new ScriptedSpeechPlayer(Time);
        Speech = new SpeechOut(Synth, () => Player, Reference);
        Synth.Render = _ => new SynthAudio(Said, SyntheticSpeech.Rate);
        Engine = new ScriptedEngine(Decide);
        var options = new VoiceConversationOptions
        {
            Capture = Capture,
            Vad = () => new EnergyVad(),
            Resampler = () => new PolyphaseResampler(),
            Engine = () => Engine,
            Model = () => Model,
            Microphone = Microphone,
            Speech = Speech,
            Brain = Brain,
            Jobs = Jobs,
            Decisions = Decisions,
            Controls = Controls,
            ShowCard = Shown.Add,
            PreemptDictation = () => Preempted++,
            StopLease = () =>
            {
                Leases++;
                if (!LeaseAvailable) return null;
                LeasesHeld++;
                return new CallbackDisposable(() => LeasesHeld--);
            },
            Time = Time,
            Post = action => action(),
            Background = work => work(),
        };
        Conversation = new VoiceConversation(change?.Invoke(options) ?? options);
    }

    public ManualTimeProvider Time { get; }

    public ScriptedCapture Capture { get; } = new();

    public ScriptedMicrophone Microphone { get; } = new();

    public ScriptedEngine Engine { get; }

    public ScriptedSpeechSynth Synth { get; } = new();

    public ScriptedSpeechPlayer Player { get; }

    public VoiceEchoReference Reference { get; } = new();

    public SpeechOut Speech { get; }

    public FakeVoiceBrain Brain { get; } = new();

    public FakeMissionJobs Jobs { get; } = new();

    public FakeDecisions Decisions { get; } = new();

    public FakeGlobalControls Controls { get; } = new();

    public List<Guid> Shown { get; } = [];

    public VoiceConversation Conversation { get; }

    /// <summary>What the engine "hears", one text for each final decode, in order.</summary>
    public Queue<string> Heard { get; } = new();

    public SpeechModel? Model { get; set; } = new("test", null);

    /// <summary>The sound of Dextra's voice: the speaker plays it and the microphone hears it back.</summary>
    public float[] Said { get; set; } = SyntheticSpeech.Voice(7, 10, 0.1);

    public double EchoGain { get; set; } = 0.5;

    public int EchoDelay { get; set; } = 960;

    public int Preempted { get; private set; }

    public int Leases { get; private set; }

    public int LeasesHeld { get; private set; }

    public bool LeaseAvailable { get; set; } = true;

    private Hypothesis Decide(ScriptedDecode decode)
    {
        var text = decode.Request.Mode == DecodeMode.Final ? (Heard.Count > 0 ? Heard.Dequeue() : "") : (Heard.Count > 0 ? Heard.Peek().Split(' ')[0] : "");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((w, i) => new HypothesisWord(w, i * 0.3, i * 0.3 + 0.25)).ToList();
        return new Hypothesis(words);
    }

    /// <summary>One 10 ms step: the owner's chunk (silence if null), what the room adds, and the echo of what Dextra is playing, into the microphone; then the clocks move.</summary>
    public void Step(ReadOnlySpan<float> owner = default)
    {
        var mic = new float[Chunk];
        for (var i = 0; i < Chunk; i++) mic[i] = (float)((room.NextDouble() * 2 - 1) * 0.0026);
        if (!owner.IsEmpty)
        {
            for (var i = 0; i < Math.Min(Chunk, owner.Length); i++) mic[i] += owner[i];
        }
        if (Player.Playing)
        {
            var at = (int)Math.Round(Player.Played.TotalSeconds * SyntheticSpeech.Rate);
            for (var i = 0; i < Chunk; i++)
            {
                var direct = at + i - EchoDelay;
                var late = direct - 190;
                var echo = 0.0;
                if (direct >= 0 && direct < Said.Length) echo += Said[direct];
                if (late >= 0 && late < Said.Length) echo += 0.25 * Said[late];
                mic[i] += (float)(EchoGain * echo);
            }
        }
        Capture.Push(mic);
        Time.Advance(Tick);
        if (Player.Playing) Player.Advance(Tick);
    }

    /// <summary>The owner says <paramref name="voice"/> (a synthetic voice), 10 ms at a time; the room and the echo go on around it.</summary>
    public void Say(float[] voice)
    {
        for (var at = 0; at < voice.Length; at += Chunk) Step(voice.AsSpan(at, Math.Min(Chunk, voice.Length - at)));
    }

    /// <summary>Nobody speaks for <paramref name="seconds"/>; the room is quiet and Dextra's echo goes on.</summary>
    public void Wait(double seconds)
    {
        for (var n = 0; n < (int)Math.Round(seconds * 100); n++) Step();
    }

    /// <summary>Steps until <paramref name="done"/> is true, at most <paramref name="seconds"/> of virtual time; false when it never was.</summary>
    public bool WaitUntil(Func<bool> done, double seconds = 20)
    {
        for (var n = 0; n < (int)Math.Round(seconds * 100); n++)
        {
            if (done()) return true;
            Step();
        }
        return done();
    }

    /// <summary>The owner says a request of about <paramref name="seconds"/> and falls silent for 1.5 s, so the request ends; <paramref name="text"/> is what the engine hears.</summary>
    public void Ask(string text, double seconds = 1.6, int seed = 2)
    {
        Heard.Enqueue(text);
        Say(SyntheticSpeech.Voice(seed, seconds, 0.12));
        Wait(1.5);
    }

    public void Dispose() => Conversation.Dispose();
}
