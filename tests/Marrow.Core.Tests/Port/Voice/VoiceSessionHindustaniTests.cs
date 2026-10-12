using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// ED-NEW-voicefix-4: a session written in Hindi or Urdu is remembered (voiceTyping.lastHindustani), and the next Auto-detect session weighs it: English must then be
// decisive before it is decoded, and Hindustani is written in the remembered script. A Hindi or Urdu Windows display language counts the same before any session.
public sealed class VoiceSessionHindustaniTests
{
    // "Haan bhai, kya chal raha hai aajkal" as the owner said it: about 2.5 s of speech in one stretch
    private static VoiceScript HaanBhai() => new(
    [
        new ScriptedWord("हाँ", 0.5, 0.8), new ScriptedWord("भाई,", 0.9, 1.3), new ScriptedWord("क्या", 1.5, 1.8), new ScriptedWord("चल", 1.9, 2.1),
        new ScriptedWord("रहा", 2.2, 2.4), new ScriptedWord("है", 2.5, 2.7), new ScriptedWord("आजकल?", 2.8, 3.0),
    ]);

    private static void Dictate(VoiceSessionRig rig, LanguageOdds odds)
    {
        rig.Speak(HaanBhai());
        rig.Engine.Detect = (_, _) => odds;
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(3.4);
        rig.Session.Activate(); // stop: the final pass decodes what is held
        rig.Settle();
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void A_Hindi_session_is_remembered_once_and_the_next_session_does_not_take_a_fairly_sure_English()
    {
        using var rig = new VoiceSessionRig();
        Dictate(rig, new LanguageOdds("en", 0.60f, "hi", 0.30f));
        Assert.NotEmpty(rig.Engine.Decodes);
        Assert.All(rig.Engine.Decodes, d => Assert.Equal("hi", d.Request.Language));
        Assert.Equal("hi", Assert.Single(rig.Stored).LastHindustani);
        Assert.Equal("hi", rig.Session.DetectedLanguage);

        // the next session: Whisper fairly sure of English (0.85), which without the memory would be trusted
        Dictate(rig, new LanguageOdds("en", 0.85f, "hi", 0.03f));
        Assert.All(rig.Engine.Decodes, d => Assert.Equal("hi", d.Request.Language));
        Assert.Single(rig.Stored); // unchanged: not stored again
        Assert.Equal("हाँ भाई, क्या चल रहा है आजकल?", string.Join(' ', rig.Updates.Last(u => u.Kind == TranscriptUpdateKind.Final).Words.Select(w => w.Text)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void The_remembered_script_is_kept_when_Whisper_hears_the_other_one()
    {
        using var rig = new VoiceSessionRig { Typing = new VoiceTypingSettings { LastHindustani = "ur" } };
        Dictate(rig, new LanguageOdds("hi", 0.70f, "hi", 0.70f));
        Assert.All(rig.Engine.Decodes, d => Assert.Equal("ur", d.Request.Language));
        Assert.Empty(rig.Stored);
    }

    [Theory]
    [InlineData("hi-IN", "hi")]
    [InlineData("ur-PK", "ur")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void A_Hindi_or_Urdu_Windows_display_language_counts_as_Hindustani_before(string windows, string expected)
    {
        using var rig = new VoiceSessionRig { WindowsUiLanguage = windows };
        Dictate(rig, new LanguageOdds("en", 0.85f, "hi", 0.03f));
        Assert.All(rig.Engine.Decodes, d => Assert.Equal(expected, d.Request.Language));
        Assert.Equal(expected, Assert.Single(rig.Stored).LastHindustani);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void An_English_session_remembers_nothing()
    {
        using var rig = new VoiceSessionRig();
        Dictate(rig, new LanguageOdds("en", 0.97f, "hi", 0.01f));
        Assert.All(rig.Engine.Decodes, d => Assert.Equal("en", d.Request.Language));
        Assert.Empty(rig.Stored);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("ur")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void A_session_forced_to_Hindi_or_Urdu_is_remembered_and_asks_for_no_detection(string code)
    {
        using var rig = new VoiceSessionRig { Typing = new VoiceTypingSettings { Lang = code } };
        Dictate(rig, new LanguageOdds("en", 0.99f, "hi", 0.001f));
        Assert.Empty(rig.Engine.Detections);
        Assert.All(rig.Engine.Decodes, d => Assert.Equal(code, d.Request.Language));
        var stored = Assert.Single(rig.Stored);
        Assert.Equal(code, stored.LastHindustani);
        Assert.Equal(code, stored.Lang); // the rest of the record is kept
    }
}
