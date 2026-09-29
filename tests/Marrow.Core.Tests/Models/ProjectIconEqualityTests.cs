using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// Audit fix (svc): Library.swift:4-6 `enum ProjectIcon: Hashable` synthesizes equality and hashing over Swift String, which is canonical
// equivalence (R-STR-2): e followed by U+0301 and U+00E9 are one string, so two icons holding them are equal and share a hash.
public sealed class ProjectIconEqualityTests
{
    private const string Composed = "é";
    private const string Decomposed = "é";

    [Trait("Req", "PR-STORE-079")] // Core/Models/Library.swift:4-8
    [Fact]
    public void Symbol_icons_holding_canonically_equivalent_names_are_equal_and_hash_alike()
    {
        var first = new ProjectIcon.Symbol(Composed);
        var second = new ProjectIcon.Symbol(Decomposed);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<ProjectIcon> { first, second });
    }

    [Trait("Req", "PR-STORE-079")] // Core/Models/Library.swift:4-8
    [Fact]
    public void Emoji_icons_holding_canonically_equivalent_values_are_equal_and_hash_alike()
    {
        var first = new ProjectIcon.Emoji(Composed);
        var second = new ProjectIcon.Emoji(Decomposed);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Trait("Req", "PR-STORE-079")] // Core/Models/Library.swift:4-8
    [Fact]
    public void An_emoji_and_a_symbol_with_the_same_text_stay_different_cases()
    {
        Assert.NotEqual<ProjectIcon>(new ProjectIcon.Emoji("a"), new ProjectIcon.Symbol("a"));
    }
}
