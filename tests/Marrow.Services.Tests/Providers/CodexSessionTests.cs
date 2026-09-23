using System.Reflection;
using System.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/CodexSession.swift (B2 1fbf81e), :1-1052.
//
// Tests for unit Services.Providers.CodexSession part 1 of 4 (PR-PROVCLI-065 to 088): the executable guard,
// the connect handshake (fixed "app-server" argv, bare-line framing, the Marrow client identity, the 20 second
// watchdog), start()'s resume/new-thread paths and requestThread's MCP-server retry, send(), interrupt(),
// compact(), rollback()'s paging/confirm path, threadParameters and policySettings. Each test spawns
// tests/fixtures/codex-cli/CodexStandIn.cs, a compiled offline stand-in (R-TEST-8, R-TEST-10; never a real AI
// CLI), driven through tests/Marrow.Services.Tests/Fixtures/CodexStandIns.cs. CodexSession's watchdogs run
// through TestRuntime's manual clock (R-TASK-6): every test binds one and drives pending work with Pump,
// advancing virtual time only where a record needs the timeout itself proven.
public sealed class CodexSessionTests
{
    // See ClaudeSessionTests' identical helper: SwiftURL.Path round-trips a Windows path unchanged only when
    // built from a non-"file" scheme (ENGINEERING DECISION, test-only).
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static SessionConfiguration Config(
        CodexScript script,
        string? resumeID = null,
        string? model = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        HydraLaunch? hydra = null,
        bool antiSlopEnabled = true)
    {
        var environment = new ValueMap<string, string>([script.EnvironmentEntry]);
        return new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            Executable = FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = environment,
            ResumeID = resumeID,
            Model = model,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            Hydra = hydra,
            AntiSlopEnabled = antiSlopEnabled,
        };
    }

    private static CodexScript Script(string label)
    {
        var script = new CodexScript(label);
        script.SetResponse("initialize", "{}");
        return script;
    }

    /// <summary>Pumps the manual main loop (where CodexSession's watchdog and JSONRPCConnection's read task
    /// run) until <paramref name="task"/> finishes, interleaving small real waits so the stand-in's background
    /// pipe I/O has a chance to land and post its continuation back onto the loop.</summary>
    private static void Pump(TestRuntime runtime, Task task, int steps = 200)
    {
        for (var i = 0; i < steps && !task.IsCompleted; i++)
        {
            runtime.RunUntilIdle();
            if (task.IsCompleted) break;
            SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromMilliseconds(25));
        }
    }

    private static string RunStart(TestRuntime runtime, CodexSession session)
    {
        var task = session.Start();
        Pump(runtime, task);
        return task.GetAwaiter().GetResult();
    }

    private static void Run(TestRuntime runtime, Task task) => Pump(runtime, task);

    private static T Resolved<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "the task did not resolve");
        return task.Result;
    }

    private static T Private<T>(CodexSession session, string member) =>
        (T)typeof(CodexSession).GetProperty(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    // ---- PR-PROVCLI-065 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-065")] // CodexSession.swift:67
    public async Task Start_without_an_executable_throws_notInstalled_before_spawning_anything()
    {
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var session = new CodexSession(config);
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Start());
        Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(
            "Codex is not installed. Install it and sign in with `codex login`.",
            error.ErrorDescription);
    }

    // ---- PR-PROVCLI-066 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-066")] // CodexSession.swift:508
    public void Connect_launches_the_process_with_exactly_the_argument_app_server()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("argv");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        var session = new CodexSession(Config(script));
        RunStart(runtime, session);
        var call = Assert.Single(script.Calls());
        Assert.Equal(["app-server"], call.Arguments);
    }

    // ---- PR-PROVCLI-067 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-067")] // CodexSession.swift:509
    public void Every_outbound_frame_is_a_bare_line_with_no_jsonrpc_member_and_ids_start_at_one()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("framing");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        var session = new CodexSession(Config(script));
        RunStart(runtime, session);

        var first = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 0)))!;
        Assert.Null(first["jsonrpc"]);
        Assert.Equal(1L, first["id"]!.JSONValueInt);
        Assert.Equal("initialize", first["method"]!.JSONValueString);

        var second = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 1)))!;
        Assert.Null(second["jsonrpc"]);
        Assert.Equal("initialized", second["method"]!.JSONValueString);
        Assert.Null(second["id"]);
        Assert.Null(second["params"]); // a present JSON null params is removed by Write (JSONRPC.cs:108)

        var third = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 2)))!;
        Assert.Equal(2L, third["id"]!.JSONValueInt);
        Assert.Equal("thread/start", third["method"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-068 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-068")] // CodexSession.swift:517-527
    public void Connect_sends_the_marrow_client_identity_then_an_initialized_notification()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("identity");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        var session = new CodexSession(Config(script));
        RunStart(runtime, session);

        var initialize = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 0)))!;
        var clientInfo = initialize["params"]!["clientInfo"]!;
        Assert.Equal("marrow", clientInfo["name"]!.JSONValueString); // DEVIATION OD-0008 B11
        Assert.Equal("Marrow", clientInfo["title"]!.JSONValueString); // DEVIATION OD-0008 B11
        Assert.Equal(AppInfo.Current.Version, clientInfo["version"]!.JSONValueString);
        Assert.True(initialize["params"]!["capabilities"]!["experimentalApi"]!.JSONValueBool);

        var second = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 1)))!;
        Assert.Equal("initialized", second["method"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-068")] // CodexSession.swift:522-524
    public async Task Connect_closes_the_connection_and_rethrows_when_initialize_fails()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("initialize-fails");
        script.SetError("initialize", "codex refused to initialize");
        var session = new CodexSession(Config(script));
        var task = session.Start();
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<RPCError>(() => task);
        Assert.Equal("codex refused to initialize", error.Message);
        Assert.False(session.IsRunning);
    }

    // ---- PR-PROVCLI-069 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-069")] // CodexSession.swift:512-516
    public async Task Connect_closes_the_connection_after_a_20_second_watchdog_when_initialize_never_replies()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("watchdog"); // no response for "initialize": the stand-in stays silent
        var session = new CodexSession(Config(script));
        var task = session.Start();
        Run(runtime, task);
        Assert.False(task.IsCompleted);

        runtime.Advance(TimeSpan.FromSeconds(20) - TimeSpan.FromTicks(1));
        Run(runtime, task);
        Assert.False(task.IsCompleted);

        runtime.Advance(TimeSpan.FromTicks(1));
        Run(runtime, task);
        await Assert.ThrowsAnyAsync<Exception>(() => task);
        Assert.False(session.IsRunning);
    }

    // ---- PR-PROVCLI-070 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-070")] // CodexSession.swift:78-84, 100-103
    public async Task Start_clears_the_connection_when_the_thread_never_opens()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("no-thread-id");
        script.SetResponse("thread/start", "{}"); // no thread.id
        var session = new CodexSession(Config(script));
        var task = session.Start();
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex did not start a thread.", error.ErrorDescription);
        Assert.False(session.IsRunning);
    }

    // ---- PR-PROVCLI-071 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-071")] // CodexSession.swift:224-232
    public void ThreadParameters_carries_cwd_approvalPolicy_sandbox_and_approvalsReviewer_rebuilt_fresh()
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("params");
        var config = Config(script, runtimeMode: RuntimeMode.Supervised);
        var session = new CodexSession(config);
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        Assert.Equal(FileUrl(Path.GetDirectoryName(script.ExecutablePath)!).Path, parameters["cwd"].JSONValueString);
        Assert.Equal("untrusted", parameters["approvalPolicy"].JSONValueString);
        Assert.Equal("read-only", parameters["sandbox"].JSONValueString); // string form, unlike turn's sandboxPolicy
        Assert.Equal("user", parameters["approvalsReviewer"].JSONValueString);
        Assert.False(parameters.ContainsKey("model")); // configuration.Model is null

        var withModel = new CodexSession(Config(script, model: "gpt-5-codex"));
        Assert.Equal("gpt-5-codex", Private<Dictionary<string, JSONValue>>(withModel, "ThreadParameters")["model"].JSONValueString);
    }

    // ---- PR-PROVCLI-072 ----

    [Theory]
    [InlineData(RuntimeMode.Supervised, "untrusted", "read-only", "readOnly", "user")]
    [InlineData(RuntimeMode.AutoAcceptEdits, "on-request", "workspace-write", "workspaceWrite", "user")]
    [InlineData(RuntimeMode.Auto, "on-request", "workspace-write", "workspaceWrite", "auto_review")]
    [InlineData(RuntimeMode.FullAccess, "never", "danger-full-access", "dangerFullAccess", "user")]
    [Trait("Req", "PR-PROVCLI-072")] // CodexSession.swift:1040-1051
    public void PolicySettings_maps_each_runtime_mode_to_its_exact_four_values(
        RuntimeMode mode, string approvalPolicy, string sandbox, string sandboxPolicyType, string reviewer)
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("policy");
        var session = new CodexSession(Config(script, runtimeMode: mode));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        Assert.Equal(approvalPolicy, parameters["approvalPolicy"].JSONValueString);
        Assert.Equal(sandbox, parameters["sandbox"].JSONValueString);
        Assert.Equal(reviewer, parameters["approvalsReviewer"].JSONValueString);

        // The turn-side table (PR-PROVCLI-080) carries the OBJECT form under "sandboxPolicy" instead.
        var input = new TurnInput
        {
            Text = "hi",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = mode,
            InteractionMode = InteractionMode.Build,
        };
        var sendMethod = typeof(CodexSession).GetMethod("PolicySettingsFor", BindingFlags.Static | BindingFlags.NonPublic)!;
        var policy = sendMethod.Invoke(null, [mode])!;
        var sandboxPolicy = (JSONValue)policy.GetType().GetProperty("SandboxPolicy")!.GetValue(policy)!;
        Assert.Equal(sandboxPolicyType, sandboxPolicy["type"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-073 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-073")] // CodexSession.swift:234-237
    public void ThreadParameters_merges_hydra_config_and_sets_developer_instructions_when_hydra_runs_natively()
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("hydra-native");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Codex, RunsNatively = true, MaxHeads = 3 };
        var session = new CodexSession(Config(script, hydra: hydra, antiSlopEnabled: false));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        var config = parameters["config"].JSONValueObject!;
        Assert.Equal(3L, config["agents"]!["max_concurrent_threads_per_session"]!.JSONValueInt);
        var instructions = parameters["developerInstructions"].JSONValueString!;
        // PR-PROVCLI-075 (:249-257) always appends the anti-slop text afterwards, even when disabled: Swift's
        // AntiSlopPolicy.disabledInstructions is itself a non-empty notice, never the empty string.
        Assert.Equal(HydraPrompts.Policy(ProviderKind.Codex, hydra) + "\n\n" + AntiSlopPolicy.Instructions(false), instructions);
    }

    // ---- PR-PROVCLI-074 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-074")] // CodexSession.swift:238-246
    public void ThreadParameters_switches_multi_agent_off_and_uses_the_fallback_policy_when_hydra_heads_run_elsewhere()
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("hydra-elsewhere");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = false, MaxHeads = 2 };
        var session = new CodexSession(Config(script, hydra: hydra, antiSlopEnabled: false));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        var config = parameters["config"].JSONValueObject!;
        Assert.False(config["features"]!["multi_agent"]!.JSONValueBool);
        Assert.Equal(
            HydraPrompts.FallbackPolicy(hydra) + "\n\n" + AntiSlopPolicy.Instructions(false),
            parameters["developerInstructions"].JSONValueString);
    }

    // ---- PR-PROVCLI-075 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-075")] // CodexSession.swift:249-257
    public void ThreadParameters_always_sets_developerInstructions_joining_existing_and_anti_slop_text()
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("anti-slop-only");
        var session = new CodexSession(Config(script, antiSlopEnabled: true));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        Assert.Equal(AntiSlopPolicy.Instructions(true), parameters["developerInstructions"].JSONValueString);

        var offScript = new CodexScript("anti-slop-off");
        var offSession = new CodexSession(Config(offScript, antiSlopEnabled: false));
        var offParameters = Private<Dictionary<string, JSONValue>>(offSession, "ThreadParameters");
        Assert.True(offParameters.ContainsKey("developerInstructions"));
        // The record's prose ("the value is the EMPTY STRING") does not match the pinned Swift:
        // AntiSlopPolicy.swift:9-13 defines disabledInstructions as a non-empty "[...guidance: off]" notice, so
        // that is what AntiSlopPolicy.Instructions(false) (and this joined value) actually is; the key is still
        // always present, which is the part of the record this proves.
        Assert.Equal(AntiSlopPolicy.Instructions(false), offParameters["developerInstructions"].JSONValueString);

        var hydraScript = new CodexScript("both");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Codex, RunsNatively = true };
        var hydraSession = new CodexSession(Config(hydraScript, hydra: hydra, antiSlopEnabled: true));
        var hydraParameters = Private<Dictionary<string, JSONValue>>(hydraSession, "ThreadParameters");
        Assert.Equal(
            HydraPrompts.Policy(ProviderKind.Codex, hydra) + "\n\n" + AntiSlopPolicy.Instructions(true),
            hydraParameters["developerInstructions"].JSONValueString);
    }

    // ---- PR-PROVCLI-076 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-076")] // CodexSession.swift:248, 258-264
    public void ThreadParameters_omits_config_when_hydra_is_off_and_no_mcp_servers_are_connected()
    {
        using var storage = new StorageScratchFolder();
        var script = new CodexScript("no-config");
        var session = new CodexSession(Config(script));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        Assert.False(parameters.ContainsKey("config"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-076")] // CodexSession.swift:258-264
    public void ThreadParameters_merges_connected_mcp_servers_into_config_without_replacing_it()
    {
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(MCPPaths.Directory);
        File.WriteAllText(MCPPaths.CodexConfigURL, "{\"mcp_servers\":{\"s1\":{\"command\":\"npx\"}}}");

        var script = new CodexScript("mcp-merge");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Codex, RunsNatively = true, MaxHeads = 4 };
        var session = new CodexSession(Config(script, hydra: hydra));
        var parameters = Private<Dictionary<string, JSONValue>>(session, "ThreadParameters");
        var config = parameters["config"].JSONValueObject!;

        // The Hydra-built key from :236 survives the merge (never replaced).
        Assert.Equal(4L, config["agents"]!["max_concurrent_threads_per_session"]!.JSONValueInt);
        // codexOverrides() may add "enabled": false entries for the real host's other configured server names
        // (PR-MCP-171, out of this unit's control), so this checks the scripted server is present rather than
        // asserting the whole object.
        var servers = config["mcp_servers"]!.JSONValueObject!;
        Assert.Equal("npx", servers["s1"]!["command"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-077 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-077")] // CodexSession.swift:87-98
    public void Start_resumes_a_thread_sending_the_resume_id_and_excludeTurns_true()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resume");
        script.SetResponse("thread/resume", "{\"thread\":{\"id\":\"resume-1\"},\"model\":\"gpt-5.1-codex\"}");
        var session = new CodexSession(Config(script, resumeID: "resume-1"));
        var id = RunStart(runtime, session);
        Assert.Equal("resume-1", id);

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 2)))!;
        Assert.Equal("thread/resume", request["method"]!.JSONValueString);
        Assert.Equal("resume-1", request["params"]!["threadId"]!.JSONValueString);
        Assert.True(request["params"]!["excludeTurns"]!.JSONValueBool);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-077")] // CodexSession.swift:92-94
    public async Task Start_throws_when_the_resumed_thread_id_does_not_match()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resume-mismatch");
        script.SetResponse("thread/resume", "{\"thread\":{\"id\":\"different\"}}");
        var session = new CodexSession(Config(script, resumeID: "resume-1"));
        var task = session.Start();
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex could not resume the original conversation.", error.ErrorDescription);
    }

    // ---- PR-PROVCLI-078 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-078")] // CodexSession.swift:100-107
    public void Start_starts_a_new_thread_and_stores_the_server_minted_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("new-thread");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"server-minted\"},\"model\":\"gpt-5.1-codex\"}");
        var session = new CodexSession(Config(script));
        var id = RunStart(runtime, session);
        Assert.Equal("server-minted", id);
        Assert.True(session.IsRunning);
    }

    // ---- PR-PROVCLI-079 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-079")] // CodexSession.swift:110-127
    public void RequestThread_retries_without_mcp_servers_and_warns_on_a_configuration_error()
    {
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(MCPPaths.Directory);
        File.WriteAllText(MCPPaths.CodexConfigURL, "{\"mcp_servers\":{\"s1\":{\"command\":\"npx\"}}}");

        using var runtime = new TestRuntime();
        var script = Script("retry");
        script.SetError("thread/start", "failed to load configuration: bad mcp_servers", occurrence: 0);
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}", occurrence: 1);
        var notices = new List<Notice>();
        var session = new CodexSession(Config(script));
        session.OnEvent = e => { if (e is ProviderEvent.Notice n) notices.Add(n.Value); };
        var id = RunStart(runtime, session);
        Assert.Equal("t1", id);

        var retried = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("thread/start", retried["method"]!.JSONValueString);
        Assert.Null(retried["params"]!["config"]); // mcp_servers stripped and config removed once it was the only key

        var notice = Assert.Single(notices);
        Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
        Assert.Equal("Codex refused the MCP server configuration, so this session runs without MCP servers. Check MCP in Settings.", notice.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-079")] // CodexSession.swift:113
    public async Task RequestThread_does_not_retry_a_configuration_error_when_there_were_no_mcp_servers()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("no-retry");
        script.SetError("thread/start", "failed to load configuration: unrelated");
        var session = new CodexSession(Config(script));
        var task = session.Start();
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<RPCError>(() => task);
        Assert.Equal("failed to load configuration: unrelated", error.Message);
        Assert.Equal(3, script.MessageCount(0)); // initialize, initialized, one failed thread/start: no retry sent
    }

    // ---- PR-PROVCLI-080, 081, 082, 083, 084 ----

    private static CodexSession StartedSession(TestRuntime runtime, CodexScript script, HydraLaunch? hydra = null)
    {
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"},\"model\":\"gpt-5.1-codex\"}");
        var session = new CodexSession(Config(script, hydra: hydra));
        RunStart(runtime, session);
        return session;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-080")] // CodexSession.swift:135-143
    public void Send_builds_the_six_base_turn_fields_from_the_turns_own_runtime_mode()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-base");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{}");
        var input = new TurnInput
        {
            Text = "hello",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
        };
        Run(runtime, session.Send(input));

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("turn/start", request["method"]!.JSONValueString);
        var p = request["params"]!;
        Assert.Equal("t1", p["threadId"]!.JSONValueString);
        Assert.Equal("never", p["approvalPolicy"]!.JSONValueString); // FullAccess row, not the session's Supervised default
        Assert.Equal("user", p["approvalsReviewer"]!.JSONValueString);
        Assert.Equal("dangerFullAccess", p["sandboxPolicy"]!["type"]!.JSONValueString); // object form
        Assert.Equal("auto", p["summary"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-081")] // CodexSession.swift:131-134
    public void Send_builds_one_text_part_by_default_then_a_localImage_part_per_image()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-content");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{}");
        var input = new TurnInput
        {
            Text = "hi there",
            Images = new ValueList<Attachment>([
                new Attachment { Name = "a.png", Path = "C:\\a.png", MimeType = "image/png" },
                new Attachment { Name = "a.txt", Path = "C:\\a.txt", MimeType = "text/plain" },
            ]),
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        Run(runtime, session.Send(input));

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        var content = request["params"]!["input"]!.JSONValueArray!;
        Assert.Equal(2, content.Count); // the text-only attachment is skipped
        Assert.Equal("text", content[0]["type"]!.JSONValueString);
        Assert.Equal("hi there", content[0]["text"]!.JSONValueString);
        Assert.Equal("localImage", content[1]["type"]!.JSONValueString);
        Assert.Equal("C:\\a.png", content[1]["path"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-082")] // CodexSession.swift:144-146
    public void Send_normalizes_an_empty_effort_to_absent_and_always_sends_a_present_service_tier()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-effort");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{}");
        var input = new TurnInput
        {
            Text = "x",
            Images = ValueList<Attachment>.Empty,
            Effort = "",
            ServiceTier = "priority",
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        Run(runtime, session.Send(input));
        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Null(request["params"]!["effort"]);
        Assert.Equal("priority", request["params"]!["serviceTierForTurn"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-083")] // CodexSession.swift:147-157
    public void Send_attaches_collaborationMode_with_mixed_casing_when_a_model_is_known()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-model");
        var session = StartedSession(runtime, script); // activeModel becomes "gpt-5.1-codex" from thread/start's reply
        script.SetResponse("turn/start", "{}");
        var input = new TurnInput
        {
            Text = "x",
            Images = ValueList<Attachment>.Empty,
            Effort = "high",
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Plan,
        };
        Run(runtime, session.Send(input));
        var p = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!["params"]!;
        Assert.Equal("gpt-5.1-codex", p["model"]!.JSONValueString); // fallback to activeModel: input.Model is null
        var mode = p["collaborationMode"]!;
        Assert.Equal("plan", mode["mode"]!.JSONValueString);
        Assert.Equal("gpt-5.1-codex", mode["settings"]!["model"]!.JSONValueString);
        Assert.Equal("high", mode["settings"]!["reasoning_effort"]!.JSONValueString);
        Assert.True(mode["settings"]!["developer_instructions"]!.IsNull);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-083")] // CodexSession.swift:147
    public void Send_sends_neither_model_nor_collaborationMode_with_no_model_known()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-no-model");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}"); // no model in the reply
        var session = new CodexSession(Config(script));
        RunStart(runtime, session);
        script.SetResponse("turn/start", "{}");
        var input = new TurnInput
        {
            Text = "x",
            Images = ValueList<Attachment>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        Run(runtime, session.Send(input));
        var p = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!["params"]!;
        Assert.Null(p["model"]);
        Assert.Null(p["collaborationMode"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-084")] // CodexSession.swift:158-160
    public void Send_records_the_turn_id_returned_by_turn_start()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("turn-id");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{\"turn\":{\"id\":\"turn-1\"}}");
        var input = new TurnInput { Text = "x", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        Run(runtime, session.Send(input));

        // The stored turnID is exercised (not directly readable) through Interrupt, PR-PROVCLI-085.
        script.SetResponse("turn/interrupt", "{}");
        Run(runtime, session.Interrupt());
        var interrupt = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 4)))!;
        Assert.Equal("turn-1", interrupt["params"]!["turnId"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-085 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-085")] // CodexSession.swift:162-168
    public void Interrupt_does_nothing_without_a_thread_or_a_turn()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("interrupt-nothing");
        var session = new CodexSession(Config(script)); // never started: no connection
        Run(runtime, session.Interrupt());
        Assert.Empty(script.Calls());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-085")] // CodexSession.swift:164-166
    public void Interrupt_sends_the_leads_thread_and_turn_and_swallows_a_failure()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("interrupt");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{\"turn\":{\"id\":\"turn-9\"}}");
        Run(runtime, session.Send(new TurnInput { Text = "x", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }));
        script.SetError("turn/interrupt", "boom");
        Run(runtime, session.Interrupt()); // must not throw: try? in the Swift
        var interrupt = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 4)))!;
        Assert.Equal("t1", interrupt["params"]!["threadId"]!.JSONValueString);
        Assert.Equal("turn-9", interrupt["params"]!["turnId"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-086 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-086")] // CodexSession.swift:170-173
    public async Task Compact_throws_notRunning_without_a_connection()
    {
        var session = new CodexSession(Config(new CodexScript("compact-not-running")));
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Compact());
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-086")] // CodexSession.swift:172
    public async Task Compact_sends_thread_compact_start_and_propagates_a_server_error()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("compact");
        var session = StartedSession(runtime, script);
        script.SetError("thread/compact/start", "cannot compact right now");
        var task = session.Compact();
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<RPCError>(() => task);
        Assert.Equal("cannot compact right now", error.Message);
        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("thread/compact/start", request["method"]!.JSONValueString);
        Assert.Equal("t1", request["params"]!["threadId"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-087, 088 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-087")] // CodexSession.swift:186-203
    public void Rollback_pages_thread_turns_list_in_descending_pages_of_100_counting_to_the_target()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-page");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list",
            "{\"data\":[{\"id\":\"t3\"},{\"id\":\"t2\"},{\"id\":\"target\"},{\"id\":\"before\"}]}");
        script.SetResponse("thread/rollback",
            "{\"thread\":{\"id\":\"t1\",\"turns\":[{\"id\":\"before\"}]}}");

        var task = session.Rollback("target");
        Run(runtime, task);
        var result = Resolved(task);
        Assert.Equal("t1", result);

        var page = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("thread/turns/list", page["method"]!.JSONValueString);
        Assert.Equal(100L, page["params"]!["limit"]!.JSONValueInt);
        Assert.Equal("desc", page["params"]!["sortDirection"]!.JSONValueString);
        Assert.Equal("notLoaded", page["params"]!["itemsView"]!.JSONValueString);

        var rollback = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 4)))!;
        Assert.Equal(3L, rollback["params"]!["numTurns"]!.JSONValueInt); // t3, t2, target: three turns walked
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-087")] // CodexSession.swift:191-192, 195
    public async Task Rollback_throws_the_exact_message_when_a_page_has_no_data_array_or_a_turn_has_no_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-no-data");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{}");
        var task = session.Rollback("target");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex did not return its conversation history.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-087")] // CodexSession.swift:204
    public async Task Rollback_throws_when_the_target_turn_is_never_found()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-not-found");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"a\"},{\"id\":\"b\"}]}"); // no nextCursor: loop ends
        var task = session.Rollback("missing");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex could not find the selected turn. The conversation was kept.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-087")] // CodexSession.swift:177-181
    public async Task Rollback_closes_the_connection_after_a_30_second_watchdog()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-watchdog");
        var session = StartedSession(runtime, script);
        // No response for thread/turns/list: the request hangs until the watchdog closes the connection.
        var task = session.Rollback("target");
        Run(runtime, task);
        Assert.False(task.IsCompleted);

        runtime.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        Run(runtime, task);
        Assert.False(task.IsCompleted);

        runtime.Advance(TimeSpan.FromTicks(1));
        Run(runtime, task);
        await Assert.ThrowsAnyAsync<Exception>(() => task);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-088")] // CodexSession.swift:207-219
    public async Task Rollback_throws_the_exact_message_when_the_server_does_not_confirm_the_rollback()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-unconfirmed");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}");
        // The server's thread.turns still contains the target: unconfirmed.
        script.SetResponse("thread/rollback", "{\"thread\":{\"id\":\"t1\",\"turns\":[{\"id\":\"target\"}]}}");
        var task = session.Rollback("target");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex did not confirm the requested conversation rollback.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-088")] // CodexSession.swift:220-221
    public void Rollback_clears_the_turn_id_and_returns_the_unchanged_thread_id_on_success()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("rollback-ok");
        var session = StartedSession(runtime, script);
        script.SetResponse("turn/start", "{\"turn\":{\"id\":\"turn-1\"}}");
        Run(runtime, session.Send(new TurnInput { Text = "x", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }));

        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}");
        script.SetResponse("thread/rollback", "{\"thread\":{\"id\":\"t1\",\"turns\":[{\"id\":\"before\"}]}}");
        var task = session.Rollback("target");
        Run(runtime, task);
        Assert.Equal("t1", Resolved(task));

        // turnID was cleared: a further Interrupt has nothing to send.
        var before = script.MessageCount(0);
        Run(runtime, session.Interrupt());
        Assert.Equal(before, script.MessageCount(0));
    }
}
