// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-18.1 "the session audio as 16 kHz mono PCM16 WAV".
using System.Buffers.Binary;

namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>A RIFF WAVE file around 16 kHz mono samples.</summary>
public static class PcmWav
{
    public const int SampleRate = 16000;
    public const int HeaderBytes = 44;

    /// <summary>The 44-byte canonical header (PCM, 1 channel, 16 000 Hz, 16 bits) and the samples as little-endian 16-bit integers.</summary>
    public static byte[] Encode(ReadOnlySpan<float> samples)
    {
        var data = samples.Length * 2;
        var wav = new byte[HeaderBytes + data];
        var w = wav.AsSpan();
        "RIFF"u8.CopyTo(w);
        BinaryPrimitives.WriteInt32LittleEndian(w[4..], 36 + data);
        "WAVE"u8.CopyTo(w[8..]);
        "fmt "u8.CopyTo(w[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(w[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(w[20..], 1);                  // PCM
        BinaryPrimitives.WriteInt16LittleEndian(w[22..], 1);                  // mono
        BinaryPrimitives.WriteInt32LittleEndian(w[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(w[28..], SampleRate * 2);     // bytes per second
        BinaryPrimitives.WriteInt16LittleEndian(w[32..], 2);                  // block align
        BinaryPrimitives.WriteInt16LittleEndian(w[34..], 16);                 // bits per sample
        "data"u8.CopyTo(w[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(w[40..], data);
        for (var i = 0; i < samples.Length; i++)
        {
            // ED-311: full scale is 32767, a sample outside -1..1 clips, a half rounds away from zero
            var v = Math.Clamp(samples[i], -1f, 1f) * 32767f;
            BinaryPrimitives.WriteInt16LittleEndian(w[(HeaderBytes + 2 * i)..], (short)MathF.Round(v, MidpointRounding.AwayFromZero));
        }
        return wav;
    }
}
