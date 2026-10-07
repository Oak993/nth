using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>"Ask others" for tests: records what was asked of whom, and a test streams each account's answer by hand (<see cref="Stream"/>, <see cref="Finish"/>); no account is asked (MA-10 fixtures).</summary>
public sealed class FakeOpinionsPort : IOpinionsPort
{
    private readonly List<OpinionRound> rounds = [];

    public event Action<Guid>? Changed;

    public List<OpinionAsk> Asked { get; } = [];
    public List<Guid> Combined { get; } = [];

    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default)
    {
        if (accounts.Count == 0) throw new ArgumentException("Ask at least one account.", nameof(accounts));
        Asked.Add(new OpinionAsk(thread, message, [.. accounts]));
        var round = new OpinionRound(Guid.NewGuid(), thread, message, [.. accounts.Select(id => new OpinionAnswer(id, "", false))], false);
        rounds.Add(round);
        Changed?.Invoke(thread);
        return Task.FromResult(round.Id);
    }

    public IReadOnlyList<OpinionRound> Rounds(Guid thread) => [.. rounds.Where(r => r.Thread == thread)];

    public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default)
    {
        var at = rounds.FindIndex(r => r.Id == round && r.Thread == thread);
        if (at < 0 || rounds[at].Combined) throw new InvalidOperationException("There is no round to combine, or it was combined already.");
        rounds[at] = rounds[at] with { Combined = true };
        Combined.Add(round);
        Changed?.Invoke(thread);
        return Task.CompletedTask;
    }

    /// <summary>The account's answer grows by <paramref name="text"/>.</summary>
    public void Stream(Guid round, Guid account, string text) => Change(round, account, a => a with { Text = a.Text + text });

    /// <summary>The account's answer is complete.</summary>
    public void Finish(Guid round, Guid account) => Change(round, account, a => a with { Done = true });

    private void Change(Guid round, Guid account, Func<OpinionAnswer, OpinionAnswer> change)
    {
        var at = rounds.FindIndex(r => r.Id == round);
        rounds[at] = rounds[at] with { Answers = [.. rounds[at].Answers.Select(a => a.AccountId == account ? change(a) : a)] };
        Changed?.Invoke(rounds[at].Thread);
    }
}
