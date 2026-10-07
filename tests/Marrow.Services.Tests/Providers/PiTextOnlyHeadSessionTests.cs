using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Windows compiled offline stand-in only. These must run on Windows before RR-49 acceptance.
[Trait("Rule", "R-TEST-4")]
public sealed class PiTextOnlyHeadSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_installed_CLI_without_a_valid_guard_or_runtime_proof_never_receives_the_question(bool guardPresent)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("text-head-unverified");
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{\"sessionId\":\"private\"}}");
        var command = guardPresent ? "{\"name\":\"marrow-text-only-proof\"}" : "{\"name\":\"other\"}";
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":{\"commands\":[" + command + "]}}");
        script.Reply(2, "{\"type\":\"response\",\"id\":\"droppy-3\",\"success\":true,\"data\":{\"disposition\":\"handled\"}}");
        script.Reply(3, "{\"type\":\"response\",\"id\":\"droppy-4\",\"data\":{\"messages\":[]}}");
        var folder = Path.GetDirectoryName(script.ExecutablePath)!;
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            Executable = SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Replace('\\', '/'))!,
            Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var session = new PiSession(configuration, Path.Combine(folder, "support"));
        Assert.Throws<NotSupportedException>(() => runtime.Loop.Run(() => session.StartTextOnlyHeadAsync(CancellationToken.None), TimeSpan.FromSeconds(30)));
        Assert.False(session.IsRunning);
        var args = script.Arguments()!;
        Assert.Contains("--no-tools", args);
        Assert.Contains("--no-extensions", args);
        Assert.DoesNotContain("--approve", args);
        Assert.EndsWith("marrow-text-only.mjs", args[args.ToList().IndexOf("-e") + 1], StringComparison.Ordinal);
        Assert.Null(script.Environment("DROPPY_CODE_MCP_BRIDGE"));
        var requests = script.StdinLines().Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()).ToArray();
        Assert.Equal(guardPresent ? new[] { "get_state", "get_commands", "prompt", "get_messages" } : new[] { "get_state", "get_commands" }, requests);
    }
}
