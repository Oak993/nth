namespace Marrow.Core.Accounts.Keys;

/// <summary>How long a key stays out when the service gives no time. ENGINEERING DECISION (MA-6 ED): rate limit 60 s, out of credits re-checked after 30 min.</summary>
public sealed record ApiKeyPoolOptions(TimeSpan RateLimitCooldown, TimeSpan CreditsRecheck)
{
    public ApiKeyPoolOptions() : this(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30)) { }
}

/// <summary>Whether any allowed key can take a request now; <see cref="ReturnsAt"/> is when the first one can again, if known.</summary>
public sealed record PoolState(bool AllOut, DateTimeOffset? ReturnsAt, int Available);

/// <summary>One service's keys (NOTES "API providers"): order modes, Allowed switch, monthly cap, silent continuation on 402 and 429, per-thread pin.</summary>
public sealed class ApiKeyPool
{
    private enum OutKind { RateLimit, Credits }

    private readonly string service;
    private readonly IApiKeyStore store;
    private readonly IApiKeyCredits credits;
    private readonly IApiKeySpend spend;
    private readonly TimeProvider clock;
    private readonly ApiKeyPoolOptions settings;
    private readonly IKeyMovePolicy? moves;
    private readonly Lock gate = new();
    private readonly Dictionary<string, (DateTimeOffset Until, OutKind Kind, DateTimeOffset At)> outs = [];
    private readonly Dictionary<string, int> uses = [];
    private readonly Dictionary<Guid, string> pins = [];
    private readonly Dictionary<Guid, Rotation> rotations = [];
    private readonly HashSet<(string Key, int Month)> capped = [];
    private readonly Dictionary<Guid, (string Key, double Usd)> activeReservations = [];

    public ApiKeyPool(string service, IApiKeyStore store, IApiKeyCredits credits, IApiKeySpend spend, TimeProvider clock, ApiKeyPoolOptions? options = null, IKeyMovePolicy? moves = null)
    {
        this.moves = moves;
        this.service = service;
        this.store = store;
        this.credits = credits;
        this.spend = spend;
        this.clock = clock;
        settings = options ?? new ApiKeyPoolOptions();
    }

    public event EventHandler<ApiKeyEvent>? Changed;

    public IApiKeyRotation ForThread(Guid threadId, string? initialKey = null)
    {
        lock (gate)
        {
            var entry = initialKey is null ? null : store.Keys(service).FirstOrDefault(key => key.Id == initialKey)
                ?? throw new ArgumentException("The bound API account is unavailable.", nameof(initialKey));
            if (!rotations.TryGetValue(threadId, out var rotation) || entry is not null && rotation.BoundKeyId != entry.Id)
            {
                rotation?.Retire();
                rotations[threadId] = rotation = new Rotation(this, threadId, entry is null ? null : new(entry.Id, entry.Nickname, ""));
            }
            return rotation;
        }
    }

    /// <summary>Pins a thread to one key, or unpins it with null (NOTES "per-thread pin").</summary>
    public void Pin(Guid threadId, string? keyId)
    {
        lock (gate)
        {
            if (keyId is null) pins.Remove(threadId);
            else pins[threadId] = keyId;
        }
    }

    public void Release(Guid threadId)
    {
        lock (gate)
        {
            pins.Remove(threadId);
            if (rotations.Remove(threadId, out var rotation)) rotation.Retire();
        }
    }

