using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.1, 8.2, 8.3): the pure rules for a memory note's text: one clean line, the key two notes are the same
// note by, the words a search looks at, the topic names, and the words of "forget everything about".
[Trait("Decision", "ED-NEW-dextra-as12-2")]
[Trait("Feature", "AS-12")]
public sealed class MemoryRulesTests
{
    [Fact]
    public void Clean_makes_one_line_and_removes_what_hides_or_reorders_text()
    {
        Assert.Equal("The owner prefers short answers.", MemoryRules.Clean("  The owner\r\nprefers\t short   answers.\n"));
        Assert.Equal("ab", MemoryRules.Clean("a\u200Bb"));
        Assert.Equal("ab", MemoryRules.Clean("a\u202Eb"));
        Assert.Equal("ab", MemoryRules.Clean("a\u2066b\u2069"));
        Assert.Equal("ab", MemoryRules.Clean("a\U000E0041b"));
        Assert.Equal("ab", MemoryRules.Clean("a\uFEFFb"));
        Assert.Equal("a b", MemoryRules.Clean("a\u0007b"));
        Assert.Equal("a b", MemoryRules.Clean("a\u2028b"));
        Assert.Equal("ab", MemoryRules.Clean("a\uD800b"));
        Assert.Equal("", MemoryRules.Clean(null));
        Assert.Equal("", MemoryRules.Clean(" \n\t "));
    }

    [Fact]
    public void Clean_removes_the_comment_markers_a_markdown_file_could_be_broken_by()
    {
        Assert.DoesNotContain("<!--", MemoryRules.Clean("x <!-- y"), StringComparison.Ordinal);
        Assert.DoesNotContain("-->", MemoryRules.Clean("x --> y"), StringComparison.Ordinal);
        Assert.Equal("x y", MemoryRules.Clean("x<!--y"));
        Assert.Equal("a b", MemoryRules.Clean("a<!---->b"));
    }

    [Fact]
    public void Clean_keeps_letters_of_every_script_and_emoji()
    {
        Assert.Equal("मुझे छोटे जवाब पसंद हैं", MemoryRules.Clean("मुझे  छोटे जवाब\nपसंद हैं"));
        Assert.Equal("مجھے مختصر جواب پسند ہیں", MemoryRules.Clean("مجھے مختصر جواب پسند ہیں"));
        Assert.Equal("Ship it 🚀", MemoryRules.Clean("Ship it 🚀"));
    }

    [Fact]
    public void The_key_ignores_case_punctuation_and_spacing_and_the_apostrophe()
    {
        Assert.Equal("the owners boss", MemoryRules.NormalKey("The  OWNER'S boss"));
        Assert.Equal(MemoryRules.NormalKey("Prefers short answers."), MemoryRules.NormalKey("prefers   SHORT answers!!"));
        Assert.NotEqual(MemoryRules.NormalKey("Prefers short answers"), MemoryRules.NormalKey("Prefers long answers"));
        Assert.Equal("", MemoryRules.NormalKey("  ...  "));
        Assert.Equal(MemoryRules.NormalKey("ｆｕｌｌｗｉｄｔｈ"), MemoryRules.NormalKey("fullwidth"));
    }

    [Fact]
    public void Tokens_drop_glue_words_fold_plurals_and_keep_other_scripts()
    {
        Assert.Equal(["prefer", "short", "answer"], MemoryRules.Tokens("The owner prefers the short answers"));
        Assert.Equal(["class", "ship", "6"], MemoryRules.Tokens("class ships 6"));
        Assert.Equal(["जवाब", "छोटे"], MemoryRules.Tokens("जवाब छोटे"));
        Assert.Empty(MemoryRules.Tokens("the and of"));
    }

    [Fact]
    public void Similarity_is_the_share_of_words_in_common()
    {
        Assert.Equal(1.0, MemoryRules.Similarity(["a1", "b1"], ["b1", "a1"]));
        Assert.Equal(0.5, MemoryRules.Similarity(["a1", "b1", "c1"], ["b1", "c1", "d1"]));
        Assert.Equal(0.0, MemoryRules.Similarity([], ["a1"]));
        Assert.Equal(0.0, MemoryRules.Similarity(["a1"], ["b1"]));
    }

    [Theory]
    [InlineData("Writing style", "writing-style")]
    [InlineData("  Build & Test Commands!  ", "build-test-commands")]
    [InlineData("owner's habits", "owners-habits")]
    [InlineData("CON", "con-notes")]
    [InlineData("memory", "memory-notes")]
    [InlineData("recent", "recent-notes")]
    [InlineData("quarantine", "quarantine-notes")]
    [InlineData("../../etc", "etc")]
    [InlineData("मुझे", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_topic_slug_is_file_safe_and_never_a_reserved_name(string? hint, string expected) => Assert.Equal(expected, MemoryRules.TopicSlug(hint));

    [Fact]
    public void A_topic_slug_is_cut_to_forty_characters_without_a_trailing_hyphen()
    {
        var slug = MemoryRules.TopicSlug(string.Join(' ', Enumerable.Repeat("word", 20)));
        Assert.True(slug.Length <= MemoryRules.MaxTopicChars);
        Assert.False(slug.EndsWith('-'));
        Assert.True(MemoryRules.IsTopic(slug));
    }

    [Fact]
    public void Topics_default_by_kind_and_the_shared_set_files_the_owner_under_about_you()
    {
        Assert.Equal("about-you", MemoryRules.DefaultTopic(MemoryScope.Shared, NoteKind.Preference));
        Assert.Equal("about-you", MemoryRules.DefaultTopic(MemoryScope.Shared, NoteKind.Fact));
        Assert.Equal("preferences", MemoryRules.DefaultTopic("dextra", NoteKind.Preference));
        Assert.Equal("facts", MemoryRules.DefaultTopic("dextra", NoteKind.Fact));
        Assert.Equal("procedures", MemoryRules.DefaultTopic("dextra", NoteKind.Procedure));
        Assert.Equal("projects", MemoryRules.DefaultTopic("dextra", NoteKind.Project));
        Assert.Equal("Writing style", MemoryRules.TopicTitle("writing-style"));
        Assert.Equal("Notes", MemoryRules.TopicTitle(""));
    }

    [Theory]
    [InlineData("Forget everything about my diet", "my diet")]
    [InlineData("please forget all about Project Atlas.", "Project Atlas")]
    [InlineData("delete everything that you know about the invoice address!", "the invoice address")]
    [InlineData("Erase anything on  travel  ", "travel")]
    [InlineData("forget my diet", null)]
    [InlineData("what do you know about my diet", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Forget_everything_about_names_its_subject(string? request, string? subject) => Assert.Equal(subject, MemoryRules.ForgetSubject(request));
}
