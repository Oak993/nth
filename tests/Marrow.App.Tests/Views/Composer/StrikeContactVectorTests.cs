using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeContactVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);
    private static dynamic Contacts(int seed, StrikeBubbleLanes lanes, StrikeFastClock clock)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeContacts"); Assert.NotNull(type); return Activator.CreateInstance(type, [seed, lanes, clock])!;
    }
    private static StrikeGeometry Geometry(string name) => name switch { "pageBig" => new(520, 40, 335.25, 28), "pageSmall" => new(300, 26, 280, 18), _ => new(298, 34, 272, 24) };

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_event_spark_and_frame_burst_matches_all_six_locked_runs_in_stable_order()
    {
        using var doc = StrikeVectors.Read("bursts.json");
        foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
        {
            var g = Geometry(run.GetProperty("geometry").GetString()!); var seed = run.GetProperty("seed").GetInt32(); var storm = new StrikeStorm(seed);
            var lanes = new StrikeBubbleLanes(run.GetProperty("bub").GetString()!, g.Width, g.Height); var clock = new StrikeFastClock(); dynamic contacts = Contacts(seed, lanes, clock);
            var off = run.GetProperty("off").GetDouble(); var ink = run.GetProperty("ink").GetDouble(); var expected = run.GetProperty("bursts"); var index = 0;
            double time = 0; var frameIndex = 0; var irregular = run.GetProperty("dt").GetString() == "irregular"; var dt = doc.RootElement.GetProperty("irregularDt");
            // Uniform source inputs, gen-vectors.mjs:348; JSON stores rounded versions of these fractions.
            double[] exactDt = [1 / 60d, 1 / 240d, 1 / 30d, 1 / 120d, 0.05, 1 / 60d, 0.025, 1 / 144d, 0.0333];
            for (var i = 0; i < exactDt.Length; i++) Near(dt[i].GetDouble(), exactDt[i]);
            while (time <= run.GetProperty("dur").GetDouble() + 1e-9)
            {
                var frame = storm.Resolve(g, time + off, time, clock.FastAt(time), ink); contacts.Process(g, storm.Plans, frame, time, off, ink);
                for (var i = 0; i < (int)contacts.NewCount; i++)
                {
                    Assert.True(index < expected.GetArrayLength()); var e = expected[index++]; dynamic b = contacts.NewBursts[i];
                    try
                    {
                        Near(e.GetProperty("t").GetDouble(), (double)b.T); Assert.Equal(e.GetProperty("i").GetInt32(), (int)b.Lane); Assert.Equal(e.GetProperty("rise").GetInt32(), (int)b.Rise);
                        Near(e.GetProperty("x").GetDouble(), (double)b.X); Near(e.GetProperty("y").GetDouble(), (double)b.Y); Near(e.GetProperty("r").GetDouble(), (double)b.Radius); Near(e.GetProperty("a").GetDouble(), (double)b.Alpha); Near(e.GetProperty("rot").GetDouble(), (double)b.Rotation);
                        Assert.Equal(e.GetProperty("n").GetInt32(), (int)b.N); Assert.Equal(e.GetProperty("k").GetInt32(), (int)b.K); Assert.Equal(e.GetProperty("src").GetString(), (int)b.Source switch { 0 => "event", 1 => "spark", _ => "frame" });
                    }
                    catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"run={run.GetProperty("name")} burst={index - 1} expected={e.GetRawText()} actual={b}: {error.Message}"); }
                }
                if (irregular) time += exactDt[frameIndex++ % exactDt.Length]; else time = ++frameIndex / 60d;
            }
            Assert.Equal(run.GetProperty("frames").GetInt32(), irregular ? frameIndex : frameIndex); Assert.Equal(run.GetProperty("count").GetInt32(), index); Assert.Equal(expected.GetArrayLength(), index);
        }
    }

    [Theory]
    [InlineData("Claude")]
    [InlineData("OpenAI")]
    [InlineData("Copilot")]
    [InlineData("AWS")]
    [InlineData("Arcee")]
    [Trait("Decision", "ED-248")]
    public void Independent_distance_audit_finds_no_intact_touched_bubble_and_a_burst_for_every_key(string maker)
    {
        var g = Geometry("appMax"); var storm = new StrikeStorm(1274); var lanes = new StrikeBubbleLanes("53,9,3,1", 298, 34); var clock = new StrikeFastClock(); dynamic contacts = Contacts(1274, lanes, clock);
        var ink = StrikeInk.Of(StrikeOracleLiquid.From(maker)).Amount; var burstKeys = new HashSet<(int Lane, int Rise)>();
        for (var n = 0; n <= 3600; n++)
        {
            var t = n / 60d; var frame = storm.Resolve(g, t + 88486.63, t, clock.FastAt(t), ink); contacts.Process(g, storm.Plans, frame, t, 88486.63, ink);
            for (var j = 0; j < (int)contacts.NewCount; j++) { dynamic b = contacts.NewBursts[j]; burstKeys.Add(((int)b.Lane, (int)b.Rise)); }
            for (var i = 0; i < lanes.Count; i++)
            {
                var bubble = lanes.Now(i, clock.ClockB(t), clock.FastAt(t)); if (bubble.Alpha < 0.03) continue;
                for (var j = 0; j < frame.LitCount; j++)
                {
                    var lit = frame.Lit[j]; var trace = lit.Trace; var touch = false;
                    // Independent point-to-segment projection, consuming the actual drawn jittered vertices.
                    for (var v = trace.First; v < trace.Last; v++)
                    {
                        var x0 = trace.Line.X(v); var y0 = trace.Y(v); var dx = trace.Line.X(v + 1) - x0; var dy = trace.Y(v + 1) - y0;
                        var length = dx * dx + dy * dy; var u = length == 0 ? 0 : Math.Clamp(((bubble.X - x0) * dx + (bubble.Y - y0) * dy) / length, 0, 1);
                        var ex = bubble.X - x0 - u * dx; var ey = bubble.Y - y0 - u * dy; var limit = bubble.Radius + lit.ContactHalfWidth;
                        if (ex * ex + ey * ey <= limit * limit) { touch = true; break; }
                    }
                    if (!touch) continue;
                    Assert.True((bool)contacts.IsDead(i, bubble.K), $"{maker} lane={i} rise={bubble.K} frame={n}"); Assert.Contains((i, bubble.K), burstKeys); break;
                }
            }
        }
    }
}