    public PoolState State()
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            var available = Candidates(now, []).Count;
            return available > 0 ? new PoolState(false, null, available) : new PoolState(true, EarliestReturn(now), 0);
        }
    }

    public bool IsAvailable(string keyId)
    {
        lock (gate) return store.Keys(service).FirstOrDefault(key => key.Id == keyId) is { } entry && Usable(entry, clock.GetUtcNow());
    }

    // ---- under the gate ----

    private List<(ApiKeyEntry Entry, string Secret)> Candidates(DateTimeOffset now, HashSet<string> exclude)
    {
        var ranked = new List<(ApiKeyEntry Entry, string Secret, int Index)>();
        var index = 0;
        foreach (var entry in store.Keys(service))
        {
            var at = index++;
            if (exclude.Contains(entry.Id) || !Usable(entry, now)) continue;
            ranked.Add((entry, store.Secret(entry.Id)!.Trim(), at));
        }
        var ordered = store.Order(service) switch
        {
            KeyOrderMode.MostCreditsFirst => ranked.OrderByDescending(r => credits.CreditsLeft(r.Entry.Id) ?? double.NegativeInfinity).ThenBy(r => r.Index),
            KeyOrderMode.SpreadEvenly => ranked.OrderBy(r => uses.GetValueOrDefault(r.Entry.Id)).ThenBy(r => r.Index),
            _ => ranked.OrderBy(r => r.Index),
        };
        return [.. ordered.Select(r => (r.Entry, r.Secret))];
    }

    private bool HasSecret(ApiKeyEntry entry) => !string.IsNullOrWhiteSpace(store.Secret(entry.Id));

    private bool Usable(ApiKeyEntry entry, DateTimeOffset now) =>
        entry.Allowed && HasSecret(entry) && !IsOut(entry.Id, now) && !OverCap(entry, now);

    private bool OverCap(ApiKeyEntry entry, DateTimeOffset now) =>
        entry.MonthlyCapUsd is { } cap && BudgetSpend(entry.Id, Month(now).Start)
            - activeReservations.Values.Where(item => item.Key == entry.Id).Sum(item => item.Usd) >= cap;

    private double BudgetSpend(string keyId, DateTimeOffset since) => spend is IApiKeyReservedSpend durable
        ? durable.BudgetSince(keyId, since) : spend.SpendSince(keyId, since);

    private bool IsOut(string keyId, DateTimeOffset now)
    {
        if (credits is IApiKeyCreditObservations { } reported && (reported.IsUsable(keyId) == false
            || reported.CreditsLeft(keyId) is { } empty && double.IsFinite(empty) && empty == 0)) return true;
        if (!outs.TryGetValue(keyId, out var state)) return false;
        // A positive report older than the actual 402 is not recovery. Legacy in-memory credit stores retain their undated seam.
        if (state.Kind == OutKind.Credits && credits.CreditsLeft(keyId) is { } positive && double.IsFinite(positive) && positive > 0
            && (credits is not IApiKeyCreditObservations dated || dated.ObservedAt(keyId) is { } at && at > state.At))
        { outs.Remove(keyId); return false; }
        if (state.Until > now) return true;
        outs.Remove(keyId);
        return false;
    }

    /// <summary>The user's LOCAL calendar month around <paramref name="now"/> (lead decision): its first instant, the next month's first instant, and an index to tell months apart.</summary>
    private (DateTimeOffset Start, DateTimeOffset Next, int Index) Month(DateTimeOffset now)
    {
        var zone = clock.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        DateTimeOffset First(int year, int month)
        {
            var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(first, zone.GetUtcOffset(first));
        }
        var next = local.Month == 12 ? First(local.Year + 1, 1) : First(local.Year, local.Month + 1);
        return (First(local.Year, local.Month), next, local.Year * 12 + local.Month);
    }

    /// <summary>The earliest moment an allowed key with a secret can take a request: its out time or the start of next local month for a capped one. Null when none is known.</summary>
    private DateTimeOffset? EarliestReturn(DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var entry in store.Keys(service))
        {
            if (!entry.Allowed || !HasSecret(entry)) continue;
            DateTimeOffset? back = null;
            if (outs.TryGetValue(entry.Id, out var state) && state.Until > now) back = state.Until;
            if (OverCap(entry, now))
            {
                if (spend is IApiKeyReservedSpend durable && durable.HasUnsettled(entry.Id)) continue;
                var next = Month(now).Next;
                back = back is { } b && b > next ? b : next;
            }
            if (back is { } when && (earliest is null || when < earliest)) earliest = when;
        }
        return earliest;
    }

    private ApiKeyEvent Event(ApiKeyEventKind kind, Guid thread, KeyChoice? from, KeyChoice? to, int? status, DateTimeOffset? returns, string message) =>
        new(kind, service, thread, from?.Id, from?.Nickname, to?.Id, to?.Nickname, status, clock.GetUtcNow(), returns, message);

    private void Raise(ApiKeyEvent e) => Changed?.Invoke(this, e);

    private sealed class Rotation(ApiKeyPool pool, Guid threadId, KeyChoice? initial) : IApiKeyRotation, IApiKeyContinuation, IApiKeyBudget
    {
        public IApiKeyBudget Budget => this;
        public bool RequiresPrice(string keyId)
        {
            lock (pool.gate) return pool.store.Keys(pool.service).FirstOrDefault(key => key.Id == keyId) is not { MonthlyCapUsd: null };
        }
        public IApiKeyReservation? Reserve(string keyId, double? maximumUsd)
        {
            lock (pool.gate)
            {
                if (retired || current?.Id != keyId || pool.store.Keys(pool.service).FirstOrDefault(key => key.Id == keyId) is not { } entry
                    || !pool.Usable(entry, pool.clock.GetUtcNow())) return null;
                if (maximumUsd is { } amount && (!double.IsFinite(amount) || amount < 0)) return null;
                if (entry.MonthlyCapUsd is { } cap && (maximumUsd is not { } bound || !double.IsFinite(cap) || cap < 0
                    || pool.BudgetSpend(keyId, pool.Month(pool.clock.GetUtcNow()).Start) + bound > cap
                    || pool.spend is not IApiKeyReservedSpend)) return null;
                var request = Guid.NewGuid();
                var at = pool.clock.GetUtcNow();
                if (pool.spend is IApiKeyReservedSpend durable) durable.Hold(request, keyId, maximumUsd, at);
                pool.activeReservations.Add(request, (keyId, maximumUsd ?? 0));
                return new Reservation(pool, threadId, current, request, at, () => !retired && current?.Id == keyId);
            }
        }

        private readonly HashSet<string> tried = [];
        private KeyChoice? current;
        // The key the thread was last on: leaving it because it ran out or hit its cap is a forced move (OD-0116); leaving it because the user turned it off is not.
        private KeyChoice? anchor = initial;
        private int? rejectedStatus;
        private bool moveOnce;
        private bool retired;
        public string? BoundKeyId => anchor?.Id;
        public void Retire() => retired = true;

        public string? StopReason { get; private set; }

        public string? ContinuedOn { get; private set; }

        public void AllowMoveOnce()
        {
            lock (pool.gate) { if (!retired) moveOnce = true; }
        }

        public KeyChoice? Current()
        {
            var raised = new List<ApiKeyEvent>();
            KeyChoice? choice;
            lock (pool.gate)
            {
                if (retired) return null;
                tried.Clear();
                rejectedStatus = null;
                StopReason = null;
                ContinuedOn = null;
                choice = Pick(raised);
                // A completed request cannot carry unused consent into the next request.
                moveOnce = false;
                current = choice;
                if (choice is not null) anchor = choice;
            }
            foreach (var e in raised) pool.Raise(e);
            return choice;
        }

        public KeyChoice? OnFailure(int status, TimeSpan? retryAfter)
        {
            if (status is not (402 or 429)) return null;
            var raised = new List<ApiKeyEvent>();
            KeyChoice? next = null;
            lock (pool.gate)
            {
                if (current is not { } failed) return null;
                var now = pool.clock.GetUtcNow();
                var (until, kind) = status == 402
                    ? (now + pool.settings.CreditsRecheck, OutKind.Credits)
                    : (now + (retryAfter ?? pool.settings.RateLimitCooldown), OutKind.RateLimit);
                pool.outs[failed.Id] = (until, kind, now);
                tried.Add(failed.Id);
                rejectedStatus = status;
                current = null;
                // The immutable request identity still owns its quota report after this thread was rebound.
                if (retired) return null;

                if (pool.pins.ContainsKey(threadId))
                {
                    StopReason = $"{failed.Nickname} is out. Unpin this thread to use another key.";
                    raised.Add(pool.Event(ApiKeyEventKind.PinnedKeyOut, threadId, failed, null, status, until, StopReason));
                }
                else if (pool.Candidates(now, tried) is not { Count: > 0 } candidates)
                {
                    raised.Add(pool.Event(ApiKeyEventKind.AllKeysOut, threadId, failed, null, status, pool.EarliestReturn(now), "All keys are out."));
                }
                else
                {
                    var proposed = new KeyChoice(candidates[0].Entry.Id, candidates[0].Entry.Nickname, candidates[0].Secret);
                    if (MoveAllowed(out var setting))
                    {
                        next = Choose(proposed);
                        current = next;
                        anchor = next;
                        raised.Add(pool.Event(ApiKeyEventKind.Continued, threadId, failed, next, status, null, $"Continued on {next.Nickname}"));
                    }
                    else
                    {
                        raised.Add(Withheld(setting, failed, proposed, $"{failed.Nickname} is out.", status));
                    }
                }
            }
            foreach (var e in raised) pool.Raise(e);
            return next;
        }

        public KeyChoice? ContinueRejectedRequest()
        {
            var raised = new List<ApiKeyEvent>();
            KeyChoice? next = null;
            lock (pool.gate)
            {
                if (retired || current is not null || rejectedStatus is not (402 or 429) || anchor is not { } failed) return null;
                var now = pool.clock.GetUtcNow();
                var candidates = pool.Candidates(now, tried);
                if (pool.pins.ContainsKey(threadId) || candidates.Count == 0) return null;
                var proposed = new KeyChoice(candidates[0].Entry.Id, candidates[0].Entry.Nickname, candidates[0].Secret);
                if (MoveAllowed(out var setting))
                {
                    next = Choose(proposed);
                    current = next;
                    anchor = next;
                    StopReason = null;
                    ContinuedOn = next.Nickname;
                    raised.Add(pool.Event(ApiKeyEventKind.Continued, threadId, failed, next, rejectedStatus, null, $"Continued on {next.Nickname}"));
                }
                else raised.Add(Withheld(setting, failed, proposed, $"{failed.Nickname} is out.", rejectedStatus));
            }
            foreach (var e in raised) pool.Raise(e);
            return next;
        }

        public void RecordSpend(double usd)
        {
            ApiKeyEvent? raised = null;
            lock (pool.gate)
            {
                if (current is not { } key) return;
                var now = pool.clock.GetUtcNow();
                pool.spend.Add(key.Id, usd, now);
                var entry = pool.store.Keys(pool.service).FirstOrDefault(k => k.Id == key.Id);
                if (entry is not null && pool.OverCap(entry, now) && pool.capped.Add((key.Id, pool.Month(now).Index)))
                    raised = pool.Event(ApiKeyEventKind.CapReached, threadId, key, null, null, null, $"{key.Nickname} reached its monthly cap.");
            }
            if (raised is not null) pool.Raise(raised);
        }

        // OD-0116: a forced move needs the user's yes (remembered, or once); the default is off.
        private bool MoveAllowed(out KeyMoveSetting setting)
        {
            setting = pool.moves?.Setting(threadId) ?? KeyMoveSetting.Unset;
            if (moveOnce)
            {
                moveOnce = false;
                return true;
            }
            return setting == KeyMoveSetting.Allow;
        }

        private ApiKeyEvent Withheld(KeyMoveSetting setting, KeyChoice from, KeyChoice proposed, string why, int? status)
        {
            if (setting == KeyMoveSetting.Deny)
            {
                StopReason = $"{why} Moving to another key is off for this thread.";
                return pool.Event(ApiKeyEventKind.MoveDeclined, threadId, from, proposed, status, null, StopReason);
            }
            StopReason = $"{why} Continue on {proposed.Nickname}?";
            return pool.Event(ApiKeyEventKind.MoveNeedsAnswer, threadId, from, proposed, status, null, StopReason);
        }

        private KeyChoice? Pick(List<ApiKeyEvent> raised)
        {
            var now = pool.clock.GetUtcNow();
            if (pool.pins.TryGetValue(threadId, out var pin)) return PickPinned(pin, now, raised);

            var candidates = pool.Candidates(now, tried);
            // Ranking a different key is also an automatic move for this existing thread.
            if (anchor is { } healthy && candidates.FirstOrDefault(candidate => candidate.Entry.Id == healthy.Id) is { Entry: not null } heldCandidate
                && (pool.moves?.Setting(threadId) ?? KeyMoveSetting.Unset) != KeyMoveSetting.Allow)
                return Choose(new(heldCandidate.Entry.Id, heldCandidate.Entry.Nickname, heldCandidate.Secret));
            if (anchor is { } held)
            {
                // OD-0116 (lead decision 2026-10-06): leaving the key a live thread is on is a forced move whether it ran out, hit its cap,
                // was turned off, lost its secret or was removed. The user's yes (remembered, or once) is needed first.
                var entry = pool.store.Keys(pool.service).FirstOrDefault(k => k.Id == held.Id);
                var gone = entry is null || !entry.Allowed || !pool.HasSecret(entry);
                if (gone || pool.IsOut(held.Id, now) || pool.OverCap(entry!, now))
                {
                    if (candidates.Count == 0)
                    {
                        raised.Add(pool.Event(ApiKeyEventKind.AllKeysOut, threadId, held, null, null, pool.EarliestReturn(now), "All keys are out."));
                        return null;
                    }
                    var proposed = new KeyChoice(candidates[0].Entry.Id, candidates[0].Entry.Nickname, candidates[0].Secret);
                    if (MoveAllowed(out var setting))
                    {
                        var moved = Choose(proposed);
                        ContinuedOn = moved.Nickname;
                        raised.Add(pool.Event(ApiKeyEventKind.Continued, threadId, held, moved, null, null, $"Continued on {moved.Nickname}"));
                        return moved;
                    }
                    var why = gone ? $"{held.Nickname} is off." : pool.IsOut(held.Id, now) ? $"{held.Nickname} is out." : $"{held.Nickname} reached its monthly cap.";
                    raised.Add(Withheld(setting, held, proposed, why, null));
                    return null;
                }
            }

            if (candidates.Count > 0) return Choose(new KeyChoice(candidates[0].Entry.Id, candidates[0].Entry.Nickname, candidates[0].Secret));
            raised.Add(pool.Event(ApiKeyEventKind.AllKeysOut, threadId, null, null, null, pool.EarliestReturn(now), "All keys are out."));
            return null;
        }

        private KeyChoice? PickPinned(string pin, DateTimeOffset now, List<ApiKeyEvent> raised)
        {
            var entry = pool.store.Keys(pool.service).FirstOrDefault(k => k.Id == pin);
            var named = entry is null ? null : new KeyChoice(entry.Id, entry.Nickname, "");
            if (entry is null || !entry.Allowed || !pool.HasSecret(entry))
            {
                StopReason = entry is null
                    ? "The key this thread is pinned to is gone. Unpin this thread to use another key."
                    : $"{entry.Nickname} is off. Unpin this thread to use another key.";
                raised.Add(pool.Event(ApiKeyEventKind.PinnedKeyOut, threadId, named, null, null, null, StopReason));
                return null;
            }
            if (pool.IsOut(pin, now))
            {
                StopReason = $"{entry.Nickname} is out. Unpin this thread to use another key.";
                raised.Add(pool.Event(ApiKeyEventKind.PinnedKeyOut, threadId, named, null, null, pool.outs.TryGetValue(pin, out var o) ? o.Until : null, StopReason));
                return null;
            }
            if (pool.OverCap(entry, now))
            {
                StopReason = $"{entry.Nickname} reached its monthly cap. Unpin this thread or raise the cap.";
                raised.Add(pool.Event(ApiKeyEventKind.PinnedKeyCapped, threadId, named, null, null, pool.Month(now).Next, StopReason));
                return null;
            }
            return Choose(new KeyChoice(entry.Id, entry.Nickname, pool.store.Secret(pin)!.Trim()));
        }

        private KeyChoice Choose(KeyChoice choice)
        {
            pool.uses[choice.Id] = pool.uses.GetValueOrDefault(choice.Id) + 1;
            return choice;
        }
    }

    private sealed class Reservation(ApiKeyPool pool, Guid thread, KeyChoice key, Guid request, DateTimeOffset at, Func<bool> owns) : IApiKeyReservation
    {
        private bool completed;
        public bool CanDispatch
        {
            get
            {
                lock (pool.gate)
                {
                    var entry = pool.store.Keys(pool.service).FirstOrDefault(item => item.Id == key.Id);
                    return !completed && owns() && entry is not null && pool.Usable(entry, pool.clock.GetUtcNow())
                        && (entry.MonthlyCapUsd is not { } cap || pool.BudgetSpend(key.Id, pool.Month(pool.clock.GetUtcNow()).Start) <= cap);
                }
            }
        }
        public void CancelBeforeDispatch()
        {
            lock (pool.gate)
            {
                if (completed) return;
                if (pool.spend is IApiKeyReservedSpend durable) durable.Cancel(request);
                pool.activeReservations.Remove(request);
                completed = true;
            }
        }
        public void Complete(double? actualUsd)
        {
            ApiKeyEvent? raised = null;
            lock (pool.gate)
            {
                if (completed) return;
                if (actualUsd is not { } usd || !double.IsFinite(usd) || usd < 0) { pool.activeReservations.Remove(request); completed = true; return; }
                if (pool.spend is IApiKeyReservedSpend durable) durable.Settle(request, usd);
                else pool.spend.Add(key.Id, usd, at);
                pool.activeReservations.Remove(request);
                completed = true;
                var now = pool.clock.GetUtcNow();
                var entry = pool.store.Keys(pool.service).FirstOrDefault(item => item.Id == key.Id);
                if (entry is not null && pool.OverCap(entry, now) && pool.capped.Add((key.Id, pool.Month(now).Index)))
                    raised = pool.Event(ApiKeyEventKind.CapReached, thread, key, null, null, null, $"{key.Nickname} reached its monthly cap.");
            }
            if (raised is not null) pool.Raise(raised);
        }
    }
}
