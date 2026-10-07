using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class SharedAccountRecoveryTests
{
    [Fact]
    public async Task A_new_valid_quota_report_recovers_an_unused_account_without_a_thread_usage_observation()
    {
        var accounts = new AccountStore(new InMemorySettingsStore());
        var account = accounts.ForService(Marrow.Core.Models.ProviderKind.Codex)[0];
        var clock = new ManualTimeProvider();
        PlanLimits? report = Healthy(clock);
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(report), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(accounts, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        clock.Advance(TimeSpan.FromSeconds(1));
        joined.NoteLimit(account.Id, null);
        Assert.True(joined.Usage(account.Id.ToString("D"))!.IsOut);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.PlanChangedAsync(joined.Key(account.Id));
        // No thread still uses this account. Recovery must not call Observe with another thread's spending.
        var recovered = joined.Usage(account.Id.ToString("D"))!;
        Assert.False(recovered.IsOut);
        Assert.Equal(20, recovered.PercentUsed);
        Assert.Null(joined.SpendPercentPerMinute(account.Id.ToString("D"), Guid.NewGuid()));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unknown")]
    [InlineData("invalid")]
    [InlineData("exhausted")]
    [InlineData("before-reset")]
    public async Task Recovery_keeps_a_hard_limit_when_health_is_stale_unknown_invalid_exhausted_or_before_the_reported_reset(string state)
    {
        var accounts = new AccountStore(new InMemorySettingsStore());
        var account = accounts.ForService(Marrow.Core.Models.ProviderKind.Codex)[0];
        var clock = new ManualTimeProvider();
        PlanLimits? report = Healthy(clock);
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(report), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(accounts, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        clock.Advance(TimeSpan.FromSeconds(1));
        var reset = state == "before-reset" ? clock.GetUtcNow().AddHours(1) : (DateTimeOffset?)null;
        joined.NoteLimit(account.Id, reset);
        if (state != "stale")
        {
            report = state switch
            {
                "unknown" => null,
                "invalid" => Healthy(clock) with { Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = double.NaN }]) },
                "exhausted" => Healthy(clock) with { Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = 100 }]) },
                _ => Healthy(clock),
            };
            clock.Advance(TimeSpan.FromMinutes(1));
            await scheduler.PlanChangedAsync(joined.Key(account.Id));
        }
        var usage = joined.Usage(account.Id.ToString("D"))!;
        Assert.True(usage.IsOut);
        Assert.Equal(reset, usage.ResetsAt);
    }

    private static PlanLimits Healthy(ManualTimeProvider clock) => new()
    {
        Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = 20, ResetsAt = clock.GetUtcNow().AddHours(4), DurationMinutes = 300 }]),
    };
}
