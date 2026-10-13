using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

// ED-NEW-final-settings-2: a window that turns over (the Settings window, OD-0088) must draw in its click frame what the frame before it drew. A still mark
// in its sidebar eases its palette from the one on screen, so that frame is the same picture; it draws nothing, and the glass its box reaches is not redrawn
// for it (a redraw of the glass at the mark's reach met the frame already drawn at its edge and differed there by up to 3 levels).
[Collection("Headless")]
public sealed class ArmillaClickFrameTests(HeadlessSession headless)
{
    private static void Pump(int frames = 1)
    {
        for (var i = 0; i < frames; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-final-settings-2")]
    public void A_still_mark_whose_appearance_turns_draws_nothing_in_the_click_frame_then_eases_and_comes_to_rest() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new ArmillaMark { MarkSize = 20, Kind = ArmillaKind.Still, State = ArmillaState.Idle };
        var window = new Window { Width = 100, Height = 100, Content = mark, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show();
            for (var i = 0; i < 400 && (i < 4 || mark.Ticking); i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(); }
            Assert.False(mark.Ticking); // at rest, with the Light palette drawn
            var drawn = mark.FramesDrawn;
            Assert.True(drawn > 0);
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Pump(4); // the click frame, with the compositor given every tick it needs and no time passing
            Assert.True(mark.SentLook!.Dark);
            Assert.Equal(drawn, mark.FramesDrawn); // no time has passed, so the picture is the one on screen and is not drawn again
            Assert.True(mark.Ticking); // but the ease is asked for
            // the ease then draws frame after frame (60 Hz slots), until its 0.32 s are over
            for (var k = 1; k <= 6; k++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(2); }
            Assert.True(mark.FramesDrawn - drawn >= 4, $"{mark.FramesDrawn - drawn} frames of the ease drawn in six slots");
            for (var i = 0; i < 400 && mark.Ticking; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(); }
            Assert.False(mark.Ticking);
            var rested = mark.FramesDrawn;
            for (var i = 0; i < 20; i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(); }
            Assert.Equal(rested, mark.FramesDrawn);
        }
        finally { window.Close(); Pump(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-final-settings-2")]
    public void Under_reduce_motion_the_new_palette_is_drawn_at_once_in_the_click_frame() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new ArmillaMark { MarkSize = 20, Kind = ArmillaKind.Still, State = ArmillaState.Idle };
        var window = new Window { Width = 100, Height = 100, Content = mark, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
        try
        {
            EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            window.Show();
            for (var i = 0; i < 400 && (i < 4 || mark.Ticking); i++) { clock.Advance(TimeSpan.FromMilliseconds(17)); Pump(); }
            var drawn = mark.FramesDrawn;
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            Pump(4);
            Assert.True(mark.SentLook!.Dark);
            Assert.True(mark.FramesDrawn > drawn); // nothing eases, so the click frame is the one that shows the new palette
        }
        finally { window.Close(); Pump(); }
    });
}
