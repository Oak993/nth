using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

// OD-0116, ED-439, research section 4: a warm start opens the destination's process and session early and sends nothing; the context goes as the first real turn.
[Trait("Rule", "R-TEST-4")]
public sealed class WarmStandbyTests
{
    [Fact]
    public async Task A_warm_standby_opens_the_session_and_sends_nothing_while_benign_session_events_change_nothing()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession { SessionId = "warm-session" };
        var warm = await WarmStandby.StartAsync(session, Config(folder.Path), CancellationToken.None);
        Assert.Equal("warm-session", warm.SessionId);
        Assert.True(session.IsRunning);
        session.OnEvent!(new ProviderEvent.SessionReady("warm-session"));
        session.OnEvent!(new ProviderEvent.Models(ValueList<ModelOption>.Empty, null));
        session.OnEvent!(new ProviderEvent.Commands(ValueList<SlashCommand>.Empty));
        session.OnEvent!(new ProviderEvent.Title("a title"));
        session.OnEvent!(new ProviderEvent.ModeChanged(InteractionMode.Build));
        Assert.Null(warm.FaultReason);
        Assert.True(warm.IsHealthy);
        Assert.Empty(session.Inputs);
        Assert.False(session.Stopped);
    }

    [Theory]
    [InlineData("turn")]
    [InlineData("message")]
    [InlineData("reasoning")]
    [InlineData("tool")]
    [InlineData("approval")]
    [InlineData("question")]
    [InlineData("agent")]
    [InlineData("exit")]
    public async Task Any_agent_message_tool_or_permission_request_before_the_switch_stops_the_session_and_it_cannot_be_adopted(string activity)
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        var warm = await WarmStandby.StartAsync(session, Config(folder.Path), CancellationToken.None);
        session.OnEvent!(activity switch
        {
            "turn" => new ProviderEvent.TurnStarted(null),
            "message" => new ProviderEvent.MessageDelta("m", "hello"),
            "reasoning" => new ProviderEvent.ReasoningDelta("r", "thinking"),
            "tool" => new ProviderEvent.ToolStarted("t", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "must not run" }),
            "approval" => new ProviderEvent.Approval(new("a", ApprovalRequest.Kind.Tool, "must not allow", ValueList<ApprovalRequest.Option>.Empty)),
            "question" => new ProviderEvent.Question(new("q", ValueList<QuestionRequest.Question>.Empty)),
            "agent" => new ProviderEvent.AgentStarted(new() { Id = "head", Description = "a head" }),
            _ => new ProviderEvent.Exited("gone"),
        });
        Assert.NotNull(warm.FaultReason);
        Assert.False(warm.IsHealthy);
        Assert.True(session.Stopped);
        Assert.Throws<InvalidOperationException>(() => warm.Take());
    }

    [Fact]
    public async Task Taking_the_session_hands_over_ownership_and_stopping_afterwards_leaves_it_running()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        var warm = await WarmStandby.StartAsync(session, Config(folder.Path), CancellationToken.None);
        Assert.Same(session, warm.Take());
        Assert.Null(session.OnEvent);
        warm.Stop();
        Assert.False(session.Stopped);
        Assert.Throws<InvalidOperationException>(() => warm.Take());
    }

    [Fact]
    public async Task Stopping_releases_a_warm_session_that_was_never_taken()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        var warm = await WarmStandby.StartAsync(session, Config(folder.Path), CancellationToken.None);
        warm.Stop();
        warm.Stop();
        Assert.True(session.Stopped);
        Assert.Empty(session.Inputs);
    }

    [Fact]
    public async Task A_failed_start_throws_and_a_cancelled_start_also_stops_a_late_successful_one()
    {
        using var folder = new AccountTestFolder();
        await Assert.ThrowsAsync<IOException>(() => WarmStandby.StartAsync(new HandoffSession { FailStart = true }, Config(folder.Path), CancellationToken.None));
        var start = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HandoffSession { StartScript = () => start.Task };
        using var cancel = new CancellationTokenSource();
        var starting = WarmStandby.StartAsync(session, Config(folder.Path), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        start.SetResult("late");
        await session.StartFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (session.IsRunning && DateTimeOffset.UtcNow < deadline) await Task.Delay(10);
        Assert.False(session.IsRunning);
    }

    private static SessionConfiguration Config(string directory) => new()
    {
        Provider = ProviderKind.Copilot, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + directory.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty, Model = "offline-model", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
