using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class CompletedLinkAnswerTests
{
    [Fact]
    public async Task The_last_completed_answer_is_checked_when_its_author_just_exhausted_its_cap_without_sending_again_to_the_author()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); engine.Link(h.Thread, a.Id);
        h.Change(h.Rows[0] with { HasRoom = false });
        var check = engine.CheckCompletedAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "LAST A");
        var flight = Assert.Single(h.Flights);
        Assert.Equal(a.Id, flight.Request.Account.Id);
        Assert.Equal("LAST A", flight.Request.Answer);
        flight.Finish("Checked last answer"); await check;
        Assert.Null(engine.Checks(h.Thread).Single().Answers.Single().Error);
        Assert.False(h.Rows[0].HasRoom);
    }
}
