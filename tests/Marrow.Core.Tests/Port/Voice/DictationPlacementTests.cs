using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Tests.Port.Voice;

// VT-11 (voice-SPEC V-8.1, V-8.3): where each dictated word sits in the draft the insertion rules make, so the dictation layer can draw the words at their places and the text after the
// insertion still. DictationInsertion.Place is Apply with the places: the same text and caret, and for each word the index of its first character, and where the text after the insertion
// begins. The expected places are counted out by hand, not read from Apply.
public sealed class DictationPlacementTests
{
    private static TranscriptWord W(string text, bool tentative = false) => new(text, tentative);

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // an empty draft: the words start at 0, one space apart, the after text is empty
    public void Into_an_empty_draft_the_words_start_at_zero_a_space_apart()
    {
        var place = new DictationInsertion(new DraftSelection("", 0, 0)).Place([W("switch"), W("the"), W("theme")]);
        Assert.Equal("switch the theme", place.Text);
        Assert.Equal([0, 7, 11], place.WordStarts);
        Assert.Equal(16, place.AfterStart);
        Assert.Equal(16, place.Caret);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-8.1: a space before the first word when the character before is not whitespace; one after the last when the next is not whitespace or punctuation
    public void A_caret_inside_text_gets_the_spaces_of_V_8_1_and_the_places_count_them()
    {
        var draft = new DraftSelection("hello world", 5, 0); // between "hello" and " world": whitespace follows, none before the words' space
        var place = new DictationInsertion(draft).Place([W("big"), W("loud")]);
        Assert.Equal("hello big loud world", place.Text);
        Assert.Equal([6, 10], place.WordStarts);
        Assert.Equal("big", place.Text.Substring(6, 3));
        Assert.Equal("loud", place.Text.Substring(10, 4));
        Assert.Equal(14, place.AfterStart); // " world" begins here (the space that was already there)
        Assert.Equal(14, place.Caret);

        var tight = new DictationInsertion(new DraftSelection("ab", 1, 0)).Place([W("x")]);
        Assert.Equal("a x b", tight.Text);
        Assert.Equal([2], tight.WordStarts);
        Assert.Equal(4, tight.Caret);
        Assert.Equal(4, tight.AfterStart); // the space after the word belongs to the insertion; "b" starts at 4
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-8.1: a selection is replaced; the text after the selection is the after text
    public void A_selection_is_replaced_and_what_follows_it_is_the_after_text()
    {
        var draft = new DraftSelection("keep OLD tail", 5, 3);
        var place = new DictationInsertion(draft).Place([W("new")]);
        Assert.Equal("keep new tail", place.Text);
        Assert.Equal([5], place.WordStarts);
        Assert.Equal(8, place.AfterStart);
        Assert.Equal(" tail", place.Text[place.AfterStart..]);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // V-8.1: a word that begins with punctuation takes no space before it
    public void A_word_that_begins_with_punctuation_takes_no_space_before_it()
    {
        var place = new DictationInsertion(new DraftSelection("hello", 5, 0)).Place([W(", and")]);
        Assert.Equal("hello, and", place.Text);
        Assert.Equal([5], place.WordStarts);
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // with no words the draft is untouched: no places, the selection is not replaced
    public void With_no_words_the_draft_is_untouched()
    {
        var draft = new DraftSelection("keep OLD tail", 5, 3);
        var place = new DictationInsertion(draft).Place([]);
        Assert.Equal("keep OLD tail", place.Text);
        Assert.Empty(place.WordStarts);
        Assert.Equal(8, place.AfterStart);
        Assert.Equal(5, place.Caret);
    }

    [Theory]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // Place agrees with Apply on the text and the caret, whatever the draft and words (the layer and the landing must write the same text)
    [InlineData("", 0, 0)]
    [InlineData("abc", 3, 0)]
    [InlineData("abc", 0, 0)]
    [InlineData("a b c", 2, 1)]
    [InlineData("a b c", 99, 4)]
    [InlineData("x. y", 2, 0)]
    public void Place_agrees_with_Apply(string text, int start, int length)
    {
        var words = new[] { W("one"), W("two", true), W(".") };
        var insertion = new DictationInsertion(new DraftSelection(text, start, length));
        var applied = insertion.Apply(words);
        var place = insertion.Place(words);
        Assert.Equal(applied.Text, place.Text);
        Assert.Equal(applied.Start, place.Caret);
        for (var k = 0; k < words.Length; k++) Assert.Equal(words[k].Text, place.Text.Substring(place.WordStarts[k], words[k].Text.Length));
    }

    [Fact]
    [Trait("Feature", "VT-11")]
    [Trait("Decision", "ED-267")] // a draft with a rune outside the BMP and RTL words: places are UTF-16 indices, as the editor's text is
    public void Places_are_utf16_indices_for_surrogate_pairs_and_rtl_words()
    {
        var place = new DictationInsertion(new DraftSelection("\U0001F600", 2, 0)).Place([W("یہ"), W("ایک")]);
        Assert.Equal("\U0001F600 یہ ایک", place.Text);
        Assert.Equal([3, 6], place.WordStarts);
        Assert.Equal(place.Text.Length, place.AfterStart);
    }
}
