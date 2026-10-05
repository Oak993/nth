using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9 (V-13.3): the raw level the glow listens to is a cell the session writes at the audio rate and the render thread reads each frame, 0 to 1, a NaN counting as 0.
public sealed class VoiceGlowLevelTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.37, 0.37)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.7, 1.0)]
    [InlineData(-0.2, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void The_level_is_kept_to_0_1_and_a_nan_is_silence(double written, double read)
    {
        var level = new VoiceGlowLevel();
        Assert.Equal(0, level.Value);
        level.Value = written;
        Assert.Equal(read, level.Value);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public async Task A_write_from_another_thread_is_the_value_the_next_read_sees()
    {
        var level = new VoiceGlowLevel();
        await Task.Run(() => { for (var i = 0; i <= 1000; i++) level.Value = i / 1000.0; });
        Assert.Equal(1.0, level.Value);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T62")]
    public void Writing_the_level_allocates_nothing()
    {
        var level = new VoiceGlowLevel();
        level.Value = 0.1;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) level.Value = (i % 100) / 100.0;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
