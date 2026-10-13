using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "Spoken status ... two or three sentences", 3.3 "three spoken sentences"): text made fit to be said and shown beside the voice.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-6")]
public sealed class SpokenTextTests
{
    [Fact]
    public void Markdown_is_taken_out_and_links_are_named()
    {
        var spoken = SpokenText.Clean("## Result\n\n**Done.** See [the report](https://example.com/r.pdf) and https://example.com/x for `details`.\n- one\n- two\n1. three");
        Assert.DoesNotContain("#", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("*", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("`", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("https", spoken, StringComparison.Ordinal);
        Assert.Contains("the report", spoken, StringComparison.Ordinal);
        Assert.Contains("a link", spoken, StringComparison.Ordinal);
        Assert.Contains("details", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_block_is_not_read_aloud_and_the_reply_says_where_it_is()
    {
        var spoken = SpokenText.ForSpeech("Here is the fix.\n```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\nIt should work now.");
        Assert.DoesNotContain("var x", spoken, StringComparison.Ordinal);
        Assert.Equal("Here is the fix. It should work now. " + SpokenText.RestOnScreen, spoken);
        Assert.Equal("That is code. It is on screen.", SpokenText.ForSpeech("```\nx\n```"));
    }

    [Fact]
    public void A_long_reply_is_cut_at_a_sentence_and_ends_with_where_the_rest_is()
    {
        var reply = string.Join(' ', Enumerable.Range(1, 12).Select(i => $"Sentence number {i} says something useful."));
        var spoken = SpokenText.ForSpeech(reply);
        Assert.StartsWith("Sentence number 1 says something useful.", spoken, StringComparison.Ordinal);
        Assert.EndsWith(SpokenText.RestOnScreen, spoken, StringComparison.Ordinal);
        Assert.Equal(SpokenText.ReplySentences + 1, SpokenText.CountSentences(spoken));
    }

    [Fact]
    public void A_short_reply_is_said_whole_and_a_single_endless_sentence_is_cut_at_a_word()
    {
        Assert.Equal("Yes. I will start it now.", SpokenText.ForSpeech("Yes. I will start it now."));
        var spoken = SpokenText.ForSpeech(string.Join(' ', Enumerable.Repeat("word", 400)));
        Assert.True(spoken.Length < SpokenText.ReplyCharacters + SpokenText.RestOnScreen.Length + 3);
        Assert.EndsWith(SpokenText.RestOnScreen, spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void Sentences_split_after_a_stop_and_a_capital_and_not_inside_a_number_or_a_name()
    {
        Assert.Equal(["It cost 3.5 dollars.", "Is that fine?", "Yes!"], SpokenText.Sentences("It cost 3.5 dollars. Is that fine? Yes!"));
        Assert.Equal(["Version 2.3 is out and e.g. this works."], SpokenText.Sentences("Version 2.3 is out and e.g. this works."));
        Assert.Empty(SpokenText.Sentences("   "));
    }

    [Fact]
    public void A_phrase_has_no_sentence_marks_of_its_own_and_is_cut_at_a_word()
    {
        Assert.Equal("Fix v2.3 login timeout", SpokenText.Phrase("Fix v2.3 login timeout!!"));
        Assert.Equal("Why lenses fog up and what to do", SpokenText.Phrase("\"Why lenses fog up?\" (and what to do.)"));
        Assert.DoesNotContain("\n", SpokenText.Phrase("a\nb"), StringComparison.Ordinal);
        var cut = SpokenText.Phrase(string.Join(' ', Enumerable.Repeat("longword", 40)), 30);
        Assert.True(cut.Length <= 30);
        Assert.False(cut.EndsWith(' '));
    }

    [Fact]
    public void Words_list_where_each_word_starts_so_the_sheet_can_light_the_spoken_one()
    {
        var words = SpokenText.Words("Two missions are running.");
        Assert.Equal([new SpokenWord(0, 3), new SpokenWord(4, 8), new SpokenWord(13, 3), new SpokenWord(17, 8)], words);
        Assert.Empty(SpokenText.Words(""));
    }

    [Fact]
    public void First_returns_the_leading_sentences_and_says_whether_it_cut()
    {
        Assert.Equal("One. Two.", SpokenText.First("One. Two. Three.", 2, out var cut));
        Assert.True(cut);
        Assert.Equal("One.", SpokenText.First("One.", 2, out cut));
        Assert.False(cut);
    }
}
