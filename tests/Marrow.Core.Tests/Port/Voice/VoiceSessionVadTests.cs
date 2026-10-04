using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-6.2): the speech state the label and the passes read, "enters speech when p >= 0.5; leaves when p < 0.35 for 100 ms". Frames are
// 512 samples (32 ms); a frame's probability comes from the scripted VAD by its position, so each frame is delivered by hand and read.
public sealed class VoiceSessionVadTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(32);

    // One probability per 32 ms frame: ScriptedVad answers by position, frame n starting at n x 32 ms.
    private static VoiceSessionRig Rig(params float[] probabilities)
    {
        var rig = new VoiceSessionRig();
        rig.Vad = new ScriptedVad(probabilities.Select((p, n) => (n * 512.0 / 16000, (n + 1) * 512.0 / 16000, p)), 0.02f);
        rig.Session.Activate();
        return rig;
    }

    private static bool Next(VoiceSessionRig rig)
    {
        rig.Capture.PushNow(ScriptedSignals.RoomNoise, Frame);
        return rig.Session.InSpeech;
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.2: speech starts at p >= 0.5, not below
    public void Speech_starts_at_a_probability_of_0_5()
    {
        using var rig = Rig(0.49f, 0.499f, 0.5f);
        Assert.False(Next(rig));
        Assert.False(Next(rig));
        Assert.True(Next(rig));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.2: below 0.35 for 100 ms leaves speech; three 32 ms frames (96 ms) do not, the fourth (128 ms) does
    public void Speech_ends_after_100_ms_below_0_35()
    {
        using var rig = Rig(0.9f, 0.34f, 0.34f, 0.34f, 0.34f);
        Assert.True(Next(rig));
        Assert.True(Next(rig));
        Assert.True(Next(rig));
        Assert.True(Next(rig));
        Assert.False(Next(rig));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.2: a frame at or above 0.35 restarts the 100 ms, and a probability between 0.35 and 0.5 keeps speech on
    public void A_frame_at_or_above_0_35_restarts_the_100_ms_and_the_middle_band_keeps_speech()
    {
        using var rig = Rig(0.9f, 0.1f, 0.1f, 0.1f, 0.35f, 0.1f, 0.1f, 0.1f, 0.45f, 0.45f, 0.45f, 0.45f, 0.45f, 0.1f);
        for (var n = 0; n < 13; n++) Assert.True(Next(rig), "frame " + n);
        Assert.True(Next(rig), "one quiet frame after the middle band");
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.2: the VAD is asked in frames of its own size; a partial frame waits for the rest
    public void The_VAD_is_asked_once_per_512_samples()
    {
        using var rig = Rig(0.9f);
        rig.Capture.PushNow(ScriptedSignals.RoomNoise, TimeSpan.FromMilliseconds(16));
        Assert.Equal(0, rig.Vad.FramesSeen);
        rig.Capture.PushNow(ScriptedSignals.RoomNoise, TimeSpan.FromMilliseconds(16));
        Assert.Equal(1, rig.Vad.FramesSeen);
        Assert.True(rig.Session.InSpeech);
    }
}
