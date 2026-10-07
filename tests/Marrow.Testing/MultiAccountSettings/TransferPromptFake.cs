using Marrow.Core.Port.MultiAccount;

namespace Marrow.Testing.MultiAccountSettings;

public sealed class TransferPromptFake : IConversationTransferPrompt
{
    public List<ConversationTransferQuestion> Questions { get; } = [];
    public ConversationTransferAnswer Answer { get; set; } = new(false, false);
    public Task<ConversationTransferAnswer>? Pending { get; set; }
    public Task<ConversationTransferAnswer> ConfirmAsync(ConversationTransferQuestion question, CancellationToken ct)
    { Questions.Add(question); return Pending?.WaitAsync(ct) ?? Task.FromResult(Answer); }
}
