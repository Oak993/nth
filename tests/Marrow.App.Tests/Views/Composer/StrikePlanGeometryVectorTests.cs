using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// TESTS A-07/A-08, K-029..K-057, K-067/K-156, and the fixed plan ring.
public sealed class StrikePlanGeometryVectorTests
{
    private static object Pool(int seed)
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikePlans");
        Assert.NotNull(type); return Activator.CreateInstance(type, [seed])!;
    }

    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    private static void Points(JsonElement expected, double[] actual, int count)
    {
        Assert.Equal(expected.GetArrayLength(), count);
        for (var i = 0; i < count; i++) Near(expected[i].GetDouble(), actual[i]);
    }

    private static void Compare(JsonElement expected, dynamic plan)
    {
        Assert.Equal(expected.GetProperty("ext").GetInt32(), (int)plan.Extent);
        Near(expected.GetProperty("flash")[0].GetDouble(), (double)plan.FlashX);
        Near(expected.GetProperty("flash")[1].GetDouble(), (double)plan.FlashY);
        var channels = expected.GetProperty("chans"); Assert.Equal(channels.GetArrayLength(), (int)plan.ChannelCount);
        for (var i = 0; i < channels.GetArrayLength(); i++)
        {
            dynamic c = plan.Channels[i]; Points(channels[i].GetProperty("pts"), (double[])c.Points, (int)c.Count);
            if (channels[i].TryGetProperty("sp", out var split)) Assert.Equal(split.GetInt32(), (int)c.Split);
            if (channels[i].TryGetProperty("gain", out var gain)) Near(gain.GetDouble(), (double)c.Gain);
        }
        var branches = expected.GetProperty("branches"); Assert.Equal(branches.GetArrayLength(), (int)plan.BranchCount);
        for (var i = 0; i < branches.GetArrayLength(); i++)
        {
            dynamic b = plan.Branches[i]; Assert.Equal(branches[i].GetProperty("at").GetInt32(), (int)b.At);
            Points(branches[i].GetProperty("pts"), (double[])b.Points, (int)b.Count);
        }
        var orb = expected.GetProperty("orb"); Near(orb.GetProperty("th").GetDouble(), (double)plan.ExitAngle);
        Assert.Equal(orb.GetProperty("last").GetInt32(), (int)plan.LastMain);
        Assert.Equal(orb.GetProperty("idx").EnumerateArray().Select(v => v.GetInt32()), ((int[])plan.OrbIndices).Take((int)plan.OrbCount));
        Points(orb.GetProperty("seg"), (double[])plan.OrbSegment, 2 * (int)plan.OrbCount);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_selected_path_fork_branch_flash_and_orb_exit_matches_the_locked_geometry()
    {
        using var doc = StrikeVectors.Read("plan-geometry.json");
        var pool = Pool(doc.RootElement.GetProperty("seed").GetInt32()); dynamic dynamicPool = pool;
        foreach (var geo in doc.RootElement.GetProperty("geometries").EnumerateObject())
        {
            var g = geo.Value.GetProperty("g"); var geometry = new StrikeGeometry(g.GetProperty("w").GetDouble(), g.GetProperty("h").GetDouble(), g.GetProperty("ex").GetDouble(), g.GetProperty("kr").GetDouble());
            foreach (var row in geo.Value.GetProperty("strikes").EnumerateArray())
            {
                var k = row.GetProperty("k").GetInt32(); Compare(row, dynamicPool.Get(geometry, k, false));
                if (row.TryGetProperty("twin", out var twin)) Compare(twin, dynamicPool.Get(geometry, k, true));
            }
        }
        var sp = doc.RootElement.GetProperty("spark"); var app = doc.RootElement.GetProperty("geometries").GetProperty(sp.GetProperty("geometry").GetString()!).GetProperty("g");
        dynamic p = dynamicPool.Get(new StrikeGeometry(app.GetProperty("w").GetDouble(), app.GetProperty("h").GetDouble(), app.GetProperty("ex").GetDouble(), app.GetProperty("kr").GetDouble()), sp.GetProperty("k").GetInt32(), false);
        Near(sp.GetProperty("ts").GetDouble(), (double)p.SparkAt); Points(sp.GetProperty("pts"), (double[])p.SparkPoints, 8);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_eight_plan_slots_reuse_all_geometry_buffers_without_allocating_on_new_strikes()
    {
        var pool = Pool(1274); dynamic inspected = pool; Assert.Equal(8, (int)inspected.SlotCount);
        var g = Expression.Parameter(typeof(StrikeGeometry)); var k = Expression.Parameter(typeof(int));
        var method = pool.GetType().GetMethod("Get", BindingFlags.Public | BindingFlags.Instance)!;
        var call = Expression.Call(Expression.Constant(pool), method, g, k, Expression.Constant(false));
        var get = Expression.Lambda<Action<StrikeGeometry, int>>(Expression.Block(call, Expression.Empty()), g, k).Compile();
        var geometry = new StrikeGeometry(298, 34, 272, 24);
        for (var i = -3; i < 100; i++) get(geometry, i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 100; i < 4100; i++) get(geometry, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
