using Marrow.Core.Models;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class LinkEngineBoundaryTests
{
    [Theory]
    [InlineData("confirm", "unlink")]
    [InlineData("confirm", "dispose")]
    [InlineData("context", "unlink")]
    [InlineData("context", "off")]
    public async Task Permission_removed_while_preparing_releases_the_wait_without_launching_a_head(string waiting, string change)
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(ProviderKind.Local); engine.Link(h.Thread, a.Id);
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waiting == "confirm") h.Confirm = ct => consent.Task.WaitAsync(ct);
        else h.ContextWait = context.Task;
        var check = engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "A");
        Assert.False(check.IsCompleted);
        switch (change)
        {
            case "unlink": engine.Unlink(h.Thread, a.Id); break;
            case "dispose": engine.Dispose(); break;
            case "off": h.Change(a with { Enabled = false }); break;
        }
        await check;
        consent.TrySetResult(true); context.TrySetResult("CONTEXT");
        Assert.Empty(h.Flights);
        Assert.NotNull(engine.Checks(h.Thread).Single().Answers.Single().Error);
    }

    [Fact]
    public async Task An_out_source_cannot_launch_a_checker_and_disposal_cancels_existing_work()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); engine.Link(h.Thread, a.Id);
        h.Change(h.Rows[0] with { HasRoom = false });
        await engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "A");
        Assert.Empty(h.Flights);
        h.Change(h.Rows[0] with { HasRoom = true });
        var work = engine.CheckAnswerAsync(h.Thread, Guid.NewGuid(), h.Active, "Q", "A");
        engine.Dispose();
        await work;
        Assert.True(h.Flights[0].Token.IsCancellationRequested);
        h.Flights[0].Text("LATE");
        Assert.Equal("", engine.Checks(h.Thread).Single().Answers.Single().Text);
    }
}
