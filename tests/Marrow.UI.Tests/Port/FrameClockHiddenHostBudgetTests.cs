using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 budget: frames delivered while no window of the app can be seen (every shown window minimised or
// cloaked; the probe is a seam here, IsIconic and DWMWA_CLOAKED in production). Headless (Avalonia 12.1.2, forced render
// ticks stand for a 240 Hz display's refreshes), Debug build. Measured on the unchanged base (1fdf47b43), with the window
// minimised: one frame per refresh (240 in 240 ticks). After ED-NEW-lean-7: 0.
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

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_display_link_gets_no_frames_while_no_window_can_be_seen_and_its_next_refresh_once_one_can() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 400, Height = 300, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var unseen = false;
        var frames = new AvaloniaFrameSource(window) { UnseenOverride = () => unseen };
        Assert.Null(frames.WakePeriod); // headless: nothing is held back to an instant, as before
        using var source = FrameClock.Source.Override(frames);
        long ticks = 0;
        var link = FrameClock.DisplayLink(() => ticks++);
        link.Add();
        Tick(10);
        Assert.Equal(10, ticks);
        unseen = true;
        Tick(10); // warm-up: the request already asked for is answered once, then the link's next one waits
        var before = ticks;
        Tick(240);
        var delivered = ticks - before;
        Assert.True(delivered <= 0, $"{delivered} frames in 240 refreshes while no window can be seen (budget 0; 240 on the unchanged base)");
        Assert.Equal(1, frames.Pending); // the link's request waits, counted as before (ED-252)
        unseen = false;
        frames.Recheck(); // a window restored or shown again
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
    public void A_request_waiting_while_no_window_can_be_seen_is_withdrawn_by_cancel() => headless.OnUiThread(() =>
    {
        var window = new Window { Width = 400, Height = 300, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frames = new AvaloniaFrameSource(window) { UnseenOverride = () => true };
        var called = 0;
        Action<TimeSpan> frame = _ => called++;
        frames.RequestFrame(frame);
        Assert.Equal(1, frames.Pending);
        frames.CancelFrame(frame);
        Assert.Equal(0, frames.Pending);
        frames.UnseenOverride = () => false;
        frames.Recheck();
        Tick(2);
        Assert.Equal(0, called);
        window.Close();
    });
}
