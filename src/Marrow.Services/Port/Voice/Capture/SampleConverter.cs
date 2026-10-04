// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): device bytes to float (voice-SPEC V-4.2).
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>Converts a device buffer to float samples: float32 as it is, PCM16, PCM24 and PCM32 scaled to [-1, 1). No allocation.</summary>
public static class SampleConverter
{
    private const float Pcm16Scale = 1f / 32768f;
    private const float Pcm24Scale = 1f / 8388608f;
    private const double Pcm32Scale = 1.0 / 2147483648.0;

    private static int BytesPerSample(SampleEncoding encoding) => encoding switch
    {
        SampleEncoding.Pcm16 => 2,
        SampleEncoding.Pcm24 => 3,
        _ => 4,
    };

    /// <summary>How many samples <paramref name="byteCount"/> bytes hold (whole samples only).</summary>
    public static int SamplesIn(int byteCount, SampleEncoding encoding) => byteCount / BytesPerSample(encoding);

    /// <summary>Converts the whole samples of <paramref name="bytes"/> into <paramref name="destination"/>, which must hold <see cref="SamplesIn"/> of them; zeros when <paramref name="silent"/>. Returns the sample count.</summary>
    public static int ToFloat(ReadOnlySpan<byte> bytes, SampleEncoding encoding, bool silent, Span<float> destination)
    {
        var count = SamplesIn(bytes.Length, encoding);
        var output = destination[..count];
        if (silent)
        {
            output.Clear();
            return count;
        }
        switch (encoding)
        {
            case SampleEncoding.Float32:
                MemoryMarshal.Cast<byte, float>(bytes[..(count * 4)]).CopyTo(output);
                break;
            case SampleEncoding.Pcm16:
                for (var i = 0; i < count; i++) output[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(i * 2)..]) * Pcm16Scale;
                break;
            case SampleEncoding.Pcm24:
                for (var i = 0; i < count; i++)
                {
                    var at = i * 3;
                    var value = (bytes[at + 2] << 16 | bytes[at + 1] << 8 | bytes[at]) << 8 >> 8; // sign-extends bit 23
                    output[i] = value * Pcm24Scale;
                }
                break;
            default:
                for (var i = 0; i < count; i++) output[i] = (float)(BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * 4)..]) * Pcm32Scale);
                break;
        }
        return count;
    }
}
