using Marrow.Services.Port.MultiAccount;

namespace Marrow.App.Tests.Fixtures;

// Only context is fake. Run reaches the actual account-head host and the test's scripted session factory.
internal sealed class C7ConversationContext(IConversationHeadHost inner, Func<CancellationToken, Task<string>> context) : IConversationHeadHost
{
    public event Action<Guid>? AccountsChanged { add => inner.AccountsChanged += value; remove => inner.AccountsChanged -= value; }
    public Guid ActiveAccount(Guid thread) => inner.ActiveAccount(thread);
    public long Revision(Guid thread) => inner.Revision(thread);
    public IReadOnlyList<ConversationHeadAccount> Accounts(Guid thread) => inner.Accounts(thread);
    public Task<string> ContextAsync(Guid thread, CancellationToken ct) => context(ct);
    public Task<bool> ConfirmTransferAsync(Guid thread, ConversationHeadAccount source, IReadOnlyList<ConversationHeadAccount> targets, CancellationToken ct)
        => inner.ConfirmTransferAsync(thread, source, targets, ct);
    public Task<string> RunAsync(ConversationHeadRequest request, Action<string> text, Func<bool> permitted, CancellationToken ct)
        => inner.RunAsync(request, text, permitted, ct);
    public Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct)
        => inner.PostCombinedAsync(thread, round, text, byline, ct);
}
