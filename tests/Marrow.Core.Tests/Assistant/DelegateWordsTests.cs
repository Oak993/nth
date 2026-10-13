using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-22 (DESIGN.md rev 5, 4.3 "labelled 'answered by ⟨head⟩ for you' in the chat", 4.2 the card's rule line, 6 "Answered for you: 2 questions in ..."; ED-NEW-dextra-as22-1):
// every word the delegate shows. The label names the head, Dextra when none is named; each reason has its own why and rule line; the correction an Override
// sends names the question and the answer; nothing runs past a line.
[Trait("Feature", "AS-22")]
[Trait("Decision", "ED-NEW-dextra-as22-1")]
public sealed class DelegateWordsTests
{
    [Fact]
    public void The_label_is_answered_by_the_head_for_you_and_dextra_when_none_is_named()
    {
        Assert.Equal("Answered by Claude for you", DelegateWords.AnsweredBy("Claude"));
        Assert.Equal("Answered by Dextra for you", DelegateWords.AnsweredBy(""));
        Assert.Equal("Answered by Dextra for you", DelegateWords.AnsweredBy("   "));
        Assert.Equal("Answered by Codex for you: keep the existing chart style", DelegateWords.AnswerNote("Codex", "keep the existing chart style"));
        Assert.Equal("Answered by Dextra for you: allowed git status. Runs alone.", DelegateWords.ApprovalNote("Dextra", "git status", "Runs alone."));
    }

    [Fact]
    public void Every_reason_has_its_own_why_and_its_own_rule_line_and_none_is_empty()
    {
        var whys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reason in Enum.GetValues<DelegateReason>())
        {
            var why = DelegateWords.Reason(reason);
            Assert.False(string.IsNullOrWhiteSpace(why), reason.ToString());
            Assert.EndsWith(".", why, StringComparison.Ordinal);
            Assert.True(whys.Add(why), $"{reason} repeats another reason's words");
            Assert.False(string.IsNullOrWhiteSpace(DelegateWords.RuleLine(reason)), reason.ToString());
        }
        Assert.Equal("Always confirmed: sending", DelegateWords.RuleLine(DelegateReason.Outward));
        Assert.Equal("Your rules ask you first.", DelegateWords.Reason(DelegateReason.Rules));
        Assert.Equal("Always confirmed: sending.", DelegateWords.Reason(DelegateReason.Rules, "Always confirmed: sending"));
        Assert.Equal("Dextra left this to you: Money is always yours to decide.", DelegateWords.LeftNote("Dextra", DelegateReason.Money));
    }

    [Fact]
    public void The_correction_names_the_question_and_the_answer_and_carries_the_owners_words_whole()
    {
        var text = DelegateWords.Correction("Which chart style?", "Keep the existing chart style", "  Use lines for quarterly reports.  ");
        Assert.StartsWith("A correction from the owner.", text, StringComparison.Ordinal);
        Assert.Contains("“Keep the existing chart style”", text, StringComparison.Ordinal);
        Assert.Contains("“Which chart style?”", text, StringComparison.Ordinal);
        Assert.Contains("The owner says that was not right: Use lines for quarterly reports.", text, StringComparison.Ordinal);
        Assert.EndsWith("Go by this from now on.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_is_folded_to_one_row_and_cut_with_an_ellipsis_at_its_length()
    {
        Assert.Equal("one two three", DelegateWords.Clip("one\r\ntwo\nthree"));
        Assert.Equal("", DelegateWords.Clip(null));
        var cut = DelegateWords.Clip(new string('x', 500));
        Assert.Equal(DelegateWords.ShownChars + 1, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.Equal(11, DelegateWords.Clip(new string('y', 50), 10).Length);
    }

    [Fact]
    public void The_digest_and_the_menu_say_what_the_design_says()
    {
        Assert.Equal("2 questions in “Pitch deck v3”", DelegateWords.DigestItem("Pitch deck v3", 2));
        Assert.Equal("1 question in “Thesis”", DelegateWords.DigestItem("Thesis", 1));
        Assert.Equal("Answered for you:", DelegateWords.DigestHead);
        Assert.Equal("(each with Override)", DelegateWords.DigestNote);
        Assert.Equal("Answer questions for me", DelegateWords.MenuTitle(on: false));
        Assert.Equal("Stop answering for me", DelegateWords.MenuTitle(on: true));
        Assert.Equal("Override", DelegateWords.OverrideButton);
        Assert.Equal("an approval was answered for you", DelegateWords.FeedLead(approval: true));
        Assert.Equal("a question was answered for you", DelegateWords.FeedLead(approval: false));
    }
}
