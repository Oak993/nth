using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-11 (voice-SPEC V-2.2, V-5.3; the VT-3 addition the orb needs): the session publishes the level and bands of the audio it is listening to. The capture hands the session each block
// on the processing thread (IAudioCaptureSink.OnAudioBlock, before the block is posted to the main executor), and AudioLevel reads what was published. It is zero outside Listening.
public sealed class VoiceSessionLevelTests
{
    private static LevelBands Reference(IEnumerable<float> mono16k)
    {
        var analyser = new LevelAnalyser(16000);
        long fed = 0, reads = 0;
        var last = LevelBands.Zero;
        foreach (var s in mono16k)
        {
            analyser.Push([s]);
            fed++;
            while ((reads + 1) * 16000 / 60 <= fed)
            {
                reads++;
                last = analyser.Read();
            }
        }
        return last;
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // nothing published at rest, in Hover, and before a click
    public void The_level_is_zero_before_any_session()
    {
        using var rig = new VoiceSessionRig();
        Assert.Equal(LevelBands.Zero, rig.Session.AudioLevel);
        rig.Session.PointerEnter();
        Assert.Equal(LevelBands.Zero, rig.Session.AudioLevel);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // the level follows the voice: a tone in the speech spans reads high, the room's noise between them reads nothing, each equal to the analyser's own read
    public void The_level_follows_the_voice_while_listening()
    {
        using var rig = new VoiceSessionRig();
        var script = VoiceSessionRig.SwitchTheme();
        rig.Speak(script);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);

        rig.Advance(0.3); // room noise before the first word (0.5 s)
        Assert.Equal(0, rig.Session.AudioLevel.All);

        rig.Advance(0.6); // 0.9 s: "switch" has been said since 0.5 s
        var speaking = rig.Session.AudioLevel;
        Assert.True(speaking.All > 0.2, "level " + speaking.All);
        Assert.InRange(speaking.All, 0, 1);

        var signal = script.Signal(16000);
        var expected = Reference(Enumerable.Range(0, (int)Math.Round(0.9 * 16000)).Select(i => signal(i)));
        Assert.Equal(expected, speaking);

        rig.Advance(0.6); // 1.5 s: inside "theme" (1.3 to 1.8 s)
        Assert.True(rig.Session.AudioLevel.All > 0.2);
        rig.Advance(0.9); // 2.4 s: "please." (2.0 to 2.5 s) is still being said
        Assert.True(rig.Session.AudioLevel.All > 0.2);
        rig.Advance(0.5); // 2.9 s: the room again
        Assert.Equal(0, rig.Session.AudioLevel.All);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // stopping ends the reading: Processing, Landing and the Rest after read zero, and a new session does not start on the last one's level
    public void The_level_is_zero_once_listening_has_ended_and_a_new_session_starts_from_silence()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(1.5);
        Assert.True(rig.Session.AudioLevel.All > 0);

        rig.Session.Activate(); // the second click
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(LevelBands.Zero, rig.Session.AudioLevel);
        rig.Advance(5);
        Assert.True(rig.Session.State == VoiceSessionState.Rest, "state after the final pass: " + rig.Session.State);
        Assert.Equal(LevelBands.Zero, rig.Session.AudioLevel);

        rig.QuietRoom();
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Advance(0.5);
        Assert.True(rig.Session.AudioLevel == LevelBands.Zero, "a new session started on " + rig.Session.AudioLevel);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // a capture that does not tap the processing thread (a sink call of OnAudio only) leaves the level at zero, and the block hook has an empty default
    public void A_sink_that_is_given_only_the_main_executors_batches_publishes_nothing()
    {
        using var rig = new VoiceSessionRig();
        rig.Capture = new ScriptedCapture();
        rig.Session.Activate();
        rig.Settle();
        IAudioCaptureSink sink = rig.Session;
        sink.OnAudio(new float[1600].Select(_ => 0.4f).ToArray());
        Assert.Equal(LevelBands.Zero, rig.Session.AudioLevel);
    }
}
