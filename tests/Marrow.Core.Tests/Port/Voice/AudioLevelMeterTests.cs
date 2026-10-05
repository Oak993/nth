using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Tests.Port.Voice;

// VT-11 (voice-SPEC V-5.3, V-2.2 items 2 and 4): the level and bands the processing thread publishes. The meter takes the capture's interleaved blocks as they come, downmixes them
// (V-5.1), pushes the device-rate mono signal into a LevelAnalyser and reads it at the fixed 60 Hz cadence of the signal (a read every rate/60 frames, whatever the block size).
// The expected values come from an analyser of the test's own fed frame by frame, not from the meter.
public sealed class AudioLevelMeterTests
{
    // The reads of a reference analyser fed one frame at a time: read k happens when k x rate / 60 frames (integer division) have been fed.
    private sealed class Reference(int rate)
    {
        private readonly LevelAnalyser analyser = new(rate);
        private long fed;
        private long reads;

        public LevelBands Last { get; private set; }

        public void Feed(float mono)
        {
            analyser.Push([mono]);
            fed++;
            while ((reads + 1) * rate / 60 <= fed)
            {
                reads++;
                Last = analyser.Read();
            }
        }
    }

    private static float[] Tone(int rate, int channels, long firstFrame, int frames, double amplitude, double hz = 220)
    {
        var samples = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        for (var c = 0; c < channels; c++)
            samples[f * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * (firstFrame + f) / rate));
        return samples;
    }

    [Theory]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-5.3: the analysis runs at a fixed 60 Hz of the signal; a block of any size gives the reads that fall inside it
    [InlineData(48000, 2, 480)]
    [InlineData(44100, 1, 441)]
    [InlineData(16000, 1, 160)]
    [InlineData(48000, 2, 1000)]
    [InlineData(16000, 1, 37)]
    public void The_published_values_are_the_analysers_reads_at_60_hz_of_the_signal(int rate, int channels, int block)
    {
        var meter = new AudioLevelMeter();
        var reference = new Reference(rate);
        long frame = 0;
        for (var n = 0; n < 60; n++)
        {
            var amplitude = n < 30 ? 0.4 : 0.02;
            var samples = Tone(rate, channels, frame, block, amplitude);
            meter.Feed(new CaptureFormat(rate, channels), samples);
            for (var f = 0; f < block; f++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += samples[f * channels + c];
                reference.Feed(sum / channels);
            }
            frame += block;
            Assert.Equal(reference.Last, meter.Latest);
        }
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-5.1: the channels are averaged; one side silent halves the amplitude, as the session's own downmix does
    public void Stereo_is_the_average_of_its_channels()
    {
        var stereo = new AudioLevelMeter();
        var mono = new AudioLevelMeter();
        var interleaved = new float[2 * 4800];
        var single = new float[4800];
        for (var f = 0; f < 4800; f++)
        {
            var v = (float)(0.6 * Math.Sin(2 * Math.PI * 300 * f / 48000.0));
            interleaved[2 * f] = v;
            interleaved[2 * f + 1] = 0;
            single[f] = v / 2;
        }
        stereo.Feed(new CaptureFormat(48000, 2), interleaved);
        mono.Feed(new CaptureFormat(48000, 1), single);
        Assert.Equal(mono.Latest, stereo.Latest);
        Assert.True(stereo.Latest.All > 0);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-5.3: nothing is published before the first 1/60 s of audio has been analysed
    public void Nothing_is_published_before_the_first_read()
    {
        var meter = new AudioLevelMeter();
        Assert.Equal(LevelBands.Zero, meter.Latest);
        meter.Feed(new CaptureFormat(48000, 1), Tone(48000, 1, 0, 799, 0.5));
        Assert.Equal(LevelBands.Zero, meter.Latest);
        meter.Feed(new CaptureFormat(48000, 1), Tone(48000, 1, 799, 1, 0.5));
        Assert.True(meter.Latest.All > 0);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // a new session starts from silence: Reset forgets the signal and the published values; a different format starts afresh too
    public void Reset_and_a_changed_format_start_from_silence()
    {
        var meter = new AudioLevelMeter();
        meter.Feed(new CaptureFormat(48000, 1), Tone(48000, 1, 0, 4800, 0.5));
        Assert.True(meter.Latest.All > 0);
        meter.Reset();
        Assert.Equal(LevelBands.Zero, meter.Latest);
        meter.Feed(new CaptureFormat(48000, 1), Tone(48000, 1, 0, 4800, 0.5));
        var first = meter.Latest;
        meter.Feed(new CaptureFormat(16000, 1), new float[1600]);
        Assert.Equal(0, meter.Latest.All); // the old analyser and its signal are gone
        meter.Reset();
        meter.Feed(new CaptureFormat(48000, 1), Tone(48000, 1, 0, 4800, 0.5));
        Assert.Equal(first, meter.Latest); // the same audio after a reset reads the same as the first time
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // a non-finite sample reads as silence (the analyser's rule), and the published values stay inside 0..1
    public void Garbage_samples_never_publish_a_value_outside_0_to_1()
    {
        var meter = new AudioLevelMeter();
        var samples = new float[4800];
        for (var i = 0; i < samples.Length; i++) samples[i] = i % 3 == 0 ? float.NaN : i % 3 == 1 ? float.PositiveInfinity : 3f;
        meter.Feed(new CaptureFormat(48000, 1), samples);
        var l = meter.Latest;
        foreach (var v in new[] { l.All, l.Low, l.Mid, l.High }) Assert.InRange(v, 0.0, 1.0);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-2.2 item 2: the processing thread's work allocates nothing once a format is running (blocks of 10 ms, stereo 48 kHz)
    public void Feeding_a_block_allocates_nothing_once_the_format_is_running()
    {
        var meter = new AudioLevelMeter();
        var format = new CaptureFormat(48000, 2);
        var block = Tone(48000, 2, 0, 480, 0.3);
        // The runtime's tiered-JIT bookkeeping allocates on the calling thread while a method is being promoted, so the steady state is waited for: three rounds of 100 blocks in
        // a row that allocate nothing, within ten seconds. A feed that allocated per block never gets there.
        var rounds = new List<long>();
        var quiet = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (quiet < 3 && DateTime.UtcNow < deadline)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) meter.Feed(format, block);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            rounds.Add(bytes);
            quiet = bytes == 0 ? quiet + 1 : 0;
            Thread.Sleep(5);
        }
        Assert.True(quiet >= 3, "feeding allocated " + string.Join(", ", rounds.TakeLast(8)) + " B per 100 blocks");
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-2.2: the UI thread reads Latest while the processing thread feeds; a read is always one publication's four values (never a mix of two)
    public void A_read_while_another_thread_feeds_is_never_a_mix_of_two_publications()
    {
        var meter = new AudioLevelMeter();
        var format = new CaptureFormat(48000, 1);
        var published = new HashSet<LevelBands> { LevelBands.Zero };
        var reference = new LevelAnalyser(48000);
        // the values a feed of 10 ms blocks publishes, each one an analyser read (the reference run on the same audio)
        var blocks = Enumerable.Range(0, 300).Select(n => Tone(48000, 1, n * 480L, 480, n % 20 < 10 ? 0.5 : 0.01)).ToArray();
        long fed = 0, reads = 0;
        foreach (var block in blocks)
        foreach (var s in block)
        {
            reference.Push([s]);
            fed++;
            while ((reads + 1) * 48000 / 60 <= fed)
            {
                reads++;
                published.Add(reference.Read());
            }
        }
        var stop = false;
        var torn = new List<LevelBands>();
        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var seen = meter.Latest;
                if (!published.Contains(seen)) torn.Add(seen);
            }
        });
        reader.Start();
        foreach (var block in blocks) meter.Feed(format, block);
        Volatile.Write(ref stop, true);
        reader.Join();
        Assert.Empty(torn);
    }
}
