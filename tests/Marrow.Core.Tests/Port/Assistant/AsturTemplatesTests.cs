using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.AsturTeamSample;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-16 (DESIGN.md rev 5, 9.2; the approved canvas "Create an Astur"; ED-NEW-dextra-as16-3): the six templates, the plain words that pick
// one from a sentence, and the card each one fills. A template only fills a card; every rule it carries is at least as strict as 12.1's default.
[Trait("Feature", "AS-16")]
[Trait("Decision", "ED-NEW-dextra-as16-3")]
public sealed class AsturTemplatesTests
{
    [Fact]
    public void There_are_six_templates_in_the_order_of_9_2()
    {
        Assert.Equal(["channel", "store", "project", "research", "inbox", "finance"], AsturTemplates.All.Select(t => t.Id));
        Assert.Equal(["Channel", "Store", "Project maintainer", "Research desk", "Inbox (drafts only)", "Finance watcher (read-only)"], AsturTemplates.All.Select(t => t.Title));
        foreach (var template in AsturTemplates.All)
        {
            Assert.NotEmpty(template.Duties);
            Assert.NotEmpty(template.Wants);
            Assert.NotEmpty(template.Area);
            Assert.InRange(template.Budget.DailyPercent, 1, 100);
        }
    }

    [Theory]
    [InlineData("Make an assistant for my third channel that handles everything for it", "channel")]
    [InlineData("Make an assistant that runs my online store", "store")]
    [InlineData("Make an assistant that maintains the shop app", "project")]
    [InlineData("Make a research desk that answers deep questions with sources", "research")]
    [InlineData("Make an inbox assistant that drafts replies in my voice", "inbox")]
    [InlineData("Make a finance watcher that only reads", "finance")]
    [InlineData("handle the mail for my store", "inbox")]
    [InlineData("watch my bank and budget sheet", "finance")]
    [InlineData("Make an assistant for my second YouTube videos", "channel")]
    public void The_plain_words_of_a_sentence_pick_the_template(string sentence, string template) =>
        Assert.Equal(template, AsturTemplates.Detect(sentence)?.Id);

