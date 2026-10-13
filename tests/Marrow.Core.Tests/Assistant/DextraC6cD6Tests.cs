using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

public sealed class DextraC6cD6Tests
{
    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-6")]
    public void A_stale_other_window_cannot_keep_a_still_unexpired_shared_episode_alive()
    {
        using var watcher = new AccountWindowWatcher();
        var now = FeedSamples.Start;
        var reset = now.AddSeconds(60);
        AccountWindowReading A(double percent, DateTimeOffset resets) => new("a", "A", "first", "first", percent, resets);
        AccountWindowReading B() => new("a", "A", "other", "other", 100, reset.AddSeconds(-30));
        Assert.Single(watcher.Observe([A(100, reset), B()], now)); // Within the one-minute shared reset slack.
        Assert.Null(watcher.NoteLimit("a", "A", reset, now.AddSeconds(20)));
        var observed = now.AddSeconds(50);
        Assert.True(B().ResetsAt <= observed);
        Assert.True(reset > observed.AddSeconds(1)); // Expiry pruning cannot decide this boundary for us.
        Assert.Equal(FeedEventKind.AccountWindowReset,
            Assert.Single(watcher.Observe([A(0, now.AddHours(8)), B()], observed)).Kind);
        Assert.Equal(FeedEventKind.AccountLimitReached,
            Assert.IsType<FeedDraft>(watcher.NoteLimit("a", "A", reset, observed.AddSeconds(1))).Kind);
    }
}
