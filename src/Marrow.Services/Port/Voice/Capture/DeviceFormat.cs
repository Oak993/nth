// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the endpoint's own mix format (voice-SPEC V-4.1, V-4.2).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>The format a capture device delivers in: its own mix format, never a converted one (V-4.1). 1 to 8 channels (V-4.2).</summary>
public readonly record struct DeviceFormat(int SampleRate, int Channels, SampleEncoding Encoding)
{
    /// <summary>V-4.2: "1-8 channels".</summary>
    public const int MaxChannels = 8;

    public int BytesPerSample => Encoding switch
    {
        SampleEncoding.Pcm16 => 2,
        SampleEncoding.Pcm24 => 3,
        _ => 4,
    };

    public int BytesPerFrame => BytesPerSample * Channels;

    /// <summary>The format the capture seam reports to a session: sample rate and channels.</summary>
    public CaptureFormat ToCaptureFormat() => new(SampleRate, Channels);

    /// <summary>
    /// The format of a device that reports <paramref name="bitsPerSample"/> per sample container and whether they are IEEE float; false for a format
    /// V-4.2 does not accept (a rate of 0, 0 or more than 8 channels, a float that is not 32 bits, an integer that is not 16, 24 or 32 bits).
    /// </summary>
    public static bool TryCreate(int sampleRate, int channels, int bitsPerSample, bool isFloat, out DeviceFormat format)
    {
        format = default;
        if (sampleRate <= 0 || channels < 1 || channels > MaxChannels) return false;
        SampleEncoding encoding;
        if (isFloat)
        {
            if (bitsPerSample != 32) return false;
            encoding = SampleEncoding.Float32;
        }
        else
        {
            switch (bitsPerSample)
            {
                case 16: encoding = SampleEncoding.Pcm16; break;
                case 24: encoding = SampleEncoding.Pcm24; break;
                case 32: encoding = SampleEncoding.Pcm32; break;
                default: return false;
            }
        }
        format = new DeviceFormat(sampleRate, channels, encoding);
        return true;
    }
}
