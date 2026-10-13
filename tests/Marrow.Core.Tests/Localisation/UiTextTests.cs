using System.Globalization;
using Marrow.Core.Localisation;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22; ED-NEW-web-w22-1). How a word is asked for: the language, the fallback to English, a key nobody has,
// the holes, the plural forms, and the test scope that keeps parallel tests apart.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class UiTextTests
{
    private static UiCatalogue Catalogue() => UiCatalogue.FromFiles(
    [
        new("t.en.json", "en", """
            {
              "t.hello": "Hello",
              "t.only_english": "Only English",
              "t.tabs.one": "{0} tab",
              "t.tabs.other": "{0} tabs",
              "t.files.other": "{0} files in {1}",
              "t.price": "Price {0:N2}",
              "t.broken": "Broken {0} and {1"
            }
            """),
        new("t.hi.json", "hi", """
            {
              "t.hello": "नमस्ते",
              "t.tabs.one": "{0} टैब",
              "t.tabs.other": "{0} टैब",
              "t.files.other": "{1} में {0} फ़ाइलें"
            }
            """),
        new("t.ur.json", "ur", """
            {
              "t.hello": "ہیلو",
              "t.tabs.other": "{0} ٹیب"
            }
            """),
    ]);

    [Fact]
    public void AWordIsReadInTheChosenLanguage()
    {
        using var _ = UiText.Use(Catalogue(), "hi");

        Assert.Equal("नमस्ते", UiText.Get("t.hello"));
        Assert.Equal("hi", UiText.Language);
    }

    [Fact]
    public void AWordTheLanguageLacksIsReadInEnglish()
    {
        using var _ = UiText.Use(Catalogue(), "hi");

        Assert.Equal("Only English", UiText.Get("t.only_english"));
    }

    [Fact]
    public void AKeyNobodyHasReadsAsItselfAndIsRemembered()
    {
        using var _ = UiText.Use(Catalogue(), "en");

        Assert.Equal("t.nobody", UiText.Get("t.nobody"));
        Assert.Contains("t.nobody", UiText.MissingKeys);
    }

    [Fact]
    public void HolesAreFilledTheWayAnInterpolatedStringWouldFillThem()
    {
        using var _ = UiText.Use(Catalogue(), "en");

        Assert.Equal("3 files in docs", UiText.Format("t.files.other", 3, "docs"));
        using var us = CultureScope.Of("en-US");
        Assert.Equal("Price 1,234.50", UiText.Format("t.price", 1234.5));
    }

    [Fact]
    public void ACultureCanBeNamedForANumberThatMustReadTheSameEverywhere()
    {
        using var _ = UiText.Use(Catalogue(), "en");
        using var german = CultureScope.Of("de-DE");

        Assert.Equal("Price 1.234,50", UiText.Format("t.price", 1234.5));
        Assert.Equal("Price 1,234.50", UiText.FormatWith(CultureInfo.InvariantCulture, "t.price", 1234.5));
    }

    [Fact]
    public void ATranslationWithABrokenHoleStillShowsSomething()
    {
        using var _ = UiText.Use(Catalogue(), "en");

        Assert.Equal("Broken {0} and {1", UiText.Format("t.broken", 1, 2));
        Assert.Contains("t.broken#format", UiText.MissingKeys);
    }

    [Theory]
    [InlineData("en", 1, "1 tab")]
    [InlineData("en", 0, "0 tabs")]
    [InlineData("en", 2, "2 tabs")]
    [InlineData("en", 11, "11 tabs")]
    [InlineData("hi", 0, "0 टैब")]
    [InlineData("hi", 1, "1 टैब")]
    [InlineData("hi", 5, "5 टैब")]
    public void ACountPicksItsForm(string language, long count, string expected)
    {
        using var _ = UiText.Use(Catalogue(), language);

        Assert.Equal(expected, UiText.Plural("t.tabs", count));
    }

    [Fact]
    public void ALanguageWithOnlyTheOtherFormUsesItForOne()
    {
        using var _ = UiText.Use(Catalogue(), "ur");

        Assert.Equal("1 ٹیب", UiText.Plural("t.tabs", 1));
        Assert.Equal("4 ٹیب", UiText.Plural("t.tabs", 4));
    }

    [Fact]
    public void ATranslationMayPutTheHolesInAnotherOrder()
    {
        using var _ = UiText.Use(Catalogue(), "hi");

        Assert.Equal("docs में 7 फ़ाइलें", UiText.Plural("t.files", 7, "docs"));
    }

    [Fact]
    public void AnEnglishFormIsUsedWhenTheLanguageHasNeither()
    {
        using var _ = UiText.Use(Catalogue(), "ur");

        Assert.Equal("7 files in docs", UiText.Plural("t.files", 7, "docs"));
    }

    [Fact]
    public void ThePluralFormsFollowTheLanguagesRule()
    {
        Assert.Equal("one", UiPlural.Category("en", 1));
        Assert.Equal("other", UiPlural.Category("en", 0));
        Assert.Equal("one", UiPlural.Category("hi", 0));
        Assert.Equal("one", UiPlural.Category("hi", 1));
        Assert.Equal("other", UiPlural.Category("hi", 2));
        Assert.Equal("one", UiPlural.Category("ur", 1));
        Assert.Equal("other", UiPlural.Category("ur", 0));
        Assert.Equal("one", UiPlural.Category("xx", 1));
    }

    [Fact]
    public void WithoutAScopeALanguageNthDoesNotShipIsSpokenInEnglish()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hi-IN");
            Assert.Equal("en", UiText.Language);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("en", UiText.Language);
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public async Task AScopeBelongsToTheTestThatMadeItAndNotToItsNeighbour()
    {
        using var scope = UiText.Use(Catalogue(), "hi");
        var inside = UiText.Get("t.hello");

        var neighbour = await Task.Run(() =>
        {
            using var other = UiText.Use(Catalogue(), "ur");
            return UiText.Get("t.hello");
        });

        Assert.Equal("नमस्ते", inside);
        Assert.Equal("ہیلو", neighbour);
        Assert.Equal("नमस्ते", UiText.Get("t.hello"));
    }

    [Fact]
    public void LeavingAScopeRestoresTheOneBefore()
    {
        using var outer = UiText.Use(Catalogue(), "hi");
        using (UiText.Use(Catalogue(), "ur")) Assert.Equal("ur", UiText.Language);

        Assert.Equal("hi", UiText.Language);
    }
}
