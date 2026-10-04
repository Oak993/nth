using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-2 (voice-SPEC V-5.2, V-T17): the rational polyphase resampler to 16 kHz. The expected numbers are the clause's: a 1 kHz tone within
// 0.1 dB at 48k, 44.1k and 16k inputs; the passband to 7.2 kHz within 0.1 dB; 8 kHz and above at least 60 dB down (9 kHz named);
// linear phase; streaming output identical to one-shot output.
public sealed class PolyphaseResamplerTests
{
    private const double InputAmplitude = 0.5;
    private static readonly double InputRms = InputAmplitude / Math.Sqrt(2);

    private static List<float> Resample(int rate, float[] input)
    {
        var output = new List<float>();
        new PolyphaseResampler().Process(rate, input, output);
        return output;
    }

    private static double LevelDb(int rate, double frequency) =>
        DspSignals.RelativeDb(Resample(rate, DspSignals.Sine(frequency, InputAmplitude, rate, 1.0)), 16000, InputRms);

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(16000)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: a 1 kHz tone keeps its level within 0.1 dB
    public void A_1_kHz_tone_keeps_its_level_within_a_tenth_of_a_dB(int rate)
    {
        Assert.InRange(LevelDb(rate, 1000), -0.1, 0.1);
    }

    [Theory]
    [InlineData(48000, 100)]
    [InlineData(48000, 4000)]
    [InlineData(48000, 7200)]
    [InlineData(44100, 100)]
    [InlineData(44100, 4000)]
    [InlineData(44100, 7200)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: passband to 7.2 kHz within 0.1 dB
    public void The_passband_to_7_2_kHz_is_flat_within_a_tenth_of_a_dB(int rate, double frequency)
    {
        Assert.InRange(LevelDb(rate, frequency), -0.1, 0.1);
    }

    [Theory]
    [InlineData(48000, 9000)]
    [InlineData(44100, 9000)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-T17: 9 kHz attenuated >= 60 dB
    public void A_9_kHz_tone_is_at_least_60_dB_down(int rate, double frequency)
    {
        var db = LevelDb(rate, frequency);
        Assert.True(db <= -60, $"{frequency} Hz at {rate}: {db:F2} dB");
    }

    [Theory]
    [InlineData(48000, 8000)]
    [InlineData(48000, 8200)]
    [InlineData(48000, 8500)]
    [InlineData(48000, 10000)]
    [InlineData(48000, 12000)]
    [InlineData(48000, 16000)]
    [InlineData(48000, 20000)]
    [InlineData(48000, 23500)]
    [InlineData(44100, 8000)]
    [InlineData(44100, 8200)]
    [InlineData(44100, 8500)]
    [InlineData(44100, 12000)]
    [InlineData(44100, 16000)]
    [InlineData(44100, 21000)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: stopband at 8 kHz and above at least 60 dB down
    public void The_stopband_from_8_kHz_is_at_least_60_dB_down(int rate, double frequency)
    {
        var db = LevelDb(rate, frequency);
        Assert.True(db <= -60, $"{frequency} Hz at {rate}: {db:F2} dB");
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: streaming output identical to one-shot output
    public void Streaming_in_odd_blocks_equals_one_shot_exactly(int rate)
    {
        var input = DspSignals.Noise(0.2, rate / 2, seed: 5);
        var oneShot = Resample(rate, input);

        var streamed = new List<float>();
        var resampler = new PolyphaseResampler();
        var rng = new Random(11);
        var sizes = new[] { 1, 7, 0, 480, 441, 1000, 3, 2048, 160 };
        for (int at = 0, i = 0; at < input.Length; i++)
        {
            var n = Math.Min(sizes[i % sizes.Length] + rng.Next(0, 3), input.Length - at);
            resampler.Process(rate, input.AsSpan(at, n), streamed);
            at += n;
        }

        Assert.Equal(oneShot.Count, streamed.Count);
        Assert.True(oneShot.SequenceEqual(streamed), "streamed output differs from one-shot output");
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: linear phase (a symmetric impulse response, no time shift)
    public void The_response_to_an_impulse_is_symmetric_about_the_input_instant(int rate)
    {
        // An impulse at 0.1 s must land at output sample 1600 (16000 * 0.1): no group delay is left in the output.
        var input = new float[rate / 5];
        input[rate / 10] = 1f;
        var output = Resample(rate, input);

        var peak = Enumerable.Range(0, output.Count).MaxBy(i => Math.Abs(output[i]));
        Assert.Equal(1600, peak);
        for (var j = 1; j <= 60; j++)
            Assert.Equal(output[peak - j], output[peak + j], 1e-6);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: 48 kHz is the ratio 1/3, 44.1 kHz is 160/441, others their reduced ratio
    public void The_output_holds_one_second_of_16_kHz_for_one_second_of_input()
    {
        foreach (var rate in new[] { 48000, 44100, 32000, 22050, 96000, 8000 })
            Assert.InRange(Resample(rate, new float[rate]).Count, 16000 - 100, 16000);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-267")] // V-5.2: other rates by their reduced ratio
    public void Other_rates_keep_the_level_and_reject_above_8_kHz()
    {
        Assert.InRange(LevelDb(32000, 1000), -0.1, 0.1);
        Assert.InRange(LevelDb(22050, 1000), -0.1, 0.1);
        Assert.InRange(LevelDb(96000, 1000), -0.1, 0.1);
        Assert.True(LevelDb(96000, 9000) <= -60);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-292")] // the seam (3): a 16 kHz mix format needs no conversion
    public void A_16_kHz_input_is_passed_through_unchanged()
    {
        var input = DspSignals.Noise(0.2, 5000, seed: 3);
        Assert.True(Resample(16000, input).SequenceEqual(input));
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-292")] // the seam (3): Reset starts a new stream
    public void Reset_and_a_change_of_rate_both_start_a_fresh_stream()
    {
        var a = DspSignals.Noise(0.2, 9000, seed: 8);
        var b = DspSignals.Noise(0.2, 6000, seed: 9);
        var fresh = Resample(48000, b);

        var r = new PolyphaseResampler();
        r.Process(48000, a, []);
        r.Reset();
        var afterReset = new List<float>();
        r.Process(48000, b, afterReset);
        Assert.True(fresh.SequenceEqual(afterReset));

        var q = new PolyphaseResampler();
        q.Process(44100, a, []);
        var afterRateChange = new List<float>();
        q.Process(48000, b, afterRateChange);
        Assert.True(fresh.SequenceEqual(afterRateChange));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-48000)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-292")] // the seam (3): an exception in the audio path ends the session with the capture note
    public void A_rate_that_is_not_positive_is_refused(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PolyphaseResampler().Process(rate, new float[10], []));
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T17")]
    [Trait("Decision", "ED-292")] // the seam (3): PolyphaseResampler implements IStreamingResampler
    public void It_is_the_IStreamingResampler_of_the_session()
    {
        IStreamingResampler seam = new PolyphaseResampler();
        var output = new List<float>();
        seam.Process(48000, DspSignals.Sine(1000, 0.5, 48000, 0.2), output);
        Assert.NotEmpty(output);
    }
}
