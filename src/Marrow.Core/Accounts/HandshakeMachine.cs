namespace Marrow.Core.Accounts;

public enum HandshakePhase
{
    Idle,
    Standby,
    Linking,
    InSync,
    Switch,

    /// <summary>PORT-ONLY phases: waiting for a reset, and waiting for the user's card answer.</summary>
    Waiting,
    Asking,
}

public enum HandshakeReason
{
    NearLimit,
    HardLimit,
    EarlierAccountBack,
    ResetReached,
}

public enum HandshakeEventKind
{
    PhaseChanged,
    DecisionRequested,
    InSyncReady,
    Degraded,
}

/// <summary>What the first-time or Ask-me card shows: one card for every unresolved kind, one Remember line.</summary>
public sealed record DecisionRequest(
    Guid ThreadId,
    IReadOnlyList<DecisionKind> Kinds,
    bool FirstTime,
    string OutgoingAccountId,
    string? IncomingAccountId,
    DateTimeOffset? ResetsAt);

/// <summary>What the UI binds to: phase changes, which accounts, why.</summary>
public sealed record HandshakeEvent(
    Guid ThreadId,
    HandshakeEventKind Kind,
    HandshakePhase From,
    HandshakePhase To,
    string? OutgoingAccountId,
    string? IncomingAccountId,
    HandshakeReason Reason,
    DateTimeOffset At,
    DateTimeOffset? ResumesAt = null,
    DecisionRequest? Request = null,
    string? Detail = null);

