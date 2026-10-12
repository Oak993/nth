using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7: the production frame source (AvaloniaFrameSource) itself, on a headless window with a fake platform
// (FakeFrameHost: the kernel timer and IsIconic/DWMWA_CLOAKED answered by the test) and a manual clock. Holding back,
// arming, re-arming, cancelling, the wake-up, parking, the 100 ms look and its cache are the production code; a render tick
// is a display refresh. The equivalence runs compare the draw instants of slow loops on the source with a precise timer
// against the same source with none (which holds nothing back, as before ED-NEW-lean-7).
[Collection("Headless")]
public sealed class FrameClockProductionSourceTests(HeadlessSession headless)
{
    private static readonly TimeSpan Refresh240 = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 240);

    private static void Tick(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Window ShownWindow()
    {
        var window = new Window { Width = 200, Height = 100, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_held_request_arms_the_timer_for_its_wake_up_asks_nothing_before_it_and_its_frame_after_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = Refresh240 };
        var frames = new AvaloniaFrameSource(window, host);
        Assert.Equal(Refresh240, frames.WakePeriod);
        var ran = 0;
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), _ => ran++);
        Assert.Equal(1, frames.Pending);
        Assert.Equal([TimeSpan.FromMilliseconds(50)], host.Precise!.Arms);
        Tick(3);
        Assert.Equal(0, ran); // nothing asked for
        runtime.Advance(TimeSpan.FromMilliseconds(40));
        host.Precise.Fire(); // a wake-up 10 ms early: nothing is due, the timer is armed again for what is left
        Assert.Equal(TimeSpan.FromMilliseconds(10), host.Precise.Arms[^1]);
        Tick(2);
        Assert.Equal(0, ran);
        runtime.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, host.Precise.FireIfDue());
        Assert.Equal(0, ran); // asked for, answered at the next frame, never inside the wake-up
        Tick();
        Assert.Equal(1, ran);
        Tick(3);
        Assert.Equal(1, ran);
        Assert.Equal(0, frames.Pending);
        Assert.Null(host.Precise.Due);
        Assert.Equal(2, host.Precise.Arms.Count);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Held_requests_wake_in_order_the_timer_follows_the_earliest_and_a_withdrawn_one_never_runs() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = Refresh240 };
        var frames = new AvaloniaFrameSource(window, host);
        var order = new List<string>();
        var start = SwiftClock.Now;
        Action<TimeSpan> a = _ => order.Add("a"), b = _ => order.Add("b"), c = _ => order.Add("c");
        frames.RequestFrameAt(start + TimeSpan.FromMilliseconds(30), a);
        frames.RequestFrameAt(start + TimeSpan.FromMilliseconds(10), b); // earlier: armed again, earlier
        frames.RequestFrameAt(start + TimeSpan.FromMilliseconds(20), c); // later than armed: the timer stays
        Assert.Equal([TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(10)], host.Precise!.Arms);
        frames.CancelFrame(b);
        Assert.Equal(2, frames.Pending);
        runtime.Advance(TimeSpan.FromMilliseconds(10));
        host.Precise.FireIfDue(); // b's wake-up with b withdrawn: nothing asked, armed for c
        Tick();
        Assert.Empty(order);
        Assert.Equal(TimeSpan.FromMilliseconds(10), host.Precise.Arms[^1]);
        runtime.Advance(TimeSpan.FromMilliseconds(10));
        host.Precise.FireIfDue();
        Tick();
        Assert.Equal(["c"], order);
        Assert.Equal(TimeSpan.FromMilliseconds(10), host.Precise.Arms[^1]);
        runtime.Advance(TimeSpan.FromMilliseconds(10));
        host.Precise.FireIfDue();
        Tick();
        Assert.Equal(["c", "a"], order);
        Assert.Equal(0, frames.Pending);
        Assert.Null(host.Precise.Due);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_request_due_within_half_a_millisecond_or_without_a_precise_timer_is_asked_for_at_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = Refresh240, HasPreciseTimer = false };
        var frames = new AvaloniaFrameSource(window, host);
        Assert.Null(frames.WakePeriod); // no precise timer: nothing is held back
        var ran = 0;
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), _ => ran++);
        Tick();
        Assert.Equal(1, ran);
        Assert.Empty(host.Coarse!.Arms); // the coarse timer only looks at the windows

        var precise = new FakeFrameHost { Period = Refresh240 };
        var held = new AvaloniaFrameSource(window, precise);
        held.RequestFrameAt(SwiftClock.Now + TimeSpan.FromTicks(4000), _ => ran++); // 0.4 ms away
        Tick();
        Assert.Equal(2, ran);
        Assert.Empty(precise.Precise!.Arms);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_look_at_the_windows_is_trusted_for_100_ms_and_redone_at_once_when_a_window_changes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = Refresh240 };
        var frames = new AvaloniaFrameSource(window, host);
        for (var i = 0; i < 3; i++) frames.RequestFrameAt(null, _ => { });
        Assert.Equal(1, host.Looks);
        runtime.Advance(TimeSpan.FromMilliseconds(99));
        frames.RequestFrameAt(null, _ => { });
        Assert.Equal(1, host.Looks);
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        frames.RequestFrameAt(null, _ => { });
        Assert.Equal(2, host.Looks);
        window.WindowState = WindowState.Minimized; // the WindowState hook marks the look stale
        frames.RequestFrameAt(null, _ => { });
        Assert.Equal(3, host.Looks);
        frames.RequestFrame(_ => { }); // not a draw loop's: never looks
        Assert.Equal(3, host.Looks);
        Tick();
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Parked_requests_look_again_every_100_ms_on_the_precise_timer_and_a_new_one_never_puts_the_look_off() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var unseen = true;
        var host = new FakeFrameHost { Period = Refresh240, Probe = () => unseen };
        var frames = new AvaloniaFrameSource(window, host);
        var ran = 0;
        frames.RequestFrameAt(null, _ => ran++);
        Assert.Equal([AvaloniaFrameSource.LookEvery], host.Precise!.Arms);
        runtime.Advance(TimeSpan.FromMilliseconds(60));
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(200), _ => ran++); // held back, later than the look
        frames.RequestFrameAt(null, _ => ran++); // parked: the look stays 40 ms away
        Assert.Single(host.Precise.Arms);
        Assert.Equal(3, frames.Pending);
        runtime.Advance(TimeSpan.FromMilliseconds(40));
        host.Precise.FireIfDue(); // still unseen: looks again in 100 ms
        Assert.Equal(TimeSpan.FromMilliseconds(100), host.Precise.Arms[^1]);
        Tick(3);
        Assert.Equal(0, ran);
        unseen = false;
        runtime.Advance(TimeSpan.FromMilliseconds(100));
        host.Precise.FireIfDue(); // seen again: both parked requests are asked for
        Tick();
        Assert.Equal(2, ran);
        Assert.Equal(1, frames.Pending); // the one held back to its instant (+260 ms from the start)
        Assert.Equal(TimeSpan.FromMilliseconds(60), host.Precise.Arms[^1]);
        runtime.Advance(TimeSpan.FromMilliseconds(60));
        host.Precise.FireIfDue();
        Tick();
        Assert.Equal(3, ran);
        Assert.Equal(0, frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Without_a_precise_timer_parked_requests_still_look_again_every_100_ms() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var unseen = true;
        var host = new FakeFrameHost { Period = Refresh240, HasPreciseTimer = false, Probe = () => unseen };
        var frames = new AvaloniaFrameSource(window, host);
        var ran = 0;
        frames.RequestFrameAt(null, _ => ran++);
        Assert.Equal([AvaloniaFrameSource.LookEvery], host.Coarse!.Arms);
        runtime.Advance(TimeSpan.FromMilliseconds(100));
        host.Coarse.FireIfDue();
        Assert.Equal(2, host.Coarse.Arms.Count);
        unseen = false; // e.g. back from another virtual desktop, with no WindowState, IsActive or IsVisible change
        runtime.Advance(TimeSpan.FromMilliseconds(100));
        host.Coarse.FireIfDue();
        Tick();
        Assert.Equal(1, ran);
        Assert.Equal(0, frames.Pending);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_window_that_is_not_win32_is_never_looked_at_and_nothing_is_held() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Win32 = false, Period = Refresh240, Probe = () => true };
        var frames = new AvaloniaFrameSource(window, host);
        Assert.Null(frames.WakePeriod);
        var ran = 0;
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), _ => ran++);
        frames.RequestFrameAt(null, _ => ran++);
        Tick();
        Assert.Equal(2, ran);
        Assert.Equal(0, host.Looks);
        Assert.Null(host.Precise);
        window.Close();
    });

    // Review 2026-10-11: only a yes is kept. A top level asked before its platform window exists (no HWND yet) is asked
    // again at the next request, so the process's source never stays without holding back and parking.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_no_from_the_platform_is_asked_again_and_a_yes_holds_back_from_then_on() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Win32 = false, Period = Refresh240 };
        var frames = new AvaloniaFrameSource(window, host);
        Assert.Null(frames.WakePeriod);
        var ran = 0;
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), _ => ran++);
        Tick();
        Assert.Equal(1, ran); // not a Win32 window yet: asked for at once, as before
        host.Win32 = true; // the platform window exists now
        Assert.Equal(Refresh240, frames.WakePeriod);
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), _ => ran++);
        Assert.Equal([TimeSpan.FromMilliseconds(50)], host.Precise!.Arms);
        Tick(3);
        Assert.Equal(1, ran);
        host.Win32 = false; // a yes is kept
        Assert.Equal(Refresh240, frames.WakePeriod);
        runtime.Advance(TimeSpan.FromMilliseconds(50));
        host.Precise.FireIfDue();
        Tick();
        Assert.Equal(2, ran);
        window.Close();
    });

    // Review 2026-10-11: a window Windows uncloaks (back from another virtual desktop) raises no Avalonia property change; the
    // host's cloak watch calls AvaloniaFrameSource.WindowsChanged, and parked requests are asked for at the next frame, not at
    // the next 100 ms look.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_cloak_change_looks_at_the_windows_again_at_once_and_resumes_parked_requests_at_the_next_frame() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var unseen = true;
        var host = new FakeFrameHost { Period = Refresh240, Probe = () => unseen };
        var frames = new AvaloniaFrameSource(window, host);
        var ran = 0;
        frames.RequestFrameAt(null, _ => ran++);
        Assert.Equal(1, frames.Pending);
        Tick(3);
        Assert.Equal(0, ran);
        var looks = host.Looks;
        unseen = false;
        AvaloniaFrameSource.WindowsChanged(); // the WinEvent hook's call (EVENT_OBJECT_UNCLOAKED); no time has passed
        Tick();
        Assert.Equal(1, ran);
        Assert.Equal(looks + 1, host.Looks);
        Assert.Equal(0, frames.Pending);
        window.Close();
    });

    // Review 2026-10-11: only the shell's cloak (another virtual desktop) hides a window. A window the app cloaks itself
    // (WindowManager.SettingsCloak, the boot stage: DWM_CLOAKED_APP) is being composed to be shown and counts as seen.
    [Theory]
    [Trait("Decision", "ED-NEW-lean-7")]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]   // minimised
    [InlineData(false, 0x1, false)] // DWM_CLOAKED_APP: the app's own cloak
    [InlineData(false, 0x2, true)]  // DWM_CLOAKED_SHELL: another virtual desktop
    [InlineData(false, 0x4, true)]  // ED-NEW-lean-c9b-1: without a readable owner origin, inherited remains unseen
    [InlineData(false, 0x6, true)]
    [InlineData(true, 0x1, true)]
    public void Minimized_shell_and_unresolved_inherited_cloaks_are_hidden(bool iconic, int cloaked, bool hidden) =>
        Assert.Equal(hidden, Win32FrameHost.HiddenBy(iconic, cloaked));

    // The production cloak watch is set on main, once, for this process only.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_production_cloak_watch_is_set_on_main_once() => headless.OnUiThread(() =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var host = new Win32FrameHost();
        var calls = 0;
        host.WatchCloaking(() => calls++);
        Assert.True(host.WatchingCloaks);
        host.WatchCloaking(() => calls += 100); // once: the first watch stays
        Assert.True(host.WatchingCloaks);
        Assert.Equal(0, calls);
    });

    // The draws of slow loops on the production source with a precise timer are those of the same source with none (which
    // asks at every frame, as before ED-NEW-lean-7): 30 Hz and 24 Hz, a link re-timed and a timeline re-timed and paused,
    // on 240/144/60 Hz, with the wake-up on time and late by up to 1.4 refreshes (the margin is 1.5).
    [Theory]
    [InlineData(240, 0.0)]
    [InlineData(240, 1.4)]
    [InlineData(144, 0.0)]
    [InlineData(144, 1.4)]
    [InlineData(60, 0.0)]
    [InlineData(60, 1.4)]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Slow_loops_on_the_production_source_draw_where_a_source_that_holds_nothing_back_draws(int display, double lateRefreshes) => headless.OnUiThread(() =>
    {
        var (linkBase, timelineBase, baseFrames) = Run(display, precise: false, 0);
        var (link, timeline, heldFrames) = Run(display, precise: true, lateRefreshes);
        Assert.NotEmpty(linkBase);
        Assert.NotEmpty(timelineBase);
        Assert.Equal(linkBase, link);
        Assert.Equal(timelineBase, timeline);
        // At 60 Hz a 30 or 24 Hz draw is too close to hold back (only 20 Hz is): fewer frames only where there is room.
        Assert.True(display == 60 ? heldFrames <= baseFrames : heldFrames < baseFrames * 3 / 4, $"{heldFrames} frame callbacks with the timer, {baseFrames} without");
        if (display == 240) Assert.True(heldFrames < baseFrames / 2, $"{heldFrames} frame callbacks with the timer, {baseFrames} without");
    });

    private static (List<long> Link, List<long> Timeline, long Frames) Run(int display, bool precise, double lateRefreshes)
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / display), HasPreciseTimer = precise };
        var frames = new AvaloniaFrameSource(window, host);
        var counting = new CountingLoopSource(frames);
        using var source = FrameClock.Source.Override(counting);
        var start = SwiftDate.Now;
        var linkDraws = new List<long>();
        var timelineDraws = new List<long>();
        var link = FrameClock.DisplayLink(() => linkDraws.Add((SwiftDate.Now - start).Ticks));
        link.PreferredFrameRateRange = new FrameRateRange(24, 60, 30);
        link.Add();
        var rate = new CellSample<int>(24);
        var paused = new CellSample<bool>(false);
        var timeline = new AnimationTimeline(
            () => new AnimationTimelineSchedule(MinimumInterval: 1.0 / rate.Value, Paused: paused.Value),
            date => timelineDraws.Add((date - start).Ticks));
        timeline.Appear();
        float[] linkRates = [30, 20, 24, 60, 30];
        int[] timelineRates = [24, 20, 30, 60, 24];
        var late = TimeSpan.FromTicks((long)(lateRefreshes * TimeSpan.TicksPerSecond / display));
        for (var k = 1; k <= display * 2; k++)
        {
            runtime.Advance(TimeSpan.FromTicks(k * TimeSpan.TicksPerSecond / display - (k - 1) * TimeSpan.TicksPerSecond / display));
            host.Precise?.FireIfDue(late); // the wake-up job runs ahead of this refresh's render job
            Tick();
            if (k % 37 == 0) link.PreferredFrameRateRange = new FrameRateRange(24, 60, linkRates[k / 37 % linkRates.Length]);
            if (k % 29 == 0) rate.Value = timelineRates[k / 29 % timelineRates.Length];
            if (k % 41 == 0) paused.Value = !paused.Value;
        }
        link.Invalidate();
        timeline.Disappear();
        window.Close();
        return (linkDraws, timelineDraws, counting.Answered);
    }
}
