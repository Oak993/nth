using Marrow.Core.Models;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: final security gate N4 (ED-NEW-secfinal-40; DESIGN.md web surfaces 6.5 "nth.send(text) puts text in the main composer as a
// draft. The user sends."). The helper behind an artifact's draft: what opens a message like a command, how the draft joins, and how the
// card shows the start of the text.
[Trait("Decision", "ED-NEW-secfinal-40")]
public sealed class ArtifactDraftTextTests
{
    private const char ZeroWidthSpace = (char)0x200B;
    private const char RightToLeftOverride = (char)0x202E;
    private const char Ellipsis = (char)0x2026;

    [Theory]
    [InlineData("/compact", true)]
    [InlineData("$deploy", true)]
    [InlineData("  /plan", true)]
    [InlineData("\n\t/plan", true)]
    [InlineData("a/b", false)]
    [InlineData("Plan: /compact", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void A_text_opens_like_a_command_when_its_first_visible_character_is_a_slash_or_a_dollar(string text, bool expected) =>
        Assert.Equal(expected, ArtifactDraftText.OpensACommand(text));

    [Fact]
    public void A_zero_width_character_ahead_of_the_mark_does_not_hide_it_and_what_the_composer_runs_is_never_the_text()
    {
        Assert.True(ArtifactDraftText.OpensACommand(ZeroWidthSpace + "/plan"));
        foreach (var text in new[] { "/plan", ZeroWidthSpace + "$skill now", "\n/compact" })
        {
            var draft = ArtifactDraftText.Join("", text);
            Assert.Equal("From the artifact: " + text, draft);
            Assert.Null(SlashCommand.NameIn(draft));
        }
    }

    [Fact]
    public void Text_that_does_not_open_a_command_joins_as_it_always_did()
    {
        Assert.Equal("Plan: Team, 12 seats", ArtifactDraftText.Join("", "Plan: Team, 12 seats"));
        Assert.Equal("Plan: Team, 12 seats", ArtifactDraftText.Join("  \n ", "Plan: Team, 12 seats"));
        Assert.Equal("Please check this\n\nPlan: Team", ArtifactDraftText.Join("Please check this", "Plan: Team"));
        Assert.Equal("Please check this\n\nPlan: Team", ArtifactDraftText.Join("Please check this \n\n", "Plan: Team"));
        Assert.Equal("Please check this\n\n/compact", ArtifactDraftText.Join("Please check this", "/compact"));
        Assert.Equal("/plan\n\nand this", ArtifactDraftText.Join("/plan", "and this"));
    }

    [Fact]
    public void The_card_preview_cuts_long_text_keeps_a_few_lines_and_writes_out_what_could_hide_or_reorder()
    {
        Assert.Equal("Plan: Team", ArtifactDraftText.Preview("Plan: Team"));
        Assert.Equal("a\nb", ArtifactDraftText.Preview("a\r\nb"));

        var many = string.Join("\n", Enumerable.Range(1, 20).Select(i => "line " + i));
        Assert.Equal(string.Join("\n", Enumerable.Range(1, ArtifactDraftText.PreviewLines).Select(i => "line " + i)) + Ellipsis, ArtifactDraftText.Preview(many));

        var long300 = new string('x', ArtifactDraftText.PreviewCharacters + 50);
        Assert.Equal(new string('x', ArtifactDraftText.PreviewCharacters) + Ellipsis, ArtifactDraftText.Preview(long300));

        var shown = ArtifactDraftText.Preview("ok" + RightToLeftOverride + "evil" + ZeroWidthSpace + "\tend");
        Assert.Equal("ok\\u202Eevil\\u200B\\u0009end", shown);
        Assert.DoesNotContain(shown, c => c is RightToLeftOverride or ZeroWidthSpace or '\t');

        // A cut never leaves half of a surrogate pair.
        var emoji = new string('x', ArtifactDraftText.PreviewCharacters - 1) + char.ConvertFromUtf32(0x1F600) + "tail";
        var cut = ArtifactDraftText.Preview(emoji);
        Assert.Equal(Ellipsis, cut[^1]);
        Assert.DoesNotContain(cut, char.IsSurrogate);
    }
}
