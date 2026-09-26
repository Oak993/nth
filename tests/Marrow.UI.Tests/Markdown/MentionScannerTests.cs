using System.Collections.Generic;
using Marrow.UI.Markdown;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MentionScanner.swift (B3 52bc792), :1-33. New at B3 (N10): the
// word-bounded, longest-first scan shared by LinkParagraphView.swift:197 and MarkdownView.swift:419/:425.
public sealed class MentionScannerTests
{
    private static IReadOnlyList<(int LowerBound, int UpperBound, string Value)> Matches(string text, IReadOnlyList<string> candidates) =>
        MentionScanner.Matches(text, candidates, name => name);

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:8
    public void Longer_name_wins_over_a_shorter_prefix_at_the_same_position()
    {
        var matches = Matches("Hank 2 is here", ["Hank", "Hank 2"]);
        var only = Assert.Single(matches);
        Assert.Equal((0, 6, "Hank 2"), only);
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:17-18, :19-20
    public void A_name_inside_a_longer_word_does_not_match()
    {
        Assert.Empty(Matches("Hankering for coffee", ["Hank"]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:19-20 afterOK: !c.isLetter && !c.isNumber
    public void A_name_beside_a_digit_does_not_match()
    {
        Assert.Empty(Matches("Hank2 is here", ["Hank"]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:17-18 beforeOK
    public void A_name_preceded_by_a_letter_does_not_match()
    {
        Assert.Empty(Matches("xHank is here", ["Hank"]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:11 i == text.startIndex
    public void A_name_at_the_very_start_of_the_text_matches()
    {
        var matches = Matches("Hank is here", ["Hank"]);
        var only = Assert.Single(matches);
        Assert.Equal((0, 4, "Hank"), only);
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:16 after == text.endIndex
    public void A_name_at_the_very_end_of_the_text_matches()
    {
        var matches = Matches("ping Hank", ["Hank"]);
        var only = Assert.Single(matches);
        Assert.Equal((5, 9, "Hank"), only);
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:23-26 i = end after a hit
    public void Two_distinct_mentions_are_both_found_in_order()
    {
        var matches = Matches("Hank and Grace are here", ["Hank", "Grace"]);
        Assert.Collection(matches,
            first => Assert.Equal((0, 4, "Hank"), first),
            second => Assert.Equal((9, 14, "Grace"), second));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:27-29 i = text.index(after: i)
    public void No_candidates_produce_no_matches()
    {
        Assert.Empty(Matches("nothing to see here", []));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")]
    public void Empty_text_produces_no_matches()
    {
        Assert.Empty(Matches("", ["Hank"]));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // an empty candidate name is skipped: the Swift loop never advances past one (b3_note, PR-THEME-143; see MentionScanner.cs's own citation)
    public void An_empty_candidate_name_is_skipped_and_never_hangs()
    {
        var matches = Matches("Hank is here", ["", "Hank"]);
        var only = Assert.Single(matches);
        Assert.Equal((0, 4, "Hank"), only);
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/MentionScanner.swift:7 name: (Value) -> String
    public void The_name_projection_is_used_rather_than_the_candidate_itself()
    {
        var candidates = new[] { (Id: 1, Name: "Hank"), (Id: 2, Name: "Grace") };
        var matches = MentionScanner.Matches("Ping Hank now", candidates, c => c.Name);
        var only = Assert.Single(matches);
        Assert.Equal(1, only.Value.Id);
        Assert.Equal((5, 9), (only.LowerBound, only.UpperBound));
    }
}
