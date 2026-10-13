using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-9): a notification channel that only knows decision notices, as a channel written before duty findings existed does; it shows
/// what the base class says about everything else. Helper code only (R-TEST-3).
/// </summary>
internal sealed class BareNotificationChannel : NotificationChannel
{
    public override string Name => "Bare";

    public override Task<bool> Send(DecisionNotice notice, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
