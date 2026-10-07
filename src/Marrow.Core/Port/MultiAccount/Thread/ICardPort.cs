// PORT-ONLY: multi-account steps 3b-5, job 0. What the in-chat cards and the capsule's Handshake bind to. It mirrors the routing the app will have (MultiAccountRouting: Changed, State(thread), AnswerAsync(thread, choices, scope)); the engine's own
// HandshakeEvent, DecisionRequest, Choice and RememberScope are used as they are.
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.MultiAccount.Thread;

public interface ICardPort
{
    /// <summary>A phase change, a card to ask, or a note, for any thread.</summary>
    event EventHandler<HandshakeEvent>? Changed;

    /// <summary>The latest event of the thread, or null when it has none. A card waiting for an answer is a state whose Request is set.</summary>
    HandshakeEvent? State(Guid thread);

    /// <summary>The user's answer to the waiting card: the choices, and the one Remember line for all of them. Throws when no card is waiting.</summary>
    Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct = default);
}
