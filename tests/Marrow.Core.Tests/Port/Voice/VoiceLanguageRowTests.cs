using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-13 (voice-SPEC V-16.3.1, V-T130, V-T92; OD-0097 language default, OD-0099 Q-V-17): what the Voice typing page's Language row shows for a stored pick or for none.
public sealed class VoiceLanguageRowTests
{
    private const string Dot = "\u00b7";

    [Theory]
    [InlineData("auto", "Whisper works out the language; pick one if Hindi and Urdu get mixed up")]
    [InlineData("hi", "Written in Devanagari")]
    [InlineData("ur", "Written in Urdu script, right to left")]
    [InlineData("en", "Every dictation is read as English")]
    [InlineData("vi", "Every dictation is read as Vietnamese")]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T92")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the detail per language (Auto-detect's is the Q-V-17 line, ED-267 (6))
    public void Each_language_has_the_details_of_V_16_3_1(string code, string detail) => Assert.Equal(detail, VoiceLanguageRow.DetailFor(code));

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 A: nothing picked, language default A: the button says Auto-detect, the check is on Auto-detect, the detail is Auto-detect's
    public void Nothing_picked_under_language_default_A_shows_Auto_detect()
    {
        var row = VoiceLanguageRow.For(null, VoiceLangMode.Auto, "en-US");
        Assert.Equal(new VoiceLanguageRowState("Auto-detect", "auto", VoiceLanguage.AutoDetail), row);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 B: the middle dot is a settings value's text, the pill's rule does not reach it
    public void Nothing_picked_under_language_default_B_says_it_is_shown_while_you_speak()
    {
        var row = VoiceLanguageRow.For(null, VoiceLangMode.Show, "en-US");
        Assert.Equal("Auto-detect " + Dot + " shown while you speak", row.ButtonText);
        Assert.Equal("auto", row.CheckedCode);
        Assert.Equal(VoiceLanguage.AutoDetail, row.Detail);
    }

    [Theory]
    [InlineData("hi-IN", "Hindi (from Windows)", "hi", "Written in Devanagari")]
    [InlineData("ur-PK", "Urdu (from Windows)", "ur", "Written in Urdu script, right to left")]
    [InlineData("en-GB", "English (from Windows)", "en", "Every dictation is read as English")]
    [InlineData("de", "German (from Windows)", "de", "Every dictation is read as German")]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 C: the Windows language's item is checked and its detail shows
    public void Nothing_picked_under_language_default_C_names_the_Windows_language(string windows, string text, string code, string detail)
    {
        var row = VoiceLanguageRow.For(null, VoiceLangMode.Windows, windows);
        Assert.Equal(new VoiceLanguageRowState(text, code, detail), row);
    }

    [Theory]
    [InlineData("sw-KE")]
    [InlineData("")]
    [InlineData(null)]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 C: a Windows language outside the list reads Auto-detect, with the check on Auto-detect
    public void Language_default_C_with_a_Windows_language_outside_the_list_falls_back_to_Auto_detect(string? windows)
    {
        var row = VoiceLanguageRow.For(null, VoiceLangMode.Windows, windows);
        Assert.Equal(new VoiceLanguageRowState("Auto-detect", "auto", VoiceLanguage.AutoDetail), row);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 D: no item is checked; the detail is the first-click line
    public void Nothing_picked_under_language_default_D_is_asked_on_first_use_with_no_check()
    {
        var row = VoiceLanguageRow.For(null, VoiceLangMode.Ask, "en-US");
        Assert.Equal(new VoiceLanguageRowState("Asked on first use", null, "You will be asked the first time you click the orb"), row);
    }

    [Theory]
    [InlineData(VoiceLangMode.Auto)]
    [InlineData(VoiceLangMode.Show)]
    [InlineData(VoiceLangMode.Windows)]
    [InlineData(VoiceLangMode.Ask)]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1: an explicit pick overrides every language default
    public void An_explicit_pick_shows_that_language_whatever_the_default(VoiceLangMode mode)
    {
        Assert.Equal(new VoiceLanguageRowState("Urdu", "ur", "Written in Urdu script, right to left"), VoiceLanguageRow.For("ur", mode, "hi-IN"));
        Assert.Equal(new VoiceLanguageRowState("Auto-detect", "auto", VoiceLanguage.AutoDetail), VoiceLanguageRow.For("auto", mode, "hi-IN"));
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.2's lenient read: a code outside the list is "nothing picked"
    public void A_code_outside_the_list_reads_as_nothing_picked() =>
        Assert.Equal(VoiceLanguageRow.For(null, VoiceLangMode.Ask, null), VoiceLanguageRow.For("sw", VoiceLangMode.Ask, null));

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T92")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the menu's items are the list, in the design's order, Auto-detect first
    public void The_menu_items_are_the_language_list_in_order()
    {
        var items = VoiceLanguageRow.MenuItems;
        Assert.Equal(VoiceLanguage.Languages.Select(l => (l.Code, l.Name)), items);
        Assert.Equal("Auto-detect", items[0].Text);
    }
}
