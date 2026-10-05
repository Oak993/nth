using System.Buffers.Binary;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.1 "the session audio as 16 kHz mono PCM16 WAV" (V-T110).
[Trait("Spec", "V-T110")]
public sealed class PcmWavTests
{
    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T110_the_header_says_PCM_mono_16000_Hz_16_bits_and_the_sizes_add_up()
    {
        var wav = PcmWav.Encode(new float[1600]);
        Assert.Equal(44 + 3200, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + 3200, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)));       // PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));       // mono
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(32)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(3200, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T110_samples_are_little_endian_integers_and_loud_ones_clip()
    {
        var wav = PcmWav.Encode(new[] { 0f, 0.5f, -0.5f, 1f, -1f, 2f, -2f });
        short At(int i) => BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + 2 * i));
        Assert.Equal([0, 16384, -16384, 32767, -32767, 32767, -32767], Enumerable.Range(0, 7).Select(At));
        Assert.Empty(PcmWav.Encode([]).Skip(44));
    }
}
