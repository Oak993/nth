using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

[Trait("Decision", "ED-NEW-dextra-smooth-14")]
public sealed class DextraSmoothRestPoseTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    public void The_continuous_rest_target_is_the_source_still_drawing_at_small_and_large_sizes(int hz)
    {
        foreach (var state in Enum.GetValues<ArmillaState>())
        foreach (var size in new[] { 20.0, 176.0 })
        {
            var look = new ArmillaLook { Size = size, State = state };
            var mark = new ArmillaEngine(look);
            for (var i = 0; i < hz * 3; i++) mark.Tick(1.0 / hz, i / (double)hz);
            mark.Sleeping = true;
            // Pure target proof: continue the spring to its asymptote. The handler's
            // earlier stop uses the separately guarded existing settled thresholds.
            for (var i = 0; i < hz * 18; i++) mark.Tick(1.0 / hz, 3 + i / (double)hz);
            var actual = new RecordingArmillaCanvas(); mark.Draw(actual);
            var source = new ArmillaEngine(look with { Reduce = true });
            var expected = new RecordingArmillaCanvas(); source.DrawStill(expected);
            Assert.True(actual.Log == expected.Log, $"{state} at {size}px does not reach the existing source still drawing at {hz} Hz\nExpected: {expected.Log}\nActual: {actual.Log}");
        }
    }
}
