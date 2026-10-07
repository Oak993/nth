using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Tests.Fixtures;

internal sealed class FollowUpPort : ICardPort
{
    internal HandshakeEvent Current = null!;
    public event EventHandler<HandshakeEvent>? Changed;
    public HandshakeEvent? State(Guid thread) => Current;
    public Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct = default)
    {
        Assert.Equal(DecisionValues.Ask, Assert.Single(choices).Value);
        Current = Current with { Request = Current.Request! with { FirstTime = false } };
        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }
}
