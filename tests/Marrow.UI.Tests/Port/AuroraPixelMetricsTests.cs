using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

public sealed class AuroraPixelMetricsTests
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void Invisible_RGB_differences_have_no_contribution_but_alpha_and_each_visible_channel_do()
    {
        var invisible = new AuroraPixelMetrics();
        invisible.Add(new SKColor(255, 255, 255, 0), new SKColor(0, 0, 0, 0));
        Assert.True(invisible.Accepted); Assert.All(invisible.Peak, x => Assert.Equal(0, x));
        Assert.Equal(255, invisible.StraightPeak[0]);
        var alpha = new AuroraPixelMetrics();
        alpha.Add(new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 25));
        Assert.False(alpha.Accepted); Assert.Equal(25, alpha.Peak[3]);
        var channel = new AuroraPixelMetrics();
        channel.Add(new SKColor(0, 0, 0, 255), new SKColor(1, 0, 0, 255));
        Assert.False(channel.Accepted); // R mean 1, even though RGBA average is only .25.
    }
}
