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
    HandshakeOptions options,
    Func<HandshakeOptions>? optionsSource = null) : IAsyncDisposable
{
    private sealed record Pending(bool Earlier, string AccountId, DateTimeOffset? ResetsAt, SwitchTarget? Back, IReadOnlyList<DecisionKind> Kinds);

    private readonly NextAccountChooser chooser = new(store, limits, clock);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> declined = [];
    private readonly Dictionary<DecisionKind, string> answers = [];
    private readonly Dictionary<DecisionKind, Resolution> answerPolicies = [];
    private readonly Dictionary<DecisionKind, Resolution> policyAtPreparation = [];

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
    private bool waitForPin;

    public HandshakePhase Phase => phase;
    private HandshakeOptions CurrentOptions => optionsSource?.Invoke() ?? options;

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
                case HandshakePhase.InSync when host.AtSafePoint: await CommitPreparedSwitchAsync(ct); break;
                case HandshakePhase.Waiting: await WaitTickAsync(ct); break;
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>The limit hit, with or without warning.</summary>
    public async Task LimitHitAsync(string accountId, DateTimeOffset? resetsAt, CancellationToken ct, bool waitOnce = false)
    {
        CancelLinking();
        await gate.WaitAsync(ct);
        try
        {
            if (waitOnce && host.ActiveAccountId == accountId)
            {
                answers[DecisionKind.OnLimit] = DecisionValues.Wait;
                answerPolicies[DecisionKind.OnLimit] = StoredPolicy(DecisionKind.OnLimit);
            }
            await HardLimitAsync(accountId, resetsAt, ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>The waiting note's explicit Switch now action; unresolved transfer choices still ask.</summary>
    public async Task SwitchNowAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (phase != HandshakePhase.Waiting) throw new InvalidOperationException("This conversation is no longer waiting.");
            answers[DecisionKind.OnLimit] = DecisionValues.Switch;
            answerPolicies[DecisionKind.OnLimit] = StoredPolicy(DecisionKind.OnLimit);
            SetPhase(HandshakePhase.Idle);
            await HardLimitAsync(host.ActiveAccountId, waitUntil, ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>The user's answer to a <see cref="HandshakeEventKind.DecisionRequested"/> card: the choices, and the one Remember line for all of them.</summary>
    public async Task AnswerAsync(IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct, Action? onAccepted = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (pending is null || phase != HandshakePhase.Asking) throw new InvalidOperationException("No card is waiting for an answer.");
            var card = pending;
            if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
            if (choices.Select(choice => choice.Kind).Distinct().Count() != choices.Count)
                throw new ArgumentException("A card needs one value per choice.", nameof(choices));
            foreach (var choice in choices)
            {
                if (!card.Kinds.Contains(choice.Kind)) throw new ArgumentException($"The card did not ask {choice.Kind}.", nameof(choices));
                if (choice.Value == DecisionValues.Ask && !(choice.Kind == DecisionKind.OnLimit
                    && choices.Count == 1 && card.Kinds.Contains(DecisionKind.OnLimit) && Get(DecisionKind.OnLimit).FirstTime))
                    throw new ArgumentException("Only the first-time limit card offers Ask me each time.", nameof(choices));
            }
            var preset = choices.Any(choice => choice.Kind == DecisionKind.OnLimit && choice.Value == DecisionValues.Switch);
            var stays = choices.Any(choice => choice.Kind == DecisionKind.OnLimit && choice.Value == DecisionValues.Ask
                || choice.Kind is DecisionKind.OnLimit or DecisionKind.OtherService && choice.Value == DecisionValues.Wait
                || choice.Kind == DecisionKind.EarlierAccount && choice.Value == DecisionValues.Stay);
            foreach (var kind in card.Kinds)
                if (!choices.Any(choice => choice.Kind == kind) && !stays
                    && (kind is DecisionKind.OnLimit or DecisionKind.OtherService or DecisionKind.EarlierAccount || !preset))
                    throw new ArgumentException("The card's permission to switch has not been answered.", nameof(choices));
            decisions.Record(threadId, choices, scope);
            foreach (var choice in choices)
            {
                answers[choice.Kind] = choice.Value;
                answerPolicies[choice.Kind] = StoredPolicy(choice.Kind);
            }
            // The existing Switch preset supplies its payload defaults for this operation, not later limits.
            if (preset)
                foreach (var kind in card.Kinds.Where(kind => kind is DecisionKind.Bring or DecisionKind.MidReply))
                    if (answers.TryAdd(kind, DecisionValues.Fallback(kind))) answerPolicies[kind] = StoredPolicy(kind);
            pending = null;
            SetPhase(HandshakePhase.Idle);
            onAccepted?.Invoke();
            try
            {
                if (card.Earlier) await EarlierAsync(card.Back!, ct);
                else await HardLimitAsync(card.AccountId, card.ResetsAt, ct);
            }
            finally { if (phase == HandshakePhase.Idle && standby is null) ClearAnswers(); }
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
        var threshold = HandshakeTiming.StartPercent(account, CurrentStep()?.Step, CurrentOptions, limits, threadId);
        if (!exhausted && usage.PercentUsed < threshold) failedFor = null;
        if (failedFor == active) return;

        if (exhausted) await HardLimitAsync(active, usage.ResetsAt, ct);
        else if (!host.Pinned && CurrentOptions.Enabled && usage.PercentUsed >= threshold) await NearLimitAsync(active, ct);
        else if (!host.Pinned && chooser.EarlierBack(active, host.Pill, host.StepIndex, HasComeBack) is { } back && !declined.Contains(back.Account.Id))
            await EarlierAsync(back, ct);
    }

    // PORT-ONLY: ED-433. An earlier account that is still near its own Handshake point has not come back: going to it would start the next Handshake at once.
    private bool HasComeBack(SwitchTarget candidate) =>
        limits.Usage(candidate.Account.Id) is not { } usage
        || usage.PercentUsed < HandshakeTiming.StartPercent(candidate.Account, candidate.Step, CurrentOptions, limits, threadId);

    private async Task NearLimitAsync(string active, CancellationToken ct)
    {
        // Nothing is spent ahead of time unless every choice the switch needs is already made (the first-time card comes at the limit).
        if (Get(DecisionKind.OnLimit).Value != DecisionValues.Switch) return;
        if (Get(DecisionKind.Bring).Value is not { } chosen) return;
        if (chooser.Next(active, host.Pill, host.StepIndex) is not ChooserResult.Move move) return;
        await RunPipelineAsync(active, move.Target, HandshakeReason.NearLimit, DecisionValues.ToBring(chosen), hardStop: false, resumeReply: false, ct);
    }

    private async Task EarlierAsync(SwitchTarget back, CancellationToken ct)
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
        if (needs.Count > 0)
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

    private async Task HardLimitAsync(string accountId, DateTimeOffset? resetsAt, CancellationToken ct,
        HandshakeReason why = HandshakeReason.HardLimit)
    {
        if (phase is HandshakePhase.Asking or HandshakePhase.Waiting) return;
        outgoing = accountId;
        reason = why;
        resetsAt ??= limits.Usage(accountId)?.ResetsAt;

        if (host.Pinned)
        {
            await EnterWaitAsync(resetsAt, forAny: false);
            waitForPin = true;
            return;
        }

        // A repeated native limit signal is still the same operation, but cannot renew a revoked grant.
        if (standby is not null && (!PolicyStillCurrent() || target is null
            || Find(target.Account.Id) is not { Enabled: true }
            || (limits.Usage(target.Account.Id)?.IsExhaustedAt(clock.GetUtcNow()) ?? false)))
            await ResetPipelineAsync(preserveAnswers: true);

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

        // A prepared target cannot answer a newly relevant mid-reply choice or a revoked preference.
        Need(onLimit);
        Need(Get(DecisionKind.Bring));
        // PORT-ONLY: ED-439. "Finish the reply?" is only asked of a destination that can continue it; a deferred one ignores the answer.
        var midAt = needs.Count;
        var midReply = host.ReplyInFlight ? Get(DecisionKind.MidReply) : null;
        void NeedMidReply()
        {
            if (midReply is not { MustAsk: true } r) return;
            needs.Insert(midAt, r.Kind);
            firstTime |= r.FirstTime;
            midReply = null;
        }
        var chosen = standby is not null ? target : null;
        DateTimeOffset? alternateReset = null;
        if (chosen is null)
        {
            switch (chooser.Next(accountId, host.Pill, host.StepIndex))
            {
                case ChooserResult.Move move:
                    chosen = move.Target;
                    break;
                case ChooserResult.OtherService other:
                    alternateReset = other.EarliestReset;
                    chosen = other.Target;
                    break;
                case ChooserResult.WaitFor wait:
                    NeedMidReply();
                    if (needs.Count > 0)
                    {
                        Ask(new Pending(false, accountId, resetsAt, null, needs), firstTime, null);
                        return;
                    }
                    await EnterWaitAsync(wait.EarliestReset, forAny: true);
                    return;
                default:
                    Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: "No enabled account to move to.");
                    await ResetPipelineAsync();
                    if (phase != HandshakePhase.Idle) SetPhase(HandshakePhase.Idle);
                    return;
            }
        }

        if (!await DeferredAsync(chosen, ct)) NeedMidReply();

        // Retained candidates and a destination that changed after a card still need service permission.
        if (host.Pill is null && Find(accountId)?.Service != chosen.Account.Service)
        {
            var service = Get(DecisionKind.OtherService);
            if (service.Value == DecisionValues.Wait)
            {
                await EnterWaitAsync(alternateReset, forAny: true);
                return;
            }
            Need(service);
        }
        if (needs.Count > 0)
        {
            Ask(new Pending(false, accountId, resetsAt, null, needs), firstTime, chosen.Account.Id);
            return;
        }

        var bringValue = Get(DecisionKind.Bring).Value ?? DecisionValues.Fallback(DecisionKind.Bring);
        var finish = host.ReplyInFlight && (Get(DecisionKind.MidReply).Value ?? DecisionValues.Fallback(DecisionKind.MidReply)) == DecisionValues.Finish;
        await RunPipelineAsync(accountId, chosen, why, DecisionValues.ToBring(bringValue), hardStop: true, resumeReply: finish, ct);
    }

    private async Task EnterWaitAsync(DateTimeOffset? until, bool forAny)
    {
        await ResetPipelineAsync(preserveAnswers: true);
        waitUntil = until;
        waitForAny = forAny;
        SetPhase(HandshakePhase.Waiting);
    }

    private async Task WaitTickAsync(CancellationToken ct)
    {
        var active = host.ActiveAccountId;
        var now = clock.GetUtcNow();
        var usage = limits.Usage(active);
        if (usage is not null && !usage.IsExhaustedAt(now)
            || usage is null && (limits.IsAvailable(active) == true
                || !waitForAny && waitUntil is { } reportedReset && now >= reportedReset))
        {
            reason = HandshakeReason.ResetReached;
            ClearAnswers();
            SetPhase(HandshakePhase.Idle);
            return;
        }
        if (waitForPin && !host.Pinned)
        {
            waitForPin = false;
            SetPhase(HandshakePhase.Idle);
            await HardLimitAsync(active, waitUntil ?? usage?.ResetsAt, ct);
            return;
        }
        if (usage is null || !waitForAny) return;
        var available = chooser.Next(active, host.Pill, host.StepIndex);
        if (available is not (ChooserResult.Move or ChooserResult.OtherService)
            || available is ChooserResult.OtherService && Get(DecisionKind.OtherService).Value == DecisionValues.Wait) return;
        SetPhase(HandshakePhase.Idle);
        await HardLimitAsync(active, usage.ResetsAt, ct, HandshakeReason.ResetReached);
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
        CapturePolicy(why, to);
        if (host is IAccountSwitchCommitGuard preparing)
            preparing.CanCommit = () => !host.Pinned && PolicyStillCurrent() && host.ActiveAccountId == outgoing
                && (reason != HandshakeReason.NearLimit || CurrentOptions.Enabled);

        linking = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linking.Token;
        try
        {
            if (standby is null)
            {
                SetPhase(HandshakePhase.Standby);
                standby = await host.StartStandbyAsync(to, token);
            }

            // A deferred destination (no verified standby) never continues the reply: its first real turn carries the context.
            var deferred = Deferred(to);
            if (deferred) resume = false;

            EnsureTransferAuthorized(from, to, why);
            SetPhase(HandshakePhase.Linking);
            summary ??= await summaries.BuildAsync(threadId, token);
            EnsureTransferAuthorized(from, to, why);

            // The outgoing account writes the briefing only while it still has usage.
            var outHasUsage = !(limits.Usage(from)?.IsExhaustedAt(clock.GetUtcNow()) ?? false);
            if (!hardStop && CurrentOptions.Enabled && briefing is null && outHasUsage)
            {
                try { briefing = await host.RequestBriefingAsync(summary, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: $"The briefing failed: {ex.Message}");
                }
            }

            if (bring == Bring.AiSummary && string.IsNullOrWhiteSpace(briefing))
            {
                EnsureTransferAuthorized(from, to, why);
                if (host is not IIncomingBriefingHost incoming)
                    throw new NotSupportedException("The incoming service cannot provide a verified light-model briefing.");
                briefing = await incoming.RequestIncomingBriefingAsync(to, summary, token);
                if (string.IsNullOrWhiteSpace(briefing) || !PolicyStillCurrent())
                    throw new InvalidOperationException("The incoming account did not provide an authorized AI briefing.");
            }

            EnsureTransferAuthorized(from, to, why);
            string? readBack = null;
            if (!received)
            {
                // An injected history is loaded without a model turn, so there is nothing to read back; its readiness is the same in-sync tick (OD-0122).
                readBack = await standby.ReceiveAsync(new HandoffPackage(bring, summary, briefing), CurrentOptions.Enabled && !deferred && standby.Kind != StandbyKind.Injected, token);
                received = true;
                // A loaded history that was discarded for safety is now a deferred delivery.
                deferred = Deferred(to);
                if (deferred) resume = false;
            }

            if (CurrentOptions.Enabled && !deferred)
            {
                SetPhase(HandshakePhase.InSync);
                if (!synced && standby.Kind == StandbyKind.Injected)
                {
                    synced = true;
                    Emit(HandshakeEventKind.InSyncReady, phase, phase, null, detail: "in sync");
                }
                else if (!synced)
                {
                    var gaps = summaries.FindGaps(summary, readBack ?? "");
                    EnsureTransferAuthorized(from, to, why);
                    if (gaps.Count > 0) await standby.FillGapsAsync(gaps, token);
                    EnsureTransferAuthorized(from, to, why);
                    synced = true;
                    Emit(HandshakeEventKind.InSyncReady, phase, phase, null, detail: gaps.Count == 0 ? "in sync" : gaps.Count == 1 ? "1 gap filled" : $"{gaps.Count} gaps filled");
                }
            }
            else
            {
                synced = true;
                SetPhase(HandshakePhase.InSync);
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

        if (host.AtSafePoint) await CommitPreparedSwitchAsync(ct);
    }

    private bool Deferred(SwitchTarget to) => standby?.Kind == StandbyKind.Deferred || host is IDeferredDeliveryHost deferred && deferred.DeliversDeferred(to);

    private async Task<bool> DeferredAsync(SwitchTarget to, CancellationToken ct) =>
        standby?.Kind == StandbyKind.Deferred || host is IDeferredDeliveryHost deferred && await deferred.ResolveDeliversDeferredAsync(to, ct);

    private void EnsureTransferAuthorized(string from, SwitchTarget to, HandshakeReason why)
    {
        if (!PolicyStillCurrent() || host.ActiveAccountId != from || host.Pinned
            || Find(to.Account.Id) is not { Enabled: true }
            || limits.Usage(to.Account.Id)?.IsExhaustedAt(clock.GetUtcNow()) == true
            || why == HandshakeReason.NearLimit && !CurrentOptions.Enabled)
            throw new InvalidOperationException("Permission for context transfer changed.");
    }

    private async Task CommitPreparedSwitchAsync(CancellationToken ct)
    {
        // PORT-ONLY: ED-403. Preparation cannot retain permission to use a row turned Off meanwhile.
        if (target is null || Find(target.Account.Id) is not { Enabled: true }
            || (limits.Usage(target.Account.Id)?.IsExhaustedAt(clock.GetUtcNow()) ?? false)
            || host.ActiveAccountId != outgoing || !PolicyStillCurrent()
            || reason == HandshakeReason.NearLimit && !CurrentOptions.Enabled
            || host.Pinned)
        {
            failedFor = host.Pinned ? null : outgoing;
            Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: "The prepared account is no longer available.");
            await ResetPipelineAsync();
            SetPhase(HandshakePhase.Idle);
            return;
        }
        // Priming can run while the source works. At a running step, only an already chosen Finish
        // grants continuation; Stop/unset keep working until the natural reply end (or hard limit).
        if (reason is HandshakeReason.NearLimit or HandshakeReason.EarlierAccountBack && host.ReplyInFlight)
        {
            // A deferred destination cannot continue a reply: the source reply ends first, whatever was chosen.
            if (Deferred(target) || Get(DecisionKind.MidReply).Value != DecisionValues.Finish) return;
            resume = true;
        }
        SetPhase(HandshakePhase.Switch);
        try
        {
            if (host is IAccountSwitchCommitGuard guarded)
                guarded.CanCommit = () => !host.Pinned && PolicyStillCurrent() && host.ActiveAccountId == outgoing
                    && (reason != HandshakeReason.NearLimit || CurrentOptions.Enabled);
            await host.SwitchAsync(standby!, target!, resume, ct);
        }
        catch (OperationCanceledException)
        {
            // A user stop at the provider's paused boundary keeps the original turn/session.
            // Retire its private preparation and suppress another proactive start at this same reading.
            failedFor = outgoing;
            await ResetPipelineAsync();
            SetPhase(HandshakePhase.Idle);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failedFor = host.Pinned ? null : outgoing;
            Emit(HandshakeEventKind.Degraded, phase, phase, null, detail: $"The switch failed: {ex.Message}");
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
            // Once OnLimit is "Ask", unresolved payload choices belong to the normal limit card.
            var request = new DecisionRequest(threadId, card.Kinds,
                firstTime && !(card.Kinds.Contains(DecisionKind.OnLimit) && !Get(DecisionKind.OnLimit).FirstTime), card.AccountId, incoming, card.ResetsAt);
        SetPhase(HandshakePhase.Asking);
        Emit(HandshakeEventKind.DecisionRequested, phase, phase, request, incomingOverride: incoming);
    }

    private Resolution Get(DecisionKind kind, string? pillValue = null)
    {
        if (answers.TryGetValue(kind, out var value) && answerPolicies.GetValueOrDefault(kind) == StoredPolicy(kind))
            return new Resolution(kind, value == DecisionValues.Ask ? null : value, ResolutionSource.Thread, FirstTime: false);
        answers.Remove(kind);
        answerPolicies.Remove(kind);
        return decisions.Resolve(threadId, kind, pillValue);
    }

    private AccountInfo? Find(string id) => store.Accounts.FirstOrDefault(a => a.Id == id);

    private (PillStep Step, int Index)? CurrentStep() =>
        host.Pill is { } pill && host.StepIndex is { } i && i >= 0 && i < pill.Steps.Count ? (pill.Effective(i), i) : null; // MA-7: a pill's same-for-every point replaces the step's own

    private void CancelLinking()
    {
        try { linking?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task ResetPipelineAsync(bool preserveAnswers = false)
    {
        var savedAnswers = preserveAnswers ? new Dictionary<DecisionKind, string>(answers) : null;
        var savedPolicies = preserveAnswers ? new Dictionary<DecisionKind, Resolution>(answerPolicies) : null;
        var abandoned = standby;
        standby = null;
        ClearPipeline();
        if (savedAnswers is not null)
        {
            foreach (var pair in savedAnswers) answers[pair.Key] = pair.Value;
            foreach (var pair in savedPolicies!) answerPolicies[pair.Key] = pair.Value;
        }
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
        waitForPin = false;
        ClearAnswers();
        policyAtPreparation.Clear();
    }

    private Resolution StoredPolicy(DecisionKind kind) => decisions.Resolve(threadId, kind,
        kind == DecisionKind.EarlierAccount ? host.Pill?.EarlierAccount : null);

    private void CapturePolicy(HandshakeReason why, SwitchTarget to)
    {
        void Capture(DecisionKind kind) => policyAtPreparation.TryAdd(kind, StoredPolicy(kind));
        Capture(DecisionKind.Bring);
        Capture(DecisionKind.MidReply);
        if (why == HandshakeReason.EarlierAccountBack) Capture(DecisionKind.EarlierAccount);
        else
        {
            Capture(DecisionKind.OnLimit);
            if (host.Pill is null && Find(host.ActiveAccountId)?.Service != to.Account.Service) Capture(DecisionKind.OtherService);
        }
    }

    private bool PolicyStillCurrent() => policyAtPreparation.All(pair => pair.Value == StoredPolicy(pair.Key));

    private void ClearAnswers() { answers.Clear(); answerPolicies.Clear(); }

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
