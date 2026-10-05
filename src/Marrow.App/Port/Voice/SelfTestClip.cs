// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-15, voice-SPEC V-3.5, V-10.3): the bundled 10 s clip.
using System.Buffers.Binary;
using System.Text;

namespace Marrow.App.Port.Voice;

/// <summary>Reads selftest-clip.wav (16 kHz, mono, 16-bit PCM; voice-pins.json "selfTestClip") as the engine takes audio: floats in -1 to 1.</summary>
public static class SelfTestClip
{
    /// <summary>The clip beside the app, under Port\Voice\Assets (Marrow.Services copies it there).</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets", "selftest-clip.wav");

    /// <summary>The samples of the file; <see cref="InvalidDataException"/> for anything but a 16 kHz mono 16-bit PCM WAV.</summary>
    public static ReadOnlyMemory<float> Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException("not a WAV file");
        var formatOk = false;
        for (var at = 12; at + 8 <= bytes.Length;)
        {
            var id = Encoding.ASCII.GetString(bytes, at, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)), (uint)(bytes.Length - at - 8));
            var body = at + 8;
            if (id == "fmt " && size >= 16)
            {
                formatOk = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body)) == 1
                    && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 2)) == 1
                    && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(body + 4)) == 16000
                    && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(body + 14)) == 16;
            }
            else if (id == "data")
            {
                if (!formatOk) throw new InvalidDataException("the clip is not 16 kHz mono 16-bit PCM");
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(body + i * 2)) / 32768f;
                return samples;
            }
            at = body + size + (size & 1);
        }
        throw new InvalidDataException("the WAV file has no sample data");
    }
}
