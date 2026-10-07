using System.Reflection;
using System.Linq.Expressions;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeBubbleVectorTests
{
    private static Type Type()
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeBubbleLanes"); Assert.NotNull(type); return type;
    }
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_normal_and_nucleating_rise_field_and_every_stride_matches_the_locked_engine()
    {
        var type = Type(); var stride = type.GetMethod("Stride", BindingFlags.Static | BindingFlags.Public); Assert.NotNull(stride);
        using var doc = StrikeVectors.Read("bubbles.json");
        foreach (var row in doc.RootElement.GetProperty("stride").EnumerateArray()) Assert.Equal(row.GetProperty("s").GetInt32(), (int)stride.Invoke(null, [row.GetProperty("n").GetInt32()])!);
        for (var n = 1; n <= 104; n++)
        {
            static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
            Assert.Equal(new[] { 7, 5, 3, 2 }.FirstOrDefault(s => Gcd(s, n) == 1, 1), (int)stride.Invoke(null, [n])!);
        }
        foreach (var c in doc.RootElement.GetProperty("configs").EnumerateArray())
        {
            dynamic lanes = Activator.CreateInstance(type, [c.GetProperty("bub").GetString()!, c.GetProperty("W").GetDouble(), c.GetProperty("H").GetDouble()])!;
            Assert.Equal(c.GetProperty("nb").GetInt32(), (int)lanes.NormalCount); Assert.Equal(c.GetProperty("nn").GetInt32(), (int)lanes.NucleationCount); Assert.Equal(c.GetProperty("n").GetInt32(), (int)lanes.Count); Assert.Equal(c.GetProperty("simmer").GetBoolean(), (bool)lanes.Simmer);
            foreach (var row in c.GetProperty("rises").EnumerateArray())
            {
                object rise = lanes.Rise(row.GetProperty("i").GetInt32(), row.GetProperty("k").GetInt32());
                foreach (var field in row.EnumerateObject())
                {
                    if (field.Name is "i" or "k") continue;
                    var p = rise.GetType().GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase); Assert.NotNull(p);
                    if (field.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) Assert.Equal(field.Value.GetBoolean(), (bool)p.GetValue(rise)!);
                    else Near(field.Value.GetDouble(), (double)p.GetValue(rise)!);
                }
            }
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_tenth_second_lane_sample_wobble_fade_and_look_back_matches_the_locked_engine()
    {
        var type = Type(); using var doc = StrikeVectors.Read("bubbles.json");
        foreach (var c in doc.RootElement.GetProperty("configs").EnumerateArray())
        {
            dynamic lanes = null!; double previousF = -1, time = -2;
            foreach (var row in c.GetProperty("samples").EnumerateArray())
            {
                var f = row[0].GetDouble(); var i = row[2].GetInt32();
                if (f != previousF) { lanes = New(); previousF = f; time = -2; }
                Near(row[1].GetDouble(), time); dynamic now = lanes.Now(i, time, f);
                Assert.Equal(row[3].GetInt32(), (int)now.K); Near(row[8].GetDouble(), (double)now.Alpha);
                if (row[4].ValueKind != JsonValueKind.Null) { Near(row[4].GetDouble(), (double)now.X); Near(row[5].GetDouble(), (double)now.Y); Near(row[6].GetDouble(), (double)now.Scale); Near(row[7].GetDouble(), (double)now.Radius); }
                if (i == (int)lanes.Count - 1) time += 0.1;
            }
            lanes = New();
            foreach (var row in c.GetProperty("lookBack").EnumerateArray())
            {
                dynamic now = lanes.Now(row[1].GetInt32(), row[0].GetDouble(), 1d);
                Assert.Equal(row[2].GetInt32(), (int)now.K); Near(row[5].GetDouble(), (double)now.Alpha);
                if (row[3].ValueKind != JsonValueKind.Null) { Near(row[3].GetDouble(), (double)now.X); Near(row[4].GetDouble(), (double)now.Y); }
            }
            object New() => Activator.CreateInstance(type, [c.GetProperty("bub").GetString()!, c.GetProperty("W").GetDouble(), c.GetProperty("H").GetDouble()])!;
        }
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Twelve_warmed_lanes_cross_new_rises_without_managed_allocations()
    {
        var type = Type(); var lanes = Activator.CreateInstance(type, ["53,9,3,1", 298d, 34d])!;
        var method = type.GetMethod("Now"); Assert.NotNull(method);
        var lane = Expression.Parameter(typeof(int)); var time = Expression.Parameter(typeof(double)); var fast = Expression.Parameter(typeof(double));
        var call = Expression.Call(Expression.Constant(lanes, type), method, lane, time, fast);
        var now = Expression.Lambda<Action<int, double, double>>(Expression.Block(call, Expression.Empty()), lane, time, fast).Compile();
        for (var f = 0; f < 600; f++) for (var i = 0; i < 12; i++) now(i, f / 60d, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 600; f < 12600; f++) for (var i = 0; i < 12; i++) now(i, f / 60d, 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
