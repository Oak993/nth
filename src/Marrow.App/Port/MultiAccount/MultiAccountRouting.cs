using Marrow.Core.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

// PORT-ONLY: OD-0114/0116. One app poller and one serialized, cancellable Handshake per live thread.
public sealed class MultiAccountRouting
{
    public static Ambient<Func<AppModel, UsageAccountKey, CancellationToken, Task<PlanLimits?>>> LimitsReader { get; } = new(ReadLimitsAsync);
    private readonly AppModel app;
    private readonly TimeProvider clock;
    private readonly AccountLimitsScheduler scheduler;
    private readonly HandshakeLimits limits;
    private readonly UsageRateEstimator rates = new();
    private readonly Dictionary<Guid, ThreadRuntime> runtimes = [];
    private readonly Dictionary<Guid, ThreadAccountRouting> routes = [];
    private readonly Dictionary<Guid, (UsageAccountKey Key, string? Plan)> connected = [];
    private ITimer? timer;
    private Task tail = Task.CompletedTask;
    private readonly Dictionary<Task, Task> polls = [];
    private readonly Dictionary<Task, Task> operations = [];

    public MultiAccountRouting(AppModel app)
    {
        MainActor.AssumeIsolated();
        this.app = app;
        clock = SwiftClock.Provider.Current;
        var read = LimitsReader.Current;
        scheduler = new(clock, (key, ct) => read(app, key, ct));
        limits = new(app.Settings.Accounts, scheduler, rates, clock);
        app.Settings.Accounts.Changed += SettingsChanged;
        app.Settings.AccountDecisions.Changed += SettingsChanged;
        app.ApiKeys.Changed += (_, value) => Changed?.Invoke(this, value);
    }

    public event EventHandler<HandshakeEvent>? Changed;
    public HandshakeEvent? State(Guid thread) => app.ApiKeys.State(thread) ?? routes.GetValueOrDefault(thread)?.State;
    public AccountPlanLimitsSnapshot? Snapshot(Guid account) => limits.Snapshot(account);
    internal bool CanUseAccount(Guid account) => limits.Usage(account.ToString("D"))?.IsExhaustedAt(clock.GetUtcNow()) != true;

    private bool Eligible(ThreadRuntime runtime) => !app.Settings.Accounts.SaveBlocked && app.Thread(runtime.ThreadID) is { } thread
        && (thread.PillRelay is not null || app.Settings.Accounts.Accounts.Count(app.IsAccountConnected) >= 2);

