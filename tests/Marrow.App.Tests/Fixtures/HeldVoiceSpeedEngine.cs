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
    public void Complete() => gate.SetResult();
}
