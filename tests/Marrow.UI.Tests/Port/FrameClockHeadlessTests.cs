using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.FrameClock on Avalonia 12.1.2 headless (ED-083, ED-098 (1), R-VIEW-10, R-TEST-7): the production frame source is
// TopLevel.RequestAnimationFrame and passes on its frame time, and the TimelineView modifier lives exactly as long as
// its control is attached. The surface is described at the top of FrameClockTests.cs.
[Collection("Headless")]
public sealed class FrameClockHeadlessTests(HeadlessSession headless)
{
    // ManualTimeProvider's default start (tests/Marrow.Testing/ManualTimeProvider.cs).
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void The_Avalonia_frame_source_calls_back_once_on_the_UI_thread_from_the_render_timer_with_the_frame_time_and_never_inline() => headless.OnUiThread(() =>
    {
        var window = Show(new Border());
        var frames = new AvaloniaFrameSource(window);
        var calls = new List<(bool OnUiThread, TimeSpan Time)>();
        frames.RequestFrame(time => calls.Add((Dispatcher.UIThread.CheckAccess(), time)));
        Assert.Empty(calls); // never inside the request
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(calls);
        Assert.True(calls[0].OnUiThread);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(calls); // one request, one call
        frames.RequestFrame(time => calls.Add((Dispatcher.UIThread.CheckAccess(), time)));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, calls.Count);
        Assert.True(calls[1].OnUiThread);
        Assert.True(calls[1].Time >= calls[0].Time); // the render timer's time, passed on, never runs backwards
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-VIEW-10")]
    [Trait("Rule", "R-VIEW-3")]
    [Trait("Decision", "ED-083")]
    [Trait("Decision", "ED-098")]
    public void The_timeline_view_modifier_draws_on_attach_follows_frames_and_stops_on_detach() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var dates = new List<DateTimeOffset>();
        // App/Views/Settings/AboutUpdateViews.swift:364  TimelineView(.animation(minimumInterval: 1.0 / 60.0, paused: ...))
        var content = new Border().TimelineView(() => new AnimationTimelineSchedule(MinimumInterval: 1.0 / 60.0, Paused: false), dates.Add);
        Assert.Empty(dates); // nothing before it is attached
        var window = Show(content);
        Assert.Equal([Start], dates);
        Assert.Equal(1, frames.Pending);
        time.Advance(TimeSpan.FromMilliseconds(20));
        frames.Advance(TimeSpan.FromMilliseconds(20));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([Start, Start + TimeSpan.FromMilliseconds(20)], dates);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        time.Advance(TimeSpan.FromMilliseconds(20));
        frames.Advance(TimeSpan.FromMilliseconds(20));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, dates.Count); // detached: no more updates
        Assert.Equal(0, frames.Pending); // and no more frames asked for
    });
}
