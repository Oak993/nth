using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeCreditRecoveryTests
{
    [Fact]
    public async Task An_unknown_credit_stop_recovers_only_from_a_new_usable_report_without_manufacturing_a_window()
    {
        var accounts = new AccountStore(new InMemorySettingsStore());
        var account = accounts.ForService(ProviderKind.Deepseek)[0];
        var clock = new ManualTimeProvider();
        var canUse = true;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = ValueList<PlanLimits.Window>.Empty, AccountCredits = new() { Total = 10, Currency = "USD", IsUsable = true },
        }), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(accounts, scheduler, new UsageRateEstimator(), clock, _ => canUse);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        clock.Advance(TimeSpan.FromSeconds(1));
        joined.NoteLimit(account.Id, null);
        Assert.True(joined.Usage(account.Id.ToString("D"))!.IsOut);
        clock.Advance(TimeSpan.FromMinutes(1));
        canUse = false;
        await scheduler.PlanChangedAsync(joined.Key(account.Id));
        Assert.True(joined.Usage(account.Id.ToString("D"))!.IsOut);
        canUse = true;
        Assert.Null(joined.Usage(account.Id.ToString("D")));
        Assert.Empty(joined.Snapshot(account.Id)!.Limits!.Windows);
    }
}
