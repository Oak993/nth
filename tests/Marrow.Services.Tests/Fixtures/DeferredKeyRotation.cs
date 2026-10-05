using Marrow.Core.Accounts.Keys;

namespace Marrow.Services.Tests.Fixtures;

public sealed class DeferredKeyRotation(IApiKeyRotation inner) : IAsyncApiKeyRotation
{
    public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int CurrentCalls { get; private set; }
    public int FailureCalls { get; private set; }
    public CancellationToken AnswerCancellation { get; private set; }
    public string? StopReason => inner.StopReason;
    public string? ContinuedOn => inner.ContinuedOn;
    public void AllowMoveOnce() => inner.AllowMoveOnce();
    public KeyChoice? Current() => inner.Current();
    public KeyChoice? OnFailure(int status, TimeSpan? retryAfter) => inner.OnFailure(status, retryAfter);
    public void RecordSpend(double usd) => inner.RecordSpend(usd);
    public Task<KeyChoice?> CurrentAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CurrentCalls++;
        return Task.FromResult(inner.Current());
    }
    public async Task<KeyChoice?> OnFailureAsync(int status, TimeSpan? retryAfter, CancellationToken ct)
    {
        var next = inner.OnFailure(status, retryAfter);
        if (next is not null || status is not (402 or 429)) return next;
        FailureCalls++;
        AnswerCancellation = ct;
        if (!await Answer.Task.WaitAsync(ct)) return null;
        inner.AllowMoveOnce();
        return ((IApiKeyContinuation)inner).ContinueRejectedRequest();
    }
}
