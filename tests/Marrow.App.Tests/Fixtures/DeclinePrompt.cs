using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Tests.Fixtures;

internal sealed class DeclinePrompt : IConversationTransferPrompt
{
    internal List<ConversationTransferQuestion> Questions { get; } = [];
    public Task<ConversationTransferAnswer> ConfirmAsync(ConversationTransferQuestion question, CancellationToken ct)
    { Questions.Add(question); return Task.FromResult(new ConversationTransferAnswer(false)); }
}
