// PORT-ONLY: OD-0121, locked sound recipe :3053. Cached PCM sine voices, through the port's existing sound-output seam.
using System.Buffers.Binary;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal static class HandshakeSound
{
    private static readonly Lazy<SwiftSound?> linking = new(() => SwiftSound.Create(Wave([new(523.25, 1.1, .03, 0, .04, .0001, .05), new(783.99, 1.3, .022, .08, .04, .0001, .05)])));
    private static readonly Lazy<SwiftSound?> tick = new(() => SwiftSound.Create(Wave([new(1318.5, .18, .025, 0, .04, .0001, .05)])));
    internal static void Play(IReadOnlyList<HandshakeTone> tones)
    {
        for (var i = 0; i < tones.Count; i++)
        {
            if (tones[i].Hertz == 523.25) linking.Value?.Play();
            else if (tones[i].Hertz == 1318.5) tick.Value?.Play();
        }
    }
    internal static byte[] Wave(IReadOnlyList<HandshakeTone> tones)
    {
        const int rate = 44100; var seconds = tones.Max(t => t.DelaySeconds + t.Seconds + t.StopTailSeconds); var count = (int)Math.Ceiling(seconds * rate); var bytes = new byte[44 + count * 2];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8); "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), rate); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), rate * 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), count * 2);
        for (var sample = 0; sample < count; sample++)
        {
            var time = sample / (double)rate; double value = 0;
            for (var i = 0; i < tones.Count; i++)
            {
                var tone = tones[i]; var t = time - tone.DelaySeconds; if (t < 0 || t >= tone.Seconds + tone.StopTailSeconds) continue;
                var gain = t <= tone.AttackSeconds ? tone.Gain * t / tone.AttackSeconds : t >= tone.Seconds ? tone.EndGain : tone.Gain * Math.Pow(tone.EndGain / tone.Gain, (t - tone.AttackSeconds) / (tone.Seconds - tone.AttackSeconds));
                value += gain * Math.Sin(Math.Tau * tone.Hertz * t);
            }
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + sample * 2), (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue));
        }
        return bytes;
    }
}
