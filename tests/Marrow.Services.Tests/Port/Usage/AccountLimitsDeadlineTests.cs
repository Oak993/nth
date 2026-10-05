using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Usage;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountLimitsDeadlineTests
{
    [Fact]
    public async Task Reapplying_the_same_use_and_handshake_settings_keeps_the_existing_poll_deadline()
    {
        var clock = new ManualTimeProvider();
        var key = UsageAccountKey.Default(ProviderKind.Claude);
        var reads = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) =>
        {
            reads++;
            return Task.FromResult<PlanLimits?>(new() { Windows = new([new() { Id = "reported", Title = "Reported", Percent = 20 }]) });
        }, jitter: () => TimeSpan.Zero);
        scheduler.SetInUse(key, true);
        await scheduler.SignedInAsync(key);
        var deadline = scheduler.NextReadAt(key);
        clock.Advance(TimeSpan.FromMinutes(1));
        scheduler.SetInUse(key, true);
        scheduler.SetHandshakePercent(key, null);
        Assert.Equal(deadline, scheduler.NextReadAt(key));
        clock.Advance(TimeSpan.FromMinutes(2));
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, reads);
    }
}
