using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-12 (voice-SPEC V-12.10): the name the pill shows for a detected language: the Voice typing page's list name for a code in the list, else Whisper's own English name with an initial capital.
public sealed class VoiceLanguageNamesTests
{
    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T126")]
    [Trait("Decision", "ED-292")]
    [InlineData("en", "English")]
    [InlineData("hi", "Hindi")]
    [InlineData("ur", "Urdu")]
    [InlineData("zh", "Chinese")]
    [InlineData("mr", "Marathi")] // outside the list: Whisper's name, capitalised (V-12.10's own example)
    [InlineData("ht", "Haitian Creole")]
    [InlineData("haw", "Hawaiian")]
    [InlineData("EN", "English")]
    public void A_code_gives_its_name(string code, string name) => Assert.Equal(name, VoiceLanguageNames.NameOf(code));

    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T126")]
    [Trait("Decision", "ED-292")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_code_gives_no_name(string? code) => Assert.Null(VoiceLanguageNames.NameOf(code));

    [Fact]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T126")]
    [Trait("Decision", "ED-292")]
    public void An_unknown_code_is_shown_as_it_is() => Assert.Equal("xx", VoiceLanguageNames.NameOf("xx"));
}
