using Avalonia.Controls;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 and the canvas's "Screen change" row; unit U1 (ED-NEW-inst-u1-1).
// The old screen fades (0.34 s) and lifts 6 DIP (0.42 s); the new one rises 8 DIP and fades in 60 ms later (0.38 s and
// 0.46 s) on cubic-bezier(.2,.8,.2,1); a screen asked back while leaving turns around from where it is; Calm only fades
// (0.2 s). The body only grows while the window is open, so the window never jumps smaller. Frames are driven by hand.
[Collection("SetupHeadless")]
public sealed class ScreenHostTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_first_screen_is_shown_without_motion()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var first = new Border { Height = 100 };

            rig.Window.Body.Show(first, immediately: true);

            Assert.Same(first, rig.Window.Body.Current);
            Assert.Equal((1, 0), rig.Window.Body.Pose(first));
            Assert.False(rig.Frames.HasPendingFrame);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_screen_change_lifts_the_old_screen_away_and_raises_the_new_one_in()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var body = rig.Window.Body;
            var first = new Border { Height = 100 };
            var second = new Border { Height = 100 };
            Control? left = null;
            body.Left += screen => left = screen;
            body.Show(first, immediately: true);

            body.Show(second);
            Assert.Equal((0, ScreenHost.ArriveRise), body.Pose(second));
            Assert.True(second.IsHitTestVisible);
            Assert.False(first.IsHitTestVisible);

            rig.Frames.Run(0.05);
            Assert.Equal(0, body.Pose(second).Opacity); // the arrival waits 60 ms
            Assert.True(body.Pose(first).Opacity < 1);

            rig.Frames.Run(0.15);
            Assert.True(body.Pose(first).Offset < 0, "the old screen lifts");
            Assert.InRange(body.Pose(second).Offset, 0.01, ScreenHost.ArriveRise - 0.01);
            Assert.InRange(body.Pose(second).Opacity, 0.01, 0.99);

            rig.Frames.Run(0.5);
            Assert.Equal((1, 0), body.Pose(second));
            Assert.DoesNotContain(first, body.Children);
            Assert.Same(first, left);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_screen_asked_back_while_leaving_turns_around_from_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var body = rig.Window.Body;
            var first = new Border { Height = 100 };
            var second = new Border { Height = 100 };
            body.Show(first, immediately: true);
            body.Show(second);
            rig.Frames.Run(0.1);
            var (opacity, offset) = body.Pose(first);

            body.Show(first);
            rig.Frames.Frame(1.0 / 60);

            Assert.True(Math.Abs(body.Pose(first).Opacity - opacity) < 0.15, "it jumped");
            Assert.True(Math.Abs(body.Pose(first).Offset - offset) < 1.5, "it jumped");
            rig.Frames.Run(1);
            Assert.Equal((1, 0), body.Pose(first));
            Assert.Contains(first, body.Children);
            Assert.DoesNotContain(second, body.Children);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_only_fades()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(WindowRig.Calm);
            var body = rig.Window.Body;
            var first = new Border { Height = 100 };
            var second = new Border { Height = 100 };
            body.Show(first, immediately: true);

            body.Show(second);
            for (var i = 0; i < 20; i++)
            {
                rig.Frames.Frame(1.0 / 60);
                Assert.Equal(0, body.Pose(second).Offset);
            }

            Assert.Equal((1, 0), body.Pose(second));
            Assert.DoesNotContain(first, body.Children);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_body_only_grows_until_its_height_is_reset()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var body = rig.Window.Body;
            var tall = new Border { Height = 300 };
            var short_ = new Border { Height = 120 };

            body.Show(tall, immediately: true);
            body.Measure(new Avalonia.Size(600, double.PositiveInfinity));
            Assert.Equal(300, body.DesiredSize.Height);

            body.Show(short_, immediately: true);
            body.Measure(new Avalonia.Size(600, double.PositiveInfinity));
            Assert.Equal(300, body.DesiredSize.Height);

            body.ResetHeight();
            body.Measure(new Avalonia.Size(600, double.PositiveInfinity));
            Assert.Equal(120, body.DesiredSize.Height);
        });
    }
}
