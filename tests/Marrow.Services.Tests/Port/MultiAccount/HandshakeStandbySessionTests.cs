using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class HandshakeStandbySessionTests
{
    [Fact]
    public async Task Dispatch_acknowledgement_is_not_a_completed_readback_and_empty_completion_preserves_streamed_text()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receiving = standby.ReceiveAsync(Package(), true, CancellationToken.None);
        Assert.False(receiving.IsCompleted);
        Assert.Single(session.Inputs);
        Assert.True(session.Inputs[0].IsFinalReport);
        session.OnEvent!(new ProviderEvent.MessageDelta("one", "Café 17"));
        session.OnEvent!(new ProviderEvent.MessageCompleted("one", ""));
        session.OnEvent!(new ProviderEvent.MessageCompleted("two", "Second segment"));
        Assert.False(receiving.IsCompleted);
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        Assert.Equal("Café 17\nSecond segment", await receiving);
        var adopted = standby.TakeSession();
        Assert.Same(session, adopted);
        Assert.Null(session.OnEvent);
        await standby.DisposeAsync();
        Assert.False(session.Stopped);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("empty")]
    [InlineData("exit")]
    [InlineData("limit")]
    [InlineData("tool")]
    [InlineData("approval")]
    [InlineData("question")]
    public async Task Incomplete_or_unsafe_priming_cannot_be_adopted(string failure)
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receiving = standby.ReceiveAsync(Package(), true, CancellationToken.None);
        var emit = session.OnEvent!;
        emit(failure switch
        {
            "failed" => new ProviderEvent.TurnCompleted(TurnStatus.Failed, "offline refusal"),
            "empty" => new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            "exit" => new ProviderEvent.Exited("offline exit"),
            "limit" => new ProviderEvent.UsageLimit(null),
            "tool" => new ProviderEvent.ToolStarted("write", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "must not run" }),
            "approval" => new ProviderEvent.Approval(new("allow", ApprovalRequest.Kind.Tool, "must not allow", ValueList<ApprovalRequest.Option>.Empty)),
            "question" => new ProviderEvent.Question(new("question", ValueList<QuestionRequest.Question>.Empty)),
            _ => throw new InvalidOperationException(),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiving);
        Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
        Assert.True(session.Stopped);
    }

    [Fact]
    public async Task Cancellation_releases_a_pending_standby_and_never_transfers_ownership()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var receiving = standby.ReceiveAsync(Package(), true, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
        Assert.True(session.Stopped);
        Assert.Null(session.OnEvent);
        Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
    }

    [Theory]
    [InlineData(Bring.Full)]
    [InlineData(Bring.MarrowSummary)]
    [InlineData(Bring.AiSummary)]
    public async Task Bring_sends_the_actual_selected_payload_and_gap_fill_waits_for_its_own_completed_turn(Bring bring)
    {
        using var folder = new AccountTestFolder();
        var file = Path.Combine(folder.Path, "archive.json");
        Directory.CreateDirectory(folder.Path);
        var full = "{\"all\":\"" + new string('x', 50_000) + "LAST-DETAIL\"}";
        File.WriteAllText(file, full);
        var summary = new HandoffSummary("MECHANICAL-SUMMARY", file, ["Config.cs"]);
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receive = standby.ReceiveAsync(new(bring, summary, "ACTUAL-AI-BRIEFING"), true, CancellationToken.None);
        var expected = bring == Bring.Full ? full : bring == Bring.MarrowSummary ? summary.Text : "ACTUAL-AI-BRIEFING";
        Assert.Contains(expected, session.Inputs[0].Text, StringComparison.Ordinal);
        Assert.Contains("ACTUAL-AI-BRIEFING", session.Inputs[0].Text, StringComparison.Ordinal);
        if (bring != Bring.Full) Assert.DoesNotContain("LAST-DETAIL", session.Inputs[0].Text, StringComparison.Ordinal);
        session.OnEvent!(new ProviderEvent.MessageCompleted("reply", "Understood."));
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        await receive;
        var gaps = standby.FillGapsAsync(["Config.cs", "Keep 17"], CancellationToken.None);
        Assert.False(gaps.IsCompleted);
        Assert.Contains("Config.cs", session.Inputs[1].Text, StringComparison.Ordinal);
        Assert.Contains("Keep 17", session.Inputs[1].Text, StringComparison.Ordinal);
        session.OnEvent!(new ProviderEvent.MessageCompleted("reply", "Config.cs and Keep 17"));
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        await gaps;
    }

    [Fact]
    public async Task AI_summary_without_a_real_briefing_is_refused_instead_of_substituting_Marrows_summary()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => standby.ReceiveAsync(Package() with { Bring = Bring.AiSummary }, true, CancellationToken.None));
        Assert.Empty(session.Inputs);
    }

    [Fact]
    public async Task Disposing_a_pending_readback_releases_its_wait_immediately()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receiving = standby.ReceiveAsync(Package(), true, CancellationToken.None);
        await standby.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiving.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(session.Stopped);
    }

    [Fact]
    public async Task A_standby_that_exits_after_priming_cannot_transfer_a_dead_session()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receiving = standby.ReceiveAsync(Package(), true, CancellationToken.None);
        session.OnEvent!(new ProviderEvent.MessageCompleted("reply", "Ready"));
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        await receiving;
        session.OnEvent!(new ProviderEvent.Exited("exit between priming and switch"));
        Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
    }

    [Fact]
    public async Task Unsupported_providers_are_rejected_before_any_priming_dispatch_or_process_start()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await Assert.ThrowsAsync<NotSupportedException>(() => HandshakeStandbySession.StartAsync(session,
            Config(folder.Path) with { Provider = ProviderKind.Codex }, CancellationToken.None));
        Assert.False(session.IsRunning);
        Assert.Empty(session.Inputs);
    }

    [Fact]
    public async Task Priming_after_ownership_transfer_cannot_stop_the_runtimes_session()
    {
        using var folder = new AccountTestFolder();
        var session = new HandoffSession();
        await using var standby = await HandshakeStandbySession.StartAsync(session, Config(folder.Path), CancellationToken.None);
        var receiving = standby.ReceiveAsync(Package(), true, CancellationToken.None);
        session.OnEvent!(new ProviderEvent.MessageCompleted("reply", "Ready"));
        session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        await receiving;
        Assert.Same(session, standby.TakeSession());
        await Assert.ThrowsAsync<InvalidOperationException>(() => standby.FillGapsAsync(["late"], CancellationToken.None));
        Assert.False(session.Stopped);
        Assert.True(session.IsRunning);
    }

    [Fact]
    public async Task Cancellation_during_start_also_stops_a_late_successful_start()
    {
        using var folder = new AccountTestFolder();
        var start = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HandoffSession { StartScript = () => start.Task };
        using var cancel = new CancellationTokenSource();
        var starting = HandshakeStandbySession.StartAsync(session, Config(folder.Path), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        start.SetResult("late-session");
        await session.StartFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Allow the observer of the same start task to finish its cleanup.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (session.IsRunning && DateTimeOffset.UtcNow < deadline) await Task.Delay(10);
        Assert.False(session.IsRunning);
        Assert.True(session.StopCalls >= 2);
    }

    private static HandoffPackage Package() => new(Bring.MarrowSummary, new("Exact context", null, ["Config.cs"]), null);
    private static SessionConfiguration Config(string directory) => new()
    {
        Provider = ProviderKind.Deepseek, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + directory.Replace('\\', '/'))!,
        Environment = ValueMap<string, string>.Empty, Model = "offline-model", RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
