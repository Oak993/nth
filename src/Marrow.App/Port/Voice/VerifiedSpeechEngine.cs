// PORT-ONLY: VT-15, V-9.4. Every local load, including hover and model changes, verifies the store before opening native code.
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

internal sealed class VerifiedSpeechEngine(ISpeechEngine engine, IVoiceModelSource source) : ISpeechEngine
{
    public async Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        var file = ModelCatalog.Find(model.Id) ?? throw new InvalidOperationException("Unknown speech model.");
        if (await source.VerifyAsync(file, cancellationToken) is ModelVerification.Corrupt or ModelVerification.Missing)
            throw new InvalidDataException("The voice model did not verify.");
        await engine.LoadAsync(new SpeechModel(file.Id, source.PathFor(file)), options, cancellationToken);
    }

    public Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken) =>
        engine.TranscribeAsync(audio16k, request, cancellationToken);
}
