using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Barge-in", 16 "done when"): the gate that tells the owner speaking over Dextra from Dextra's own voice
// coming back through the speakers. All audio here is synthetic (SyntheticSpeech): a played voice, its echo at a gain and a delay, the
// owner as a second voice. "Its own speech fed back at -6 dB never triggers" is checked at several delays, with a late reflection and a
// delay that drifts; "barge-in" is checked by the time from the owner's first audible sample to the frame that completes it.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-2")]
public sealed class VoiceBargeInTests
{
    private const int Frame = VoiceBargeIn.FrameSamples;
    private const int Ahead = 1600; // the sound card holds 100 ms the player has already handed over

    // Feeds the gate one microphone frame at a time while the reference grows as a player would extend it: Ahead samples before what is
    // being heard. Returns the frame that completed a barge-in (-1: none) and how the frames were judged.
    private static (int FirstTrigger, int Echo, int NearEnd, int Residual, int Quiet) Simulate(float[] played, float[] heard, VoiceBargeInOptions? options = null, int stopAfter = int.MaxValue)
    {
        var reference = new VoiceEchoReference();
        var gate = new VoiceBargeIn(reference, options);
        var appended = 0;
        var first = -1;
        int echo = 0, near = 0, residual = 0, quiet = 0;
        for (var at = 0; at + Frame <= heard.Length && at / Frame < stopAfter; at += Frame)
        {
            var upTo = Math.Min(played.Length, at + Frame + Ahead);
            if (upTo > appended)
            {
                reference.Append(played.AsSpan(appended, upTo - appended));
                appended = upTo;
            }
            var frame = heard.AsSpan(at, Frame);
            var triggered = gate.Process(frame, SyntheticSpeech.EnergyProbability(frame));
            switch (gate.Last)
            {
                case EchoVerdict.Echo: echo++; break;
                case EchoVerdict.NearEnd: near++; break;
                case EchoVerdict.Residual: residual++; break;
                default: quiet++; break;
            }
            if (triggered && first < 0) first = at / Frame;
        }
        return (first, echo, near, residual, quiet);
    }

    private static float[] Room(float[] played, double gain, int delay, int seed = 77) =>
        SyntheticSpeech.Mix(SyntheticSpeech.Echo(played, gain, delay), SyntheticSpeech.Noise(seed, played.Length / (double)SyntheticSpeech.Rate, 0.0015));

    [Theory]
    [InlineData(320)]
    [InlineData(960)]
    [InlineData(2240)]
    [InlineData(4000)]
    public void Dextras_own_speech_fed_back_at_minus_6_dB_never_triggers_barge_in(int delaySamples)
    {
        var played = SyntheticSpeech.Voice(1, 9, 0.1);
        var heard = Room(played, 0.5, delaySamples);

        var run = Simulate(played, heard);

        Assert.Equal(-1, run.FirstTrigger);
        Assert.True(run.Echo > 60, $"the gate must have seen the echo as speech and called it echo ({run.Echo} frames); a gate that sees nothing proves nothing");
        Assert.Equal(0, run.NearEnd);
    }

    [Theory]
    [InlineData(0.7)]
    [InlineData(0.35)]
    public void Louder_and_quieter_echoes_are_still_its_own_voice(double gain)
    {
        var played = SyntheticSpeech.Voice(5, 8, 0.12);
        Assert.Equal(-1, Simulate(played, Room(played, gain, 1100)).FirstTrigger);
    }

    [Fact]
    public void An_echo_whose_delay_drifts_by_five_milliseconds_is_still_its_own_voice()
    {
        var played = SyntheticSpeech.Voice(3, 9, 0.1);
        var first = SyntheticSpeech.Echo(played, 0.5, 900);
        var later = SyntheticSpeech.Echo(played, 0.5, 980);
        var heard = new float[played.Length];
        for (var n = 0; n < heard.Length; n++) heard[n] = n < heard.Length / 2 ? first[n] : later[n];

        Assert.Equal(-1, Simulate(played, heard).FirstTrigger);
    }

