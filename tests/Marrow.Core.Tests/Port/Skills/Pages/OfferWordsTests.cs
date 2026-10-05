using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// PORT-ONLY: SK-14 (DL-18). The in-chat offer's words, word set S1 (Plain). LEAD-PROPOSED WORDING (ED-NEW-cloud-sk-14 item 16): the lead named the decline label as the plan has it ("Don't ask again in this chat", ONDEMAND 5.3) and asked
// for the asked sentence, the why-needed line and the three captions to be written in the same voice (short, plain, no jargon); the owner can revise them. The expected strings are written out here on purpose.
[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-4")]
public sealed class OfferWordsTests
{
    [Fact]
    public void The_decline_is_labelled_as_the_plan_names_it() => Assert.Equal("Don't ask again in this chat", OfferWords.DeclineLabel);

    [Theory]
    [InlineData("LibreOffice", "LibreOffice isn't on this PC.")]
    [InlineData("LibreOffice|Python", "LibreOffice and Python aren't on this PC.")]
    [InlineData("LibreOffice|Python|Node.js", "LibreOffice, Python and Node.js aren't on this PC.")]
    public void The_asked_sentence_says_what_is_missing_and_agrees_with_how_many(string titles, string expected) => Assert.Equal(expected, OfferWords.Asked(titles.Split('|')));

    [Fact]
    public void The_why_line_names_the_step_it_blocks_and_gives_a_models_own_reason_as_it_was_given()
    {
        Assert.Equal("`soffice --headless` can't run without it.", OfferWords.WhyCommand("soffice --headless"));
        Assert.Equal("The docx skill needs it.", OfferWords.WhySkill("docx"));
        Assert.Equal("To convert the report to PDF.", OfferWords.WhyModel("  To convert the report to PDF.  "));
        Assert.Null(OfferWords.WhyModel("   "));
    }

    [Theory]
    [InlineData("LibreOffice", "LibreOffice is installed.")]
    [InlineData("LibreOffice|Python", "LibreOffice and Python are installed.")]
    public void Done_says_it_is_installed(string titles, string expected) => Assert.Equal(expected, OfferWords.Done(titles.Split('|')));

    [Fact]
    public void Continuing_says_the_task_goes_on() => Assert.Equal("LibreOffice is installed. Carrying on where it stopped.", OfferWords.Continuing(["LibreOffice"]));

    [Theory]
    [InlineData(ItemKind.Tool, "Not downloaded. You can get LibreOffice later in Settings, under Tools.")]
    [InlineData(ItemKind.Skillset, "Not downloaded. You can get LibreOffice later in Settings, under Skills.")]
    [InlineData(ItemKind.Plugin, "Not downloaded. You can get LibreOffice later in Settings, under Plugins.")]
    public void Declined_says_where_it_can_be_downloaded_later(ItemKind kind, string expected) => Assert.Equal(expected, OfferWords.Declined(["LibreOffice"], [kind]));

    [Fact]
    public void Declined_names_every_page_once_when_the_items_sit_on_several()
    {
        Assert.Equal("Not downloaded. You can get LibreOffice and Python later in Settings, under Tools.", OfferWords.Declined(["LibreOffice", "Python"], [ItemKind.Tool, ItemKind.Pyset]));
        Assert.Equal("Not downloaded. You can get A and B later in Settings, under Skills and Tools.", OfferWords.Declined(["A", "B"], [ItemKind.Skillset, ItemKind.Tool]));
    }
}
