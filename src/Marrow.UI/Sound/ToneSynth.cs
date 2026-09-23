using System.Numerics;
using Marrow.Core.Port;

namespace Marrow.UI.Sound;

// Source: DroppyCode/UI/Sound/ToneSynth.swift (B2 1fbf81e), :1-70.

/// <summary>
/// Renders a short struck tone to 16-bit mono WAV data, so the app's chimes ship as a few
/// numbers rather than sound files and stay tunable right where they are declared.
/// </summary>
// :3-5 a caseless `enum` is a static class.
public static class ToneSynth
{
    /// <summary>A note of a chime: when it starts and how strongly it rings, relative to the others.</summary>
    // :6-11 a Swift struct is an immutable sealed record (R-VAL-1).
    public sealed record Note(double Frequency, double Onset, double Level);

    /// <summary>
    /// One sine in a note's tone: its frequency as a multiple of the fundamental, how loud it
    /// starts, and how quickly it fades (the seconds it takes to fall by about two thirds).
    /// </summary>
    // :13-19
    public sealed record Partial(double Multiple, double Level, double Decay);

    // :21
    private const double SampleRate = 44_100.0;

    /// <summary>A raised-cosine onset: quick enough to feel struck, with no click.</summary>
    // :22-23
    private const double Attack = 0.006;

    /// <summary>The tail is inaudible by the end; the fade makes sure it stops without a tick.</summary>
    // :24-25
    private const double Release = 0.08;

    // :27-69
    public static byte[] Wave(IReadOnlyList<Note> notes, IReadOnlyList<Partial> partials, double duration)
    {
        // :28-29 `Int(...)` truncates toward zero and traps on NaN, infinity or out of range (R-NUM-4).
        var count = SwiftNumber.TruncatingToInt(SampleRate * duration);
        var samples = new double[count];
        // :30-40
        foreach (var note in notes)
        {
            var start = SwiftNumber.TruncatingToInt(note.Onset * SampleRate);
            // :32 `start..<count` traps when start > count; an empty range (start == count) runs zero times.
            if (start > count) throw SwiftTraps.Fatal("Range requires lowerBound <= upperBound");
            for (var index = start; index < count; index++)
            {
                // :33-34
                var time = (double)(index - start) / SampleRate;
                var onset = time < Attack ? 0.5 - 0.5 * Math.Cos(Math.PI * time / Attack) : 1;
                // :35-37 `reduce(0.0)` over the shared partials, in declaration order.
                var tone = 0.0;
                foreach (var partial in partials)
                {
                    tone = tone + partial.Level * Math.Exp(-time / partial.Decay)
                        * Math.Sin(2 * Math.PI * note.Frequency * partial.Multiple * time);
                }

                // :38
                samples[index] += note.Level * onset * tone;
            }
        }

        // :41-44 `(count - fade)..<count`: with count < fade the lower bound is negative and `samples[index]`
        // traps (here IndexOutOfRangeException, a trap under R-ERR-1).
        var fade = SwiftNumber.TruncatingToInt(Release * SampleRate);
        for (var index = count - fade; index < count; index++)
        {
            samples[index] *= (double)(count - index) / (double)fade;
        }

        // :45 `reduce(0) { max($0, abs($1)) }`; Swift's max(x, y) is `y >= x ? y : x` (R-NUM-4).
        var peak = 0.0;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            peak = magnitude >= peak ? magnitude : peak;
        }

        // :46
        var scale = peak > 0 ? 0.9 / peak : 0;

        // :48-51
        var data = new List<byte>(checked((int)(44 + count * 2)));
        void Append<TValue>(TValue value) where TValue : IBinaryInteger<TValue>
        {
            Span<byte> bytes = stackalloc byte[value.GetByteCount()];
            value.WriteLittleEndian(bytes);
            data.AddRange(bytes);
        }

        data.AddRange("RIFF"u8); // :52
        Append((uint)(36 + count * 2)); // :53 a checked conversion, as `UInt32(_:)` traps out of range
        data.AddRange("WAVE"u8); // :54
        data.AddRange("fmt "u8); // :55
        Append((uint)16); // :56
        Append((ushort)1); // :57 PCM
        Append((ushort)1); // :58 mono
        Append((uint)SampleRate); // :59
        Append((uint)SampleRate * 2); // :60 bytes per second
        Append((ushort)2); // :61 bytes per frame
        Append((ushort)16); // :62 bits per sample
        data.AddRange("data"u8); // :63
        Append((uint)(count * 2)); // :64
        // :65-67 `.rounded()` is half away from zero (R-NUM-3); `Int16(_:)` is a checked cast that traps out of range.
        foreach (var sample in samples)
        {
            Append((short)SwiftNumber.Rounded(sample * scale * (double)short.MaxValue));
        }

        // :68
        return data.ToArray();
    }
}
