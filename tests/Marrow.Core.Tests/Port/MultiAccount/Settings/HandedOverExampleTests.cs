using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (step 2): "Show an example" under "What was handed over": the page's example sheet, in the page's words, for the way the next account is brought up to date.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class HandedOverExampleTests
{
    [Fact]
    public void Marrows_summary_lists_what_is_kept_and_what_is_shortened_and_where_the_whole_conversation_is()
    {
        var example = HandedOverExample.For(DecisionValues.MarrowSummary, "Claude Max 2", handshake: false);
        Assert.Equal((MultiAccountWords.HandedTitle, "to Claude Max 2"), (example.Title, example.Sub));
        Assert.Equal(MultiAccountWords.HandedSummaryIntro("Claude Max 2"), example.Intro);
        Assert.Null(example.AiNote);
        Assert.Equal(9, example.Items.Count);
        Assert.Equal(["Your messages", "Claude’s replies", "Task, plan and to-do", "Files changed and the current diff", "Commands and their results", "Approvals and web pages", "File reads", "Searches", "Long build log"], example.Items.Select(i => i.Title));
        Assert.Equal(6, example.Items.Count(i => i.Kept));
        Assert.Equal("Shortened to path and lines; it can read them again", example.Items.Single(i => i.Title == "File reads").Note);
        Assert.Equal("Kept word for word", example.Items[0].Note);
        Assert.Equal(("handoff-thread-0412.md", MultiAccountWords.HandedFileLine), (example.File, example.FileLine));
        Assert.Null(example.HandshakeLine);
    }

    [Fact]
    public void The_whole_conversation_has_one_line_and_no_list()
    {
        var example = HandedOverExample.For(DecisionValues.Full, "Claude Max 2", handshake: false);
        Assert.Equal(MultiAccountWords.HandedFullIntro, example.Intro);
        Assert.Empty(example.Items);
        Assert.Null(example.File);
    }

    [Fact]
    public void A_summary_by_ai_says_who_wrote_it_and_keeps_marrows_summary_with_it()
    {
        var example = HandedOverExample.For(DecisionValues.AiSummary, "Codex", handshake: false);
        Assert.Equal(MultiAccountWords.HandedAiNote, example.AiNote);
        Assert.Equal(MultiAccountWords.HandedSummaryIntro("Codex"), example.Intro);
        Assert.Equal(9, example.Items.Count);
    }

    [Fact]
    public void With_the_handshake_on_the_sub_line_and_a_last_line_say_so()
    {
        var example = HandedOverExample.For(DecisionValues.MarrowSummary, "Claude Max 2", handshake: true);
        Assert.Equal("to Claude Max 2 · with a Handshake", example.Sub);
        Assert.Equal(MultiAccountWords.HandedHandshakeLine("Claude Max 2"), example.HandshakeLine);
    }

    [Fact]
    public void An_unset_choice_shows_the_page_default_marrows_summary()
    {
        Assert.Equal(MultiAccountWords.HandedSummaryIntro("x"), HandedOverExample.For(null, "x", false).Intro);
    }
}
