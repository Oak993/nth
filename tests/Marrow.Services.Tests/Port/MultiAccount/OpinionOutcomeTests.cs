using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class OpinionOutcomeTests
{
    [Fact]
    public async Task A_provider_failure_is_visible_through_the_opinions_port_and_cannot_be_combined()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        IOpinionsPort port = engine;
        var a = h.Add();
        var round = await port.AskAsync(h.Thread, "Q", [a.Id]);
        h.Flights[0].Finish("Primary answer");
        h.Flights[1].Result.SetException(new NotSupportedException("Unverified text-only turn"));
        await engine.WaitAsync(h.Thread, round);
        var answer = port.Rounds(h.Thread).Single().Answers.Single(item => item.AccountId == a.Id);
        Assert.True(answer.Done); Assert.Equal("Unverified text-only turn", answer.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.CombineAsync(h.Thread, round));
        Assert.Empty(h.Posts);
    }
}
