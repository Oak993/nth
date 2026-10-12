using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7, review 2026-10-11: a held request's wake-up asks into a render loop that has gone idle. ED-237 found that a
// frame request after a frame that committed nothing waits for an idle wake-up (16-31 ms) instead of the next refresh. In
// Avalonia 12.1.2 (MediaContext, decompiled) that wait is MediaContext's animations timer: a 16 ms DispatcherTimer started
// by a render pass that leaves frame requests queued and has nothing to commit. A request into an idle MediaContext is
// answered by the render job it posts at once (DispatcherPriority.Render), and a request with a commit, as every request of
// AvaloniaFrameSource is (ED-237), is answered when the compositor has taken that commit, at the render loop's next tick
// (the display's refresh). These tests run the real MediaContext and compositor on the headless platform (a forced render
// tick is a refresh) and read the two MediaContext fields that decide it. The Win32 render timer's own wake from idle is
// FrameClockWakeNativeTests' (Marrow.App.Tests, owed to a native run).
[Collection("Headless")]
public sealed class FrameClockIdleWakeTests(HeadlessSession headless)
{
    private static readonly TimeSpan Refresh240 = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 240);
    private static readonly Type MediaContextType = typeof(Avalonia.Visual).Assembly.GetType("Avalonia.Media.MediaContext", throwOnError: true)!;

    private static object Media => MediaContextType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    // The 16 ms DispatcherTimer behind ED-237's idle wake-up.
    private static DispatcherTimer IdleTimer =>
        (DispatcherTimer)MediaContextType.GetField("_animationsTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Media)!;

    // A commit is in flight: the next render pass comes when the compositor has processed it (the render loop's tick).
    private static bool CommitInFlight =>
        (bool)MediaContextType.GetField("_animationsAreWaitingForComposition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Media)!;

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

    // Idle: nothing requested, no commit in flight, the idle timer stopped (another test may have left it running).
    private static void Idle()
    {
        Tick(6);
        IdleTimer.Stop();
        Assert.False(CommitInFlight);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_wake_up_into_an_idle_loop_is_answered_at_once_and_then_at_each_refresh_never_by_the_idle_timer() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var window = ShownWindow();
        var host = new FakeFrameHost { Period = Refresh240 };
        var frames = new AvaloniaFrameSource(window, host);
        Idle();
        var answered = 0;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            answered++;
            if (answered < 4) frames.RequestFrameAt(null, frame!); // the loop asks at every frame from its wake-up to its draw
        };
        frames.RequestFrameAt(SwiftClock.Now + TimeSpan.FromMilliseconds(50), frame);
        Tick(3);
        Assert.Equal(0, answered); // held back: the loop has gone idle
        Assert.False(CommitInFlight);
        Assert.False(IdleTimer.IsEnabled);

        runtime.Advance(TimeSpan.FromMilliseconds(50));
        host.Post(host.Precise!.Fire); // as production: the waiting thread posts the wake-up to main at Normal
        Dispatcher.UIThread.RunJobs(); // no render tick, no timer
        Assert.Equal(1, answered); // answered by MediaContext's own render job, at once
        Assert.True(CommitInFlight); // the loop asked again with a commit: its next frame comes with the compositor's tick
        Assert.False(IdleTimer.IsEnabled); // not the idle wake-up (ED-237's 16-31 ms)

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, answered); // nothing before the refresh
        Tick();
        Assert.Equal(2, answered); // the next refresh
        Assert.False(IdleTimer.IsEnabled);
        Tick();
        Assert.Equal(3, answered);
        Assert.False(IdleTimer.IsEnabled);
        Tick();
        Assert.Equal(4, answered);
        Tick(3);
        Assert.Equal(4, answered);
        Assert.Equal(0, frames.Pending);
        window.Close();
    });

    // The contrast that shows the tests above can see ED-237's idle path: a frame request made in a frame with nothing to
    // commit (a bare RequestAnimationFrame, which AvaloniaFrameSource never makes) starts the idle timer.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_request_without_a_commit_in_a_frame_that_commits_nothing_waits_for_the_idle_timer() => headless.OnUiThread(() =>
    {
        var window = ShownWindow();
        Idle();
        var answered = 0;
        window.RequestAnimationFrame(_ =>
        {
            answered++;
            window.RequestAnimationFrame(_ => answered++); // no commit asked for
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, answered);
        Assert.False(CommitInFlight);
        Assert.True(IdleTimer.IsEnabled); // the second request waits for the 16 ms timer, not for a refresh
        Tick(2);
        Assert.Equal(1, answered); // refreshes do not answer it
        IdleTimer.Stop();
        _ = ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync(); // clean up: a render pass answers it
        Dispatcher.UIThread.RunJobs();
        Tick(2);
        Assert.Equal(2, answered);
        IdleTimer.Stop();
        window.Close();
    });
}
