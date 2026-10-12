using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Port.PcControl;

// PORT-ONLY: web surfaces W11 (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "The pill: 'Using Visus' with the step line,
// Pause and Stop", "Pause: ... the page stays as it is", "User takes over: ... the pill says 'You took over Visus' with Resume";
// the canvas's "nth needs your OK" / "Check the card"; ED-NEW-web-w11-1). What the Tactus pill says for the app "visus".
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class PillStatesVisusTests
{
    private static readonly PillApp Visus = new("visus", "Visus", "Filling in the email") { PausedLine = "The page stays as it is" };
    private static readonly PillApp Excel = new("exe:c:\\office\\excel.exe", "Excel", "Typed 4 characters into B2");

    [Fact]
    public void Visus_working_reads_Using_Visus_with_its_step()
    {
        var view = PillStates.Compute(new PillFacts { Apps = [Visus], LatestApp = "visus" }, 10);

        Assert.Equal("Using Visus", view.Title);
        Assert.Equal("Visus: Filling in the email", view.Line);
        Assert.Equal(PillGroupKind.Run, view.Group);
        Assert.Equal(PillGlyph.Live, view.Glyph);
    }

    [Fact]
    public void A_take_over_reads_You_took_over_Visus_with_Resume_and_the_other_apps_go_on()
    {
        var alone = PillStates.Compute(new PillFacts { Apps = [Visus with { TookOver = true }] }, 10);
        var beside = PillStates.Compute(new PillFacts { Apps = [Excel, Visus with { TookOver = true }], LatestApp = Excel.Key }, 10);

        Assert.Equal("You took over Visus", alone.Title);
        Assert.Equal("Resume when you are done", alone.Line);
        Assert.Equal(PillGroupKind.Paused, alone.Group); // Resume and Stop
        Assert.Equal(PillGlyph.Pause, alone.Glyph);
        Assert.Equal(TactusMarkState.Paused, alone.Mark);
        Assert.Equal("You took over Visus", beside.Title);
        var row = Assert.Single(beside.Rows, r => r.Key == "visus");
        Assert.Equal("You took over", row.Line);
        Assert.True(row.Paused);
        Assert.Equal("Resume Visus", row.PauseName);
        Assert.Equal("Typed 4 characters into B2", Assert.Single(beside.Rows, r => r.Key == Excel.Key).Line);
    }

    [Fact]
    public void Visus_waiting_on_its_own_card_reads_nth_needs_your_OK_Check_the_card()
    {
        var view = PillStates.Compute(new PillFacts { Apps = [Visus with { OwnCard = true, WaitsOnAsk = true }] }, 10);

        Assert.Equal("nth needs your OK", view.Title);
        Assert.Equal("Check the card", view.Line);
        Assert.False(view.Ask); // the card is in Visus, not in the pill
        Assert.Equal("Check the card", Assert.Single(view.Rows).Line);
    }

    [Fact]
    public void A_pill_ask_still_comes_first_and_Visus_is_not_said_to_keep_working_while_it_waits()
    {
        var asks = new[] { new PillAsk("a", PillAskKind.Approval, "Outlook", "Report", "Claude") };
        var view = PillStates.Compute(new PillFacts { Apps = [Visus with { OwnCard = true, WaitsOnAsk = true }], Asks = asks }, 10);

        Assert.Equal("Needs your OK. Outlook", view.Title);
        Assert.Equal("Click to answer", view.Line);
    }

    [Fact]
    public void Visus_paused_alone_says_the_page_stays_as_it_is()
    {
        var view = PillStates.Compute(new PillFacts { Apps = [Visus with { Phase = PillAppPhase.Paused, PhaseSince = 9.5 }] }, 10);
        var two = PillStates.Compute(new PillFacts { Apps = [Visus with { Phase = PillAppPhase.Paused, PhaseSince = 9.5 }, Excel with { Phase = PillAppPhase.Paused, PhaseSince = 9.5 }] }, 10);

        Assert.Equal("Paused in Visus", view.Title);
        Assert.Equal("The page stays as it is", view.Line);
        Assert.Equal("Visus and Excel hold the next steps", two.Line);
    }

    [Fact]
    public void A_Tactus_app_with_none_of_the_web_facts_reads_as_before()
    {
        var view = PillStates.Compute(new PillFacts { Apps = [Excel], LatestApp = Excel.Key }, 10);

        Assert.Equal("Using Excel", view.Title);
        Assert.Equal("Excel: Typed 4 characters into B2", view.Line);
    }
}
