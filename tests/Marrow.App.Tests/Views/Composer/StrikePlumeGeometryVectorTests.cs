using System.Reflection;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikePlumeGeometryVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_plume_vertex_weight_level_id_and_order_matches_the_locked_engine()
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikePlume"); Assert.NotNull(type);
        var method = type.GetMethod("Geometry", BindingFlags.Public | BindingFlags.Static); Assert.NotNull(method);
        using var doc = StrikeVectors.Read("plan-geometry.json"); var seed = doc.RootElement.GetProperty("seed").GetInt32();
        var pool = new StrikePlans(seed);
        foreach (var geo in doc.RootElement.GetProperty("geometries").EnumerateObject())
        {
            var g = geo.Value.GetProperty("g"); var geometry = new StrikeGeometry(g.GetProperty("w").GetDouble(), g.GetProperty("h").GetDouble(), g.GetProperty("ex").GetDouble(), g.GetProperty("kr").GetDouble());
            foreach (var row in geo.Value.GetProperty("strikes").EnumerateArray())
            {
                try
                {
                    Compare(row, pool.Get(geometry, row.GetProperty("k").GetInt32()), geometry);
                    if (row.TryGetProperty("twin", out var twin)) Compare(twin, pool.Get(geometry, row.GetProperty("k").GetInt32(), true), geometry);
                }
                catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"{geo.Name} k={row.GetProperty("k")} : {error.Message}"); }
            }
        }
        void Compare(JsonElement row, StrikePlan plan, StrikeGeometry geometry)
        {
            dynamic plume = method.Invoke(null, [plan, geometry, seed])!;
            if (row.TryGetProperty("plumeSummary", out var summary))
            {
                Assert.Equal(summary.GetProperty("lines").GetInt32(), (int)plume.Count);
                var levels = new Dictionary<int, int>(); var max = 0;
                for (var i = 0; i < (int)plume.Count; i++) { dynamic line = plume.Lines[i]; var level = (int)line.Level; levels[level] = levels.GetValueOrDefault(level) + 1; max = Math.Max(max, (int)line.N); }
                Assert.Equal(summary.GetProperty("maxN").GetInt32(), max);
                foreach (var level in summary.GetProperty("byLevel").EnumerateObject()) Assert.Equal(level.Value.GetInt32(), levels[int.Parse(level.Name, System.Globalization.CultureInfo.InvariantCulture)]);
                return;
            }
            var expected = row.GetProperty("plume"); Assert.Equal(expected.GetArrayLength(), (int)plume.Count);
            for (var i = 0; i < expected.GetArrayLength(); i++)
            {
                var e = expected[i]; dynamic line = plume.Lines[i]; var start = (int)line.Start; var n = (int)line.N;
                Assert.Equal(e.GetProperty("id").GetInt32(), (int)line.Id); Assert.Equal(e.GetProperty("lvl").GetInt32(), (int)line.Level);
                Assert.Equal(e.GetProperty("n").GetInt32(), n); Near(e.GetProperty("w").GetDouble(), (double)line.Weight);
                var points = e.GetProperty("pts"); var us = e.GetProperty("u"); Assert.Equal(2 * (n + 1), points.GetArrayLength()); Assert.Equal(n + 1, us.GetArrayLength());
                for (var j = 0; j <= n; j++) { Near(us[j].GetDouble(), (double)plume.U[start + j]); Near(points[j * 2].GetDouble(), (double)plume.Points[(start + j) * 2]); Near(points[j * 2 + 1].GetDouble(), (double)plume.Points[(start + j) * 2 + 1]); }
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_leader_return_flicker_and_grid_contact_segment_matches_the_locked_engine()
    {
        var method = typeof(StrikePlan).GetMethod("ContactEvents", BindingFlags.Public | BindingFlags.Instance); Assert.NotNull(method);
        using var doc = StrikeVectors.Read("plan-geometry.json"); var pool = new StrikePlans(doc.RootElement.GetProperty("seed").GetInt32());
        foreach (var geo in doc.RootElement.GetProperty("geometries").EnumerateObject())
        {
            var g = geo.Value.GetProperty("g"); var geometry = new StrikeGeometry(g.GetProperty("w").GetDouble(), g.GetProperty("h").GetDouble(), g.GetProperty("ex").GetDouble(), g.GetProperty("kr").GetDouble());
            foreach (var row in geo.Value.GetProperty("strikes").EnumerateArray())
            {
                Compare(row, pool.Get(geometry, row.GetProperty("k").GetInt32()));
                if (row.TryGetProperty("twin", out var twin)) Compare(twin, pool.Get(geometry, row.GetProperty("k").GetInt32(), true));
            }
        }
        void Compare(JsonElement row, StrikePlan plan)
        {
            dynamic events = method.Invoke(plan, null)!; var expected = row.GetProperty("events"); Assert.Equal(expected.GetArrayLength(), (int)events.Count);
            for (var i = 0; i < expected.GetArrayLength(); i++)
            {
                dynamic actual = events.Items[i]; Near(expected[i].GetProperty("a").GetDouble(), (double)actual.Age);
                var segs = expected[i].GetProperty("segs"); Assert.Equal(segs.GetArrayLength(), (int)actual.Count);
                for (var j = 0; j < segs.GetArrayLength(); j++)
                {
                    dynamic seg = actual.Segments[j]; var reference = segs[j][0].GetString()!;
                    var index = reference == "m" ? 0 : reference == "f" ? 1 : 2 + int.Parse(reference.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Equal(index, (int)seg.Reference); Assert.Equal(segs[j][1].GetInt32(), (int)seg.First); Assert.Equal(segs[j][2].GetInt32(), (int)seg.Last);
                }
            }
        }
    }
}
