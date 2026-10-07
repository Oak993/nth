using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-15, V-7.7: the stored LiveWords flag controls inference, while capture and the final pass stay real.
[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoicePreparedSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "VT-15")]
    public void The_record_controls_live_passes_and_always_keeps_the_final_pass(bool live)
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        var updates = new List<TranscriptUpdate>();
        var session = new VoiceSession(() => new VoiceSessionSettings(rig.Orb, rig.Typing, rig.WindowsUiLanguage, rig.Model) { LiveWords = live },
            _ => { }, rig.Seams, rig.Hub);
        session.TranscriptChanged += updates.Add;
        session.Activate();
        rig.Settle();
        rig.Advance(3);
        Assert.Equal(1, rig.Capture.StartCount);
        if (live) Assert.Contains(rig.Engine.Decodes, d => d.Request.Mode == DecodeMode.Live);
        else
        {
            Assert.Empty(rig.Engine.Decodes);
            Assert.Empty(updates);
        }
        session.Activate();
        rig.Settle();
        Assert.Contains(rig.Engine.Decodes, d => d.Request.Mode == DecodeMode.Final);
        Assert.Equal("switch the theme please.", string.Join(' ', session.Words.Select(w => w.Text)));
    }
}
