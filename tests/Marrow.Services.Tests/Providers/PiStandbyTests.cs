using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// OD-0116, OD-0122, ED-439, research section 2: Pi holds the next account's context through the app's own load extension with `triggerTurn: false`, behind the
// existing tool gate (finalReport blocks every tool). Every test spawns the compiled offline stand-in (R-TEST-8, R-TEST-10); no real CLI, network or model.
[Trait("Rule", "R-TEST-4")]
public sealed class PiStandbyTests
{
    private const string Commands = "{\"commands\":[{\"name\":\"droppy-load\",\"description\":\"Marrow\",\"source\":\"extension\"}]}";
    private const string Handled = "{\"type\":\"response\",\"id\":\"droppy-3\",\"command\":\"prompt\",\"success\":true,\"data\":{\"disposition\":\"handled\"}}";

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static SessionConfiguration Config(PiScript script) => new()
    {
        Provider = ProviderKind.Pi,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
        Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
    };

    private static (PiSession Session, string SessionFile, List<ProviderEvent> Events) Make(PiScript script, string commands = Commands)
    {
        var folder = Path.GetDirectoryName(script.ExecutablePath)!;
        var sessionFile = Path.Combine(folder, "session-file.jsonl");
        File.WriteAllText(sessionFile, "{}");
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{\"sessionFile\":" + JsonSerializer.Serialize(sessionFile) + ",\"model\":{\"contextWindow\":128000}}}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":" + commands + "}");
        var session = new PiSession(Config(script), Path.Combine(folder, "local-app-data"));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, sessionFile, events);
    }

    private static string Messages(string text, string customType = "droppy-handoff") =>
        "{\"type\":\"response\",\"id\":\"droppy-4\",\"command\":\"get_messages\",\"success\":true,\"data\":{\"messages\":[{\"role\":\"user\",\"content\":\"hello\"},{\"role\":\"custom\",\"customType\":\""
        + customType + "\",\"content\":" + JsonSerializer.Serialize(text) + "}]}}";

    private static string WaitForLine(PiScript script, int index)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var lines = script.StdinLines();
            if (lines.Count > index) return lines[index];
            Thread.Sleep(10);
        }
        throw new TimeoutException($"Stdin line {index} never arrived.");
    }

    private static void Await(TestRuntime runtime, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            runtime.RunUntilIdle();
            Thread.Sleep(10);
        }
    }

    private static TurnInput Input(string text) => new()
    {
        Text = text, Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };

    [Fact]
    public void The_standby_stores_the_context_on_the_idle_session_with_no_turn_and_the_gate_blocks_every_tool_until_the_handover()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("standby-happy");
        var (session, sessionFile, events) = Make(script);
        script.Reply(2, Handled);
        script.Reply(3, Messages("recorded context"));
        Assert.Equal(sessionFile, runtime.Loop.Run(() => session.StartInjectedStandby(CancellationToken.None), TimeSpan.FromSeconds(30)));
        var arguments = script.Arguments()!;
        var extension = arguments[arguments.ToList().LastIndexOf("-e") + 1];
        Assert.EndsWith("droppy-load.ts", extension, StringComparison.Ordinal);
        Assert.Contains("triggerTurn: false", File.ReadAllText(extension), StringComparison.Ordinal);
        var gate = JsonDocument.Parse(File.ReadAllText(script.Environment("DROPPY_CODE_PI_GATE")!)).RootElement;
        Assert.True(gate.GetProperty("finalReport").GetBoolean());

        runtime.Loop.Run(() => session.InjectStandbyHistory("recorded context", CancellationToken.None), TimeSpan.FromSeconds(30));
        var prompt = JsonDocument.Parse(WaitForLine(script, 2)).RootElement;
        Assert.Equal("prompt", prompt.GetProperty("type").GetString());
        Assert.StartsWith("/droppy-load ", prompt.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("get_messages", JsonDocument.Parse(WaitForLine(script, 3)).RootElement.GetProperty("type").GetString());
        Assert.Null(session.StandbyViolation);
        Assert.DoesNotContain(events, value => value is ProviderEvent.TurnStarted);

        // The standby runs no turn.
        var early = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Send(Input("too early")), TimeSpan.FromSeconds(30)));
        Assert.Contains("waiting", early.ErrorDescription, StringComparison.OrdinalIgnoreCase);

        // The handover: the user's own turn, with the gate open again.
        runtime.Loop.Run(() => session.EndStandbyAsync(CancellationToken.None), TimeSpan.FromSeconds(30));
        runtime.Loop.Run(() => session.Send(Input("the user's message")), TimeSpan.FromSeconds(30));
        var turn = JsonDocument.Parse(WaitForLine(script, 4)).RootElement;
        Assert.Equal("prompt", turn.GetProperty("type").GetString());
        Assert.Equal("the user's message", turn.GetProperty("message").GetString());
        gate = JsonDocument.Parse(File.ReadAllText(script.Environment("DROPPY_CODE_PI_GATE")!)).RootElement;
        Assert.False(gate.GetProperty("finalReport").GetBoolean());
    }

    [Theory]
    [InlineData("{\"type\":\"agent_start\"}", "agent_start")]
    [InlineData("{\"type\":\"tool_execution_start\",\"toolCallId\":\"t1\",\"toolName\":\"bash\"}", "tool_execution_start")]
    [InlineData("{\"type\":\"extension_ui_request\",\"id\":\"q1\",\"method\":\"select\",\"title\":\"droppy-gate {}\"}", "extension_ui_request")]
    public void Any_agent_or_tool_activity_on_the_waiting_session_stops_it_and_never_reaches_the_conversation(string line, string expected)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("standby-violation-" + expected);
        var (session, sessionFile, events) = Make(script);
        script.Reply(2, Handled + "\n" + line);
        script.Reply(3, Messages("recorded context"));
        var reported = new List<string>();
        session.OnStandbyViolation = reported.Add;
        runtime.Loop.Run(() => session.StartInjectedStandby(CancellationToken.None), TimeSpan.FromSeconds(30));
        runtime.Loop.Run(async () =>
        {
            try { await session.InjectStandbyHistory("recorded context", CancellationToken.None); }
            catch (StandbyUnavailableException) { /* The violation ends the load. */ }
        }, TimeSpan.FromSeconds(30));
        Await(runtime, () => session.StandbyViolation is not null);
        Assert.Equal(expected, session.StandbyViolation);
        Assert.Equal([expected], reported);
        Await(runtime, () => !session.IsRunning && script.SawEndOfInput);
        Assert.False(session.IsRunning);
        Assert.DoesNotContain(events, value => value is ProviderEvent.TurnStarted or ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.ToolStarted);
        Assert.False(File.Exists(sessionFile), "the discarded standby's session file is deleted");
    }

    [Fact]
    public void A_Pi_that_did_not_load_the_extension_command_has_no_standby_and_is_remembered()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("standby-no-command");
        var (session, sessionFile, _) = Make(script, "{\"commands\":[{\"name\":\"other\"}]}");
        Assert.True(runtime.Loop.Run(() => session.SupportsStandbyInjection(CancellationToken.None), TimeSpan.FromSeconds(30)));
        Assert.Throws<StandbyUnavailableException>(() => runtime.Loop.Run(() => session.StartInjectedStandby(CancellationToken.None), TimeSpan.FromSeconds(30)));
        Await(runtime, () => !session.IsRunning && script.SawEndOfInput);
        Assert.False(File.Exists(sessionFile));
        Assert.DoesNotContain("prompt", script.StdinLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()));
        // The same executable is not tried again: the host knows before it asks the user anything.
        var again = new PiSession(Config(script), Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "local-app-data"));
        Assert.False(runtime.Loop.Run(() => again.SupportsStandbyInjection(CancellationToken.None), TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData("not-handled")]
    [InlineData("failed")]
    [InlineData("missing")]
    [InlineData("different-text")]
    public void A_load_that_cannot_be_proven_from_the_session_fails_the_standby(string failure)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("standby-unproven-" + failure);
        var (session, sessionFile, _) = Make(script);
        script.Reply(2, failure switch
        {
            "not-handled" => "{\"type\":\"response\",\"id\":\"droppy-3\",\"command\":\"prompt\",\"success\":true,\"data\":{\"disposition\":\"queued\"}}",
            "failed" => "{\"type\":\"response\",\"id\":\"droppy-3\",\"command\":\"prompt\",\"success\":false,\"error\":\"nope\"}",
            _ => Handled,
        });
        script.Reply(3, failure switch
        {
            "missing" => Messages("recorded context", customType: "somebody-else"),
            "different-text" => Messages("something else entirely"),
            _ => Messages("recorded context"),
        });
        runtime.Loop.Run(() => session.StartInjectedStandby(CancellationToken.None), TimeSpan.FromSeconds(30));
        Assert.Throws<StandbyUnavailableException>(() => runtime.Loop.Run(() => session.InjectStandbyHistory("recorded context", CancellationToken.None), TimeSpan.FromSeconds(30)));
        Await(runtime, () => !session.IsRunning);
        Assert.False(session.IsRunning);
        Assert.False(File.Exists(sessionFile));
    }

    [Fact]
    public void Cancelling_the_standby_stops_the_process_and_deletes_its_session_file_without_a_turn()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new PiScript("standby-cancel");
        var (session, sessionFile, _) = Make(script);
        script.Reply(2, Handled);
        script.Reply(3, Messages("recorded context"));
        runtime.Loop.Run(() => session.StartInjectedStandby(CancellationToken.None), TimeSpan.FromSeconds(30));
        runtime.Loop.Run(() => session.InjectStandbyHistory("recorded context", CancellationToken.None), TimeSpan.FromSeconds(30));
        runtime.Loop.Run(() => session.DiscardStandby(), TimeSpan.FromSeconds(30));
        Assert.False(session.IsRunning);
        Await(runtime, () => script.SawEndOfInput);
        Assert.True(script.SawEndOfInput);
        Assert.False(File.Exists(sessionFile));
        Assert.Equal(["get_state", "get_commands", "prompt", "get_messages"], script.StdinLines().Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()));
    }

    [Fact]
    public void The_load_extension_is_the_apps_own_file_next_to_the_gate_and_leaves_the_gate_source_alone()
    {
        Assert.Contains("registerCommand(\"droppy-load\"", PiCLI.LoadExtensionSource, StringComparison.Ordinal);
        Assert.Contains("sendMessage(", PiCLI.LoadExtensionSource, StringComparison.Ordinal);
        Assert.Contains("triggerTurn: false", PiCLI.LoadExtensionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("sendUserMessage", PiCLI.LoadExtensionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("droppy-load", PiCLI.ExtensionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", PiCLI.LoadExtensionSource, StringComparison.Ordinal);
    }
}
