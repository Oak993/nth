// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the mix format NAudio reports, as voice-SPEC V-4.2 accepts it.
using NAudio.Wave;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>Maps an NAudio <see cref="WaveFormat"/> to a <see cref="DeviceFormat"/>. Pure data, no device.</summary>
public static class NAudioFormats
{
    private static readonly Guid IeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>
    /// True for float32 and for PCM of a 16, 24 or 32 bit container (a 32-bit container with 24 valid bits is PCM32, left-justified: it scales by 2^31), at any rate, with
    /// 1 to 8 channels. False for anything else (V-4.2); the container size is used, not the valid bits.
    /// </summary>
    public static bool TryMap(WaveFormat wave, out DeviceFormat format)
    {
        bool isFloat;
        if (wave is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == IeeeFloat) isFloat = true;
            else if (extensible.SubFormat == Pcm) isFloat = false;
            else
            {
                format = default;
                return false;
            }
        }
        else if (wave.Encoding == WaveFormatEncoding.IeeeFloat) isFloat = true;
        else if (wave.Encoding == WaveFormatEncoding.Pcm) isFloat = false;
        else
        {
            format = default;
            return false;
        }
        return DeviceFormat.TryCreate(wave.SampleRate, wave.Channels, wave.BitsPerSample, isFloat, out format);
    }
}
