using Marrow.Core.Port.Voice.Light;

namespace Marrow.Core.Tests.Port.Voice;

// VT-9, V-T61 (V-13.4): vis = follow(vis, target, dt, 0.2 up, 0.17 down) on the frame clock; snapped to 0 below 0.003 and to 1 above 0.997; reduce motion jumps; the default
// 1 changes nothing.
public sealed class VoiceLightFollowerTests
{
    private const double Frame = 1.0 / 60;

    private static double Run(VoiceLightFollower f, double target, double seconds)
    {
        var steps = (int)Math.Round(seconds / Frame);
        for (var i = 0; i < steps; i++) f.Step(target, Frame, false);
        return f.Value;
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void It_starts_at_full_strength_and_a_target_of_one_changes_nothing()
    {
        var f = new VoiceLightFollower();
        Assert.Equal(1, f.Value);
        for (var i = 0; i < 600; i++) Assert.Equal(1, f.Step(1, Frame, false));
        Assert.False(f.Moving);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void Going_down_it_has_travelled_95_percent_of_the_way_in_0_51_seconds()
    {
        var f = new VoiceLightFollower();
        Assert.Equal(1 - 0.95, Run(f, 0, 0.51), 0.004);   // exp(-0.51 / 0.17) = 0.0498
        Assert.True(f.Moving);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void Going_up_it_has_travelled_95_percent_of_the_way_in_0_6_seconds()
    {
        var f = new VoiceLightFollower();
        f.Jump(0);
        Assert.Equal(0.95, Run(f, 1, 0.60), 0.004);       // 1 - exp(-0.6 / 0.2) = 0.9502
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void The_step_is_the_designs_follow_whatever_the_frame_rate()
    {
        // follow(x, t, dt, up, down): one 0.51 s step equals thirty-one 1/60 s steps (the exponential composes)
        var one = new VoiceLightFollower();
        one.Step(0, 0.51, false);
        var many = new VoiceLightFollower();
        for (var i = 0; i < 51; i++) many.Step(0, 0.01, false);
        Assert.Equal(one.Value, many.Value, 1e-9);
        Assert.Equal(Math.Exp(-0.51 / 0.17), one.Value, 1e-12);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void It_snaps_to_zero_below_0_003_and_to_one_above_0_997()
    {
        var down = new VoiceLightFollower();
        var previous = 1.0;
        var landed = false;
        for (var i = 0; i < 2000 && !landed; i++)
        {
            var v = down.Step(0, Frame, false);
            if (v == 0) { landed = true; Assert.True(previous * Math.Exp(-Frame / 0.17) < 0.003 + 1e-12, "snapped only once below 0.003"); }
            else Assert.True(v >= 0.003, "no value between 0 and 0.003 is ever shown");
            previous = v;
        }
        Assert.True(landed);
        Assert.False(down.Moving);

        var up = new VoiceLightFollower();
        up.Jump(0);
        var arrived = false;
        for (var i = 0; i < 2000 && !arrived; i++)
        {
            var v = up.Step(1, Frame, false);
            if (v == 1) arrived = true;
            else Assert.True(v <= 0.997, "no value between 0.997 and 1 is ever shown");
        }
        Assert.True(arrived);
        Assert.False(up.Moving);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void A_change_of_target_in_the_middle_retargets_from_where_it_is()
    {
        var f = new VoiceLightFollower();
        Run(f, 0, 0.3);
        var mid = f.Value;
        Assert.InRange(mid, 0.05, 0.95);
        Run(f, 0.35, Frame);                                   // the setting changes to "dims": from the middle value, no jump
        Assert.InRange(f.Value, mid, 0.35);                    // it rises from the middle value toward 0.35
        Assert.True(f.Value > mid);
        Run(f, 0.35, 1.5);
        Assert.Equal(0.35, f.Value, 0.004);
        Assert.Equal(0.35, f.Target);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void Under_reduce_motion_it_jumps_to_the_target()
    {
        var f = new VoiceLightFollower();
        Assert.Equal(0, f.Step(0, Frame, true));
        Assert.Equal(0.35, f.Step(0.35, Frame, true));
        Assert.Equal(1, f.Step(1, Frame, true));
        Assert.False(f.Moving);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void A_zero_or_negative_step_does_not_move_it_and_a_target_is_clamped_to_0_1()
    {
        var f = new VoiceLightFollower();
        Assert.Equal(1, f.Step(0, 0, false));
        Assert.Equal(1, f.Step(0, -1, false));
        Assert.True(f.Moving);
        Assert.Equal(1, f.Step(7, Frame, false));
        Assert.Equal(1, f.Target);
        f.Step(-3, Frame, true);
        Assert.Equal(0, f.Value);
    }
}
