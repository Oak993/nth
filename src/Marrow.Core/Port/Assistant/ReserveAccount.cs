// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5; unit AS-13; ED-NEW-dextra-as13-4). No Swift twin.
// One account as the reserve guard sees it: the last reading of each of its windows, the tokens the ledger saw since, the
// turns in flight on it, the recent burn, and the use the owner made outside nth. It answers one question: may another
// turn be sent without crossing a floor? No clock, no store: every call is given the time. Not thread-safe; the guard
// serialises its calls, and the simulation drives it from one thread.
namespace Marrow.Core.Port.Assistant;

/// <summary>One window checked for a turn: where it stands and how much room the floor leaves after the projection.</summary>
public sealed record ReserveWindowCheck(ReserveWindow Window, ReserveFloor Floor, double Estimate, double TurnP90, double Margin, double Projected)
{
    /// <summary>Points left under the floor after the projection; negative when the projection crosses it.</summary>
    public double Room => Floor.Percent - Projected;

    public bool Crosses => ReserveMath.Crosses(Projected, Floor.Percent);
}

/// <summary>
/// The answer of <see cref="ReserveAccount.Check"/>: <see cref="Go"/> when no window's projection crosses its floor.
/// <see cref="Binding"/> is the window with the least room (the tighter window binds), or null when no floor applies.
/// </summary>
public sealed record ReserveCheck(bool Go, ReserveWindowCheck? Binding, IReadOnlyList<ReserveWindowCheck> Windows)
{
    public static ReserveCheck Free { get; } = new(true, null, []);
}

/// <summary>What a batch of readings showed about one window.</summary>
public sealed record ReserveWindowChange(ReserveWindow Window, double Estimate, double OutsidePoints, bool Reset, ReserveWindow? Previous);

/// <summary>Use the owner made outside nth, found when a reading beat the estimate (11.5).</summary>
public sealed record ReserveOutsideUse(DateTimeOffset At, DateTimeOffset Since, string WindowId, double Points);

public sealed class ReserveAccount(string key, string name, string providerId, Func<string?, ReserveWindowKind, CalibratedRate> rates)
{
    /// <summary>The most spends kept between two readings; past it the older half is merged pairwise (an extreme case only; the estimate stays a bound).</summary>
    public const int MaxSpends = 4096;

    /// <summary>Outside use is remembered this long (the calibrator and the after-report read it).</summary>
    public static readonly TimeSpan OutsideKept = TimeSpan.FromDays(14);

    private sealed record SpendEntry(DateTimeOffset At, long Tokens, string? Model);

    private sealed class Flight(string? model, double p90Tokens)
    {
        public string? Model { get; } = model;
        public double P90Tokens { get; } = p90Tokens;
        public double SpentTokens { get; set; }
    }

    private sealed class Tracked(ReserveWindow last)
    {
        public ReserveWindow Last { get; set; } = last;
        public List<(DateTimeOffset At, double PerSecond)> Rises { get; } = [];
        public int Readings { get; set; } = 1;
    }

    private readonly Dictionary<string, Tracked> windows = new(StringComparer.Ordinal);
    private readonly List<SpendEntry> spends = [];
    private readonly Dictionary<Guid, Flight> flights = [];
    private readonly List<ReserveOutsideUse> outside = [];

    public string Key { get; } = key;

    public string Name { get; } = name;

    public string ProviderId { get; } = providerId;

    /// <summary>The window readings, as the last poll gave them.</summary>
    public IReadOnlyList<ReserveWindow> Windows => [.. windows.Values.Select(t => t.Last)];

    public ReserveWindow? Window(string id) => windows.TryGetValue(id, out var tracked) ? tracked.Last : null;

    /// <summary>The use outside nth found so far, oldest first.</summary>
    public IReadOnlyList<ReserveOutsideUse> OutsideUses => outside;

    /// <summary>Turns in flight on this account (across all missions).</summary>
    public int InFlight => flights.Count;

    // ---- readings ----

