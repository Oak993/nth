using Marrow.Core.Localisation;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (DESIGN.md 14 W22; ED-NEW-web-w22-1). The gate on the lists themselves: a list exists for every language nth ships
// and for Hindi and Urdu, each has every guarded word and the three hard-deny lists, and a language added without a list is refused.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class GuardedWordListsTests
{
    [Fact]
    public void ThereIsAListForEveryLanguageNthShipsAndEnglishIsFirst()
    {
        var shared = GuardedWords.Shared;
        Assert.Empty(shared.LanguagesWithoutList(UiLanguages.Shipped));
        Assert.Equal(UiLanguages.Guarded, shared.Languages);
        Assert.Equal("en", shared.Languages[0]);
        Assert.All(UiLanguages.Shipped, language => Assert.Contains(language, UiLanguages.Guarded));
    }

    [Fact]
    public void HindiAndUrduAreKeptBecauseTheOwnersPagesAreWrittenInThem()
    {
        Assert.Contains("hi", GuardedWords.Shared.Languages);
        Assert.Contains("ur", GuardedWords.Shared.Languages);
    }

    [Fact]
    public void ALanguageShippedWithoutAListIsNamed()
    {
        Assert.Equal(["fr"], GuardedWords.Shared.LanguagesWithoutList(["en", "fr", "hi"]));
    }

    [Fact]
    public void EveryListGivesEveryGuardedWordAtLeastOneForm()
    {
        foreach (var list in GuardedWords.Shared.Lists)
        {
            foreach (var word in GuardedWords.Canonical)
            {
                Assert.True(list.Words.TryGetValue(word, out var forms) && forms.Count > 0, $"{list.Language} has no form of \"{word}\"");
            }
            Assert.Equal(GuardedWords.Canonical.Count, list.Words.Count);
        }
    }

    [Fact]
    public void EveryListHasPaymentAccountAndCaptchaPhrases()
    {
        foreach (var list in GuardedWords.Shared.Lists)
        {
            Assert.NotEmpty(list.Payment);
            Assert.NotEmpty(list.Account);
            Assert.NotEmpty(list.Captcha);
        }
    }

    [Fact]
    public void EnglishKeepsTheIdentifierTokensTheOtherListsDoNot()
    {
        var english = GuardedWords.Shared.Lists[0];
        Assert.Equal("sign out", english.Identifiers["signout"]);
        Assert.Equal("buy", english.Identifiers["checkout"]);
        Assert.All(GuardedWords.Shared.Lists, list => Assert.All(list.Identifiers.Values, word => Assert.Contains(word, GuardedWords.Canonical)));
    }

    [Fact]
    public void AllWordsOfAListAreFoundInTheirOwnNames()
    {
        // Every listed form, written alone, names the word it is listed under: no form is shadowed by another word's.
        var problems = new List<string>();
        foreach (var list in GuardedWords.Shared.Lists)
        {
            foreach (var (word, forms) in list.Words)
            {
                foreach (var form in forms)
                {
                    var found = GuardedWords.Shared.Find(form);
                    if (found != word) problems.Add($"{list.Language}: \"{form}\" is {found ?? "not found"}, listed as {word}");
                }
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryPhraseOfTheThreeHardDeniesIsFound()
    {
        var problems = new List<string>();
        foreach (var list in GuardedWords.Shared.Lists)
        {
            problems.AddRange(list.Payment.Where(phrase => !GuardedWords.Shared.IsPaymentPhrase(phrase)).Select(phrase => $"{list.Language} payment \"{phrase}\""));
            problems.AddRange(list.Account.Where(phrase => !GuardedWords.Shared.IsAccountPhrase(phrase)).Select(phrase => $"{list.Language} account \"{phrase}\""));
            problems.AddRange(list.Captcha.Where(phrase => !GuardedWords.Shared.IsCaptchaPhrase(phrase)).Select(phrase => $"{list.Language} captcha \"{phrase}\""));
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void AnEmptyOrBlankNameNamesNothing()
    {
        Assert.Null(GuardedWords.Shared.Find(null));
        Assert.Null(GuardedWords.Shared.Find(""));
        Assert.Null(GuardedWords.Shared.Find("   \t"));
        Assert.Null(GuardedWords.Shared.FindIdentifier(null));
        Assert.False(GuardedWords.Shared.IsPaymentPhrase(null));
        Assert.False(GuardedWords.Shared.IsAccountPhrase(""));
        Assert.False(GuardedWords.Shared.IsCaptchaPhrase(" "));
    }

    [Fact]
    public void ALoneSurrogateInANameDoesNotThrow()
    {
        // string.Normalize throws on text that is not well-formed; a page can put such text in a name.
        var broken = "Send " + (char)0xD800 + " now";
        Assert.Equal("send", GuardedWords.Shared.Find(broken));
        Assert.Null(GuardedWords.Shared.Find("Open " + (char)0xDC00));
    }

    [Fact]
    public void AVeryLongNameIsReadInOnePassAndWithoutHanging()
    {
        var long1 = new string('a', 400_000) + " Delete account " + new string('b', 400_000);
        Assert.Equal("delete account", GuardedWords.Shared.Find(long1));
        Assert.Null(GuardedWords.Shared.Find(new string('x', 800_000)));
    }
}
