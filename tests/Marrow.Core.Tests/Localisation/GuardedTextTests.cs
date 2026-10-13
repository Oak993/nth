using Marrow.Core.Localisation;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (ED-NEW-web-w22-1). The fold a name goes through before a list reads it: what it makes equal, and what it keeps apart.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class GuardedTextTests
{
    private static string Chars(params int[] codes) => string.Concat(codes.Select(code => ((char)code).ToString()));

    [Fact]
    public void BlankAndNullFoldToNothing()
    {
        Assert.Equal("", GuardedText.Fold(null));
        Assert.Equal("", GuardedText.Fold(""));
        Assert.Equal("", GuardedText.Fold("  \t\r\n  "));
    }

    [Fact]
    public void WhiteSpaceBecomesOneSpaceAndCaseGoes()
    {
        Assert.Equal("sign out now", GuardedText.Fold("  SIGN \t\n Out" + Chars(0xA0) + " now "));
    }

    [Fact]
    public void InvisibleCharactersVanishInsideAWordSoAPageCannotHideOneThere()
    {
        // zero-width space, zero-width non-joiner and joiner, soft hyphen, the byte order mark, a word joiner, direction marks
        var hidden = Chars(0x200B, 0x200C, 0x200D, 0x00AD, 0xFEFF, 0x2060, 0x200E, 0x200F, 0x061C);
        Assert.Equal("send", GuardedText.Fold("S" + hidden + "e" + hidden + "n" + hidden + "d"));
    }

    [Fact]
    public void AZeroWidthSpaceCanBeTakenAsASpaceInTheSecondFold()
    {
        var z = Chars(0x200B);
        Assert.Equal("deleteaccount", GuardedText.Fold("Delete" + z + "account"));
        Assert.Equal("delete account", GuardedText.FoldSeparated("Delete" + z + "account"));
        Assert.Equal("s end", GuardedText.FoldSeparated("S" + z + "end"));
    }

    [Fact]
    public void FullWidthLettersAndLigaturesBecomeThePlainOnes()
    {
        Assert.Equal("send", GuardedText.Fold(Chars(0xFF33, 0xFF45, 0xFF4E, 0xFF44)));
        Assert.Equal("official", GuardedText.Fold("o" + Chars(0xFB00) + "icial"));
    }

    [Fact]
    public void ArabicAndUrduLettersWrittenInMoreThanOneWayFoldToOne()
    {
        Assert.Equal(GuardedText.Fold("بھیجیں"), GuardedText.Fold("بھ" + Chars(0x064A) + "جیں"));
        Assert.Equal(GuardedText.Fold("بھیجیں"), GuardedText.Fold("بھ" + Chars(0x0649) + "ج" + Chars(0x064A) + "ں"));
        Assert.Equal(GuardedText.Fold("کھاتہ"), GuardedText.Fold(Chars(0x0643) + "ھات" + Chars(0x0647)));
    }

    [Fact]
    public void TatweelAndVowelMarksOnAnArabicWordAreDropped()
    {
        Assert.Equal(GuardedText.Fold("حذف"), GuardedText.Fold("حـذف"));
        Assert.Equal(GuardedText.Fold("حذف"), GuardedText.Fold("ح" + Chars(0x064E) + "ذ" + Chars(0x0652) + "ف"));
    }

    [Fact]
    public void ArabicPresentationFormsBecomeTheBaseLetters()
    {
        Assert.Equal(GuardedText.Fold("حذف"), GuardedText.Fold(Chars(0xFEA3, 0xFEAC, 0xFED1)));
    }

    [Fact]
    public void TheNuktaAndTheCandrabinduFoldInHindi()
    {
        Assert.Equal(GuardedText.Fold("ट्रांसफर"), GuardedText.Fold("ट्रांस" + Chars(0x095E) + "र"));
        Assert.Equal(GuardedText.Fold("ट्रांसफर"), GuardedText.Fold("ट्रांस" + Chars(0x092B, 0x093C) + "र"));
        Assert.Equal(GuardedText.Fold("हटाएं"), GuardedText.Fold("हटाए" + Chars(0x0901)));
    }

    [Fact]
    public void LettersThatAreDifferentStayDifferent()
    {
        // the do-chashmi he (bhe) is not the plain he; a Latin word and a similar-looking Hindi word stay apart
        Assert.NotEqual(GuardedText.Fold("بھیجیں"), GuardedText.Fold("بہیجیں"));
        Assert.NotEqual(GuardedText.Fold("हटाएं"), GuardedText.Fold("हटाएगा"));
    }

    [Fact]
    public void IllFormedTextIsFoldedAsItIsAndNeverThrows()
    {
        Assert.Equal("a" + (char)0xD800 + "b", GuardedText.Fold("A" + (char)0xD800 + "B"));
    }

    [Fact]
    public void FoldingTwiceChangesNothing()
    {
        foreach (var text in new[] { "Send", "Ｓｅｎｄ", "भेजें", "بھیجیں", "حـذف", "a" + Chars(0xA0) + "b" })
            Assert.Equal(GuardedText.Fold(text), GuardedText.Fold(GuardedText.Fold(text)));
    }
}
