using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class CombineRevisionTests
{
    [Fact]
    public async Task An_edit_during_synthesis_cancels_it_and_never_posts_into_the_new_context()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var round = await engine.AskAsync(h.Thread, "Q", [a.Id]);
        h.Flights[0].Finish("A"); h.Flights[1].Finish("B"); await engine.WaitAsync(h.Thread, round);
        var combine = engine.CombineAsync(h.Thread, round);
        h.ChangeRevision(); h.Flights[2].Finish("STALE SYNTHESIS");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => combine);
        Assert.Empty(h.Posts);
        Assert.False(engine.Rounds(h.Thread).Single().Combined);
    }
}
