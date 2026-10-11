// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-3): the fail-closed default of every voice seam
// (voice-SPEC V-2.5). Each member throws SeamNotBoundException synchronously, also the ones that return a Task.
namespace Marrow.Core.Port.Voice.Seams;

/// <summary><see cref="IAudioCapture"/> default: opens no microphone.</summary>
public sealed class FailClosedAudioCapture : IAudioCapture
{
    public CaptureFormat Start(IAudioCaptureSink sink) => throw new SeamNotBoundException(nameof(IAudioCapture), nameof(Start));

    public void Stop() => throw new SeamNotBoundException(nameof(IAudioCapture), nameof(Stop));
}

/// <summary><see cref="IVoiceActivityDetector"/> default.</summary>
public sealed class FailClosedVoiceActivityDetector : IVoiceActivityDetector
{
    public int FrameSamples => throw new SeamNotBoundException(nameof(IVoiceActivityDetector), nameof(FrameSamples));

    public float Probability(ReadOnlySpan<float> frame) => throw new SeamNotBoundException(nameof(IVoiceActivityDetector), nameof(Probability));

    public void Reset() => throw new SeamNotBoundException(nameof(IVoiceActivityDetector), nameof(Reset));
}

/// <summary><see cref="ISpeechEngine"/> default: loads no model, decodes nothing, reaches no network.</summary>
public sealed class FailClosedSpeechEngine : ISpeechEngine
{
    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(ISpeechEngine), nameof(LoadAsync));

    public Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(ISpeechEngine), nameof(TranscribeAsync));

    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(ISpeechEngine), nameof(DetectLanguageAsync));
}

/// <summary><see cref="IMicrophoneAccess"/> default.</summary>
public sealed class FailClosedMicrophoneAccess : IMicrophoneAccess
{
    public MicrophoneAccessStatus Status() => throw new SeamNotBoundException(nameof(IMicrophoneAccess), nameof(Status));
}

/// <summary><see cref="IStreamingResampler"/> default.</summary>
public sealed class FailClosedStreamingResampler : IStreamingResampler
{
    public void Process(int inputRate, ReadOnlySpan<float> mono, List<float> output) =>
        throw new SeamNotBoundException(nameof(IStreamingResampler), nameof(Process));

    public void Reset() => throw new SeamNotBoundException(nameof(IStreamingResampler), nameof(Reset));
}

/// <summary><see cref="IModelTransport"/> default: no download.</summary>
public sealed class FailClosedModelTransport : IModelTransport
{
    public Task<ModelTransportResponse> GetRange(Uri uri, long offset, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(IModelTransport), nameof(GetRange));
}
