using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Usage;

// PORT-ONLY: OD-0114 / ED-406; an account-owned observation, including honest absence of quota data.
public sealed record AccountPlanLimitsSnapshot(UsageAccountKey Account, PlanLimits? Limits, DateTimeOffset ObservedAt)
{
    // A response arriving after a refusal cannot prove recovery if its request began before that refusal.
    public DateTimeOffset ReadStartedAt { get; init; } = ObservedAt;
    public bool LimitsShared => Limits is { Windows.Count: > 0 } || Limits?.PlanLimitsResetCredits is not null || Limits?.AccountCredits is not null;

    public string FiveHourStatus => !LimitsShared ? "limits not shared" :
        Limits!.Windows.Any(window => window.DurationMinutes == 300) ? "5-hour limit" : "no 5-hour window";
}
