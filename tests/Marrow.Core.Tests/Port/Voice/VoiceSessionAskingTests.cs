using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-15 Asking rows, V-12.11, V-7.14): the language default D, "asked on the first click of the orb".
public sealed class VoiceSessionAskingTests
{
    private static VoiceSessionRig Asking()
    {
        var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { LangMode = VoiceLangMode.Ask };
        return rig;
    }

    private static void AssertNothingTouched(VoiceSessionRig rig)
    {
        Assert.Equal(0, rig.Capture.StartCount);
        Assert.Equal(0, rig.Microphone.Asked);
        Assert.Empty(rig.Engine.Loads);
        Assert.Equal(0, rig.Vad.ResetCount);
        Assert.Empty(rig.Stored);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 Rest | click while Resolve gives ask -> Asking: with the ambient seams, which all throw when touched, nothing is touched
    public void A_click_that_would_ask_touches_no_seam_at_all()
    {
        var typing = new List<VoiceTypingSettings>();
        var orb = new VoiceOrbSettings { LangMode = VoiceLangMode.Ask };
        var session = new VoiceSession(() => new VoiceSessionSettings(orb, VoiceTypingSettings.Default, "en-US", new SpeechModel("tiny", null)),
            typing.Add, VoiceSeamSet.Ambient, new VoiceSessionHub());
        var states = new List<VoiceSessionState>();
        session.StateChanged += (_, to) => states.Add(to);
        session.Activate();
        Assert.Equal(VoiceSessionState.Asking, session.State);
        Assert.Equal([VoiceSessionState.Asking], states);
        Assert.Empty(typing);
        session.DismissQuestion();
        Assert.Equal(VoiceSessionState.Rest, session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15: Asking opens no capture, loads nothing, stores nothing and does not pop (no Listening)
    public void Asking_opens_no_capture_loads_nothing_and_stores_nothing()
    {
        using var rig = Asking();
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
        AssertNothingTouched(rig);
        Assert.Null(rig.Hub.Active);
        Assert.Empty(rig.Notes);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 Asking -> Listening, V-12.11: an answer stores voiceTyping.lang at once and listens with that language
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    [InlineData("auto")]
    public void An_answer_stores_the_language_and_starts_listening_with_it(string answer)
    {
        using var rig = Asking();
        var script = VoiceSessionRig.SwitchTheme();
        rig.Speak(script);
        rig.Session.Activate();
        rig.Session.AnswerLanguage(answer);
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Equal([VoiceSessionState.Asking, VoiceSessionState.Listening], rig.Visited);
        Assert.Equal(answer, Assert.Single(rig.Stored).Lang);
        Assert.Equal(1, rig.Capture.StartCount);
        Assert.Single(rig.Engine.Loads);
        rig.Advance(1.2);
        Assert.NotEmpty(rig.Engine.Decodes);
        Assert.All(rig.Engine.Decodes, d => Assert.Equal(answer, d.Request.Language));
        Assert.Equal(answer, rig.Session.ResolvedLanguage);
        Assert.False(rig.Session.LanguageNamed);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-7.14: once an answer is stored the next click does not ask again
    public void After_an_answer_the_next_click_listens_without_asking()
    {
        using var rig = Asking();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Session.AnswerLanguage("ur");
        rig.Advance(0.5);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Equal("ur", rig.Session.ResolvedLanguage);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 Asking -> Rest: Esc closes the question with nothing stored or started; the next click asks again
    public void Escape_closes_the_question_and_the_next_click_asks_again()
    {
        using var rig = Asking();
        rig.Session.Activate();
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        AssertNothingTouched(rig);
        rig.Session.Activate();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
        Assert.Equal([VoiceSessionState.Asking, VoiceSessionState.Rest, VoiceSessionState.Asking], rig.Visited);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 Asking -> Rest: a click outside the card closes it the same way
    public void A_click_outside_closes_the_question_with_nothing_stored()
    {
        using var rig = Asking();
        rig.Session.Activate();
        rig.Session.DismissQuestion();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        AssertNothingTouched(rig);
        rig.Session.Activate();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 Asking -> Rest: the orb clicked again closes the question; nothing stored; the next click asks
    public void A_second_orb_click_closes_the_question_with_nothing_started()
    {
        using var rig = Asking();
        rig.Session.Activate();
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        AssertNothingTouched(rig);
        rig.Session.Activate();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-12.11: an answer outside English, Hindi, Urdu and Auto-detect is refused; the question stays open
    public void An_answer_the_question_does_not_offer_is_refused()
    {
        using var rig = Asking();
        rig.Session.Activate();
        Assert.Throws<ArgumentException>(() => rig.Session.AnswerLanguage("fr"));
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
        Assert.Empty(rig.Stored);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15: an answer with no question open does nothing
    public void An_answer_with_no_question_open_does_nothing()
    {
        using var rig = Asking();
        rig.Session.AnswerLanguage("ur");
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        AssertNothingTouched(rig);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15 last row: the language default changing closes an open question
    public void Changing_the_language_default_closes_the_open_question()
    {
        using var rig = Asking();
        rig.Session.Activate();
        rig.Orb = new VoiceOrbSettings { LangMode = VoiceLangMode.Auto };
        rig.Session.LanguageDefaultChanged();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        AssertNothingTouched(rig);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-7.14 rules 1 and 2: a language picked on the Voice typing page (a code or auto) means the default never asks
    public void A_language_picked_on_the_page_never_asks()
    {
        using var forced = Asking();
        forced.Typing = new VoiceTypingSettings { Lang = "hi" };
        forced.QuietRoom();
        forced.Session.Activate();
        Assert.Equal(VoiceSessionState.Listening, forced.Session.State);
        using var auto = Asking();
        auto.Typing = new VoiceTypingSettings { Lang = "auto" };
        auto.QuietRoom();
        auto.Session.Activate();
        Assert.Equal(VoiceSessionState.Listening, auto.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T124")]
    [Trait("Decision", "ED-267")] // V-15, V-4.5: the question comes first; a blocked microphone is found after the answer
    public void A_denied_microphone_shows_after_the_answer_not_before()
    {
        using var rig = Asking();
        rig.Microphone.Current = MicrophoneAccessStatus.Denied;
        rig.Session.Activate();
        Assert.Equal(VoiceSessionState.Asking, rig.Session.State);
        Assert.Equal(0, rig.Microphone.Asked);
        rig.Session.AnswerLanguage("en");
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal([VoiceNoteKind.Blocked], rig.Notes);
        Assert.Equal("en", Assert.Single(rig.Stored).Lang);
    }
}
