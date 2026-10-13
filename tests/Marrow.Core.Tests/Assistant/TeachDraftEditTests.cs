using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 steps 2 and 3; ED-NEW-dextra-as19-4): the owner's edits to a draft. A step can be reworded and its guard stays
// (only code decides that), a step can be flagged so the trial pauses at it, a typed text can become an input, a step can be taken out,
// and the draft says what is wrong with it before it can be saved.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-4")]
public sealed class TeachDraftEditTests
{
    private static TeachDraft Mail() => TeachDraftBuilder.Skeleton(TeachSamples.Info(), TeachSamples.Mail(), []);

    [Fact]
    public void Rewording_a_step_keeps_its_guard_and_folds_the_text_to_one_line()
    {
        var draft = Mail().Reword(4, "  Press\nthe   big button ");
        Assert.Equal("Press the big button", draft.Steps[3].Text);
        Assert.Equal(ActionClass.Outward, draft.Steps[3].Guard!.Class);
    }

    [Fact]
    public void Flagging_a_step_makes_it_ask_and_unflagging_makes_it_stop_asking()
    {
        var flagged = Mail().Flag(1, true);
        Assert.True(flagged.Steps[0].AsksYou);
        Assert.Equal([1, 4], flagged.Boundary.Select(s => s.Number));
        Assert.Equal([4], flagged.Flag(1, false).Boundary.Select(s => s.Number));
    }

    [Fact]
    public void Flagging_a_guarded_step_off_leaves_it_asking()
    {
        var draft = Mail().Flag(4, false);
        Assert.True(draft.Steps[3].AsksYou);
    }

    [Fact]
    public void Taking_a_step_out_numbers_the_rest_again()
    {
        var draft = Mail().Remove(2);
        Assert.Equal([1, 2, 3], draft.Steps.Select(s => s.Number));
        Assert.Equal("Type “Weekly report” into “Subject” (edit)", draft.Steps[1].Text);
    }

    [Fact]
    public void A_typed_text_becomes_an_input_once_and_a_secret_or_a_click_cannot()
    {
        var draft = Mail();
        Assert.True(draft.TryMakeInput(2, "Recipient", "sam@example.com", out var made));
        Assert.Equal("Type “{recipient}” into “To” (edit)", made.Steps[1].Text);
        Assert.False(made.TryMakeInput(3, "recipient", "Weekly report", out _));
        Assert.False(draft.TryMakeInput(1, "name", "x", out _));
        Assert.False(draft.TryMakeInput(2, "9bad", "sam@example.com", out _));
        var secret = TeachDraftBuilder.Skeleton(TeachSamples.Info(), [TeachSamples.Type(1, 1, TeachSamples.Field("Key"), null, 20, secret: true)], []);
        Assert.False(secret.TryMakeInput(1, "key", "abc", out _));
    }

    [Theory]
    [InlineData("Recipient", "recipient")]
    [InlineData("send to", "send_to")]
    [InlineData("Due-Date", "due_date")]
    [InlineData("9lives", null)]
    [InlineData("", null)]
    [InlineData("a-very-very-very-very-very-long-name-indeed", null)]
    public void An_input_name_is_lowercase_letters_digits_and_underscores_from_a_letter(string name, string? expected) =>
        Assert.Equal(expected, TeachDraft.InputName(name));

    [Theory]
    [InlineData("Send the weekly report!", "send-the-weekly-report")]
    [InlineData("  --Hello   World--  ", "hello-world")]
    [InlineData("", "taught-skill")]
    [InlineData("भेजें", "taught-skill")]
    public void A_skill_name_is_made_from_words(string words, string expected) => Assert.Equal(expected, TeachDraft.SkillNameFrom(words));

    [Fact]
    public void A_skill_name_is_cut_to_the_limit()
    {
        var name = TeachDraft.SkillNameFrom(string.Join(' ', Enumerable.Repeat("word", 40)));
        Assert.True(name.Length <= 64);
        Assert.True(Marrow.Core.Port.Skills.Catalogue.SkillName.IsValid(name));
    }

    [Fact]
    public void The_draft_says_what_is_wrong_before_it_can_be_saved()
    {
        var good = Mail();
        Assert.Null(good.Problem());
        Assert.Equal("Name the skill with lowercase letters, digits and single hyphens, up to 64 characters.", (good with { Name = "Bad Name" }).Problem());
        Assert.StartsWith("Describe the skill in one line", (good with { Description = "" }).Problem(), StringComparison.Ordinal);
        Assert.StartsWith("Describe the skill in one line", (good with { Description = "two\nlines" }).Problem(), StringComparison.Ordinal);
        Assert.Equal("The skill has no steps.", (good with { Steps = ValueList<TeachStep>.Empty }).Problem());
        Assert.Equal("Step 1 is empty or out of order.", good.Reword(1, "   ").Problem());
        Assert.Equal("Step 1 is empty or out of order.", (good with { Steps = new ValueList<TeachStep>(good.Steps.Skip(1)) }).Problem());
    }

    [Fact]
    public void The_clock_shows_minutes_and_seconds_and_an_hour_from_an_hour()
    {
        Assert.Equal("0:00", TeachWords.Clock(TimeSpan.Zero));
        Assert.Equal("2:14", TeachWords.Clock(TimeSpan.FromSeconds(134.9)));
        Assert.Equal("1:02:14", TeachWords.Clock(TimeSpan.FromSeconds(3734)));
        Assert.Equal("0:00", TeachWords.Clock(TimeSpan.FromSeconds(-5)));
    }
}
