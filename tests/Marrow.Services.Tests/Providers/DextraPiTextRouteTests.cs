using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Providers;
[Trait("Decision", "ED-NEW-dextra-open-7")]
public sealed class DextraPiTextRouteTests
{
    [Fact]
    public void Pi_proves_its_current_empty_tool_set_before_the_question_then_returns_text_on_the_selected_model()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("dextra-text-proof");
        script.Reply(0, """{"type":"response","id":"droppy-1","data":{"sessionId":"private","model":{"contextWindow":128000}}}""");
        script.Reply(1, """{"type":"response","id":"droppy-2","data":{"commands":[{"name":"marrow-text-only-proof"}]}}""");
        script.Reply(2, """{"type":"response","id":"droppy-3","success":true,"data":{"disposition":"handled"}}""");
        script.Reply(3, """{"type":"response","id":"droppy-4","data":{"messages":[{"role":"custom","customType":"marrow-text-only-proof","content":"{{TEXT_NONCE}}","details":{"tools":[]}}]}}""");
        script.Reply(4,
            """{"type":"message_start","message":{"role":"assistant"}}""",
            """{"type":"message_end","message":{"role":"assistant","content":[{"type":"text","text":"answer"}]}}""",
            """{"type":"agent_end"}""");
        var folder = Path.GetDirectoryName(script.ExecutablePath)!;
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Pi, Model = "openai/selected-model",
            Executable = SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\','/'))!,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Replace('\\','/'))!,
            Environment = new ValueMap<string,string>([script.EnvironmentEntry]), RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var session = new PiSession(config, Path.Combine(folder, "support"));
        Assert.Equal("answer", runtime.Loop.Run(() => TextOnlyAccountHead.RunAsync(session, config, "QUESTION-SENTINEL", _ => { }, () => true, CancellationToken.None), TimeSpan.FromSeconds(30)));
        Assert.False(session.IsRunning);
        var args = script.Arguments()!;
        Assert.Contains("--no-tools", args); Assert.Contains("--no-extensions", args); Assert.Contains("--no-skills", args);
        Assert.Contains("selected-model", args);
        var lines = script.StdinLines();
        Assert.Equal(5, lines.Count);
        Assert.Contains("marrow-text-only-proof", lines[2], StringComparison.Ordinal);
        Assert.Contains("get_messages", lines[3], StringComparison.Ordinal);
        Assert.Contains("QUESTION-SENTINEL", lines[4], StringComparison.Ordinal);
    }
}
