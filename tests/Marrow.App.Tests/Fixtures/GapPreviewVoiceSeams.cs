using Marrow.Core.Port.Voice.Seams;

namespace Marrow.App.Tests.Fixtures;

// GAP-2: record forbidden preview I/O before throwing, so swallowing an exception cannot hide a call.
internal sealed class GapPreviewVoiceSeams : IMicrophoneAccess, IAudioCapture, ISpeechEngine, IModelTransport
{
    internal int Calls { get; private set; }

    private Exception Touched(string operation)
    {
        Calls++;
        return new InvalidOperationException("Preview touched " + operation);
    }

    public MicrophoneAccessStatus Status() => throw Touched("microphone");

    public CaptureFormat Start(IAudioCaptureSink sink) => throw Touched("capture start");

    public void Stop() => throw Touched("capture stop");

    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken) => throw Touched("model load");

    public Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken) => throw Touched("transcription");

    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) => throw Touched("language detection");

    public Task<ModelTransportResponse> GetRange(Uri uri, long offset, CancellationToken cancellationToken) => throw Touched("model download");
}
