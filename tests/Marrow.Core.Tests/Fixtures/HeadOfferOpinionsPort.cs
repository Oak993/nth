using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Core.Tests.Port.MultiAccount;

internal sealed class HeadOfferOpinionsPort : IOpinionsPort
{
    public event Action<Guid>? Changed { add { } remove { } }
    public List<IReadOnlyList<Guid>> Sends { get; } = [];
    public Task<Guid> AskAsync(Guid thread, string message, IReadOnlyList<Guid> accounts, CancellationToken ct = default)
    { Sends.Add(accounts); return Task.FromResult(Guid.NewGuid()); }
    public IReadOnlyList<OpinionRound> Rounds(Guid thread) => [];
    public Task CombineAsync(Guid thread, Guid round, CancellationToken ct = default) => throw new NotSupportedException();
}
