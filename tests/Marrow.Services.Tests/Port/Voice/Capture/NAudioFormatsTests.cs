using Marrow.Services.Port.Voice.Capture;
using NAudio.Wave;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (voice-SPEC V-4.1, V-4.2): the mix format NAudio reports for an endpoint, mapped to the formats the capture accepts. WaveFormat objects are plain data: no device is involved.
public sealed class NAudioFormatsTests
{
    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // the usual Windows 11 shared-mode mix format: float32, 48 kHz, stereo (extensible)
    public void An_extensible_float_format_is_float32()
    {
        var wave = new WaveFormatExtensible(48000, 32, 2, true, 32, 3);
        Assert.True(NAudioFormats.TryMap(wave, out var format));
        Assert.Equal(new DeviceFormat(48000, 2, SampleEncoding.Float32), format);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // a plain IEEE float format
    public void A_plain_float_format_is_float32()
    {
        Assert.True(NAudioFormats.TryMap(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1), out var format));
        Assert.Equal(new DeviceFormat(44100, 1, SampleEncoding.Float32), format);
    }

    [Theory]
    [InlineData(16, SampleEncoding.Pcm16)]
    [InlineData(24, SampleEncoding.Pcm24)]
    [InlineData(32, SampleEncoding.Pcm32)]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // V-4.2 PCM 16/24/32, plain
    public void Plain_pcm_formats_map_by_their_container_size(int bits, SampleEncoding expected)
    {
        Assert.True(NAudioFormats.TryMap(new WaveFormat(44100, bits, 2), out var format));
        Assert.Equal(new DeviceFormat(44100, 2, expected), format);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // 24 valid bits in a 32-bit container (the common USB interface) are PCM32, left-justified
    public void An_extensible_pcm_format_maps_by_its_container_not_its_valid_bits()
    {
        var wave = new WaveFormatExtensible(48000, 32, 6, false, 24, 0);
        Assert.True(NAudioFormats.TryMap(wave, out var format));
        Assert.Equal(new DeviceFormat(48000, 6, SampleEncoding.Pcm32), format);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")] // anything V-4.2 does not list is refused, not guessed
    public void Formats_outside_V_4_2_are_refused()
    {
        Assert.False(NAudioFormats.TryMap(new WaveFormat(8000, 8, 1), out _));
        Assert.False(NAudioFormats.TryMap(WaveFormat.CreateALawFormat(8000, 1), out _));
        Assert.False(NAudioFormats.TryMap(new WaveFormat(48000, 16, 10), out _));
        Assert.False(NAudioFormats.TryMap(new WaveFormatExtensible(48000, 16, 2, true, 16, 3), out _)); // float must be 32 bits
    }
}
