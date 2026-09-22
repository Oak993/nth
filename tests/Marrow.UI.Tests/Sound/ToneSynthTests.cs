using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;
using Marrow.UI.Sound;

namespace Marrow.UI.Tests.Sound;

// Source: DroppyCode/UI/Sound/ToneSynth.swift (B2 1fbf81e), :1-70. Records PR-THEME-078, PR-THEME-079, PR-THEME-080.
// Chime fixtures are the note and partial lists of DroppyCode/UI/Sound/FinishChime.swift:13,25-36 and
// DroppyCode/UI/Sound/SettleChime.swift:12,24-35 (R-TEST-8).
//
// How the expected values were obtained (R-TEST-4), independently of any C# implementation:
// - Golden SHA-256 digests, lengths, headers and spot samples were computed by a transcription of ToneSynth.swift:27-68
//   in Node.js 24 (V8's libm), and the same digests were reproduced by a separate .NET 10 transcription; both agree
//   byte for byte. No Swift-recorded buffer exists in porting/ (searched for ToneSynth, FinishChime and .wav).
// - Reference() below is the verification_method of PR-THEME-079 ("sample-by-sample comparison against the formula"),
//   transcribed from ToneSynth.swift:27-46,66-67 with the Swift evaluation order kept.
// - The 11025 Hz fixtures put every odd sample on a sine crest, where sin() is exactly +1 or -1 in any faithfully
//   rounded libm, and use a 1e300 decay so exp(-t / decay) is exactly 1; their values are therefore exact arithmetic.
public sealed class ToneSynthTests
{
    private const string FinishGoldenSha256 = "51761373e4570017b1cdc2ea4702b69ede1a94ef91daf15808252fdc7c119ebd";
    private const string SettleGoldenSha256 = "403fad7b4b016bc5ead15b57a1e4ef350f1afbfda7ab403933747195688e5b5f";

    // FinishChime.swift:25-28
    private static readonly ToneSynth.Note[] FinishNotes =
    [
        new ToneSynth.Note(587.33, 0, 0.8),
        new ToneSynth.Note(880, 0.14, 1),
    ];

    // FinishChime.swift:32-36
    private static readonly ToneSynth.Partial[] FinishPartials =
    [
        new ToneSynth.Partial(1, 1, 0.14),
        new ToneSynth.Partial(2, 0.22, 0.06),
        new ToneSynth.Partial(4, 0.08, 0.04),
    ];

    // SettleChime.swift:24-27
    private static readonly ToneSynth.Note[] SettleNotes =
    [
        new ToneSynth.Note(659.26, 0, 0.7),
        new ToneSynth.Note(523.25, 0.075, 1),
    ];

    // SettleChime.swift:31-35
    private static readonly ToneSynth.Partial[] SettlePartials =
    [
        new ToneSynth.Partial(1, 1, 0.075),
        new ToneSynth.Partial(3, 0.16, 0.02),
        new ToneSynth.Partial(2, 0.08, 0.03),
    ];

    // A single steady sine: 1e300 makes exp(-t / decay) exactly 1 for every t in the buffer.
    private static readonly ToneSynth.Partial[] SteadySine = { new ToneSynth.Partial(1, 1, 1e300) };

    private static byte[] Finish() => ToneSynth.Wave(FinishNotes, FinishPartials, 0.7); // FinishChime.swift:13

    private static byte[] Settle() => ToneSynth.Wave(SettleNotes, SettlePartials, 0.32); // SettleChime.swift:12