    /// <summary>
    /// A poll's readings replace the estimate (the reading is the truth, 11.5). For each window the estimate the ledger
    /// gave for that moment is compared with the reading: higher by more than 2 points is outside use. A window whose reset
    /// time moved on is reset: nothing before it counts.
    /// </summary>
    public IReadOnlyList<ReserveWindowChange> Observe(IEnumerable<ReserveWindow> readings)
    {
        var changes = new List<ReserveWindowChange>();
        foreach (var reading in readings)
        {
            if (!double.IsFinite(reading.Percent)) continue;
            var percent = Math.Clamp(reading.Percent, 0, 100);
            var window = reading with { Percent = percent };
            if (!windows.TryGetValue(window.Id, out var tracked))
            {
                windows[window.Id] = new Tracked(window);
                changes.Add(new ReserveWindowChange(window, percent, 0, false, null));
                continue;
            }
            var previous = tracked.Last;
            if (window.ObservedAt < previous.ObservedAt) continue; // a late answer to an older request
            var reset = IsReset(previous, window);
            var estimate = percent;
            var outsidePoints = 0.0;
            if (reset)
            {
                tracked.Rises.Clear();
            }
            else
            {
                estimate = ReserveMath.EstimateFromPoints(previous.Percent, PointsBetween(previous.ObservedAt, window.ObservedAt, window.Kind));
                outsidePoints = ReserveMath.OutsideUse(percent, estimate);
                if (outsidePoints > 0) AddOutside(new ReserveOutsideUse(window.ObservedAt, previous.ObservedAt, window.Id, outsidePoints));
                if (percent > previous.Percent)
                {
                    var span = window.ObservedAt - previous.ObservedAt;
                    if (span < ReserveDefaults.BurnMinSpan) span = ReserveDefaults.BurnMinSpan;
                    tracked.Rises.Add((window.ObservedAt, (percent - previous.Percent) / span.TotalSeconds));
                }
            }
            tracked.Last = window;
            tracked.Readings++;
            tracked.Rises.RemoveAll(r => window.ObservedAt - r.At > ReserveDefaults.BurnLookback);
            changes.Add(new ReserveWindowChange(window, estimate, outsidePoints, reset, previous));
        }
        PruneSpends();
        return changes;
    }

    // A window reset when its percent fell and its reset time moved on (or had already passed): the same rule the feed uses.
    private static bool IsReset(ReserveWindow previous, ReserveWindow current)
    {
        if (previous.Percent - current.Percent < AccountWindowWatcher.MinChange) return false;
        if (previous.ResetsAt is not { } before) return false;
        return before <= current.ObservedAt || (current.ResetsAt is { } after && after > before + AccountWindowWatcher.ResetSlack);
    }

    private void AddOutside(ReserveOutsideUse use)
    {
        outside.Add(use);
        outside.RemoveAll(o => use.At - o.At > OutsideKept);
    }

    // ---- the ledger ----

    /// <summary>The ledger saw <paramref name="tokens"/> spent on this account (by a turn, when <paramref name="turn"/> is in flight).</summary>
    public void Spend(DateTimeOffset at, long tokens, string? model, Guid? turn = null)
    {
        if (tokens <= 0) return;
        if (turn is { } id && flights.TryGetValue(id, out var flight)) flight.SpentTokens += tokens;
        // Spends of one model in the same second are one entry: a busy team does not grow the list between two polls.
        if (spends.Count > 0 && spends[^1] is var last && last.Model == model && SameSecond(last.At, at) && at >= last.At)
            spends[^1] = last with { At = at, Tokens = last.Tokens + tokens };
        else spends.Add(new SpendEntry(at, tokens, model));
        if (spends.Count > MaxSpends) Compact();
    }

    private static bool SameSecond(DateTimeOffset a, DateTimeOffset b) => (b - a).Duration() < TimeSpan.FromSeconds(1);

    /// <summary>A turn began on this account; its p90 in tokens counts in every margin until it ends (11.5).</summary>
    public void TurnStarted(Guid turn, string? model, double p90Tokens) => flights[turn] = new Flight(model, Math.Max(0, p90Tokens));

    public void TurnEnded(Guid turn) => flights.Remove(turn);

    /// <summary>Whether the turn is in flight here.</summary>
    public bool IsInFlight(Guid turn) => flights.ContainsKey(turn);

    // ---- the estimate, the margin ----

    /// <summary>The percent of one window now: its last reading plus the ledger's tokens since, rounded up (11.5).</summary>
    public double Estimate(string windowId, DateTimeOffset now)
    {
        if (!windows.TryGetValue(windowId, out var tracked)) return 0;
        return ReserveMath.EstimateFromPoints(tracked.Last.Percent, PointsBetween(tracked.Last.ObservedAt, now, tracked.Last.Kind));
    }

