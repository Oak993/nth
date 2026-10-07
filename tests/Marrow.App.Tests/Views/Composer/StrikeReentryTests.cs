using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeReentryTests
{
    private static dynamic State(ScriptedStrikeDraws draws, double? offset = null)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeReentry"); Assert.NotNull(type); return Activator.CreateInstance(type, [1274, draws, offset])!;
    }
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void New_tracks_start_on_the_first_frame_and_offsets_are_independent()
    {
        var a = new ScriptedStrikeDraws([0.25, 0.5, 0.4]); var b = new ScriptedStrikeDraws([0.7, 0.3, 0.9]);
        dynamic first = State(a); dynamic second = State(b); Assert.Equal(0, a.Consumed); Assert.Equal(0, b.Consumed);
        Assert.True((bool)first.BeforeFrame(10d)); Assert.Equal(3, a.Consumed); Assert.Equal(0, b.Consumed);
        Assert.True((bool)second.BeforeFrame(10d)); Assert.Equal(3, b.Consumed); Assert.NotEqual((double)first.Offset, (double)second.Offset);
        Assert.True(new StrikeStarts(1274).IsClean(10 + (double)first.Offset)); Assert.True(new StrikeStarts(1274).IsClean(10 + (double)second.Offset));
        var prior = (double)first.Offset; Assert.False((bool)first.BeforeFrame(10.01)); Near(prior, (double)first.Offset); Assert.Equal(3, a.Consumed);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Fast_on_uses_the_strict_faded_threshold_otherwise_it_waits_without_drawing_randomness()
    {
        var p = StrikeMix.Timing(36862, 1274); var offset = p.At - 0.05;
        foreach (var f in new[] { 0d, 0.009999, 0.01, 0.3, 1 })
        {
            var draws = new ScriptedStrikeDraws([0.25, 0.5, 0.4]); dynamic state = State(draws, offset); state.FastOn(0d, f);
            Assert.Equal(f >= 0.01, (bool)state.Pending); Assert.Equal(f < 0.01 ? 3 : 0, draws.Consumed);
            if (f >= 0.01) { Near(offset, (double)state.Offset); Assert.False((bool)state.BeforeFrame(0d)); Assert.Equal(0, draws.Consumed); }
            else Assert.True(new StrikeStarts(1274).IsClean((double)state.Offset));
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_pending_entry_jumps_on_the_locked_first_quiet_frame_before_resolving_the_scene()
    {
        using var doc = StrikeVectors.Read("random-entry.json"); var index = 0;
        foreach (var row in doc.RootElement.GetProperty("waitExamples").EnumerateArray())
        {
            var p = StrikeMix.Timing(row.GetProperty("k").GetInt32(), 1274);
            var phase = (index++ % 8) switch { 0 => -p.Lead - p.Charge - 0.4, 1 => -p.Lead - p.Charge + 0.01, 2 => -0.05, 3 => 0, 4 => 0.5, 5 => 1, 6 => 1.85, _ => 1.95 };
            var oldOffset = p.At + phase; Near(row.GetProperty("Tstart").GetDouble(), oldOffset);
            var draws = new ScriptedStrikeDraws(row.GetProperty("draws").EnumerateArray().Select(d => d.GetDouble()).ToArray()); dynamic state = State(draws, oldOffset); state.Request();
            var frames = row.GetProperty("framesWaited").GetInt32();
            for (var n = 0; n < frames; n++) { Assert.False((bool)state.BeforeFrame(n / 60d)); Assert.True((bool)state.Pending); Near(oldOffset, (double)state.Offset); Assert.Equal(0, draws.Consumed); }
            var pageTime = frames / 60d; Assert.True((bool)state.BeforeFrame(pageTime)); Assert.False((bool)state.Pending); Assert.Equal(3, draws.Consumed);
            Near(row.GetProperty("u").GetDouble(), pageTime + (double)state.Offset); Near(row.GetProperty("newOffMinusT").GetDouble(), (double)state.Offset);
            var g = new StrikeGeometry(298, 34, 272, 24); var oldFrame = new StrikeStorm(1274).Resolve(g, pageTime + oldOffset, pageTime, 1, 0);
            var newFrame = new StrikeStorm(1274).Resolve(g, pageTime + (double)state.Offset, pageTime, 1, 0);
            Assert.Equal(0, oldFrame.Masks.Count); Assert.Equal(0, newFrame.Masks.Count); Assert.Equal(1, oldFrame.LightCount); Assert.Equal(1, newFrame.LightCount);
            Near(oldFrame.Light[0].Alpha, newFrame.Light[0].Alpha); Near(oldFrame.Light[0].X, newFrame.Light[0].X); Near(oldFrame.Light[0].Radius, newFrame.Light[0].Radius);
        }
    }
}
