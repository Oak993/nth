using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class OpinionsEngineTests
{
    [Fact]
    public async Task One_message_streams_active_answer_and_other_opinions_in_parallel_and_the_next_has_its_own_selection()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        IOpinionsPort port = engine;
        var a = h.Add(); var local = h.Add(ProviderKind.Local);
        var first = await port.AskAsync(h.Thread, "Question 1", [a.Id, local.Id, a.Id]);
        Assert.Equal(3, h.Flights.Count);
        Assert.Equal([ConversationHeadPurpose.Answer, ConversationHeadPurpose.Opinion, ConversationHeadPurpose.Opinion], h.Flights.Select(f => f.Request.Purpose));
        h.Flights[1].Text("PARTIAL");
        Assert.Equal("PARTIAL", port.Rounds(h.Thread).Single().Answers[1].Text);
        Assert.All(port.Rounds(h.Thread).Single().Answers, answer => Assert.False(answer.Done));
        h.Flights[0].Finish("Answer"); h.Flights[1].Finish("Opinion"); h.Flights[2].Finish("Local opinion");
        await engine.WaitAsync(h.Thread, first);
        var second = await port.AskAsync(h.Thread, "Question 2", [local.Id]);
        Assert.Equal(5, h.Flights.Count);
        Assert.Equal([h.Active, local.Id], port.Rounds(h.Thread)[1].Answers.Select(a => a.AccountId));
        h.Flights[3].Finish("Second answer"); h.Flights[4].Finish("Second opinion");
        await engine.WaitAsync(h.Thread, second);
        Assert.Equal(h.Rows[0].Id, h.Active);
        Assert.Empty(port.Rounds(Guid.NewGuid()));
    }

    [Fact]
    public async Task Everyone_with_room_includes_other_services_and_each_Local_entry_but_excludes_active_off_and_out()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var same = h.Add(); var other = h.Add(ProviderKind.Codex); var local1 = h.Add(ProviderKind.Local); var local2 = h.Add(ProviderKind.Local);
        h.Add(enabled: false); h.Add(room: false);
        Assert.Equal([same.Id, other.Id, local1.Id, local2.Id], engine.EveryoneWithRoom(h.Thread));
        var round = await engine.AskAsync(h.Thread, "@all question", engine.EveryoneWithRoom(h.Thread));
        Assert.Equal(5, h.Flights.Count);
        foreach (var flight in h.Flights) flight.Finish("A");
        await engine.WaitAsync(h.Thread, round);
    }

    [Fact]
    public async Task Combine_requires_complete_real_answers_runs_once_and_posts_the_original_account_byline()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        var round = await engine.AskAsync(h.Thread, "Q", [a.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, round));
        h.Flights[0].Finish("ACTIVE ANSWER"); h.Flights[1].Finish("OTHER OPINION");
        await engine.WaitAsync(h.Thread, round);
        var combining = engine.CombineAsync(h.Thread, round);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, round));
        var flight = h.Flights[2];
        Assert.Equal(ConversationHeadPurpose.Combine, flight.Request.Purpose);
        Assert.Equal(h.Active, flight.Request.Account.Id);
        Assert.Contains("ACTIVE ANSWER", flight.Request.Message, StringComparison.Ordinal);
        Assert.Contains("OTHER OPINION", flight.Request.Message, StringComparison.Ordinal);
        flight.Finish("COMBINED ANSWER");
        await combining;
        var post = Assert.Single(h.Posts);
        Assert.Equal("COMBINED ANSWER", post.Text);
        Assert.Equal("Claude Max 1 \U000000B7 combined 2 answers", post.Byline);
        Assert.True(engine.Rounds(h.Thread).Single().Combined);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, round));
        Assert.Equal(3, h.Flights.Count);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("failed")]
    [InlineData("empty")]
    [InlineData("off")]
    public async Task A_cancelled_failed_empty_or_disabled_head_cannot_be_combined_and_late_events_are_ignored(string why)
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add();
        using var cancel = new CancellationTokenSource();
        var round = await engine.AskAsync(h.Thread, "Q", [a.Id], cancel.Token);
        h.Flights[0].Finish("A");
        switch (why)
        {
            case "cancel": cancel.Cancel(); break;
            case "failed": h.Flights[1].Result.SetException(new InvalidOperationException("Offline failure")); break;
            case "empty": h.Flights[1].Finish(""); break;
            case "off": h.Change(a with { Enabled = false }); break;
        }
        await engine.WaitAsync(h.Thread, round);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CombineAsync(h.Thread, round));
        h.Flights[1].Text("LATE");
        Assert.DoesNotContain("LATE", engine.Rounds(h.Thread).Single().Answers[1].Text, StringComparison.Ordinal);
        Assert.NotEmpty(engine.Errors(h.Thread, round));
        Assert.Empty(h.Posts);
    }

    [Fact]
    public async Task Refused_transfer_or_lost_room_while_waiting_sends_nothing_and_does_not_make_a_round()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        var a = h.Add(ProviderKind.Local);
        h.Confirm = _ => Task.FromResult(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.AskAsync(h.Thread, "Q", [a.Id]));
        h.Confirm = _ => { h.Change(a with { HasRoom = false }); return Task.FromResult(true); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AskAsync(h.Thread, "Q", [a.Id]));
        Assert.Empty(engine.Rounds(h.Thread));
        Assert.Empty(h.Flights);
    }

    [Fact]
    public async Task Invalid_selection_or_an_out_active_account_never_dispatches()
    {
        var h = new ConversationHeadHarness();
        using var engine = new OpinionsEngine(h);
        await Assert.ThrowsAsync<ArgumentException>(() => engine.AskAsync(h.Thread, "Q", []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AskAsync(h.Thread, "Q", [h.Active]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AskAsync(h.Thread, "Q", [Guid.NewGuid()]));
        var a = h.Add(); h.Change(h.Rows[0] with { HasRoom = false });
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AskAsync(h.Thread, "Q", [a.Id]));
        Assert.Empty(h.Flights);
    }
}