    /// <summary>The recent burn of one window in percent per second: the peak of its recent rises, never below one quantum per poll.</summary>
    public double BurnPerSecond(string windowId, DateTimeOffset now)
    {
        var peak = 0.0;
        if (windows.TryGetValue(windowId, out var tracked))
            foreach (var (at, perSecond) in tracked.Rises)
                if (now - at <= ReserveDefaults.BurnLookback && perSecond > peak) peak = perSecond;
        return Math.Max(peak, ReserveDefaults.MinimumBurnPerSecond);
    }

    /// <summary>The margin of one window now (11.5). <paramref name="remainingOnly"/> counts only what an in-flight turn has still to spend.</summary>
    public double Margin(string windowId, DateTimeOffset now, bool remainingOnly = false)
    {
        if (!windows.TryGetValue(windowId, out var tracked)) return ReserveMath.MinMargin;
        var kind = tracked.Last.Kind;
        var inFlight = flights.Values.Select(f =>
        {
            var tokens = remainingOnly ? Math.Max(0, f.P90Tokens - f.SpentTokens) : f.P90Tokens;
            return rates(f.Model, kind).PercentP90(tokens);
        });
        return ReserveMath.Margin(inFlight, now - tracked.Last.ObservedAt, BurnPerSecond(windowId, now));
    }

    /// <summary>
    /// May another turn be sent (11.5)? For each window a floor applies to, projected = estimate + this turn's p90 +
    /// margin; a window whose projection crosses its floor says no. <paramref name="newTurnP90Tokens"/> of 0 asks whether
    /// the account has room at all (a running mission's check between turns, counting only what its turns have left).
    /// </summary>
    public ReserveCheck Check(Func<ReserveWindow, ReserveFloor?> floorOf, string? model, double newTurnP90Tokens, DateTimeOffset now)
    {
        var checks = new List<ReserveWindowCheck>();
        foreach (var tracked in windows.Values)
        {
            var window = tracked.Last;
            if (floorOf(window) is not { } floor || floor.Percent >= 100) continue;
            var estimate = Estimate(window.Id, now);
            var turn = newTurnP90Tokens > 0 ? rates(model, window.Kind).PercentP90(newTurnP90Tokens) : 0;
            var margin = Margin(window.Id, now, remainingOnly: newTurnP90Tokens <= 0);
            checks.Add(new ReserveWindowCheck(window, floor, estimate, turn, margin, ReserveMath.Projected(estimate, turn, margin)));
        }
        if (checks.Count == 0) return ReserveCheck.Free;
        var binding = checks.OrderBy(c => c.Room).First();
        return new ReserveCheck(checks.All(c => !c.Crosses), binding, checks);
    }

    /// <summary>The use outside nth since <paramref name="since"/> (a mission's start), on one window or on all.</summary>
    public double OutsideSince(DateTimeOffset since, string? windowId = null) =>
        outside.Where(o => o.At > since && (windowId is null || o.WindowId == windowId)).Sum(o => o.Points);

    // ---- the ledger's conversion ----

    private double PointsBetween(DateTimeOffset from, DateTimeOffset to, ReserveWindowKind kind)
    {
        var points = 0.0;
        foreach (var s in spends)
        {
            if (s.At <= from || s.At > to) continue;
            var rate = rates(s.Model, kind).TokensPerPercent;
            if (!(rate > 0) || !double.IsFinite(rate)) return double.PositiveInfinity;
            points += s.Tokens / rate;
        }
        return points;
    }

    // Only spends after the oldest window's last reading can matter again.
    private void PruneSpends()
    {
        if (windows.Count == 0) return;
        var oldest = windows.Values.Min(t => t.Last.ObservedAt);
        spends.RemoveAll(s => s.At <= oldest);
    }

    // Past the cap the older half is merged pairwise. The merged spend takes the later time (counted as later, never as earlier,
    // which could drop it) and, when the two models differ, no model: the account's conservative rate then converts it.
    private void Compact()
    {
        var half = spends.Count / 2;
        var merged = new List<SpendEntry>(spends.Count - half / 2 + 1);
        var i = 0;
        for (; i + 1 < half; i += 2)
        {
            var a = spends[i];
            var b = spends[i + 1];
            merged.Add(new SpendEntry(b.At > a.At ? b.At : a.At, a.Tokens + b.Tokens, a.Model == b.Model ? a.Model : null));
        }
        for (; i < spends.Count; i++) merged.Add(spends[i]);
        spends.Clear();
        spends.AddRange(merged);
    }
}
