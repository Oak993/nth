using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

// Dextra AS-17 (DESIGN.md rev 5, 2.5 "An Astur's own mark", "Render budget", "Hover"; ED-NEW-dextra-as17-2): the control. One fixed square slot (30 px in the org chart) that no
// state resizes, a picture out of the accessibility tree that is never focused, drawn on the render thread; a settled mark stops drawing, a hidden mark or a window
// without focus draws nothing, and it never counts against the motion budget.
[Collection("Headless")]
public sealed class AsturMarkTests(HeadlessSession headless)
{
    private static void Pump(int frames = 6)
    {
        for (var i = 0; i < frames; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_slot_is_a_fixed_square_that_no_state_or_shape_resizes_and_the_mark_is_a_picture() => headless.OnUiThread(() =>
    {
        var mark = new AsturMark { MarkSize = 30, Activity = "Voiceover for video 42" };
        var window = new Window { Width = 200, Height = 120, Content = new StackPanel { Children = { mark } } };
        try
        {
            window.Show(); Pump();
            var size = mark.Bounds.Size;
            Assert.Equal(new Size(30, 30), size);
            foreach (var shape in Enum.GetValues<ArmillaShape>())
            foreach (var state in Enum.GetValues<AsturMarkState>())
            {
                mark.Shape = shape; mark.State = state;
                Pump(1);
                Assert.Equal(size, mark.Bounds.Size);
            }
            Assert.False(mark.Focusable);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(mark));
            Assert.Equal("Voiceover for video 42", ToolTip.GetTip(mark));
            mark.Activity = null;
            Assert.Null(ToolTip.GetTip(mark));
            Assert.False(mark.Moves);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void An_Astur_mark_never_counts_against_the_motion_budget_in_any_state() => headless.OnUiThread(() =>
    {
        var mark = new AsturMark { MarkSize = 30 };
        foreach (var state in Enum.GetValues<AsturMarkState>())
        {
            mark.State = state;
            Assert.False(mark.Moves, state.ToString());
        }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void The_look_follows_the_theme_the_calm_flag_and_the_reduce_motion_setting() => headless.OnUiThread(() =>
    {
        var mark = new AsturMark { MarkSize = 30, Shape = ArmillaShape.Clover, State = AsturMarkState.Working };
        var window = new Window { Width = 200, Height = 120, Content = mark, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            Assert.NotNull(mark.SentLook);
            Assert.False(mark.SentLook!.Dark);
            Assert.False(mark.SentLook.Reduce);
            Assert.Equal(ArmillaShape.Clover, mark.SentLook.Shape);
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Pump(1);
            Assert.True(mark.SentLook!.Dark);
            mark.Calm = true; Pump(1);
            Assert.True(mark.SentLook!.Reduce);
            mark.Calm = false;
            EnvironmentValues.SetAccessibilityReduceMotion(window, true); Pump(1);
            Assert.True(mark.SentLook!.Reduce);
            EnvironmentValues.SetAccessibilityReduceMotion(window, false); Pump(1);
            Assert.False(mark.SentLook!.Reduce);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_settled_mark_stops_drawing_and_a_state_change_wakes_it_for_its_spring_only() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new AsturMark { MarkSize = 30, Shape = ArmillaShape.Star, State = AsturMarkState.Idle };
        var window = new Window { Width = 100, Height = 100, Content = mark };
        try
        {
            window.Show();
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > 0);
            for (var i = 0; i < 60 && mark.Ticking; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(mark.Ticking);
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);                                             // a mark at rest draws nothing
            Assert.InRange(mark.LastExtent, .5, .999);

            mark.State = AsturMarkState.Working;
            for (var i = 0; i < 10; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > frames);
            for (var i = 0; i < 90 && mark.Ticking; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(mark.Ticking);                                                         // the spring settled and the mark left the clock again
            frames = mark.FramesDrawn;
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void With_reduce_motion_a_state_change_draws_once_and_asks_for_no_more_frames() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new AsturMark { MarkSize = 30, Shape = ArmillaShape.Diamond, State = AsturMarkState.Idle, Calm = true };
        var window = new Window { Width = 100, Height = 100, Content = mark };
        try
        {
            window.Show();
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            for (var i = 0; i < 6 && mark.Ticking; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            mark.State = AsturMarkState.Done;
            for (var i = 0; i < 3; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(mark.Ticking);
            Assert.True(mark.SentLook!.Reduce);
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void A_hidden_mark_draws_nothing_and_shows_the_current_pose_at_once_when_it_is_shown() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new AsturMark { MarkSize = 30, Shape = ArmillaShape.Hexagon, State = AsturMarkState.Idle };
        var host = new Border { Child = mark };
        var window = new Window { Width = 100, Height = 100, Content = host };
        try
        {
            window.Show();
            for (var i = 0; i < 8; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            host.IsVisible = false;
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            mark.State = AsturMarkState.Waiting;                                                // changes while nobody sees it
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);                                             // nothing is drawn while a parent hides it
            host.IsVisible = true;
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > frames);
            Assert.False(mark.Ticking);                                                         // it took the pose at once: no spring was left to play

            // and it can spring again afterwards: a change that came while it was hidden left no request behind that would swallow this one
            frames = mark.FramesDrawn;
            mark.State = AsturMarkState.Done;
            for (var i = 0; i < 12; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn - frames >= 6, $"only {mark.FramesDrawn - frames} frames of the spring were drawn");
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-17")]
    [Trait("Decision", "ED-NEW-dextra-as17-2")]
    public void Thirty_seconds_without_focus_puts_the_mark_to_sleep_so_a_change_meanwhile_is_drawn_once_and_never_sprung() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new AsturMark { MarkSize = 30, Shape = ArmillaShape.Squircle, State = AsturMarkState.Idle };
        var window = new Window { Width = 100, Height = 100, Content = mark };
        try
        {
            window.Show(); Pump();
            var watch = ArmillaWindowWatch.For(window);
            watch.Focused(true);
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            watch.Focused(false);
            clock.Advance(TimeSpan.FromSeconds(31)); Pump(2);
            Assert.True(watch.Sleeping);
            mark.State = AsturMarkState.Stopped;                                                // a window that is only unfocused still shows the new pose, drawn once
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(mark.Ticking);                                                         // and no spring plays behind another window
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
            watch.Focused(true);
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(watch.Sleeping);
            Assert.False(mark.Ticking);
        }
        finally { window.Close(); Pump(1); }
    });
}
