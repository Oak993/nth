using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-11: a stand-in for the cheap model that rewrites suggestion lines (no provider is called). It records each prompt and whether the batch was local-only.</summary>
public sealed class FakeSuggestionModel(Func<string, string?> answer) : ISuggestionModel
{
    private readonly List<string> prompts = [];
    private readonly List<bool> local = [];

    public IReadOnlyList<string> Prompts
    {
        get
        {
            lock (prompts) return [.. prompts];
        }
    }

    public IReadOnlyList<bool> Local
    {
        get
        {
            lock (prompts) return [.. local];
        }
    }

    public Task<string?> Reword(string prompt, bool localOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (prompts)
        {
            prompts.Add(prompt);
            local.Add(localOnly);
        }
        return Task.FromResult(answer(prompt));
    }
}

/// <summary>Dextra AS-11: a cheap model that fails with the given error.</summary>
public sealed class ThrowingSuggestionModel(Exception error) : ISuggestionModel
{
    public Task<string?> Reword(string prompt, bool local, CancellationToken cancellationToken) => throw error;
}

/// <summary>Dextra AS-11: a cheap model that never answers until it is cancelled (a slow provider). <see cref="Started"/> completes when it is asked.</summary>
public sealed class HangingSuggestionModel : ISuggestionModel
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string?> Reword(string prompt, bool local, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return null;
    }
}
