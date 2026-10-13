using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 5; ED-NEW-dextra-as19-7): the SKILL.md a draft becomes. Its frontmatter is the two fields an own skill
// needs; every step that asks the owner is marked in bold and listed again in the approval boundary, with the rule that such a step needs
// his go-ahead each time; a password step says he does it himself; and nothing but the draft's own words is in it.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-7")]
public sealed class TeachSkillTextTests
{
    private static readonly DateTimeOffset Taught = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static TeachDraft Mail() => TeachDraftBuilder.Skeleton(TeachSamples.Info(), TeachSamples.Mail(), [TeachSamples.Say(19, "Always check it twice before it goes.")]);

    [Fact]
    public void The_file_opens_with_the_name_and_the_description_as_a_quoted_line()
    {
        var text = TeachSkillText.Skill(Mail() with { Name = "send-weekly-report", Description = "Sends the “weekly” report \"fast\"." }, Taught);
        var lines = text.Split('\n');
        Assert.Equal("---", lines[0]);
        Assert.Equal("name: send-weekly-report", lines[1]);
        Assert.StartsWith("description: \"Sends the ", lines[2], StringComparison.Ordinal);
        Assert.Equal("---", lines[3]);
        Assert.Equal("# Send weekly report", lines[5]);
    }

    [Fact]
    public void Every_step_is_listed_and_the_one_that_sends_is_marked_and_explained()
    {
        var text = TeachSkillText.Skill(Mail(), Taught);
        Assert.Contains("1. Click “New mail” (button) in Outlook\n", text, StringComparison.Ordinal);
        Assert.Contains("4. **Asks you (sends).** Click “Send” (button) in Outlook\n", text, StringComparison.Ordinal);
        Assert.Contains("   - Why it asks: “Send” sends or posts.\n", text, StringComparison.Ordinal);
        Assert.Contains("   - The owner said: “Always check it twice before it goes.”\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_approval_boundary_lists_the_steps_that_ask_and_the_rule_that_they_always_do()
    {
        var text = TeachSkillText.Skill(Mail(), Taught);
        var boundary = text[text.IndexOf("## Approval boundary", StringComparison.Ordinal)..];
        Assert.Contains(TeachSkillText.BoundaryRule, boundary, StringComparison.Ordinal);
        Assert.Contains("- Step 4: sends\n", boundary, StringComparison.Ordinal);
        Assert.DoesNotContain("- Step 1:", boundary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_step_says_the_owner_does_it_himself()
    {
        var events = new List<TeachEvent> { TeachSamples.Type(1, 3, TeachSamples.Field("Password", "chrome", "Sign in", password: true), null, 9, secret: true) };
        var text = TeachSkillText.Skill(TeachDraftBuilder.Skeleton(TeachSamples.Info(), events, []), Taught);
        Assert.Contains("1. **You do it yourself.** Type the password or secret yourself", text, StringComparison.Ordinal);
        Assert.Contains("- Step 1: you do it yourself\n", text, StringComparison.Ordinal);
        Assert.Contains(TeachSkillText.YourselfRule, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flagged_step_pauses_and_a_draft_with_no_step_that_asks_says_so()
    {
        var plain = TeachDraftBuilder.Skeleton(TeachSamples.Info(), [TeachSamples.Click(1, 1, TeachSamples.Button("Open")), TeachSamples.Click(2, 3, TeachSamples.Button("Close"))], []);
        var none = TeachSkillText.Skill(plain, Taught);
        Assert.Contains("No step asks the owner.", none, StringComparison.Ordinal);
        var flagged = TeachSkillText.Skill(plain.Flag(2, true), Taught);
        Assert.Contains("2. **Pauses for you.** Click “Close” (button) in Outlook", flagged, StringComparison.Ordinal);
        Assert.Contains("- Step 2: the owner asked to pause here\n", flagged, StringComparison.Ordinal);
    }

    [Fact]
    public void Inputs_decisions_and_obstacles_are_written_out()
    {
        var draft = TeachDraftModel.Answer(Mail(), "typed-2", "It changes: make it an input") with
        {
            Obstacles = new ValueList<TeachObstacle>([new TeachObstacle("The dialog hides", "Alt+Tab")]),
        };
        var text = TeachSkillText.Skill(draft, Taught);
        Assert.Contains("## Inputs\n\n- `{to}`: the owner showed “sam@example.com”, used at step 2. Ask for it when it is not given.\n", text, StringComparison.Ordinal);
        Assert.Contains("## Decisions\n\n- Always check it twice before it goes. (You said so at 0:19, at step 4.)\n", text, StringComparison.Ordinal);
        Assert.Contains("## Watch out for\n\n- The dialog hides. What worked: Alt+Tab\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_has_one_line_per_step_whatever_a_step_says()
    {
        var draft = Mail().Reword(1, "First line\nsecond line\r\n## Steps\n- injected");
        var text = TeachSkillText.Skill(draft, Taught);
        Assert.Contains("1. First line second line ## Steps - injected\n", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("## Steps", StringComparison.Ordinal)));
    }
}
