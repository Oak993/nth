using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-19: a teach recorder over a throwaway assistant store, the hook, UI Automation, the screen and the narration as fakes, a stop
/// chord on a fake host, and a clock that moves only when a test moves it. Helper code only (R-TEST-3).
/// </summary>
internal sealed class TeachRecorderRig : IDisposable
{
    private readonly TeachRig services = new();

    public TeachRecorderRig(bool narration = false, bool stopKey = true, long? softBytes = null, long? hardBytes = null, int? maxEvents = null, bool browser = false)
    {
        Host = new FakeStopChordHost();
        Stop = new GlobalStop(Host, () => null, Clock);
        Input = new FakeTeachInput();
        Probe = new FakeProbe();
        Frames = new FakeFrames();
        Browser = browser ? new FakeBrowser() : null;
        Recorder = new TeachRecorder(new TeachRecorderOptions
        {
            Recordings = services.Recordings,
            Input = Input,
            Probe = Probe,
            Frames = Frames,
            Time = Clock,
            StopKey = stopKey ? Stop : null,
            Browser = Browser,
            SoftBytes = softBytes ?? TeachLimits.FrameSoftBytes,
            HardBytes = hardBytes ?? TeachLimits.FrameHardBytes,
            MaxEvents = maxEvents ?? TeachLimits.MaxEvents,
            Narration = narration ? (clock, heard, noted) => Narration = new FakeNarration { Clock = clock, Heard = heard, Noted = noted } : null,
            Ended = info => Ended.Add(info),
            Faulted = message =>
            {
                lock (Faults) Faults.Add(message);
            },
        });
    }

    public ManualTimeProvider Clock => services.Clock;

    public AssistantStore Store => services.Store;

    public TeachRecordings Recordings => services.Recordings;

    public FakeStopChordHost Host { get; }

    public GlobalStop Stop { get; }

    public FakeTeachInput Input { get; }

    public FakeProbe Probe { get; }

    public FakeFrames Frames { get; }

    public FakeNarration? Narration { get; private set; }

    public FakeBrowser? Browser { get; }

    public TeachRecorder Recorder { get; }

    public List<TeachRecordingInfo> Ended { get; } = [];

    public List<string> Faults { get; } = [];

    public string Folder => services.RecordingFolder(Recorder.Id!);

    /// <summary>Starts the recorder and asserts nothing; the test reads the result.</summary>
    public TeachStartResult Start() => Recorder.Start();

    /// <summary>Everything the hook, the timer and the narration queued has been handled.</summary>
    public Task Settle() => Recorder.Flush();

    public IReadOnlyList<TeachEvent> Events() => Recordings.Events(Recorder.Id!, out _);

    /// <summary>Every byte of every file of the recording, as text: what a scan for a secret reads.</summary>
    public string EverythingOnDisk()
    {
        var all = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            all.Append(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file)));
        return all.ToString();
    }

    /// <summary>The recording runs for this long, a second at a time so the frame timer ticks as it would.</summary>
    public async Task Run(TimeSpan time)
    {
        for (var left = time; left > TimeSpan.Zero; left -= TimeSpan.FromSeconds(1))
        {
            Clock.Advance(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1));
            await Settle();
        }
    }

    public void Dispose()
    {
        Recorder.Dispose();
        Stop.Dispose();
        services.Dispose();
    }
}
