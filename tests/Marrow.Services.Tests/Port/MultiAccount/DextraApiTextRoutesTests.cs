using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.MultiAccount;
[Trait("Decision", "ED-NEW-dextra-open-7")]
public sealed class DextraApiTextRoutesTests
{
    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.OpenRouter)]
    [InlineData(ProviderKind.Local)]
    public void Every_API_route_sends_one_private_round_with_no_tools_and_keeps_the_selected_model(ProviderKind provider)
    {
        using var folder = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var config = new SessionConfiguration
        {
            Provider = provider, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Root.Replace('\\','/'))!,
            Model = "selected-model", ApiKey = "fixture-key", Environment = ValueMap<string,string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var session = new OfflineOpinionApiSession(config);
        var receipts = new List<TokenSpend>();
        var answer = runtime.Loop.Run(() => TextOnlyAccountHead.RunAsync(session, config, "QUESTION-SENTINEL", _ => { }, () => true, CancellationToken.None, receipts.Add));
        Assert.Equal("Opinion text", answer);
        var payload = Assert.Single(session.Payloads);
        Assert.DoesNotContain("tools", payload.Keys);
        Assert.DoesNotContain("tool_choice", payload.Keys);
        Assert.Equal("selected-model", payload["model"].JSONValueString);
        Assert.Single(receipts);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Claude_uses_its_acknowledged_empty_agent_tool_set_and_terminates_after_the_answer()
    {
        using var rig = new ClaudePrimingRig();
        var answer = rig.Runtime.Loop.Run(() => TextOnlyAccountHead.RunAsync(rig.Session, rig.Configuration, "QUESTION-SENTINEL", _ => { }, () => true, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.True(rig.Process!.Terminated);
        var index = Array.IndexOf(rig.Process.Arguments, "--agents");
        Assert.True(index >= 0);
        var agent = Marrow.Core.Support.JSONValue.Parse(rig.Process.Arguments[index+1])![rig.Process.Agent]!;
        Assert.Empty(agent["tools"]!.JSONValueArray!);
        Assert.Contains(agent["disallowedTools"]!.JSONValueArray!, t => t.JSONValueString == "*");
    }
}
