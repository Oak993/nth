using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b and 8a, "The mark", unit U2 (ED-NEW-inst-u2-1). The canvas's
// pop-in timeline (the tile fades in over 0.26 s and springs from 86 %, the lift rises 7 DIP, the line draws from 0.30 s
// to 1.04 s, the bead lands at 0.98 s), the silver's alternating 16 s drift (start, far end, back, and the mid-path rest
// at 7.65 s), the pop playing once per window on its first screen and again on Ready (never on the way back from
// Options), and Calm: a 0.2 s fade, no spring, no drift.
[Collection("SetupHeadless")]
public sealed class SetupMarkTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_pop_in_follows_the_canvas_timeline_and_ends_on_the_finished_mark()
    {
        var start = SetupMark.PopPose(0, calm: false);
        Assert.Equal(0, start.TileOpacity);
        Assert.Equal(.86, start.TileScale, 6);
        Assert.Equal(-7, start.LiftY, 6);
        Assert.Equal(0, start.Draw);
        Assert.Equal(0, start.BeadOpacity);

        Assert.Equal(1, SetupMark.PopPose(.26, false).TileOpacity, 6);
        Assert.Equal(0, SetupMark.PopPose(.30, false).Draw, 6);
        Assert.InRange(SetupMark.PopPose(.67, false).Draw, .4, .6);
        Assert.Equal(1, SetupMark.PopPose(1.04, false).Draw, 6);
        Assert.Equal(0, SetupMark.PopPose(.98, false).BeadOpacity, 6);
        Assert.Equal(1, SetupMark.PopPose(1.10, false).BeadOpacity, 6);

        var end = SetupMark.PopPose(SetupMark.PopDuration, false);
        Assert.Equal(1, end.TileOpacity, 6);
        Assert.Equal(1, end.TileScale, 2);
        Assert.Equal(0, end.LiftY, 1);
        Assert.Equal(1, end.Draw, 6);
        Assert.Equal(1, end.BeadScale, 1);

        double lastDraw = 0;
        for (var t = 0.0; t <= SetupMark.PopDuration; t += 1.0 / 60)
        {
            var pose = SetupMark.PopPose(t, false);
            Assert.True(pose.Draw >= lastDraw - 1e-9, "the line never undraws");
            lastDraw = pose.Draw;
            Assert.InRange(pose.TileOpacity, 0, 1);
            Assert.InRange(pose.LiftOpacity, 0, 1);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_drift_alternates_over_16_s_and_rests_mid_path_at_7_65_s()
    {
        Assert.Equal((-.07, -.06), Round(SetupMark.DriftAt(0)));
        Assert.Equal((.08, .07), Round(SetupMark.DriftAt(16)));
        Assert.Equal((-.07, -.06), Round(SetupMark.DriftAt(32)));
        Assert.Equal(Round(SetupMark.DriftAt(4)), Round(SetupMark.DriftAt(28)));
        var (x, y) = SetupMark.DriftAt(SetupMark.MidPath);
        Assert.InRange(x, -.004, .004);
        Assert.InRange(y, -.004, .004);
        // smooth: no step bigger than the path's own speed allows at 60 fps
        for (var t = 0.0; t < 40; t += 1.0 / 60)
        {
            var a = SetupMark.DriftAt(t);
            var b = SetupMark.DriftAt(t + 1.0 / 60);
            Assert.True(Math.Abs(b.X - a.X) < .0004 && Math.Abs(b.Y - a.Y) < .0004);
        }
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_only_fades_in_0_2_s_and_never_drifts()
    {
        headless.OnUiThread(() =>
        {
            Assert.Equal(new MarkPose(0, 1, 0, 0, 1, 1, 0, 1, 0, 0), SetupMark.PopPose(0, calm: true));
            Assert.Equal(.5, SetupMark.PopPose(.1, true).TileOpacity, 6);
            Assert.Equal(MarkPose.Shown, SetupMark.PopPose(.2, true));

            var frames = new ManualFrameSource();
            var mark = new SetupMark(72, () => true, drifts: true);
            mark.Attach(new Animator(frames));
            mark.Pop();
            frames.Run(.25);
            Assert.Equal(MarkPose.Shown, mark.Pose);
            Assert.False(mark.IsDrifting);
            frames.Run(5);
            Assert.Equal(MarkPose.Shown, mark.Pose);
            mark.StartDriftMidPath();
            Assert.False(mark.IsDrifting);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_held_mark_stops_where_it_is_and_carries_on_from_there()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var mark = new SetupMark(72, () => false, drifts: true);
            mark.Attach(new Animator(frames));
            mark.Pop();
            frames.Run(.5);
            var at = mark.PopElapsed;
            var pose = mark.Pose;
            mark.Pause();
            frames.Run(1);
            Assert.Equal(at, mark.PopElapsed);
            Assert.Equal(pose, mark.Pose);
            mark.Resume();
            frames.Run(.1);
            Assert.InRange(mark.PopElapsed, at + .09, at + .11);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_mark_pops_once_per_window_on_its_first_screen_not_on_the_way_back_from_Options()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);
            rig.Run(.1);
            Assert.InRange(welcome.MarkView.PopElapsed, .05, .2);
            Assert.True(welcome.MarkView.IsDrifting);

            rig.Run(2);
            var drift = welcome.MarkView.Pose;
            session.Go(SetupScreen.Options);
            rig.Run(1);
            Assert.Equal(drift.DriftX, welcome.MarkView.Pose.DriftX, 6);
            session.Go(SetupScreen.Welcome);
            rig.Run(.2);
            Assert.Equal(-1, welcome.MarkView.PopElapsed);
            Assert.True(welcome.MarkView.IsDrifting);
            Assert.Equal(1, welcome.MarkView.Pose.Draw);
            Assert.False(session.TakeFirstPop());
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(SetupScreen.Update, "1.0.0")]
    [InlineData(SetupScreen.UpToDate, "1.1.0")]
    [InlineData(SetupScreen.NewerInstalled, "1.2.0")]
    [Trait("Decision", "OD-0072")]
    public void Each_first_screen_pops_the_mark(SetupScreen first, string installed)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(first, installed);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(.1);
            var mark = ((CanvasScreen)session.Current!).Column.Children.OfType<SetupMark>().Single();
            Assert.InRange(mark.PopElapsed, .05, .2);
            rig.Run(2);
            Assert.Equal(MarkPose.Shown, mark.Pose with { DriftX = 0, DriftY = 0 });
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Ready_pops_the_mark_again_after_Welcome_and_Installing()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(2);
            session.Go(SetupScreen.Installing);
            rig.Run(1);
            var installingMark = ((CanvasScreen)session.Current!).Column.Children.OfType<SetupMark>().Single();
            Assert.Equal(-1, installingMark.PopElapsed);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Off), false);
            session.Go(SetupScreen.Ready);
            rig.Run(.1);
            var ready = Assert.IsType<ReadyScreen>(session.Current);
            Assert.InRange(ready.MarkView.PopElapsed, .05, .2);
            rig.Window.CloseNow();
        });
    }

    private static (double, double) Round((double X, double Y) p) => (Math.Round(p.X, 4), Math.Round(p.Y, 4));
}
