using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 budget: frames delivered to a draw loop while no window of the app can be seen (every shown window minimised
// or cloaked). Headless (Avalonia 12.1.2, forced render ticks stand for a 240 Hz display's refreshes), Debug build. Measured on
// the unchanged base (1fdf47b43), with the window minimised: one frame per refresh (240 in 240 ticks). After ED-NEW-lean-7: 0.
// The window is really minimised and restored (WindowState): the source's window hooks, its look cache and its posted re-look
// run as in production; only IsIconic is answered by the fake host from the window's state (a headless window has no HWND),
// through the production rule AvaloniaFrameSource.NoWindowSeen.
[Collection("Headless")]
public sealed class FrameClockHiddenHostBudgetTests(HeadlessSession headless)
{
    private static void Tick(int count)
    {
        for (var i = 0; i < count; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static FakeFrameHost MinimisedIsIconic(Window window) => new()
    {
        HasPreciseTimer = false,
        Probe = () => AvaloniaFrameSource.NoWindowSeen([(window.IsVisible, 1)], _ => window.WindowState == WindowState.Minimized),
    };

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_display_link_gets_no_frames_while_no_window_can_be_seen_and_its_next_refresh_once_one_can() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 400, Height = 300, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var host = MinimisedIsIconic(window);
        var frames = new AvaloniaFrameSource(window, host);
        Assert.Null(frames.WakePeriod); // no precise timer: nothing is held back to an instant, as before
        using var source = FrameClock.Source.Override(frames);
        long ticks = 0;
        var link = FrameClock.DisplayLink(() => ticks++);
        link.Add();
        Tick(10);
        Assert.Equal(10, ticks);
        window.WindowState = WindowState.Minimized; // the hook marks the last look stale
        Tick(10); // warm-up: the request already asked for is answered once, then the link's next one waits
        var before = ticks;
        Tick(240);
        var delivered = ticks - before;
        Assert.True(delivered <= 0, $"{delivered} frames in 240 refreshes while no window can be seen (budget 0; 240 on the unchanged base)");
        Assert.Equal(1, frames.Pending); // the link's request waits, counted as before (ED-252)
        // looked at again in 100 ms (the system clock moves on while it arms), without a precise timer
        Assert.InRange(Assert.Single(host.Coarse!.Arms), AvaloniaFrameSource.LookEvery - TimeSpan.FromMilliseconds(20), AvaloniaFrameSource.LookEvery);
        window.WindowState = WindowState.Normal; // restored: the hook posts a look ahead of the next frame's render job
        Tick(1);
        Assert.Equal(before + 1, ticks);
        Tick(5);
        Assert.Equal(before + 6, ticks); // at every refresh again
        link.Invalidate();
        Assert.Equal(0, frames.Pending); // withdrawn: it does nothing with its frame
        Tick(1);
        Assert.Equal(before + 6, ticks);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_request_waiting_while_no_window_can_be_seen_is_withdrawn_by_cancel_and_other_requests_are_never_held() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 400, Height = 300, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frames = new AvaloniaFrameSource(window, MinimisedIsIconic(window));
        window.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        var called = 0;
        Action<TimeSpan> frame = _ => called++;
        frames.RequestFrameAt(null, frame); // a draw loop's request: held
        Assert.Equal(1, frames.Pending);
        var other = 0;
        frames.RequestFrame(_ => other++); // a transition, a completion, a TrySleep: asked for at once, as before ED-NEW-lean-7
        Tick(1);
        Assert.Equal(1, other);
        frames.CancelFrame(frame);
        Assert.Equal(0, frames.Pending);
        window.WindowState = WindowState.Normal;
        Tick(2);
        Assert.Equal(0, called);
        window.Close();
    });
}
