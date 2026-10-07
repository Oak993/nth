using System.Globalization;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// Phase B-01..B-04: input phases are reconstructed uniformly from gen-vectors.mjs:319-322/341.
public sealed class StrikeFrameVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);
    private static dynamic Storm(int seed)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeStorm"); Assert.NotNull(type); return Activator.CreateInstance(type, [seed])!;
    }

    private static void Ref(JsonElement expected, dynamic line)
    {
        if (expected.ValueKind == JsonValueKind.String)
        {
            var kind = (int)line.Kind; var index = ((int)line.Index).ToString(CultureInfo.InvariantCulture);
            var suffix = kind switch { 0 => "m", 1 => "f", 2 => "b" + index, 3 => "p" + index, _ => "spark" };
            var tag = ((int)line.K).ToString(CultureInfo.InvariantCulture) + ((bool)line.Twin ? "t" : "") + ":" + suffix;
            Assert.Equal(expected.GetString(), tag);
        }
        else
        {
            var points = expected.GetProperty("pts"); Assert.Equal(points.GetArrayLength() / 2, (int)line.Count);
            for (var i = 0; i < points.GetArrayLength() / 2; i++) { Near(points[i * 2].GetDouble(), (double)line.X(i)); Near(points[i * 2 + 1].GetDouble(), (double)line.Y(i)); }
        }
    }
    private static void Trace(JsonElement expected, dynamic trace)
    {
        Ref(expected.GetProperty("ref"), trace.Line); Assert.Equal(expected.GetProperty("i0").GetInt32(), (int)trace.First); Assert.Equal(expected.GetProperty("i1").GetInt32(), (int)trace.Last);
        Assert.Equal(expected.GetProperty("js").GetInt32(), (int)trace.JitterSeed); Near(expected.GetProperty("ja").GetDouble(), (double)trace.Jitter);
    }
    private static void Check(string part, bool stillOnly = false)
    {
        using var doc = StrikeVectors.Read("plume-frames.json"); var phaseIndex = 0;
        foreach (var row in doc.RootElement.GetProperty("frames").EnumerateArray())
        {
            var still = row.TryGetProperty("still", out var s) && s.GetBoolean();
            if (still != stillOnly) continue;
            var seed = still ? row.GetProperty("seed").GetInt32() : 1274; var plan = StrikeMix.Timing(row.GetProperty("k").GetInt32(), seed);
            var phase = still ? 0.035 : (phaseIndex++ % 11) switch { 0 => -plan.Lead + 0.001, 1 => -plan.Lead * 0.45, 2 => -0.001, 3 => 0, 4 => 0.03, 5 => 0.075, 6 => 0.15, 7 => 0.31, 8 => 0.62, 9 => 1.3, _ => 1.89 };
            var time = plan.At + phase; Near(row.GetProperty("T").GetDouble(), time);
            if (!still) { Near(row.GetProperty("a").GetDouble(), phase); Near(row.GetProperty("t").GetDouble(), time); }
            var g = new StrikeGeometry(298, 34, 272, 24); dynamic storm = Storm(seed);
            // frame() sets E.pageT; the two still drawTile calls retain the final live capture's value.
            var lastLive = doc.RootElement.GetProperty("frames").EnumerateArray().Last(r => !r.TryGetProperty("still", out _));
            var carriedPageTime = StrikeMix.Timing(lastLive.GetProperty("k").GetInt32(), 1274).At + 1.89;
            dynamic actual;
            if (part == "light" && !still)
            {
                var contactType = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeContacts"); Assert.NotNull(contactType);
                dynamic contacts = Activator.CreateInstance(contactType, [seed, new StrikeBubbleLanes("53,9,3,1", 298, 34), new StrikeFastClock()])!;
                // gen-vectors.mjs:323-325 primes a fresh tile twice before its recorded frame.
                for (var warm = 2; warm >= 1; warm--) { var t = time - warm / 60d; dynamic f = storm.Resolve(g, t, t, 1d, 0d); contacts.Process(g, storm.Plans, f, t, 0d, 0d); }
                actual = storm.Resolve(g, time, time, 1d, 0d); contacts.Process(g, storm.Plans, actual, time, 0d, 0d); contacts.ResolveLight(g, actual, time);
            }
            else actual = storm.Resolve(g, time, still ? carriedPageTime : time, 1d, 0d);
            try
            {
                if (part == "masks" || still)
                {
                    var masks = row.GetProperty("masks"); Assert.Equal(masks.GetArrayLength(), (int)actual.Masks.Count);
                    for (var i = 0; i < masks.GetArrayLength(); i++)
                    {
                        var e = masks[i]; dynamic a = actual.Masks.At(i); Assert.Equal(e.GetProperty("set").GetString(), (int)a.Set == 0 ? "lock" : "plume");
                        Assert.Equal(e.GetProperty("L").GetString(), (int)a.Layer switch { 0 => "o", 1 => "i", _ => "c" });
                        Trace(e.GetProperty("path"), a.Trace); Near(e.GetProperty("w").GetDouble(), (double)a.Width); Near(e.GetProperty("alpha").GetDouble(), (double)a.Alpha);
                    }
                }
                if (part == "light" || still)
                {
                    var light = row.GetProperty("light"); Assert.Equal(light.GetArrayLength(), (int)actual.LightCount);
                    for (var i = 0; i < light.GetArrayLength(); i++)
                    {
                        var e = light[i]; dynamic a = actual.Light[i]; Assert.Equal(e.GetProperty("kind").GetString(), (int)a.Kind == 0 ? "wash" : "glow"); Assert.Equal(e.GetProperty("layer").GetString(), (int)a.Layer == 0 ? "light" : "zapb"); Near(e.GetProperty("a").GetDouble(), (double)a.Alpha);
                        if ((int)a.Kind == 0) { Near(e.GetProperty("x").GetDouble(), (double)a.X); Near(e.GetProperty("y").GetDouble(), (double)a.Y); Near(e.GetProperty("r").GetDouble(), (double)a.Radius); Near(e.GetProperty("sx").GetDouble(), (double)a.ScaleX); }
                        else { Near(e.GetProperty("wide").GetDouble(), (double)a.Wide); Trace(e.GetProperty("path"), a.Trace); }
                    }
                }
                if (part == "lit")
                {
                    var lit = row.GetProperty("lit"); Assert.Equal(lit.GetArrayLength(), (int)actual.LitCount);
                    for (var i = 0; i < lit.GetArrayLength(); i++)
                    {
                        var e = lit[i]; dynamic a = actual.Lit[i]; Ref(e[0], a.Trace.Line); Assert.Equal(e[1].GetInt32(), (int)a.Trace.First); Assert.Equal(e[2].GetInt32(), (int)a.Trace.Last);
                        Assert.Equal(e[3].GetInt32(), (int)a.Trace.JitterSeed); Near(e[4].GetDouble(), (double)a.Trace.Jitter); Near(e[5].ValueKind == JsonValueKind.Null ? 0.6 : e[5].GetDouble(), (double)a.HalfWidth);
                        // A missing sixth element uses tl.hw*k (P:636); plume widths already include k.
                        Near(e[5].ValueKind == JsonValueKind.Null ? 0.6 * g.K : e[5].GetDouble(), (double)a.ContactHalfWidth);
                    }
                }
            }
            catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"part={part} k={plan.K} phase={phase:R} seed={seed}: {error.Message}"); }
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_mask_stroke_layer_reference_width_alpha_and_queue_order_matches_the_locked_frames() => Check("masks");
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_wash_and_spark_glow_matches_the_locked_light_operations() => Check("light");
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_drawn_contact_line_uses_the_locked_jitter_and_own_half_width() => Check("lit");
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Both_reduce_motion_still_frames_keep_the_mix_plume_and_light() => Check("still", true);
}
