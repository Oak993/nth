// PORT-ONLY: MA-3b-E1 / OD-0115. App wiring supplies live account state and presentation; the engines own per-message head work.
using Marrow.Core.Models;

namespace Marrow.Services.Port.MultiAccount;

public sealed record ConversationHeadAccount(Guid Id, ProviderKind Service, string Name, bool Enabled, bool HasRoom)
{
    /// <summary>The live API key's configured monthly USD cap; null means no cap. The app must supply it with HasRoom.</summary>
    public double? MonthlyCapUsd { get; init; }
}

public enum ConversationHeadPurpose { Check, Answer, Opinion, Combine }

public sealed record ConversationHeadRequest(Guid Thread, ConversationHeadAccount Account, ConversationHeadPurpose Purpose,
    string Context, string Message, string? Answer = null);

/// <summary>All accounts are connected identities (including Local). HasRoom includes key caps, reported limits and out states.</summary>
public interface IConversationHeadHost
{
    event Action<Guid>? AccountsChanged;
    Guid ActiveAccount(Guid thread);
    /// <summary>Monotonic context/ownership epoch. Advance for transcript edits/resume, model/effort/endpoint changes and pin/pill/source changes;
    /// raise AccountsChanged too. Capacity changes can use the account rows without changing this epoch.</summary>
    long Revision(Guid thread);
    IReadOnlyList<ConversationHeadAccount> Accounts(Guid thread);
    Task<string> ContextAsync(Guid thread, CancellationToken ct);
    /// <summary>OD-0120: required before context is given to any different service. A denied request sends nothing.</summary>
    Task<bool> ConfirmTransferAsync(Guid thread, ConversationHeadAccount source, IReadOnlyList<ConversationHeadAccount> targets, CancellationToken ct);
    /// <summary>A private, text-only head. Reports the complete text so far; rechecks permitted at the provider dispatch boundary.</summary>
    Task<string> RunAsync(ConversationHeadRequest request, Action<string> text, Func<bool> permitted, CancellationToken ct);
    /// <summary>Atomically appends the answer with its byline to the thread, without changing account, pin or pill step.</summary>
    Task PostCombinedAsync(Guid thread, Guid round, string text, string byline, CancellationToken ct);
}