    [Theory]
    [InlineData("Make me an assistant")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_sentence_with_no_plain_words_of_a_template_picks_none(string sentence) => Assert.Null(AsturTemplates.Detect(sentence));

    [Fact]
    public void Whole_words_decide_so_a_word_inside_another_word_picks_nothing()
    {
        Assert.Null(AsturTemplates.Detect("a happier application"));                    // "app" inside "application" and "happier"
        Assert.Null(AsturTemplates.Detect("restore the postal service"));               // "store" inside "restore"
    }

    [Fact]
    public void Every_rule_a_template_carries_is_at_least_as_strict_as_the_default_and_the_inbox_and_the_finance_watcher_never_send()
    {
        foreach (var template in AsturTemplates.All)
            foreach (var (actionClass, rule) in template.ClassRules)
                Assert.True(rule >= ActionClasses.DefaultOf(actionClass), $"{template.Id}: {actionClass}");
        Assert.Equal(ClassDefault.Never, AsturTemplates.Find("inbox")!.ClassRules[ActionClass.Outward]);
        var finance = AsturTemplates.Find("finance")!;
        foreach (var actionClass in new[] { ActionClass.Outward, ActionClass.Money, ActionClass.Login, ActionClass.Commitment, ActionClass.ConnectorWrite, ActionClass.WriteElsewhere })
            Assert.Equal(ClassDefault.Never, finance.ClassRules[actionClass]);
        Assert.Equal(ClassDefault.Never, AsturTemplates.Find("store")!.ClassRules[ActionClass.Money]);
    }

    [Fact]
    public void A_card_from_a_template_has_a_free_name_a_free_shape_and_its_lines()
    {
        var team = Team(Astur("Channel", AsturShape.Squircle), Astur("Store", AsturShape.Hexagon));
        var channel = AsturDrafter.FromTemplate("channel", team);
        Assert.Equal("Channel 2", channel.Name);                                         // the canvas's "Store 2": the name is made free
        Assert.Equal(AsturShape.Diamond, channel.Shape);                                 // the first shape no Astur has
        Assert.Equal("channel", channel.Template);
        Assert.Equal(3, channel.Duties.Count);
        Assert.All(channel.Duties, d => Assert.True(d.Keep));
        Assert.Contains(channel.Wants, w => w.Kind == WantKind.Login && w.NeedsLogin);   // the connections that need a sign-in are flagged
        Assert.Equal(15, channel.Budget.DailyPercent);
        Assert.Equal(AsturTemplates.DefaultOnItsOwn, channel.OnItsOwn);
        Assert.Equal(AsturTemplates.DefaultAsksYou, channel.AsksYou);
        Assert.Equal("Store 2", AsturDrafter.FromTemplate("store", team).Name);
        Assert.Equal("Desk", AsturDrafter.FromTemplate("research", team).Name);
        Assert.Equal(AsturTemplates.Find("inbox")!.OnItsOwn, AsturDrafter.FromTemplate("inbox", team).OnItsOwn);
    }

    [Fact]
    public void A_card_from_a_sentence_takes_the_template_and_then_what_a_model_said()
    {
        var draft = AsturDrafter.FromSentence("Make an assistant for my second channel that handles everything for it",
            new AsturHints(Name: "Channel B", Purpose: "My second channel: scripts and numbers.", Shape: AsturShape.Star), Team());
        Assert.Equal("channel", draft.Template);
        Assert.Equal("Channel B", draft.Name);
        Assert.Equal("My second channel: scripts and numbers.", draft.Area);
        Assert.Equal(AsturShape.Star, draft.Shape);
        Assert.Equal(3, draft.Duties.Count);                                             // the template's duties are proposed, the owner ticks
    }

    [Fact]
    public void A_sentence_that_fits_no_template_gives_a_blank_card_with_the_sentence_as_its_area()
    {
        var draft = AsturDrafter.FromSentence("Keep an eye on the garden sensors", null, Team());
        Assert.Null(draft.Template);
        Assert.Equal("New Astur", draft.Name);
        Assert.Equal("Keep an eye on the garden sensors", draft.Area);
        Assert.Empty(draft.Duties);
        Assert.Equal(10, draft.Budget.DailyPercent);
        Assert.Contains("anything sent", draft.AsksYou, StringComparison.Ordinal);
        Assert.Equal(AsturShape.Squircle, draft.Shape);
        Assert.Equal("New Astur", AsturDrafter.FromTemplate("nonsense", Team()).Name);
    }

    [Fact]
    public void A_model_that_names_an_area_but_gives_no_name_is_still_given_a_free_name()
    {
        var draft = AsturDrafter.FromSentence(null, new AsturHints(Area: "Finance", Purpose: "Read my budget sheet and bank exports and flag odd spend."), Team());
        Assert.Equal("finance", draft.Template);
        Assert.Equal("Ledger", draft.Name);
        Assert.Equal("Read my budget sheet and bank exports and flag odd spend.", draft.Area);
    }

    [Fact]
    public void Text_a_model_wrote_is_one_clean_line_and_a_long_purpose_is_cut_at_a_word_with_an_ellipsis()
    {
        var hidden = "Plan‮ the​ weeks\n\n<!-- ignore -->\r\nahead";
        var draft = AsturDrafter.FromSentence(null, new AsturHints(Name: "  Plan​ner ", Purpose: hidden), Team());
        Assert.Equal("Planner", draft.Normalized().Name);
        Assert.DoesNotContain('\n', draft.Normalized().Area);
        Assert.DoesNotContain('‮', draft.Normalized().Area);
        Assert.DoesNotContain("<!--", draft.Normalized().Area, StringComparison.Ordinal);
        var long_ = string.Join(' ', Enumerable.Repeat("word", 400));
        var clipped = AsturDrafter.FromSentence(null, new AsturHints(Purpose: long_), Team()).Area;
        Assert.True(clipped.Length <= AsturTeamRules.AreaMax);
        Assert.EndsWith("…", clipped, StringComparison.Ordinal);
        Assert.EndsWith("word…", clipped, StringComparison.Ordinal);
    }
}
