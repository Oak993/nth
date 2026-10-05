using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-4.2): float32 or PCM 16/24/32 become float samples; the WASAPI "silent" flag means zeros. Pure functions on spans.
public sealed class SampleConverterTests
{
    private static float[] Convert(byte[] bytes, SampleEncoding encoding, bool silent = false)
    {
        var destination = new float[SampleConverter.SamplesIn(bytes.Length, encoding)];
        var count = SampleConverter.ToFloat(bytes, encoding, silent, destination);
        Assert.Equal(destination.Length, count);
        return destination;
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2 float32 is taken as it is
    public void Float32_samples_pass_through_exactly()
    {
        var values = new[] { 0f, 0.25f, -0.5f, 1f, -1f, 0.123456f };
        var bytes = values.SelectMany(BitConverter.GetBytes).ToArray();
        Assert.Equal(values, Convert(bytes, SampleEncoding.Float32));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2 PCM16 scales by 1/32768
    public void Pcm16_scales_by_32768_with_negative_full_scale_exactly_minus_one()
    {
        byte[] bytes = [0x00, 0x00, 0x00, 0x40, 0x00, 0x80, 0xFF, 0x7F, 0x00, 0xC0];
        Assert.Equal([0f, 0.5f, -1f, 32767f / 32768f, -0.5f], Convert(bytes, SampleEncoding.Pcm16));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2 PCM24: three little-endian bytes, signed, 1/8388608
    public void Pcm24_reads_three_little_endian_bytes_signed()
    {
        byte[] bytes = [0x00, 0x00, 0x40, 0x00, 0x00, 0x80, 0xFF, 0xFF, 0x7F, 0x00, 0x00, 0xC0, 0x01, 0x00, 0x00];
        Assert.Equal([0.5f, -1f, 8388607f / 8388608f, -0.5f, 1f / 8388608f], Convert(bytes, SampleEncoding.Pcm24));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2 PCM32: 1/2^31
    public void Pcm32_scales_by_two_to_the_31st()
    {
        byte[] bytes = [.. BitConverter.GetBytes(0x40000000), .. BitConverter.GetBytes(int.MinValue), .. BitConverter.GetBytes(-0x40000000)];
        Assert.Equal([0.5f, -1f, -0.5f], Convert(bytes, SampleEncoding.Pcm32));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T41")]
    [Trait("Decision", "ED-267")] // V-4.5 / WASAPI: a buffer flagged silent is zeros whatever its bytes hold
    public void A_silent_flagged_buffer_converts_to_zeros_whatever_its_bytes()
    {
        var bytes = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x7F };
        Assert.All(Convert(bytes, SampleEncoding.Float32, silent: true), v => Assert.Equal(0f, v));
        Assert.All(Convert(bytes, SampleEncoding.Pcm16, silent: true), v => Assert.Equal(0f, v));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // a trailing partial sample is not a sample
    public void Only_whole_samples_are_counted()
    {
        Assert.Equal(2, SampleConverter.SamplesIn(7, SampleEncoding.Pcm24));
        Assert.Equal(3, SampleConverter.SamplesIn(7, SampleEncoding.Pcm16));
        Assert.Equal(1, SampleConverter.SamplesIn(7, SampleEncoding.Float32));
        Assert.Equal(0, SampleConverter.SamplesIn(0, SampleEncoding.Pcm32));
    }
}
