using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class TextOnlyAccountHeadTests
{
    [Fact]
    public async Task A_private_head_streams_text_waits_for_completion_and_stops_without_adoption()
    {
        var session = new HandoffSession();
        var progress = new List<string>();
        var run = TextOnlyAccountHead.RunAsync(session, Config(), "QUESTION AND CONTEXT", progress.Add, () => true, CancellationToken.None);
        Assert.Single(session.Inputs);
        Assert.True(session.Inputs[0].IsFinalReport);
        Assert.False(run.IsCompleted);
        session.OnEvent!(new ProviderEvent.MessageDelta("a", "First "));
        session.OnEvent!(new ProviderEvent.MessageDelta("a", "answer"));
        session.OnEvent!(new ProviderEvent.MessageCompleted("a", ""));
        Assert.Equal("First answer", progress[^1]);
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        Assert.Equal("First answer", await run);
        Assert.True(session.Stopped);
        Assert.Null(session.OnEvent);
    }

    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Pi)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Cursor)]
    public async Task An_injected_warm_or_deferred_standby_capability_is_not_permission_for_a_model_head(ProviderKind provider)
    {
        var session = new HandoffSession();
        await Assert.ThrowsAsync<NotSupportedException>(() => TextOnlyAccountHead.RunAsync(session, Config() with { Provider = provider }, "NEVER SEND", _ => { }, () => true, CancellationToken.None));
        Assert.False(session.IsRunning);
        Assert.Empty(session.Inputs);
    }

    [Theory]
    [InlineData("tool")]
    [InlineData("approval")]
    [InlineData("question")]
    [InlineData("agent")]
    [InlineData("limit")]
    public async Task A_head_attempting_actions_or_hitting_a_limit_is_stopped_and_never_returns_a_success(string kind)
    {
        var session = new HandoffSession();
        var run = TextOnlyAccountHead.RunAsync(session, Config(), "Q", _ => { }, () => true, CancellationToken.None);
        var emit = session.OnEvent!;
        emit(kind switch
        {
            "tool" => new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "must not act" }),
            "approval" => new ProviderEvent.Approval(new("allow", ApprovalRequest.Kind.Tool, "must not allow", ValueList<ApprovalRequest.Option>.Empty)),
            "question" => new ProviderEvent.Question(new("question", ValueList<QuestionRequest.Question>.Empty)),
            "agent" => new ProviderEvent.AgentStarted(new() { Id = "head", Description = "must not spawn" }),
            "limit" => new ProviderEvent.UsageLimit(null),
            _ => throw new InvalidOperationException(),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.True(session.Stopped);
        Assert.Null(session.OnEvent);
    }

    [Fact]
    public async Task Revocation_during_start_prevents_send_and_cancel_releases_a_pending_turn()
    {
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HandoffSession { StartScript = () => started.Task };
        var allowed = true;
        var run = TextOnlyAccountHead.RunAsync(session, Config(), "Q", _ => { }, () => allowed, CancellationToken.None);
        allowed = false; started.SetResult("private-id");
        await Assert.ThrowsAsync<InvalidOperationException>(() => run);
        Assert.Empty(session.Inputs);
        Assert.True(session.Stopped);
        var pending = new HandoffSession();
        using var cancel = new CancellationTokenSource();
        var turn = TextOnlyAccountHead.RunAsync(pending, Config(), "Q", _ => { }, () => true, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        Assert.True(pending.Stopped);
    }

    private static SessionConfiguration Config() => new()
    {
        Provider = ProviderKind.Deepseek, AccountId = Guid.NewGuid(), WorkingDirectory = SwiftURL.Create("marrow-native-path:/offline")!,
        Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
    };
}
