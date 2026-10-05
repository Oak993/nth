using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-13 (voice-SPEC V-7.14 rule 3 C, V-16.3.1): Windows' display language for the Language row and the session start, read through GetUserDefaultUILanguage and mapped through CultureInfo.
public sealed class WindowsDisplayLanguageTests
{
    [Theory]
    [InlineData((ushort)0x0409, "en-US")]
    [InlineData((ushort)0x0439, "hi-IN")]
    [InlineData((ushort)0x0420, "ur-PK")]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-7.14: the language id maps through CultureInfo(lcid); the list lookup takes its two-letter part
    public void A_language_id_maps_to_its_culture_name(ushort id, string name) => Assert.Equal(name, WindowsDisplayLanguage.NameOf(id));

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // an id the runtime does not know reads as no language (the row falls back to Auto-detect)
    public void An_unknown_language_id_reads_as_null() => Assert.Null(WindowsDisplayLanguage.NameOf(0xFFFF));

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // a test replaces the reader for its own flow; the default is the system's
    public void A_test_replaces_the_reader_for_its_own_flow()
    {
        using (WindowsDisplayLanguage.Source.Override(() => "hi-IN")) Assert.Equal("hi-IN", WindowsDisplayLanguage.Current());
        using (WindowsDisplayLanguage.Source.Override(() => throw new InvalidOperationException("no reader"))) Assert.Null(WindowsDisplayLanguage.Current());
    }
}
