// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-3): the recognition double (voice-SPEC V-2.4
// "hypotheses as a function of the audio length decoded, with a scripted latency on the manual clock; throws on demand").
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>What a scripted engine is asked: the decoded audio's length, the request, and the audio itself.</summary>
public sealed record ScriptedDecode(TimeSpan AudioLength, DecodeRequest Request, ReadOnlyMemory<float> Audio)
{
    /// <summary>The session time of the last decoded sample.</summary>
    public TimeSpan AudioEnd => Request.AudioStart + AudioLength;
}

/// <summary>
/// <see cref="ISpeechEngine"/> over a function. Waits (<see cref="Latency"/>, <see cref="LoadLatency"/>) are timers on
/// <see cref="SwiftClock"/> bounded by the call's token, so a test moves them with the manual clock and cancels them.
/// </summary>
public sealed class ScriptedEngine(Func<ScriptedDecode, Hypothesis> script, TimeSpan latency = default) : ISpeechEngine
{
    private Exception? failLoad;
    private Exception? failNextTranscribe;

    /// <summary>An engine that always answers the same.</summary>
    public static ScriptedEngine Always(Hypothesis hypothesis, TimeSpan latency = default) => new(_ => hypothesis, latency);

    /// <summary>The time one decode takes on the clock.</summary>
    public TimeSpan Latency { get; set; } = latency;

    /// <summary>The time a load takes on the clock.</summary>
    public TimeSpan LoadLatency { get; set; }

    public List<(SpeechModel Model, EngineLoadOptions Options)> Loads { get; } = [];

    public List<ScriptedDecode> Decodes { get; } = [];

    /// <summary>
    /// What language detection answers for the audio and the runners-up asked about (ED-NEW-voicefix-4); null (the default) is an engine without detection, which is
    /// what every script before it was written against.
    /// </summary>
    public Func<ScriptedDecode, IReadOnlyList<string>, LanguageOdds?>? Detect { get; set; }

    /// <summary>The detections asked for: the audio (as a decode with the request "auto") and the runners-up.</summary>
    public List<(ScriptedDecode Audio, IReadOnlyList<string> RunnersUp)> Detections { get; } = [];

    /// <summary>Decodes started and not yet finished.</summary>
    public int InFlight { get; private set; }

    /// <summary>The most decodes that were ever in flight at once (the engine is single: this stays 1).</summary>
    public int MaxInFlight { get; private set; }

    /// <summary>Decodes that ended because their token was cancelled.</summary>
    public int Aborted { get; private set; }

    /// <summary>Every load throws this until it is set to null.</summary>
    public void FailLoads(Exception? error) => failLoad = error;

    /// <summary>The next decode (only) throws this.</summary>
    public void FailNextDecode(Exception error) => failNextTranscribe = error;

    public async Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        Loads.Add((model, options));
        if (LoadLatency > TimeSpan.Zero) await Task.Delay(LoadLatency, SwiftClock.Provider.Current, cancellationToken);
        if (failLoad is { } error) throw error;
    }

    public async Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        var decode = new ScriptedDecode(TimeSpan.FromSeconds(audio16k.Length / 16000.0), request, audio16k);
        Decodes.Add(decode);
        InFlight += 1;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        try
        {
            if (failNextTranscribe is { } error)
            {
                failNextTranscribe = null;
                throw error;
            }
            if (Latency > TimeSpan.Zero) await Task.Delay(Latency, SwiftClock.Provider.Current, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return script(decode);
        }
        catch (OperationCanceledException)
        {
            Aborted += 1;
            throw;
        }
        finally
        {
            InFlight -= 1;
        }
    }

    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Detect is not { } detect) return Task.FromResult<LanguageOdds?>(null);
        var audio = new ScriptedDecode(TimeSpan.FromSeconds(audio16k.Length / 16000.0), new DecodeRequest("auto", "", DecodeMode.Live, TimeSpan.Zero), audio16k);
        Detections.Add((audio, runnersUp));
        return Task.FromResult(detect(audio, runnersUp));
    }
}