    [Fact]
    public void What_an_echo_canceller_leaves_behind_is_residue_and_never_triggers()
    {
        var played = SyntheticSpeech.Voice(1, 9, 0.4);
        var heard = Room(played, 0.06, 1000);

        var run = Simulate(played, heard);

        Assert.Equal(-1, run.FirstTrigger);
        Assert.True(run.Residual > 20, $"the quiet echo must be seen and called residue ({run.Residual} frames)");
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(3.0073)]
    [InlineData(3.0211)]
    [InlineData(3.0309)]
    [InlineData(4.4)]
    public void The_owner_speaking_over_Dextra_completes_a_barge_in_within_150_ms_of_his_first_sound(double onsetSeconds)
    {
        var played = SyntheticSpeech.Voice(1, 9, 0.1);
        var onset = (int)(onsetSeconds * SyntheticSpeech.Rate);
        var owner = SyntheticSpeech.At(SyntheticSpeech.Voice(2, 3, 0.15), onset, played.Length);
        var heard = SyntheticSpeech.Mix(Room(played, 0.5, 960), owner);

        var run = Simulate(played, heard);

        var firstSound = Array.FindIndex(owner, s => Math.Abs(s) > 0.02f);
        Assert.True(run.FirstTrigger >= 0, "the owner talking over Dextra must stop it");
        var latencyMs = ((run.FirstTrigger + 1) * Frame - firstSound) * 1000.0 / SyntheticSpeech.Rate;
        Assert.InRange(latencyMs, 0, 150);
    }

    [Fact]
    public void The_owner_speaking_while_Dextra_is_silent_triggers_on_the_detector_alone()
    {
        var owner = SyntheticSpeech.At(SyntheticSpeech.Voice(2, 2, 0.1), SyntheticSpeech.Rate, 4 * SyntheticSpeech.Rate);
        var run = Simulate([], owner);

        var firstSound = Array.FindIndex(owner, s => Math.Abs(s) > 0.02f);
        Assert.True(run.FirstTrigger >= 0);
        Assert.InRange(((run.FirstTrigger + 1) * Frame - firstSound) * 1000.0 / SyntheticSpeech.Rate, 0, 150);
    }

    [Fact]
    public void Room_noise_and_a_single_loud_frame_never_trigger_with_or_without_Dextra_speaking()
    {
        var noise = SyntheticSpeech.Noise(9, 6, 0.004);
        Assert.Equal(-1, Simulate([], noise).FirstTrigger);
        var played = SyntheticSpeech.Voice(1, 6, 0.1);
        var heard = SyntheticSpeech.Mix(Room(played, 0.5, 960), SyntheticSpeech.At(SyntheticSpeech.Noise(4, 0.03, 0.2), 94 * Frame, played.Length));
        Assert.Equal(-1, Simulate(played, heard).FirstTrigger);
    }

    [Fact]
    public void Eight_speakers_in_rooms_with_a_loud_late_reflection_are_never_taken_for_the_owner()
    {
        for (var seed = 11; seed < 19; seed++)
        {
            var played = SyntheticSpeech.Voice(seed, 7, 0.08 + 0.01 * (seed % 5));
            foreach (var delay in new[] { 400, 1500, 3200 })
            {
                var heard = SyntheticSpeech.Mix(SyntheticSpeech.Echo(played, 0.5, delay, reflection: 0.5, reflectionAfter: 330), SyntheticSpeech.Noise(seed, 7, 0.002));
                var run = Simulate(played, heard);
                Assert.True(run.FirstTrigger == -1, $"speaker {seed}, delay {delay}: barge-in at frame {run.FirstTrigger}");
            }
        }
    }

    [Fact]
    public void Clearing_the_reference_and_resetting_the_gate_forget_the_old_utterance()
    {
        var reference = new VoiceEchoReference();
        var gate = new VoiceBargeIn(reference);
        reference.Append(SyntheticSpeech.Voice(1, 1, 0.1));
        Assert.True(reference.AudibleWithin(VoiceEchoReference.Rate));
        reference.Clear();
        Assert.False(reference.AudibleWithin(VoiceEchoReference.Rate));
        Assert.Equal(0, reference.Written);
        gate.Reset();
        Assert.Equal(0, gate.NearFrames);
    }

    [Fact]
    public void A_frame_of_the_wrong_length_is_refused()
    {
        var gate = new VoiceBargeIn(new VoiceEchoReference());
        Assert.Throws<ArgumentException>(() => gate.Process(new float[100], 0.9f));
    }

    [Fact]
    public void The_reference_copies_back_from_its_newest_sample_and_reads_zero_past_either_end()
    {
        var reference = new VoiceEchoReference();
        reference.Append(Enumerable.Range(1, 10).Select(i => (float)i).ToArray());
        var copy = new float[4];
        reference.CopyBack(copy, 0);
        Assert.Equal([7f, 8f, 9f, 10f], copy);
        reference.CopyBack(copy, 3);
        Assert.Equal([4f, 5f, 6f, 7f], copy);
        reference.CopyBack(copy, 8);
        Assert.Equal([0f, 0f, 1f, 2f], copy);
        Assert.Equal(10, reference.Written);
    }
}
