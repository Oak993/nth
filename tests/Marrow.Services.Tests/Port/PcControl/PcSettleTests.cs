using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.11 item 4; critique C21). The settle after an action
// waits until its check holds and the window has been quiet for 100 ms, at most 3 s (8 s while busy), and every wait ends
// on a change event or a timeout, never a fixed sleep. In a batch a check that already holds returns at once unless a
// structure change or a new window arrived since the action. On the fake desktop the clock jumps to each event.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcSettleTests
{
    private const nint Window = 0x1a;

    private static (PcFakeDesktop Desktop, PcSettle Settle) Make()
    {
        var desktop = new PcFakeDesktop();
        return (desktop, new PcSettle(desktop, desktop.Clock));
    }

    private static Func<Task<bool>> Always(bool value) => () => Task.FromResult(value);

    [Fact]
    public async Task A_check_that_holds_in_a_quiet_window_returns_after_the_quiet_window()
    {
        var (desktop, settle) = Make();
        var result = await settle.Until(Window, 0, busy: false, skipQuiet: false, Always(true), CancellationToken.None);
        Assert.True(result.Held);
        Assert.False(result.TimedOut);
        Assert.Equal(PcSettle.QuietMs, result.ElapsedMs);
        Assert.Empty(desktop.Clock.Delays);
    }

    [Fact]
    public async Task Events_keep_the_window_busy_until_they_stop()
    {
        var (desktop, settle) = Make();
        for (var at = 50; at <= 400; at += 50) desktop.Schedule(at, () => desktop.Bump(Window));
        var result = await settle.Until(Window, 0, busy: false, skipQuiet: false, Always(true), CancellationToken.None);
        Assert.True(result.Held);
        Assert.Equal(400 + PcSettle.QuietMs, result.ElapsedMs);
    }

    [Fact]
    public async Task A_check_that_comes_true_with_a_later_event_returns_once_that_event_has_settled()
    {
        var (desktop, settle) = Make();
        var done = false;
        desktop.Schedule(800, () =>
        {
            done = true;
            desktop.Bump(Window);
        });
        var checks = 0;
        var result = await settle.Until(Window, 0, busy: false, skipQuiet: false, () =>
        {
            checks++;
            return Task.FromResult(done);
        }, CancellationToken.None);
        Assert.True(result.Held);
        Assert.Equal(800 + PcSettle.QuietMs, result.ElapsedMs);
        // It looked after the first quiet window and again once the app changed, not on a timer.
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task A_check_that_never_holds_gives_up_at_three_seconds_or_eight_while_busy()
    {
        var (_, settle) = Make();
        var result = await settle.Until(Window, 0, busy: false, skipQuiet: false, Always(false), CancellationToken.None);
        Assert.False(result.Held);
        Assert.True(result.TimedOut);
        Assert.Equal(PcSettle.CapMs, result.ElapsedMs);
        var (_, busy) = Make();
        var slow = await busy.Until(Window, 0, busy: true, skipQuiet: false, Always(false), CancellationToken.None);
        Assert.False(slow.Held);
        Assert.Equal(PcSettle.BusyCapMs, slow.ElapsedMs);
    }

    [Fact]
    public async Task An_app_that_never_goes_quiet_is_looked_at_by_the_cap()
    {
        var (desktop, settle) = Make();
        for (var at = 20; at <= 5000; at += 20) desktop.Schedule(at, () => desktop.Bump(Window));
        var result = await settle.Until(Window, 0, busy: false, skipQuiet: false, Always(true), CancellationToken.None);
        Assert.True(result.Held);
        Assert.Equal(PcSettle.CapMs, result.ElapsedMs);
    }

    [Fact]
    public async Task Skip_quiet_returns_at_once_when_the_check_holds_and_no_structure_changed()
    {
        var (desktop, settle) = Make();
        desktop.Bump(Window, structure: true);
        var seen = desktop.StructureVersion(Window);
        // A property event (the value just set) does not force the wait.
        desktop.Bump(Window);
        var result = await settle.Until(Window, seen, busy: false, skipQuiet: true, Always(true), CancellationToken.None);
        Assert.True(result.Held);
        Assert.Equal(0, result.ElapsedMs);
    }

    [Fact]
    public async Task Skip_quiet_still_waits_when_a_structure_change_or_window_arrived_since_the_action()
    {
        var (desktop, settle) = Make();
        var seen = desktop.StructureVersion(Window);
        desktop.Bump(Window, structure: true);
        var result = await settle.Until(Window, seen, busy: false, skipQuiet: true, Always(true), CancellationToken.None);
        Assert.True(result.Held);
        Assert.Equal(PcSettle.QuietMs, result.ElapsedMs);
        // A check that does not hold yet is never skipped either.
        var (later, wait) = Make();
        var done = false;
        later.Schedule(300, () =>
        {
            done = true;
            later.Bump(Window);
        });
        var slow = await wait.Until(Window, 0, busy: false, skipQuiet: true, () => Task.FromResult(done), CancellationToken.None);
        Assert.True(slow.Held);
        Assert.Equal(300 + PcSettle.QuietMs, slow.ElapsedMs);
    }

    [Fact]
    public async Task Quiet_waits_for_a_hundred_quiet_milliseconds_up_to_its_cap()
    {
        var (desktop, settle) = Make();
        desktop.Schedule(60, () => desktop.Bump(Window));
        var quiet = await settle.Quiet(Window, 2000, CancellationToken.None);
        Assert.True(quiet.Held);
        Assert.Equal(60 + PcSettle.QuietMs, quiet.ElapsedMs);
        var (noisy, never) = Make();
        for (var at = 30; at <= 3000; at += 30) noisy.Schedule(at, () => noisy.Bump(Window));
        var capped = await never.Quiet(Window, 1000, CancellationToken.None);
        Assert.False(capped.Held);
        Assert.True(capped.TimedOut);
        Assert.Equal(1000, capped.ElapsedMs);
    }

    [Fact]
    public async Task A_cancelled_settle_throws()
    {
        var (_, settle) = Make();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settle.Until(Window, 0, busy: false, skipQuiet: false, Always(false), cancel.Token));
    }
}
