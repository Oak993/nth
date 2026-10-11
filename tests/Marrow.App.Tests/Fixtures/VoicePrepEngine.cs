using Marrow.Core.Port.Voice.Seams;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-15: a speech engine whose timed decode takes the scripted seconds of the model last loaded, on the manual clock (so the self-test's RTF is exact and no real time passes).</summary>
public sealed class VoicePrepEngine(ManualTimeProvider clock, IReadOnlyDictionary<string, double> seconds) : ISpeechEngine
{
    private string current = "";

    public List<(SpeechModel Model, EngineLoadOptions Options)> Loads { get; } = [];

    public List<DecodeRequest> Decodes { get; } = [];

    public Exception? FailLoad { get; set; }

    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        Loads.Add((model, options));
        if (FailLoad is { } error) throw error;
        current = model.Id;
        return Task.CompletedTask;
    }

    public Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        Decodes.Add(request);
        clock.Advance(TimeSpan.FromSeconds(seconds[current]));
        return Task.FromResult(Hypothesis.Empty);
    }

    // The self-test decodes in a forced language; this engine has no detection (ED-NEW-voicefix-4).
    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) =>
        Task.FromResult<LanguageOdds?>(null);
}
