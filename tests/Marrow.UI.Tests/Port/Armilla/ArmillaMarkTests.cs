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

// AS-8 "done when": the mark is one fixed square slot that a state change never resizes, out of the accessibility tree and
// never focused; a settled row mark stops drawing; a mark that is hidden, or in a window that has been without focus for 30 s,
// reaches rest continuously, stops drawing and wakes on focus (2.5 "Render budget", "Hover", "The Avalonia port"; CU-DX-SMOOTH3).
[Collection("Headless")]
public sealed class ArmillaMarkTests(HeadlessSession headless)
{
    private static void Pump(int frames = 6)
    {
        for (var i = 0; i < frames; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void The_slot_is_a_fixed_square_that_no_state_glyph_or_asturs_resize_and_the_mark_is_a_picture() => headless.OnUiThread(() =>
    {
        var mark = new ArmillaMark { MarkSize = 20, Activity = "Generating the voiceover" };
        var window = new Window { Width = 200, Height = 120, Content = new StackPanel { Children = { mark } } };
        try
        {
            window.Show(); Pump();
            var size = mark.Bounds.Size;
            Assert.Equal(new Size(20, 20), size);
            foreach (var state in Enum.GetValues<ArmillaState>())
            foreach (var glyph in Enum.GetValues<ArmillaGlyph>())
            {
                mark.State = state; mark.Glyph = glyph; mark.Nodes = [new(ArmillaShape.Star), new(ArmillaShape.Clover)];
                Pump(1);
                Assert.Equal(size, mark.Bounds.Size);
            }
            Assert.False(mark.Focusable);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(mark));
            Assert.Equal("Generating the voiceover", ToolTip.GetTip(mark));
            mark.Activity = null;
            Assert.Null(ToolTip.GetTip(mark));
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void The_look_follows_the_theme_the_calm_flag_and_the_reduce_motion_setting() => headless.OnUiThread(() =>
    {
        var mark = new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Row, State = ArmillaState.Thinking };
        var window = new Window { Width = 200, Height = 120, Content = mark, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            Assert.NotNull(mark.SentLook);
            Assert.False(mark.SentLook!.Dark);
            Assert.True(mark.SentLook.Rest);
            Assert.False(mark.SentLook.Reduce);
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Pump(1);
            Assert.True(mark.SentLook!.Dark);
            mark.Calm = true; Pump(1);
            Assert.True(mark.SentLook!.Reduce);
            Assert.True(mark.Moves);
            mark.Calm = false;
            EnvironmentValues.SetAccessibilityReduceMotion(window, true); Pump(1);
            Assert.True(mark.SentLook!.Reduce);
            mark.Nodes = [new(ArmillaShape.Hexagon)];
            Pump(1);
            Assert.Empty(mark.SentLook!.Nodes);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_settled_row_mark_stops_drawing_and_a_state_change_wakes_it() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new ArmillaMark { MarkSize = 20, Kind = ArmillaKind.Row, State = ArmillaState.Paused, Glyph = ArmillaGlyph.Stopped };
        var window = new Window { Width = 100, Height = 100, Content = mark };
        try
        {
            window.Show();
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > 0);
            for (var i = 0; i < 400 && mark.Ticking; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(mark.Ticking);
            var frames = mark.FramesDrawn;
            Assert.True(frames > 0);
            for (var i = 0; i < 30; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
            Assert.InRange(mark.LastExtent, .3, .999);
            mark.State = ArmillaState.Thinking; mark.Glyph = ArmillaGlyph.None;
            for (var i = 0; i < 10; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > frames);
            Assert.True(mark.Ticking);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    public void A_hidden_mark_draws_nothing_and_drawing_resumes_when_it_shows() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new ArmillaMark { MarkSize = 48, Kind = ArmillaKind.Live, State = ArmillaState.Thinking };
        var host = new Border { Child = mark };
        var window = new Window { Width = 100, Height = 100, Content = host };
        try
        {
            window.Show();
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > 0);
            host.IsVisible = false;
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
            Assert.False(mark.Ticking);
            host.IsVisible = true;
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(mark.FramesDrawn > frames);
            mark.IsVisible = false;
            for (var i = 0; i < 4; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            frames = mark.FramesDrawn;
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, mark.FramesDrawn);
        }
        finally { window.Close(); Pump(1); }
    });

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-1")]
    [Trait("Decision", "ED-NEW-dextra-smooth-12")]
    public void Thirty_seconds_without_focus_puts_every_mark_of_the_window_to_sleep_and_focus_wakes_them() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var first = new ArmillaMark { MarkSize = 48, Kind = ArmillaKind.Live, State = ArmillaState.Thinking };
        var second = new ArmillaMark { MarkSize = 20, Kind = ArmillaKind.Row, State = ArmillaState.Listening };
        var window = new Window { Width = 200, Height = 100, Content = new StackPanel { Children = { first, second } } };
        try
        {
            window.Show(); Pump();
            var watch = ArmillaWindowWatch.For(window);
            watch.Focused(true);
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.True(first.Ticking && second.Ticking);
            watch.Focused(false);
            clock.Advance(TimeSpan.FromSeconds(29)); Pump(1);
            Assert.False(watch.Sleeping);
            Assert.True(first.Ticking);
            clock.Advance(TimeSpan.FromSeconds(1.1)); Pump(2);
            Assert.True(watch.Sleeping);
            // CU-DX-SMOOTH3 (a): sleep ends in a continuous rest, not an immediate
            // frozen moving frame. Retain the actual 29/30 s focus boundary above.
            for (var i = 0; i < 1080; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(first.Ticking); Assert.False(second.Ticking);
            var frames = (first.FramesDrawn, second.FramesDrawn);
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.Equal(frames, (first.FramesDrawn, second.FramesDrawn));
            watch.Focused(true);
            for (var i = 0; i < 6; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(1); }
            Assert.False(watch.Sleeping);
            Assert.True(first.FramesDrawn > frames.Item1 && second.FramesDrawn > frames.Item2);
        }
        finally { window.Close(); Pump(1); }
    });
}
