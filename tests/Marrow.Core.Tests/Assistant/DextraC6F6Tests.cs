using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Regression for the accepted Dextra C6-F6 finding. Offline only.
public sealed class DextraC6F6Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-6")]
    public void Reset_of_another_window_does_not_report_the_same_live_limit_twice()
    {
        var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        var longReset = now.AddDays(4);
        AccountWindowReading Long() => new("account", "Claude", "weekly", "weekly", 100, longReset);
        AccountWindowReading Short(double percent, DateTimeOffset reset) => new("account", "Claude", "short", "5-hour", percent, reset);
        Assert.Single(watcher.Observe([Long(), Short(90, now.AddMinutes(10))], now));
        var reset = Assert.Single(watcher.Observe([Long(), Short(0, now.AddHours(5))], now.AddMinutes(11)));
        Assert.Equal(FeedEventKind.AccountWindowReset, reset.Kind);
        Assert.Null(watcher.NoteLimit("account", "Claude", longReset, now.AddMinutes(12)));
    }
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-6")]
    public void Distinct_live_reset_episodes_survive_alternating_reports_and_an_early_reset()
    {
        var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        var shortReset = now.AddHours(3);
        var longReset = now.AddDays(4);
        AccountWindowReading Short(double percent, DateTimeOffset reset) => new("account", "Claude", "short", "5-hour", percent, reset);
        AccountWindowReading Long() => new("account", "Claude", "weekly", "weekly", 100, longReset);
        Assert.Equal(2, watcher.Observe([Long(), Short(100, shortReset)], now).Count);
        Assert.Null(watcher.NoteLimit("account", "Claude", longReset, now.AddMinutes(1)));
        Assert.Null(watcher.NoteLimit("account", "Claude", shortReset, now.AddMinutes(1)));
        Assert.Single(watcher.Observe([Long(), Short(0, now.AddHours(8))], now.AddMinutes(2)));
        Assert.Null(watcher.NoteLimit("account", "Claude", longReset, now.AddMinutes(3)));
        Assert.NotNull(watcher.NoteLimit("account", "Claude", shortReset, now.AddMinutes(3)));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-6")]
    public void An_unidentified_provider_limit_survives_an_unrelated_reset_with_a_lagging_snapshot()
    {
        var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        AccountWindowReading Weekly() => new("account", "Claude", "weekly", "weekly", 99, now.AddDays(4));
        AccountWindowReading Short(double percent, DateTimeOffset reset) => new("account", "Claude", "short", "5-hour", percent, reset);
        watcher.Observe([Weekly(), Short(90, now.AddMinutes(10))], now);
        Assert.NotNull(watcher.NoteLimit("account", "Claude", null, now));
        Assert.Single(watcher.Observe([Weekly(), Short(0, now.AddHours(5))], now.AddMinutes(11)));
        Assert.Null(watcher.NoteLimit("account", "Claude", null, now.AddMinutes(12)));
        Assert.NotNull(watcher.NoteLimit("account", "Claude", null, now.AddMinutes(31)));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-6")]
    public void A_shared_reset_episode_is_kept_until_both_limited_windows_reset()
    {
        var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        var reset = now.AddHours(3);
        AccountWindowReading Window(string id, double percent, DateTimeOffset resets) => new("account", "Claude", id, id, percent, resets);
        Assert.Single(watcher.Observe([Window("weekly", 100, reset), Window("short", 100, reset)], now));
        Assert.Single(watcher.Observe([Window("weekly", 100, reset), Window("short", 0, now.AddHours(8))], now.AddMinutes(1)));
        Assert.Null(watcher.NoteLimit("account", "Claude", reset, now.AddMinutes(2)));
        Assert.Single(watcher.Observe([Window("weekly", 0, now.AddDays(4)), Window("short", 0, now.AddHours(8))], now.AddMinutes(3)));
        Assert.NotNull(watcher.NoteLimit("account", "Claude", reset, now.AddMinutes(4)));
    }

    [Theory]
    [Trait("Decision", "ED-NEW-dextra-cx-6")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_known_provider_limit_survives_a_shared_reset_with_a_lagging_snapshot(bool resetUnknown)
    {
        var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        var reset = now.AddHours(3);
        AccountWindowReading Weekly() => new("account", "Claude", "weekly", "weekly", 99, resetUnknown ? null : reset);
        AccountWindowReading Short(double percent, DateTimeOffset resets) => new("account", "Claude", "short", "5-hour", percent, resets);
        watcher.Observe([Weekly(), Short(90, reset)], now);
        Assert.NotNull(watcher.NoteLimit("account", "Claude", reset, now));
        Assert.Single(watcher.Observe([Weekly(), Short(0, now.AddHours(8))], now.AddMinutes(1)));
        Assert.Null(watcher.NoteLimit("account", "Claude", reset, now.AddMinutes(2)));
    }

}
