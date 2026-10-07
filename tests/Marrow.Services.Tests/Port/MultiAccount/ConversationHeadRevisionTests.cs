using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationHeadRevisionTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("context")]
    public async Task A_context_edit_during_link_preparation_cancels_the_old_answer_before_dispatch(string waiting)
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); engine.Link(h.Thread, a.Id);
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waiting == "confirm") h.Confirm = ct => consent.Task.WaitAsync(ct); else h.ContextWait = context.Task;
        var check = engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "OLD Q", "OLD A");
        h.ChangeRevision();
        consent.TrySetResult(true); context.TrySetResult("NEW CONTEXT");
        // Release hypothetical wrongly dispatched work as well, so a regression fails without hanging the suite.
        foreach (var flight in h.Flights) flight.Finish("Unexpected check");
        await check.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(h.Flights);
        Assert.NotNull(engine.Checks(h.Thread).Single().Answers.Single().Error);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("context")]
    public async Task A_context_edit_during_opinion_preparation_sends_nothing_after_a_late_yes(string waiting)
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waiting == "confirm") h.Confirm = ct => consent.Task.WaitAsync(ct); else h.ContextWait = context.Task;
        var asking = engine.AskAsync(h.Thread, "OLD Q", [a.Id]);
        h.ChangeRevision(); consent.TrySetResult(true); context.TrySetResult("NEW CONTEXT");
        await Assert.ThrowsAnyAsync<Exception>(() => asking);
        Assert.Empty(h.Flights); Assert.Empty(engine.Rounds(h.Thread));
    }

    [Fact]
    public async Task A_running_round_cannot_publish_late_text_or_combine_into_an_edited_thread()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var round = await engine.AskAsync(h.Thread, "Q", [a.Id]);
        h.ChangeRevision();
        foreach (var flight in h.Flights) flight.Finish("STALE ANSWER");
        await engine.WaitAsync(h.Thread, round);
        Assert.All(engine.Rounds(h.Thread).Single().Answers, answer => Assert.Empty(answer.Text));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, round));
        Assert.Empty(h.Posts);
    }
}
