using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-2 (voice-SPEC V-5.3, V-5.4, V-T18, V-T19): the analyser equal to Web Audio's AnalyserNode. The expectations are what headless Chrome's
// real AnalyserNode read from the committed fixture signals (tests/fixtures/voice/analyser, made by generate.cjs): the 1024 spectrum bytes
// and the design's own all / low / mid / high computed from them.
public sealed class LevelAnalyserTests
{
    // The spec says "equal to Web Audio's" (V-T18) and gives no number, so the test asks for equal: every byte, and the four values to 1e-12.
    // (ED-NEW-f-vt-2 (5): the first run on x64 .NET 10 matched all 252,928 fixture bytes exactly, so no slack is allowed. Chrome keeps float32
    // FFT results and the port computes in double; had a byte sat on a floor() edge the test would show it, and the tolerance would then be
    // one byte, never more.)
    private const double ValueTolerance = 1e-12;

    public static TheoryData<string> Signals() => new(AnalyserFixture.Names);

    private static List<LevelBands> RunFixture(AnalyserFixture f, out List<byte[]> spectra, bool subtractNoiseFloor = false)
    {
        var analyser = new LevelAnalyser(f.SampleRate, subtractNoiseFloor);
        var reads = new List<LevelBands>();
        spectra = [];
        var fed = 0;
        foreach (var p in f.Positions)
        {
            analyser.Push(f.Samples.AsSpan(fed, p - fed));
            fed = p;
            reads.Add(analyser.Read());
            spectra.Add(analyser.Spectrum.ToArray());
        }

        return reads;
    }

    [Theory]
    [MemberData(nameof(Signals))]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: the spectrum bytes equal Web Audio's getByteFrequencyData
    public void The_spectrum_bytes_equal_the_AnalyserNodes(string name)
    {
        var f = AnalyserFixture.Load(name);
        RunFixture(f, out var spectra);

        for (var r = 0; r < f.Positions.Length; r++)
        {
            Assert.Equal(1024, spectra[r].Length);
            for (var i = 0; i < 1024; i++)
                Assert.True(spectra[r][i] == f.Spectrum[r][i], $"{name} read {r} (frame {f.Positions[r]}) bin {i}: port {spectra[r][i]}, Chrome {f.Spectrum[r][i]}");
        }
    }

