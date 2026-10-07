using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Port;

namespace Marrow.App.Runtime;

// PORT-ONLY OD-0116: retain the refused request while the shared card awaits its answer.
public sealed class AppApiKeyRotation : IAsyncApiKeyRotation, IDisposable
{
    private readonly AppApiKeyRouting owner;
    private readonly ApiKeyPool pool;
    private readonly IApiKeyRotation inner;
    private readonly Action<KeyChoice> selected;
    private readonly Func<string?> pin;
    private readonly Func<bool> owns;
    private readonly bool canAsk;
    private ApiKeyEvent? last;
    private TaskCompletionSource<bool>? answer;
    private bool disposed;
    private bool answering;
    private Resolution? acceptedPolicy;
    private (string Source, Resolution Policy)? waitAnswer;
    public AppApiKeyRotation(AppApiKeyRouting owner, ApiKeyPool pool, Guid threadId, Guid accountId,
        Action<KeyChoice> selected, Func<string?> pin, Func<bool> owns, bool canAsk = true)
    {
        this.owner = owner; this.pool = pool; this.selected = selected; this.pin = pin; this.owns = owns; this.canAsk = canAsk;
        ThreadId = threadId;
        inner = pool.ForThread(threadId, accountId.ToString("D"));
        pool.Changed += Changed;
    }
    public Guid ThreadId { get; }
    public HandshakeEvent? State { get; private set; }
    public DecisionRequest? Request => answer is { Task.IsCompleted: false } ? State?.Request : null;
    public string? StopReason => inner.StopReason;
    public IApiKeyBudget? Budget => inner.Budget;
    public string? ContinuedOn => inner.ContinuedOn;
    private void Changed(object? sender, ApiKeyEvent value) { if (value.ThreadId == ThreadId) last = value; }
    private void Prepare()
    {
        MainActor.AssumeIsolated();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!owns()) throw new CancellationError();
        pool.Pin(ThreadId, pin());
        last = null;
        waitAnswer = null;
    }
    private KeyChoice? Select(KeyChoice? choice) { if (choice is not null) selected(choice); return choice; }
    public KeyChoice? Current() { Prepare(); return Select(inner.Current()); }
    public KeyChoice? OnFailure(int status, TimeSpan? retryAfter) { Prepare(); return Select(inner.OnFailure(status, retryAfter)); }
    public void AllowMoveOnce() => inner.AllowMoveOnce();
    public void RecordSpend(double usd) => inner.RecordSpend(usd);
    public async Task<KeyChoice?> CurrentAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var choice = Current();
        ct.ThrowIfCancellationRequested();
        return await ResolveAsync(choice, false, ct);
    }
    public async Task<KeyChoice?> OnFailureAsync(int status, TimeSpan? retryAfter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var choice = OnFailure(status, retryAfter);
        ct.ThrowIfCancellationRequested();
        return await ResolveAsync(choice, true, ct);
    }
    private async Task<KeyChoice?> ResolveAsync(KeyChoice? choice, bool rejected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (choice is not null || !canAsk || last is not { Kind: ApiKeyEventKind.MoveNeedsAnswer } request) return choice;
        ct.ThrowIfCancellationRequested();
        answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        acceptedPolicy = null;
        var pending = answer;
        State = owner.Card(ThreadId, request);
        owner.Publish(State);
        try
        {
            var allow = await pending.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (disposed || !owns()) throw new CancellationError();
            if (!allow || owner.Policy(ThreadId) != acceptedPolicy) return null;
            Prepare();
            if (pin() is not null) return null;
            inner.AllowMoveOnce();
            return Select(rejected ? ((IApiKeyContinuation)inner).ContinueRejectedRequest() : inner.Current());
        }
        finally
        {
            if (ReferenceEquals(answer, pending)) answer = null;
            if (State is { } state)
            {
                State = null;
                owner.Publish(state with { Kind = HandshakeEventKind.PhaseChanged, From = HandshakePhase.Asking, To = HandshakePhase.Idle, Request = null });
            }
        }
    }
    public DecisionRequest BeginAnswer()
    {
        if (answering || disposed || !owns() || pin() is not null || Request is not { } request)
            throw new InvalidOperationException("The API key card no longer belongs to this conversation.");
        answering = true;
        return request;
    }
    public void ValidatePending(DecisionRequest request)
    {
        if (disposed || !owns() || pin() is not null || !ReferenceEquals(request, Request)) throw new CancellationError();
    }
    public void EndAnswer() => answering = false;
    public void Answer(bool allow, Resolution policy, bool wait = false)
    {
        acceptedPolicy = policy;
        waitAnswer = wait && State?.OutgoingAccountId is { } source ? (source, policy) : null;
        answer!.TrySetResult(allow);
    }
    internal bool TakeWait(string source)
    {
        var choice = waitAnswer;
        waitAnswer = null;
        return choice is { } value && !disposed && owns() && pin() is null
            && value.Source == source && owner.Policy(ThreadId) == value.Policy;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        waitAnswer = null;
        answer?.TrySetCanceled();
        pool.Changed -= Changed;
        pool.Release(ThreadId);
        owner.Retired(this);
    }
}
