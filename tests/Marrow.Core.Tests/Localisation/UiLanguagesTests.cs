using System.Globalization;
using Marrow.Core.Localisation;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22; ED-NEW-web-w22-1). Which language a culture speaks, and the one place that says which languages ship.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class UiLanguagesTests
{
    [Theory]
    [InlineData("ur-PK", "ur-PK,ur,en", "ur-PK")]
    [InlineData("ur-PK", "en,ur", "ur")]
    [InlineData("ur-IN", "en,ur-PK", "en")]
    [InlineData("hi-IN", "en,hi", "hi")]
    [InlineData("HI-in", "en,hi", "hi")]
    [InlineData("fr-FR", "en,hi,ur", "en")]
    [InlineData("en-GB", "en", "en")]
    [InlineData("", "en,hi", "en")]
    public void ACultureSpeaksItsOwnTagThenItsLanguageThenEnglish(string culture, string available, string expected) =>
        Assert.Equal(expected, UiLanguages.Resolve(CultureInfo.GetCultureInfo(culture), available.Split(',')));

    [Fact]
    public void NoCultureSpeaksEnglish() =>
        Assert.Equal("en", UiLanguages.Resolve(null, ["hi"]));

    [Fact]
    public void NthShipsEnglishAndEnglishIsTheFallback()
    {
        // ED-132: the pinned app has an English localisation only; a language is added here deliberately, with its file and its guarded list.
        Assert.Equal(["en"], UiLanguages.Shipped);
        Assert.Equal("en", UiLanguages.Fallback);
        Assert.Equal(["en", "hi", "ur"], UiLanguages.Guarded);
    }

    [Fact]
    public void EveryShippedLanguageHasAGuardedList()
    {
        Assert.Empty(GuardedWords.Shared.LanguagesWithoutList(UiLanguages.Shipped));
    }
}
