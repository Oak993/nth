// PORT-ONLY: the Tactus pill v2 capsule (OD-0145, ED-NEW-pcctl-24; DESIGN.md 4.5, 10.1, 10.3, 10.5, 10.6): its accessible
// names, its fit, the no-fades poses it hands the compositor (text and groups slide under their clips, glyphs and Stop
// scale, rows unroll), Reduce Motion's fades, a part that left being hidden after its leave, the layers a band draws, and
// the press: a click, a drag that lifts and ends with an anchor. A recording animator stands in for the compositor and a
// manual clock for time; nothing here sends input outside the headless window.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-24")]
public sealed class ControlPillTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");

    private static PillView Working(string step = "Filling row 12 of 40") =>
        PillStates.Compute(new PillFacts { Apps = [Excel with { Step = step }] }, 10);

    private static PillView Paused() =>
        PillStates.Compute(new PillFacts { Apps = [Excel with { Phase = PillAppPhase.Paused, PhaseSince = 9 }] }, 10);

    private static (ControlPill Pill, RecordingPillAnimator Animator, ManualTimeProvider Clock) Make()
    {
        var animator = new RecordingPillAnimator();
        var clock = new ManualTimeProvider();
        return (new ControlPill(PillDriverKind.RenderThread, animator, clock), animator, clock);
    }

    private static void Pass(ManualTimeProvider clock, double seconds)
    {
        clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Its_parts_carry_the_accessible_names_with_their_chords() => headless.OnUiThread(() =>
    {
        var (pill, _, _) = Make();
        pill.Show(Working());

        Assert.Equal("Tactus", AutomationProperties.GetName(pill));
        Assert.Equal("Stop all, Ctrl+Alt+Esc", AutomationProperties.GetName(pill.StopButton));
        Assert.Equal("Ctrl+Alt+Esc", AutomationProperties.GetHelpText(pill.StopButton));
        var run = Assert.IsType<StackPanel>(pill.GroupOf(PillGroupKind.Run));
        Assert.Equal("Pause all, Ctrl+Alt+Space", AutomationProperties.GetName(run.Children[0]));
        Assert.Equal("Ctrl+Alt+Space", AutomationProperties.GetHelpText(run.Children[0]));
        Assert.Equal("Using Excel", pill.TitleSlot.Text);
        Assert.Equal("Excel: Filling row 12 of 40", pill.LineSlot.Text);

        pill.Show(Paused());
        var paused = Assert.IsType<StackPanel>(pill.GroupOf(PillGroupKind.Paused));
        Assert.Equal("Resume all, Ctrl+Alt+Space", AutomationProperties.GetName(paused.Children[0]));
        // decoration stays out of the accessibility tree
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(pill.Dot));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(pill.BadgePart));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(pill.HitArea));
    });

    [Fact]
    public void It_fits_its_width_and_folds_Stop_s_key_cap_for_an_ask() => headless.OnUiThread(() =>
    {
        var (pill, _, _) = Make();
        pill.Show(Working());

        var fitted = Assert.IsType<PillLayoutResult>(pill.Fitted);
        Assert.InRange(fitted.Width, PillLayout.MinWidth, PillLayout.MaxWidth);
        Assert.False(fitted.CapFolded);
        Assert.True(pill.Parts.ShowsStop);
        Assert.True(pill.Parts.StopWidth > pill.Parts.CapWidth);
        Assert.True(pill.Parts.GroupWidth > 0);
        // the first show is instant: the motion rests at the fitted width
        Assert.True(pill.Motion.AtRest);
        Assert.Equal(fitted.Width, pill.Motion.Final.Width, 9);
        Assert.Equal(0, pill.Motion.Final.Cap, 9);

        var ask = PillStates.Compute(new PillFacts { Apps = [Excel], Asks = [new PillAsk("a", PillAskKind.Approval, "Outlook", "Report", "Claude")] }, 10);
        pill.Arrive(pill.Now);
        pill.Show(ask);
        Assert.True(pill.Fitted!.CapFolded);
        Assert.Equal(1, pill.Motion.Final.Cap, 9);
        Assert.False(pill.Motion.AtRest);

        // a long step is left out of the fit while working; the title still fits
        var longStep = Working(new string('x', 400));
        pill.Show(longStep);
        Assert.InRange(pill.Fitted!.Width, PillLayout.MinWidth, PillLayout.MaxWidth);
    });

    [Fact]
    public void A_new_line_slides_in_under_its_clip_and_the_old_one_is_emptied_after() => headless.OnUiThread(() =>
    {
        var (pill, animator, clock) = Make();
        pill.Show(Working());
        pill.Arrive();
        animator.Clear();

        pill.Show(Working("Filling row 13 of 40"));
        Assert.Equal("Excel: Filling row 13 of 40", pill.LineSlot.Text);
        Assert.Equal("Excel: Filling row 12 of 40", pill.LineSlot.Previous);
        var slides = animator.Calls.Where(c => c.Kind == "Translate" && c.Target is TextBlock).ToList();
        Assert.Equal(2, slides.Count);
        // the old line from 0 up to -17, the new one from +17 to 0, both over 150 ms
        Assert.Contains(slides, c => Math.Abs(c.Y!.Values[0]) < 1e-9 && Math.Abs(c.Y.Last + 17) < 1e-9);
        Assert.Contains(slides, c => Math.Abs(c.Y!.Values[0] - 17) < 1e-9 && Math.Abs(c.Y.Last) < 1e-9);
        Assert.All(slides, c => Assert.Equal(ControlPill.TextSwapSeconds, c.Y!.Duration, 9));
        Assert.DoesNotContain(animator.Calls, c => c.Kind == "Fade");

        Pass(clock, ControlPill.TextSwapSeconds + ControlPill.HideMargin);
        Assert.Equal("", pill.LineSlot.Previous);
        Assert.Equal("Excel: Filling row 13 of 40", pill.LineSlot.Text);
    });

    [Fact]
    public void A_new_group_slides_in_from_below_and_the_old_one_leaves_up_and_is_hidden_after() => headless.OnUiThread(() =>
    {
        var (pill, animator, clock) = Make();
        pill.Show(Working());
        pill.Arrive();
        var run = pill.GroupOf(PillGroupKind.Run)!;
        Assert.True(run.IsVisible);
        Assert.True(run.IsEnabled);
        Assert.Equal(PillGroupKind.Run, pill.ShownGroup);
        animator.Clear();

        pill.Show(Paused());
        var paused = pill.GroupOf(PillGroupKind.Paused)!;
        Assert.Equal(PillGroupKind.Paused, pill.ShownGroup);
        Assert.True(paused.IsVisible);
        Assert.True(paused.IsEnabled);
        Assert.False(run.IsEnabled);
        var leave = animator.Last(run, "Translate")!;
        Assert.Equal(0, leave.Y!.Values[0], 9);
        Assert.Equal(-PillLayout.Height, leave.Y.Last, 9);
        var enter = animator.Last(paused, "Translate")!;
        Assert.Equal(PillLayout.Height, enter.Y!.Values[0], 9);
        Assert.Equal(0, enter.Y.Last, 9);
        Assert.True(run.IsVisible);

        Pass(clock, ControlPill.TextSwapSeconds + ControlPill.HideMargin);
        Assert.False(run.IsVisible);
        Assert.True(paused.IsVisible);

        // back before the old one is hidden: it is shown again and the pending hide does nothing
        pill.Show(Working());
        Pass(clock, 0.05);
        pill.Show(Paused());
        Pass(clock, ControlPill.TextSwapSeconds);
        Assert.True(paused.IsVisible);
    });

    [Fact]
    public void The_glyph_swaps_by_scale_and_breathes_and_Reduce_Motion_fades_without_breathing() => headless.OnUiThread(() =>
    {
        var (pill, animator, clock) = Make();
        pill.Show(Working());
        pill.Arrive();
        Assert.True(pill.Dot.IsVisible);
        Assert.False(pill.Bars.IsVisible);
        Assert.NotNull(animator.Last(pill.Dot, "Breathe"));
        Assert.Equal(0.45, animator.Last(pill.Dot, "Breathe")!.A, 9);
        Assert.Equal(2.2, animator.Last(pill.Dot, "Breathe")!.B, 9);
        animator.Clear();

        pill.Show(Paused());
        var dotScale = animator.Last(pill.Dot, "Scale")!;
        Assert.Equal(1, dotScale.X!.Values[0], 9);
        Assert.Equal(0, dotScale.X.Last, 9);
        var barsScale = animator.Last(pill.Bars, "Scale")!;
        Assert.Equal(0, barsScale.X!.Values[0], 9);
        Assert.Equal(1, barsScale.X.Last, 9);
        Assert.True(pill.Bars.IsVisible);
        Assert.NotNull(animator.Last(pill.Dot, "StopBreathing"));
        Pass(clock, dotScale.X.Duration + ControlPill.HideMargin);
        Assert.False(pill.Dot.IsVisible);

        pill.ReduceMotion = true;
        animator.Clear();
        pill.Show(Working());
        Assert.Null(animator.Last(pill.Dot, "Scale"));
        var fadeIn = animator.Last(pill.Dot, "Fade")!;
        Assert.Equal(0, fadeIn.X!.Values[0], 9);
        Assert.Equal(1, fadeIn.X.Last, 9);
        Assert.Equal(PillCurve.ReducedFade, fadeIn.X.Duration, 9);
        Assert.Null(animator.Last(pill.Dot, "Breathe"));
        Assert.DoesNotContain(animator.Calls, c => c.Kind == "Translate" && c.Target is TextBlock);
    });

    [Fact]
    public void Stop_scales_away_where_it_was_and_the_hint_unrolls() => headless.OnUiThread(() =>
    {
        var (pill, animator, clock) = Make();
        pill.Show(Working());
        pill.Arrive();
        Assert.True(pill.StopSlot.IsVisible);
        Assert.True(pill.StopButton.IsEnabled);
        animator.Clear();

        var stopped = PillStates.Compute(new PillFacts { Apps = [Excel], Stopped = new PillStop(9.5) }, 10);
        pill.Show(stopped);
        Assert.False(pill.StopButton.IsEnabled);
        var scale = animator.Last(pill.StopSlot, "Scale")!;
        Assert.Equal(1, scale.X!.Values[0], 9);
        Assert.Equal(0, scale.X.Last, 9);
        // it keeps its capsule's width and scales about its own centre
        var centre = animator.Last(pill.StopSlot, "SetCentre")!;
        Assert.Equal(pill.Parts.StopWidth / 2, centre.A, 9);
        Assert.Equal(PillSurfacePainter.StopHeight / 2, centre.B, 9);
        Pass(clock, scale.X.Duration + ControlPill.HideMargin);
        Assert.False(pill.StopSlot.IsVisible);

        var paused = Paused();
        Assert.True(paused.HintShown);
        animator.Clear();
        pill.Show(paused);
        Assert.True(pill.Hint.IsVisible);
        // the row reads the sentence; it draws chips for the keys and a dot between the chords
        Assert.Equal(paused.Hint, AutomationProperties.GetName(pill.Hint));
        var pieces = ((Panel)((Border)pill.Hint).Child!).Children;
        Assert.Equal(paused.HintPieces.Count, pieces.Count);
        var chip = Assert.IsType<Border>(pieces[0]);
        Assert.Equal("Ctrl", Assert.IsType<TextBlock>(chip.Child).Text);
        Assert.Equal(" resumes", Assert.IsType<TextBlock>(pieces[3]).Text);
        Assert.Equal("·", Assert.IsType<TextBlock>(pieces[4]).Text);
        Assert.All(pieces, p => Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(p)));
        var unroll = animator.Last(pill.Hint, "Translate")!;
        Assert.Equal(-(ControlPill.HintHeight + 8), unroll.Y!.Values[0], 9);
        Assert.Equal(0, unroll.Y.Last, 9);
        Assert.True(pill.Hint.IsHitTestVisible);

        pill.Show(Working());
        var rollBack = animator.Last(pill.Hint, "Translate")!;
        Assert.Equal(-(ControlPill.HintHeight + 8), rollBack.Y!.Last, 9);
        Assert.False(pill.Hint.IsHitTestVisible);
        Pass(clock, rollBack.Y.Duration + ControlPill.HideMargin);
        Assert.False(pill.Hint.IsVisible);
    });

    [Fact]
    public void A_band_draws_only_its_layers() => headless.OnUiThread(() =>
    {
        var (pill, _, _) = Make();
        pill.Show(Working());

        pill.Paint = PillPaint.Shadow;
        Assert.Equal(PillPaint.Shadow, pill.Body.Look.Paint);
        Assert.Equal(PillPaint.Shadow, pill.StopSurface.Look.Paint);
        Assert.False(pill.Content.IsVisible);
        Assert.False(pill.Below.IsVisible);
        Assert.False(pill.HitArea.IsHitTestVisible);

        pill.Paint = PillPaint.Body | PillPaint.Content;
        Assert.True(pill.Content.IsVisible);
        Assert.True(pill.Below.IsVisible);
        Assert.True(pill.HitArea.IsHitTestVisible);
        Assert.Equal(PillPaint.Body | PillPaint.Content, pill.Body.Look.Paint);

        pill.Glass = true;
        Assert.True(pill.Body.Look.Glass);
        pill.HighContrast = true;
        Assert.True(pill.Body.Look.HighContrast);
        Assert.True(pill.StopSurface.Look.HighContrast);
        // Stop's capsule rides its slot, with its content and its hit area
        Assert.Same(pill.StopSlot, pill.StopSurface.Parent);
        Assert.Same(pill.StopSlot, pill.StopClip.Parent);
    });

    [Fact]
    public void Arrive_and_leave_are_scale_poses_and_Reduce_Motion_fades_the_content_with_the_body() => headless.OnUiThread(() =>
    {
        var (pill, animator, _) = Make();
        pill.Show(Working());
        Assert.Equal(PillPresence.Gone, pill.Motion.Presence);
        animator.Clear();

        pill.Arrive();
        Assert.Equal(PillPresence.Arriving, pill.Motion.Presence);
        var stage = animator.Last(pill.Stage, "Scale")!;
        Assert.Equal(0, stage.X!.Values[0], 9);
        Assert.Equal(1, stage.X.Last, 9);
        Assert.Equal(1, animator.Last(pill.Content, "SetOpacity")!.A, 9);

        var (calm, calmAnimator, _) = Make();
        calm.ReduceMotion = true;
        calm.Show(Working());
        calmAnimator.Clear();
        calm.Arrive();
        var fade = calmAnimator.Last(calm.Content, "Fade")!;
        Assert.Equal(0, fade.X!.Values[0], 9);
        Assert.Equal(1, fade.X.Last, 9);
        Assert.NotNull(calmAnimator.Last(calm.Below, "Fade"));
        Assert.Equal(1, calmAnimator.Last(calm.Stage, "SetScale")!.A, 9);
    });

    [Fact]
    public void A_click_on_the_body_is_the_pill_s_click_and_a_drag_lifts_it_and_ends_with_its_anchor() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (pill, _, _) = Make();
        var window = new Window { Width = 900, Height = 300, Content = pill };
        window.Show();
        try
        {
            pill.Show(Working());
            pill.Place(new PillVector(400, 8), 0.5);
            pill.Arrive();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var commands = new List<PillCommand>();
            pill.Command += commands.Add;
            var started = 0;
            pill.DragStarted += () => started++;
            var ended = new List<(PillVector At, PillVector Anchor, PillVector Speed)>();
            pill.DragEnded += (at, anchor, speed) => ended.Add((at, anchor, speed));
            var left = 400 - (pill.Motion.Final.Width / 2);
            var press = new Point(left + 30, 32);

            window.MouseDown(press, MouseButton.Left);
            window.MouseUp(press, MouseButton.Left);
            Assert.Equal(new[] { new PillCommand(PillCommandKind.Click) }, commands);
            Assert.Equal(0, started);

            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(new Point(press.X + 2, press.Y + 0));
            Assert.Equal(0, started);
            window.MouseMove(new Point(press.X + 10, press.Y + 0));
            Assert.Equal(1, started);
            Assert.True(pill.Motion.Final.Lift > 1);
            window.MouseMove(new Point(press.X + 60, press.Y + 0));
            Assert.Equal(450, pill.Motion.At(pill.Now).Anchor.X, 6);
            window.MouseUp(new Point(press.X + 60, press.Y + 0), MouseButton.Left);

            var end = Assert.Single(ended);
            Assert.Equal(450, end.Anchor.X, 6);
            Assert.Equal(8, end.Anchor.Y, 6);
            Assert.Equal(press.X + 60, end.At.X, 6);
            Assert.Single(commands);
            Assert.Equal(1, pill.Motion.Final.Lift, 9);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void A_drag_down_is_rubber_banded() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (pill, _, _) = Make();
        var window = new Window { Width = 900, Height = 400, Content = pill };
        window.Show();
        try
        {
            pill.Show(Working());
            pill.Place(new PillVector(400, 8), 0.5);
            pill.Arrive();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var press = new Point(400 - (pill.Motion.Final.Width / 2) + 30, 32);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(new Point(press.X + 0, press.Y + 10));
            window.MouseMove(new Point(press.X + 0, press.Y + 210));
            Assert.Equal(8 + PillRubberBand.Vertical(200), pill.Motion.At(pill.Now).Anchor.Y, 6);
            Assert.True(PillRubberBand.Vertical(200) < 200);
            window.MouseUp(new Point(press.X + 0, press.Y + 210), MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    });
}
