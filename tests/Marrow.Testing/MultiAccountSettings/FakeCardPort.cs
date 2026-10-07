using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Testing.MultiAccountSettings;

/// <summary>The card seam for tests: a test raises the engine's own events, the fake keeps the latest per thread and records answers, and, like the machine, refuses an answer when no card is waiting (MA-10 fixtures).</summary>
public sealed class FakeCardPort : ICardPort
{
    private readonly Dictionary<Guid, HandshakeEvent> states = [];

    public event EventHandler<HandshakeEvent>? Changed;

    public List<CardAnswer> Answers { get; } = [];

    /// <summary>When set, an answer waits for it (a test looks at the card meanwhile).</summary>
    public Task? AnswerHold { get; set; }

    public HandshakeEvent? State(Guid thread) => states.GetValueOrDefault(thread);

    public void Raise(HandshakeEvent e)
    {
        states[e.ThreadId] = e;
        Changed?.Invoke(this, e);
    }

    public async Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct = default)
    {
        if (State(thread) is not { Request: not null } waiting) throw new InvalidOperationException("No card is waiting for an answer.");
        if (AnswerHold is { } hold) await hold;
        Answers.Add(new CardAnswer(thread, [.. choices], scope));
        Raise(waiting with { Kind = HandshakeEventKind.PhaseChanged, Request = null });
    }
}