    // ---------------------------------------------------------------- PR-THEME-078: the WAV container

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:48-68, FinishChime.swift:13,25-36
    public void The_finish_chime_renders_the_golden_61782_byte_wav()
    {
        var data = Finish();

        Assert.Equal(61782, data.Length); // 44 + 30869 * 2
        Assert.Equal(FinishGoldenSha256, Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:48-68, SettleChime.swift:12,24-35
    public void The_settle_chime_renders_the_golden_28268_byte_wav()
    {
        var data = Settle();

        Assert.Equal(28268, data.Length); // 44 + 14112 * 2
        Assert.Equal(SettleGoldenSha256, Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:53-65
    public void The_header_is_the_canonical_44_byte_riff_wave_pcm_mono_44100_hz_16_bit_header()
    {
        // "RIFF", UInt32 36 + 30869 * 2 = 61774, "WAVE", "fmt ", UInt32 16, UInt16 1 (PCM), UInt16 1 (mono),
        // UInt32 44100, UInt32 88200, UInt16 2, UInt16 16, "data", UInt32 61738; all little-endian.
        var finish = Convert.FromHexString(
            "524946464ef1000057415645666d7420100000000100010044ac00008858010002001000646174612af10000");
        // Same with 14112 samples: 36 + 28224 = 28260, data size 28224.
        var settle = Convert.FromHexString(
            "52494646646e000057415645666d7420100000000100010044ac0000885801000200100064617461406e0000");

        Assert.Equal(finish, Finish()[..44]);
        Assert.Equal(settle, Settle()[..44]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:53-65
    public void Each_header_field_is_written_little_endian_at_its_canonical_offset()
    {
        var data = ToneSynth.Wave(new[] { new ToneSynth.Note(11025, 0, 1) }, SteadySine, 0.1); // 4410 samples

        Assert.Equal("RIFF"u8.ToArray(), data[0..4]);
        Assert.Equal(36u + 4410u * 2u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        Assert.Equal("WAVE"u8.ToArray(), data[8..12]);
        Assert.Equal("fmt "u8.ToArray(), data[12..16]); // U+0020 SPACE is the fourth byte
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20))); // PCM
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(22))); // mono
        Assert.Equal(44100u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24)));
        Assert.Equal(88200u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28))); // bytes per second
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32))); // bytes per frame
        Assert.Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(34))); // bits per sample
        Assert.Equal("data"u8.ToArray(), data[36..40]);
        Assert.Equal(4410u * 2u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(40)));
        Assert.Equal(44 + 4410 * 2, data.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(data, 0, 4));
    }

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:66-68
    public void Samples_are_signed_16_bit_little_endian_after_the_header()
    {
        var finish = Finish();
        var settle = Settle();

        // The finish chime's loudest sample is negative: -29490 at index 6514 is 0x8CCE, written CE 8C.
        Assert.Equal(new byte[] { 0xCE, 0x8C }, finish[(44 + 6514 * 2)..(44 + 6514 * 2 + 2)]);
        Assert.Equal(-29490, SampleAt(finish, 6514));
        // Settle: -29490 at index 3797.
        Assert.Equal(-29490, SampleAt(settle, 3797));
        // The first twelve samples of each chime: the raised-cosine attack from silence.
        Assert.Equal(new short[] { 0, 0, 1, 3, 6, 11, 18, 27, 37, 50, 63, 79 }, Samples(finish)[..12]);
        Assert.Equal(new short[] { 0, 0, 1, 3, 6, 12, 20, 29, 40, 53, 66, 80 }, Samples(settle)[..12]);
        Assert.Equal((short)13470, SampleAt(finish, 1000));
        Assert.Equal((short)-5992, SampleAt(settle, 1000));
    }

    [Fact]
    [Trait("Req", "PR-THEME-078")] // DroppyCode/UI/Sound/ToneSynth.swift:26-69
    public void Rendering_is_a_pure_function_of_its_arguments()
    {
        var first = Finish();
        var second = Finish();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    // ---------------------------------------------------------------- PR-THEME-079: the synthesis

    [Fact]
    [Trait("Req", "PR-THEME-079")] // DroppyCode/UI/Sound/ToneSynth.swift:27-40
    public void Both_chimes_match_the_formula_sample_by_sample()
    {
        Assert.Equal(Reference(FinishNotes, FinishPartials, 0.7), Samples(Finish()));
        Assert.Equal(Reference(SettleNotes, SettlePartials, 0.32), Samples(Settle()));
    }

    [Fact]
    [Trait("Req", "PR-THEME-079")] // DroppyCode/UI/Sound/ToneSynth.swift:27-40
    public void Overlapping_notes_add_and_every_note_uses_the_shared_partials_at_its_own_frequency()
    {
        ToneSynth.Note[] notes =
        [
            new ToneSynth.Note(440, 0, 0.5),
            new ToneSynth.Note(660, 0.01, 1),
            new ToneSynth.Note(330, 0.02, 0.25),
        ];
        ToneSynth.Partial[] partials =
        [
            new ToneSynth.Partial(1, 1, 0.1),
            new ToneSynth.Partial(1.5, 0.3, 0.05),
        ];

        Assert.Equal(Reference(notes, partials, 0.12), Samples(ToneSynth.Wave(notes, partials, 0.12)));
    }

    [Fact]
    [Trait("Req", "PR-THEME-079")] // DroppyCode/UI/Sound/ToneSynth.swift:28
    public void The_sample_count_truncates_rate_times_duration_toward_zero()
    {
        // 44100 * 0.7 is 30869.999999999996 in binary floating point; Int() truncates it to 30869.
        Assert.Equal(30869.999999999996, 44_100.0 * 0.7);
        Assert.Equal(44 + 30869 * 2, Finish().Length);
        // Negative control: rounding instead of truncating would give one more sample.
        Assert.Equal(30870.0, Math.Round(44_100.0 * 0.7, MidpointRounding.AwayFromZero));
        // 44100 * 0.32 is exactly 14112.
        Assert.Equal(44 + 14112 * 2, Settle().Length);
    }

    [Fact]
    [Trait("Req", "PR-THEME-079")] // DroppyCode/UI/Sound/ToneSynth.swift:31-35
    public void A_note_starts_at_the_truncated_onset_sample_and_is_silent_before_it()
    {
        // 0.075 * 44100 = 3307.5, truncated to 3307: the note's time is 0 at index 3307 (SettleChime.swift:26).
        var samples = Samples(ToneSynth.Wave(
            new[] { new ToneSynth.Note(523.25, 0.075, 1) }, new[] { new ToneSynth.Partial(1, 1, 0.075) }, 0.32));

        Assert.All(samples[..3309], sample => Assert.Equal(0, sample));
        Assert.Equal(new short[] { 1, 2 }, samples[3309..3311]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-079")] // DroppyCode/UI/Sound/ToneSynth.swift:22-23,34
    public void The_raised_cosine_attack_lasts_6_ms_and_the_note_is_at_full_level_from_sample_265()
    {
        // 11025 Hz: odd samples sit on crests (+1, -1, +1, ...), even samples on zero crossings. One note, peak 1,
        // so scale = 0.9 and a full crest is 0.9 * 32767 = 29490.3 -> 29490.
        var samples = Samples(ToneSynth.Wave(new[] { new ToneSynth.Note(11025, 0, 1) }, SteadySine, 0.1));

        Assert.Equal(0, samples[0]); // time 0: 0.5 - 0.5 * cos(0) = 0
        Assert.Equal(1, samples[1]);
        Assert.Equal(0, samples[2]);
        Assert.Equal(-9, samples[3]);
        Assert.Equal(26, samples[5]);
        Assert.Equal(29477, samples[261]);
        Assert.Equal(-29488, samples[263]); // 263 / 44100 = 0.0059637 s, still inside the 0.006 s attack
        Assert.Equal(0, samples[264]);
        Assert.Equal(29490, samples[265]); // 265 / 44100 = 0.0060091 s, onset = 1
        Assert.Equal(-29490, samples[267]);
        Assert.Equal(-29490, samples[879]);
        Assert.Equal(29490, samples[881]);
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")] // DroppyCode/UI/Sound/ToneSynth.swift:32 `start..<count` traps when start > count
    public void A_note_whose_onset_lies_past_the_end_traps()
    {
        var trap = Record.Exception(() => ToneSynth.Wave(new[] { new ToneSynth.Note(440, 0.5, 1) }, SteadySine, 0.1));

        Assert.NotNull(trap);
        Assert.False(SwiftTry.IsThrownError(trap));
    }

    // ---------------------------------------------------------------- PR-THEME-080: fade, normalisation, rounding

    [Fact]
    [Trait("Req", "PR-THEME-080")] // DroppyCode/UI/Sound/ToneSynth.swift:45-46,67
    public void Both_chimes_peak_at_exactly_29490()
    {
        Assert.Equal(29490, Samples(Finish()).Max(sample => Math.Abs((int)sample)));
        Assert.Equal(29490, Samples(Settle()).Max(sample => Math.Abs((int)sample)));
        // 0.9 of Int16.max, not of 32768 (which would give 29491).
        Assert.Equal(29490.3, 0.9 * short.MaxValue, 9);
    }

    [Fact]
    [Trait("Req", "PR-THEME-080")] // DroppyCode/UI/Sound/ToneSynth.swift:41-44
    public void Both_chimes_fade_to_a_near_zero_last_sample()
    {
        Assert.Equal(new short[] { -1, -1, 0, 0, 0, 0 }, Samples(Finish())[^6..]);
        Assert.Equal(new short[] { 1, 1, 1, 1, 0, 0 }, Samples(Settle())[^6..]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-080")] // DroppyCode/UI/Sound/ToneSynth.swift:24-25,41-44
    public void The_last_3528_samples_ramp_linearly_from_one_to_one_3528th()
    {
        // 4410 samples; the fade covers 882..4409 and multiplies sample i by (4410 - i) / 3528.
        var samples = Samples(ToneSynth.Wave(new[] { new ToneSynth.Note(11025, 0, 1) }, SteadySine, 0.1));

        Assert.Equal(3528, (int)(0.08 * 44_100.0));
        Assert.Equal(29490, samples[881]); // before the fade
        Assert.Equal(0, samples[882]);
        Assert.Equal(-29482, samples[883]); // 3527 / 3528 of a crest
        Assert.Equal(29465, samples[885]); // 3525 / 3528
        Assert.Equal(42, samples[4405]); // 5 / 3528
        Assert.Equal(-25, samples[4407]); // 3 / 3528
        Assert.Equal(8, samples[4409]); // 1 / 3528: 0.9 * 32767 / 3528 = 8.36
    }

    [Fact]
    [Trait("Req", "PR-THEME-080")] // DroppyCode/UI/Sound/ToneSynth.swift:45-46
    public void A_silent_buffer_scales_to_zero_instead_of_dividing_by_zero()
    {
        var noNotes = ToneSynth.Wave(Array.Empty<ToneSynth.Note>(), FinishPartials, 0.1);
        var noPartials = ToneSynth.Wave(FinishNotes, Array.Empty<ToneSynth.Partial>(), 0.7);

        Assert.Equal(44 + 4410 * 2, noNotes.Length);
        Assert.All(noNotes[44..], value => Assert.Equal(0, value));
        Assert.Equal(44 + 30869 * 2, noPartials.Length);
        Assert.All(noPartials[44..], value => Assert.Equal(0, value));
    }

    [Fact]
    [Trait("Req", "PR-THEME-080")] // DroppyCode/UI/Sound/ToneSynth.swift:67 `.rounded()` is half away from zero
    public void Samples_round_half_away_from_zero()
    {
        // Two 11025 Hz notes in opposite phase (onset 302.5 / 44100 truncates to sample 302, 2 mod 4).
        // The first note alone sets the peak to 0.45, so scale = 0.9 / 0.45 = 2 exactly. After both attacks each
        // crest is 0.45 - 0.44608600115970337 (exact by Sterbenz), and crest * 2 * 32767 is exactly 256.5.
        var samples = Samples(ToneSynth.Wave(
            new[]
            {
                new ToneSynth.Note(11025, 0, 0.45),
                new ToneSynth.Note(11025, 302.5 / 44_100, 0.44608600115970337),
            },
            SteadySine,
            0.1));

        Assert.Equal(256.5, (0.45 - 0.44608600115970337) * (0.9 / 0.45) * 32767);
        Assert.Equal(29490, samples[265]);
        Assert.Equal(new short[] { 0, -257, 0, 257, 0, -257, 0, 257, 0 }, samples[566..575]);
        Assert.Equal(new short[] { -257, 0, 257, 0, -257, 0, 257 }, samples[699..706]);
        Assert.Equal(257, samples[881]); // the last crest before the fade
        // Negative control: .NET's default Math.Round is half to even and would write 256 and -256.
        Assert.Equal(256.0, Math.Round(256.5));
        Assert.Equal(-256.0, Math.Round(-256.5));
    }

    [Fact]
    [Trait("Rule", "R-ERR-1")] // DroppyCode/UI/Sound/ToneSynth.swift:42-43 `samples[index]` with count - fade < 0 traps
    public void A_duration_shorter_than_the_80_ms_fade_traps()
    {
        var trap = Record.Exception(() => ToneSynth.Wave(new[] { new ToneSynth.Note(440, 0, 1) }, SteadySine, 0.05));

        Assert.NotNull(trap);
        Assert.False(SwiftTry.IsThrownError(trap));
    }

    // ---------------------------------------------------------------- helpers

    private static short SampleAt(byte[] data, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(44 + index * 2));

    private static short[] Samples(byte[] data)
    {
        var samples = new short[(data.Length - 44) / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = SampleAt(data, index);
        }

        return samples;
    }

    // ToneSynth.swift:27-46,66-67 transcribed with Swift's evaluation order: the verification formula of
    // PR-THEME-079 and PR-THEME-080. Only called with inputs where Swift does not trap.
    private static short[] Reference(
        IReadOnlyList<ToneSynth.Note> notes, IReadOnlyList<ToneSynth.Partial> partials, double duration)
    {
        const double sampleRate = 44_100.0;
        const double attack = 0.006;
        const double release = 0.08;
        var count = (int)(sampleRate * duration);
        var samples = new double[count];
        foreach (var note in notes)
        {
            var start = (int)(note.Onset * sampleRate);
            for (var index = start; index < count; index++)
            {
                var time = (double)(index - start) / sampleRate;
                var onset = time < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * time / attack) : 1;
                var tone = 0.0;
                foreach (var partial in partials)
                {
                    tone = tone + partial.Level * Math.Exp(-time / partial.Decay)
                        * Math.Sin(2 * Math.PI * note.Frequency * partial.Multiple * time);
                }

                samples[index] += note.Level * onset * tone;
            }
        }

        var fade = (int)(release * sampleRate);
        for (var index = count - fade; index < count; index++)
        {
            samples[index] *= (double)(count - index) / (double)fade;
        }

        var peak = 0.0;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            peak = magnitude >= peak ? magnitude : peak; // Swift max(x, y) is `y >= x ? y : x`
        }

        var scale = peak > 0 ? 0.9 / peak : 0;
        var result = new short[count];
        for (var index = 0; index < count; index++)
        {
            var value = samples[index] * scale * short.MaxValue;
            result[index] = checked((short)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        return result;
    }
}
