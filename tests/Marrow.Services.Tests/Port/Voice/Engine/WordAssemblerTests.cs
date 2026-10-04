using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.2: words from segments. A token beginning with a space starts a word; a word's start and end are its first and last token's timestamps; punctuation
// stays attached; scripts written without spaces yield one word per token run.
public sealed class WordAssemblerTests
{
    private static BackendToken T(string text, double start, double end, bool special = false) => new(text, start, end, special);

    private static BackendSegment Seg(params BackendToken[] tokens) => new(tokens, null);

    private static HypothesisWord[] Words(params BackendSegment[] segments) => [.. WordAssembler.Assemble(segments)];

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_token_with_a_leading_space_starts_a_word_and_the_word_runs_from_its_first_token_to_its_last()
    {
        var words = Words(Seg(T(" Hel", 0.10, 0.25), T("lo", 0.25, 0.40), T(" world", 0.50, 0.90)));
        Assert.Equal([new HypothesisWord("Hello", 0.10, 0.40), new HypothesisWord("world", 0.50, 0.90)], words);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Punctuation_stays_attached_to_the_word_before_it()
    {
        var words = Words(Seg(T(" Yes", 0.0, 0.3), T(",", 0.3, 0.35), T(" it", 0.5, 0.6), T(" is", 0.6, 0.8), T(".", 0.8, 0.85)));
        Assert.Equal([new HypothesisWord("Yes,", 0.0, 0.35), new HypothesisWord("it", 0.5, 0.6), new HypothesisWord("is.", 0.6, 0.85)], words);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_first_token_without_a_space_still_opens_a_word()
    {
        var words = Words(Seg(T("Hi", 0.0, 0.2), T(" there", 0.3, 0.6)));
        Assert.Equal(["Hi", "there"], words.Select(w => w.Text));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_script_without_spaces_gives_one_word_per_token_run_and_a_new_segment_starts_a_new_run()
    {
        var words = Words(
            Seg(T("今日", 0.0, 0.3), T("は", 0.3, 0.4), T("晴れ", 0.4, 0.8)),
            Seg(T("です", 1.0, 1.2)));
        Assert.Equal([new HypothesisWord("今日は晴れ", 0.0, 0.8), new HypothesisWord("です", 1.0, 1.2)], words);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Special_tokens_are_not_part_of_any_word_and_do_not_split_one()
    {
        var words = Words(Seg(T("[_BEG_]", 0.0, 0.0, special: true), T(" he", 0.1, 0.2), T("[_TT_50]", 0.2, 0.2, special: true), T("llo", 0.2, 0.4), T("<|endoftext|>", 0.4, 0.4, special: true)));
        Assert.Equal([new HypothesisWord("hello", 0.1, 0.4)], words);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Space_only_tokens_make_no_word_and_an_empty_answer_has_no_words()
    {
        Assert.Empty(Words(Seg(T(" ", 0.0, 0.1), T("  ", 0.1, 0.2))));
        Assert.Empty(Words());
        Assert.Empty(Words(Seg()));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_word_never_ends_before_it_starts()
    {
        var word = Assert.Single(Words(Seg(T(" odd", 1.0, 0.9))));
        Assert.Equal(1.0, word.Start);
        Assert.Equal(1.0, word.End);
    }
}
