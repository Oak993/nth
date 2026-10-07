using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class LinkEngineTests
{
    [Fact]
    public void Tap_link_is_ordered_idempotent_and_refuses_active_out_disabled_or_unknown_accounts()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        ILinkPort port = engine;
        var a = h.Add(); var b = h.Add(); var off = h.Add(enabled: false); var full = h.Add(room: false);
        var changed = 0;
        port.Changed += _ => changed++;
        foreach (var id in new[] { h.Active, off.Id, full.Id, Guid.NewGuid(), b.Id, a.Id, b.Id }) port.Link(h.Thread, id);
        Assert.Equal([b.Id, a.Id], port.Linked(h.Thread));
        Assert.Equal(2, changed);
        port.Unlink(h.Thread, b.Id); port.Unlink(h.Thread, b.Id);
        Assert.Equal([a.Id], port.Linked(h.Thread));
        Assert.Equal(3, changed);
        Assert.Empty(port.Linked(Guid.NewGuid()));
        Assert.Empty(h.Flights);
    }

    [Fact]
    public async Task Every_answer_is_checked_once_by_each_link_in_parallel_and_text_streams_separately()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); var b = h.Add(ProviderKind.Local);
        engine.Link(h.Thread, a.Id); engine.Link(h.Thread, b.Id);
        var answer = Guid.NewGuid();
        var checking = engine.CheckAnswerAsync(h.Thread, answer, h.Active, "Question", "PRIMARY ANSWER");
        Assert.Equal(2, h.Flights.Count);
        Assert.All(h.Flights, flight => Assert.Equal(ConversationHeadPurpose.Check, flight.Request.Purpose));
        Assert.All(h.Flights, flight => Assert.Equal("PRIMARY ANSWER", flight.Request.Answer));
        h.Flights[0].Text("One fix");
        Assert.Equal("One fix", engine.Checks(h.Thread).Single().Answers[0].Text);
        Assert.False(engine.Checks(h.Thread).Single().Answers[0].Done);
        h.Flights[1].Finish("Looks safe"); h.Flights[0].Finish("One fix: restore timers");
        await checking;
        Assert.All(engine.Checks(h.Thread).Single().Answers, value => Assert.True(value.Done));
        await engine.CheckAnswerAsync(h.Thread, answer, h.Active, "Question", "PRIMARY ANSWER");
        Assert.Equal(2, h.Flights.Count);
        var next = engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Next", "SECOND ANSWER");
        Assert.Equal(4, h.Flights.Count);
        h.Flights[2].Finish("Second check"); h.Flights[3].Finish("Second local check");
        await next;
        Assert.Equal(2, engine.Checks(h.Thread).Count);
        Assert.Equal(h.Rows[0].Id, h.Active);
    }

    [Theory]
    [InlineData("unlink")]
    [InlineData("off")]
    [InlineData("out")]
    [InlineData("active")]
    [InlineData("cancel")]
    public async Task Removing_permission_cancels_the_checker_and_ignores_late_text(string why)
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); engine.Link(h.Thread, a.Id);
        using var cancel = new CancellationTokenSource();
        var checking = engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "A", cancel.Token);
        var flight = Assert.Single(h.Flights);
        switch (why)
        {
            case "unlink": engine.Unlink(h.Thread, a.Id); break;
            case "off": h.Change(a with { Enabled = false }); break;
            case "out": h.Change(a with { HasRoom = false }); break;
            case "active": h.ChangeActive(a.Id); break;
            case "cancel": cancel.Cancel(); break;
        }
        await checking;
        Assert.True(flight.Token.IsCancellationRequested);
        flight.Text("LATE TEXT");
        var result = Assert.Single(Assert.Single(engine.Checks(h.Thread)).Answers);
        Assert.DoesNotContain("LATE TEXT", result.Text, StringComparison.Ordinal);
        Assert.True(result.Done);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Cross_service_refusal_or_a_changed_source_during_confirmation_sends_nothing()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var other = h.Add(ProviderKind.Local); engine.Link(h.Thread, other.Id);
        h.Confirm = _ => Task.FromResult(false);
        await engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "A");
        Assert.Empty(h.Flights);
        h.Confirm = _ => { h.ChangeActive(other.Id); return Task.FromResult(true); };
        var source = h.Rows[0].Id;
        await engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), source, "Q", "A");
        Assert.Empty(h.Flights);
        Assert.Equal(2, h.Confirmations.Count);
    }
}
