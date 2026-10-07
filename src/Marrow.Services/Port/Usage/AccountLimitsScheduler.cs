using System.Globalization;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Usage;

// PORT-ONLY: OD-0114 / ED-406; account-scoped polling, independent of views and provider-keyed Registry.
// Compose the reader with the selected account's environment/key. Calls never borrow another account's snapshot.
public sealed class AccountLimitsScheduler : IDisposable
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan NearHandshakeInterval = TimeSpan.FromMinutes(1);

    private sealed class PollState(UsageAccountKey account)
    {
        public UsageAccountKey Account { get; } = account;
        public CancellationTokenSource Cancellation { get; set; } = new();
        public ITimer? Timer { get; set; }
        public bool SignedIn { get; set; }
        public bool InUse { get; set; }
        public bool Requested { get; set; }
        public long Generation { get; set; }
        public int Failures { get; set; }
        public double? HandshakePercent { get; set; }
        public DateTimeOffset? Next { get; set; }
        public DateTimeOffset? NotBefore { get; set; }
        public AccountPlanLimitsSnapshot? Snapshot { get; set; }
        public Task? Flight { get; set; }
    }

    private readonly Lock gate = new();
    private readonly TimeProvider clock;
    private readonly Func<UsageAccountKey, CancellationToken, Task<PlanLimits?>> read;
    private readonly Func<TimeSpan> jitter;
    private readonly SemaphoreSlim concurrency;
    private readonly Dictionary<UsageAccountKey, PollState> accounts = [];
    private readonly Dictionary<UsageAccountKey, DateTimeOffset> lastAttempts = [];
    private readonly Dictionary<UsageAccountKey, DateTimeOffset> serviceEmbargoes = [];
    private bool disposed;

    public AccountLimitsScheduler(TimeProvider clock, Func<UsageAccountKey, CancellationToken, Task<PlanLimits?>> read,
        int maxConcurrency = 2, Func<TimeSpan>? jitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        this.clock = clock;
        this.read = read;
        this.jitter = jitter ?? (() => TimeSpan.FromSeconds(Random.Shared.Next(0, 11)));
        concurrency = new(maxConcurrency);
    }

    public event EventHandler<AccountPlanLimitsSnapshot>? SnapshotChanged;

    public AccountPlanLimitsSnapshot? Snapshot(UsageAccountKey account)
    {
        lock (gate) return accounts.TryGetValue(account, out var state) ? state.Snapshot : null;
    }

    public DateTimeOffset? NextReadAt(UsageAccountKey account)
    {
        lock (gate) return accounts.TryGetValue(account, out var state) ? state.Next : null;
    }

    public Task SignedInAsync(UsageAccountKey account)
    {
        lock (gate)
        {
            var state = State(account);
            state.SignedIn = true;
            Request(state);
        }
        return RefreshAccountAsync(account);
    }

    public void SetInUse(UsageAccountKey account, bool inUse)
    {
        lock (gate)
        {
            var state = State(account);
            if (state.InUse == inUse) return;
            state.InUse = inUse;
            if (inUse) state.Next = Allowed(state, clock.GetUtcNow());
            else if (!state.Requested) state.Next = null;
            Arm(state);
        }
    }

    public void SetHandshakePercent(UsageAccountKey account, double? percent)
    {
        if (percent is { } value && (!double.IsFinite(value) || value < 80 || value > 99))
            throw new ArgumentOutOfRangeException(nameof(percent));
        lock (gate)
        {
            var state = State(account);
            if (state.HandshakePercent == percent) return;
            state.HandshakePercent = percent;
            if (state.InUse) state.Next = Allowed(state, clock.GetUtcNow() + Interval(state));
            Arm(state);
        }
    }

    public Task LimitReachedAsync(UsageAccountKey account) => RequestRefreshAsync(account);

    public Task RequestRefreshAsync(UsageAccountKey account)
    {
        lock (gate) Request(State(account));
        return RefreshAccountAsync(account);
    }

    public Task PlanChangedAsync(UsageAccountKey account)
    {
        lock (gate)
        {
            var state = State(account);
            state.Generation++;
            state.Cancellation.Cancel();
            state.Cancellation.Dispose();
            state.Cancellation = new();
            state.Snapshot = null;
            state.Failures = 0;
            Request(state);
        }
        return RefreshAccountAsync(account);
    }

    public void SignedOut(UsageAccountKey account)
    {
        lock (gate)
        {
            if (!accounts.Remove(account, out var state)) return;
            state.Generation++;
            state.Cancellation.Cancel();
            state.Cancellation.Dispose();
            state.Timer?.Dispose();
        }
    }

    public void Pause(UsageAccountKey account)
    {
        lock (gate)
        {
            if (!accounts.TryGetValue(account, out var state)) return;
            state.SignedIn = false;
            state.InUse = false;
            state.Requested = false;
            state.Next = null;
            state.Generation++;
            state.Cancellation.Cancel();
            state.Cancellation.Dispose();
            state.Cancellation = new();
            Arm(state);
        }
    }

    private Task RefreshAccountAsync(UsageAccountKey account)
    {
        lock (gate)
        {
            if (disposed || !accounts.TryGetValue(account, out var state)) return Task.CompletedTask;
            if (state.Flight is { } flight) return flight;
            return state.SignedIn && (state.Requested || state.InUse) && state.Next <= clock.GetUtcNow() ? Start(state) : Task.CompletedTask;
        }
    }

    // Also a deterministic pump: joining in-flight work lets tests await the timer's asynchronous completion.
    public Task RefreshDueAsync()
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            var pending = new List<Task>();
            foreach (var state in accounts.Values)
            {
                if (state.Flight is { } flight) pending.Add(flight);
                else if (state.SignedIn && (state.Requested || state.InUse) && state.Next <= clock.GetUtcNow())
                    pending.Add(Start(state));
            }
            return Task.WhenAll(pending);
        }
    }

    private PollState State(UsageAccountKey account)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (accounts.TryGetValue(account, out var state)) return state;
        state = new(account);
        state.Timer = clock.CreateTimer(_ => { _ = RefreshAccountAsync(account); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        accounts.Add(account, state);
        return state;
    }

    private void Request(PollState state)
    {
        state.Requested = true;
        state.Next = Allowed(state, clock.GetUtcNow());
        Arm(state);
    }

    private DateTimeOffset Allowed(PollState state, DateTimeOffset requested)
    {
        if (lastAttempts.TryGetValue(state.Account, out var last) && requested < last + MinimumInterval) requested = last + MinimumInterval;
        if (serviceEmbargoes.TryGetValue(state.Account, out var embargo) && requested < embargo) requested = embargo;
        if (state.NotBefore is { } backoff && requested < backoff) requested = backoff;
        return requested;
    }

    private TimeSpan Interval(PollState state)
    {
        var threshold = state.HandshakePercent ?? UsageRateEstimator.DefaultHandshakePercent(state.Snapshot?.Limits?.PlanName);
        return state.Snapshot?.Limits?.Windows.Any(window => double.IsFinite(window.Percent) && window.Percent >= threshold - 5) == true
            ? NearHandshakeInterval : ActiveInterval;
    }

    private void Arm(PollState state)
    {
        var due = state.SignedIn && state.Flight is null && (state.InUse || state.Requested) && state.Next is { } next
            ? next - clock.GetUtcNow() : Timeout.InfiniteTimeSpan;
        if (due != Timeout.InfiniteTimeSpan && due < TimeSpan.Zero) due = TimeSpan.Zero;
        state.Timer?.Change(due, Timeout.InfiniteTimeSpan);
    }

    private Task Start(PollState state)
    {
        var allowed = Allowed(state, clock.GetUtcNow());
        if (allowed > clock.GetUtcNow())
        {
            state.Next = allowed;
            Arm(state);
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Flight = completion.Task;
        state.Requested = false;
        Arm(state);
        var generation = state.Generation;
        var cancellation = state.Cancellation.Token;
        _ = Poll(state, generation, cancellation, completion);
        return completion.Task;
    }

    private async Task Poll(PollState state, long generation, CancellationToken cancellation, TaskCompletionSource completion)
    {
        var acquired = false;
        PlanLimits? limits = null;
        DateTimeOffset? readStartedAt = null;
        Exception? failure = null;
        AccountPlanLimitsSnapshot? published = null;
        try
        {
            while (true)
            {
                await concurrency.WaitAsync(cancellation).ConfigureAwait(false);
                acquired = true;
                cancellation.ThrowIfCancellationRequested();
                DateTimeOffset allowed;
                lock (gate)
                {
                    allowed = Allowed(state, clock.GetUtcNow());
                    if (allowed <= clock.GetUtcNow()) lastAttempts[state.Account] = clock.GetUtcNow();
                }
                if (allowed <= clock.GetUtcNow()) break;
                concurrency.Release();
                acquired = false;
                await Task.Delay(allowed - clock.GetUtcNow(), clock, cancellation).ConfigureAwait(false);
            }
            readStartedAt = clock.GetUtcNow();
            limits = await read(state.Account, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (acquired) concurrency.Release();
            lock (gate)
            {
                state.Flight = null;
                if (failure is LimitsPollException throttle)
                {
                    var embargo = clock.GetUtcNow() + throttle.RetryAfter;
                    if (!serviceEmbargoes.TryGetValue(state.Account, out var existing) || embargo > existing) serviceEmbargoes[state.Account] = embargo;
                    if (accounts.TryGetValue(state.Account, out var successor) && !ReferenceEquals(successor, state) && successor.Next is { } next)
                    {
                        successor.Next = Allowed(successor, next);
                        Arm(successor);
                    }
                }
                var fatal = failure is not null && !SwiftTry.IsThrownError(failure);
                if (!disposed && accounts.TryGetValue(state.Account, out var current) && ReferenceEquals(current, state))
                {
                    if (fatal)
                    {
                        state.Next = null;
                        state.Requested = false;
                    }
                    else if (state.Generation == generation && !cancellation.IsCancellationRequested)
                    {
                        // A failed/missing report replaces the old value; it must not masquerade as fresh limits.
                        state.Snapshot = new(state.Account, failure is null ? limits : null, clock.GetUtcNow()) { ReadStartedAt = readStartedAt ?? clock.GetUtcNow() };
                        published = state.Snapshot;
                        var shared = failure is null && state.Snapshot.LimitsShared;
                        state.Failures = shared ? 0 : Math.Min(8, state.Failures + 1);
                        var delay = shared ? Interval(state) : TimeSpan.FromMinutes(Math.Min(30, 3 * Math.Pow(2, state.Failures - 1)));
                        if (failure is LimitsPollException throttled && throttled.RetryAfter > delay) delay = throttled.RetryAfter;
                        var spread = jitter();
                        if (spread < TimeSpan.Zero || spread > TimeSpan.FromSeconds(10)) spread = TimeSpan.Zero;
                        state.NotBefore = shared ? null : clock.GetUtcNow() + delay;
                        state.Next = state.Requested ? Allowed(state, state.Next ?? clock.GetUtcNow()) :
                            state.InUse ? Allowed(state, clock.GetUtcNow() + delay + spread) : null;
                    }
                    else if (state.Requested) state.Next = Allowed(state, clock.GetUtcNow());
                    Arm(state);
                }
            }
            if (published is not null) SnapshotChanged?.Invoke(this, published);
            // The public poll task propagates port traps; they never become an unknown-quota snapshot.
            if (failure is not null && !SwiftTry.IsThrownError(failure)) completion.TrySetException(failure);
            else completion.TrySetResult();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var state in accounts.Values)
            {
                state.Cancellation.Cancel();
                state.Cancellation.Dispose();
                state.Timer?.Dispose();
            }
            accounts.Clear();
            // Flights release their slots when the reader actually exits, even if it ignores cancellation.
        }
    }
}

// Adapter exception for a service's throttling response; immediate triggers also honor its Retry-After.
public sealed class LimitsPollException(TimeSpan retryAfter) : Exception("Usage service asked polling to wait.")
{
    public TimeSpan RetryAfter { get; } = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;

    internal static void ThrowIfThrottled(HTTPURLResponse response)
    {
        if (response.StatusCode is not (429 or 503)) return;
        var delay = AccountLimitsScheduler.MinimumInterval;
        if (response.AllHeaderFields.TryGetValue("Retry-After", out var header))
        {
            if (double.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0)
                delay = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
            else if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
                delay = until - SwiftDate.Now;
        }
        throw new LimitsPollException(delay);
    }
}
