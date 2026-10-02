using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// TimelineView(.animation(minimumInterval:paused:)) re-reads its schedule when the environment it read changes: SwiftUI
// re-evaluates the view, and so the `paused:` argument, when \.accessibilityReduceMotion changes. In the port that value is
// an inherited Avalonia property (ED-084), which observation tracking does not see, so the timeline also re-reads its
// schedule when that property changes on its view (found by the native frame run, 2026-10-02: Reduce Motion turned on
// while a track ran left it running).
[Collection("Headless")]
public sealed class TimelineViewEnvironmentTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-084")]
    [Trait("Decision", "ED-117")]
    public void A_running_timeline_pauses_when_reduce_motion_turns_on_and_resumes_when_it_turns_off() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var draws = 0;
        Border view = null!;
        view = new Border { Width = 40, Height = 20 }.TimelineView(
            () => new AnimationTimelineSchedule(1.0 / 60, EnvironmentValues.GetAccessibilityReduceMotion(view)), _ => draws++);
        var window = new Window { Content = view, Width = 100, Height = 60 };
        window.Show();
        try
        {
            void Step(int times)
            {
                for (var i = 0; i < times; i++)
                {
                    time.Advance(TimeSpan.FromMilliseconds(20));
                    frames.Advance(TimeSpan.FromMilliseconds(20));
                    Dispatcher.UIThread.RunJobs();
                }
            }
            Dispatcher.UIThread.RunJobs();
            var start = draws;
            Step(3);
            Assert.Equal(start + 3, draws);
            EnvironmentValues.SetAccessibilityReduceMotion(window, true); // inherited by the view
            Dispatcher.UIThread.RunJobs();
            Step(5);
            Assert.Equal(start + 3, draws);
            EnvironmentValues.SetAccessibilityReduceMotion(window, false);
            Dispatcher.UIThread.RunJobs();
            Step(3);
            Assert.Equal(start + 6, draws);
        }
        finally { window.Close(); }
    });
}
