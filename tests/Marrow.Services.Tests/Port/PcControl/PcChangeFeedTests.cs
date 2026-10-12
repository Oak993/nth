using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.11; critique C22). The change feed counts each
// watched window's changes from its sources and wakes whoever waits on them: no polling and no fixed sleep. A dialog a
// watched process opens counts for that process's watched windows as a window change; a window shown anywhere counts for
// the Desktop pseudo-window while pc_launch_app waits on it; a window's subscriptions end 10 s after its last use.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcChangeFeedTests
{
    private static (PcChangeFeed Feed, PcFakeChangeSource Source, PcManualClock Clock) Make()
    {
        var clock = new PcManualClock();
        var source = new PcFakeChangeSource();
        return (new PcChangeFeed([source], clock), source, clock);
    }

    [Fact]
    public void Watching_subscribes_each_window_once_and_counts_its_changes()
    {
        var (feed, source, _) = Make();
        feed.Watch(0x1a, 101);
        feed.Watch(0x1a, 101);
        Assert.Single(source.Watches);
        Assert.Equal(((nint)0x1a, 101), source.Watches[0]);
        Assert.Equal(0, feed.Version(0x1a));
        source.Raise(0x1a, 0x1a, 101, PcChangeKind.Property);
        Assert.Equal(1, feed.Version(0x1a));
        Assert.Equal(0, feed.StructureVersion(0x1a));
        source.Raise(0x1a, 0x1a, 101, PcChangeKind.Structure);
        source.Raise(0x1a, 0x1a, 101, PcChangeKind.Window);
        Assert.Equal(3, feed.Version(0x1a));
        Assert.Equal(2, feed.StructureVersion(0x1a));
        // A window nobody watches has no count.
        Assert.Equal(0, feed.Version(0x2b));
        feed.Signal(0x2b, 202, PcChangeKind.Structure);
        Assert.Equal(0, feed.Version(0x2b));
    }

    [Fact]
    public void A_dialog_of_a_watched_process_counts_as_a_window_change_for_that_process_only()
    {
        var (feed, _, _) = Make();
        feed.Watch(0x1a, 101);
        feed.Watch(0x2b, 202);
        feed.Signal(0x5e, 101, PcChangeKind.Structure);
        Assert.Equal(1, feed.Version(0x1a));
        Assert.Equal(1, feed.StructureVersion(0x1a));
        Assert.Equal(0, feed.Version(0x2b));
        // A property change in the dialog stays a property change.
        feed.Signal(0x5e, 101, PcChangeKind.Property);
        Assert.Equal(2, feed.Version(0x1a));
        Assert.Equal(1, feed.StructureVersion(0x1a));
    }

    [Fact]
    public void A_window_shown_anywhere_counts_for_the_desktop_only_while_it_is_watched()
    {
        var (feed, source, _) = Make();
        feed.Signal(0x7f, 303, PcChangeKind.Window);
        Assert.Equal(0, feed.Version(PcChangeFeed.Desktop));
        feed.Watch(PcChangeFeed.Desktop, 0);
        Assert.Equal((PcChangeFeed.Desktop, 0), source.Watches[0]);
        feed.Signal(0x7f, 303, PcChangeKind.Window);
        Assert.Equal(1, feed.Version(PcChangeFeed.Desktop));
        Assert.Equal(1, feed.StructureVersion(PcChangeFeed.Desktop));
        // A property or structure change in some window is not a new window.
        feed.Signal(0x7f, 303, PcChangeKind.Property);
        feed.Signal(0x7f, 303, PcChangeKind.Structure);
        Assert.Equal(1, feed.Version(PcChangeFeed.Desktop));
        // A new window of a watched process counts for both.
        feed.Watch(0x1a, 101);
        feed.Signal(0x5e, 101, PcChangeKind.Window);
        Assert.Equal(2, feed.Version(PcChangeFeed.Desktop));
        Assert.Equal(1, feed.Version(0x1a));
    }

    [Fact]
    public async Task Changed_answers_at_once_for_a_change_already_counted_and_wakes_on_the_next_one()
    {
        var (feed, source, clock) = Make();
        feed.Watch(0x1a, 101);
        source.Raise(0x1a, 0x1a, 101, PcChangeKind.Property);
        Assert.True(await feed.Changed(0x1a, 0, 100, CancellationToken.None));
        var waiting = feed.Changed(0x1a, 1, 3000, CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(waiting));
        source.Raise(0x1a, 0x1a, 101, PcChangeKind.Property);
        Assert.True(await waiting);
        // The wake came from the event: no time passed on the clock.
        Assert.Equal(0, clock.NowMs);
    }

    [Fact]
    public async Task Changed_answers_false_when_the_timeout_passes_quietly_and_for_a_window_not_watched()
    {
        var (feed, _, clock) = Make();
        feed.Watch(0x1a, 101);
        Assert.False(await feed.Changed(0x1a, 0, 0, CancellationToken.None));
        var waiting = feed.Changed(0x1a, 0, 100, CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(waiting));
        clock.Advance(100);
        Assert.False(await waiting);
        Assert.False(await feed.Changed(0x2b, 0, 100, CancellationToken.None));
    }

    [Fact]
    public async Task A_cancelled_wait_throws_and_unwatching_wakes_a_waiter()
    {
        var (feed, source, _) = Make();
        feed.Watch(0x1a, 101);
        using var cancel = new CancellationTokenSource();
        var waiting = feed.Changed(0x1a, 0, 3000, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var second = feed.Changed(0x1a, 0, 3000, CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(second));
        feed.Unwatch(0x1a);
        Assert.False(await second);
        Assert.Equal(new nint[] { 0x1a }, source.Unwatches);
        Assert.False(source.Watching(0x1a));
    }

    [Fact]
    public async Task A_window_unused_for_ten_seconds_is_unsubscribed_and_each_use_restarts_the_ten_seconds()
    {
        var (feed, source, clock) = Make();
        feed.Watch(0x1a, 101);
        clock.Advance(6000);
        feed.Watch(0x1a, 101);
        clock.Advance(PcChangeFeed.IdleUnwatchMs - 6000);
        // The first ten seconds ended, but the window was used again at 6 s.
        await Task.Delay(50);
        Assert.True(source.Watching(0x1a));
        clock.Advance(6000);
        await PcTestWait.Until(() => !source.Watching(0x1a));
        Assert.Equal(0, feed.Version(0x1a));
        // Watching it again subscribes again.
        feed.Watch(0x1a, 101);
        Assert.Equal(2, source.Watches.Count);
    }

    [Fact]
    public void Disposing_ends_every_subscription()
    {
        var (feed, source, _) = Make();
        feed.Watch(0x1a, 101);
        feed.Watch(0x2b, 202);
        feed.Dispose();
        Assert.False(source.Watching(0x1a));
        Assert.False(source.Watching(0x2b));
        Assert.Equal(2, source.Unwatches.Count);
    }
}
