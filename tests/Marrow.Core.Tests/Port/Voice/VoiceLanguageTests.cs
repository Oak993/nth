using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0092, OD-0097; ED-267 (6) Q-V-17): the language list and the Q-V-17 line.
public sealed class VoiceLanguageTests
{
    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T11")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.2: the languages in the design's order with their codes
    public void The_language_list_is_the_designs_order_names_and_codes_of_V_16_2()
    {
        var expected = new[]
        {
            ("Auto-detect", "auto"), ("English", "en"), ("Hindi", "hi"), ("Urdu", "ur"), ("Arabic", "ar"), ("Bengali", "bn"),
            ("Chinese", "zh"), ("French", "fr"), ("German", "de"), ("Indonesian", "id"), ("Italian", "it"), ("Japanese", "ja"),
            ("Korean", "ko"), ("Persian", "fa"), ("Portuguese", "pt"), ("Punjabi", "pa"), ("Russian", "ru"), ("Spanish", "es"),
            ("Tamil", "ta"), ("Turkish", "tr"), ("Vietnamese", "vi"),
        };
        Assert.Equal(expected, VoiceLanguage.Languages.Select(l => (l.Name, l.Code)).ToArray());
        Assert.Equal(21, VoiceLanguage.Languages.Count);
        Assert.Equal("auto", VoiceLanguage.AutoCode);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T11")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-16.2: lookup by code; Whisper codes only, "auto" is the list's first item
    public void A_language_is_found_by_its_exact_code_only()
    {
        Assert.Equal("Urdu", VoiceLanguage.Find("ur")!.Name);
        Assert.Equal("Auto-detect", VoiceLanguage.Find("auto")!.Name);
        Assert.Null(VoiceLanguage.Find("UR"));
        Assert.Null(VoiceLanguage.Find("xx"));
        Assert.Null(VoiceLanguage.Find(null));
        Assert.Null(VoiceLanguage.Find(""));
        Assert.True(VoiceLanguage.IsKnownCode("hi"));
        Assert.False(VoiceLanguage.IsKnownCode("auto"));
        Assert.False(VoiceLanguage.IsKnownCode(null));
        Assert.False(VoiceLanguage.IsKnownCode("sw"));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T11")]
    [Trait("Decision", "ED-267")] // ED-267 (6) / OD-0099 Q-V-17: the Auto-detect line, byte for byte
    public void The_Auto_detect_line_is_the_Q_V_17_text() =>
        Assert.Equal("Whisper works out the language; pick one if Hindi and Urdu get mixed up", VoiceLanguage.AutoDetail);
}
