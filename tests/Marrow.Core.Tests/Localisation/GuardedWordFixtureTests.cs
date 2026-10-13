using Marrow.Core.Localisation;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22 "guarded-action lists for every UI language nth ships, tested on localised fixtures"; ED-NEW-web-w22-1).
// Each language's label file (tests/fixtures/web-surfaces/guarded) is run through the shared lists: every guarded label names its word, every
// ordinary label names none, and the same word written another way (invisible characters, full-width letters, the spellings Urdu and Hindi
// pages use for one letter) is still found.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class GuardedWordFixtureTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void EveryGuardedLabelNamesItsWord(string language)
    {
        var problems = new List<string>();
        foreach (var label in GuardedFixtures.Load(language).Guarded)
        {
            var found = GuardedWords.Shared.Find(label.Name);
            if (found != label.Word) problems.Add($"{label.Name} gave {found ?? "nothing"}, wanted {label.Word}");
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void OrdinaryLabelsNameNoWord(string language)
    {
        var fixture = GuardedFixtures.Load(language);
        Assert.NotEmpty(fixture.Allowed);
        var problems = fixture.Allowed.Where(name => GuardedWords.Shared.Find(name) is not null)
            .Select(name => $"{name} gave {GuardedWords.Shared.Find(name)}").ToList();
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void TheSameWordWrittenAnotherWayIsStillFound(string language)
    {
        var fixture = GuardedFixtures.Load(language);
        Assert.NotEmpty(fixture.Variants);
        var problems = new List<string>();
        foreach (var variant in fixture.Variants)
        {
            var found = GuardedWords.Shared.Find(variant.Name);
            if (found != variant.Word) problems.Add($"{variant.What}: gave {found ?? "nothing"}, wanted {variant.Word}");
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void ALabelThatMixesLanguagesIsFoundWhicheverComesFirst(string language)
    {
        var fixture = GuardedFixtures.Load(language);
        Assert.NotEmpty(fixture.Mixed);
        foreach (var mixed in fixture.Mixed) Assert.Equal(mixed.Word, GuardedWords.Shared.Find(mixed.Name));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void EveryFixtureLanguageIsAGuardedLanguage(string language) =>
        Assert.Contains(language, UiLanguages.Guarded);

    [Fact]
    public void TheFixturesCoverEveryGuardedLanguage()
    {
        var files = Directory.EnumerateFiles(GuardedFixtures.Folder(), "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);
        Assert.Equal(UiLanguages.Guarded.Order(StringComparer.Ordinal), files);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void EveryGuardedWordHasALabelInTheFixtureOfEveryLanguage(string language)
    {
        // Each of the fourteen words (or the payment phrase that covers it) appears among the language's labels, so no word of the list goes untested.
        var fixture = GuardedFixtures.Load(language);
        var named = fixture.Guarded.Select(label => label.Word)
            .Concat(fixture.Denied.Select(denial => GuardedWords.Shared.Find(denial.Name) ?? ""))
            .Concat(fixture.Variants.Select(variant => variant.Word)).ToHashSet(StringComparer.Ordinal);
        var untested = GuardedWords.Canonical.Where(word => !named.Contains(word)).ToList();
        Assert.Empty(untested);
    }
}
