using Marrow.Services.Port.Usage;

namespace Marrow.Services.Port.MultiAccount;

internal sealed class HandshakeCostInterval(AccountPlanLimitsSnapshot before)
{
    public AccountPlanLimitsSnapshot Before { get; } = before;
    public int Active { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
