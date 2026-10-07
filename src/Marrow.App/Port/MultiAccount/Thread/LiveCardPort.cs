using Marrow.App.Runtime;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

// T25 W: the cards and Handshake presentation share the engine's event stream and answer boundary.
internal sealed class LiveCardPort(MultiAccountRouting routing) : ICardPort
{
    public event EventHandler<HandshakeEvent>? Changed
    {
        add => routing.Changed += value;
        remove => routing.Changed -= value;
    }
    public HandshakeEvent? State(Guid thread) => routing.State(thread);
    public Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct = default)
        => routing.AnswerAsync(thread, choices, scope, ct);
}
