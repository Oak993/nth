using Marrow.Services.Port.MultiAccount;

namespace Marrow.Services.Tests.Fixtures;

public sealed record ConversationHeadFlight(ConversationHeadRequest Request, Action<string> Text, Func<bool> Permitted, CancellationToken Token)
{
    public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Finish(string text) { Text(text); Result.TrySetResult(text); }
}
