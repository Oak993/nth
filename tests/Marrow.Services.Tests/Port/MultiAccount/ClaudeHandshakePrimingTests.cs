using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.MultiAccount;
[Trait("Rule", "R-TEST-4")]
public sealed class ClaudeHandshakePrimingTests
{
    [Fact]
    public void Text_only_CLI_priming_restores_normal_tools_in_the_same_acknowledged_session()
    {
        using var rig = new ClaudePrimingRig();
        rig.Runtime.Loop.Run(async () =>
        {
            await using var standby = await rig.Start();
            await ClaudePrimingRig.Prime(standby);
            var process = rig.Process!;
            Assert.Equal("actual-native-session", standby.SessionId);
            Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
            Assert.Single(process.Arguments, value => value == "--settings");
            var i = Array.IndexOf(process.Arguments, "--agents");
            Assert.True(i >= 0);
            var definition = JSONValue.Parse(process.Arguments[i + 1])![process.Agent]!;
            Assert.Empty(definition["tools"]!.JSONValueArray!);
            Assert.Equal("", definition["prompt"]!.JSONValueString);
            Assert.Contains(definition["disallowedTools"]!.JSONValueArray!, item => item.JSONValueString == "*");
            Assert.DoesNotContain("--tools", process.Arguments);
            Assert.DoesNotContain("--bare", process.Arguments);
            await ClaudePrimingRig.Restore(standby);
            Assert.Same(rig.Session, standby.TakeSession());
            Assert.False(process.Terminated);
            var apply = Assert.Single(process.Sent, value => value["request"]?["subtype"]?.JSONValueString == "apply_flag_settings");
            Assert.IsType<JSONValue.Null>(apply["request"]!["settings"]!["agent"]);
            Assert.IsType<JSONValue.Null>(apply["request"]!["settings"]!["disableAllHooks"]);
            Assert.Null(apply["request"]!["settings"]!["fastMode"]);
            var events = new List<ProviderEvent>();
            rig.Session.OnEvent = events.Add;
            rig.Session.Handle(JSONValue.Parse("""{"type":"assistant","message":{"id":"normal","content":[{"type":"tool_use","id":"normal-read","name":"Read","input":{"file_path":"Config.cs"}}]}}""")!);
            Assert.Contains(events, value => value is ProviderEvent.ToolStarted);
            Assert.False(process.Terminated);
        }, TimeSpan.FromSeconds(8));
    }
    [Theory]
    [InlineData("version")]
    [InlineData("schema")]
    [InlineData("agent")]
    [InlineData("hooks")]
    [InlineData("managed-hooks")]
    [InlineData("identity")]
    [InlineData("init-version")]
    [InlineData("tools")]
    [InlineData("tool-request")]
    public void Unverified_CLI_policy_or_session_identity_cannot_receive_or_adopt_context(string failure)
    {
        using var rig = new ClaudePrimingRig(failure);
        rig.Runtime.Loop.Run(async () =>
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                await using var standby = await rig.Start();
                await ClaudePrimingRig.Prime(standby);
                await ClaudePrimingRig.Restore(standby);
                standby.TakeSession();
            });
            Assert.NotNull(error);
            if (failure is "version" or "schema" or "agent" or "hooks" or "managed-hooks")
                Assert.DoesNotContain(rig.Process?.Sent ?? [], value => value["type"]?.JSONValueString == "user");
            if (failure != "version") Assert.True(rig.Process?.Terminated);
            if (failure == "tool-request")
                Assert.Contains(rig.Process!.Sent, value => value["response"]?["response"]?["behavior"]?.JSONValueString == "deny");
        }, TimeSpan.FromSeconds(8));
    }
    [Theory]
    [InlineData("restore-rejected")]
    [InlineData("restore-latched")]
    public void Rejected_or_latched_CLI_restoration_never_transfers_the_private_session(string failure)
    {
        using var rig = new ClaudePrimingRig();
        rig.Runtime.Loop.Run(async () =>
        {
            await using var standby = await rig.Start();
            await ClaudePrimingRig.Prime(standby);
            rig.Process!.Failure = failure;
            await Assert.ThrowsAnyAsync<Exception>(() => ClaudePrimingRig.Restore(standby));
            Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
            Assert.True(rig.Process.Terminated);
        }, TimeSpan.FromSeconds(8));
    }
    [Theory]
    [InlineData("restore-hook-latched")]
    [InlineData("restore-wrong-agent")]
    [InlineData("restore-mode-rejected")]
    public void CLI_restoration_preserves_the_original_agent_hooks_and_permission_mode(string failure)
    {
        using var rig = new ClaudePrimingRig();
        rig.Runtime.Loop.Run(async () =>
        {
            await using var standby = await rig.Start();
            await ClaudePrimingRig.Prime(standby);
            rig.Process!.Failure = failure;
            await Assert.ThrowsAnyAsync<Exception>(() => ClaudePrimingRig.Restore(standby));
            Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
            Assert.True(rig.Process.Terminated);
        }, TimeSpan.FromSeconds(8));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unanswered_CLI_policy_controls_release_the_private_session_at_the_bounded_deadline(bool restore)
    {
        using var rig = new ClaudePrimingRig(restore ? "" : "start-silent");
        Marrow.Services.Port.MultiAccount.HandshakeStandbySession? standby = null;
        Task pending;
        if (restore)
        {
            rig.Runtime.Loop.Run(async () => { standby = await rig.Start(); await ClaudePrimingRig.Prime(standby); }, TimeSpan.FromSeconds(8));
            rig.Process!.Failure = "restore-silent";
            pending = rig.Runtime.OnMain(() => ClaudePrimingRig.Restore(standby!));
        }
        else pending = rig.Runtime.OnMain(() => rig.Start());
        var request = restore ? "apply_flag_settings" : "get_settings";
        rig.Runtime.Loop.RunUntil(() => rig.Process?.Sent.Any(value => value["request"]?["subtype"]?.JSONValueString == request) == true, TimeSpan.FromSeconds(8));
        rig.Runtime.Advance(TimeSpan.FromSeconds(19));
        Assert.False(pending.IsCompleted);
        rig.Runtime.Advance(TimeSpan.FromSeconds(1));
        rig.Runtime.Loop.Run(async () => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending), TimeSpan.FromSeconds(8));
        Assert.True(rig.Process!.Terminated);
        if (standby is not null) Assert.Throws<InvalidOperationException>(() => standby.TakeSession());
        else Assert.DoesNotContain(rig.Process.Sent, value => value["type"]?.JSONValueString == "user");
    }
}
