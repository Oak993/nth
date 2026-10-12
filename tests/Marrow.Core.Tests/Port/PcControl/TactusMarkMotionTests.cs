// PORT-ONLY: Tactus's mark in motion (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). Stepped frame by frame on a plain clock:
// still between actions; one press per action, at most one per 450 ms, landing on the pointer's ripple; a typing burst
// holds the press; Paused springs into the lift pose without a bounce; arrival and leaving through the poses; Reduce
// motion without any press and with immediate poses.
using Marrow.Core.Port.PcControl.Mark;

namespace Marrow.Core.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-11")]
public sealed class TactusMarkMotionTests
{
    private const int Frame = 8;

    [Fact]
    public void A_new_mark_is_still_in_its_base_pose()
    {
        var motion = new TactusMarkMotion();

        Assert.False(motion.Moving);
        Assert.False(motion.Step(1_000));
        Assert.Equal(TactusGlyph.Draw(TactusPose.Rest).ToSvg(), motion.Path.ToSvg());
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), new TactusMarkMotion(paused: true).Path.ToSvg());
    }

    [Fact]
    public void A_press_winds_up_100_ms_before_contact_is_down_at_contact_and_springs_back_without_a_bounce()
    {
        var motion = new TactusMarkMotion();
        motion.Step(10_000);

        Assert.True(motion.Press(10_000, contactInMs: 300));

        var gap = Run(motion, 10_000, 10_000 + 1_200);
        // Still until the wind-up, 100 ms before contact.
        Assert.All(gap.Where(g => g.At < 10_200), g => Assert.Equal(3, g.Gap, 6));
        // The wind-up opens the cut (toward 3.6) before the press closes it.
        Assert.Contains(gap, g => g.At > 10_200 && g.At < 10_235 && g.Gap > 3.02);
        // Down at contact: within 0.25 units of the press pose's gap (1.2).
        var atContact = gap.First(g => g.At >= 10_300);
        Assert.InRange(atContact.Gap, 1.2 - 0.25, 1.2 + 0.25);
        // Back at rest within the frame budget, never past rest (no bounce above 3 after the spring back starts).
        Assert.All(gap.Where(g => g.At > 10_420), g => Assert.True(g.Gap <= 3 + 0.03, "overshoot " + g.Gap + " at " + g.At));
        Assert.Equal(3, gap[^1].Gap, 6);
        Assert.False(motion.Moving);
    }

    [Fact]
    public void Presses_closer_than_450_ms_count_once()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);

        Assert.True(motion.Press(1_000));
        Assert.False(motion.Press(1_200));
        Assert.False(motion.Press(1_000 + 100 + 449));
        Assert.True(motion.Press(1_000 + 100 + 450));
    }

    [Fact]
    public void A_late_contact_presses_at_once()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);

        Assert.True(motion.Press(1_000, contactInMs: 0));
        var gap = Run(motion, 1_000, 1_100);

        Assert.True(gap.Min(g => g.Gap) < 2, "the press went down");
    }

    [Fact]
    public void A_typing_burst_holds_the_press_until_it_ends()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);

        motion.Hold(1_000, true);
        var held = Run(motion, 1_000, 2_500);
        Assert.True(motion.Held);
        Assert.InRange(held[^1].Gap, 1.2 - 0.01, 1.2 + 0.01);
        Assert.False(motion.Press(2_500), "no press while held");

        motion.Hold(2_500, false);
        var released = Run(motion, 2_500, 3_500);
        Assert.False(motion.Held);
        Assert.Equal(3, released[^1].Gap, 6);
    }

    [Fact]
    public void Paused_springs_into_the_lift_pose_without_a_bounce_and_resume_springs_back()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);

        motion.SetPaused(1_000, true);
        var lifting = Run(motion, 1_000, 2_500);
        Assert.Equal(TactusPose.Lift, motion.Base);
        Assert.All(lifting, g => Assert.True(g.Gap <= 5 + 1e-6, "past the lift: " + g.Gap));
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), motion.Path.ToSvg());

        motion.SetPaused(2_500, false);
        var resuming = Run(motion, 2_500, 4_000);
        Assert.All(resuming, g => Assert.True(g.Gap >= 3 - 1e-6, "past rest: " + g.Gap));
        Assert.Equal(TactusGlyph.Draw(TactusPose.Rest).ToSvg(), motion.Path.ToSvg());
    }

    [Fact]
    public void Pausing_ends_a_held_press()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);
        motion.Hold(1_000, true);
        Run(motion, 1_000, 1_400);

        motion.SetPaused(1_400, true);
        Run(motion, 1_400, 3_000);

        Assert.False(motion.Held);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), motion.Path.ToSvg());
    }

    [Fact]
    public void The_mark_arrives_from_its_tip_and_leaves_back_to_it_with_no_fade()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);

        motion.Enter(1_000);
        Assert.Equal(0, motion.Values[TactusGlyph.Scale]);
        var arriving = Run(motion, 1_000, 2_500, TactusGlyph.Scale);
        Assert.Contains(arriving, g => g.Gap > 0.5 && g.At < 1_200);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Rest).ToSvg(), motion.Path.ToSvg());

        motion.Leave(2_500);
        Run(motion, 2_500, 4_000);
        Assert.Equal(0, motion.Values[TactusGlyph.Scale], 6);
        Assert.Equal(0, motion.Values[TactusGlyph.Gap], 6);
    }

    [Fact]
    public void Reduce_motion_never_presses_or_holds_and_changes_poses_at_once()
    {
        var motion = new TactusMarkMotion(reduceMotion: true);

        Assert.False(motion.Press(1_000));
        motion.Hold(1_000, true);
        Assert.False(motion.Held);
        Assert.False(motion.Moving);

        motion.SetPaused(1_000, true);
        Assert.False(motion.Moving);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), motion.Path.ToSvg());

        motion.Enter(1_000);
        Assert.False(motion.Moving);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Lift).ToSvg(), motion.Path.ToSvg());
    }

    [Fact]
    public void Turning_reduce_motion_on_mid_press_stops_on_the_base_pose()
    {
        var motion = new TactusMarkMotion();
        motion.Step(0);
        motion.Press(1_000, contactInMs: 0);
        Run(motion, 1_000, 1_040);

        motion.ReduceMotion = true;

        Assert.False(motion.Moving);
        Assert.Equal(TactusGlyph.Draw(TactusPose.Rest).ToSvg(), motion.Path.ToSvg());
    }

    /// <summary>Steps the motion every 8 ms from <paramref name="from"/> to <paramref name="to"/>; one value per frame.</summary>
    private static List<(long At, double Gap)> Run(TactusMarkMotion motion, long from, long to, int parameter = TactusGlyph.Gap)
    {
        var frames = new List<(long At, double Gap)>();
        for (var at = from; at <= to; at += Frame)
        {
            motion.Step(at);
            frames.Add((at, motion.Values[parameter]));
        }
        return frames;
    }
}
