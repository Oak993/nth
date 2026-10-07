using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class OpinionsEnginePublicationTests
{
    [Fact]
    public async Task A_stream_observer_sees_the_real_round_completion_before_Ask_returns_its_identity()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        Task? observed = null;
        engine.Changed += thread =>
        {
            if (observed is null && engine.Rounds(thread).SingleOrDefault() is { } round)
                observed = engine.WaitAsync(thread, round.Id);
        };
        h.OnRun = flight => flight.Text("FIRST PARTIAL");
        var id = await engine.AskAsync(h.Thread, "Q", [a.Id]);
        Assert.NotNull(observed);
        Assert.False(observed.IsCompleted);
        h.Flights[0].Finish("Answer");
        Assert.False(observed.IsCompleted);
        h.Flights[1].Finish("Opinion");
        await observed;
        Assert.Same(observed, engine.WaitAsync(h.Thread, id));
        Assert.All(engine.Rounds(h.Thread).Single().Answers, answer => Assert.True(answer.Done));
    }
}
