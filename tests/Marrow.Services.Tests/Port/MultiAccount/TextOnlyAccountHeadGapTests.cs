using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class TextOnlyAccountHeadGapTests
{
    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Pi)]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Grok)]
    [InlineData(ProviderKind.Devin)]
    public async Task No_generic_or_injected_session_can_claim_a_verified_CLI_text_head(ProviderKind service)
    {
        var session = new HandoffSession();
        await Assert.ThrowsAsync<NotSupportedException>(() => TextOnlyAccountHead.RunAsync(session, Config(service), "NEVER SEND", _ => { }, () => true, CancellationToken.None));
        Assert.Empty(session.Inputs);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task An_action_during_startup_fails_before_the_private_question_is_sent()
    {
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HandoffSession { StartScript = () => started.Task };
        var head = TextOnlyAccountHead.RunAsync(session, Config(ProviderKind.Deepseek), "NEVER SEND", _ => { }, () => true, CancellationToken.None);
        session.OnEvent!(new ProviderEvent.ToolStarted("write", new() { ToolCallKind = ToolCall.Kind.Edit, Title = "unsafe" }));
        started.SetResult("fresh");
        await Assert.ThrowsAsync<InvalidOperationException>(() => head);
        Assert.Empty(session.Inputs);
        Assert.True(session.Stopped);
    }

    private static SessionConfiguration Config(ProviderKind service) => new()
    {
        Provider = service, WorkingDirectory = SwiftURL.Create("marrow-native-path:/offline")!,
        Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };
}
