using System.Buffers.Binary;
using System.Text;
using Marrow.UI.Port;
using Marrow.UI.Sound;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// NSSound as ED-151 gives it, on the fake output (FakeSoundOutput). The only NSSound sites at B2 are
// DroppyCode/UI/Sound/FinishChime.swift:12-22 and DroppyCode/UI/Sound/SettleChime.swift:11-21:
// NSSound(data:) is failable (nil for data it cannot read), `volume` is a Float set once, FinishChime relies on
// play() declining while the sound is in flight (:19-21), and SettleChime calls stop() then play() for a replay
// from the top (:55-57). The chime bytes come from ToneSynth.wave (DroppyCode/UI/Sound/ToneSynth.swift:27-68),
// whose header (:52-64) fixes the format every expectation below reads: PCM, mono, 44100 Hz, 16 bits, 2 bytes a
// frame, then `count * 2` sample bytes with count = Int(44100 * duration).
public sealed class SwiftSoundTests
{
    // FinishChime.swift:25-36
    private static readonly ToneSynth.Note[] FinishNotes =
    [
        new ToneSynth.Note(587.33, 0, 0.8),
        new ToneSynth.Note(880, 0.14, 1),
    ];

    private static readonly ToneSynth.Partial[] FinishPartials =
    [
        new ToneSynth.Partial(1, 1, 0.14),
        new ToneSynth.Partial(2, 0.22, 0.06),
        new ToneSynth.Partial(4, 0.08, 0.04),
    ];

    // FinishChime.swift:13, duration 0.7
    private static byte[] FinishWave() => ToneSynth.Wave(FinishNotes, FinishPartials, 0.7);

    private static SwiftSound FinishSound() => SwiftSound.Create(FinishWave()) ?? throw new InvalidOperationException("no sound");

    /// <summary>A RIFF/WAVE file with the given fmt fields and data bytes, laid out as ToneSynth.swift:52-64 lays it out.</summary>
    private static byte[] Wave(ushort formatTag, ushort channels, uint sampleRate, ushort bitsPerSample, byte[] samples,
        bool withData = true, byte[]? extraChunkBeforeData = null)
    {
        var blockAlign = (ushort)(channels * bitsPerSample / 8);
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        body.AddRange(Encoding.ASCII.GetBytes("fmt "));
        body.AddRange(U32(16));
        body.AddRange(U16(formatTag));
        body.AddRange(U16(channels));
        body.AddRange(U32(sampleRate));
        body.AddRange(U32(sampleRate * blockAlign));
        body.AddRange(U16(blockAlign));
        body.AddRange(U16(bitsPerSample));
        if (extraChunkBeforeData is not null) body.AddRange(extraChunkBeforeData);
        if (withData)
        {
            body.AddRange(Encoding.ASCII.GetBytes("data"));
            body.AddRange(U32((uint)samples.Length));
            body.AddRange(samples);
        }
        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        file.AddRange(U32((uint)body.Count));
        file.AddRange(body);
        return file.ToArray();
    }

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int16Samples(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }

    public static TheoryData<string, byte[]> UnplayableData() => new()
    {
        { "empty", [] },
        { "text", Encoding.UTF8.GetBytes("not a sound") },
        { "RIFF with no WAVE", Encoding.ASCII.GetBytes("RIFF\u0004\0\0\0AVI ") },
        { "header cut inside fmt", FinishWave()[..30] },
        { "IEEE float, not PCM", Wave(3, 1, 44100, 32, new byte[8]) },
        { "no data chunk", Wave(1, 1, 44100, 16, [], withData: false) },
        { "no channels", Wave(1, 0, 44100, 16, new byte[4]) },
        { "no sample rate", Wave(1, 1, 0, 16, new byte[4]) },
        { "12-bit samples", Wave(1, 1, 44100, 12, new byte[4]) },
    };

