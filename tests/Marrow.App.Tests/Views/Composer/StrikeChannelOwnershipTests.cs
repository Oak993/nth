using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeChannelOwnershipTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void An_inspected_channel_keeps_its_vertices_after_the_render_ring_reuses_its_slot()
    {
        var channels = new StrikeChannels(1274);
        var geometry = new StrikeGeometry(298, 34, 272, 24);
        var held = channels.Main(geometry, 0);
        var expected = (double[])held.Clone();
        for (var k = 1; k <= 12; k++) channels.Main(geometry, k);
        Assert.Equal(expected, held);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void An_inspected_branch_keeps_its_vertices_after_the_render_ring_reuses_its_slot()
    {
        var channels = new StrikeChannels(1274);
        var geometry = new StrikeGeometry(298, 34, 272, 24);
        var held = channels.Branch(geometry, 0, 0);
        var expected = (double[])held.Clone();
        for (var k = 1; k <= 12; k++) channels.Branch(geometry, k, 0);
        Assert.Equal(expected, held);
    }
}
