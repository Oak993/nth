using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeLimitsTests
{
    [Fact]
    public async Task An_unknown_reset_hard_stop_ignores_stale_health_but_recovers_after_a_new_confirmed_observation()
    {
        var accounts = new AccountStore(new InMemorySettingsStore());
        var account = accounts.ForService(ProviderKind.Codex)[0];
        var clock = new ManualTimeProvider();
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = 20, ResetsAt = clock.GetUtcNow().AddHours(4), DurationMinutes = 300 }]),
        }), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(accounts, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        clock.Advance(TimeSpan.FromSeconds(1));
        joined.NoteLimit(account.Id, null);
        joined.Observe(Guid.NewGuid(), account.Id);
        Assert.True(joined.Usage(account.Id.ToString("D"))!.IsOut);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.PlanChangedAsync(joined.Key(account.Id));
        joined.Observe(Guid.NewGuid(), account.Id);
        Assert.False(joined.Usage(account.Id.ToString("D"))!.IsOut);
        Assert.Equal(20, joined.Usage(account.Id.ToString("D"))!.PercentUsed);
    }

    [Fact]
    public async Task Guid_keys_preserve_legacy_reader_selection_and_the_binding_window_and_never_borrow_other_usage()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var legacy = store.ForService(ProviderKind.Claude)[0];
        var other = store.Add(ProviderKind.Claude);
        var clock = new ManualTimeProvider();
        var weeklyReset = clock.GetUtcNow().AddDays(2);
        using var scheduler = new AccountLimitsScheduler(clock, (key, _) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = new ValueList<PlanLimits.Window>([
                new() { Id = "five", Title = "5-hour limit", Percent = key.IsDefault ? 10 : 30, DurationMinutes = 300 },
                new() { Id = "week", Title = "Weekly", Percent = key.IsDefault ? 20 : 100, DurationMinutes = 10080, ResetsAt = weeklyReset }]),
        }), jitter: () => TimeSpan.Zero);
        var rates = new UsageRateEstimator();
        var joined = new HandshakeLimits(store, scheduler, rates, clock);
        Assert.True(joined.Key(legacy.Id).IsDefault);
        Assert.Equal(other.Id.ToString("D"), joined.Key(other.Id).AccountId);
        Assert.Null(joined.Usage(other.Id.ToString("D")));
        await scheduler.SignedInAsync(joined.Key(legacy.Id));
        await scheduler.SignedInAsync(joined.Key(other.Id));
        Assert.Equal(20, joined.Usage(legacy.Id.ToString("D"))!.PercentUsed);
        var exhausted = joined.Usage(other.Id.ToString("D"))!;
        Assert.True(exhausted.IsExhaustedAt(clock.GetUtcNow()));
        Assert.Equal(weeklyReset, exhausted.ResetsAt);
        Assert.Null(joined.HandshakeCostPercent(other.Id.ToString("D")));
        joined.RecordHandshakeCost(other.Id, new(joined.Key(other.Id), scheduler.Snapshot(joined.Key(other.Id))!.Limits, clock.GetUtcNow()),
            new(joined.Key(other.Id), new() { Windows = new ValueList<PlanLimits.Window>([new() { Id = "week", Title = "Weekly", Percent = 99, DurationMinutes = 10080, ResetsAt = weeklyReset }]) }, clock.GetUtcNow().AddMinutes(1)));
        Assert.Null(joined.HandshakeCostPercent(other.Id.ToString("D")));
    }

    [Fact]
    public async Task Two_exhausted_windows_do_not_revive_the_account_when_only_the_short_window_resets()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var account = store.Add(ProviderKind.Codex);
        var clock = new ManualTimeProvider();
        var now = clock.GetUtcNow();
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(new()
        {
            Windows = new ValueList<PlanLimits.Window>([
                new() { Id = "five", Title = "5-hour limit", Percent = 100, ResetsAt = now.AddHours(1), DurationMinutes = 300 },
                new() { Id = "week", Title = "Weekly", Percent = 100, ResetsAt = now.AddDays(2), DurationMinutes = 10080 }]),
        }), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(store, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        var usage = joined.Usage(account.Id.ToString("D"))!;
        Assert.Equal(now.AddDays(2), usage.ResetsAt);
        Assert.True(usage.IsExhaustedAt(now.AddHours(2)));
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("duration")]
    [InlineData("plan")]
    public async Task A_measured_cost_expires_when_the_current_window_epoch_duration_or_plan_changes(string changed)
    {
        var accounts = new AccountStore(new InMemorySettingsStore());
        var account = accounts.ForService(ProviderKind.Codex)[0];
        var clock = new ManualTimeProvider();
        var reset = clock.GetUtcNow().AddHours(4);
        PlanLimits limits = new() { PlanName = "Pro", Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = 24, ResetsAt = reset, DurationMinutes = 300 }]) };
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(limits), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(accounts, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        var before = scheduler.Snapshot(joined.Key(account.Id))!;
        joined.RecordHandshakeCost(account.Id, before with { Limits = limits with { Windows = new([limits.Windows[0] with { Percent = 20 }]) } }, before with { ObservedAt = before.ObservedAt.AddMinutes(1) });
        Assert.Equal(4, joined.HandshakeCostPercent(account.Id.ToString("D")));
        var window = limits.Windows[0];
        limits = limits with { PlanName = changed == "plan" ? "Max 20x" : limits.PlanName,
            Windows = new([window with { ResetsAt = changed == "reset" ? reset.AddHours(4) : reset, DurationMinutes = changed == "duration" ? 600 : 300 }]) };
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.PlanChangedAsync(joined.Key(account.Id));
        Assert.Null(joined.HandshakeCostPercent(account.Id.ToString("D")));
    }

    [Fact]
    public async Task Handshake_cost_is_an_observed_delta_and_changed_reset_epochs_or_accounts_cannot_replace_it()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var account = store.Add(ProviderKind.Codex);
        var other = store.Add(ProviderKind.Codex);
        var clock = new ManualTimeProvider();
        var reset = clock.GetUtcNow().AddHours(4);
        PlanLimits Report(double percent, DateTimeOffset at) => new()
        {
            PlanName = "Pro", Windows = new ValueList<PlanLimits.Window>([new() { Id = "five", Title = "5-hour limit", Percent = percent, ResetsAt = at, DurationMinutes = 300 }]),
        };
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(Report(24, reset)), jitter: () => TimeSpan.Zero);
        var joined = new HandshakeLimits(store, scheduler, new UsageRateEstimator(), clock);
        await scheduler.SignedInAsync(joined.Key(account.Id));
        var before = new AccountPlanLimitsSnapshot(joined.Key(account.Id), Report(20, reset), clock.GetUtcNow());
        var after = new AccountPlanLimitsSnapshot(joined.Key(account.Id), Report(24, reset), clock.GetUtcNow().AddMinutes(1));
        joined.RecordHandshakeCost(account.Id, before, after);
        Assert.Equal(4, joined.HandshakeCostPercent(account.Id.ToString("D")));
        joined.RecordHandshakeCost(account.Id, before, after with { Account = joined.Key(other.Id) });
        joined.RecordHandshakeCost(account.Id, before, after with { Limits = Report(99, reset.AddDays(7)) });
        Assert.Equal(4, joined.HandshakeCostPercent(account.Id.ToString("D")));
    }
}
