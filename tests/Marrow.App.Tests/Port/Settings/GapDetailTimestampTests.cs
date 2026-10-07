using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
public sealed class GapDetailTimestampTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")] // OD-0126; locked customization gen.py .xf>span
    public void The_detail_out_step_never_exceeds_the_approved_curve_at_the_recorded_frame_times() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(1500);
        rig.Do(() => rig.Page.Beam!.Motion.Control.Select("pulse"));
        var start = rig.Get(() => SwiftClock.Now);
        var frameStart = rig.Frames.Now;
        var previous = rig.Get(() => rig.Page.Beam!.Motion.Row.Detail.OutgoingOpacity);
        var previousOracle = GapDetailFrameOracle.Opacity(TimeSpan.Zero);
        Assert.Equal(1, previous);
        for (var i = 1; i <= 24; i++)
        {
            rig.Advance(FrameCurves.FrameMs, settle: false);
            var elapsed = rig.Get(() => SwiftClock.Now - start);
            Assert.Equal(rig.Frames.Now - frameStart, elapsed);
            var actual = rig.Get(() => rig.Page.Beam!.Motion.Row.Detail.OutgoingOpacity);
            var expected = GapDetailFrameOracle.Opacity(elapsed);
            var step = previous - actual;
            var designStep = previousOracle - expected;
            Assert.True(actual <= previous, $"frame {i}: opacity reverses");
            Assert.True(step <= .15 + 1e-9 || step <= designStep,
                $"frame {i} at {elapsed.Ticks} ticks: step {step:R}, approved step {designStep:R}");
            previous = actual;
            previousOracle = expected;
        }
        Assert.Equal(0, previous);
    });
}
