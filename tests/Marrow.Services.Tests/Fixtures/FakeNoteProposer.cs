using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-12: a stand-in for the cheap model that proposes notes (no provider is called). It records each turn and prompt it is asked.</summary>
public sealed class FakeNoteProposer(Func<string, string?> answer) : INoteProposer
{
    public List<string> Prompts { get; } = [];

    public List<CapturedTurn> Turns { get; } = [];

    public Task<string?> Propose(CapturedTurn turn, string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Turns)
        {
            Turns.Add(turn);
            Prompts.Add(prompt);
        }
        return Task.FromResult(answer(prompt));
    }
}

/// <summary>Dextra AS-12: a cheap model that fails with the given error.</summary>
public sealed class ThrowingNoteProposer(Exception error) : INoteProposer
{
    public Task<string?> Propose(CapturedTurn turn, string prompt, CancellationToken cancellationToken) => throw error;
}
