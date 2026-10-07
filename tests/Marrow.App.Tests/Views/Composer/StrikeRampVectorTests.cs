using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeRampVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_ramp_reversal_and_both_integrated_clocks_match_without_phase_jumps()
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeFastClock"); Assert.NotNull(type); dynamic clock = Activator.CreateInstance(type)!;
        using var doc = StrikeVectors.Read("bubbles.json"); var ramp = doc.RootElement.GetProperty("ramp");
        foreach (var row in ramp.GetProperty("toggles").EnumerateArray())
        {
            var t = row[1].GetDouble(); double b = clock.ClockB(t), c = clock.ClockC(t);
            clock.SetFast(row[0].GetBoolean(), t); Assert.Equal(b, (double)clock.ClockB(t)); Assert.Equal(c, (double)clock.ClockC(t));
        }
        var time = -1d; double? lastB = null, lastC = null;
        foreach (var row in ramp.GetProperty("samples").EnumerateArray())
        {
            Near(row[0].GetDouble(), time); Near(row[1].GetDouble(), (double)clock.FastAt(time));
            double b = clock.ClockB(time), c = clock.ClockC(time); Near(row[2].GetDouble(), b); Near(row[3].GetDouble(), c);
            if (lastB is { } lb) Assert.InRange(b - lb, 0, 0.025 * 1.75 + 1e-12);
            if (lastC is { } lc) Assert.InRange(c - lc, 0, 0.025 * 1.5 + 1e-12);
            lastB = b; lastC = c; time += 0.025;
        }
    }
}
