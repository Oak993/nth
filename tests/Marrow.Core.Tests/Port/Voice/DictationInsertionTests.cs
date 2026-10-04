using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-8.1, V-8.6): where dictated words go. Offsets are UTF-16 (what a TextBox reports). Expected texts are written out from the
// clause: the caret (a selection is replaced when the first word arrives), a space before the first word when the preceding character is not
// whitespace and the word does not begin with punctuation, a space after the last word when the following character is not whitespace or
// punctuation, the caret after the inserted text, no words = the draft untouched.
public sealed class DictationInsertionTests
{
    private static IReadOnlyList<TranscriptWord> Words(params string[] texts) => texts.Select(t => new TranscriptWord(t, false)).ToList();

    private static DraftSelection Caret(string text, int at) => new(text, at, 0);

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: into an empty draft the words go in as they are and the caret ends after them
    public void Words_go_into_an_empty_draft_with_the_caret_after_them()
    {
        var result = new DictationInsertion(Caret("", 0)).Apply(Words("Hello", "world."));
        Assert.Equal(new DraftSelection("Hello world.", 12, 0), result);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: no words, the draft is untouched, selection included
    public void With_no_words_the_draft_and_its_selection_are_untouched()
    {
        var original = new DraftSelection("keep this", 5, 4);
        Assert.Equal(original, new DictationInsertion(original).Apply([]));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: a space before the first word when the preceding character is not whitespace
    public void A_space_goes_before_the_first_word_after_a_non_space_character()
    {
        var result = new DictationInsertion(Caret("hello", 5)).Apply(Words("there"));
        Assert.Equal(new DraftSelection("hello there", 11, 0), result);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: no space before the first word after whitespace (space, newline, tab)
    [InlineData("hello ", "hello there")]
    [InlineData("line one\n", "line one\nthere")]
    [InlineData("col\t", "col\tthere")]
    public void No_space_goes_before_the_first_word_after_whitespace(string draft, string expected)
    {
        var result = new DictationInsertion(Caret(draft, draft.Length)).Apply(Words("there"));
        Assert.Equal(new DraftSelection(expected, expected.Length, 0), result);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: no space before a first word that begins with punctuation
    [InlineData(",")]
    [InlineData(".")]
    [InlineData("?")]
    [InlineData("\U00002026")] // U+2026 HORIZONTAL ELLIPSIS
    public void No_space_goes_before_a_first_word_that_begins_with_punctuation(string word)
    {
        var result = new DictationInsertion(Caret("hello", 5)).Apply(Words(word));
        Assert.Equal(new DraftSelection("hello" + word, 5 + word.Length, 0), result);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: a space after the last word when the following character is not whitespace or punctuation
    public void A_space_goes_after_the_last_word_before_a_word_and_the_caret_ends_after_the_inserted_text()
    {
        var result = new DictationInsertion(Caret("hello world", 6)).Apply(Words("big"));
        Assert.Equal(new DraftSelection("hello big world", 10, 0), result);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: no space after the last word before whitespace or punctuation
    [InlineData("a b", 1, "a x b")] // the following character is a space already: space before only
    [InlineData("a.", 1, "a x.")] // the following character is punctuation
    [InlineData("a\nb", 1, "a x\nb")] // newline is whitespace
    public void No_space_goes_after_the_last_word_before_whitespace_or_punctuation(string draft, int at, string expected)
    {
        var result = new DictationInsertion(Caret(draft, at)).Apply(Words("x"));
        Assert.Equal(expected, result.Text);
        Assert.Equal(at + " x".Length, result.Start);
        Assert.Equal(0, result.Length);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: a selection is replaced when the first word arrives (one edit); the spacing looks at the characters around it
    public void A_selection_is_replaced_by_the_words_and_the_spacing_reads_what_surrounds_it()
    {
        var result = new DictationInsertion(new DraftSelection("hello brave world", 6, 5)).Apply(Words("new"));
        Assert.Equal(new DraftSelection("hello new world", 9, 0), result);
        // The rule is read literally: "(" is not whitespace, so a space goes before the word; ")" is punctuation, so none goes after.
        var glued = new DictationInsertion(new DraftSelection("(brave)", 1, 5)).Apply(Words("new"));
        Assert.Equal(new DraftSelection("( new)", 5, 0), glued);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.2: live updates rewrite the inserted run from the original draft, so a growing or revised list never stacks
    public void Live_updates_are_computed_from_the_original_draft_each_time()
    {
        var insertion = new DictationInsertion(Caret("hello world", 6));
        Assert.Equal("hello a world", insertion.Apply(Words("a")).Text);
        Assert.Equal("hello a b world", insertion.Apply(Words("a", "b")).Text);
        var revised = insertion.Apply(Words("A", "b", "c"));
        Assert.Equal("hello A b c world", revised.Text);
        Assert.Equal(12, revised.Start); // "hello " (6) + "A b c" (5) + the space before "world" (1)
        Assert.Equal("hello world", insertion.Apply([]).Text);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.6: cancel returns the draft to its pre-session text and selection
    public void Cancel_restores_the_text_and_the_selection()
    {
        var original = new DraftSelection("hello brave world", 6, 5);
        var insertion = new DictationInsertion(original);
        insertion.Apply(Words("a", "b"));
        Assert.Equal(original, insertion.Restore());
        Assert.Equal(original, insertion.Original);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1: a character outside the BMP before the caret is one character, not a surrogate half
    public void A_surrogate_pair_before_the_caret_counts_as_a_character_in_one_piece()
    {
        var emoji = "\U0001F600"; // U+1F600 GRINNING FACE, two UTF-16 units
        var result = new DictationInsertion(Caret(emoji, 2)).Apply(Words("ok"));
        Assert.Equal(new DraftSelection(emoji + " ok", 5, 0), result);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T31")]
    [Trait("Decision", "ED-267")] // V-8.1 (ED-292 (5)): a caret or selection outside the text is clamped into it
    public void A_selection_outside_the_text_is_clamped()
    {
        var result = new DictationInsertion(new DraftSelection("abc", 9, 4)).Apply(Words("x"));
        Assert.Equal(new DraftSelection("abc x", 5, 0), result);
        var negative = new DictationInsertion(new DraftSelection("abc", -3, 1)).Apply(Words("x"));
        Assert.Equal("x abc", negative.Text);
    }
}
