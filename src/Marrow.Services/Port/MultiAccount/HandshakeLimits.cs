// PORT-ONLY: OD-0114, OD-0116; ED-NEW-ma-ui. Joins MA-1 identities and MA-2 observations to MA-5.
using Marrow.Core.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed class HandshakeLimits(AccountStore accounts, AccountLimitsScheduler scheduler, UsageRateEstimator rates, TimeProvider clock,
    Func<Guid, bool>? canUseAccount = null) : IAccountLimits
{
    private readonly Dictionary<Guid, (string Window, DateTimeOffset? Reset, long? Duration, string? Plan, double Percent)> costs = [];
    private readonly Dictionary<Guid, (DateTimeOffset? Reset, DateTimeOffset ObservedAt)> hardLimits = [];

    public UsageAccountKey Key(Guid id)
    {
        var account = accounts.Get(id);
        return account.UsesLegacyCredentials ? UsageAccountKey.Default(account.Service) : new(account.Service, id.ToString("D"));
    }

    public AccountPlanLimitsSnapshot? Snapshot(Guid id) => scheduler.Snapshot(Key(id));

    public AccountUsage? Usage(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id)) return null;
        // A known key cap/out state is an availability guard, never a measured quota window.
        if (canUseAccount?.Invoke(id) == false) return new(100, null, IsOut: true);
        var snapshot = Snapshot(id);
        ReconcileHardLimit(id, snapshot);
        if (hardLimits.TryGetValue(id, out var hard))
        {
            if (hard.Reset is null || hard.Reset > clock.GetUtcNow()) return new(100, hard.Reset, IsOut: true);
            hardLimits.Remove(id);
        }
        var binding = Binding(snapshot);
        return binding is null ? null : new(binding.Percent, binding.ResetsAt);
    }

    public void NoteLimit(Guid id, DateTimeOffset? reset) => hardLimits[id] = (reset, clock.GetUtcNow());

    public void Observe(Guid thread, Guid id)
    {
        if (Snapshot(id) is not { } snapshot) return;
        rates.Observe(thread, snapshot);
        ReconcileHardLimit(id, snapshot);
    }

    // Reports belong to accounts even after their last thread has moved elsewhere.
    // Reconciliation never observes another thread's spending merely to revive this account.
    private void ReconcileHardLimit(Guid id, AccountPlanLimitsSnapshot? snapshot)
    {
        if (Binding(snapshot) is { Percent: < 100 } && hardLimits.TryGetValue(id, out var hard)
            && snapshot!.ObservedAt > hard.ObservedAt && (hard.Reset is null || snapshot.ObservedAt >= hard.Reset)) hardLimits.Remove(id);
    }

    public double? SpendPercentPerMinute(string accountId, Guid threadId)
    {
        if (!Guid.TryParse(accountId, out var id) || Binding(Snapshot(id)) is not { } window) return null;
        return rates.PercentPerMinute(threadId, Key(id), window.Id);
    }

    public double? HandshakeCostPercent(string accountId) => Guid.TryParse(accountId, out var id)
        && costs.TryGetValue(id, out var cost) && Binding(Snapshot(id)) is { } window
        && window.Id == cost.Window && window.ResetsAt == cost.Reset && window.DurationMinutes == cost.Duration
        && Snapshot(id)?.Limits?.PlanName == cost.Plan ? cost.Percent : null;

    // A reset, changed binding window or stale observation cannot manufacture a Handshake cost.
    public void RecordHandshakeCost(Guid id, AccountPlanLimitsSnapshot before, AccountPlanLimitsSnapshot after)
    {
        if (before.Account != Key(id) || after.Account != Key(id) || after.ObservedAt <= before.ObservedAt) return;
        var first = Binding(before);
        var last = Binding(after);
        if (first is null || last is null || first.Id != last.Id || first.DurationMinutes != last.DurationMinutes
            || first.ResetsAt != last.ResetsAt || before.Limits?.PlanName != after.Limits?.PlanName || last.Percent < first.Percent) return;
        costs[id] = (last.Id, last.ResetsAt, last.DurationMinutes, after.Limits?.PlanName, last.Percent - first.Percent);
    }

    private PlanLimits.Window? Binding(AccountPlanLimitsSnapshot? snapshot) => snapshot?.Limits?.Windows
        .Where(window => double.IsFinite(window.Percent) && window.Percent is >= 0 and <= 100
            && !(window.ResetsAt is { } reset && reset <= clock.GetUtcNow()))
        .OrderByDescending(window => window.Percent)
        .ThenByDescending(window => window.ResetsAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
}
