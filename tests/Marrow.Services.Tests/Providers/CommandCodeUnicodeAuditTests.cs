using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeAPI.swift (B3 52bc792), :194-221.
public sealed class CommandCodeUnicodeAuditTests
{
    [Theory]
    [InlineData("\U000000DCnicode")] // U+00DC LATIN CAPITAL LETTER U WITH DIAERESIS
    [InlineData("\U000000E9clair")] // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    [InlineData("A\U00000301b")] // U+0301 COMBINING ACUTE ACCENT
    [InlineData("\U0001F600")] // U+1F600 GRINNING FACE
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:204-205,214-218
    public void Non_ASCII_first_Characters_parse_without_losing_the_catalog(string header)
    {
        var options = CommandCodeAPI.ParseModels(header + "\nclaude-opus-4  description", null, null);
        Assert.Equal(header + " \U000000B7 description", Assert.Single(options).Detail); // U+00B7 MIDDLE DOT
    }

    [Theory]
    [InlineData("\U000000DC", true)] // U+00DC LATIN CAPITAL LETTER U WITH DIAERESIS
    [InlineData("\U000000E9", false)] // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    [InlineData("A\U00000301", true)] // U+0301 COMBINING ACUTE ACCENT
    [InlineData("a\U00000301", false)] // U+0301 COMBINING ACUTE ACCENT
    [InlineData("\U0001F600", false)] // U+1F600 GRINNING FACE
    [InlineData("\U0001D400", true)] // U+1D400 MATHEMATICAL BOLD CAPITAL A: Uppercase property without a lowercase mapping
    [InlineData("\U00002160", true)] // U+2160 ROMAN NUMERAL ONE: Uppercase property outside Lu category
    [InlineData("\U000001C5", false)] // U+01C5 LATIN CAPITAL LETTER D WITH SMALL LETTER Z WITH CARON (titlecase)
    [InlineData("\U000000AA", true)] // U+00AA: Cased, unchanged by both mappings (single-scalar isCased).
    [InlineData("\U0001D44E", true)] // U+1D44E MATHEMATICAL ITALIC SMALL A: same isCased fast path.
    [InlineData("\r\n", false)]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:204; Swift stdlib CharacterProperties.swift:223-265
    public void Uppercase_helper_uses_scalar_Uppercase_or_cluster_case_changes(string character, bool expected)
    {
        var method = typeof(Marrow.Core.Port.SwiftCharacter).GetMethod("IsUppercase");
        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method.Invoke(null, [character])!);
    }

    [Theory]
    [InlineData("\U000000DC", true)] // U+00DC LATIN CAPITAL LETTER U WITH DIAERESIS
    [InlineData("A\U00000301", true)] // U+0301 COMBINING ACUTE ACCENT
    [InlineData("\U000000E9", false)] // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    [InlineData("\U000000AA", true)] // Single-scalar isCased, even though both mappings leave it unchanged.
    [InlineData("\U0001D44E", true)] // Supplementary single scalar must take the same path.
    [InlineData("\U0001F600", false)] // U+1F600 GRINNING FACE
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:204-205 (<24 fallback must not mask the uppercase branch)
    public void Long_headers_only_replace_the_section_when_the_first_Character_is_uppercase(string first, bool replaces)
    {
        var header = first + new string('x', 30);
        var options = CommandCodeAPI.ParseModels("Previous\n" + header + "\nclaude-opus-4  description", null, null);
        Assert.Equal((replaces ? header : "Previous") + " \U000000B7 description", Assert.Single(options).Detail); // U+00B7 MIDDLE DOT
    }
}
