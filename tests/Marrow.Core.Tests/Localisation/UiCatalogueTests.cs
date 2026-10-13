using Marrow.Core.Localisation;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22 "every new string in the resource files"; ED-NEW-web-w22-1). The resource files as a table: read, merged,
// refused when wrong, and compared with English.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class UiCatalogueTests
{
    private static UiCatalogueFile File(string area, string language, string json) => new($"{area}.{language}.json", language, json);

    [Fact]
    public void FilesOfOneLanguageMergeAndLanguagesStayApart()
    {
        var catalogue = UiCatalogue.FromFiles(
        [
            File("a", "en", "{\"a.one\":\"One\"}"),
            File("b", "en", "{\"b.two\":\"Two\"}"),
            File("a", "hi", "{\"a.one\":\"Ek\"}"),
        ]);

        Assert.Equal(["en", "hi"], catalogue.Languages.Order(StringComparer.Ordinal));
        Assert.Equal(2, catalogue.Entries("en").Count);
        Assert.True(catalogue.TryGet("hi", "a.one", out var ek));
        Assert.Equal("Ek", ek);
        Assert.False(catalogue.TryGet("hi", "b.two", out _));
        Assert.Empty(catalogue.Entries("fr"));
    }

    [Theory]
    [InlineData("[]", "not an object")]
    [InlineData("{\"a.x\":1}", "not a string")]
    [InlineData("{\"a.x\":\"1\",\"a.x\":\"2\"}", "twice")]
    [InlineData("{\"x\":\"no area\"}", "area.name")]
    [InlineData("{\"A.x\":\"capital\"}", "area.name")]
    [InlineData("{\"a..x\":\"gap\"}", "area.name")]
    [InlineData("{\"a.x y\":\"space\"}", "area.name")]
    [InlineData("{\"a.x\":\"1\"", "")]
    public void AFileThatIsWrongIsRefusedAndNamed(string json, string reason)
    {
        var error = Assert.Throws<InvalidDataException>(() => UiCatalogue.FromFiles([File("bad", "en", json)]));

        Assert.Contains("bad.en.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameKeyInTwoFilesOfOneLanguageIsRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => UiCatalogue.FromFiles(
            [File("a", "en", "{\"a.x\":\"1\"}"), File("b", "en", "{\"a.x\":\"2\"}")]));

        Assert.Contains("already in a.en.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameKeyInTwoLanguagesIsTheTranslationAndIsAllowed()
    {
        var catalogue = UiCatalogue.FromFiles([File("a", "en", "{\"a.x\":\"1\"}"), File("a", "ur", "{\"a.x\":\"2\"}")]);

        Assert.Equal(2, catalogue.Languages.Count);
    }

    [Fact]
    public void AnEmptyFileIsAnEmptyLanguage()
    {
        Assert.Empty(UiCatalogue.FromFiles([File("a", "en", "{}")]).Entries("en"));
    }

    [Fact]
    public void TheShippedFilesLoadAndEnglishIsThere()
    {
        var shipped = UiCatalogue.Embedded;

        Assert.Contains("en", shipped.Languages);
        Assert.NotEmpty(shipped.Entries("en"));
    }

    [Fact]
    public void EveryShippedLanguageIsCompleteNextToEnglishAndNoOtherLanguageShips()
    {
        var shipped = UiCatalogue.Embedded;

        Assert.All(UiLanguages.Shipped, language => Assert.Contains(language, shipped.Languages));
        Assert.All(shipped.Languages, language => Assert.Contains(language, UiLanguages.Shipped));
        Assert.All(UiLanguages.Shipped, language => Assert.Empty(shipped.Problems(language)));
    }

    [Fact]
    public void ATranslationThatLacksAKeyAddsOneOrChangesTheHolesIsFlagged()
    {
        var catalogue = UiCatalogue.FromFiles(
        [
            File("a", "en", "{\"a.one\":\"One\",\"a.two\":\"{0} of {1}\",\"a.three\":\"Three\"}"),
            File("a", "hi", "{\"a.one\":\"Ek\",\"a.two\":\"{0}\",\"a.extra\":\"Extra\"}"),
        ]);

        var problems = catalogue.Problems("hi");

        Assert.Contains("hi lacks a.three", problems);
        Assert.Contains("hi a.two has other holes than English", problems);
        Assert.Contains("hi has a.extra, which English does not", problems);
        Assert.Equal(3, problems.Count);
        Assert.Empty(catalogue.Problems("en"));
    }

    [Fact]
    public void HolesAreTheNumberedOnesWithOrWithoutAFormatAndNotEscapedBraces()
    {
        Assert.Equal([0, 1], UiCatalogue.Holes("{1} before {0}, {0:N0} again"));
        Assert.Equal([2], UiCatalogue.Holes("{2,5} wide"));
        Assert.Empty(UiCatalogue.Holes("a {{0}} brace and {name}"));
        Assert.Empty(UiCatalogue.Holes("Nothing here"));
    }
}
