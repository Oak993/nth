using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-4.1, V-4.2): the endpoint's mix format as the capture accepts it.
public sealed class DeviceFormatTests
{
    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2: 1 to 8 channels, float32 or PCM16/24/32, any positive rate
    public void Only_the_formats_of_V_4_2_are_accepted()
    {
        Assert.True(DeviceFormat.TryCreate(48000, 2, 32, true, out var f));
        Assert.Equal(new DeviceFormat(48000, 2, SampleEncoding.Float32), f);
        Assert.True(DeviceFormat.TryCreate(44100, 8, 16, false, out f));
        Assert.Equal(new DeviceFormat(44100, 8, SampleEncoding.Pcm16), f);
        Assert.True(DeviceFormat.TryCreate(96000, 1, 24, false, out f));
        Assert.Equal(new DeviceFormat(96000, 1, SampleEncoding.Pcm24), f);
        Assert.True(DeviceFormat.TryCreate(16000, 1, 32, false, out f));
        Assert.Equal(new DeviceFormat(16000, 1, SampleEncoding.Pcm32), f);
        Assert.False(DeviceFormat.TryCreate(48000, 9, 32, true, out _));
        Assert.False(DeviceFormat.TryCreate(48000, 0, 32, true, out _));
        Assert.False(DeviceFormat.TryCreate(0, 2, 16, false, out _));
        Assert.False(DeviceFormat.TryCreate(48000, 2, 8, false, out _));
        Assert.False(DeviceFormat.TryCreate(48000, 2, 64, true, out _));
        Assert.Equal(6, new DeviceFormat(48000, 2, SampleEncoding.Pcm24).BytesPerFrame);
        Assert.Equal(new CaptureFormat(44100, 3), new DeviceFormat(44100, 3, SampleEncoding.Float32).ToCaptureFormat());
    }
}
