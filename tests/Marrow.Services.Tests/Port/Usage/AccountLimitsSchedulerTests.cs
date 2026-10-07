using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Usage;

public sealed class AccountLimitsSchedulerTests
{
    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Sign_in_active_polling_and_idle_stop_use_the_injected_clock()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Claude, "A");
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => { calls++; return Task.FromResult<PlanLimits?>(Limits(20)); }, jitter: () => TimeSpan.Zero);
        await scheduler.SignedInAsync(a);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(10));
        await scheduler.RefreshDueAsync();
        Assert.Equal(1, calls);
        scheduler.SetInUse(a, true);
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, calls);
        clock.Advance(TimeSpan.FromMinutes(3));
        await scheduler.RefreshDueAsync();
        Assert.Equal(3, calls);
        scheduler.SetInUse(a, false);
        clock.Advance(TimeSpan.FromMinutes(10));
        await scheduler.RefreshDueAsync();
        Assert.Equal(3, calls);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Near_handshake_limit_messages_and_plan_changes_refresh_with_a_60_second_ceiling()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Claude, "A");
        var percent = 94d;
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => { calls++; return Task.FromResult<PlanLimits?>(Limits(percent)); }, jitter: () => TimeSpan.Zero);
        scheduler.SetInUse(a, true);
        await scheduler.SignedInAsync(a);
        clock.Advance(TimeSpan.FromSeconds(59));
        await scheduler.LimitReachedAsync(a);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.RefreshDueAsync();
        Assert.Equal(3, calls);
        percent = 10;
        await scheduler.PlanChangedAsync(a);
        Assert.Null(scheduler.Snapshot(a));
        Assert.Equal(3, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.RefreshDueAsync();
        Assert.Equal(4, calls);
        Assert.Equal(10, scheduler.Snapshot(a)!.Limits!.Windows[0].Percent);
        Assert.Equal(clock.GetUtcNow().AddMinutes(3), scheduler.NextReadAt(a));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Concurrent_accounts_keep_their_own_snapshots_and_coalesce_inflight_reads()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Claude, "A");
        var b = new UsageAccountKey(ProviderKind.Claude, "B");
        var pending = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (key, _) => { calls++; return key == a ? pending.Task : Task.FromResult<PlanLimits?>(Limits(80)); }, jitter: () => TimeSpan.Zero);
        var first = scheduler.SignedInAsync(a);
        var duplicate = scheduler.LimitReachedAsync(a);
        await scheduler.SignedInAsync(b);
        Assert.Equal(2, calls);
        Assert.Equal(80, scheduler.Snapshot(b)!.Limits!.Windows[0].Percent);
        Assert.Null(scheduler.Snapshot(a));
        pending.SetResult(Limits(15));
        await Task.WhenAll(first, duplicate);
        Assert.Equal(15, scheduler.Snapshot(a)!.Limits!.Windows[0].Percent);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Signout_cancels_and_late_results_cannot_replace_the_new_generation()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Codex, "A");
        var pending = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        using var scheduler = new AccountLimitsScheduler(clock, (_, ct) => { token = ct; return pending.Task; }, jitter: () => TimeSpan.Zero);
        var read = scheduler.SignedInAsync(a);
        scheduler.SignedOut(a);
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(Limits(99));
        await read;
        Assert.Null(scheduler.Snapshot(a));
        Assert.Null(scheduler.NextReadAt(a));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Missing_reports_clear_old_limits_and_failures_back_off_instead_of_spinning()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Codex, "A");
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => Task.FromResult<PlanLimits?>(++calls == 1 ? Limits(40) : null), jitter: () => TimeSpan.Zero);
        scheduler.SetInUse(a, true);
        await scheduler.SignedInAsync(a);
        clock.Advance(TimeSpan.FromMinutes(3));
        await scheduler.RefreshDueAsync();
        Assert.Equal("limits not shared", scheduler.Snapshot(a)!.FiveHourStatus);
        var firstRetry = scheduler.NextReadAt(a)!.Value;
        var firstInterval = firstRetry - clock.GetUtcNow();
        clock.Advance(firstInterval);
        await scheduler.RefreshDueAsync();
        Assert.True(scheduler.NextReadAt(a)!.Value - clock.GetUtcNow() > firstInterval);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Server_retry_after_is_respected_by_limit_and_plan_triggers()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Zai, "A");
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => { calls++; throw new LimitsPollException(TimeSpan.FromMinutes(10)); }, jitter: () => TimeSpan.Zero);
        await scheduler.SignedInAsync(a);
        await scheduler.LimitReachedAsync(a);
        await scheduler.PlanChangedAsync(a);
        clock.Advance(TimeSpan.FromMinutes(9));
        await scheduler.RefreshDueAsync();
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Global_concurrency_is_bounded_and_disposal_stops_timers()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => { Interlocked.Increment(ref calls); return pending.Task; }, maxConcurrency: 1, jitter: () => TimeSpan.Zero);
        var first = scheduler.SignedInAsync(new(ProviderKind.Claude, "A"));
        var second = scheduler.SignedInAsync(new(ProviderKind.Claude, "B"));
        Assert.Equal(1, calls);
        pending.SetResult(Limits(10));
        await Task.WhenAll(first, second);
        Assert.Equal(2, calls);
        scheduler.Dispose();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task An_active_account_polls_from_timer_callbacks_without_a_manual_refresh()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Claude, "A");
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) =>
        {
            if (++calls == 2) polled.SetResult();
            return Task.FromResult<PlanLimits?>(Limits(10));
        }, jitter: () => TimeSpan.Zero);
        scheduler.SetInUse(a, true);
        await scheduler.SignedInAsync(a);
        Assert.True(clock.ActiveTimers > 0);
        clock.Advance(TimeSpan.FromMinutes(3));
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_late_signed_out_read_cannot_overwrite_a_resigned_in_account()
    {
        var clock = new ManualTimeProvider();
        var a = new UsageAccountKey(ProviderKind.Codex, "A");
        var old = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => ++calls == 1 ? old.Task : Task.FromResult<PlanLimits?>(Limits(15)), jitter: () => TimeSpan.Zero);
        var first = scheduler.SignedInAsync(a);
        scheduler.SignedOut(a);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.SignedInAsync(a);
        old.SetResult(Limits(99));
        await first;
        Assert.Equal(15, scheduler.Snapshot(a)!.Limits!.Windows[0].Percent);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_limit_during_a_read_keeps_the_earliest_permitted_followup_deadline()
    {
        var clock = new ManualTimeProvider();
        var account = new UsageAccountKey(ProviderKind.Claude, "A");
        var pending = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => pending.Task, jitter: () => TimeSpan.Zero);
        scheduler.SetInUse(account, true);
        var first = scheduler.SignedInAsync(account);
        var start = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromSeconds(10));
        var limit = scheduler.LimitReachedAsync(account);
        clock.Advance(TimeSpan.FromSeconds(20));
        pending.SetResult(Limits(10));
        await Task.WhenAll(first, limit);
        Assert.Equal(start.AddMinutes(1), scheduler.NextReadAt(account));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_service_embargo_survives_signout_and_signin_of_the_same_account()
    {
        var clock = new ManualTimeProvider();
        var account = new UsageAccountKey(ProviderKind.Zai, "A");
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => { calls++; throw new LimitsPollException(TimeSpan.FromMinutes(10)); }, jitter: () => TimeSpan.Zero);
        await scheduler.SignedInAsync(account);
        scheduler.SignedOut(account);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.SignedInAsync(account);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(9));
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_late_throttle_updates_the_successor_generations_deadline()
    {
        var clock = new ManualTimeProvider();
        var account = new UsageAccountKey(ProviderKind.Zai, "A");
        var old = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new AccountLimitsScheduler(clock, (_, _) => ++calls == 1 ? old.Task : Task.FromResult<PlanLimits?>(Limits(10)), jitter: () => TimeSpan.Zero);
        var first = scheduler.SignedInAsync(account);
        scheduler.SignedOut(account);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.SignedInAsync(account);
        scheduler.SetInUse(account, true);
        old.SetException(new LimitsPollException(TimeSpan.FromMinutes(10)));
        await first;
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.RefreshDueAsync();
        Assert.Equal(2, calls);
        Assert.True(scheduler.NextReadAt(account) >= clock.GetUtcNow().AddMinutes(9));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_port_trap_is_surfaced_instead_of_being_reported_as_unknown_quota()
    {
        using var scheduler = new AccountLimitsScheduler(new ManualTimeProvider(), (_, _) => throw new SwiftTrapException("Port invariant"), jitter: () => TimeSpan.Zero);
        await Assert.ThrowsAsync<SwiftTrapException>(() => scheduler.SignedInAsync(new(ProviderKind.Claude, "A")));
    }

    private static PlanLimits Limits(double percent) => new()
    {
        PlanName = "Pro",
        Windows = new ValueList<PlanLimits.Window>([new() { Id = "five", Title = "5-hour limit", Percent = percent, DurationMinutes = 300 }]),
    };
}
