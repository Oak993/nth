using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Tests.Fixtures;

// Decorates only context/provider dispatch. Identity, revision, consent and posting remain the real app boundaries.
internal sealed class OfflineOpinionHost(IConversationHeadHost inner) : IConversationHeadHost
{
    internal Guid? FailedAccount { get; set; }
    internal List<ConversationHeadRequest> Requests { get; } = [];
    public event Action<Guid>? AccountsChanged { add => inner.AccountsChanged += value; remove => inner.AccountsChanged -= value; }
    public Guid ActiveAccount(Guid thread) => inner.ActiveAccount(thread);
    public long Revision(Guid thread) => inner.Revision(thread);
    public IReadOnlyList<ConversationHeadAccount> Accounts(Guid thread) => inner.Accounts(thread);
    public Task<string> ContextAsync(Guid thread, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult("Recorded offline conversation."); }
    public Task<bool> ConfirmTransferAsync(Guid thread, ConversationHeadAccount source, IReadOnlyList<ConversationHeadAccount> targets, CancellationToken ct)
        => inner.ConfirmTransferAsync(thread, source, targets, ct);
    public Task<string> RunAsync(ConversationHeadRequest request, Action<string> text, Func<bool> permitted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!permitted()) throw new InvalidOperationException("The real app revoked this head.");
        Requests.Add(request);
        if (request.Account.Id == FailedAccount) throw new InvalidOperationException("Offline head failed.");
        var answer = request.Purpose == ConversationHeadPurpose.Combine ? "Combined offline answer." : "Offline answer from " + request.Account.Name;
        text(answer); return Task.FromResult(answer);
    }
    public Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct)
        => inner.PostCombinedAsync(thread, round, text, byline, ct);
}
