using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// V-7.7: words after stop must keep the entire recording, including words older than the live 30 s window.
[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class VoiceStoppedRecordingTests
{
    [Fact]
    [Trait("Feature", "VT-15")]
    public void Accuracy_choice_keeps_speech_older_than_thirty_seconds()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Vad = new ScriptedVad([], baseline: 1f);
        rig.Engine = ScriptedEngine.Always(new Hypothesis([new HypothesisWord("first", 0.1, 0.4), new HypothesisWord("last", 34, 35)]));
        var session = new VoiceSession(() => new VoiceSessionSettings(rig.Orb, rig.Typing, rig.WindowsUiLanguage, rig.Model) { LiveWords = false },
            _ => { }, rig.Seams, rig.Hub);
        session.Activate();
        rig.Settle();
        rig.Advance(35);
        Assert.Empty(rig.Engine.Decodes);
        session.Activate();
        rig.Settle();
        var decode = Assert.Single(rig.Engine.Decodes);
        Assert.Equal(TimeSpan.Zero, decode.Request.AudioStart);
        Assert.True(decode.AudioLength > TimeSpan.FromSeconds(35));
        Assert.Equal("first last", string.Join(' ', session.Words.Select(w => w.Text)));
    }
}
