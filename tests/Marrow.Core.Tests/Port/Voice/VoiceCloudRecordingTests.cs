using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// voice-SPEC V-18.1/V-18.2: cloud records in memory until stop and does not use local recognition windows.
public sealed class VoiceCloudRecordingTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T110")]
    public void A_cloud_session_waits_for_stop_and_decodes_all_audio_including_pauses()
    {
        using var rig = new VoiceSessionRig();
        rig.Typing = rig.Typing with { Engine = VoiceEngine.Cloud };
        rig.Vad = ScriptedVad.Speech((1, 2), (39, 40));
        rig.Engine = ScriptedEngine.Always(new Hypothesis([new("first", 1, 2), new("last", 39, 40)]));
        rig.Session.Activate();
        rig.Settle();
        rig.Capture.PushNow(_ => .25f, TimeSpan.FromSeconds(40));
        rig.Settle();
        Assert.Empty(rig.Engine.Decodes);
        Assert.Empty(rig.Updates);
        Assert.Equal(TimeSpan.FromSeconds(40), rig.Session.BufferedAudio);
        rig.Session.Activate();
        rig.Settle();
        var request = Assert.Single(rig.Engine.Decodes);
        Assert.Equal(DecodeMode.Final, request.Request.Mode);
        Assert.Equal(TimeSpan.Zero, request.Request.AudioStart);
        Assert.Equal(40 * 16000, request.Audio.Length);
        Assert.Equal(["first", "last"], rig.Session.Words.Select(word => word.Text));
        rig.Advance(2);
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-16")]
    [Trait("Spec", "V-T111")]
    public void Cancelling_cloud_Listening_sends_nothing_and_clears_the_recording()
    {
        using var rig = new VoiceSessionRig();
        rig.Typing = rig.Typing with { Engine = VoiceEngine.Cloud };
        rig.Vad = ScriptedVad.Speech((0, 300));
        rig.Session.Activate();
        rig.Settle();
        rig.Capture.PushNow(_ => .25f, TimeSpan.FromSeconds(2));
        rig.Settle();
        rig.Session.Cancel();
        rig.Settle();
        Assert.Empty(rig.Engine.Decodes);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
    }
}
