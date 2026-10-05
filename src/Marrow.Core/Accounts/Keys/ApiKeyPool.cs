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
    private readonly Lock gate = new();
    private readonly Dictionary<string, (DateTimeOffset Until, OutKind Kind)> outs = [];
    private readonly Dictionary<string, int> uses = [];
    private readonly Dictionary<Guid, string> pins = [];
    private readonly Dictionary<Guid, Rotation> rotations = [];
    private readonly HashSet<(string Key, int Month)> capped = [];

    public ApiKeyPool(string service, IApiKeyStore store, IApiKeyCredits credits, IApiKeySpend spend, TimeProvider clock, ApiKeyPoolOptions? options = null)
    {
        this.service = service;
        this.store = store;
        this.credits = credits;
        this.spend = spend;
        this.clock = clock;
        settings = options ?? new ApiKeyPoolOptions();
    }

    public event EventHandler<ApiKeyEvent>? Changed;

    public IApiKeyRotation ForThread(Guid threadId)
    {
        lock (gate)
        {
            if (!rotations.TryGetValue(threadId, out var rotation)) rotations[threadId] = rotation = new Rotation(this, threadId);
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

    public PoolState State()
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            var available = Candidates(now, []).Count;
            return available > 0 ? new PoolState(false, null, available) : new PoolState(true, EarliestReturn(now), 0);
        }
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

    private bool Usable(ApiKeyEntry entry, DateTimeOffset now)
    {
        if (!entry.Allowed || string.IsNullOrWhiteSpace(store.Secret(entry.Id))) return false;
        if (IsOut(entry.Id, now)) return false;
        return !(entry.MonthlyCapUsd is { } cap && spend.MonthSpend(entry.Id, now) >= cap);
    }

    private bool IsOut(string keyId, DateTimeOffset now)
    {
        if (!outs.TryGetValue(keyId, out var state)) return false;
        // An out-of-credits key is back as soon as the service reports credits again.
        if (state.Kind == OutKind.Credits && credits.CreditsLeft(keyId) is > 0) { outs.Remove(keyId); return false; }
        if (state.Until > now) return true;
        outs.Remove(keyId);
        return false;
    }

    /// <summary>The earliest moment an allowed key with a secret can take a request: its out time or the start of next month for a capped one. Null when none is known.</summary>
    private DateTimeOffset? EarliestReturn(DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var entry in store.Keys(service))
        {
            if (!entry.Allowed || string.IsNullOrWhiteSpace(store.Secret(entry.Id))) continue;
            DateTimeOffset? back = null;
            if (outs.TryGetValue(entry.Id, out var state) && state.Until > now) back = state.Until;
            if (entry.MonthlyCapUsd is { } cap && spend.MonthSpend(entry.Id, now) >= cap)
            {
                var next = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
                back = back is { } b && b > next ? b : next;
            }
            if (back is { } when && (earliest is null || when < earliest)) earliest = when;
        }
        return earliest;
    }

    private ApiKeyEvent Event(ApiKeyEventKind kind, Guid thread, KeyChoice? from, KeyChoice? to, int? status, DateTimeOffset? returns, string message) =>
        new(kind, service, thread, from?.Id, from?.Nickname, to?.Id, to?.Nickname, status, clock.GetUtcNow(), returns, message);

    private void Raise(ApiKeyEvent e) => Changed?.Invoke(this, e);

    private sealed class Rotation(ApiKeyPool pool, Guid threadId) : IApiKeyRotation
    {
        private readonly HashSet<string> tried = [];
        private KeyChoice? current;

        public KeyChoice? Current()
        {
            ApiKeyEvent? raised = null;
            KeyChoice? choice;
            lock (pool.gate)
            {
                tried.Clear();
                choice = Pick(out raised);
                current = choice;
            }
            if (raised is not null) pool.Raise(raised);
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
                pool.outs[failed.Id] = (until, kind);
                tried.Add(failed.Id);

                if (pool.pins.ContainsKey(threadId))
                {
                    current = null;
                    raised.Add(pool.Event(ApiKeyEventKind.PinnedKeyOut, threadId, failed, null, status, until, $"{failed.Nickname} is out. This thread is pinned to it."));
                }
                else
                {
                    next = Pick(out _);
                    if (next is not null)
                    {
                        current = next;
                        raised.Add(pool.Event(ApiKeyEventKind.Continued, threadId, failed, next, status, null, $"Continued on {next.Nickname}"));
                    }
                    else
                    {
                        current = null;
                        raised.Add(pool.Event(ApiKeyEventKind.AllKeysOut, threadId, failed, null, status, pool.EarliestReturn(now), "All keys are out."));
                    }
                }
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
                if (entry?.MonthlyCapUsd is { } cap && pool.spend.MonthSpend(key.Id, now) >= cap
                    && pool.capped.Add((key.Id, now.UtcDateTime.Year * 12 + now.UtcDateTime.Month)))
                {
                    raised = pool.Event(ApiKeyEventKind.CapReached, threadId, key, null, null, null, $"{key.Nickname} reached its monthly cap.");
                }
            }
            if (raised is not null) pool.Raise(raised);
        }

        private KeyChoice? Pick(out ApiKeyEvent? raised)
        {
            raised = null;
            var now = pool.clock.GetUtcNow();
            if (pool.pins.TryGetValue(threadId, out var pin))
            {
                var entry = pool.store.Keys(pool.service).FirstOrDefault(k => k.Id == pin);
                if (entry is null || !entry.Allowed || string.IsNullOrWhiteSpace(pool.store.Secret(pin)) || pool.IsOut(pin, now)) return null;
                return Choose(entry, pool.store.Secret(pin)!.Trim());
            }
            var candidates = pool.Candidates(now, tried);
            if (candidates.Count > 0) return Choose(candidates[0].Entry, candidates[0].Secret);
            if (tried.Count == 0) raised = pool.Event(ApiKeyEventKind.AllKeysOut, threadId, null, null, null, pool.EarliestReturn(now), "All keys are out.");
            return null;
        }

        private KeyChoice Choose(ApiKeyEntry entry, string secret)
        {
            pool.uses[entry.Id] = pool.uses.GetValueOrDefault(entry.Id) + 1;
            return new KeyChoice(entry.Id, entry.Nickname, secret);
        }
    }
}
