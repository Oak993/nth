using Marrow.Core.Port.MultiAccount;

namespace Marrow.Services.Tests.Port.MultiAccount;

internal sealed class HeadSelectionNeverPrompt : IConversationTransferPrompt
{
    public int Calls { get; private set; }
    public Task<ConversationTransferAnswer> ConfirmAsync(ConversationTransferQuestion question, CancellationToken ct)
    { Calls++; throw new InvalidOperationException("No transfer prompt is allowed."); }
}
