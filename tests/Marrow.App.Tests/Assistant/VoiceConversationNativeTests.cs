using System.Diagnostics;
using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 NATIVE checks (DESIGN.md rev 5, 16.1 "echo cancellation: the Assistant's own speech played through the speakers into the mic never triggers barge-in"; 2.4; 12.6): the parts that
// exist only on a real PC with a real microphone, speakers, voices and keyboard hook. NEVER run by the unit agent and never by the unit suites (the filter excludes Native): the lead runs
// them alone, with focus, in a quiet room, with the speakers up and the default microphone as the owner uses it, and no other agent typing or playing sound. Owed:
//  (1) the echo test below, first with the microphone in communications mode (as the product opens it), and read its numbers: echo/near-end/residue frames, and whether the peak shows the
//      microphone heard the speakers at all; then with the owner talking over the speech (by hand): the speech must stop within 200 ms of his first word;
//  (2) the voice is made by an installed voice, with cues;
//  (3) Stop flushes the speakers at once;
//  (4) the chord registers and releases with Windows, and a second holder is refused;
//  (5) the microphone opens in communications mode.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-2")]
public sealed class VoiceConversationNativeTests
{
    private const string Sentence = "Two missions are running. The shop login fix is in tests, and the video voiceover is almost done. Nothing else needs you right now.";

    [Fact]
    public async Task Dextras_own_speech_played_through_the_real_speakers_into_the_real_microphone_never_triggers_barge_in()
    {
        var reference = new VoiceEchoReference();
        var probe = new BargeInProbe(reference);
        var capture = new WasapiCapture(new NAudioCaptureHost(), () => null, runThread: true, communications: true);
        using var speech = new SpeechOut(new WinRtSpeechSynth(), () => new WasapiSpeechPlayer(), reference, new SpeechOutOptions { Language = "en-US" });
        capture.Start(probe);
        try
        {
            using var turn = speech.BeginTurn("native echo test");
            var ended = await speech.SpeakAsync(Sentence);
            await Task.Delay(500); // the room's tail
            Assert.Equal(SpeechEnd.Finished, ended);
        }
        finally
        {
            capture.Stop();
        }

        Assert.True(probe.Blocks > 100, "the microphone delivered nothing: " + probe.Verdicts);
        Assert.True(probe.Triggers == 0, "Dextra's own voice was taken for the owner: " + probe.Verdicts);
    }

    [Fact]
    public async Task A_real_voice_is_made_decoded_and_cued_by_the_PCs_own_synthesizer_and_every_voice_listed_is_installed()
    {
        var synth = new WinRtSpeechSynth();
        var voices = synth.Voices();
        Assert.NotEmpty(voices);
        Assert.All(voices, voice => Assert.True(SpeechVoices.IsLocal(voice), voice.Name));

        var made = await synth.SynthesizeAsync("Two missions are running.", SpeechVoices.Choose(voices, null, "en-US"), 1.0, CancellationToken.None);

        Assert.InRange(made.Audio.Duration.TotalSeconds, 0.8, 6);
        Assert.True(made.Audio.Samples.Max(Math.Abs) > 0.05f);
        Assert.Equal(4, made.Cues.Count);
        Assert.Equal(made.Cues.OrderBy(c => c.At), made.Cues);
        Assert.Equal(0, made.Cues[0].Start);
    }

    [Fact]
    public async Task Stop_flushes_the_real_speakers_within_50_ms_and_the_play_ends()
    {
        using var player = new WasapiSpeechPlayer();
        var tone = new float[3 * 24_000];
        for (var n = 0; n < tone.Length; n++) tone[n] = (float)(0.05 * Math.Sin(Math.Tau * 330 * n / 24_000));
        var playing = player.PlayAsync(new SynthAudio(tone, 24_000), _ => { }, CancellationToken.None);
        await Task.Delay(500);
        Assert.False(playing.IsCompleted);

        var timer = Stopwatch.StartNew();
        player.Stop();
        await playing;

        Assert.True(timer.ElapsedMilliseconds < 50, $"Stop took {timer.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_talk_chord_registers_with_Windows_a_second_holder_is_refused_and_a_released_chord_can_be_taken_again()
    {
        using var first = new VoiceChord(new StopChordThread(), () => "Ctrl+Alt+F9", () => [PcHotkey.StopDefault, PcHotkey.PauseDefault]);
        Assert.True(first.Register(), first.Status.Line);

        using (var second = new VoiceChord(new StopChordThread(), () => "Ctrl+Alt+F9"))
        {
            Assert.False(second.Register());
            Assert.True(second.Status.Taken);
        }

        first.Dispose();
        using var third = new VoiceChord(new StopChordThread(), () => "Ctrl+Alt+F9");
        Assert.True(third.Register(), third.Status.Line);
    }

    [Fact]
    public void The_microphone_opens_in_communications_mode_and_delivers_audio()
    {
        var host = new NAudioCaptureHost();
        using var device = host.OpenCommunications(null);
        var buffers = 0;
        device.DataAvailable += (_, _) => Interlocked.Increment(ref buffers);
        device.Start();
        Thread.Sleep(1500);
        device.Stop();

        Assert.True(buffers >= 10, $"{buffers} buffers in 1.5 s");
    }
}
