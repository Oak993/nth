using Marrow.Core.Localisation;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (ED-NEW-web-w22-1). Tactus asked its own English regex and its own Hindi and Urdu table (PcPolicy before W22); the web guard
// asked Tactus. Both now ask the shared lists. This pins every form and every answer the old table gave, so moving them cost no word.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class GuardedWordParityTests
{
    // The table PcPolicy held before the lists moved to Localisation/Guarded, form by form (the nukta form of khareeden is written with the sign).
    private static readonly (string Form, string Word)[] Before =
    [
        ("अनइंस्टॉल", "uninstall"), ("इंस्टॉल", "install"), ("साइन आउट", "sign out"), ("लॉग आउट", "sign out"),
        ("भेजें", "send"), ("भेजो", "send"), ("भेजना", "send"), ("हटाएं", "delete"), ("हटाएँ", "delete"), ("मिटाएं", "delete"),
        ("मिटाएँ", "delete"), ("डिलीट", "delete"), ("भुगतान", "pay"), ("खरीदें", "buy"), ("ऑर्डर", "order"),
        ("सबमिट", "submit"), ("प्रकाशित", "publish"), ("पोस्ट", "post"), ("स्थानांतरित", "transfer"), ("ट्रांसफर", "transfer"),
        ("ان انسٹال", "uninstall"), ("انسٹال", "install"), ("سائن آؤٹ", "sign out"), ("لاگ آؤٹ", "sign out"), ("بھیجیں", "send"),
        ("بھیجو", "send"), ("ارسال", "send"), ("حذف", "delete"), ("مٹائیں", "delete"), ("ڈیلیٹ", "delete"), ("ادائیگی", "pay"),
        ("خریدیں", "buy"), ("آرڈر", "order"), ("جمع کرائیں", "submit"), ("شائع", "publish"), ("پوسٹ", "post"), ("منتقل", "transfer"),
        ("ٹرانسفر", "transfer"),
    ];

    private static readonly string[] EnglishBefore =
        ["send", "delete", "delete account", "sign out", "pay", "buy", "purchase", "order", "submit", "publish", "post", "transfer", "install", "uninstall"];

    [Fact]
    public void EveryFormTheOldTableKnewIsStillFound()
    {
        var problems = new List<string>();
        foreach (var (form, word) in Before)
        {
            if (PcPolicy.GuardedWord(form) != word) problems.Add($"{form}: gave {PcPolicy.GuardedWord(form) ?? "nothing"}, wanted {word}");
            if (PcPolicy.GuardedWord("ईमेल " + form + " करें") != word && PcPolicy.GuardedWord(form + " now") != word) problems.Add($"{form}: not found inside a longer name");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void TheNuktaFormOfKhareedenIsStillFoundWhetherItIsOneLetterOrALetterAndASign()
    {
        Assert.Equal("buy", PcPolicy.GuardedWord(((char)0x0959).ToString() + "रीदें"));
        Assert.Equal("buy", PcPolicy.GuardedWord(((char)0x0916).ToString() + ((char)0x093C) + "रीदें"));
        Assert.Equal("transfer", PcPolicy.GuardedWord("ट्रांस" + ((char)0x095E) + "र"));
    }

    [Fact]
    public void EveryEnglishWordTheOldRegexKnewIsStillFoundWholeAndOnlyWhole()
    {
        foreach (var word in EnglishBefore)
        {
            Assert.Equal(word, PcPolicy.GuardedWord(word));
            Assert.Equal(word, PcPolicy.GuardedWord(word.ToUpperInvariant()));
            Assert.Equal(word, PcPolicy.GuardedWord("Please " + word + " it"));
            Assert.Null(PcPolicy.GuardedWord(word.Replace(' ', '_') + "ed" + "x"));
            Assert.Null(PcPolicy.GuardedWord("un" + word.Replace(' ', '_') + "z"));
        }
    }

    [Fact]
    public void AnExistingEnglishAnswerIsUnchanged()
    {
        Assert.Equal("publish", PcPolicy.GuardedWord("Publish post"));
        Assert.Equal("delete account", PcPolicy.GuardedWord("Delete account"));
        Assert.Equal("sign out", PcPolicy.GuardedWord("Sign  out"));
        Assert.Equal("uninstall", PcPolicy.GuardedWord("Uninstall"));
        Assert.Equal("install", PcPolicy.GuardedWord("Install update"));
        Assert.Null(PcPolicy.GuardedWord("Postpone"));
        Assert.Null(PcPolicy.GuardedWord("Ordered list"));
    }

    [Theory]
    [InlineData("SendButton", "send")]
    [InlineData("btnDelete", "delete")]
    [InlineData("id_pay_now", "pay")]
    [InlineData("SignOut", "sign out")]
    [InlineData("LogoutLink", "sign out")]
    [InlineData("checkout", "buy")]
    [InlineData("PostalCode", null)]
    [InlineData("OrderedList", null)]
    [InlineData("PaymentHistory", null)]
    public void IdentifiersKeepTheirTokenRules(string identifier, string? word)
    {
        Assert.Equal(word, GuardedWords.Shared.FindIdentifier(identifier));
        Assert.Equal(word, PcPolicy.GuardedWord("", identifier));
    }
}