    [Theory]
    [MemberData(nameof(UnplayableData))]
    [Trait("Decision", "ED-151")]
    [Trait("Rule", "R-NAME-5")]
    public void Create_returns_null_for_data_that_is_not_a_playable_PCM_WAV_as_NSSound_data_init_returns_nil(string label, byte[] data)
    {
        Assert.False(string.IsNullOrEmpty(label));
        Assert.Null(SwiftSound.Create(data));
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Create_reads_the_ToneSynth_wave_of_the_finish_chime_as_mono_44100_Hz_16_bit_PCM()
    {
        var wave = FinishWave();
        var sound = SwiftSound.Create(wave);

        Assert.NotNull(sound);
        Assert.Equal(wave, sound.Data.ToArray());
        // ToneSynth.swift:57-62
        Assert.Equal(1, sound.Format.Channels);
        Assert.Equal(44100u, sound.Format.SampleRate);
        Assert.Equal(16, sound.Format.BitsPerSample);
        Assert.Equal(2, sound.Format.BlockAlign);
        // ToneSynth.swift:28,63-67: Int(44100 * 0.7) = 30870 samples after the 44-byte header
        Assert.Equal(30870 * 2, sound.Samples.Length);
        Assert.Equal(wave[44..], sound.Samples.ToArray());
        Assert.False(sound.IsPlaying);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Create_keeps_its_own_copy_of_the_bytes_as_NSSound_data_init_takes_a_Data_value()
    {
        var wave = FinishWave();
        var expected = wave.ToArray();
        var sound = FinishSound();
        var created = SwiftSound.Create(wave)!;

        Array.Clear(wave);

        Assert.Equal(expected, created.Data.ToArray());
        Assert.Equal(sound.Samples.ToArray(), created.Samples.ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Create_skips_a_chunk_it_does_not_know_before_the_data_chunk()
    {
        var samples = Int16Samples(1, -2, 3);
        byte[] list = [.. Encoding.ASCII.GetBytes("LIST"), .. U32(4), .. Encoding.ASCII.GetBytes("INFO")];

        var sound = SwiftSound.Create(Wave(1, 2, 22050, 8, samples, extraChunkBeforeData: list));

        Assert.NotNull(sound);
        Assert.Equal(2, sound.Format.Channels);
        Assert.Equal(22050u, sound.Format.SampleRate);
        Assert.Equal(8, sound.Format.BitsPerSample);
        Assert.Equal(samples, sound.Samples.ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Play_starts_one_playback_of_the_sound_and_returns_true()
    {
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        Assert.True(sound.Play());

        Assert.True(sound.IsPlaying);
        var play = Assert.Single(output.Plays);
        Assert.Same(sound, play.Sound);
        Assert.Equal(["play"], output.Log);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Play_while_the_sound_is_playing_returns_false_and_does_nothing_so_several_finishes_make_one_chime()
    {
        // FinishChime.swift:19-21: "NSSound declines a play while one is in flight"
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        Assert.True(sound.Play());
        Assert.False(sound.Play());
        Assert.False(sound.Play());

        Assert.True(sound.IsPlaying);
        Assert.Single(output.Plays);
        Assert.Equal(0, output.Stops);
        Assert.Equal(["play"], output.Log);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Stop_then_play_replays_from_the_top_so_two_settles_in_a_row_tick_twice()
    {
        // SettleChime.swift:55-57
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        sound.Stop();
        Assert.True(sound.Play());
        Assert.True(sound.Stop());
        Assert.False(sound.IsPlaying);
        Assert.True(sound.Play());

        Assert.True(sound.IsPlaying);
        Assert.Equal(2, output.Plays.Count);
        Assert.True(output.Plays[0].Stopped);
        Assert.False(output.Plays[1].Stopped);
        Assert.Equal(["play", "stop", "play"], output.Log);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Stop_while_the_sound_is_idle_leaves_the_output_untouched()
    {
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        sound.Stop();

        Assert.False(sound.IsPlaying);
        Assert.Empty(output.Log);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void The_volume_reaches_the_output_with_each_play()
    {
        // FinishChime.swift:10,14 and SettleChime.swift:46,50: `sound?.volume = volume` before any play
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        sound.Volume = 0.35f;
        Assert.Equal(0.35f, sound.Volume);
        Assert.True(sound.Play());
        sound.Stop();
        sound.Volume = 0.18f;
        Assert.True(sound.Play());

        Assert.Equal([0.35f, 0.18f], output.Volumes);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void A_playback_that_completes_turns_IsPlaying_false_and_the_next_play_starts_again()
    {
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        Assert.True(sound.Play());
        output.CompleteLast();

        Assert.False(sound.IsPlaying);
        Assert.True(sound.Play());
        Assert.True(sound.IsPlaying);
        Assert.Equal(2, output.Plays.Count);
        Assert.Equal(0, output.Stops);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void A_finish_report_from_a_stopped_playback_does_not_end_the_replay_that_followed_it()
    {
        var output = new FakeSoundOutput();
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        Assert.True(sound.Play());
        sound.Stop();
        Assert.True(sound.Play());
        output.Plays[0].ReportFinishedLate();

        Assert.True(sound.IsPlaying);
        Assert.False(sound.Play());
        Assert.Equal(2, output.Plays.Count);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void Play_returns_false_and_the_sound_stays_idle_when_the_output_cannot_start()
    {
        var output = new FakeSoundOutput { Refuses = true };
        using var _ = SoundOutput.Current.Override(output);
        var sound = FinishSound();

        Assert.False(sound.Play());

        Assert.False(sound.IsPlaying);
        output.Refuses = false;
        Assert.True(sound.Play());
        Assert.True(sound.IsPlaying);
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void The_production_output_applies_the_volume_to_16_bit_samples()
    {
        var sound = SwiftSound.Create(Wave(1, 1, 44100, 16, Int16Samples(1000, -2000, 30000, -32768, 0)))!;

        Assert.Equal(Int16Samples(500, -1000, 15000, -16384, 0), WaveOutSoundOutput.ApplyVolume(sound, 0.5f));
        Assert.Equal(sound.Samples.ToArray(), WaveOutSoundOutput.ApplyVolume(sound, 1f));
        Assert.Equal(new byte[10], WaveOutSoundOutput.ApplyVolume(sound, 0f));
    }

    [Fact]
    [Trait("Decision", "ED-151")]
    public void The_production_output_applies_the_volume_to_8_bit_samples_around_their_128_midpoint()
    {
        var sound = SwiftSound.Create(Wave(1, 1, 8000, 8, [128, 228, 28, 0, 208]))!;

        Assert.Equal([128, 178, 78, 64, 168], WaveOutSoundOutput.ApplyVolume(sound, 0.5f));
        Assert.Equal([128, 228, 28, 0, 208], WaveOutSoundOutput.ApplyVolume(sound, 1f));
        Assert.Equal([128, 128, 128, 128, 128], WaveOutSoundOutput.ApplyVolume(sound, 0f));
    }
}
