using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Tests.Fixtures;

// VT-3 review fixture: an engine whose next decode waits until the test answers it, so audio can arrive while a pass decodes.
public sealed class GatedSpeechEngine : ISpeechEngine
{
    public List<(DecodeRequest Request, int Samples)> Decodes { get; } = [];

    public TaskCompletionSource<Hypothesis>? Gate { get; set; }

    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        Decodes.Add((request, audio16k.Length));
        if (Gate is { } gate)
        {
            Gate = null;
            return gate.Task;
        }
        return Task.FromResult(Hypothesis.Empty);
    }

    // Without detection (ED-NEW-voicefix-4): an "auto" session decodes with "auto", as before.
    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) =>
        Task.FromResult<LanguageOdds?>(null);
}
