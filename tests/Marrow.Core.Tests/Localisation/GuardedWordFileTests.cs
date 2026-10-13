using Marrow.Core.Localisation;

namespace Marrow.Core.Tests.Localisation;

// PORT-ONLY: web surfaces W22 (ED-NEW-web-w22-1). A list file that is wrong is refused at load, naming the file and the word, never half read: a
// guard with a silently missing list would let a localised page through unasked.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class GuardedWordFileTests
{
    private const string Good = """
        {
          "language": "xx",
          "match": "word",
          "words": { "send": ["zend"], "sign out": ["zout", "zlogout"] },
          "identifiers": { "zend": "send" },
          "payment": ["zpay now"],
          "account": ["zsign up"],
          "captcha": ["zcaptcha"]
        }
        """;

    [Fact]
    public void AGoodFileIsRead()
    {
        var list = GuardedWordList.Parse("xx.json", Good);

        Assert.Equal("xx", list.Language);
        Assert.True(list.WholeWords);
        Assert.Equal(["zout", "zlogout"], list.Words["sign out"]);
        Assert.Equal("send", list.Identifiers["zend"]);
        Assert.Equal("send", new GuardedWords([list]).Find("Please ZEND it"));
        Assert.True(new GuardedWords([list]).IsPaymentPhrase("zpay now"));
    }

    [Theory]
    [InlineData("[]", "not an object")]
    [InlineData("{", "")]
    [InlineData("{\"match\":\"word\",\"words\":{},\"payment\":[],\"account\":[],\"captcha\":[]}", "\"language\" is missing")]
    [InlineData("{\"language\":\"xx\",\"match\":\"sometimes\",\"words\":{},\"payment\":[],\"account\":[],\"captcha\":[]}", "not \"word\" or \"text\"")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"payment\":[],\"account\":[],\"captcha\":[]}", "\"words\" is missing")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"leave\":[\"x\"]},\"payment\":[],\"account\":[],\"captcha\":[]}", "\"leave\" is not a guarded word")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"send\":[\"x\",\"x\"]},\"payment\":[],\"account\":[],\"captcha\":[]}", "listed twice")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"send\":[\"  \"]},\"payment\":[],\"account\":[],\"captcha\":[]}", "non-empty")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"send\":[\"x\"],\"post\":[\"X\"]},\"payment\":[],\"account\":[],\"captcha\":[]}", "stands for both")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"send\":[\"x\"]},\"identifiers\":{\"k\":\"leave\"},\"payment\":[],\"account\":[],\"captcha\":[]}", "does not name a guarded word")]
    [InlineData("{\"language\":\"xx\",\"match\":\"word\",\"words\":{\"send\":[\"x\"]},\"account\":[],\"captcha\":[]}", "\"payment\" is missing")]
    public void AFileThatIsWrongIsRefusedAndNamed(string json, string reason)
    {
        var error = Assert.Throws<InvalidDataException>(() => GuardedWordList.Parse("bad.json", json));

        Assert.Contains("bad.json", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoFilesForOneLanguageAreRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => GuardedWords.FromJson([("a.json", Good), ("b.json", Good)]));

        Assert.Contains("\"xx\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TextMatchingFindsAFormInsideAWordAndWholeWordMatchingDoesNot()
    {
        var text = Good.Replace("\"match\": \"word\"", "\"match\": \"text\"", StringComparison.Ordinal);

        Assert.Equal("send", new GuardedWords([GuardedWordList.Parse("t.json", text)]).Find("unzendable"));
        Assert.Null(new GuardedWords([GuardedWordList.Parse("w.json", Good)]).Find("unzendable"));
    }

    [Fact]
    public void ALongerFormWinsOverAShorterOneInsideIt()
    {
        var json = Good.Replace("\"send\": [\"zend\"]", "\"send\": [\"zend\"], \"post\": [\"zend it\"]", StringComparison.Ordinal);

        Assert.Equal("post", new GuardedWords([GuardedWordList.Parse("l.json", json)]).Find("please zend it now"));
        Assert.Equal("send", new GuardedWords([GuardedWordList.Parse("l.json", json)]).Find("please zend now"));
    }

    [Fact]
    public void CaptchaPhrasesAreFoundInsideWordsLikeAClassName()
    {
        var list = new GuardedWords([GuardedWordList.Parse("xx.json", Good)]);

        Assert.True(list.IsCaptchaPhrase("g-zcaptcha-frame"));
        Assert.False(list.IsPaymentPhrase("prezpay nowpost"));
        Assert.True(list.IsPaymentPhrase("Press zpay now!"));
    }

    [Fact]
    public void ALanguageThatComesLaterInTheGuardedListRanksAfterEnglish()
    {
        var english = Good.Replace("\"xx\"", "\"en\"", StringComparison.Ordinal).Replace("zend", "send", StringComparison.Ordinal);
        var hindi = Good.Replace("\"xx\"", "\"hi\"", StringComparison.Ordinal);

        var set = GuardedWords.FromJson([("hi.json", hindi), ("en.json", english)]);

        Assert.Equal(["en", "hi"], set.Languages);
    }
}
