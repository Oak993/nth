using Marrow.Core.Port.SelfUpdate;

namespace Marrow.Core.Tests.Port.SelfUpdate;

// UE-1: Velopack reports whole percent; the install pill takes a fraction.
public sealed class SelfUpdateProgressTests
{
    [Theory]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.01)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(-5, 0.0)]
    [InlineData(140, 1.0)]
    public void Percent_becomes_a_fraction_clamped_to_0_to_1(int percent, double fraction) =>
        Assert.Equal(fraction, SelfUpdateProgress.Fraction(percent), 12);

    [Fact]
    [Trait("Feature", "UE-1")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_fail_closed_engine_is_not_an_install_and_does_nothing()
    {
        var engine = new FailClosedSelfUpdateEngine();
        Assert.False(engine.IsInstalled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CheckAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.DownloadAsync("1.0.0", _ => { }, CancellationToken.None));
        Assert.Throws<InvalidOperationException>(engine.WaitExitThenApplyAndRestart);
    }
}
