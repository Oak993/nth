// PORT-ONLY: OD-0114, OD-0116; ED-NEW-ma-ui. Joins MA-1 identities and MA-2 observations to MA-5.
using Marrow.Core.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed class HandshakeLimits(AccountStore accounts, AccountLimitsScheduler scheduler, UsageRateEstimator rates, TimeProvider clock) : IAccountLimits
{
    private readonly Dictionary<Guid, (string Window, double Percent)> costs = [];
    private readonly Dictionary<Guid, DateTimeOffset?> hardLimits = [];

    public UsageAccountKey Key(Guid id)
    {
        var account = accounts.Get(id);
        return account.UsesLegacyCredentials ? UsageAccountKey.Default(account.Service) : new(account.Service, id.ToString("D"));
    }

    public AccountPlanLimitsSnapshot? Snapshot(Guid id) => scheduler.Snapshot(Key(id));

    public AccountUsage? Usage(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id)) return null;
        if (hardLimits.TryGetValue(id, out var reset))
        {
            if (reset is null || reset > clock.GetUtcNow()) return new(100, reset, IsOut: true);
            hardLimits.Remove(id);
        }
        var binding = Binding(Snapshot(id));
        return binding is null ? null : new(binding.Percent, binding.ResetsAt);
    }

    public void NoteLimit(Guid id, DateTimeOffset? reset) => hardLimits[id] = reset;

    public void Observe(Guid thread, Guid id)
    {
        if (Snapshot(id) is not { } snapshot) return;
        rates.Observe(thread, snapshot);
        if (Binding(snapshot) is { Percent: < 100 } && hardLimits.TryGetValue(id, out var reset)
            && reset is { } at && snapshot.ObservedAt >= at) hardLimits.Remove(id);
    }

    public double? SpendPercentPerMinute(string accountId, Guid threadId)
    {
        if (!Guid.TryParse(accountId, out var id) || Binding(Snapshot(id)) is not { } window) return null;
        return rates.PercentPerMinute(threadId, Key(id), window.Id);
    }

    public double? HandshakeCostPercent(string accountId) => Guid.TryParse(accountId, out var id)
        && costs.TryGetValue(id, out var cost) && Binding(Snapshot(id))?.Id == cost.Window ? cost.Percent : null;

    // A reset, changed binding window or stale observation cannot manufacture a Handshake cost.
    public void RecordHandshakeCost(Guid id, AccountPlanLimitsSnapshot before, AccountPlanLimitsSnapshot after)
    {
        if (before.Account != Key(id) || after.Account != Key(id) || after.ObservedAt <= before.ObservedAt) return;
        var first = Binding(before);
        var last = Binding(after);
        if (first is null || last is null || first.Id != last.Id || first.DurationMinutes != last.DurationMinutes
            || first.ResetsAt != last.ResetsAt || before.Limits?.PlanName != after.Limits?.PlanName || last.Percent < first.Percent) return;
        costs[id] = (last.Id, last.Percent - first.Percent);
    }

    private PlanLimits.Window? Binding(AccountPlanLimitsSnapshot? snapshot) => snapshot?.Limits?.Windows
        .Where(window => double.IsFinite(window.Percent) && window.Percent is >= 0 and <= 100
            && !(window.ResetsAt is { } reset && reset <= clock.GetUtcNow()))
        .OrderByDescending(window => window.Percent)
        .ThenByDescending(window => window.ResetsAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
}
