using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2; ED-NEW-dextra-as10-6): the file-name glob of a folder trigger. * is any run of characters, ?
// is one, case is ignored, and nothing else is special (a bracket or a dot is itself).
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-6")]
public sealed class FolderGlobTests
{
    [Theory]
    [InlineData("*", "anything.txt", true)]
    [InlineData("*", "", true)]
    [InlineData("*.pdf", "invoice.pdf", true)]
    [InlineData("*.pdf", "INVOICE.PDF", true)]
    [InlineData("*.pdf", "invoice.pdf.txt", false)]
    [InlineData("*.pdf", "pdf", false)]
    [InlineData("inv-???.pdf", "inv-123.pdf", true)]
    [InlineData("inv-???.pdf", "inv-12.pdf", false)]
    [InlineData("inv-???.pdf", "inv-1234.pdf", false)]
    [InlineData("*.p*f", "a.pdf", true)]
    [InlineData("*.p*f", "a.png", false)]
    [InlineData("a*b*c", "aXXbYYc", true)]
    [InlineData("a*b*c", "aXXbYY", false)]
    [InlineData("**", "x", true)]
    [InlineData("?", "", false)]
    [InlineData("[a].txt", "[a].txt", true)]
    [InlineData("[a].txt", "a.txt", false)]
    [InlineData("a.txt", "A.TXT", true)]
    public void A_name_matches_or_it_does_not(string pattern, string name, bool matches) => Assert.Equal(matches, FolderGlob.IsMatch(pattern, name));
}
