using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

/// <summary>
/// VT-6: <see cref="IWhisperBackend"/> over a script (the ScriptedEngine shape for the Whisper.net adapter): loads and decodes are recorded in
/// <see cref="Log"/>, the runtime a load reports is set by <see cref="Loads"/>, a decode can be held until a test releases it, and a load or a
/// decode can be made to fail. It touches no native library, model file or network.
/// </summary>
public sealed class ScriptedWhisperBackend : IWhisperBackend
{
    private Exception? failLoad;
    private TaskCompletionSource? loadGate;
    private TaskCompletionSource? decodeGate;

    public List<string> Log { get; } = [];

    public List<BackendLoad> Loads { get; } = [];

    public List<ScriptedWhisperModel> Models { get; } = [];

    public List<BackendDecode> Decodes { get; } = [];

    public List<int> DecodedSamples { get; } = [];

    /// <summary>The runtime the next loads report.</summary>
    public RuntimeKind Reports { get; set; } = RuntimeKind.Vulkan;

    /// <summary>What a decode answers.</summary>
    public Func<BackendDecode, IReadOnlyList<BackendSegment>> Script { get; set; } = _ => [];

    /// <summary>What a language detection answers (ED-NEW-voicefix-4): from the runners-up asked about.</summary>
    public Func<IReadOnlyList<string>, LanguageOdds?> DetectScript { get; set; } = _ => null;

    /// <summary>The detections asked for: the runners-up, the threads and the samples.</summary>
    public List<(IReadOnlyList<string> RunnersUp, int Threads, int Samples)> Detections { get; } = [];

    public int InFlight { get; private set; }

    public int MaxInFlight { get; private set; }

    public void FailLoads(Exception? error) => failLoad = error;

    /// <summary>Every load waits until the returned action runs (or its token is cancelled).</summary>
    public Action HoldLoads()
    {
        var gate = loadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return () => gate.TrySetResult();
    }

    /// <summary>Every decode waits until the returned action runs (or its token is cancelled).</summary>
    public Action HoldDecodes()
    {
        var gate = decodeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return () => gate.TrySetResult();
    }

    public async Task<IWhisperModel> LoadAsync(BackendLoad load, CancellationToken cancellationToken)
    {
        Loads.Add(load);
        Log.Add("load:" + load.ModelPath);
        if (loadGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (failLoad is { } error) throw error;
        var model = new ScriptedWhisperModel(this, load.ModelPath, Reports);
        Models.Add(model);
        return model;
    }

    internal async Task<LanguageOdds?> DetectAsync(ScriptedWhisperModel model, ReadOnlyMemory<float> audio, IReadOnlyList<string> runnersUp, int threads, CancellationToken cancellationToken)
    {
        InFlight += 1;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        Detections.Add((runnersUp, threads, audio.Length));
        Log.Add("detect:" + model.Path);
        try
        {
            if (decodeGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return DetectScript(runnersUp);
        }
        finally
        {
            InFlight -= 1;
        }
    }

    internal async Task<IReadOnlyList<BackendSegment>> DecodeAsync(ScriptedWhisperModel model, ReadOnlyMemory<float> audio, BackendDecode decode, CancellationToken cancellationToken)
    {
        InFlight += 1;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        Decodes.Add(decode);
        DecodedSamples.Add(audio.Length);
        Log.Add("decode:" + model.Path);
        try
        {
            if (decodeGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Script(decode);
        }
        finally
        {
            InFlight -= 1;
        }
    }
}
