using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class TextCleanupUnicodeTests
{
    [Theory] // Swift SE-0363 custom ranges match whole, single-scalar NFC characters.
    [Trait("Req", "PR-STORE-107")]
    [InlineData("\u001B[31m\u0301X", "\u001B[31m\u0301X")]
    [InlineData("\u001B[3\u03011mX", "\u001B[3\u03011mX")]
    [InlineData("\u001B[\u030131mX", "\u001B[\u030131mX")]
    [InlineData("\u001B[1\u037E2mX", "X")]
    [InlineData("\u001B[\u212AX", "X")]
    public void ANSI_pattern_retains_Swift_grapheme_boundaries_and_canonical_equivalence(string input, string expected) =>
        Assert.Equal(expected, TextCleanup.StripANSI(input));
}
