using System.Reflection;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// TESTS A-03..A-06; SPEC K-018..K-040, K-057, K-067.
public sealed class StrikeMixTimingVectorTests
{
    private static MethodInfo Method(string name)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeMix");
        Assert.NotNull(type);
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method); return method;
    }

    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    private static void Compare(JsonElement row, dynamic actual, bool twin)
    {
        foreach (var (key, value) in new (string, double)[] { ("at", actual.At), ("lead", actual.Lead), ("reach", actual.Reach), ("str", actual.Strength), ("charge", actual.Charge) }) Near(row.GetProperty(key).GetDouble(), value);
        Assert.Equal(row.GetProperty("steps").GetInt32(), (int)actual.Steps);
        Assert.Equal(row.GetProperty("partial").GetBoolean(), (bool)actual.Partial);
        Assert.Equal(row.GetProperty("fam").GetInt32(), (int)actual.Family);
        if (!twin)
        {
            Assert.Equal(row.GetProperty("k").GetInt32(), (int)actual.K);
            Assert.Equal(row.GetProperty("sty").GetInt32(), (int)actual.Style);
            Assert.Equal(row.GetProperty("ev").GetInt32(), (int)actual.Event);
        }
        else { Assert.Equal(0, (int)actual.Event); Assert.Equal(0d, (double)actual.Charge); }
        var flick = row.GetProperty("flick"); Assert.Equal(flick.GetArrayLength(), (int)actual.FlickerCount);
        for (var i = 0; i < flick.GetArrayLength(); i++)
        {
            dynamic f = actual.Flicker(i);
            Near(flick[i][0].GetDouble(), (double)f.At); Near(flick[i][1].GetDouble(), (double)f.Decay); Near(flick[i][2].GetDouble(), (double)f.Gain);
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_timing_flicker_twin_and_spark_matches_the_locked_ranges()
    {
        var timing = Method("Timing"); var spark = Method("Spark");
        using var doc = StrikeVectors.Read("plan-timing.json");
        foreach (var seedRows in doc.RootElement.GetProperty("seeds").EnumerateObject())
        {
            var seed = int.Parse(seedRows.Name, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var row in seedRows.Value.EnumerateArray())
            {
                var k = row.GetProperty("k").GetInt32(); dynamic actual = timing.Invoke(null, [k, seed, false])!;
                Compare(row, actual, false);
                var twin = row.TryGetProperty("twin", out var tw); Assert.Equal(twin, (int)actual.Event == 2);
                if (twin) Compare(tw, timing.Invoke(null, [k, seed, true])!, true);
                var expected = row.GetProperty("spark"); dynamic? found = spark.Invoke(null, [k, seed]);
                if (expected.ValueKind == JsonValueKind.Null) Assert.Null(found);
                else
                {
                    Assert.NotNull(found); dynamic present = found!;
                    Near(expected.GetProperty("ts").GetDouble(), (double)present.At);
                    Assert.Equal(expected.TryGetProperty("blocked", out var b) && b.GetBoolean(), (bool)present.Blocked);
                }
            }
        }
        var page = doc.RootElement.GetProperty("pageRareFromOff");
        foreach (var row in page.GetProperty("rare").EnumerateArray())
        {
            dynamic actual = timing.Invoke(null, [row.GetProperty("k").GetInt32(), page.GetProperty("seed").GetInt32(), false])!;
            Assert.Equal(row.GetProperty("ev").GetInt32(), (int)actual.Event);
            Near(row.GetProperty("sinceOff").GetDouble(), (double)actual.At - page.GetProperty("off").GetDouble());
        }
    }

    [Theory]
    [InlineData(110)]
    [InlineData(207)]
    [InlineData(1274)]
    [InlineData(2000000011)]
    [Trait("Decision", "ED-248")]
    public void All_hundred_thousand_strike_statistics_match_exactly(int seed)
    {
        var timing = Method("Timing"); var spark = Method("Spark");
        using var doc = StrikeVectors.Read("plan-stats.json");
        var expected = doc.RootElement.GetProperty("seeds").GetProperty(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        int[] styles = new int[4], events = new int[5], flickers = new int[5];
        int partial = 0, twins = 0, adjacentRare = 0, adjacentA2 = 0, sameFamily = 0, sparks = 0, blocked = 0;
        double gapMin = double.PositiveInfinity, gapMax = double.NegativeInfinity, gapSum = 0,
            leadMin = double.PositiveInfinity, leadMax = 0, chargeMin = double.PositiveInfinity, chargeMax = 0;
        int previousStyle = -1, previousFamily = -2; double previousAt = 0;
        for (var k = 0; k < 100_000; k++)
        {
            dynamic t = timing.Invoke(null, [k, seed, false])!;
            int style = t.Style, family = t.Family, ev = t.Event; double at = t.At, lead = t.Lead, charge = t.Charge;
            styles[style]++; events[ev]++; flickers[(int)t.FlickerCount]++;
            if ((bool)t.Partial) partial++; if (ev == 2) twins++;
            leadMin = Math.Min(leadMin, lead); leadMax = Math.Max(leadMax, lead);
            if (charge > 0) { chargeMin = Math.Min(chargeMin, charge); chargeMax = Math.Max(chargeMax, charge); }
            if (k > 0)
            {
                var gap = at - previousAt; gapSum += gap; gapMin = Math.Min(gapMin, gap); gapMax = Math.Max(gapMax, gap);
                if (style == 3 && previousStyle == 3) adjacentRare++;
                if (style == 0 && previousStyle == 0) adjacentA2++;
                if (style != 0 && previousStyle != 0 && family == previousFamily) sameFamily++;
            }
            dynamic? s = spark.Invoke(null, [k, seed]); if (s is not null) { if ((bool)s.Blocked) blocked++; else sparks++; }
            previousStyle = style; previousFamily = family; previousAt = at;
        }
        foreach (var (name, actual) in new (string, int[])[] { ("sty", styles), ("ev", events), ("flickN", flickers) })
            Assert.Equal(expected.GetProperty(name).EnumerateArray().Select(v => v.GetInt32()), actual);
        foreach (var (name, actual) in new (string, int)[] { ("partial", partial), ("twin", twins), ("adjRare", adjacentRare), ("adjA2", adjacentA2), ("sameFamNext", sameFamily), ("sparks", sparks), ("sparksBlocked", blocked) }) Assert.Equal(expected.GetProperty(name).GetInt32(), actual);
        foreach (var (name, actual) in new (string, double)[] { ("gapMin", gapMin), ("gapMax", gapMax), ("gapMean", gapSum / 99_999), ("leadMin", leadMin), ("leadMax", leadMax), ("chargeMin", chargeMin), ("chargeMax", chargeMax) }) Near(expected.GetProperty(name).GetDouble(), actual);
    }
}
