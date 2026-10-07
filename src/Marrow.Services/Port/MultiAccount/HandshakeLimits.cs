// PORT-ONLY: OD-0114, OD-0116; ED-403. Joins MA-1 identities and MA-2 observations to MA-5.
using Marrow.Core.Accounts;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed partial class HandshakeLimits(AccountStore accounts, AccountLimitsScheduler scheduler, UsageRateEstimator rates, TimeProvider clock,
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

    public bool? IsAvailable(string accountId)
    {
        if (!Guid.TryParse(accountId, out var id)) return null;
        if (Usage(accountId) is { } usage) return !usage.IsExhaustedAt(clock.GetUtcNow());
        return HasUsableCredits(Snapshot(id)) ? true : null;
    }

    private static bool HasUsableCredits(AccountPlanLimitsSnapshot? snapshot) => snapshot?.Limits?.AccountCredits is
        { IsUsable: true, Total: var total } && double.IsFinite(total) && total > 0;

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
        if (hardLimits.TryGetValue(id, out var hard) && snapshot is not null && canUseAccount?.Invoke(id) != false
            && (Binding(snapshot) is { Percent: < 100 } || HasUsableCredits(snapshot))
            && snapshot.ReadStartedAt > hard.ObservedAt && (hard.Reset is null || snapshot.ReadStartedAt >= hard.Reset)) hardLimits.Remove(id);
    }

    public double? SpendPercentPerMinute(string accountId, Guid threadId)
    {
        if (!Guid.TryParse(accountId, out var id) || Binding(Snapshot(id)) is not { } window) return null;
        return rates.PercentPerMinute(threadId, Key(id), window.Id);
    }

    private PlanLimits.Window? Binding(AccountPlanLimitsSnapshot? snapshot) => snapshot?.Limits?.Windows
        .Where(window => double.IsFinite(window.Percent) && window.Percent is >= 0 and <= 100
            && !(window.ResetsAt is { } reset && reset <= clock.GetUtcNow()))
        .OrderByDescending(window => window.Percent)
        .ThenByDescending(window => window.ResetsAt ?? DateTimeOffset.MaxValue).FirstOrDefault();
}