    [Theory]
    [MemberData(nameof(Signals))]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: all, low, mid and high equal what the design reads from the AnalyserNode
    public void All_low_mid_and_high_equal_the_designs_reading_of_the_AnalyserNode(string name)
    {
        var f = AnalyserFixture.Load(name);
        var reads = RunFixture(f, out _);

        for (var r = 0; r < reads.Count; r++)
        {
            var at = $"{name} read {r} (frame {f.Positions[r]})";
            Assert.True(Math.Abs(reads[r].All - f.All[r]) <= ValueTolerance, $"{at} all: port {reads[r].All}, Chrome {f.All[r]}");
            Assert.True(Math.Abs(reads[r].Low - f.Low[r]) <= ValueTolerance, $"{at} low: port {reads[r].Low}, Chrome {f.Low[r]}");
            Assert.True(Math.Abs(reads[r].Mid - f.Mid[r]) <= ValueTolerance, $"{at} mid: port {reads[r].Mid}, Chrome {f.Mid[r]}");
            Assert.True(Math.Abs(reads[r].High - f.High[r]) <= ValueTolerance, $"{at} high: port {reads[r].High}, Chrome {f.High[r]}");
        }
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: the fixtures exercise every part of the response (a fixture that is all silence proves nothing)
    public void The_fixtures_cover_loud_quiet_and_dead_zone_reads_in_every_band()
    {
        var all = AnalyserFixture.Names.Select(AnalyserFixture.Load).ToList();
        Assert.Contains(all, f => f.All.Any(a => a == 1.0));
        Assert.Contains(all, f => f.All.Any(a => a == 0.0) && f.Spectrum.Any(s => s.Any(b => b > 0)));
        Assert.Contains(all, f => f.Low.Any(v => v > 0.1));
        Assert.Contains(all, f => f.Mid.Any(v => v > 0.1));
        Assert.Contains(all, f => f.High.Any(v => v > 0.1));
        Assert.Contains(all, f => f.SampleRate == 16000);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: all = min(1, max(0, rms - 0.004) * 5.5); bands are 0 whenever all is 0
    public void Below_the_dead_zone_the_level_and_every_band_are_zero_even_though_the_spectrum_is_not()
    {
        var analyser = new LevelAnalyser(48000, subtractNoiseFloor: false);
        analyser.Push(DspSignals.Sine(1000, 0.005, 48000, 0.2)); // rms 0.00354 < 0.004
        var bands = analyser.Read();

        Assert.Equal(LevelBands.Zero, bands);
        Assert.Contains(analyser.Spectrum.ToArray(), b => b > 0);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: rms - 0.004, times 5.5
    public void Above_the_dead_zone_the_level_is_rms_less_0_004_times_5_5()
    {
        var analyser = new LevelAnalyser(48000, subtractNoiseFloor: false);
        analyser.Push(DspSignals.Sine(1000, 0.01, 48000, 0.2)); // rms = 0.01 / sqrt(2) = 0.007071
        var bands = analyser.Read();

        Assert.Equal((0.01 / Math.Sqrt(2) - 0.004) * 5.5, bands.All, 4);
        Assert.Equal(0.01 / Math.Sqrt(2), analyser.Rms, 4);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: smoothing X = 0.65 X_prev + 0.35 |X|; Reset clears it
    public void Reset_clears_the_smoothing_so_the_next_read_equals_a_new_analysers()
    {
        var tone = DspSignals.Sine(1000, 0.5, 48000, 0.2);
        var used = new LevelAnalyser(48000, subtractNoiseFloor: false);
        used.Push(DspSignals.Noise(0.3, 9600, seed: 4));
        used.Read();
        used.Read();
        used.Reset();
        used.Push(tone);
        var afterReset = used.Read();
        var afterResetBytes = used.Spectrum.ToArray();

        var fresh = new LevelAnalyser(48000, subtractNoiseFloor: false);
        fresh.Push(tone);
        Assert.Equal(fresh.Read(), afterReset);
        Assert.Equal(fresh.Spectrum.ToArray(), afterResetBytes);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // V-5.3: the last 2048 samples, whatever the push sizes
    public void The_push_size_does_not_change_what_is_read()
    {
        var input = DspSignals.Noise(0.2, 10000, seed: 21);
        var whole = new LevelAnalyser(44100, subtractNoiseFloor: false);
        whole.Push(input);
        var a = whole.Read();

        var pieces = new LevelAnalyser(44100, subtractNoiseFloor: false);
        for (var at = 0; at < input.Length; at += 333) pieces.Push(input.AsSpan(at, Math.Min(333, input.Length - at)));
        Assert.Equal(a, pieces.Read());
        Assert.Equal(whole.Spectrum.ToArray(), pieces.Spectrum.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T18")]
    [Trait("Decision", "ED-267")] // a device rate that is not positive is no device
    public void A_sample_rate_that_is_not_positive_is_refused(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LevelAnalyser(rate));
    }

    // ---- V-T19: the noise floor (V-5.4) -----------------------------------------------------------------------------------------

    private static (List<double> Times, List<LevelBands> With, List<LevelBands> Without) ScriptedNoisyPause()
    {
        // 1 s of room noise (rms 0.03), 1 s of speech (a tone of rms 0.212), then a 3 s pause in the same room noise. Read at 60 Hz.
        const int rate = 48000;
        var script = new List<float>();
        script.AddRange(DspSignals.Noise(0.03, rate, seed: 1));
        script.AddRange(DspSignals.Sine(1000, 0.3, rate, 1.0));
        script.AddRange(DspSignals.Noise(0.03, rate * 3, seed: 2));

        var with = new LevelAnalyser(rate);
        var without = new LevelAnalyser(rate, subtractNoiseFloor: false);
        var times = new List<double>();
        var a = new List<LevelBands>();
        var b = new List<LevelBands>();
        for (var at = 0; at + 800 <= script.Count; at += 800)
        {
            var block = script.GetRange(at, 800).ToArray();
            with.Push(block);
            without.Push(block);
            times.Add((at + 800) / (double)rate);
            a.Add(with.Read());
            b.Add(without.Read());
        }

        return (times, a, b);
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: the noise floor is subtracted from rms; a pause in a noisy room reads 0
    public void In_a_noisy_pause_all_returns_to_zero_with_the_floor_subtracted()
    {
        var (times, with, without) = ScriptedNoisyPause();
        for (var i = 0; i < times.Count; i++)
        {
            var inPause = times[i] >= 2.1;       // speech ended at 2.0 s; the 2048-sample window is clear of it after 43 ms
            var inRoomBeforeSpeech = times[i] is >= 0.2 and < 1.0;
            if (inPause || inRoomBeforeSpeech) Assert.Equal(0.0, with[i].All);
            if (inPause) Assert.Equal(LevelBands.Zero, with[i]);
        }
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: without the floor the same pause holds the orb above rest (the reason the floor exists)
    public void Without_the_floor_the_same_pause_stays_well_above_zero()
    {
        var (times, _, without) = ScriptedNoisyPause();
        for (var i = 0; i < times.Count; i++)
            if (times[i] >= 2.1) Assert.InRange(without[i].All, 0.1, 0.2); // (0.03 - 0.004) * 5.5 = 0.143
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: speech above the floor still reads
    public void Speech_over_the_floor_still_reads_loud()
    {
        var (times, with, _) = ScriptedNoisyPause();
        for (var i = 0; i < times.Count; i++)
            if (times[i] is >= 1.2 and < 1.95) Assert.True(with[i].All > 0.9, $"{times[i]:F3} s: {with[i].All}");
    }

    [Fact]
    [Trait("Feature", "VT-2")]
    [Trait("Spec", "V-T19")]
    [Trait("Decision", "ED-267")] // V-5.4: NoiseFloorValue is the rolling minimum
    public void The_analysers_floor_is_the_rolling_minimum_of_its_noise_floor()
    {
        var analyser = new LevelAnalyser(48000);
        Assert.Equal(0.0, analyser.NoiseFloorValue);
        analyser.Push(DspSignals.Noise(0.03, 48000, seed: 1));
        Assert.InRange(analyser.NoiseFloorValue, 0.028, 0.031);
    }
}
