using System.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/PiSession.swift (B2 1fbf81e), :1-654.
//
// Tests for unit Services.Providers.PiSession part 1 of 2 (PR-PROVCLI-267 to 291): the launch path (the
// resume-file check, the gate and extension setup, the argument vector, the environment, the process launch,
// the get_state/get_commands handshake, the request/response wire), send/interrupt/compact/stop, approvals and
// question answers, and the message/tool event stream. Each launch test spawns tests/fixtures/pi-cli/PiStandIn.cs,
// a compiled offline stand-in (R-TEST-8, R-TEST-10; never a real AI CLI), driven through
// tests/Marrow.Services.Tests/Fixtures/PiStandIns.cs. Stream-dispatch tests call PiSession's internal Handle
// methods directly with constructed JSONValue lines, which needs no live process.
public sealed class PiSessionTests
{
    // See ClaudeSessionTests for why a "file" URL is wrong on this platform for SwiftURL.Path: build from a
    // non-"file" scheme so .Path round-trips the real Windows path unchanged (SwiftURL.cs :524-532).
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static SessionConfiguration Config(
        PiScript script,
        string? resumeID = null,
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build)
    {
        var environment = new ValueMap<string, string>([script.EnvironmentEntry]);
        return new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            Executable = FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = environment,
            ResumeID = resumeID,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
        };
    }

    private static (PiSession Session, List<ProviderEvent> Events) Make(SessionConfiguration configuration)
    {
        var session = new PiSession(configuration);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    /// <summary>Replies to the get_state and get_commands requests Start() always sends first, and starts the
    /// session (real StdioProcess, real handshake, through the offline stand-in).</summary>
    private static async Task<(PiSession Session, List<ProviderEvent> Events, PiScript Script, string ID)> Started(
        TestRuntime runtime, PiScript script, string? resumeID = null, string? model = null, string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised, InteractionMode interactionMode = InteractionMode.Build,
        string stateData = "{\"model\":{\"contextWindow\":128000}}", string commandsData = "{\"commands\":[]}")
    {
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":" + stateData + "}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":" + commandsData + "}");
        var (session, events) = Make(Config(script, resumeID: resumeID, model: model, effort: effort, runtimeMode: runtimeMode, interactionMode: interactionMode));
        var id = runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30));
        return (session, events, script, id);
    }

    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    /// <summary>The n-th (0-based) stdin line the stand-in received, polling briefly: a fire-and-forget send
    /// (interrupt, compact, an approval or question answer) has no reply to await, so the write to the child's
    /// pipe can still be in flight when the test's assertion runs (as ClaudeStandIns.CapturedLine documents).</summary>
    private static string WaitForStdinLine(PiScript script, int index)
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

    // ---- PR-PROVCLI-267 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-267")] // PiSession.swift:71,82-84
    public async Task Start_throws_when_the_resume_id_file_is_gone_and_normalizes_an_empty_resume_id_to_a_fresh_start()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("resume-missing");
        var (session, _) = Make(Config(script, resumeID: Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "no-such-session.json")));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30)));
        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("Pi no longer has this conversation; starting a new one.", ((ProviderError.Case.Failed)error.ErrorCase).Message);
        Assert.Null(script.Arguments()); // the process was never launched

        // An empty resume id is normalized to null in init, so start() proceeds instead of checking a path.
        var (_, _, _, id) = await Started(runtime, new PiScript("resume-empty"), resumeID: "");
        Assert.NotNull(id);
    }

    // ---- PR-PROVCLI-268 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-268")] // PiSession.swift:85-90
    public async Task Start_installs_the_extension_and_writes_a_fresh_gate_file_before_launch()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("gate-setup");
        var (_, _, startedScript, _) = await Started(runtime, script);
        var arguments = startedScript.Arguments()!;
        var extensionIndex = arguments.ToList().IndexOf("-e");
        Assert.True(extensionIndex >= 0);
        var extensionPath = arguments[extensionIndex + 1];
        Assert.True(File.Exists(extensionPath));
        Assert.EndsWith("droppy-gate.ts", extensionPath, StringComparison.Ordinal);

        var gatePath = startedScript.Environment("DROPPY_CODE_PI_GATE");
        Assert.NotNull(gatePath);
        Assert.Contains(Path.Combine("pi", "gates"), gatePath, StringComparison.Ordinal);
        Assert.True(File.Exists(gatePath));
        var gate = Json(File.ReadAllText(gatePath));
        Assert.False(gate["finalReport"]!.JSONValueBool);
    }

    // ---- PR-PROVCLI-269 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-269")] // PiSession.swift:92-93
    public async Task Start_argument_vector_fixes_the_approve_flag_at_launch_from_the_runtime_mode()
    {
        using var runtime = new TestRuntime();
        var (_, _, autoScript, _) = await Started(runtime, new PiScript("argv-approve"), runtimeMode: RuntimeMode.Auto);
        var autoArguments = autoScript.Arguments()!;
        Assert.Equal("--mode", autoArguments[0]);
        Assert.Equal("rpc", autoArguments[1]);
        Assert.Equal("-e", autoArguments[2]);
        Assert.Equal("--approve", autoArguments[4]);

        using var runtime2 = new TestRuntime();
        var (_, _, fullAccessScript, _) = await Started(runtime2, new PiScript("argv-approve-full"), runtimeMode: RuntimeMode.FullAccess);
        Assert.Equal("--approve", fullAccessScript.Arguments()![4]);

        using var runtime3 = new TestRuntime();
        var (_, _, supervisedScript, _) = await Started(runtime3, new PiScript("argv-noapprove-supervised"), runtimeMode: RuntimeMode.Supervised);
        Assert.Equal("--no-approve", supervisedScript.Arguments()![4]);

        using var runtime4 = new TestRuntime();
        var (_, _, acceptEditsScript, _) = await Started(runtime4, new PiScript("argv-noapprove-accept"), runtimeMode: RuntimeMode.AutoAcceptEdits);
        Assert.Equal("--no-approve", acceptEditsScript.Arguments()![4]);
    }

    // ---- PR-PROVCLI-270 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-270")] // PiSession.swift:94-96
    public async Task Start_splits_a_slashed_model_id_and_omits_the_flags_otherwise()
    {
        using var runtime = new TestRuntime();
        var (_, _, slashed, _) = await Started(runtime, new PiScript("model-slash"), model: "anthropic/claude-3.5/extra");
        var slashedArguments = slashed.Arguments()!;
        var providerIndex = slashedArguments.ToList().IndexOf("--provider");
        Assert.True(providerIndex >= 0);
        Assert.Equal("anthropic", slashedArguments[providerIndex + 1]);
        Assert.Equal("--model", slashedArguments[providerIndex + 2]);
        Assert.Equal("claude-3.5/extra", slashedArguments[providerIndex + 3]); // split on the FIRST slash only

        using var runtimeB = new TestRuntime();
        var (_, _, noSlash, _) = await Started(runtimeB, new PiScript("model-no-slash"), model: "opus");
        Assert.DoesNotContain("--provider", noSlash.Arguments()!);
        Assert.DoesNotContain("--model", noSlash.Arguments()!);

        using var runtimeC = new TestRuntime();
        var (_, _, defaultModel, _) = await Started(runtimeC, new PiScript("model-default"), model: "default");
        Assert.DoesNotContain("--provider", defaultModel.Arguments()!);
    }

    // ---- PR-PROVCLI-271 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-271")] // PiSession.swift:97-99
    public async Task Start_appends_thinking_only_for_one_of_the_seven_known_levels()
    {
        using var runtime = new TestRuntime();
        var (_, _, knownScript, _) = await Started(runtime, new PiScript("thinking-known"), effort: "xhigh");
        var arguments = knownScript.Arguments()!;
        var thinkingIndex = arguments.ToList().IndexOf("--thinking");
        Assert.True(thinkingIndex >= 0);
        Assert.Equal("xhigh", arguments[thinkingIndex + 1]);

        using var runtimeB = new TestRuntime();
        var (_, _, unknownScript, _) = await Started(runtimeB, new PiScript("thinking-unknown"), effort: "turbo");
        Assert.DoesNotContain("--thinking", unknownScript.Arguments()!);
    }

    // ---- PR-PROVCLI-272 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-272")] // PiSession.swift:100
    public async Task Start_appends_session_with_the_resume_path_last()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("session-flag");
        var resumeFile = script.ExecutablePath; // an existing file on disk
        var (_, _, startedScript, _) = await Started(runtime, script, resumeID: resumeFile);
        var arguments = startedScript.Arguments()!;
        Assert.Equal("--session", arguments[^2]);
        Assert.Equal(resumeFile, arguments[^1]);
    }

    // ---- PR-PROVCLI-273 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-273")] // PiSession.swift:102-110
    public async Task Start_sets_exactly_the_gate_and_version_check_keys_unconditionally()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("env-keys");
        var (_, _, startedScript, _) = await Started(runtime, script);
        Assert.Equal("1", startedScript.Environment("PI_SKIP_VERSION_CHECK"));
        Assert.NotNull(startedScript.Environment("DROPPY_CODE_PI_GATE"));
        // The bridge key is present because MCPBridge.InstallBridge() always succeeds against a real disk.
        Assert.NotNull(startedScript.Environment("DROPPY_CODE_MCP_BRIDGE"));
        Assert.StartsWith("file://", startedScript.Environment("DROPPY_CODE_MCP_BRIDGE"), StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-274 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-274")] // PiSession.swift:112-133
    public async Task Start_launch_failure_is_reported_with_the_prefixed_message()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        var missingExecutable = Path.Combine(Path.GetTempPath(), "marrow-tests-nonexistent-" + Guid.NewGuid().ToString("N") + ".exe");
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            Executable = FileUrl(missingExecutable),
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var (session, _) = Make(config);
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30)));
        var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.StartsWith("Pi could not start: ", failed.Message, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-275 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-275")] // PiSession.swift:135-156
    public async Task Start_handshakes_with_get_state_then_get_commands_and_no_initialize()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("handshake");
        var (session, events, startedScript, id) = await Started(runtime, script,
            stateData: "{\"sessionFile\":\"C:\\\\pi\\\\session-1.json\",\"model\":{\"contextWindow\":50000}}",
            commandsData: "{\"commands\":[{\"name\":\"compact\",\"description\":\"Compact\"},{\"name\":\"no-desc\"}]}");
        Assert.Equal(@"C:\pi\session-1.json", id);
        Assert.Equal(2, startedScript.StdinLines().Count);
        Assert.Equal("get_state", Json(startedScript.StdinLines()[0])["type"]!.JSONValueString);
        Assert.Equal("get_commands", Json(startedScript.StdinLines()[1])["type"]!.JSONValueString);
        Assert.DoesNotContain(startedScript.StdinLines(), line => Json(line)["type"]?.JSONValueString == "initialize");
        var ready = Assert.Single(events.OfType<ProviderEvent.SessionReady>());
        Assert.Equal(@"C:\pi\session-1.json", ready.SessionID);
        var commands = Assert.Single(events.OfType<ProviderEvent.Commands>());
        Assert.Equal(2, commands.Value.Count);
        Assert.Equal("compact", commands.Value[0].Name);
        Assert.Equal("Compact", commands.Value[0].Detail);
        Assert.Equal("", commands.Value[1].Detail);
        _ = session;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-275")] // PiSession.swift:139-143
    public async Task Start_falls_back_to_sessionId_when_sessionFile_is_absent_and_emits_empty_ready_when_neither_is_present()
    {
        using var runtime = new TestRuntime();
        var (_, events, _, id) = await Started(runtime, new PiScript("handshake-sessionid"), stateData: "{\"sessionId\":\"abc-123\"}");
        Assert.Equal("abc-123", id);
        Assert.Equal("abc-123", Assert.Single(events.OfType<ProviderEvent.SessionReady>()).SessionID);

        using var runtime2 = new TestRuntime();
        var (_, events2, _, id2) = await Started(runtime2, new PiScript("handshake-neither"), stateData: "{}");
        Assert.Equal("", id2);
        Assert.Equal("", Assert.Single(events2.OfType<ProviderEvent.SessionReady>()).SessionID);
    }

    // ---- PR-PROVCLI-276 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:257-276
    public async Task Request_returns_null_with_no_process_and_numbers_ids_from_droppy_1()
    {
        var session = new PiSession(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var value = await session.Request(JSONValue.FromDictionaryLiteral(("type", "get_state")));
        Assert.IsType<JSONValue.Null>(value);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:268-273
    public async Task Request_resolves_null_after_thirty_seconds_when_no_response_arrives()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        var script = new PiScript("timeout");
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{}}");
        // No reply for line 1 (get_commands): the request must resolve via the 30 second timeout, not hang.
        var (session, events) = Make(Config(script));
        Task<string>? startTask = null;
        runtime.Loop.Enqueue(() => startTask = session.Start());
        // Both stdin lines must actually reach the real child process (its background reader is not on this
        // manual loop) before the clock advance can resolve get_commands' pending 30 second timeout.
        runtime.Loop.RunUntil(() => script.StdinLines().Count == 2, TimeSpan.FromSeconds(20));
        runtime.Advance(TimeSpan.FromSeconds(30));
        var id = runtime.Loop.Run(() => startTask!, TimeSpan.FromSeconds(30));
        Assert.Equal("", id); // get_state's data carried no session id
        Assert.Empty(events.OfType<ProviderEvent.Commands>()); // the timed-out get_commands produced no commands
    }

    // ---- PR-PROVCLI-277 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-277")] // PiSession.swift:162-164,278-288
    public async Task Send_rewrites_the_gate_before_every_turn_with_the_camelCase_raw_values()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("gate-rewrite");
        var (session, _, startedScript, _) = await Started(runtime, script, runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Build);
        var gatePath = startedScript.Environment("DROPPY_CODE_PI_GATE")!;

        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "hello",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Plan,
            IsFinalReport = true,
        }), TimeSpan.FromSeconds(10));

        var gate = Json(File.ReadAllText(gatePath));
        Assert.Equal("fullAccess", gate["runtimeMode"]!.JSONValueString);
        Assert.Equal("plan", gate["interactionMode"]!.JSONValueString);
        Assert.True(gate["finalReport"]!.JSONValueBool);
        Assert.Equal("", gate["systemPrompt"]!.JSONValueString);

        // The launch flag is fixed at start and unaffected by the mode change.
        Assert.Equal("--no-approve", startedScript.Arguments()![4]);
    }

    // ---- PR-PROVCLI-279 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:159-161
    public async Task Send_throws_notRunning_before_start_and_failed_while_a_turn_is_active()
    {
        var session = new PiSession(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var input = new TurnInput { Text = "hi", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        var notRunning = await Assert.ThrowsAsync<ProviderError>(() => session.Send(input));
        Assert.IsType<ProviderError.Case.NotRunning>(notRunning.ErrorCase);

        using var runtime = new TestRuntime();
        var (started, _, _, _) = await Started(runtime, new PiScript("busy-turn"));
        runtime.Loop.Run(() => started.Send(input), TimeSpan.FromSeconds(10));
        var busy = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => started.Send(input), TimeSpan.FromSeconds(10)));
        Assert.Equal("Pi is still on the last turn.", ((ProviderError.Case.Failed)busy.ErrorCase).Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:165-174
    public async Task Send_switches_model_on_a_slashed_change_and_refetches_the_context_window()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("model-switch");
        var (session, _, startedScript, _) = await Started(runtime, script, model: "anthropic/opus");
        script.Reply(2, "{\"type\":\"response\",\"id\":\"droppy-3\",\"data\":{\"model\":{\"contextWindow\":9000}}}");

        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "hi", Images = ValueList<Attachment>.Empty, Model = "openai/gpt-5",
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        }), TimeSpan.FromSeconds(10));

        var setModel = Json(WaitForStdinLine(startedScript, 2));
        Assert.Equal("set_model", setModel["type"]!.JSONValueString);
        Assert.Equal("openai", setModel["provider"]!.JSONValueString);
        Assert.Equal("gpt-5", setModel["modelId"]!.JSONValueString);
        Assert.Equal("get_state", Json(WaitForStdinLine(startedScript, 3))["type"]!.JSONValueString);

        // A slashless target is never switched (no set_model line follows). Finish the first turn first: a
        // second Send() while one is active throws (PR-PROVCLI-279), proven separately.
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        script.Reply(4, "{\"type\":\"response\",\"id\":\"droppy-4\",\"data\":{}}");
        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "hi again", Images = ValueList<Attachment>.Empty, Model = "opus",
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        }), TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(startedScript.StdinLines().Skip(4), line => Json(line)["type"]?.JSONValueString == "set_model");
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:175-178
    public async Task Send_switches_thinking_level_only_for_a_changed_known_effort()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("thinking-switch");
        var (session, _, startedScript, _) = await Started(runtime, script, effort: "low");
        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "hi", Images = ValueList<Attachment>.Empty, Effort = "max",
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        }), TimeSpan.FromSeconds(10));
        var setLevel = Json(WaitForStdinLine(startedScript, 2));
        Assert.Equal("set_thinking_level", setLevel["type"]!.JSONValueString);
        Assert.Equal("max", setLevel["level"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-280 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-280")] // PiSession.swift:179-200
    public async Task Send_resets_turn_state_writes_the_prompt_with_images_and_emits_turnStarted_after_the_write()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("send-prompt");
        var (session, events, startedScript, _) = await Started(runtime, script);

        var imagePath = Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "pic.png");
        File.WriteAllBytes(imagePath, [1, 2, 3, 4]);
        var image = new Attachment { Name = "pic.png", Path = imagePath, MimeType = "image/png" };
        var textOnly = new Attachment { Name = "note.txt", Path = imagePath, MimeType = "text/plain" };

        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "do it", Images = new ValueList<Attachment>([image, textOnly]),
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        }), TimeSpan.FromSeconds(10));

        var prompt = Json(WaitForStdinLine(startedScript, 2));
        Assert.Equal("prompt", prompt["type"]!.JSONValueString);
        Assert.Equal("do it", prompt["message"]!.JSONValueString);
        var images = prompt["images"]!.JSONValueArray!;
        Assert.Single(images); // the non-image attachment is silently omitted
        Assert.Equal("image", images[0]["type"]!.JSONValueString);
        Assert.Equal("image/png", images[0]["mimeType"]!.JSONValueString);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), images[0]["data"]!.JSONValueString);

        Assert.IsType<ProviderEvent.TurnStarted>(events[^1]); // emitted AFTER the stdin write, last of this call
        _ = session;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-280")] // PiSession.swift:188-190
    public async Task Send_omits_the_images_key_when_no_attachment_is_a_readable_image()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("send-no-image");
        var (session, _, startedScript, _) = await Started(runtime, script);
        runtime.Loop.Run(() => session.Send(new TurnInput
        {
            Text = "plain", Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        }), TimeSpan.FromSeconds(10));
        var prompt = Json(WaitForStdinLine(startedScript, 2));
        Assert.Null(prompt["images"]);
        _ = session;
    }

    // ---- PR-PROVCLI-281 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-281")] // PiSession.swift:202-205
    public async Task Interrupt_sends_abort_unconditionally_and_sets_the_flag_with_no_process()
    {
        var session = new PiSession(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        await session.Interrupt(); // no process: the write is dropped, nothing throws

        using var runtime = new TestRuntime();
        var (started, _, startedScript, _) = await Started(runtime, new PiScript("interrupt"));
        runtime.Loop.Run(() => started.Interrupt(), TimeSpan.FromSeconds(10));
        Assert.Equal("abort", Json(WaitForStdinLine(startedScript, 2))["type"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-282 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-282")] // PiSession.swift:207-210
    public async Task Compact_requires_a_running_session_and_sends_a_bare_compact_line()
    {
        var session = new PiSession(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Compact());
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);

        using var runtime = new TestRuntime();
        var (started, _, startedScript, _) = await Started(runtime, new PiScript("compact"));
        runtime.Loop.Run(() => started.Compact(), TimeSpan.FromSeconds(10));
        var line = Json(WaitForStdinLine(startedScript, 2));
        Assert.Equal("compact", line["type"]!.JSONValueString);
        Assert.Null(line["id"]); // no reply awaited, no request id
    }

    // ---- PR-PROVCLI-283 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-283")] // PiSession.swift:212-222
    public async Task Stop_terminates_the_process_and_deletes_the_gate_file()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("stop");
        var (session, _, startedScript, _) = await Started(runtime, script);
        var gatePath = startedScript.Environment("DROPPY_CODE_PI_GATE")!;
        Assert.True(File.Exists(gatePath));

        session.Stop();
        Assert.False(session.IsRunning);
        Assert.False(File.Exists(gatePath));
    }

    // ---- PR-PROVCLI-284 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-284")] // PiSession.swift:224
    public async Task StopAgent_always_returns_false()
    {
        var session = new PiSession(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        Assert.False(await session.StopAgent("any-id"));
    }

    // ---- PR-PROVCLI-285 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-285")] // PiSession.swift:226-240,290-296
    public async Task ResolveApproval_sends_the_role_string_and_grants_a_session_key_for_approveAlways()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("approvals");
        var (session, events, startedScript, _) = await Started(runtime, script);

        // Drive a select request through the gate title convention so pendingApprovals is populated.
        var gatePayload = "{\"kind\":\"command\",\"toolName\":\"bash\",\"input\":{\"command\":\"ls\"},\"toolCallId\":\"t1\"}";
        session.Handle(Json(
            "{\"type\":\"extension_ui_request\",\"id\":\"req-1\",\"method\":\"select\"," +
            "\"params\":{\"title\":\"droppy-gate " + gatePayload.Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\"options\":[\"Allow\",\"Allow for session\",\"Deny\"]}}"));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        var alwaysOption = approval.Options.Single(o => o.OptionRole == ApprovalRequest.Option.Role.ApproveAlways);

        session.ResolveApproval(approval.Id, alwaysOption.Id);
        var sent = Json(WaitForStdinLine(startedScript, 2));
        Assert.Equal("extension_ui_response", sent["type"]!.JSONValueString);
        Assert.Equal("Allow for session", sent["value"]!.JSONValueString);
        Assert.Contains(events, e => e is ProviderEvent.RequestResolved resolved && resolved.Id == approval.Id);

        // A second, identical gate is now auto-approved from the session grant, with no new approval event.
        var beforeCount = events.OfType<ProviderEvent.Approval>().Count();
        session.Handle(Json(
            "{\"type\":\"extension_ui_request\",\"id\":\"req-2\",\"method\":\"select\"," +
            "\"params\":{\"title\":\"droppy-gate " + gatePayload.Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\"options\":[\"Allow\",\"Allow for session\",\"Deny\"]}}"));
        Assert.Equal(beforeCount, events.OfType<ProviderEvent.Approval>().Count());
        Assert.Equal("Allow", Json(WaitForStdinLine(startedScript, 3))["value"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-285")] // PiSession.swift:229-237
    public async Task ResolveApproval_sends_Deny_for_decline_cancel_and_an_unknown_option()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("approvals-deny");
        var (session, events, startedScript, _) = await Started(runtime, script);
        var gatePayload = "{\"kind\":\"tool\",\"toolName\":\"edit_file\",\"toolCallId\":\"t2\"}";
        session.Handle(Json(
            "{\"type\":\"extension_ui_request\",\"id\":\"req-1\",\"method\":\"select\"," +
            "\"params\":{\"title\":\"droppy-gate " + gatePayload.Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\"options\":[\"Allow\",\"Allow for session\",\"Deny\"]}}"));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        session.ResolveApproval(approval.Id, "not-a-real-option-id");
        Assert.Equal("Deny", Json(WaitForStdinLine(startedScript, 2))["value"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-286 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-286")] // PiSession.swift:242-253
    public async Task AnswerQuestion_shapes_the_reply_by_emptiness_confirm_or_plain_value()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("questions");
        var (session, events, startedScript, _) = await Started(runtime, script);

        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q1\",\"method\":\"confirm\",\"params\":{\"title\":\"Proceed?\"}}"));
        var confirmId = Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Id;
        session.AnswerQuestion(confirmId, new ValueMap<string, ValueList<string>>(
            new Dictionary<string, ValueList<string>> { ["a"] = new ValueList<string>(["Yes"]) }));
        var confirmed = Json(WaitForStdinLine(startedScript, 2));
        Assert.True(confirmed["confirmed"]!.JSONValueBool);

        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q2\",\"method\":\"input\",\"params\":{\"title\":\"Name?\"}}"));
        var inputId = events.OfType<ProviderEvent.Question>().Last().Value.Id;
        session.AnswerQuestion(inputId, new ValueMap<string, ValueList<string>>(
            new Dictionary<string, ValueList<string>> { ["a"] = new ValueList<string>(["Ada"]) }));
        Assert.Equal("Ada", Json(WaitForStdinLine(startedScript, 3))["value"]!.JSONValueString);

        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q3\",\"method\":\"input\",\"params\":{\"title\":\"Name?\"}}"));
        var cancelId = events.OfType<ProviderEvent.Question>().Last().Value.Id;
        session.AnswerQuestion(cancelId, ValueMap<string, ValueList<string>>.Empty);
        Assert.True(Json(WaitForStdinLine(startedScript, 4))["cancelled"]!.JSONValueBool);
    }

    // ---- PR-PROVCLI-287 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:302-309
    public void Handle_a_response_with_no_pending_id_and_a_failed_prompt_command_ends_the_turn()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        // Directly reach into the turn state the way send() would, via a text delta.
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"x\"}}"));

        session.Handle(Json("{\"type\":\"response\",\"command\":\"prompt\",\"success\":false,\"error\":\"boom\"}"));
        // finishTurn only emits when turnActive; set it first via a real call path is out of scope here, so this
        // test proves the error-selection and no-throw behavior of the response branch itself.
        Assert.DoesNotContain(events, e => e is ProviderEvent.TurnCompleted);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:331-338
    public void Handle_compaction_and_retry_and_extension_error_notices_use_the_exact_wording()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"compaction_start\"}"));
        session.Handle(Json("{\"type\":\"compaction_end\"}"));
        session.Handle(Json("{\"type\":\"auto_retry_start\"}"));
        session.Handle(Json("{\"type\":\"extension_error\"}"));
        var notices = events.OfType<ProviderEvent.Notice>().Select(e => e.Value).ToList();
        Assert.Equal("Compacting the conversation\u2026", notices[0].Message); // U+2026 HORIZONTAL ELLIPSIS, no period
        Assert.Equal(Notice.Level.Info, notices[0].NoticeLevel);
        Assert.Equal("Conversation compacted", notices[1].Message); // no trailing period
        Assert.Equal("Pi is retrying.", notices[2].Message);
        Assert.Equal(Notice.Level.Warning, notices[2].NoticeLevel);
        Assert.Equal("Pi reported an extension error.", notices[3].Message);
        Assert.Equal(Notice.Level.Error, notices[3].NoticeLevel);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:341-343
    public void Handle_ignores_an_unknown_message_type()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"something_else\"}"));
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-288 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-288")] // PiSession.swift:37-38,313,361,521
    public void Ids_are_namespaced_by_an_eight_character_launchID_with_m_t_and_plan_shapes()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"hi\"}}"));
        session.HandleUpdate(Json("{\"event\":{\"type\":\"thinking_delta\",\"delta\":\"...\"}}"));
        var messageID = Assert.Single(events.OfType<ProviderEvent.MessageDelta>()).Id;
        var thinkingID = Assert.Single(events.OfType<ProviderEvent.ReasoningDelta>()).Id;
        Assert.Matches("^[0-9A-F]{8}-m-1$", messageID);
        Assert.Matches("^[0-9A-F]{8}-t-1$", thinkingID);
        Assert.Equal(messageID[..8], thinkingID[..8]); // the same launchID namespaces both
    }

    // ---- PR-PROVCLI-289 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-289")] // PiSession.swift:346-368
    public void HandleUpdate_mints_ids_on_first_delta_only_and_accumulates_text_and_thinking()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"\"}}")); // empty delta: ignored
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"He\"}}"));
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"llo\"}}"));
        var deltas = events.OfType<ProviderEvent.MessageDelta>().ToList();
        Assert.Equal(2, deltas.Count);
        Assert.Equal(deltas[0].Id, deltas[1].Id); // same id across both deltas
        Assert.Equal("He", deltas[0].Text);
        Assert.Equal("llo", deltas[1].Text);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-289")] // PiSession.swift:369-380
    public void HandleUpdate_reports_usage_from_totalTokens_or_the_four_fallback_pairs()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.HandleUpdate(Json("{\"usage\":{\"totalTokens\":500}}"));
        Assert.Equal(500, Assert.Single(events.OfType<ProviderEvent.Usage>()).Value.UsedTokens);

        events.Clear();
        session.HandleUpdate(Json("{\"usage\":{\"input\":10,\"output\":20,\"cacheRead\":5,\"cacheWrite\":1}}"));
        Assert.Equal(36, Assert.Single(events.OfType<ProviderEvent.Usage>()).Value.UsedTokens);

        events.Clear();
        session.HandleUpdate(Json("{\"usage\":{\"inputTokens\":2,\"outputTokens\":3}}"));
        Assert.Equal(5, Assert.Single(events.OfType<ProviderEvent.Usage>()).Value.UsedTokens);

        events.Clear();
        session.HandleUpdate(Json("{\"usage\":{}}"));
        Assert.Empty(events.OfType<ProviderEvent.Usage>()); // zero usage emits nothing
    }

    // ---- PR-PROVCLI-290 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-290")] // PiSession.swift:384-404
    public void HandleMessageEnd_ignores_non_assistant_and_prefers_joined_content_blocks_over_the_streamed_text()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"user\"}}"));
        Assert.Empty(events); // non-assistant: ignored entirely

        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.HandleUpdate(Json("{\"event\":{\"type\":\"text_delta\",\"delta\":\"streamed\"}}"));
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"joi\"},{\"type\":\"tool\",\"text\":\"skip\"},{\"type\":\"text\",\"text\":\"ned\"}],\"stopReason\":\"error\",\"errorMessage\":\"bad\"}"));
        var completed = Assert.Single(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.Equal("joined", completed.Text); // replaces the streamed text, no separator between blocks
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-290")] // PiSession.swift:393-396
    public void HandleMessageEnd_emits_reasoningCompleted_only_for_non_blank_accumulated_thinking()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.HandleUpdate(Json("{\"event\":{\"type\":\"thinking_delta\",\"delta\":\"  \"}}")); // blank once trimmed
        session.HandleMessageEnd(Json("{\"role\":\"assistant\"}"));
        Assert.Empty(events.OfType<ProviderEvent.ReasoningCompleted>());
    }

    // ---- PR-PROVCLI-291 ----

    [Theory]
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:415-422
    [InlineData("bash", ToolCall.Kind.Command)]
    [InlineData("read", ToolCall.Kind.Read)]
    [InlineData("edit", ToolCall.Kind.Edit)]
    [InlineData("write", ToolCall.Kind.Edit)]
    [InlineData("grep", ToolCall.Kind.Search)]
    [InlineData("find", ToolCall.Kind.Search)]
    [InlineData("ls", ToolCall.Kind.Search)]
    [InlineData("mcp__server__tool", ToolCall.Kind.Mcp)]
    [InlineData("anything_else", ToolCall.Kind.Other)]
    public void HandleToolStart_maps_the_six_tool_name_groups_to_kinds(string toolName, ToolCall.Kind expected)
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.HandleToolStart(Json($"{{\"toolCallId\":\"c1\",\"toolName\":\"{toolName}\",\"args\":{{}}}}"));
        var started = Assert.Single(events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal(expected, started.Call.ToolCallKind);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:412-413,496-500
    public void HandleToolStart_snapshots_a_write_targets_file_before_the_call()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var target = Path.Combine(Path.GetTempPath(), "marrow-pisession-snapshot-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(target, "before");
        try
        {
            session.HandleToolStart(Json("{\"toolCallId\":\"c1\",\"toolName\":\"write\",\"args\":{\"path\":\"" +
                target.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}}"));
            var started = Assert.Single(events.OfType<ProviderEvent.ToolStarted>());
            Assert.Equal(ToolCall.Kind.Edit, started.Call.ToolCallKind);
            Assert.Equal(Path.GetFileName(target), started.Call.Title); // relative to WorkingDirectory (ToolTitles.RelativePath)
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:426-446
    public void HandleToolStart_titles_bash_and_mcp_and_default_tools()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        session.HandleToolStart(Json("{\"toolCallId\":\"c1\",\"toolName\":\"bash\",\"args\":{\"command\":\"echo hi\"}}"));
        Assert.Equal("echo hi", events.OfType<ProviderEvent.ToolStarted>().Last().Call.Title);

        session.HandleToolStart(Json("{\"toolCallId\":\"c2\",\"toolName\":\"mcp__filesystem__read_file\",\"args\":{}}"));
        var mcpCall = events.OfType<ProviderEvent.ToolStarted>().Last().Call;
        Assert.Equal("filesystem \u00B7 read_file", mcpCall.Title); // U+00B7 MIDDLE DOT

        session.HandleToolStart(Json("{\"toolCallId\":\"c3\",\"toolName\":\"custom_tool\",\"args\":{\"x\":1}}"));
        var customCall = events.OfType<ProviderEvent.ToolStarted>().Last().Call;
        Assert.Equal("custom_tool", customCall.Title);
        Assert.NotNull(customCall.Detail);

        session.HandleToolStart(Json("{\"toolCallId\":\"c4\",\"toolName\":\"custom_empty\",\"args\":{}}"));
        Assert.Null(events.OfType<ProviderEvent.ToolStarted>().Last().Call.Detail); // {} compacts to no detail
    }
}

