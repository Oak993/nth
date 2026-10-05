using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY: SK-14 (DL-18, ONDEMAND 5.6: the card says why the item is needed, the step it blocks). What the flow says the card's why line is, from the signal that made the offer (LEAD-PROPOSED WORDING, OfferWords, ED-NEW-cloud-sk-14 item 16).
public sealed class SkillsOfferWhyTests
{
    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_failed_command_says_the_command_cannot_run_without_it()
    {
        using var r = new SkillsOfferRig();
        Assert.Null(r.Flow.Why);
        r.Offer();
        Assert.Equal(OfferWords.WhyCommand("soffice --headless"), r.Flow.Why);
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_held_command_says_the_same()
    {
        using var r = new SkillsOfferRig();
        r.Turn.Send("fixture");
        r.Held();
        Assert.Equal(OfferWords.WhyCommand("soffice --headless"), r.Flow.Why);
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_skills_declared_need_names_the_skill()
    {
        using var r = new SkillsOfferRig();
        r.Main(() => r.Flow.Observe(new SkillUsed("docx", "A2")));
        Assert.NotNull(r.Flow.Card);
        Assert.Equal(OfferWords.WhySkill("docx"), r.Flow.Why);
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_models_request_gives_its_own_reason_and_without_one_the_line_is_empty()
    {
        using (var r = new SkillsOfferRig())
        {
            r.Main(() => r.Flow.Observe(new ToolRequested("soffice", "To convert the report to PDF.")));
            Assert.Equal("To convert the report to PDF.", r.Flow.Why);
        }
        using var without = new SkillsOfferRig();
        without.Main(() => without.Flow.Observe(new ToolRequested("soffice", " ")));
        Assert.NotNull(without.Flow.Card);
        Assert.Null(without.Flow.Why);
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_line_stays_through_the_states_of_one_offer_and_a_need_that_joins_does_not_replace_it()
    {
        using var r = new SkillsOfferRig();
        r.Offer();
        var first = r.Flow.Why;
        r.Main(() => r.Flow.Observe(new SkillUsed("docx", "A2")));
        Assert.Equal(first, r.Flow.Why);
    }
}
