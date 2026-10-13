using Marrow.UI.Port.Armilla;
namespace Marrow.UI.Tests.Port.Armilla;
[Trait("Decision", "ED-NEW-dextra-open-8")]
public sealed class ArmillaAppearanceTests
{
    [Fact]
    public void Accessibility_reduce_motion_snaps_the_appearance_and_does_not_schedule_extra_frames()
    {
        var look = new ArmillaLook { Size = 48, Dark = true, Rest = true, Reduce = true, AnimateAppearance = true, ReduceAppearance = true };
        var mark = new ArmillaEngine(look); mark.Set(look with { Dark = false });
        var current = new RecordingArmillaCanvas(); mark.Draw(current);
        var expected = new RecordingArmillaCanvas(); new ArmillaEngine(look with { Dark = false }).DrawStill(expected);
        Assert.Equal(expected.Log, current.Log);
        Assert.False(mark.Tick(1.0 / 60, 1.0 / 60));
    }
    [Theory]
    [InlineData(true, 13.8)]
    [InlineData(false, 13.8)]
    [InlineData(true, 48)]
    [InlineData(false, 48)]
    [InlineData(true, 72)]
    [InlineData(false, 72)]
    public void A_still_mark_keeps_its_geometry_and_eases_its_ink_for_point_32_seconds(bool dark, double size)
    {
        var look = new ArmillaLook { Size = size, Dark = dark, Rest = true, Reduce = true, AnimateAppearance = true };
        var mark = new ArmillaEngine(look);
        var before = new RecordingArmillaCanvas(); mark.DrawStill(before);
        mark.Set(look with { Dark = !dark });
        var click = new RecordingArmillaCanvas(); mark.Draw(click);
        Assert.Equal(before.Log, click.Log);
        for (var k = 1; k <= 19; k++) Assert.True(mark.Tick(1.0 / 60, k / 60.0));
        Assert.False(mark.Tick(1.0 / 60, 20.0 / 60));
        var end = new RecordingArmillaCanvas(); mark.Draw(end);
        var fresh = new RecordingArmillaCanvas(); new ArmillaEngine(look with { Dark = !dark }).DrawStill(fresh);
        Assert.Equal(fresh.Log, end.Log);
        Assert.NotEqual(before.Log, end.Log);
        mark.Set(look); mark.Tick(1.0 / 60, 21.0 / 60);
        var interrupted = new RecordingArmillaCanvas(); mark.Draw(interrupted);
        mark.Set(look with { Dark = !dark });
        var rebased = new RecordingArmillaCanvas(); mark.Draw(rebased);
        Assert.Equal(interrupted.Log, rebased.Log);
    }
    [Fact]
    public void A_settled_mark_requests_the_zero_delta_frame_and_keeps_ticking_until_its_palette_ends()
    {
        var look = new ArmillaLook { Size = 13.8, Rest = true, Reduce = true, AnimateAppearance = true };
        var mark = new ArmillaEngine(look); mark.DrawStill(new RecordingArmillaCanvas());
        Assert.True(mark.Settled());
        mark.Set(look with { Dark = false });
        Assert.False(mark.Settled()); // the handler's first dt=0 frame must request another frame
        Assert.True(mark.Tick(0, 0));
        Assert.False(mark.Tick(.32, .32));
        Assert.True(mark.Settled());
        var quiet = new ArmillaEngine(look with { ReduceAppearance = true });
        quiet.Set(look with { Dark = false, ReduceAppearance = true });
        Assert.True(quiet.Settled());
        Assert.False(quiet.Tick(0, 0));
    }
    [Fact]
    public void A_sleeping_still_mark_completes_its_palette_and_accessibility_reduction_stays_still()
    {
        var look = new ArmillaLook { Size = 13.8, Rest = true, Reduce = true, AnimateAppearance = true };
        var mark = new ArmillaEngine(look) { Sleeping = true };
        var before = new RecordingArmillaCanvas(); mark.DrawStill(before);
        mark.Set(look with { Dark = false });
        var click = new RecordingArmillaCanvas(); mark.Draw(click); Assert.Equal(before.Log, click.Log);
        for (var k = 1; k <= 19; k++) Assert.True(mark.Tick(1.0 / 60, k / 60.0));
        Assert.False(mark.Tick(1.0 / 60, 20.0 / 60));
        var end = new RecordingArmillaCanvas(); mark.Draw(end);
        var fresh = new RecordingArmillaCanvas(); new ArmillaEngine(look with { Dark = false }).DrawStill(fresh);
        Assert.Equal(fresh.Log, end.Log);
        var quiet = new ArmillaEngine(look with { ReduceAppearance = true }) { Sleeping = true };
        quiet.Set(look with { Dark = false, ReduceAppearance = true }); Assert.False(quiet.Tick(0, 0));
    }
}
