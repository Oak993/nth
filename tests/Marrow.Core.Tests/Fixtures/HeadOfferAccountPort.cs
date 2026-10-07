using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Core.Tests.Port.MultiAccount;

internal sealed class HeadOfferAccountPort(ProviderKind service, params ThreadAccount[] rows) : IThreadAccountPort
{
    public event Action? Changed;
    public ProviderKind Service => service;
    public IReadOnlyList<ThreadAccount> Accounts { get; private set; } = rows;
    public Guid ActiveAccountId { get; set; } = rows[0].Id;
    public Guid? PinnedAccountId => null;
    public IReadOnlyList<Choice> ThreadChoices => [];
    public void Replace(params ThreadAccount[] next) { Accounts = next; Changed?.Invoke(); }
    public Task<PinResult> PinAsync(Guid id, Bring? bring = null, CancellationToken ct = default) => throw new NotSupportedException();
    public void BackToOrder() => throw new NotSupportedException();
    public void UseSettings() => throw new NotSupportedException();
}
