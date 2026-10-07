// PORT-ONLY: OD-0114; ED-403. Shared quota deltas are conservative estimates, including other activity.
using Marrow.Services.Port.Usage;

namespace Marrow.Services.Port.MultiAccount;

public sealed partial class HandshakeLimits
{
    private readonly Lock costGate = new();
    private readonly Dictionary<Guid, HandshakeCostInterval> measuring = [];

    public HandshakeCostObservation? BeginHandshakeCost(Guid id, Action? onEnded = null)
    {
        var before = Snapshot(id);
        if (before is null || Binding(before) is null || before.ObservedAt > clock.GetUtcNow()
            || clock.GetUtcNow() - before.ObservedAt > AccountLimitsScheduler.ActiveInterval) return null;
        HandshakeCostInterval interval;
        lock (costGate)
        {
            if (!measuring.TryGetValue(id, out interval!)) measuring[id] = interval = new(before);
            interval.Active++;
            interval.CompletedAt = null;
        }
        return new(completed =>
        {
            lock (costGate)
            {
                if (!measuring.TryGetValue(id, out var current) || !ReferenceEquals(interval, current)) return;
                if (!completed) measuring.Remove(id);
                else if (--interval.Active == 0) interval.CompletedAt = clock.GetUtcNow();
            }
            onEnded?.Invoke();
        });
    }

    public bool HasPendingHandshakeCost(Guid id) { lock (costGate) return measuring.ContainsKey(id); }
    public void CancelHandshakeCosts(Guid id) { lock (costGate) measuring.Remove(id); }
    public void ForgetHandshakeCosts(Guid id) { lock (costGate) { measuring.Remove(id); costs.Remove(id); } }

    // Only the app's already accepted shared snapshot reaches this method. A late response whose read began
    // during the transfer is not a post-transfer measurement; overlap is coalesced instead of double-counted.
    public bool ObserveHandshakeCosts(AccountPlanLimitsSnapshot after)
    {
        lock (costGate)
        {
            var changed = false;
            foreach (var pair in measuring.Where(pair => pair.Value.Before.Account == after.Account).ToArray())
            {
                var interval = pair.Value;
                if (!Compatible(interval.Before, after)) { measuring.Remove(pair.Key); changed = true; continue; }
                if (interval.Active != 0 || interval.CompletedAt is not { } end || after.ReadStartedAt <= end) continue;
                RecordCost(pair.Key, interval.Before, after);
                measuring.Remove(pair.Key);
                changed = true;
            }
            return changed;
        }
    }

    public double? HandshakeCostPercent(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id) || Binding(Snapshot(id)) is not { } window) return null;
        lock (costGate) return costs.TryGetValue(id, out var cost) && window.Id == cost.Window && window.ResetsAt == cost.Reset
            && window.DurationMinutes == cost.Duration && Snapshot(id)?.Limits?.PlanName == cost.Plan ? cost.Percent : null;
    }

    public void RecordHandshakeCost(Guid id, AccountPlanLimitsSnapshot before, AccountPlanLimitsSnapshot after)
    {
        if (before.Account != Key(id) || after.Account != Key(id) || !Compatible(before, after)) return;
        lock (costGate) RecordCost(id, before, after);
    }

    private bool Compatible(AccountPlanLimitsSnapshot before, AccountPlanLimitsSnapshot after)
    {
        var first = Binding(before);
        var last = Binding(after);
        return before.Account == after.Account && after.ObservedAt > before.ObservedAt && first is not null && last is not null
            && first.Id == last.Id && first.DurationMinutes == last.DurationMinutes && first.ResetsAt == last.ResetsAt
            && before.Limits?.PlanName == after.Limits?.PlanName && last.Percent >= first.Percent;
    }

    private void RecordCost(Guid id, AccountPlanLimitsSnapshot before, AccountPlanLimitsSnapshot after)
    {
        var first = Binding(before)!;
        var last = Binding(after)!;
        var percent = last.Percent - first.Percent;
        // Keep the larger observed bound within the same quota epoch; a cheaper later turn cannot cause a late start.
        if (costs.TryGetValue(id, out var previous) && previous.Window == last.Id && previous.Reset == last.ResetsAt
            && previous.Duration == last.DurationMinutes && previous.Plan == after.Limits?.PlanName) percent = Math.Max(percent, previous.Percent);
        costs[id] = (last.Id, last.ResetsAt, last.DurationMinutes, after.Limits?.PlanName, percent);
    }
}
