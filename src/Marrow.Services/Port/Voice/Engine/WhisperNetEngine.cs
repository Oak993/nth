// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the Whisper.net recogniser (voice-SPEC V-7.1, V-7.2, V-7.9, V-7.10).
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// <see cref="ISpeechEngine"/> over an <see cref="IWhisperBackend"/> (Whisper.net in production). One model is loaded at a time, from the model store's verified path (bind one
/// instance for the process). Loading a model releases the one before it; asking for the model already loaded shares it. Passes run one at a time. The model is released five
/// minutes after the last load or pass (V-7.9: the engine sees calls, not sessions), and a pass after that release loads the model it had again (ENGINEERING DECISION).
/// </summary>
public sealed class WhisperNetEngine(IWhisperBackend backend, Func<RuntimeFacts> runtimeFacts, Func<int> physicalCores) : ISpeechEngine, IDisposable
{
    /// <summary>V-7.9: "The model is unloaded after 5 minutes without a session."</summary>
    public static readonly TimeSpan IdleUnloadAfter = TimeSpan.FromMinutes(5);

    /// <summary>V-7.1: live passes use greedy sampling with best-of 1.</summary>
    public const int LiveBestOf = 1;

    /// <summary>V-7.1: the final pass is a beam search of beam 5.</summary>
    public const int FinalBeamSize = 5;

    private readonly SemaphoreSlim gate = new(1, 1);
    private IWhisperModel? model;
    private SpeechModel? lastModel;
    private EngineLoadOptions? lastOptions;
    private int threads = 1;
    private ITimer? idle;
    private bool disposed;

    public bool IsLoaded => Volatile.Read(ref model) is not null;

    /// <summary>The id of the model that is loaded, or null.</summary>
    public string? LoadedModelId => IsLoaded ? lastModel?.Id : null;

    /// <summary>The runtime that loaded the model, or null.</summary>
    public RuntimeKind? LoadedRuntime => Volatile.Read(ref model)?.Runtime;

    /// <summary>True when a GPU runtime (CUDA or Vulkan) is the one that loaded (V-7.1: the GPU is on when a GPU runtime loaded).</summary>
    public bool GpuActive => LoadedRuntime is RuntimeKind.Cuda12 or RuntimeKind.Vulkan;

    public async Task LoadAsync(SpeechModel speechModel, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(speechModel);
        if (speechModel.Path is null) throw new ArgumentException("A local engine needs the model store's verified path (V-7.1).", nameof(speechModel));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await LoadLocked(speechModel, options, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (audio16k.IsEmpty) return Hypothesis.Empty;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (model is null)
            {
                if (lastModel is null || lastOptions is null) throw new InvalidOperationException("No model is loaded (V-7.9: loading starts on the pointer-enter or the click).");
                await LoadLocked(lastModel, lastOptions, cancellationToken);
            }
            StopIdle();
            var auto = request.Language == "auto";
            var live = request.Mode == DecodeMode.Live;
            var decode = new BackendDecode(auto ? null : request.Language, request.Prompt, live ? BackendSampling.Greedy : BackendSampling.BeamSearch,
                LiveBestOf, FinalBeamSize, threads, 0f, true);
            var segments = await model!.DecodeAsync(audio16k, decode, cancellationToken);
            var language = auto ? segments.Select(s => s.Language).FirstOrDefault(l => !string.IsNullOrEmpty(l)) : null;
            return new Hypothesis(WordAssembler.Assemble(segments), language);
        }
        finally
        {
            if (model is not null && !disposed) Rearm();
            gate.Release();
        }
    }

    /// <summary>
    /// ED-NEW-voicefix-4: Whisper's language detection with probabilities, under the same one-at-a-time lock as a pass (it runs the encoder). Loads the model again after an
    /// idle release, as a pass does. Null for empty audio or a failed detection.
    /// </summary>
    public async Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runnersUp);
        cancellationToken.ThrowIfCancellationRequested();
        if (audio16k.IsEmpty) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (model is null)
            {
                if (lastModel is null || lastOptions is null) throw new InvalidOperationException("No model is loaded (V-7.9: loading starts on the pointer-enter or the click).");
                await LoadLocked(lastModel, lastOptions, cancellationToken);
            }
            StopIdle();
            return await model!.DetectLanguageAsync(audio16k, runnersUp, threads, cancellationToken);
        }
        finally
        {
            if (model is not null && !disposed) Rearm();
            gate.Release();
        }
    }

    /// <summary>Releases the model now. Waits for a pass in flight.</summary>
    public void Unload()
    {
        gate.Wait();
        try
        {
            Release();
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        disposed = true;
        StopIdle();
        Release();
        gate.Dispose();
    }

    private async Task LoadLocked(SpeechModel speechModel, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        if (model is not null && speechModel == lastModel && options == lastOptions)
        {
            Rearm();
            return;
        }
        Release();
        lastModel = null;
        lastOptions = null;
        var facts = runtimeFacts();
        var load = new BackendLoad(speechModel.Path!, options.UseGpu != false, RuntimeOrder.For(facts, options.UseGpu), facts.LibraryRoot);
        model = await backend.LoadAsync(load, cancellationToken);
        lastModel = speechModel;
        lastOptions = options;
        threads = options.Threads ?? ThreadCount.For(physicalCores());
        Rearm();
    }

    private void Release()
    {
        StopIdle();
        var loaded = model;
        model = null;
        loaded?.Dispose();
    }

    private void Rearm()
    {
        if (disposed) return;
        StopIdle();
        idle = SwiftClock.Provider.Current.CreateTimer(_ => IdleElapsed(), null, IdleUnloadAfter, Timeout.InfiniteTimeSpan);
    }

    private void StopIdle() => Interlocked.Exchange(ref idle, null)?.Dispose();

    private void IdleElapsed()
    {
        if (!gate.Wait(0)) return;
        try
        {
            Release();
        }
        finally
        {
            gate.Release();
        }
    }
}
