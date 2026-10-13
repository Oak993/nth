using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-10")]
public sealed class DextraSmoothVisibilityTests(HeadlessSession headless)
{
    [Theory]
    [InlineData("hidden")]
    [InlineData("covered")]
    [InlineData("minimized")]
    [InlineData("cloaked")]
    public void Window_visibility_is_independent_of_focus_and_unseen_changes_resume_current(string condition) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        AmbientWindowState? source = null;
        AmbientWindowState.TestFactory = w => source = new(w, true);
        var mark = new ArmillaMark { MarkSize = 176, Kind = ArmillaKind.Live, State = ArmillaState.Thinking };
        var astur = new AsturMark { MarkSize = 30, State = AsturMarkState.Idle };
        var text = new TickText { Width = 200, Text = "Working", Sheen = true };
        var window = new Window { Width = 240, Height = 280, Content = new StackPanel { Children = { mark, astur, text } } };
        try
        {
            window.Show(); Pump(clock, 8);
            var watch = ArmillaWindowWatch.For(window); watch.Focused(true);
            source!.SetWindowFacts(new(true, false, false, false, false, false)); watch.Focused(false);
            var before = (mark.FramesDrawn, astur.FramesDrawn, text.SheenFrames);
            astur.State = AsturMarkState.Working; Pump(clock, 8);
            Assert.True(mark.FramesDrawn > before.Item1, "visible unfocused Armilla stopped at blur");
            Assert.True(astur.FramesDrawn > before.Item2, "visible unfocused Astur did not draw its state change");
            Assert.True(text.SheenFrames > before.Item3, "visible unfocused shimmer stopped at blur");
            Assert.False(watch.Sleeping);
            source.SetWindowFacts(new(condition != "hidden", condition == "minimized", condition == "cloaked", condition == "covered", false, false));
            Pump(clock, 4);
            before = (mark.FramesDrawn, astur.FramesDrawn, text.SheenFrames);
            mark.State = ArmillaState.Idle; mark.Glyph = ArmillaGlyph.Done; astur.State = AsturMarkState.Done; text.Text = "Latest unseen status";
            Pump(clock, 8);
            Assert.Equal(before, (mark.FramesDrawn, astur.FramesDrawn, text.SheenFrames));
            Assert.False(mark.Ticking); Assert.False(astur.Ticking); Assert.False(text.SheenTicking);
            Assert.Equal(1, Engine<ArmillaEngine>(mark).GlyphScale);
            AssertDone(Engine<AsturMarkEngine>(astur));
            // Restore while still unfocused; focus is never needed to see the latest pose.
            source.SetWindowFacts(new(true, false, false, false, false, false));
            // A forced timer tick delivers composition messages before the render. Observe each
            // first actual drawing, rather than assuming one pump has already presented it.
            var markDrawn = false; var asturDrawn = false; var textDrawn = false;
            for (var i = 0; i < 8 && !(markDrawn && asturDrawn && textDrawn); i++)
            {
                Pump(clock, 1);
                if (!markDrawn && mark.FramesDrawn > before.Item1) { AssertDone(Engine<ArmillaEngine>(mark)); markDrawn = true; }
                if (!asturDrawn && astur.FramesDrawn > before.Item2) { AssertDone(Engine<AsturMarkEngine>(astur)); asturDrawn = true; }
                textDrawn |= text.SheenFrames > before.Item3;
            }
            Assert.True(markDrawn, "Armilla reshow has not drawn yet");
            Assert.True(asturDrawn, "Astur reshow has not drawn yet");
            Assert.True(textDrawn, "shimmer reshow has not drawn yet");
            Assert.Equal(ArmillaGlyph.Done, mark.SentLook!.Glyph); Assert.Equal(AsturMarkState.Done, astur.SentLook!.State);
        }
        finally { window.Close(); Pump(clock, 1); AmbientWindowState.TestFactory = null; }
    });

    [Theory]
    [InlineData("Armilla")]
    [InlineData("Astur")]
    [InlineData("shimmer")]
    public void A_visible_state_change_after_thirty_seconds_of_blur_draws_without_restarting_motion(string kind) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var mark = new ArmillaMark { MarkSize = 176, Kind = ArmillaKind.Live, State = ArmillaState.Thinking };
        var astur = new AsturMark { MarkSize = 30, State = AsturMarkState.Idle };
        var text = new TickText { Width = 200, Text = "Working", Sheen = true };
        var window = new Window { Width = 240, Height = 280, Content = new StackPanel { Children = { mark, astur, text } } };
        int Draws() => kind == "Armilla" ? mark.FramesDrawn : kind == "Astur" ? astur.FramesDrawn : text.SheenFrames;
        try
        {
            window.Show(); Pump(clock, 8);
            var watch = ArmillaWindowWatch.For(window); watch.Focused(true); watch.Focused(false);
            clock.Advance(TimeSpan.FromSeconds(29)); Pump(clock, 2); Assert.False(watch.Sleeping);
            clock.Advance(TimeSpan.FromSeconds(1)); Pump(clock, 4); Assert.True(watch.Sleeping);
            var before = Draws(); Pump(clock, 6); Assert.Equal(before, Draws());
            if (kind == "Armilla") { mark.State = ArmillaState.Idle; mark.Glyph = ArmillaGlyph.Done; }
            else if (kind == "Astur") astur.State = AsturMarkState.Done;
            else text.Text = "A longer new status";
            Pump(clock, 4);
            Assert.True(Draws() > before, "visible sleeping state change drew nothing");
            if (kind == "Armilla") AssertDone(Engine<ArmillaEngine>(mark));
            if (kind == "Astur") AssertDone(Engine<AsturMarkEngine>(astur));
            Assert.False(mark.Ticking); Assert.False(astur.Ticking); Assert.False(text.SheenTicking);
            before = Draws(); Pump(clock, 12); Assert.Equal(before, Draws());
        }
        finally { window.Close(); Pump(clock, 1); }
    });

    private static T Engine<T>(Control control)
    {
        var handler = control.GetType().GetField("handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
        return Assert.IsType<T>(handler.GetType().GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler));
    }

    private static void AssertDone(ArmillaEngine engine)
    {
        Assert.Equal(ArmillaState.Idle, engine.State); Assert.Equal(1, engine.GlyphScale);
        var pose = ArmillaPoses.Pose(ArmillaState.Idle, engine.Look.Size, engine.Look.Rest);
        foreach (var key in Enum.GetValues<PoseKey>().Where(k => k != PoseKey.Count)) Assert.Equal(pose[key], engine.Value(key));
    }

    private static void AssertDone(AsturMarkEngine engine)
    {
        Assert.Equal(AsturMarkState.Done, engine.Look.State); Assert.Equal(1, engine.Scale); Assert.Equal(1, engine.Strength);
        Assert.Equal(AsturBadge.Check, engine.Badge); Assert.Equal(1, engine.Pop); Assert.True(engine.Settled, "Astur Done pose is not settled");
    }

    private static void Pump(ManualTimeProvider clock, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(17));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
