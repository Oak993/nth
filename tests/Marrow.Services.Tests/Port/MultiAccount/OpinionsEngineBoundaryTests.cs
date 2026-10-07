using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class OpinionsEngineBoundaryTests
{
    [Fact]
    public async Task Cancelling_a_round_is_isolated_and_a_source_switch_prevents_synthesis_on_the_new_account()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var first = await engine.AskAsync(h.Thread, "First", [a.Id]);
        var second = await engine.AskAsync(h.Thread, "Second", [a.Id]);
        engine.Cancel(h.Thread, first);
        await engine.WaitAsync(h.Thread, first);
        Assert.True(h.Flights[0].Token.IsCancellationRequested);
        Assert.False(h.Flights[2].Token.IsCancellationRequested);
        h.Flights[2].Finish("Second answer"); h.Flights[3].Finish("Second opinion");
        await engine.WaitAsync(h.Thread, second);
        h.ChangeActive(a.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, second));
        Assert.Empty(h.Posts);
        Assert.Equal(4, h.Flights.Count);
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("off")]
    public async Task Pending_confirmation_is_cancelled_before_context_or_model_dispatch(string why)
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add(ProviderKind.Local);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Confirm = ct => pending.Task.WaitAsync(ct);
        using var cancel = new CancellationTokenSource();
        var asking = engine.AskAsync(h.Thread, "Q", [a.Id], cancel.Token);
        switch (why)
        {
            case "dispose": engine.Dispose(); break;
            case "cancel": cancel.Cancel(); break;
            case "off": h.Change(a with { Enabled = false }); break;
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        pending.TrySetResult(true);
        Assert.Empty(engine.Rounds(h.Thread));
        Assert.Empty(h.Flights);
    }

    [Fact]
    public async Task Cancellation_during_Combine_never_posts_a_partial_result_and_unknown_rounds_are_refused()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var round = await engine.AskAsync(h.Thread, "Q", [a.Id]);
        h.Flights[0].Finish("A"); h.Flights[1].Finish("B"); await engine.WaitAsync(h.Thread, round);
        using var cancel = new CancellationTokenSource();
        var combine = engine.CombineAsync(h.Thread, round, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => combine);
        Assert.True(h.Flights[2].Token.IsCancellationRequested);
        h.Flights[2].Finish("LATE SYNTHESIS");
        Assert.Empty(h.Posts);
        Assert.False(engine.Rounds(h.Thread).Single().Combined);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, Guid.NewGuid()));
    }
}
