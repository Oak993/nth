using Marrow.App.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-19: the model that writes a draft up, as a test scripts it: what it is asked, and what it answers (or that it fails).</summary>
internal sealed class FakeDraftModel(Func<string, string?>? answer = null) : ITeachDraftModel
{
    public List<string> Prompts { get; } = [];

    public Exception? Throws { get; set; }

    public Task<string?> Complete(string prompt, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        lock (Prompts) Prompts.Add(prompt);
        if (Throws is { } failure) throw failure;
        return Task.FromResult(answer?.Invoke(prompt));
    }
}
