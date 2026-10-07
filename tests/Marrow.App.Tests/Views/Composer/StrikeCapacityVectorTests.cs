using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeCapacityVectorTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Each_buffer_uses_the_locked_maximum_plus_twenty_five_percent_rounded_up()
    {
        var pool = new StrikePlans(1274); var g = new StrikeGeometry(298, 34, 272, 24); var p = pool.Get(g, 36918);
        Assert.Equal(95 * 2, p.Channels[0].Points.Length); Assert.Equal(44 * 2, p.Channels[1].Points.Length);
        Assert.Equal(8, p.Branches.Length); foreach (var b in p.Branches) Assert.Equal(9 * 2, b.Points.Length);
        var plume = StrikePlume.Geometry(p, g, 1274); Assert.Equal(249, plume.Lines.Length); Assert.Equal(1803, plume.U.Length); Assert.Equal(1803 * 2, plume.Points.Length);
        var events = p.ContactEvents(); Assert.Equal(24, events.Items.Length); foreach (var e in events.Items) Assert.Equal(10, e.Segments.Length);
    }

    [Theory]
    [InlineData("appMax", 298d, 34d, 272d, 24d)]
    [InlineData("appHigh", 298d, 34d, 188.75d, 24d)]
    [InlineData("pageBig", 520d, 40d, 335.25d, 28d)]
    [InlineData("pageSmall", 300d, 26d, 280d, 18d)]
    [Trait("Decision", "ED-248")]
    public void A_hundred_thousand_strikes_and_twins_reach_the_measured_maxima_without_buffer_growth(string name, double w, double h, double ex, double radius)
    {
        using var doc = StrikeVectors.Read("budget.json"); var expected = doc.RootElement.GetProperty("sizes").GetProperty(name);
        var maxima = new int[9]; var g = new StrikeGeometry(w, h, ex, radius); var pool = new StrikePlans(1274);
        for (var k = 0; k < 100; k++) Inspect(pool.Get(g, k));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 100000; k++) { var p = pool.Get(g, k); Inspect(p); if (p.Twin is { } twin) Inspect(twin); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var keys = new[] { "maxChannelVertices", "maxForkVertices", "maxBranches", "maxBranchVertices", "maxPlumeLines", "maxPlumeVertsTotal", "maxPlumeLineVertices", "maxEvents", "maxEventSegs" };
        for (var i = 0; i < keys.Length; i++) Assert.True(maxima[i] >= expected.GetProperty(keys[i]).GetInt32(), keys[i]);
        void Inspect(StrikePlan p)
        {
            maxima[0] = Math.Max(maxima[0], p.Channels[0].Count / 2); if (p.ChannelCount == 2) maxima[1] = Math.Max(maxima[1], p.Channels[1].Count / 2);
            maxima[2] = Math.Max(maxima[2], p.BranchCount); for (var b = 0; b < p.BranchCount; b++) maxima[3] = Math.Max(maxima[3], p.Branches[b].Count / 2);
            var plume = StrikePlume.Geometry(p, g, 1274); maxima[4] = Math.Max(maxima[4], plume.Count); maxima[5] = Math.Max(maxima[5], plume.Vertices);
            for (var i = 0; i < plume.Count; i++) maxima[6] = Math.Max(maxima[6], plume.Lines[i].N + 1);
            var events = p.ContactEvents(); maxima[7] = Math.Max(maxima[7], events.Count); for (var i = 0; i < events.Count; i++) maxima[8] = Math.Max(maxima[8], events.Items[i].Count);
        }
    }
}
