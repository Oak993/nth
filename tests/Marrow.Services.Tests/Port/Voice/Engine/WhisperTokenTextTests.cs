using System.Text;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// ED-NEW-voicefix-2 (release 2026-10-11: "������○� करो।"): Whisper's tokens are byte pieces and Whisper.net decodes each by itself, so a character split across tokens arrives
// as U+FFFD in each. The tokens are rebuilt from the segment's whole text, for the live and the final passes alike (both go through WhisperNetModel).
public sealed class WhisperTokenTextTests
{
    public static TheoryData<string> Sentences => new()
    {
        " ज़िंदगी में कुछ अच्छा करना है तो कुछ बड़ा करो।",
        " زندگی میں کچھ اچھا کرنا ہے تو کچھ بڑا کرو۔",
        " हाँ भाई, क्या चल रहा है आजकल?",
        " ok 👍🏽 done 😀 👩‍👩‍👧 फिर",
        " Haan bhai, کیا حال ہے? 🙂",
    };

    // what Whisper.net hands over: each token's bytes decoded on their own (U+FFFD for a broken piece), a word's first token starting with its space
    private static List<BackendToken> Tokens(string text, Random random, double start = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var tokens = new List<BackendToken>();
        var at = 0;
        while (at < bytes.Length)
        {
            var length = Math.Min(bytes.Length - at, random.Next(1, 5));
            var space = Array.IndexOf(bytes, (byte)' ', at + 1, length - 1);
            if (space > at) length = space - at; // a space begins the next token, as in Whisper's vocabulary
            tokens.Add(new BackendToken(Encoding.UTF8.GetString(bytes, at, length), start, start + 0.02, false));
            start += 0.02;
            at += length;
        }
        return tokens;
    }

    [Theory]
    [MemberData(nameof(Sentences))]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-2")]
    public void Characters_split_across_tokens_come_back_whole(string sentence)
    {
        var random = new Random(7);
        var sawBroken = false;
        for (var trial = 0; trial < 300; trial++)
        {
            var tokens = Tokens(sentence, random);
            sawBroken |= tokens.Any(t => t.Text.Contains('�', StringComparison.Ordinal));
            var repaired = WhisperTokenText.Repair(tokens, sentence);
            Assert.Equal(tokens.Count, repaired.Count);
            var joined = string.Concat(repaired.Select(t => t.Text));
            Assert.Equal(sentence, joined);
            Assert.DoesNotContain(repaired, t => t.Text.Contains('�', StringComparison.Ordinal));
            // the words the composer gets are the spoken ones
            var words = WordAssembler.Assemble([new BackendSegment(repaired, "hi")]).Select(w => w.Text).ToArray();
            Assert.Equal(sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries), words);
        }
        Assert.True(sawBroken, "the splits broke characters");
    }

    [Theory]
    [InlineData("क")]
    [InlineData("ک")]
    [InlineData("😀")]
    [InlineData("👍🏽")]
    [InlineData("करो۔")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-2")]
    public void Every_byte_split_of_a_word_comes_back_whole(string word)
    {
        var text = " " + word;
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            BackendToken[] tokens =
            [
                new("[_BEG_]", 0, 0, true),
                new(Encoding.UTF8.GetString(bytes, 0, cut), 0.1, 0.2, false),
                new(Encoding.UTF8.GetString(bytes, cut, bytes.Length - cut), 0.2, 0.3, false),
                new("[_TT_50]", 0.3, 0.3, true),
            ];
            var repaired = WhisperTokenText.Repair(tokens, text);
            Assert.Equal(text, repaired[1].Text + repaired[2].Text);
            Assert.True(repaired[0].IsSpecial && repaired[3].IsSpecial);
            var words = WordAssembler.Assemble([new BackendSegment(repaired, null)]);
            var only = Assert.Single(words);
            Assert.Equal(word, only.Text);
            Assert.Equal(cut == 1 ? 0.2 : 0.1, only.Start, 6); // the word keeps the time of the token its first character starts in (a lone space token starts none)
        }
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-2")]
    public void Whole_tokens_are_left_as_they_are_and_nothing_broken_is_ever_shown()
    {
        BackendToken[] whole = [new(" hello", 0, 0.1, false), new(" दुनिया", 0.1, 0.2, false)];
        Assert.Same(whole, WhisperTokenText.Repair(whole, " hello दुनिया"));

        // no segment text to mend from, or one that does not match: the broken pieces are dropped, the whole ones kept
        BackendToken[] broken = [new(" a�", 0, 0.1, false), new("��b", 0.1, 0.2, false)];
        foreach (var text in new string?[] { null, "", " xyz" })
        {
            var repaired = WhisperTokenText.Repair(broken, text);
            Assert.Equal(" a", repaired[0].Text);
            Assert.Equal("b", repaired[1].Text);
        }
    }
}
