using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: final security gate N4 (ED-NEW-secfinal-40; DESIGN.md web surfaces 6.5 "nth.send(text) puts text in the main composer as a
// draft. The user sends."). ThreadRuntime sends a message that opens with "/" or "$" as a command (SlashCommand.NameIn), so an
// artifact's text that would open the owner's message that way is led by "From the artifact: " and can never run as one. A draft that
// already has words keeps its own opening, and ordinary text joins exactly as before.
[Trait("Decision", "ED-NEW-secfinal-40")]
public sealed class ThreadRuntimeArtifactDraftTests
{
    private const char ZeroWidthSpace = (char)0x200B;

    [Theory]
    [InlineData("/compact")]
    [InlineData("/plan now")]
    [InlineData("$deploy prod")]
    [InlineData("  \n /plan")]
    [InlineData("\t/plan")]
    public void Text_that_would_open_the_message_as_a_command_is_led_by_the_artifacts_name_and_runs_nothing(string text)
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.DraftFromArtifact(text);
            var message = FoundationText.Trim(rig.Runtime.Draft.Text, includeNewlines: true);
            Assert.Null(SlashCommand.NameIn(message));
            Assert.StartsWith("From the artifact: ", message, StringComparison.Ordinal);
            Assert.EndsWith(text.Trim(), message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_zero_width_character_ahead_of_the_slash_does_not_hide_it()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.DraftFromArtifact(ZeroWidthSpace + "/plan");
            Assert.Null(SlashCommand.NameIn(FoundationText.Trim(rig.Runtime.Draft.Text, includeNewlines: true)));
            Assert.StartsWith("From the artifact: ", rig.Runtime.Draft.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Ordinary_text_and_text_after_the_owners_own_words_join_as_they_always_did()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            rig.Runtime.DraftFromArtifact("Plan: Team, 12 seats");
            Assert.Equal("Plan: Team, 12 seats", rig.Runtime.Draft.Text);

            rig.Runtime.Draft = rig.Runtime.Draft with { Text = "Please check this" };
            rig.Runtime.DraftFromArtifact("/not-first");
            Assert.Equal("Please check this\n\n/not-first", rig.Runtime.Draft.Text);

            rig.Runtime.Draft = rig.Runtime.Draft with { Text = "" };
            rig.Runtime.DraftFromArtifact("a/b is not a command");
            Assert.Equal("a/b is not a command", rig.Runtime.Draft.Text);
        });
    }
}
