using Marrow.Core.Port.Voice.Seams;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

public sealed class HeldVoiceSpeedEngine(ManualTimeProvider clock) : ISpeechEngine
{
    private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Model { get; private set; }
    public Task LoadAsync(SpeechModel model, EngineLoadOptions options, CancellationToken cancellationToken)
    {
        Model = model.Id;
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return Task.CompletedTask;
    }
    public async Task<Hypothesis> TranscribeAsync(ReadOnlyMemory<float> audio16k, DecodeRequest request, CancellationToken cancellationToken)
    {
        await gate.Task.WaitAsync(cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(Model == "large-v3-turbo-q5_0" ? 9 : 7));
        return Hypothesis.Empty;
    }

    // A speed run decodes in a forced language; this engine has no detection (ED-NEW-voicefix-4).
    public Task<LanguageOdds?> DetectLanguageAsync(ReadOnlyMemory<float> audio16k, IReadOnlyList<string> runnersUp, CancellationToken cancellationToken) =>
        Task.FromResult<LanguageOdds?>(null);
    public void Complete() => gate.SetResult();
}
