using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Port.Usage;

public sealed class UsageRateEstimatorTests
{
    [Fact]
    [Trait("Decision", "ED-406")]
    public void Rates_are_separate_for_each_thread_account_and_service_window()
    {
        var estimator = new UsageRateEstimator();
        var thread = Guid.NewGuid();
        var otherThread = Guid.NewGuid();
        var a = new UsageAccountKey(ProviderKind.Claude, "A");
        var b = new UsageAccountKey(ProviderKind.Claude, "B");
        var now = DateTimeOffset.UnixEpoch;
        estimator.Observe(thread, new(a, Limits(10, 20), now));
        estimator.Observe(otherThread, new(a, Limits(10, 20), now));
        estimator.Observe(thread, new(b, Limits(40, 60), now));
        estimator.Observe(thread, new(a, Limits(16, 22), now.AddMinutes(2)));
        Assert.Equal(3, estimator.PercentPerMinute(thread, a, "five"));
        Assert.Equal(1, estimator.PercentPerMinute(thread, a, "weekly"));
        Assert.Null(estimator.PercentPerMinute(otherThread, a, "five"));
        Assert.Null(estimator.PercentPerMinute(thread, b, "five"));
        Assert.Null(estimator.PercentPerMinute(thread, a, "missing"));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void Resets_missing_data_and_out_of_order_samples_cannot_create_a_false_spending_rate()
    {
        var estimator = new UsageRateEstimator();
        var thread = Guid.NewGuid();
        var a = new UsageAccountKey(ProviderKind.Codex, "A");
        var now = DateTimeOffset.UnixEpoch;
        estimator.Observe(thread, new(a, Limits(70, 10), now));
        estimator.Observe(thread, new(a, Limits(80, 20), now));
        Assert.Null(estimator.PercentPerMinute(thread, a, "five"));
        estimator.Observe(thread, new(a, Limits(76, 12), now.AddMinutes(2)));
        Assert.Equal(3, estimator.PercentPerMinute(thread, a, "five"));
        estimator.Observe(thread, new(a, Limits(99, 99), now.AddMinutes(1)));
        Assert.Equal(3, estimator.PercentPerMinute(thread, a, "five"));
        estimator.Observe(thread, new(a, Limits(2, 13), now.AddMinutes(3)));
        Assert.Null(estimator.PercentPerMinute(thread, a, "five"));
        estimator.Observe(thread, new(a, null, now.AddMinutes(4)));
        Assert.Null(estimator.PercentPerMinute(thread, a, "weekly"));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void Credits_removed_windows_and_new_reset_epochs_use_separate_baselines()
    {
        var estimator = new UsageRateEstimator();
        var thread = Guid.NewGuid();
        var a = new UsageAccountKey(ProviderKind.Zai, "A");
        var now = DateTimeOffset.UnixEpoch;
        PlanLimits Credit(double percent, DateTimeOffset? reset = null) => new()
        {
            Windows = new ValueList<PlanLimits.Window>([new() { Id = "credits", Title = "Credits", Percent = percent, ResetsAt = reset }]),
        };
        estimator.Observe(thread, new(a, Credit(10, now.AddDays(1)), now));
        estimator.Observe(thread, new(a, Credit(20, now.AddDays(1)), now.AddMinutes(2)));
        Assert.Equal(5, estimator.PercentPerMinute(thread, a, "credits"));
        estimator.Observe(thread, new(a, Credit(25, now.AddDays(2)), now.AddMinutes(3)));
        Assert.Null(estimator.PercentPerMinute(thread, a, "credits"));
        estimator.Observe(thread, new(a, new PlanLimits { Windows = ValueList<PlanLimits.Window>.Empty }, now.AddMinutes(4)));
        estimator.Observe(thread, new(a, Credit(30), now.AddMinutes(5)));
        Assert.Null(estimator.PercentPerMinute(thread, a, "credits"));
    }

    [Theory]
    [Trait("Decision", "ED-406")]
    [InlineData("Pro", 95)]
    [InlineData("Plus", 95)]
    [InlineData("$20", 95)]
    [InlineData("Max (5x)", 98)]
    [InlineData("Max (10x)", 98)]
    [InlineData("Max (20x)", 98)]
    [InlineData("Pro 20x", 98)]
    [InlineData(null, 95)]
    public void No_data_uses_owner_defaults_without_creating_quota_windows(string? plan, double expected) =>
        Assert.Equal(expected, UsageRateEstimator.DefaultHandshakePercent(plan));

    private static PlanLimits Limits(double five, double weekly) => new()
    {
        Windows = new ValueList<PlanLimits.Window>([
            new() { Id = "five", Title = "5-hour limit", Percent = five, DurationMinutes = 300 },
            new() { Id = "weekly", Title = "Weekly", Percent = weekly, DurationMinutes = 10080 },
        ]),
    };
}
