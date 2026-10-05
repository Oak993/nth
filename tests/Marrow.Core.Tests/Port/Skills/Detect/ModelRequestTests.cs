using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: S4, the model asks (ONDEMAND-PLAN.md 5.1): "A Marrow tool request_tool {name, reason} for the native API providers, which blocks until the
// user answers". The detector only decides whether the name is a catalogue item and whether an offer is owed; the tool itself is DL-14. The name is an item id
// or a program name from the provides index (ED-296: the plan does not say which spelling the model uses).
[Trait("Spec", "DL-10")]
public sealed class ModelRequestTests
{
    private static Detection Asked(string name, DetectionContext? context = null) =>
        DetectFixtures.Run(context ?? DetectFixtures.Ctx(), new ToolRequested(name, "the task needs it"));

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("T3", "T3")]
    [InlineData("P1", "P1")]
    [InlineData("A2", "A2")]
    [InlineData("soffice", "T3")]
    [InlineData("SOFFICE", "T3")]
    [InlineData("pdftoppm", "T4")]
    [InlineData("python", "T1")]
    [InlineData("pip", "T1p")]
    public void A_catalogue_id_or_a_program_name_the_catalogue_provides_is_an_offer(string name, string item)
    {
        var detection = Asked(name);
        Assert.Equal([item], DetectFixtures.Offered(detection));
        Assert.Equal(DetectedSignal.ModelRequest, detection.Findings.Single().Signal);
        Assert.Equal(name, detection.Findings.Single().Token);
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("LibreOffice")]
    [InlineData("t3")]
    [InlineData("docx")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("rm")]
    [InlineData("T99")]
    [InlineData("soffice.exe")]
    public void A_name_outside_the_catalogue_gets_no_hit(string name)
    {
        var detection = Asked(name);
        Assert.Empty(detection.Findings);
        Assert.Null(detection.Card);
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_that_is_there_or_declined_or_already_offered_is_not_offered_again()
    {
        var installed = Asked("soffice", DetectFixtures.Ctx(DetectFixtures.State(DetectFixtures.Has("T3"))));
        Assert.Equal(SuppressReason.InstalledAndHealthy, installed.Findings.Single().Reason);
        Assert.Null(installed.Card);
        Assert.Equal(SuppressReason.AlreadyOffered, Asked("T3", DetectFixtures.Ctx(null, "chat-1", "T3")).Findings.Single().Reason);
        var declined = AddonsStateWithDecline();
        Assert.Equal(SuppressReason.Declined, Asked("T3", DetectFixtures.Ctx(declined)).Findings.Single().Reason);
    }

    private static Marrow.Core.Port.Skills.State.AddonsState AddonsStateWithDecline() => Marrow.Core.Port.Skills.State.AddonsState.Empty.Decline("chat-1", "T3");
}
