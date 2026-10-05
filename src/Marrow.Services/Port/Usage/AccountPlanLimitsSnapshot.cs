using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Usage;

// PORT-ONLY: OD-0114 / ED-NEW-ma-2; an account-owned observation, including honest absence of quota data.
public sealed record AccountPlanLimitsSnapshot(UsageAccountKey Account, PlanLimits? Limits, DateTimeOffset ObservedAt)
{
    public bool LimitsShared => Limits is { Windows.Count: > 0 } || Limits?.PlanLimitsResetCredits is not null;

    public string FiveHourStatus => !LimitsShared ? "limits not shared" :
        Limits!.Windows.Any(window => window.DurationMinutes == 300) ? "5-hour limit" : "no 5-hour window";
}