    public void Attach(ThreadRuntime runtime)
    {
        MainActor.AssumeIsolated();
        runtimes[runtime.ThreadID] = runtime;
        if (!Eligible(runtime) || !runtime.TryHandshakeAccountId(out _)) return;
        EnsureRoute(runtime);
        Queue(async () => { await RefreshAsync(); StartTick(runtime.ThreadID); });
        timer ??= clock.CreateTimer(_ => MainActor.Post(() => Queue(BeatAsync)), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    // Ownership transfers synchronously, before a TurnCompleted can run the old reset-send path.
    public bool NoteLimit(ThreadRuntime runtime, UsageAccountKey? observed, DateTimeOffset? reset)
    {
        MainActor.AssumeIsolated();
        if (!Eligible(runtime) || !runtime.TryHandshakeAccountId(out _)) return false;
        Attach(runtime);
        var account = ResolveAccount(observed, runtime);
        if (account is null) return false;
        var route = EnsureRoute(runtime);
        app.AutoContinue.TakeLimitOwnership(runtime.ThreadID);
        limits.NoteLimit(account.Value, reset);
        route.LimitedEpoch = runtime.HandshakeStamp.Epoch;
        route.LimitedTurn = runtime.Turns.LastOrDefault()?.Id;
        route.LimitedAccount = account;
        route.LimitedSessionId = app.Thread(runtime.ThreadID)?.ProviderSessionID;
        TrackPoll(scheduler.LimitReachedAsync(limits.Key(account.Value)));
        // LimitHit cancels the machine's own linking operation immediately, before waiting for its gate.
        TrackOperation(route.Machine.LimitHitAsync(account.Value.ToString("D"), reset, route.Cancellation.Token));
        return true;
    }

    private Guid? ResolveAccount(UsageAccountKey? observed, ThreadRuntime runtime)
    {
        if (observed is not { } key) return runtime.TryHandshakeAccountId(out var active) ? active : null;
        var row = key.IsDefault
            ? app.Settings.Accounts.ForService(key.Provider).FirstOrDefault(value => value.UsesLegacyCredentials)
            : Guid.TryParse(key.AccountId, out var id) ? app.Settings.Accounts.Accounts.FirstOrDefault(value => value.Id == id && value.Service == key.Provider) : null;
        return row?.Id;
    }

    private ThreadAccountRouting EnsureRoute(ThreadRuntime runtime)
    {
        if (routes.TryGetValue(runtime.ThreadID, out var existing)) return existing;
        var summaries = new HandshakeSummaryAdapter((id, ct) => runtime.BuildHandshakeSourceAsync(ct));
        var host = new ThreadAccountHost(app, runtime, app.Settings.Pills, summaries);
        HandshakeOptions Options() => new(app.Settings.AccountHandshakeEnabled,
            app.Settings.Accounts.GeneralHandshakePoint.Percentage is { } percent
                ? HandshakePoint.At(percent) : HandshakePoint.Smart);
        var machine = new HandshakeMachine(runtime.ThreadID, new HandshakeAccountStore(app.Settings.Accounts, app.IsAccountConnected),
            limits, summaries, new(new PillDecisionStore(app.Settings.AccountDecisions, runtime.ThreadID, () => host.Pill)), host, clock, Options(), Options);
        var route = new ThreadAccountRouting(runtime, machine);
        machine.Changed += (_, value) =>
        {
            if (!routes.TryGetValue(runtime.ThreadID, out var current) || !ReferenceEquals(current, route)) return;
            route.State = value;
            if (value.Kind == HandshakeEventKind.PhaseChanged)
            {
                route.ResetTimer?.Dispose();
                route.ResetTimer = null;
                if (value.To == HandshakePhase.Waiting && value.ResumesAt is { } reset)
                    route.ResetTimer = clock.CreateTimer(_ => MainActor.Post(() => StartTick(runtime.ThreadID)), null,
                        reset > clock.GetUtcNow() ? reset - clock.GetUtcNow() : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                if (value.From == HandshakePhase.Waiting && value.To == HandshakePhase.Idle && value.Reason == HandshakeReason.ResetReached)
                    route.ResumeWaiting = true;
            }
            Changed?.Invoke(this, value);
        };
        routes.Add(runtime.ThreadID, route);
        timer ??= clock.CreateTimer(_ => MainActor.Post(() => Queue(BeatAsync)), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return route;
    }

    public async Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope)
    {
        MainActor.AssumeIsolated();
        if (app.ApiKeys.TryAnswer(thread, choices, scope)) return;
        var route = routes.GetValueOrDefault(thread) ?? throw new InvalidOperationException("No account card is waiting for this conversation.");
        // Preserve this caller's rejection without making it the predecessor of other cards or cleanup.
        var enableHandshake = route.State?.Request is { FirstTime: true } request && request.Kinds.Contains(DecisionKind.OnLimit)
            && choices.Any(choice => choice.Kind == DecisionKind.OnLimit && choice.Value == DecisionValues.Switch);
        await route.Machine.AnswerAsync(choices, scope, route.Cancellation.Token,
            () => { if (enableHandshake) app.Settings.AccountHandshakeEnabled = true; });
    }

    public void AtSafePoint(Guid thread)
    {
        if (routes.ContainsKey(thread)) StartTick(thread);
    }

    public void Detach(Guid thread)
    {
        MainActor.AssumeIsolated();
        app.ApiKeys.Cancel(thread);
        runtimes.Remove(thread);
        rates.Forget(thread);
        if (routes.Remove(thread, out var route))
        {
            route.Cancellation.Cancel();
            route.ResetTimer?.Dispose();
            TrackOperation(DisposeRouteAsync(route));
        }
        if (runtimes.Count == 0)
        {
            timer?.Dispose();
            timer = null;
            foreach (var row in connected.Values) scheduler.SignedOut(row.Key);
            connected.Clear();
        }
    }

    private void SettingsChanged() => MainActor.Post(() => Queue(BeatAsync));

    private async Task BeatAsync()
    {
        foreach (var runtime in runtimes.Values.ToArray())
        {
            if (!runtime.TryHandshakeAccountId(out _)) { Detach(runtime.ThreadID); continue; }
            if (Eligible(runtime)) EnsureRoute(runtime);
        }
        await RefreshAsync();
        foreach (var id in routes.Keys.ToArray()) StartTick(id);
    }

    private async Task RefreshAsync()
    {
        if (routes.Count == 0) return;
        var available = app.Settings.Accounts.Accounts.Where(app.IsAccountConnected).ToArray();
        var active = routes.Values.Select(route => ResolveAccount(route.Runtime.UsageAccount, route.Runtime)).Where(id => id is not null).ToHashSet();
        foreach (var id in connected.Keys.Except(available.Select(row => row.Id)).ToArray())
        {
            // Metadata may already be removed, so use the captured service/key instead of looking it up again.
            scheduler.SignedOut(connected[id].Key);
            connected.Remove(id);
        }
        foreach (var account in available)
        {
            var key = limits.Key(account.Id);
            if (!connected.TryGetValue(account.Id, out var plan))
            {
                connected[account.Id] = (key, account.Plan);
                TrackPoll(scheduler.SignedInAsync(key));
            }
            else if (plan.Plan != account.Plan)
            {
                connected[account.Id] = (key, account.Plan);
                TrackPoll(scheduler.PlanChangedAsync(key));
            }
            scheduler.SetInUse(key, account.Enabled && active.Contains(account.Id));
            scheduler.SetHandshakePercent(key, app.Settings.Accounts.EffectiveHandshakePoint(account.Id).Percentage);
        }
        TrackPoll(scheduler.RefreshDueAsync());
        await Task.CompletedTask;
    }

    private async Task TickAsync(Guid thread)
    {
        if (app.ApiKeys.AwaitingChoice(thread)) return;
        if (!routes.TryGetValue(thread, out var route)) return;
        if (!route.Runtime.TryHandshakeAccountId(out var active)) { Detach(thread); return; }
        if (!Eligible(route.Runtime) && route.Machine.Phase == HandshakePhase.Idle && !route.ResumeWaiting) return;
        limits.Observe(thread, active);
        await route.Machine.TickAsync(route.Cancellation.Token);
        if (route.ResumeWaiting && route.Runtime.IsAccountSwitchSafe && route.LimitedAccount is { } account && route.LimitedTurn is { } turn)
        {
            route.ResumeWaiting = false;
            await route.Runtime.TryResumeAfterAccountWaitAsync(account, route.LimitedEpoch, turn, route.LimitedSessionId, route.Cancellation.Token);
        }
    }

    private void TrackPoll(Task flight)
    {
        if (flight.IsCompleted) { flight.GetAwaiter().GetResult(); return; }
        if (polls.ContainsKey(flight)) return;
        var observer = MainActor.SpawnThrowing(async () =>
        {
            try { await flight; }
            finally { polls.Remove(flight); }
            foreach (var id in routes.Keys.ToArray()) StartTick(id);
            return true;
        }).Value;
        polls.Add(flight, observer);
    }

    private Task Queue(Func<Task> work)
    {
        var previous = tail;
        tail = MainActor.SpawnThrowing(async () =>
        {
            await previous;
            try { await work(); }
            catch (OperationCanceledException) { }
            return true;
        }).Value;
        return tail;
    }

    private void StartTick(Guid thread) => TrackOperation(TickAsync(thread));

    private void TrackOperation(Task work)
    {
        if (work.IsCompleted) { work.GetAwaiter().GetResult(); return; }
        if (operations.ContainsKey(work)) return;
        var observer = MainActor.SpawnThrowing(async () =>
        {
            try { await work; }
            catch (OperationCanceledException) { }
            finally { operations.Remove(work); }
            return true;
        }).Value;
        operations.Add(work, observer);
    }

    private static async Task DisposeRouteAsync(ThreadAccountRouting route)
    {
        await route.Machine.DisposeAsync();
        route.Cancellation.Dispose();
    }

    public async Task DrainAsync()
    {
        Task observed;
        do
        {
            observed = tail;
            await observed;
            if (polls.Count != 0) await Task.WhenAll(polls.Values.ToArray());
            if (operations.Count != 0) await Task.WhenAll(operations.Values.ToArray());
        } while (!ReferenceEquals(observed, tail) || polls.Count != 0 || operations.Count != 0);
    }

    private static async Task<PlanLimits?> ReadLimitsAsync(AppModel app, UsageAccountKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var read = await MainActor.Run<Func<Task<PlanLimits?>>?>(() =>
        {
            var account = key.IsDefault ? app.Settings.Accounts.ForService(key.Provider).FirstOrDefault(row => row.UsesLegacyCredentials)
                : Guid.TryParse(key.AccountId, out var id) ? app.Settings.Accounts.Accounts.FirstOrDefault(row => row.Id == id && row.Service == key.Provider) : null;
            if (account is not { Enabled: true } || !app.IsAccountConnected(account)) return null;
            if (key.Provider.IsAPIKeyBased)
            {
                var apiKey = app.Providers.ApiKey(account.Id);
                return () => PlanLimitsReader.Read(key, apiKey);
            }
            if (app.Providers.Executable(key.Provider) is not { } executable) return null;
            var environment = app.Providers.Environment(account.Id);
            return () => PlanLimitsReader.Read(key, executable, environment);
        });
        // The scheduler keeps its concurrency slot until the actual provider read exits, even after sign-out.
        ct.ThrowIfCancellationRequested();
        return read is null ? null : await read();
    }
}