/// <summary>
/// One thread's Handshake (NOTES "Handshake", "Handshake timing"): Idle, Standby (the next account is started in the background), Linking (Marrow's summary, the outgoing account's briefing,
/// the incoming account's read-back), In sync (Marrow checks the read-back and fills gaps), Switch (only at a safe point, never mid-edit). A limit that hits without warning is the hard-stop fallback.
/// The caller beats <see cref="TickAsync"/> on the poller; nothing here reads a real clock, CLI, model or network.
/// </summary>
public sealed class HandshakeMachine(
    Guid threadId,
    IAccountStore store,
    IAccountLimits limits,
    IHandoffSummary summaries,
    DecisionPolicy decisions,
    IThreadAccountHost host,
    TimeProvider clock,
    HandshakeOptions options) : IAsyncDisposable
{
    private sealed record Pending(bool Earlier, string AccountId, DateTimeOffset? ResetsAt, SwitchTarget? Back, IReadOnlyList<DecisionKind> Kinds);

    private readonly NextAccountChooser chooser = new(store, limits, clock);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> declined = [];
    private readonly Dictionary<DecisionKind, string> answers = [];

    private int phaseValue;
    private HandshakePhase phase => (HandshakePhase)Volatile.Read(ref phaseValue);
    private CancellationTokenSource? linking;
    private string? outgoing;
    private SwitchTarget? target;
    private HandshakeReason reason;
    private Bring bring = Bring.MarrowSummary;
    private bool resume;
    private IStandbySession? standby;
    private HandoffSummary? summary;
    private string? briefing;
    private bool received;
    private bool synced;
    private string? failedFor;
    private Pending? pending;
    private DateTimeOffset? waitUntil;
    private bool waitForAny;

    public HandshakePhase Phase => phase;

    public event EventHandler<HandshakeEvent>? Changed;

    /// <summary>The poller's beat: starts the Handshake at the start point, switches in sync at a safe point, ends a wait. A beat that finds a Handshake already running does nothing.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!gate.Wait(0, CancellationToken.None)) return;
        try
        {
            switch (phase)
            {
                case HandshakePhase.Idle: await IdleTickAsync(ct); break;
                case HandshakePhase.InSync when host.AtSafePoint: await SwitchNowAsync(ct); break;
                case HandshakePhase.Waiting: await WaitTickAsync(ct); break;
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>The limit hit, with or without warning.</summary>
    public async Task LimitHitAsync(string accountId, DateTimeOffset? resetsAt, CancellationToken ct)
    {
        CancelLinking();
        await gate.WaitAsync(ct);
        try { await HardLimitAsync(accountId, resetsAt, allowAsk: true, ct); }
        finally { gate.Release(); }
    }

    /// <summary>The user's answer to a <see cref="HandshakeEventKind.DecisionRequested"/> card: the choices, and the one Remember line for all of them.</summary>
    public async Task AnswerAsync(IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (pending is null || phase != HandshakePhase.Asking) throw new InvalidOperationException("No card is waiting for an answer.");
            var card = pending;
            foreach (var choice in choices)
            {
                if (!card.Kinds.Contains(choice.Kind)) throw new ArgumentException($"The card did not ask {choice.Kind}.", nameof(choices));
                if (choice.Value == DecisionValues.Ask) throw new ArgumentException("A card cannot be answered with Ask me.", nameof(choices));
            }
            decisions.Record(threadId, choices, scope);
            foreach (var choice in choices) answers[choice.Kind] = choice.Value;
            pending = null;
            SetPhase(HandshakePhase.Idle);
            try
            {
                if (card.Earlier) await EarlierAsync(card.Back!, allowAsk: false, ct);
                else await HardLimitAsync(card.AccountId, card.ResetsAt, allowAsk: false, ct);
            }
            finally { answers.Clear(); }
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        CancelLinking();
        await gate.WaitAsync();
        try { await ResetPipelineAsync(); }
        finally { gate.Release(); }
    }

    // ---- Idle: the start point, the hard stop seen by polling, and an earlier account coming back ----

    private async Task IdleTickAsync(CancellationToken ct)
    {
        var active = host.ActiveAccountId;
        var account = Find(active);
        if (account is null || limits.Usage(active) is not { } usage) return;

        var now = clock.GetUtcNow();
        var exhausted = usage.IsExhaustedAt(now);
        var threshold = HandshakeTiming.StartPercent(account, CurrentStep()?.Step, options, limits, threadId);
        if (!exhausted && usage.PercentUsed < threshold) failedFor = null;
        if (failedFor == active) return;

        if (exhausted) await HardLimitAsync(active, usage.ResetsAt, allowAsk: true, ct);
        else if (options.Enabled && usage.PercentUsed >= threshold) await NearLimitAsync(active, ct);
        else if (!host.Pinned && chooser.EarlierBack(active, host.Pill, host.StepIndex) is { } back && !declined.Contains(back.Account.Id))
            await EarlierAsync(back, allowAsk: true, ct);
    }

    private async Task NearLimitAsync(string active, CancellationToken ct)
    {
        // Nothing is spent ahead of time unless every choice the switch needs is already made (the first-time card comes at the limit).
        if (Get(DecisionKind.OnLimit).Value != DecisionValues.Switch) return;
        if (Get(DecisionKind.Bring).Value is not { } chosen) return;
        if (chooser.Next(active, host.Pill, host.StepIndex) is not ChooserResult.Move move) return;
        await RunPipelineAsync(active, move.Target, HandshakeReason.NearLimit, DecisionValues.ToBring(chosen), hardStop: false, resumeReply: false, ct);
    }

    private async Task EarlierAsync(SwitchTarget back, bool allowAsk, CancellationToken ct)
    {
        var active = host.ActiveAccountId;
        outgoing = active;
        reason = HandshakeReason.EarlierAccountBack;
        var earlier = Get(DecisionKind.EarlierAccount, host.Pill?.EarlierAccount);
        if (earlier.Value == DecisionValues.Stay)
        {
            declined.Add(back.Account.Id);
            return;
        }

        var bringChoice = Get(DecisionKind.Bring);
        var needs = new List<DecisionKind>();
        if (earlier.MustAsk) needs.Add(DecisionKind.EarlierAccount);
        if (bringChoice.MustAsk) needs.Add(DecisionKind.Bring);
        if (needs.Count > 0 && allowAsk)
        {
            Ask(new Pending(true, active, null, back, needs), earlier.MustAsk && earlier.FirstTime || bringChoice.MustAsk && bringChoice.FirstTime, back.Account.Id);
            return;
        }

        if ((earlier.Value ?? DecisionValues.Fallback(DecisionKind.EarlierAccount)) == DecisionValues.Stay)
        {
            declined.Add(back.Account.Id);
            return;
        }
        var bringValue = bringChoice.Value ?? DecisionValues.Fallback(DecisionKind.Bring);
        await RunPipelineAsync(active, back, HandshakeReason.EarlierAccountBack, DecisionValues.ToBring(bringValue), hardStop: false, resumeReply: false, ct);
    }

    // ---- The limit has hit ----

    private async Task HardLimitAsync(string accountId, DateTimeOffset? resetsAt, bool allowAsk, CancellationToken ct)
    {
        if (phase is HandshakePhase.Asking or HandshakePhase.Waiting) return;
        outgoing = accountId;
        reason = HandshakeReason.HardLimit;
        resetsAt ??= limits.Usage(accountId)?.ResetsAt;

        var onLimit = Get(DecisionKind.OnLimit);
        if (onLimit.Value == DecisionValues.Wait)
        {
            await EnterWaitAsync(resetsAt, forAny: false);
            return;
        }

        var needs = new List<DecisionKind>();
        var firstTime = false;
        void Need(Resolution r)
        {
            if (!r.MustAsk) return;
            needs.Add(r.Kind);
            firstTime |= r.FirstTime;
        }

        // A standby already prepared keeps its target: its choices were all made before it started.
        var chosen = standby is not null ? target : null;
        if (chosen is null)
        {
            Need(onLimit);
            switch (chooser.Next(accountId, host.Pill, host.StepIndex))
            {
                case ChooserResult.Move move:
                    chosen = move.Target;
                    break;
                case ChooserResult.OtherService other:
                    var service = Get(DecisionKind.OtherService);
                    if (service.Value == DecisionValues.Wait)
                    {
                        await EnterWaitAsync(other.EarliestReset, forAny: true);
                        return;
                    }
                    Need(service);
                    chosen = other.Target;
                    break;
                case ChooserResult.WaitFor wait:
                    await EnterWaitAsync(wait.EarliestReset, forAny: true);
                    return;
                default:
                    Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: "No enabled account to move to.");
                    await ResetPipelineAsync();
                    if (phase != HandshakePhase.Idle) SetPhase(HandshakePhase.Idle);
                    return;
            }
            Need(Get(DecisionKind.Bring));
            if (host.ReplyInFlight) Need(Get(DecisionKind.MidReply));
        }

        if (needs.Count > 0 && allowAsk)
        {
            Ask(new Pending(false, accountId, resetsAt, null, needs), firstTime, chosen.Account.Id);
            return;
        }

        var bringValue = Get(DecisionKind.Bring).Value ?? DecisionValues.Fallback(DecisionKind.Bring);
        var finish = host.ReplyInFlight && (Get(DecisionKind.MidReply).Value ?? DecisionValues.Fallback(DecisionKind.MidReply)) == DecisionValues.Finish;
        await RunPipelineAsync(accountId, chosen, HandshakeReason.HardLimit, DecisionValues.ToBring(bringValue), hardStop: true, resumeReply: finish, ct);
    }

    private async Task EnterWaitAsync(DateTimeOffset? until, bool forAny)
    {
        await ResetPipelineAsync();
        waitUntil = until;
        waitForAny = forAny;
        SetPhase(HandshakePhase.Waiting);
    }

    private async Task WaitTickAsync(CancellationToken ct)
    {
        var active = host.ActiveAccountId;
        var now = clock.GetUtcNow();
        if (limits.Usage(active) is not { } usage || !usage.IsExhaustedAt(now))
        {
            reason = HandshakeReason.ResetReached;
            SetPhase(HandshakePhase.Idle);
            return;
        }
        if (!waitForAny || chooser.Next(active, host.Pill, host.StepIndex) is not ChooserResult.Move move) return;

        var bringValue = Get(DecisionKind.Bring).Value ?? DecisionValues.Fallback(DecisionKind.Bring);
        await RunPipelineAsync(active, move.Target, HandshakeReason.ResetReached, DecisionValues.ToBring(bringValue), hardStop: true, resumeReply: false, ct);
    }

    // ---- The pipeline: Standby, Linking, In sync, Switch ----

    private async Task RunPipelineAsync(
        string from, SwitchTarget to, HandshakeReason why, Bring what, bool hardStop, bool resumeReply, CancellationToken ct)
    {
        outgoing = from;
        target = to;
        reason = why;
        bring = what;
        resume = resumeReply;
        waitUntil = null;

        linking = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linking.Token;
        try
        {
            if (standby is null)
            {
                SetPhase(HandshakePhase.Standby);
                standby = await host.StartStandbyAsync(to, token);
            }

            SetPhase(HandshakePhase.Linking);
            summary ??= await summaries.BuildAsync(threadId, token);

            // The outgoing account writes the briefing only while it still has usage.
            var outHasUsage = !(limits.Usage(from)?.IsExhaustedAt(clock.GetUtcNow()) ?? false);
            if (!hardStop && options.Enabled && briefing is null && outHasUsage)
            {
                try { briefing = await host.RequestBriefingAsync(summary, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: $"The briefing failed: {ex.Message}");
                }
            }

            string? readBack = null;
            if (!received)
            {
                readBack = await standby.ReceiveAsync(new HandoffPackage(bring, summary, briefing), options.Enabled, token);
                received = true;
            }

            if (options.Enabled)
            {
                SetPhase(HandshakePhase.InSync);
                if (!synced)
                {
                    var gaps = summaries.FindGaps(summary, readBack ?? "");
                    if (gaps.Count > 0) await standby.FillGapsAsync(gaps, token);
                    synced = true;
                    Emit(HandshakeEventKind.InSyncReady, phase, phase, null, detail: gaps.Count == 0 ? "in sync" : gaps.Count == 1 ? "1 gap filled" : $"{gaps.Count} gaps filled");
                }
            }
            else
            {
                synced = true;
            }
        }
        catch (OperationCanceledException) when (linking.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // A limit hit under it: the hard-stop path takes over with what is already prepared.
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: $"The Handshake could not finish: {ex.Message}");
            failedFor = from;
            await ResetPipelineAsync();
            SetPhase(HandshakePhase.Idle);
            return;
        }
        finally
        {
            linking?.Dispose();
            linking = null;
        }

        if (hardStop || host.AtSafePoint) await SwitchNowAsync(ct);
    }

    private async Task SwitchNowAsync(CancellationToken ct)
    {
        SetPhase(HandshakePhase.Switch);
        try
        {
            await host.SwitchAsync(standby!, target!, resume, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: $"The switch failed: {ex.Message}");
            failedFor = outgoing;
            await ResetPipelineAsync();
            SetPhase(HandshakePhase.Idle);
            return;
        }

        // The host owns the session now.
        standby = null;
        declined.Clear();
        failedFor = null;
        SetPhase(HandshakePhase.Idle);
        ClearPipeline();
    }

    // ---- State, events ----

    private void Ask(Pending card, bool firstTime, string? incoming)
    {
        pending = card;
        var request = new DecisionRequest(threadId, card.Kinds, firstTime, card.AccountId, incoming, card.ResetsAt);
        SetPhase(HandshakePhase.Asking);
        Emit(HandshakeEventKind.DecisionRequested, phase, phase, request, incomingOverride: incoming);
    }

    private Resolution Get(DecisionKind kind, string? pillValue = null) =>
        answers.TryGetValue(kind, out var value)
            ? new Resolution(kind, value, ResolutionSource.Thread, FirstTime: false)
            : decisions.Resolve(threadId, kind, pillValue);

    private AccountInfo? Find(string id) => store.Accounts.FirstOrDefault(a => a.Id == id);

    private (PillStep Step, int Index)? CurrentStep() =>
        host.Pill is { } pill && host.StepIndex is { } i && i >= 0 && i < pill.Steps.Count ? (pill.Steps[i], i) : null;

    private void CancelLinking()
    {
        try { linking?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task ResetPipelineAsync()
    {
        var abandoned = standby;
        standby = null;
        ClearPipeline();
        if (abandoned is not null) await abandoned.DisposeAsync();
    }

    private void ClearPipeline()
    {
        target = null;
        summary = null;
        briefing = null;
        received = false;
        synced = false;
        resume = false;
        waitUntil = null;
    }

    private void SetPhase(HandshakePhase to)
    {
        var from = phase;
        Volatile.Write(ref phaseValue, (int)to);
        Emit(HandshakeEventKind.PhaseChanged, from, to, null);
    }

    private void Emit(HandshakeEventKind kind, HandshakePhase from, HandshakePhase to, DecisionRequest? request, string? detail = null, string? incomingOverride = null) =>
        Changed?.Invoke(this, new HandshakeEvent(
            threadId, kind, from, to, outgoing, incomingOverride ?? target?.Account.Id, reason, clock.GetUtcNow(),
            to == HandshakePhase.Waiting ? waitUntil : null, request, detail));
}
