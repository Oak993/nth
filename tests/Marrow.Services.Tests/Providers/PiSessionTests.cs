using System.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/PiSession.swift (B3 52bc7926), :1-610. B3 delta (lane L3f): the
// PR-PROVCLI-275, PR-PROVCLI-276 and PR-PROVCLI-299 tests below now exercise the shared PendingReplies wire
// (Core/Support/PendingReplies.cs, N1) instead of this file's own continuation dictionary; citations elsewhere
// in this file are re-pinned to the B3 line numbers, and a few whose code moved to SessionSupport.cs (N7) or
// ProviderSession.cs cite that file too.
//
// Tests for unit Services.Providers.PiSession part 1 of 2 (PR-PROVCLI-267 to 291): the launch path (the
// resume-file check, the gate and extension setup, the argument vector, the environment, the process launch,
// the get_state/get_commands handshake, the request/response wire), send/interrupt/compact/stop, approvals and
// question answers, and the message/tool event stream. Each launch test spawns tests/fixtures/pi-cli/PiStandIn.cs,
// a compiled offline stand-in (R-TEST-8, R-TEST-10; never a real AI CLI), driven through
// tests/Marrow.Services.Tests/Fixtures/PiStandIns.cs. Stream-dispatch tests call PiSession's internal Handle
// methods directly with constructed JSONValue lines, which needs no live process.
//
// Part 2 of 2 (PR-PROVCLI-292 to 300, UI-PROVCLI-010 to 012), below: handleToolEnd and its unified-diff helper,
// finishTurn's plan publication, the eight extension_ui_request methods, the droppy-gate select payload and its
// session-grant auto-allow, the three-option approval row, didClose's turn/exit reporting, and isRunning's three
// flags. Most of these dispatch through the internal Handle(...) with no live process; didClose is reached
// through the internal DidClose(...) seam and a turn is put in flight through the internal TestBeginTurn(...)
// seam (a no-Swift-twin subset of send()'s state at :180-187, since send() itself requires a live process).
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
        var localApplicationData = configuration.Executable is { } executable
            ? Path.Combine(Path.GetDirectoryName(executable.Path)!, "local-app-data")
            : null;
        var session = new PiSession(configuration, localApplicationData);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    private static string CreateTestFolder(string label)
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var directory = Path.Combine(root, "PiSessionTests", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Replies to the get_state and get_commands requests Start() always sends first, and starts the
    /// session (real StdioProcess, real handshake, through the offline stand-in).</summary>
    private static async Task<(PiSession Session, List<ProviderEvent> Events, PiScript Script, string ID)> Started(
        TestRuntime runtime, PiScript script, string? resumeID = null, string? model = null, string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised, InteractionMode interactionMode = InteractionMode.Build,
        string stateData = "{\"model\":{\"contextWindow\":128000}}", string commandsData = "{\"commands\":[]}",
        Action? prepareStorage = null, Func<IDisposable>? scope = null)
    {
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        prepareStorage?.Invoke();
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":" + stateData + "}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":" + commandsData + "}");
        var (session, events) = Make(Config(script, resumeID: resumeID, model: model, effort: effort, runtimeMode: runtimeMode, interactionMode: interactionMode));
        // PORT-ONLY: `scope` opens an Ambient override on the loop's own thread (the test thread's async flow does not reach it).
        var id = runtime.Loop.Run(async () =>
        {
            using var overrides = scope?.Invoke();
            return await session.Start();
        }, TimeSpan.FromSeconds(30));
        return (session, events, script, id);
    }

    private static IDisposable Both(IDisposable first, IDisposable second) => new DisposePair(first, second);

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
    [Trait("Req", "PR-PROVCLI-267")] // PiSession.swift:72,83-85
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
    [Trait("Req", "PR-PROVCLI-268")] // PiSession.swift:86-91
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
    [Trait("Req", "PR-PROVCLI-269")] // PiSession.swift:93-94
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
    [Trait("Req", "PR-PROVCLI-270")] // PiSession.swift:95-97
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
    [Trait("Req", "PR-PROVCLI-271")] // PiSession.swift:98-100
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
    [Trait("Req", "PR-PROVCLI-272")] // PiSession.swift:101
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
    [Trait("Req", "PR-PROVCLI-273")] // PiSession.swift:103-111
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
        Assert.Null(startedScript.Environment("DROPPY_CODE_MCP_CONFIG"));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public async Task Start_adds_long_cache_retention_only_when_ignis_is_on_and_its_gap_rule_allows_it()
    {
        // PORT-ONLY (ED-NEW-ignis-integration): the launch model reaches the gap rule and its verdict decides the variable.
        IgnisPiRetentionQuery? asked = null;
        using var runtime = new TestRuntime();
        var (_, _, longScript, _) = await Started(runtime, new PiScript("retention-long"), model: "openai/gpt-5.1", scope: () => Both(
            IgnisSeams.IsOn.Override((_, _) => true),
            IgnisSeams.PiRetention.Override(query => { asked = query; return new IgnisPiRetentionVerdict(IgnisPiRetentionReason.LongerFree, "openai"); })));
        Assert.Equal("long", longScript.Environment("PI_CACHE_RETENTION"));
        Assert.Equal("openai/gpt-5.1", asked!.Model);

        using var runtimeB = new TestRuntime();
        var (_, _, shortScript, _) = await Started(runtimeB, new PiScript("retention-short"), model: "anthropic/claude-sonnet-4-5", scope: () => Both(
            IgnisSeams.IsOn.Override((_, _) => true),
            IgnisSeams.PiRetention.Override(_ => new IgnisPiRetentionVerdict(IgnisPiRetentionReason.TooFewGaps, "anthropic", 3))));
        Assert.Null(shortScript.Environment("PI_CACHE_RETENTION"));

        // Ignis on but the gap rule unbound: nothing is set either.
        using var runtimeC = new TestRuntime();
        var (_, _, unboundScript, _) = await Started(runtimeC, new PiScript("retention-unbound"), model: "openai/gpt-5.1", scope: () => IgnisSeams.IsOn.Override((_, _) => true));
        Assert.Null(unboundScript.Environment("PI_CACHE_RETENTION"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-273")] // PiSession.swift:110-111
    public async Task Start_sets_the_claude_mcp_config_key_only_when_its_file_exists()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("env-mcp-config-present");
        string? configPath = null;
        var (_, _, startedScript, _) = await Started(runtime, script, prepareStorage: () =>
        {
            configPath = MCPPaths.ClaudeConfigURL;
            File.WriteAllText(configPath, "{}");
        });

        Assert.Equal(configPath, startedScript.Environment("DROPPY_CODE_MCP_CONFIG"));
    }

    // ---- PR-PROVCLI-274 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-274")] // PiSession.swift:113-134
    public async Task Start_launch_failure_is_reported_with_the_prefixed_message()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        var missingExecutable = Path.Combine(CreateTestFolder("launch-failure"), "missing-pi.exe");
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

    [Fact]
    [Trait("Req", "PR-PROVCLI-274")] // PiSession.swift:5-6,113-134; packet requires a CRLF stand-in
    public async Task Start_reads_crlf_delimited_handshake_response_lines()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        var script = new PiScript("handshake-crlf");
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{\"sessionFile\":\"session-1.json\"}}\r");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":{\"commands\":[{\"name\":\"compact\"}]}}\r");
        var (session, events) = Make(Config(script));

        var id = runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30));

        Assert.Equal("session-1.json", id);
        Assert.Equal(2, script.StdinLines().Count); // both CRLF responses completed the normal two-request handshake
        Assert.Single(events.OfType<ProviderEvent.SessionReady>());
        Assert.Equal("compact", Assert.Single(events.OfType<ProviderEvent.Commands>()).Value[0].Name);
    }

    // ---- PR-PROVCLI-275 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-275")] // PiSession.swift:136-157
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
    [Trait("Req", "PR-PROVCLI-275")] // PiSession.swift:140-144
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
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:258-270
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
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:266; Core/Support/PendingReplies.swift:21-29 (B3: the 30s timeout moved into PendingReplies.Register)
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
        // PendingReplies starts each timeout in a queued MainActor task. Seeing stdin alone does not
        // establish that get_commands' timer has started; join both request timers before advancing.
        runtime.Loop.RunUntil(() => runtime.Time.TimerDeadlines.Count(t => t == TimeSpan.FromSeconds(30)) == 2,
            TimeSpan.FromSeconds(20));
        runtime.Advance(TimeSpan.FromSeconds(30));
        var id = runtime.Loop.Run(() => startTask!, TimeSpan.FromSeconds(30));
        Assert.Equal("", id); // get_state's data carried no session id
        Assert.Empty(events.OfType<ProviderEvent.Commands>()); // the timed-out get_commands produced no commands
    }

    // B3 delta (N1): PendingReplies.Register checks the caller's own cancellation before calling send, so an
    // already-cancelled caller resolves null without writing a line at all, though the id number is still used
    // up (requestCounter incremented before the check). This did not exist at B2, whose own request(_:) had no
    // cancellation handling of any kind, so there is no earlier version of this test.
    [Fact]
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:262-266; Core/Support/PendingReplies.swift:17
    public void Request_from_an_already_cancelled_caller_resolves_null_and_writes_no_line()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        var script = new PiScript("request-already-cancelled");
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{}}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":{}}");
        var (session, _) = Make(Config(script));
        runtime.Loop.Enqueue(() => _ = session.Start());
        runtime.Loop.RunUntil(() => script.StdinLines().Count == 2, TimeSpan.FromSeconds(20));

        JSONValue? result = null;
        var handle = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(5));
            result = await session.Request(JSONValue.FromDictionaryLiteral(("type", "get_state")));
        });
        runtime.RunUntilIdle();
        handle.Cancel(); // cancelled before the sleep ever finishes: Register sees an already-cancelled caller
        runtime.RunUntilIdle();

        Assert.IsType<JSONValue.Null>(result);
        Assert.Equal(2, script.StdinLines().Count); // still just the handshake's two lines: the cancelled request wrote nothing
    }

    // ---- PR-PROVCLI-277 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-277")] // PiSession.swift:163-165,272-282
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
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:160-162
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
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:166-175
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
    [Trait("Req", "PR-PROVCLI-279")] // PiSession.swift:176-179
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
    [Trait("Req", "PR-PROVCLI-280")] // PiSession.swift:180-201
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
    [Trait("Req", "PR-PROVCLI-280")] // PiSession.swift:189-191
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
    [Trait("Req", "PR-PROVCLI-281")] // PiSession.swift:203-206
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
    [Trait("Req", "PR-PROVCLI-282")] // PiSession.swift:208-211
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
    [Trait("Req", "PR-PROVCLI-283")] // PiSession.swift:213-223
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
    [Trait("Req", "PR-PROVCLI-284")] // PiSession.swift:225
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
    [Trait("Req", "PR-PROVCLI-285")] // PiSession.swift:227-241; ProviderSession.swift:222-229 (B3: ApprovalRequest.Kind.grantKey, moved out of this file)
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
    [Trait("Req", "PR-PROVCLI-285")] // PiSession.swift:230-238
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
    [Trait("Req", "PR-PROVCLI-286")] // PiSession.swift:243-254
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
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:288-294
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
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:313-320
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
    [Trait("Req", "PR-PROVCLI-287")] // PiSession.swift:323-325
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
    [Trait("Req", "PR-PROVCLI-288")] // PiSession.swift:37-38,64-65,476 (B3: the id-format closures moved into init, called from stream.AppendMessage/AppendThinking at :333,:336)
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
    [Trait("Req", "PR-PROVCLI-289")] // PiSession.swift:328-339
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
    [Trait("Req", "PR-PROVCLI-289")] // PiSession.swift:340-351
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

    // ---- PORT-ONLY: Ignis (ED-NEW-ignis-integration): pi's cache numbers, so Ignis learns pi's idle gaps ----

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void HandleMessageEnd_reports_the_assistant_messages_cache_numbers_for_ignis()
    {
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
            Model = "anthropic/claude-sonnet-4-5",
        });
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"provider\":\"openai\",\"model\":\"gpt-5.1\",\"content\":[],\"usage\":{\"input\":1200,\"output\":300,\"cacheRead\":40000,\"cacheWrite\":800}}"));
        var usage = Assert.Single(events.OfType<ProviderEvent.CacheUsage>()).Value;
        // The message's own provider and model name the upstream; pi gives no write lifetime, so the writes are "unknown TTL".
        Assert.Equal((ProviderKind.Pi, "openai/gpt-5.1", null, 1200L, 40000L, 0L, 0L, 800L, 300L, true), (usage.Provider, usage.Model, usage.AgentID, usage.UncachedInput, usage.CacheRead, usage.CacheWrite5m, usage.CacheWrite1h, usage.CacheWriteUnknownTtl, usage.Output, usage.Reported));
        Assert.Equal(42000, usage.TotalInput);

        // A message that names no model falls back to the session's; the *Tokens spellings are read like the usage panel's.
        events.Clear();
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"content\":[],\"usage\":{\"inputTokens\":10,\"outputTokens\":2,\"cacheReadTokens\":0,\"cacheWriteTokens\":5}}"));
        var fallback = Assert.Single(events.OfType<ProviderEvent.CacheUsage>()).Value;
        Assert.Equal(("anthropic/claude-sonnet-4-5", 10L, 0L, 5L, 2L), (fallback.Model, fallback.UncachedInput, fallback.CacheRead, fallback.CacheWriteUnknownTtl, fallback.Output));

        // No cache fields, no tokens at all, a non-assistant message: nothing for Ignis.
        events.Clear();
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"content\":[],\"usage\":{\"input\":10,\"output\":2}}"));
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"content\":[],\"usage\":{\"input\":0,\"output\":0,\"cacheRead\":0,\"cacheWrite\":0}}"));
        session.HandleMessageEnd(Json("{\"role\":\"assistant\",\"content\":[]}"));
        session.HandleMessageEnd(Json("{\"role\":\"user\",\"usage\":{\"input\":10,\"cacheRead\":5}}"));
        Assert.Empty(events.OfType<ProviderEvent.CacheUsage>());
    }

    // ---- PR-PROVCLI-290 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-290")] // PiSession.swift:355-375
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
    [Trait("Req", "PR-PROVCLI-290")] // PiSession.swift:364-367
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
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:386-393
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
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:383-384; SessionSupport.swift:9-11,36-40 (B3: EditSnapshots.Capture's own Read, moved out of this file)
    public void HandleToolStart_snapshots_a_write_targets_file_before_the_call()
    {
        var folder = CreateTestFolder("snapshot");
        var (session, events) = Make(new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            WorkingDirectory = FileUrl(folder),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var target = Path.Combine(folder, "snapshot-" + Guid.NewGuid().ToString("N") + ".txt");
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
    [Trait("Req", "PR-PROVCLI-291")] // PiSession.swift:397-417
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

    // =================== Part 2 of 2 (PR-PROVCLI-292 to 300, UI-PROVCLI-010 to 012) ===================

    private static (PiSession Session, List<ProviderEvent> Events) MakeInDir(string dir) => Make(new SessionConfiguration
    {
        Provider = ProviderKind.Pi,
        WorkingDirectory = FileUrl(dir),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    });

    private static (PiSession Session, List<ProviderEvent> Events) Make2() => MakeInDir(Path.GetTempPath());

    // ---- PR-PROVCLI-292 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:422-426
    public void HandleToolEnd_completed_uses_result_content_text()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t1\",\"toolName\":\"bash\",\"args\":{\"command\":\"ls\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"t1\",\"result\":{\"content\":\"hello out\"}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal("t1", updated.Id);
        Assert.Equal("hello out", updated.Update.Output);
        Assert.Equal(ToolCall.Status.Completed, updated.Update.Status);
        Assert.Null(updated.Update.Edits); // no error, and no recorded edit/write tool
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:427-430
    public void HandleToolEnd_an_error_starting_with_the_decline_prefix_reads_declined()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t2\",\"toolName\":\"edit\",\"args\":{\"path\":\"/a/f.txt\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"t2\",\"isError\":true,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"The user declined this edit.\"}]}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Declined, updated.Update.Status);
        Assert.Equal("The user declined this edit.", updated.Update.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:429-430
    public void HandleToolEnd_an_error_without_the_prefix_reads_failed()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t3\",\"toolName\":\"bash\",\"args\":{}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"t3\",\"result\":{\"content\":\"boom happened\",\"isError\":true}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Failed, updated.Update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:426
    public void HandleToolEnd_isError_reads_only_the_boolean_true()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t4\",\"toolName\":\"bash\",\"args\":{}}"));
        // A string "true" is not the boolean true, so the call still completes.
        session.HandleToolEnd(Json("{\"toolCallId\":\"t4\",\"isError\":\"true\",\"result\":{\"content\":\"fine\"}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Completed, updated.Update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:431
    public void HandleToolEnd_exitCode_comes_from_result_details()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t5\",\"toolName\":\"bash\",\"args\":{}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"t5\",\"result\":{\"content\":\"x\",\"details\":{\"exitCode\":2}}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(2L, updated.Update.ExitCode);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-292")] // PiSession.swift:436
    public void HandleToolEnd_empty_output_yields_a_null_output()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"t6\",\"toolName\":\"bash\",\"args\":{}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"t6\",\"result\":{\"content\":\"\"}}"));
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Null(updated.Update.Output);
    }

    // ---- PR-PROVCLI-293 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:443-447,458-465
    public void EditDiff_edit_builds_a_whole_file_diff_and_never_relativizes_the_path()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"e1\",\"toolName\":\"edit\",\"args\":{\"path\":\"/abs/dir/File.txt\",\"oldText\":\"a\\nb\",\"newText\":\"c\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"e1\",\"result\":{\"content\":\"\"}}"));
        var edit = Assert.Single(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits!);
        // UPSTREAM DEFECT, reproduced (PR-PROVCLI-293): path.hasPrefix("/") ? path : path is a no-op, so the
        // absolute path is never made relative.
        Assert.Equal("/abs/dir/File.txt", edit.Path);
        Assert.Equal("--- a//abs/dir/File.txt\n+++ b//abs/dir/File.txt\n-a\n-b\n+c", edit.Diff);
        Assert.Equal(1, edit.Additions);
        Assert.Equal(2, edit.Deletions);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:383-384,448-452; SessionSupport.swift:9-11,13-15,21-23,36-40 (B3: EditSnapshots, moved out of this file)
    public void EditDiff_write_uses_the_pre_call_snapshot_and_removes_it_as_consumed()
    {
        var dir = CreateTestFolder("editdiff");
        try
        {
            var file = SwiftPath.AppendingPathComponent(dir, "out.txt");
            File.WriteAllText(file, "old line\n");
            var (session, events) = MakeInDir(dir);
            var argsJson = "{\"toolCallId\":\"w1\",\"toolName\":\"write\",\"args\":{\"path\":\"" + file.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"content\":\"new line\"}}";
            session.HandleToolStart(Json(argsJson));
            session.HandleToolEnd(Json("{\"toolCallId\":\"w1\",\"result\":{\"content\":\"\"}}"));
            var first = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits!;
            Assert.Equal("--- a/" + file + "\n+++ b/" + file + "\n-old line\n+new line", Assert.Single(first).Diff);

            // The snapshot is removed as it is consumed: a second call for the same id sees an empty old side.
            session.HandleToolEnd(Json("{\"toolCallId\":\"w1\",\"result\":{\"content\":\"\"}}"));
            var second = events.OfType<ProviderEvent.ToolUpdated>().Last().Update.Edits!;
            Assert.Equal("--- a/" + file + "\n+++ b/" + file + "\n+new line", Assert.Single(second).Diff);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:450-451
    public void EditDiff_write_without_a_snapshot_treats_the_old_side_as_empty()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"w2\",\"toolName\":\"write\",\"args\":{\"path\":\"/a/missing.txt\",\"content\":\"new line\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"w2\",\"result\":{\"content\":\"\"}}"));
        var edit = Assert.Single(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits!);
        Assert.Equal("--- a//a/missing.txt\n+++ b//a/missing.txt\n+new line", edit.Diff);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:449
    public void EditDiff_write_missing_content_yields_no_edits()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"w3\",\"toolName\":\"write\",\"args\":{\"path\":\"/a/f.txt\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"w3\",\"result\":{\"content\":\"\"}}"));
        Assert.Null(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:444
    public void EditDiff_edit_missing_path_yields_no_edits()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"e2\",\"toolName\":\"edit\",\"args\":{\"oldText\":\"a\",\"newText\":\"b\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"e2\",\"result\":{\"content\":\"\"}}"));
        Assert.Null(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:453-455
    public void EditDiff_an_unhandled_tool_name_yields_no_edits()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"g1\",\"toolName\":\"grep\",\"args\":{\"pattern\":\"x\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"g1\",\"result\":{\"content\":\"\"}}"));
        Assert.Null(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-293")] // PiSession.swift:461-462
    public void EditDiff_drops_every_empty_line_on_both_sides()
    {
        var (session, events) = Make2();
        session.HandleToolStart(Json("{\"toolCallId\":\"e3\",\"toolName\":\"edit\",\"args\":{\"path\":\"/a/f.txt\",\"oldText\":\"a\\n\\nb\",\"newText\":\"\\nc\"}}"));
        session.HandleToolEnd(Json("{\"toolCallId\":\"e3\",\"result\":{\"content\":\"\"}}"));
        var edit = Assert.Single(Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Edits!);
        Assert.Equal("--- a//a/f.txt\n+++ b//a/f.txt\n-a\n-b\n+c", edit.Diff);
    }

    // ---- PR-PROVCLI-294 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:468-477
    public void FinishTurn_plan_mode_with_no_error_publishes_the_plan_then_completes()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Plan);
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"My plan\"}]}}"));
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        var plan = Assert.Single(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Matches("^[0-9A-F]{8}-plan-1$", plan.Id);
        Assert.Equal("My plan", plan.Markdown);
        var turn = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Null(turn.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:473-474,480-481
    public void FinishTurn_an_errored_plan_mode_turn_publishes_no_plan()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Plan);
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"My plan\"}]}}"));
        session.Handle(Json("{\"type\":\"response\",\"id\":\"droppy-9\",\"command\":\"prompt\",\"success\":false,\"error\":\"boom\"}"));
        Assert.Empty(events.OfType<ProviderEvent.PlanCompleted>());
        var turn = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal("boom", turn.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:474,478-479
    public async Task FinishTurn_interrupted_reports_interrupted_and_publishes_no_plan()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Plan);
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"My plan\"}]}}"));
        await session.Interrupt(); // no process: the write is dropped, the flag is still set
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        Assert.Empty(events.OfType<ProviderEvent.PlanCompleted>());
        var turn = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Interrupted, turn.Status);
        Assert.Null(turn.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:469
    public void FinishTurn_with_no_active_turn_emits_nothing()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:474
    public void FinishTurn_outside_plan_mode_publishes_no_plan()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Build);
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}]}}"));
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        Assert.Empty(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Equal(TurnStatus.Completed, Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-294")] // PiSession.swift:475
    public void FinishTurn_a_blank_last_reply_publishes_no_plan()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Plan);
        session.Handle(Json("{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\"}}"));
        session.Handle(Json("{\"type\":\"message_end\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"   \"}]}}"));
        session.Handle(Json("{\"type\":\"agent_end\"}"));
        Assert.Empty(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Equal(TurnStatus.Completed, Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()).Status);
    }

    // ---- PR-PROVCLI-295 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:497-511
    [Trait("Req", "UI-PROVCLI-010")] // PiSession.swift:506: Yes is the first of exactly two choices, no free-text field; the answer mapping is the wire test AnswerQuestion_confirm_maps_exactly_Yes_to_true_and_anything_else_to_false
    [Trait("Req", "UI-PROVCLI-011")] // PiSession.swift:506: No is the second; the answer mapping is the same wire test
    public void HandleExtensionUI_confirm_emits_a_yes_no_question_with_a_pi_header()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"r1\",\"method\":\"confirm\",\"params\":{\"title\":\"T\",\"message\":\"M\"}}"));
        var q = Assert.Single(Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Questions);
        Assert.Equal("r1", q.Id);
        Assert.Equal("Pi", q.Header);
        Assert.Equal("T\nM", q.Prompt);
        Assert.Equal(2, q.Choices.Count);
        Assert.Equal("Yes", q.Choices[0].Label);
        Assert.Equal("No", q.Choices[1].Label);
        Assert.False(q.AllowsMultiple);
        Assert.False(q.AllowsOther);
        Assert.False(q.IsSecret);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:499
    public void HandleExtensionUI_confirm_prompt_joins_title_and_message_dropping_empties()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"r2\",\"method\":\"confirm\",\"params\":{\"title\":\"\",\"message\":\"M\"}}"));
        Assert.Equal("M", Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Questions[0].Prompt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:498,504
    [Trait("Req", "UI-PROVCLI-012")] // PiSession.swift:504
    public void HandleExtensionUI_confirm_defaults_the_header_and_title_to_pi()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"r3\",\"method\":\"confirm\",\"params\":{}}"));
        var q = Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Questions[0];
        Assert.Equal("Pi", q.Header);
        Assert.Equal("Pi", q.Prompt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:512-524
    [Trait("Req", "UI-PROVCLI-012")] // PiSession.swift:517-518
    public void HandleExtensionUI_input_and_editor_allow_other_text_with_a_pi_header()
    {
        var (session, events) = Make2();
        foreach (var (method, id) in new[] { ("input", "i1"), ("editor", "i2") })
        {
            session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"" + id + "\",\"method\":\"" + method + "\",\"params\":{\"title\":\"Pick\"}}"));
            var q = Assert.Single(events.OfType<ProviderEvent.Question>(), e => e.Value.Id == id).Value.Questions[0];
            Assert.Equal(id, q.Id);
            Assert.Equal("Pi", q.Header);
            Assert.Equal("Pick", q.Prompt);
            Assert.Empty(q.Choices);
            Assert.False(q.AllowsMultiple);
            Assert.True(q.AllowsOther);
            Assert.False(q.IsSecret);
        }

        // A title-less input shows "Pi" twice: header and prompt (UI-PROVCLI-012).
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"i3\",\"method\":\"input\",\"params\":{}}"));
        Assert.Equal("Pi", events.OfType<ProviderEvent.Question>().Last().Value.Questions[0].Prompt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:525-531
    public void HandleExtensionUI_notify_maps_the_three_levels_and_defaults_the_message()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"n1\",\"method\":\"notify\",\"params\":{\"notifyType\":\"warning\",\"message\":\"watch out\"}}"));
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"n2\",\"method\":\"notify\",\"params\":{\"notifyType\":\"error\",\"message\":\"it broke\"}}"));
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"n3\",\"method\":\"notify\",\"params\":{\"notifyType\":\"other\",\"message\":\"hi\"}}"));
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"n4\",\"method\":\"notify\",\"params\":{}}"));
        var notices = events.OfType<ProviderEvent.Notice>().ToList();
        Assert.Equal(4, notices.Count);
        Assert.Equal(Notice.Level.Warning, notices[0].Value.NoticeLevel);
        Assert.Equal("watch out", notices[0].Value.Message);
        Assert.Equal(Notice.Level.Error, notices[1].Value.NoticeLevel);
        Assert.Equal(Notice.Level.Info, notices[2].Value.NoticeLevel);
        Assert.Equal("Pi sent a notification.", notices[3].Value.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:532-536
    public void HandleExtensionUI_ignores_the_four_named_ui_methods_and_any_other_method()
    {
        var (session, events) = Make2();
        foreach (var method in new[] { "setStatus", "setWidget", "setTitle", "set_editor_text", "somethingElse" })
            session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"x\",\"method\":\"" + method + "\",\"params\":{}}"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:491-492
    public void HandleExtensionUI_with_no_id_or_no_method_is_ignored()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"method\":\"notify\",\"message\":\"x\"}")); // no id/requestId
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"y\"}")); // no method
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"requestId\":\"z\",\"params\":{}}")); // no method
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-295")] // PiSession.swift:491-493
    public void HandleExtensionUI_reads_the_id_from_requestId_and_falls_back_to_the_message_for_params()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"requestId\":\"rq1\",\"method\":\"notify\",\"message\":\"direct\"}"));
        Assert.Equal("direct", Assert.Single(events.OfType<ProviderEvent.Notice>()).Value.Message);
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"requestId\":\"rq2\",\"method\":\"confirm\",\"title\":\"in\",\"message\":\"msg\"}"));
        var question = events.OfType<ProviderEvent.Question>().Last();
        Assert.Equal("rq2", question.Value.Id);
        Assert.Equal("in\nmsg", question.Value.Questions[0].Prompt);
    }

    /// <summary>Builds a well-formed extension_ui_request/select message via JSONValue (never hand-escaped
    /// text), so a Windows path's backslashes need no manual JSON escaping.</summary>
    private static JSONValue SelectRequest(string id, string title, IReadOnlyList<string>? options = null) =>
        JSONValue.FromDictionaryLiteral(("type", "extension_ui_request"), ("id", id), ("method", "select"),
            ("params", JSONValue.FromDictionaryLiteral(("title", title),
                ("options", options is null ? JSONValue.FromArrayLiteral() : new JSONValue.Array(new ValueList<JSONValue>(options.Select(o => (JSONValue)new JSONValue.String(o))))))));

    /// <summary>The "droppy-gate " title an approved tool call carries, built from JSONValue so its embedded
    /// JSON is always well-formed (PiSession.swift:557-564).</summary>
    private static string GateTitle(string kind, string toolName, string toolCallId, JSONValue? input = null) =>
        "droppy-gate " + JSONValue.FromDictionaryLiteral(("kind", kind), ("toolName", toolName),
            ("toolCallId", toolCallId), ("input", input ?? JSONValue.FromDictionaryLiteral())).CompactString;

    [Fact]
    [Trait("Req", "UI-PROVCLI-012")] // PiSession.swift:504, :517-518, :547: the header is the literal Pi on confirm, input, editor and a non-gate select; input/editor prompt = params.title defaulting to Pi
    public void Every_Pi_question_kind_carries_the_literal_header_Pi_and_a_title_less_editor_shows_Pi_twice()
    {
        var (session, events) = Make2();
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"h1\",\"method\":\"confirm\",\"params\":{\"title\":\"Go?\"}}"));
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"h2\",\"method\":\"input\",\"params\":{\"title\":\"Name?\"}}"));
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"h3\",\"method\":\"editor\",\"params\":{}}"));
        session.Handle(SelectRequest("h4", "Pick one", ["a", "b"]));
        var questions = events.OfType<ProviderEvent.Question>().Select(e => Assert.Single(e.Value.Questions)).ToArray();
        Assert.Equal(4, questions.Length);
        Assert.All(questions, question => Assert.Equal("Pi", question.Header));
        Assert.Equal("Go?", questions[0].Prompt);
        Assert.Equal("Name?", questions[1].Prompt); // input: the prompt is params.title
        Assert.Equal("Pi", questions[2].Prompt); // a title-less editor: the prompt defaults to Pi as well, so the sheet reads Pi twice
        Assert.Equal("Pick one", questions[3].Prompt);
    }

    // ---- PR-PROVCLI-296 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-296")] // PiSession.swift:539-556
    public void HandleSelect_a_non_gate_title_becomes_a_plain_question_from_the_string_options()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("s1", "Pick one", ["a", "b"]));
        var q = Assert.Single(Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Questions);
        Assert.Equal("s1", q.Id);
        Assert.Equal("Pi", q.Header);
        Assert.Equal("Pick one", q.Prompt);
        Assert.Equal(2, q.Choices.Count);
        Assert.Equal("a", q.Choices[0].Label);
        Assert.Equal("b", q.Choices[1].Label);
        Assert.False(q.AllowsOther);
        Assert.False(q.IsSecret);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-296")] // PiSession.swift:541
    public void HandleSelect_a_non_string_option_is_dropped_by_compactMap()
    {
        var (session, events) = Make2();
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "extension_ui_request"), ("id", "s1b"), ("method", "select"),
            ("params", JSONValue.FromDictionaryLiteral(("title", "Pick"), ("options", JSONValue.FromArrayLiteral("a", 1, "b"))))));
        var q = events.OfType<ProviderEvent.Question>().Single().Value.Questions[0];
        Assert.Equal(2, q.Choices.Count);
        Assert.Equal("a", q.Choices[0].Label);
        Assert.Equal("b", q.Choices[1].Label);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-296")] // PiSession.swift:542
    public void HandleSelect_a_malformed_gate_payload_degrades_to_a_plain_question()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("s2", "droppy-gate not-json", ["a"]));
        var q = Assert.Single(events.OfType<ProviderEvent.Question>()).Value.Questions[0];
        Assert.Equal("droppy-gate not-json", q.Prompt);
        Assert.Equal("a", q.Choices[0].Label);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-296")] // PiSession.swift:542
    public void HandleSelect_the_gate_prefix_requires_its_trailing_space()
    {
        var (session, events) = Make2();
        // "droppy-gate" without the trailing space before the JSON is not the prefix: degrades.
        session.Handle(SelectRequest("s3", "droppy-gate" + JSONValue.FromDictionaryLiteral(("kind", "tool")).CompactString));
        Assert.Single(events.OfType<ProviderEvent.Question>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-296")] // PiSession.swift:542,557-564
    public void HandleSelect_a_well_formed_gate_payload_reads_its_fields_as_written()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("s4", GateTitle("command", "bash", "c1", JSONValue.FromDictionaryLiteral(("command", "ls")))));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("s4", approval.Id);
        Assert.Equal(ApprovalRequest.Kind.Command, approval.RequestKind);
        Assert.Equal("c1", approval.ToolItemID);
        Assert.Equal("ls", approval.Detail); // the RAW input.command (:614-615), not through ToolTitles
    }

    // ---- PR-PROVCLI-297 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-297")] // PiSession.swift:557-561
    public void HandleSelect_a_granted_kind_auto_allows_with_no_prompt_and_no_event()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("g1", GateTitle("command", "bash", "c1", JSONValue.FromDictionaryLiteral(("command", "ls")))));
        Assert.Single(events.OfType<ProviderEvent.Approval>());
        session.ResolveApproval("g1", "always"); // stores the "command" grant (:232)
        Assert.IsType<ProviderEvent.RequestResolved>(events[^1]);
        var before = events.Count;
        session.Handle(SelectRequest("g2", GateTitle("command", "bash", "c2", JSONValue.FromDictionaryLiteral(("command", "rm -rf x")))));
        Assert.Equal(before, events.Count); // auto-allowed: no new event
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-297")] // PiSession.swift:558; ProviderSession.swift:222-229 (B3: ApprovalRequest.Kind.grantKey, moved out of this file)
    public void HandleSelect_a_grant_matches_only_the_exact_kind_string()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("g3", GateTitle("command", "bash", "c1", JSONValue.FromDictionaryLiteral(("command", "ls")))));
        session.ResolveApproval("g3", "always");
        var before = events.Count;
        // A "tool" kind is not the granted "command" string: it still prompts.
        session.Handle(SelectRequest("g4", GateTitle("tool", "webfetch", "c2", JSONValue.FromDictionaryLiteral(("url", "https://x")))));
        Assert.True(events.Count > before);
        Assert.IsType<ProviderEvent.Approval>(events[^1]);
    }

    // ---- PR-PROVCLI-298 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-298")] // PiSession.swift:583-588
    public void HandleSelect_the_approval_options_are_always_exactly_three_in_order()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("a1", GateTitle("tool", "webfetch", "call-9")));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("call-9", approval.ToolItemID);
        Assert.Equal(3, approval.Options.Count);
        Assert.Equal(("allow", "Approve", ApprovalRequest.Option.Role.Approve), (approval.Options[0].Id, approval.Options[0].Title, approval.Options[0].OptionRole));
        Assert.Equal(("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways), (approval.Options[1].Id, approval.Options[1].Title, approval.Options[1].OptionRole));
        Assert.Equal(("deny", "Decline", ApprovalRequest.Option.Role.Decline), (approval.Options[2].Id, approval.Options[2].Title, approval.Options[2].OptionRole));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-298")] // PiSession.swift:568-570
    public void HandleSelect_a_command_kind_row_has_a_raw_command_detail()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("a2", GateTitle("command", "bash", "c", JSONValue.FromDictionaryLiteral(("command", "rm -rf /tmp/x")))));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.Command, approval.RequestKind);
        Assert.Equal("rm -rf /tmp/x", approval.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-298")] // PiSession.swift:571-573
    public void HandleSelect_a_fileChange_kind_row_has_a_relative_title_and_a_raw_path_detail()
    {
        var dir = CreateTestFolder("filechange");
        try
        {
            var (session, events) = MakeInDir(dir);
            var absolute = SwiftPath.AppendingPathComponent(SwiftPath.AppendingPathComponent(dir, "sub"), "file.txt");
            session.Handle(SelectRequest("a3", GateTitle("fileChange", "edit", "c", JSONValue.FromDictionaryLiteral(("path", absolute)))));
            var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
            Assert.Equal(ApprovalRequest.Kind.FileChange, approval.RequestKind);
            // SwiftPath.TailUnder keeps the original path's own separators (:301-309): native backslashes.
            Assert.Equal("sub\\file.txt", approval.Title);
            Assert.Equal(absolute, approval.Detail);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-298")] // PiSession.swift:574-576
    public void HandleSelect_a_tool_kind_row_has_a_pretty_printed_input_detail()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("a4", GateTitle("mcp", "mcp__srv__do", "c", JSONValue.FromDictionaryLiteral(("z", 1L)))));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        // "mcp" is not "command" or "fileChange", so it falls into the .tool row (:619-621).
        Assert.Equal(ApprovalRequest.Kind.Tool, approval.RequestKind);
        Assert.Equal("mcp__srv__do", approval.Title);
        Assert.Equal("{\n  \"z\" : 1\n}", approval.Detail); // JSONValue.PrettyString's own shape (JSONValueTests.cs)
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-298")] // PiSession.swift:576
    public void HandleSelect_a_tool_kind_row_has_no_detail_for_null_or_empty_input()
    {
        var (session, events) = Make2();
        session.Handle(SelectRequest("a5", "droppy-gate " + JSONValue.FromDictionaryLiteral(("kind", "tool"), ("toolName", "webfetch"), ("toolCallId", "c1")).CompactString));
        session.Handle(SelectRequest("a6", GateTitle("tool", "webfetch", "c2")));
        var approvals = events.OfType<ProviderEvent.Approval>().ToList();
        Assert.Equal(2, approvals.Count);
        Assert.Null(approvals[0].Value.Detail); // no "input" field at all
        Assert.Null(approvals[1].Value.Detail); // "input" compacts to "{}"
    }

    // ---- PR-PROVCLI-299 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:593-600,606-607
    // PA-0008: the bounded-tail collector itself (Core/Support/Shell.swift:244-321) is tested at ShellTests;
    // this test proves only DidClose's own branching over that tail.
    public void DidClose_an_active_turn_reports_failed_with_a_null_error_when_the_stderr_tail_is_empty()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Build);
        session.DidClose(1); // no live process: the stderr tail is empty
        var turn = Assert.IsType<ProviderEvent.TurnCompleted>(events[0]);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Null(turn.Error);
        var exited = Assert.IsType<ProviderEvent.Exited>(events[1]);
        Assert.Equal("Pi exited with status 1", exited.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:597-599
    public async Task DidClose_an_interrupted_active_turn_reports_interrupted()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Build);
        await session.Interrupt(); // no process: only sets the flag
        session.DidClose(0);
        var turn = Assert.IsType<ProviderEvent.TurnCompleted>(events[0]);
        Assert.Equal(TurnStatus.Interrupted, turn.Status);
        var exited = Assert.IsType<ProviderEvent.Exited>(events[1]);
        Assert.Null(exited.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:601
    public void DidClose_after_stop_still_reports_the_turn_but_suppresses_the_exit()
    {
        var (session, events) = Make2();
        session.TestBeginTurn(InteractionMode.Build);
        session.Stop();
        session.DidClose(1);
        Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()); // the turn branch runs before the stopped guard
        Assert.Empty(events.OfType<ProviderEvent.Exited>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:603-604
    public void DidClose_a_zero_status_with_no_active_turn_exits_cleanly()
    {
        var (session, events) = Make2();
        session.DidClose(0);
        var exited = Assert.Single(events.OfType<ProviderEvent.Exited>());
        Assert.Null(exited.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:605-607
    public void DidClose_a_nonzero_status_formats_the_signed_status_in_the_exit_message()
    {
        var (session, events) = Make2();
        session.DidClose(-1);
        var exited = Assert.Single(events.OfType<ProviderEvent.Exited>());
        Assert.Equal("Pi exited with status -1", exited.Error);
    }

    // B3 delta (N1): at 1.7.0, DidClose's first statement releases every waiting request through the shared
    // PendingReplies, so a request still waiting when pi exits returns at once instead of hanging for the rest
    // of its 30 second timeout. This did not exist at B2 (the old per-file continuation dictionary was only
    // ever drained by its own 30 second Task, never by didClose), so there is no earlier version of this test.
    [Fact]
    [Trait("Req", "PR-PROVCLI-299")] // PiSession.swift:594
    [Trait("Req", "PR-PROVCLI-276")] // PiSession.swift:266
    public async Task DidClose_resolves_a_still_waiting_request_with_null_at_once_instead_of_after_thirty_seconds()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("didclose-releases-pending-request");
        var (session, _, startedScript, _) = await Started(runtime, script);

        Task<JSONValue>? pending = null;
        runtime.Loop.Enqueue(() => pending = session.Request(JSONValue.FromDictionaryLiteral(("type", "get_state"))));
        runtime.RunUntilIdle();
        // The extra request (droppy-3, after the handshake's droppy-1/droppy-2) must actually reach the real
        // child's stdin before didClose can matter: its own background reader is not on this manual loop.
        runtime.Loop.RunUntil(() => startedScript.StdinLines().Count == 3, TimeSpan.FromSeconds(20));
        Assert.False(pending!.IsCompleted); // no reply was ever scripted for this line

        runtime.Loop.Enqueue(() => session.DidClose(1)); // pi exits mid-request, with no reply queued
        runtime.RunUntilIdle();
        var value = runtime.Loop.Run(() => pending!, TimeSpan.FromSeconds(5)); // resolves without advancing the clock
        Assert.IsType<JSONValue.Null>(value);
    }

    // ---- PR-PROVCLI-300 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-300")] // PiSession.swift:75
    public void IsRunning_is_false_before_start_even_conceptually()
    {
        var (session, _) = Make2();
        Assert.False(session.IsRunning); // started is false
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-300")] // PiSession.swift:75 - no process means false, even when started and not stopped
    public void IsRunning_is_false_when_started_but_no_process_is_attached()
    {
        var (session, _) = Make2();
        session.TestSetStartedForTesting(true);
        Assert.False(session.IsRunning);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-300")] // PiSession.swift:75
    public void IsRunning_is_false_after_stop()
    {
        var (session, _) = Make2();
        session.Stop();
        Assert.False(session.IsRunning); // stopped is true, regardless of the other two flags
    }

    // ---- UI-PROVCLI-010, UI-PROVCLI-011 ----
    // The header ("Pi") and the choice labels ("Yes"/"No") are proven above by
    // HandleExtensionUI_confirm_emits_a_yes_no_question_with_a_pi_header (UI-PROVCLI-010 first choice,
    // UI-PROVCLI-011 second choice). The click -> confirmed mapping is on the wire and needs a live process
    // (R-TEST-8): proven through the compiled Pi stand-in, driving both branches of the ordinal comparison.

    [Fact]
    [Trait("Req", "UI-PROVCLI-010")] // PiSession.swift:249,506
    [Trait("Req", "UI-PROVCLI-011")] // PiSession.swift:249,506
    public async Task AnswerQuestion_confirm_maps_exactly_Yes_to_true_and_anything_else_to_false()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("confirm-yes-no");
        var (session, events, startedScript, _) = await Started(runtime, script);

        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q1\",\"method\":\"confirm\",\"params\":{\"title\":\"Proceed?\"}}"));
        var q1 = events.OfType<ProviderEvent.Question>().Last().Value;
        Assert.Equal("Pi", q1.Questions[0].Header); // UI-PROVCLI-012
        session.AnswerQuestion(q1.Id, new ValueMap<string, ValueList<string>>(
            new Dictionary<string, ValueList<string>> { ["a"] = new ValueList<string>(["Yes"]) }));
        Assert.True(Json(WaitForStdinLine(startedScript, 2))["confirmed"]!.JSONValueBool); // UI-PROVCLI-010

        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q2\",\"method\":\"confirm\",\"params\":{\"title\":\"Proceed?\"}}"));
        var q2 = events.OfType<ProviderEvent.Question>().Last().Value;
        session.AnswerQuestion(q2.Id, new ValueMap<string, ValueList<string>>(
            new Dictionary<string, ValueList<string>> { ["a"] = new ValueList<string>(["No"]) }));
        Assert.False(Json(WaitForStdinLine(startedScript, 3))["confirmed"]!.JSONValueBool); // UI-PROVCLI-011

        // The comparison is ordinal and against the literal "Yes" only: any other text, a translated or re-cased label included, answers no.
        session.Handle(Json("{\"type\":\"extension_ui_request\",\"id\":\"q3\",\"method\":\"confirm\",\"params\":{\"title\":\"Proceed?\"}}"));
        var q3 = events.OfType<ProviderEvent.Question>().Last().Value;
        session.AnswerQuestion(q3.Id, new ValueMap<string, ValueList<string>>(
            new Dictionary<string, ValueList<string>> { ["a"] = new ValueList<string>(["yes"]) }));
        Assert.False(Json(WaitForStdinLine(startedScript, 4))["confirmed"]!.JSONValueBool);
    }
}

