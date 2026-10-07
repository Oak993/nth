using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

// Only the composer sends through this adapter. Timeline rows retain the same app-owned real engine.
internal sealed class LiveOpinionSendPort(OpinionsEngine engine,
    Func<Guid, string, IReadOnlyList<Guid>, CancellationToken, Task<Guid>> send) : IOpinionsPort
{
    public event Action<Guid>? Changed { add => engine.Changed += value; remove => engine.Changed -= value; }
    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default)
        => send(thread, message, accounts, ct);
    public IReadOnlyList<OpinionRound> Rounds(Guid thread) => engine.Rounds(thread);
    public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default) => engine.CombineAsync(thread, round, ct);
}
