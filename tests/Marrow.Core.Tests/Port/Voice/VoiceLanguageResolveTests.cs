using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0097; voice-SPEC V-7.14, V-12.11): VoiceLanguage.Resolve and the reset rule. The expected values are written out
// from the rules of V-7.14, not computed by the port.
public sealed class VoiceLanguageResolveTests
{
    private static readonly string[] Windows = ["en-US", "hi-IN", "ur-PK", "fr-FR", "sw-KE"];

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14 rule 3: nothing picked; the language default decides
    [InlineData(VoiceLangMode.Auto, "en-US", "auto", false, false)]
    [InlineData(VoiceLangMode.Auto, "hi-IN", "auto", false, false)]
    [InlineData(VoiceLangMode.Auto, "ur-PK", "auto", false, false)]
    [InlineData(VoiceLangMode.Auto, "fr-FR", "auto", false, false)]
    [InlineData(VoiceLangMode.Auto, "sw-KE", "auto", false, false)]
    [InlineData(VoiceLangMode.Show, "en-US", "auto", true, false)]
    [InlineData(VoiceLangMode.Show, "hi-IN", "auto", true, false)]
    [InlineData(VoiceLangMode.Show, "ur-PK", "auto", true, false)]
    [InlineData(VoiceLangMode.Show, "fr-FR", "auto", true, false)]
    [InlineData(VoiceLangMode.Show, "sw-KE", "auto", true, false)]
    [InlineData(VoiceLangMode.Windows, "en-US", "en", false, false)]
    [InlineData(VoiceLangMode.Windows, "hi-IN", "hi", false, false)]
    [InlineData(VoiceLangMode.Windows, "ur-PK", "ur", false, false)]
    [InlineData(VoiceLangMode.Windows, "fr-FR", "fr", false, false)]
    [InlineData(VoiceLangMode.Windows, "sw-KE", "auto", false, false)] // Swahili is not in the list: never forced
    [InlineData(VoiceLangMode.Ask, "en-US", "auto", false, true)]
    [InlineData(VoiceLangMode.Ask, "hi-IN", "auto", false, true)]
    [InlineData(VoiceLangMode.Ask, "ur-PK", "auto", false, true)]
    [InlineData(VoiceLangMode.Ask, "fr-FR", "auto", false, true)]
    [InlineData(VoiceLangMode.Ask, "sw-KE", "auto", false, true)]
    public void With_nothing_picked_the_language_default_decides(VoiceLangMode mode, string windows, string language, bool named, bool ask) =>
        Assert.Equal(new LanguageResolution(language, named, ask), VoiceLanguage.Resolve(null, mode, windows));

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14 rule 2: Auto-detect picked on the Voice typing page; naming belongs to B (show), never asking
    public void An_explicit_Auto_detect_auto_detects_and_is_named_only_under_show()
    {
        foreach (var windows in Windows)
        {
            Assert.Equal(new LanguageResolution("auto", false, false), VoiceLanguage.Resolve("auto", VoiceLangMode.Auto, windows));
            Assert.Equal(new LanguageResolution("auto", true, false), VoiceLanguage.Resolve("auto", VoiceLangMode.Show, windows));
            Assert.Equal(new LanguageResolution("auto", false, false), VoiceLanguage.Resolve("auto", VoiceLangMode.Windows, windows));
            Assert.Equal(new LanguageResolution("auto", false, false), VoiceLanguage.Resolve("auto", VoiceLangMode.Ask, windows));
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14 rule 1: a Whisper code is forced, never named, never asked, whatever the default and Windows say
    public void A_picked_code_is_forced_under_every_language_default_and_windows_language()
    {
        foreach (var code in new[] { "en", "hi", "ur", "ar", "zh" })
        foreach (var mode in Enum.GetValues<VoiceLangMode>())
        foreach (var windows in Windows)
            Assert.Equal(new LanguageResolution(code, false, false), VoiceLanguage.Resolve(code, mode, windows));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14: a stored code outside the list reads as nothing picked (V-16.2 lenient read), so the default decides
    public void A_code_outside_the_list_counts_as_nothing_picked()
    {
        Assert.Equal(VoiceLanguage.Resolve(null, VoiceLangMode.Show, "en-US"), VoiceLanguage.Resolve("xx", VoiceLangMode.Show, "en-US"));
        Assert.Equal(VoiceLanguage.Resolve(null, VoiceLangMode.Ask, "en-US"), VoiceLanguage.Resolve("EN", VoiceLangMode.Ask, "en-US"));
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14 rule 3 C: GetUserDefaultUILanguage mapped to its two-letter language name (the part before the region)
    [InlineData("en-US", "en")]
    [InlineData("en", "en")]
    [InlineData("EN-gb", "en")]
    [InlineData("zh-Hans-CN", "zh")]
    [InlineData("pt-BR", "pt")]
    [InlineData("pa-Guru-IN", "pa")]
    [InlineData("fa-IR", "fa")]
    [InlineData("fil-PH", "auto")] // three-letter language: not in the list
    [InlineData("", "auto")]
    [InlineData("-", "auto")]
    [InlineData("  ", "auto")]
    [InlineData("auto", "auto")]
    public void The_Windows_display_language_maps_to_its_list_code_or_to_auto(string windows, string language) =>
        Assert.Equal(language, VoiceLanguage.Resolve(null, VoiceLangMode.Windows, windows).Language);

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T122")]
    [Trait("Decision", "ED-267")] // V-7.14 / V-16.3.1: the code the Windows default is shown with on the Voice typing page
    public void The_Windows_language_code_is_null_when_the_list_has_no_such_language()
    {
        Assert.Equal("hi", VoiceLanguage.WindowsLanguageCode("hi-IN"));
        Assert.Null(VoiceLanguage.WindowsLanguageCode("sw-KE"));
        Assert.Null(VoiceLanguage.WindowsLanguageCode(null));
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T123")]
    [Trait("Decision", "ED-267")] // V-7.14: picking any option of the Customization row (also the one already picked) sets voiceTyping.lang back to null
    [InlineData(VoiceLangMode.Auto)]
    [InlineData(VoiceLangMode.Show)]
    [InlineData(VoiceLangMode.Windows)]
    [InlineData(VoiceLangMode.Ask)]
    public void Picking_a_language_default_stores_it_and_resets_lang_to_null_also_when_it_is_already_picked(VoiceLangMode picked)
    {
        foreach (var current in Enum.GetValues<VoiceLangMode>())
        foreach (var lang in new string?[] { null, "auto", "en", "ur" })
        {
            var orb = VoiceOrbSettings.Default with { LangMode = current, Labels = VoicePillWords.L3, OrbSize = 26 };
            var typing = VoiceTypingSettings.Default with { Lang = lang, Model = "tiny", Mic = "m" };
            var changed = VoiceLanguage.WithLanguageDefault(orb, typing, picked);
            Assert.Equal(picked, changed.Orb.LangMode);
            Assert.Null(changed.Typing.Lang);
            Assert.Equal(orb with { LangMode = picked }, changed.Orb);
            Assert.Equal(typing with { Lang = null }, changed.Typing);
        }
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T123")]
    [Trait("Decision", "ED-267")] // V-7.14: the default just chosen applies at once, and D asks again at the next first click
    public void After_the_reset_the_chosen_default_applies_and_the_question_is_asked_again()
    {
        var orb = VoiceOrbSettings.Default;
        var typing = VoiceTypingSettings.Default with { Lang = "hi" };
        Assert.Equal(new LanguageResolution("hi", false, false), VoiceLanguage.Resolve(typing.Lang, orb.LangMode, "en-US"));
        var asked = VoiceLanguage.WithLanguageDefault(orb, typing, VoiceLangMode.Ask);
        Assert.Equal(new LanguageResolution("auto", false, true), VoiceLanguage.Resolve(asked.Typing.Lang, asked.Orb.LangMode, "en-US"));
        var answered = VoiceLanguage.WithQuestionAnswer(asked.Typing, "ur");
        Assert.Equal(new LanguageResolution("ur", false, false), VoiceLanguage.Resolve(answered.Lang, asked.Orb.LangMode, "en-US"));
        var again = VoiceLanguage.WithLanguageDefault(asked.Orb, answered, VoiceLangMode.Ask);
        Assert.Equal(new LanguageResolution("auto", false, true), VoiceLanguage.Resolve(again.Typing.Lang, again.Orb.LangMode, "en-US"));
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T123")]
    [Trait("Decision", "ED-267")] // V-12.11: an answer to the language question stores en / hi / ur / auto at once
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    [InlineData("auto")]
    public void An_answer_to_the_question_stores_the_four_answers(string answer) =>
        Assert.Equal(answer, VoiceLanguage.WithQuestionAnswer(VoiceTypingSettings.Default with { Model = "base" }, answer).Lang);

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T123")]
    [Trait("Decision", "ED-267")] // V-12.11: the question offers English, Hindi, Urdu and Auto-detect only; anything else is refused
    public void The_question_has_four_answers_and_refuses_any_other()
    {
        Assert.Equal(["en", "hi", "ur", "auto"], VoiceLanguage.QuestionAnswers);
        foreach (var bad in new[] { "fr", "", "EN", "Auto-detect" })
            Assert.Throws<ArgumentException>(() => VoiceLanguage.WithQuestionAnswer(VoiceTypingSettings.Default, bad));
        Assert.Throws<ArgumentException>(() => VoiceLanguage.WithQuestionAnswer(VoiceTypingSettings.Default, null!));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T123")]
    [Trait("Decision", "ED-267")] // V-7.14: a Voice typing page pick stores it explicitly (auto or the code), overriding the default
    public void A_page_pick_stores_the_code_explicitly_and_overrides_the_default()
    {
        var typing = VoiceTypingSettings.Default with { Mic = "m", SlowChoice = LiveWordsChoice.Live };
        Assert.Equal(typing with { Lang = "auto" }, VoiceLanguage.WithPagePick(typing, "auto"));
        Assert.Equal(typing with { Lang = "de" }, VoiceLanguage.WithPagePick(typing, "de"));
        Assert.Equal(new LanguageResolution("de", false, false), VoiceLanguage.Resolve("de", VoiceLangMode.Ask, "en-US"));
        Assert.Throws<ArgumentException>(() => VoiceLanguage.WithPagePick(typing, "xx"));
        Assert.Throws<ArgumentException>(() => VoiceLanguage.WithPagePick(typing, null!));
    }
}
