using System.Reflection;
using System.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Providers;
using Marrow.Services.Store;
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

    /// <summary>Pumps a static connect-and-request task (its own Connect watchdog needs a bound MainActor) to
    /// completion and returns its result.</summary>
    private static T RunAsync<T>(TestRuntime runtime, Task<T> task)
    {
        Pump(runtime, task);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>As <see cref="RunAsync{T}"/>, but for a task that is expected to throw: pumps it and returns it
    /// for <c>Assert.ThrowsAsync</c> to await.</summary>
    private static Task<T> PumpFaulted<T>(TestRuntime runtime, Task<T> task)
    {
        Pump(runtime, task);
        return task;
    }

    private static T Resolved<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "the task did not resolve");
        return task.Result;
    }

    private static T Private<T>(CodexSession session, string member) =>
        (T)typeof(CodexSession).GetProperty(member, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    // ---- part 2 helpers (PR-PROVCLI-089 to 112) ----

    private static object InvokePrivate(CodexSession session, string method, params object?[] args) =>
        typeof(CodexSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args)!;

    private static T PrivateField<T>(CodexSession session, string name) =>
        (T)typeof(CodexSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    private static Type PendingRequestCase(string nested) =>
        typeof(CodexSession).GetNestedType("PendingRequest", BindingFlags.NonPublic)!
            .GetNestedType(nested, BindingFlags.Public | BindingFlags.NonPublic)!;

    private static void SeedDecision(CodexSession session, string requestID, RPCID id, IReadOnlyDictionary<string, JSONValue> payloads)
    {
        var pending = Activator.CreateInstance(PendingRequestCase("Decision"), id, new ValueMap<string, JSONValue>(payloads))!;
        AddPending(session, requestID, pending);
    }

    private static void SeedPermissions(CodexSession session, string requestID, RPCID id, JSONValue requested)
    {
        var pending = Activator.CreateInstance(PendingRequestCase("Permissions"), id, requested)!;
        AddPending(session, requestID, pending);
    }

    private static void SeedQuestion(CodexSession session, string requestID, RPCID id)
    {
        var pending = Activator.CreateInstance(PendingRequestCase("Question"), id)!;
        AddPending(session, requestID, pending);
    }

    private static void AddPending(CodexSession session, string requestID, object pending)
    {
        var dict = typeof(CodexSession).GetField("pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        dict.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance)!.Invoke(dict, [requestID, pending]);
    }

    private static bool HasPending(CodexSession session, string requestID)
    {
        var dict = (System.Collections.IDictionary)typeof(CodexSession).GetField("pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        return dict.Contains(requestID);
    }

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

    // ==== Part 2 of 4 (PR-PROVCLI-089 to 112) ====

    private static IReadOnlyDictionary<string, string> Env(CodexScript script) =>
        new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };

    // ---- PR-PROVCLI-089 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-089")] // CodexSession.swift:211-213, 268-301
    public async Task Rollback_forks_history_on_the_paginated_rollback_error_archives_the_original_thread_and_updates_state()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("fork-success");
        var session = StartedSession(runtime, script); // thread/start -> "t1", model "gpt-5.1-codex"
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}", occurrence: 0);
        script.SetError("thread/rollback", "codex: paginated threads do not support thread/rollback here");
        script.SetResponse("thread/fork", "{\"thread\":{\"id\":\"forked-1\"},\"model\":\"gpt-6-codex\"}");
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"before\"}]}", occurrence: 1);
        script.SetResponse("thread/archive", "{}");

        var task = session.Rollback("target");
        Run(runtime, task);
        Assert.Equal("forked-1", Resolved(task));
        Assert.Equal("forked-1", PrivateField<string?>(session, "threadID"));
        Assert.Null(PrivateField<string?>(session, "turnID"));
        Assert.Equal("gpt-6-codex", PrivateField<string?>(session, "activeModel"));

        var fork = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 5)))!;
        Assert.Equal("thread/fork", fork["method"]!.JSONValueString);
        Assert.Equal("t1", fork["params"]!["threadId"]!.JSONValueString);
        Assert.Equal("before", fork["params"]!["lastTurnId"]!.JSONValueString);
        Assert.True(fork["params"]!["excludeTurns"]!.JSONValueBool);

        var archive = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 7)))!;
        Assert.Equal("thread/archive", archive["method"]!.JSONValueString);
        Assert.Equal("t1", archive["params"]!["threadId"]!.JSONValueString); // the ORIGINAL thread, not the fork
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-089")] // CodexSession.swift:269-273
    public void ForkHistory_with_no_previousID_archives_the_original_thread_and_clears_state()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("fork-no-previous");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"}]}"); // found immediately, no cursor: previousID stays null
        script.SetError("thread/rollback", "paginated threads do not support thread/rollback");
        script.SetResponse("thread/archive", "{}");

        var task = session.Rollback("target");
        Run(runtime, task);
        Assert.Null(Resolved(task));
        Assert.Null(PrivateField<string?>(session, "threadID"));
        Assert.Null(PrivateField<string?>(session, "turnID"));

        var archive = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 5)))!;
        Assert.Equal("thread/archive", archive["method"]!.JSONValueString);
        Assert.Equal("t1", archive["params"]!["threadId"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-089")] // CodexSession.swift:280-282
    public async Task ForkHistory_throws_the_exact_message_when_the_forked_thread_id_matches_the_original()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("fork-same-id");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}");
        script.SetError("thread/rollback", "paginated threads do not support thread/rollback");
        script.SetResponse("thread/fork", "{\"thread\":{\"id\":\"t1\"}}"); // same as the original

        var task = session.Rollback("target");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex did not create a conversation at the revert point.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-089")] // CodexSession.swift:286-296
    public async Task ForkHistory_archives_the_new_thread_and_rethrows_when_the_verification_page_does_not_confirm_it()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("fork-unconfirmed");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}", occurrence: 0);
        script.SetError("thread/rollback", "paginated threads do not support thread/rollback");
        script.SetResponse("thread/fork", "{\"thread\":{\"id\":\"forked-2\"}}");
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"wrong\"}]}", occurrence: 1); // does not match "before"
        script.SetResponse("thread/archive", "{}");

        var task = session.Rollback("target");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<ProviderError>(() => task);
        Assert.Equal("Codex did not confirm the reverted conversation history.", error.ErrorDescription);

        var archive = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 7)))!;
        Assert.Equal("thread/archive", archive["method"]!.JSONValueString);
        Assert.Equal("forked-2", archive["params"]!["threadId"]!.JSONValueString); // the NEW thread, not the original
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-089")] // CodexSession.swift:207-213
    public async Task Rollback_does_not_fork_on_a_different_rollback_error()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("fork-not-triggered");
        var session = StartedSession(runtime, script);
        script.SetResponse("thread/turns/list", "{\"data\":[{\"id\":\"target\"},{\"id\":\"before\"}]}");
        script.SetError("thread/rollback", "unrelated failure");

        var task = session.Rollback("target");
        Run(runtime, task);
        var error = await Assert.ThrowsAsync<RPCError>(() => task);
        Assert.Equal("unrelated failure", error.Message);
        Assert.Equal(5, script.MessageCount(0)); // initialize, initialized, thread/start, thread/turns/list, thread/rollback: no fork call
    }

    // ---- PR-PROVCLI-090, 091, 092 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-090")] // CodexSession.swift:306-307, 317
    public void ResolveApproval_decision_echoes_the_servers_own_payload_for_the_chosen_option()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-decision");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        SeedDecision(session, "req1", new RPCID.Int(7), new Dictionary<string, JSONValue>
        {
            ["accept"] = new JSONValue.String("accept-payload"),
        });

        session.ResolveApproval("req1", "accept");

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal(7L, response["id"]!.JSONValueInt);
        Assert.Equal("accept-payload", response["result"]!["decision"]!.JSONValueString);
        var resolved = Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
        Assert.Equal("req1", resolved.Id);
        Assert.False(HasPending(session, "req1"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-090")] // CodexSession.swift:307
    public void ResolveApproval_decision_falls_back_to_cancel_for_an_unknown_option_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-decision-unknown");
        var session = StartedSession(runtime, script);
        SeedDecision(session, "req1", new RPCID.Int(9), new Dictionary<string, JSONValue> { ["accept"] = new JSONValue.String("x") });

        session.ResolveApproval("req1", "not-a-real-option");

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("cancel", response["result"]!["decision"]!.JSONValueString);
    }

    [Theory]
    [InlineData("turn")]
    [InlineData("session")]
    [Trait("Req", "PR-PROVCLI-091")] // CodexSession.swift:309-310
    public void ResolveApproval_permissions_echoes_the_requested_set_for_turn_or_session_scope(string optionID)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-permissions-" + optionID);
        var session = StartedSession(runtime, script);
        var requested = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["fileSystem"] = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["write"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("/a")])) })),
        }));
        SeedPermissions(session, "req1", new RPCID.Int(1), requested);

        session.ResolveApproval("req1", optionID);

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal(optionID, response["result"]!["scope"]!.JSONValueString);
        Assert.Equal("/a", response["result"]!["permissions"]!["fileSystem"]!["write"]![0]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-091")] // CodexSession.swift:312
    public void ResolveApproval_permissions_declines_to_an_empty_object_with_turn_scope()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-permissions-decline");
        var session = StartedSession(runtime, script);
        var requested = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue> { ["network"] = true }));
        SeedPermissions(session, "req1", new RPCID.Int(2), requested);

        session.ResolveApproval("req1", "decline");

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("turn", response["result"]!["scope"]!.JSONValueString);
        Assert.Empty((System.Collections.IDictionary)response["result"]!["permissions"]!.JSONValueObject!);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-092")] // CodexSession.swift:314-316
    public void ResolveApproval_question_answers_with_an_empty_answers_object()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-question");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        SeedQuestion(session, "req1", new RPCID.Int(3));

        session.ResolveApproval("req1", "whatever");

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Empty((System.Collections.IDictionary)response["result"]!["answers"]!.JSONValueObject!);
        Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-090")] // CodexSession.swift:304
    public void ResolveApproval_with_an_unknown_request_key_does_not_respond()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("resolve-unknown-key");
        var session = StartedSession(runtime, script);
        var before = script.MessageCount(0);
        session.ResolveApproval("not-pending", "accept");
        Assert.Equal(before, script.MessageCount(0));
    }

    // ---- PR-PROVCLI-093 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-093")] // CodexSession.swift:322-328
    public void AnswerQuestion_nests_each_questions_answers_array_under_its_own_answers_object()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("answer-question");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        SeedQuestion(session, "req1", new RPCID.Int(11));

        session.AnswerQuestion("req1", new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>>
        {
            ["q1"] = new ValueList<string>(["red", "blue"]),
        }));

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal(11L, response["id"]!.JSONValueInt);
        var q1 = response["result"]!["answers"]!["q1"]!;
        Assert.Equal("red", q1["answers"]![0]!.JSONValueString);
        Assert.Equal("blue", q1["answers"]![1]!.JSONValueString);
        Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-093")] // CodexSession.swift:321
    public void AnswerQuestion_silently_drops_a_non_question_pending_entry()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("answer-non-question");
        var session = StartedSession(runtime, script);
        SeedDecision(session, "req1", new RPCID.Int(1), new Dictionary<string, JSONValue>());
        var before = script.MessageCount(0);

        session.AnswerQuestion("req1", ValueMap<string, ValueList<string>>.Empty);

        Assert.Equal(before, script.MessageCount(0)); // no response sent
        Assert.False(HasPending(session, "req1")); // but it was removed
    }

    // ---- PR-PROVCLI-094 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-094")] // CodexSession.swift:330-333, 1036
    public void Stop_marks_stopping_before_closing_and_suppresses_the_exited_event()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("stop");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var before = script.MessageCount(0);

        session.Stop();
        runtime.Loop.RunUntil(() => !session.IsRunning, TimeSpan.FromSeconds(5));

        Assert.False(session.IsRunning);
        Assert.Equal(before, script.MessageCount(0)); // no shutdown request sent, unlike Copilot
        Assert.Empty(events.OfType<ProviderEvent.Exited>());
    }

    // ---- PR-PROVCLI-095 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:336-340
    public void StopAgent_interrupts_that_heads_own_thread_and_turn()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("stop-agent");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        PrivateField<Dictionary<string, string>>(session, "headTurns")["head-1"] = "turn-1";
        script.SetResponse("turn/interrupt", "{}");

        var task = session.StopAgent("head-1");
        Run(runtime, task);
        Assert.True(Resolved(task));

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 3)))!;
        Assert.Equal("turn/interrupt", request["method"]!.JSONValueString);
        Assert.Equal("head-1", request["params"]!["threadId"]!.JSONValueString);
        Assert.Equal("turn-1", request["params"]!["turnId"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:336
    public void StopAgent_returns_false_for_an_unknown_head_or_a_head_with_no_running_turn()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("stop-agent-unknown");
        var session = StartedSession(runtime, script);
        Assert.False(Resolved(session.StopAgent("nope")));

        PrivateField<HashSet<string>>(session, "headThreads").Add("head-2"); // known head, but no recorded turn
        Assert.False(Resolved(session.StopAgent("head-2")));
    }

    // ---- PR-PROVCLI-096 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-096")] // CodexSession.swift:346-352
    public void ListCommands_asks_skills_list_with_forceReload_and_maps_the_result()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("list-commands");
        script.SetResponse("initialize", "{}");
        script.SetResponse("skills/list", "{\"data\":[{\"skills\":[{\"name\":\"plan\",\"path\":\"/skills/plan\",\"description\":\"Plan the work\"}]}]}");

        var commands = RunAsync(runtime, CodexSession.ListCommands(FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), Env(script)));

        var command = Assert.Single(commands);
        Assert.Equal("plan", command.Name);
        Assert.Equal("Plan the work", command.Detail);
        Assert.Equal("/skills/plan", command.SkillPath);

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 2)))!;
        Assert.Equal("skills/list", request["method"]!.JSONValueString);
        Assert.True(request["params"]!["forceReload"]!.JSONValueBool);
        Assert.Single(request["params"]!["cwds"]!.JSONValueArray!);
    }

    // ---- PR-PROVCLI-097, 098 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-097")] // CodexSession.swift:363-383
    public void ListModels_maps_fields_skips_hidden_entries_and_stops_when_nextCursor_is_null()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("list-models");
        script.SetResponse("initialize", "{}");
        script.SetResponse("model/list",
            "{\"data\":[" +
            "{\"id\":\"m1\",\"displayName\":\"Model One\",\"description\":\"d1\",\"supportedReasoningEfforts\":[{\"reasoningEffort\":\"low\"},{\"reasoningEffort\":\"high\"}],\"defaultReasoningEffort\":\"low\",\"isDefault\":true,\"serviceTiers\":[{\"id\":\"tier-fast\",\"name\":\"Fast Lane\"}]}," +
            "{\"id\":\"m2\",\"hidden\":true}" +
            "],\"nextCursor\":null}");

        var models = RunAsync(runtime, CodexSession.ListModels(FileUrl(script.ExecutablePath), Env(script)));

        var model = Assert.Single(models);
        Assert.Equal("m1", model.Id);
        Assert.Equal("Model One", model.Name);
        Assert.Equal("d1", model.Detail);
        Assert.Equal(["low", "high"], model.Efforts);
        Assert.Equal("low", model.DefaultEffort);
        Assert.True(model.IsDefault);
        Assert.Equal("tier-fast", model.FastTier);
        Assert.Equal(3, script.MessageCount(0)); // initialize, initialized, one model/list: no second page
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-097")] // CodexSession.swift:365-383
    public void ListModels_stops_after_ten_pages_even_when_nextCursor_keeps_paging()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("list-models-paging");
        script.SetResponse("initialize", "{}");
        for (var i = 0; i < 10; i++)
        {
            script.SetResponse("model/list", "{\"data\":[],\"nextCursor\":\"c" + (i + 1) + "\"}", occurrence: i);
        }

        _ = RunAsync(runtime, CodexSession.ListModels(FileUrl(script.ExecutablePath), Env(script)));
        Assert.Equal(12, script.MessageCount(0)); // initialize, initialized, exactly 10 model/list calls
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-098")] // CodexSession.swift:493-500
    public void ListModels_fastTier_matches_case_insensitively_on_fast_or_priority_taking_the_first_match()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("fast-tier");
        script.SetResponse("initialize", "{}");
        script.SetResponse("model/list",
            "{\"data\":[{\"id\":\"m1\",\"serviceTiers\":[{\"id\":\"std\",\"name\":\"Standard\"},{\"id\":\"PRIORITY-tier\",\"name\":\"\"},{\"id\":\"z-fast\",\"name\":\"\"}]}," +
            "{\"id\":\"m2\",\"serviceTiers\":[{\"id\":\"none\",\"name\":\"plain\"}]}]," +
            "\"nextCursor\":null}");

        var models = RunAsync(runtime, CodexSession.ListModels(FileUrl(script.ExecutablePath), Env(script)));
        Assert.Equal("PRIORITY-tier", models[0].FastTier); // first match, case-insensitive on "priority"
        Assert.Null(models[1].FastTier); // no tier id or name contains fast/priority
    }

    // ---- PR-PROVCLI-099, 100 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-099")] // CodexSession.swift:396-405
    [Trait("Req", "PR-PROVCLI-100")] // CodexSession.swift:439-445
    public void ReadPlanLimits_turns_a_thrown_read_into_a_problem_string_instead_of_throwing()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-error");
        script.SetResponse("initialize", "{}");
        script.SetError("account/rateLimits/read", "boom");

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        Assert.NotNull(limits);
        Assert.Null(limits!.PlanName);
        Assert.Empty(limits.Windows);
        // JSONRPC.swift:31-36's RPCError is a plain LocalizedError struct with no CustomStringConvertible, so
        // String(describing:) is Swift's default struct-mirror dump; the stand-in's scripted error always
        // carries code -32000 and no "data" field (CodexStandIn.cs:114).
        Assert.Equal("RPCError(code: -32000, message: \"boom\", data: nil)", limits.Problem);
    }

    [Theory]
    [InlineData("invalid_grant: token_expired")]
    [InlineData("HTTP 401 Unauthorized")]
    [Trait("Req", "PR-PROVCLI-100")] // CodexSession.swift:441-442
    public void ReadPlanLimits_rewrites_a_token_expired_or_401_failure_to_the_login_message(string failure)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-expired");
        script.SetResponse("initialize", "{}");
        script.SetError("account/rateLimits/read", failure);

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        Assert.Equal(
            "Codex's stored login has expired. Run `codex login` in a terminal, then check again.",
            limits!.Problem);
    }

    // ---- PR-PROVCLI-101, 102, 103 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-101")] // CodexSession.swift:406-411
    [Trait("Req", "PR-PROVCLI-102")] // CodexSession.swift:412-428
    public void ReadPlanLimits_collects_snapshots_by_sorted_limit_id_and_builds_two_windows_each()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-windows");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimits/read",
            "{\"rateLimitsByLimitId\":{" +
            "\"b-limit\":{\"planType\":\"pro\",\"limitId\":\"b-limit\",\"limitName\":\"Weekly\",\"primary\":{\"usedPercent\":40.0,\"windowDurationMins\":10080,\"resetsAt\":1700000000}}," +
            "\"a-limit\":{\"planType\":\"pro\",\"limitId\":\"a-limit\",\"limitName\":\"5h\",\"primary\":{\"usedPercent\":10.0,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":5.0}}" +
            "}}");

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        Assert.NotNull(limits);
        Assert.Equal(3, limits!.Windows.Count); // a-limit primary+secondary, b-limit primary
        // "a-limit" sorts before "b-limit": its windows come first.
        Assert.Equal("a-limit-primary", limits.Windows[0].Id);
        Assert.Equal(PlanLimitsReader.WindowTitle(300) + " · 5h", limits.Windows[0].Title); // U+00B7 MIDDLE DOT
        Assert.Equal(10.0, limits.Windows[0].Percent);
        Assert.Equal("a-limit-secondary", limits.Windows[1].Id);
        Assert.Equal(PlanLimitsReader.WindowTitle(null) + " · 5h", limits.Windows[1].Title);
        Assert.Equal("b-limit-primary", limits.Windows[2].Id);
        Assert.Equal(PlanLimitsReader.WindowTitle(10080) + " · Weekly", limits.Windows[2].Title);
        Assert.Equal(SwiftDate.From1970Seconds(1700000000), limits.Windows[2].ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-101")] // CodexSession.swift:409-410
    [Trait("Req", "PR-PROVCLI-102")] // CodexSession.swift:416 (single snapshot: no limitName suffix)
    public void ReadPlanLimits_falls_back_to_the_single_rateLimits_snapshot_with_no_name_suffix()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-single");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimits/read",
            "{\"rateLimits\":{\"planType\":\"pro\",\"limitName\":\"ignored\",\"primary\":{\"usedPercent\":50.0}}}");

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        var window = Assert.Single(limits!.Windows);
        Assert.Equal(PlanLimitsReader.WindowTitle(null), window.Title); // no " · ignored" suffix: only one snapshot
        Assert.Equal("codex-primary", window.Id); // no limitId in this snapshot: falls back to "codex"
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-103")] // CodexSession.swift:429-434
    public void ReadPlanLimits_with_no_windows_returns_a_problem_string_not_null()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-empty");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimits/read", "{\"rateLimits\":{\"planType\":\"pro\"}}"); // no primary/secondary usedPercent

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        Assert.NotNull(limits);
        Assert.Empty(limits!.Windows);
        Assert.Equal("Codex reported no usage windows for this account.", limits.Problem);
        Assert.Equal(PlanLimitsReader.CodexPlanName("pro"), limits.PlanName);
    }

    // ---- PR-PROVCLI-104, 105 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-104")] // CodexSession.swift:447-462
    [Trait("Req", "PR-PROVCLI-105")] // CodexSession.swift:464-473
    public void ReadPlanLimits_parses_reset_credits_clamping_negative_counts_and_dropping_bad_entries()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-credits");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimits/read",
            "{\"rateLimits\":{\"primary\":{\"usedPercent\":1.0}}," +
            "\"rateLimitResetCredits\":{\"availableCount\":-2,\"credits\":[" +
            "{\"id\":\"\",\"title\":\"empty-id-dropped\"}," +
            "{\"id\":\"c1\",\"status\":\"redeemed\"}," + // status neither available nor unknown nor absent: dropped
            "{\"id\":\"c2\",\"status\":\"available\",\"title\":\"Free reset\",\"description\":\"one time\",\"expiresAt\":1700003600}," +
            "{\"id\":\"c3\",\"status\":\"unknown\",\"expiresAt\":1700003600000}," + // epoch ms (> 1e10)
            "{\"id\":\"c4\",\"expiresAt\":\"2023-11-15T00:00:00Z\"}" + // missing status: kept; ISO 8601
            "]}}");

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        var credits = limits!.PlanLimitsResetCredits!;
        Assert.Equal(0L, credits.AvailableCount); // -2 clamped to 0
        Assert.Equal(["c2", "c3", "c4"], credits.Credits.Select(c => c.Id));
        var c2 = credits.Credits[0];
        Assert.Equal("Free reset", c2.Title);
        Assert.Equal("one time", c2.Detail);
        Assert.Equal(SwiftDate.From1970Seconds(1700003600), c2.ExpiresAt);
        Assert.Equal(SwiftDate.From1970Seconds(1700003600), credits.Credits[1].ExpiresAt); // ms divided by 1000
        Assert.Equal(ISO8601.Date("2023-11-15T00:00:00Z"), credits.Credits[2].ExpiresAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-104")] // CodexSession.swift:449
    public void ReadPlanLimits_with_no_available_count_leaves_reset_credits_null()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("plan-limits-no-count");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimits/read", "{\"rateLimits\":{\"primary\":{\"usedPercent\":1.0}},\"rateLimitResetCredits\":{\"credits\":[]}}");

        var limits = RunAsync(runtime, CodexSession.ReadPlanLimits(FileUrl(script.ExecutablePath), Env(script)));
        Assert.Null(limits!.PlanLimitsResetCredits);
    }

    // ---- PR-PROVCLI-106 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-106")] // CodexSession.swift:483-490
    public void ConsumeResetCredit_sends_a_fresh_lowercased_idempotency_key_and_the_credit_id_when_given()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("consume-credit");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimitResetCredit/consume", "{\"outcome\":\"reset\"}");

        var outcome = RunAsync(runtime, CodexSession.ConsumeResetCredit(FileUrl(script.ExecutablePath), Env(script), "credit-1"));
        Assert.Equal(PlanLimits.ResetOutcome.Reset, outcome);

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 2)))!;
        Assert.Equal("account/rateLimitResetCredit/consume", request["method"]!.JSONValueString);
        Assert.Equal("credit-1", request["params"]!["creditId"]!.JSONValueString);
        var key = request["params"]!["idempotencyKey"]!.JSONValueString!;
        Assert.Equal(key, key.ToLowerInvariant());
        Assert.True(Guid.TryParse(key, out _));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-106")] // CodexSession.swift:485
    public void ConsumeResetCredit_omits_creditId_when_none_is_given_so_the_server_picks_one()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("consume-credit-none");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimitResetCredit/consume", "{\"outcome\":\"noCredit\"}");

        var outcome = RunAsync(runtime, CodexSession.ConsumeResetCredit(FileUrl(script.ExecutablePath), Env(script), null));
        Assert.Equal(PlanLimits.ResetOutcome.NoCredit, outcome);

        var request = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, 2)))!;
        Assert.Null(request["params"]!["creditId"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-106")] // CodexSession.swift:487-488
    public async Task ConsumeResetCredit_throws_unknownOutcome_for_an_unrecognized_outcome_string()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("consume-credit-unknown");
        script.SetResponse("initialize", "{}");
        script.SetResponse("account/rateLimitResetCredit/consume", "{\"outcome\":\"somethingElse\"}");

        var faulted = PumpFaulted(runtime, CodexSession.ConsumeResetCredit(FileUrl(script.ExecutablePath), Env(script), null));
        var error = await Assert.ThrowsAsync<ResetCreditError>(() => faulted);
        Assert.Equal("Codex returned an unknown reset result.", error.ErrorDescription);
    }

    // ---- PR-PROVCLI-107, 108 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-107")] // CodexSession.swift:538-541
    public void HandleNotification_routes_a_known_heads_notifications_wrapped_in_agentEvent()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("route-head");
        var session = StartedSession(runtime, script); // threadID = "t1"
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/agentMessage/delta",
            JSONValue.FromDictionaryLiteral(("threadId", "head-1"), ("itemId", "m1"), ("delta", "hi")));

        var wrapped = Assert.Single(events.OfType<ProviderEvent.AgentEvent>());
        Assert.Equal("head-1", wrapped.AgentID);
        var inner = Assert.IsType<ProviderEvent.MessageDelta>(wrapped.Value);
        Assert.Equal("m1", inner.Id);
        Assert.Equal("hi", inner.Text);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-107")] // CodexSession.swift:539-540
    public void HandleNotification_drops_a_notification_for_an_unrelated_unregistered_thread()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("route-unrelated");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/agentMessage/delta",
            JSONValue.FromDictionaryLiteral(("threadId", "not-a-head"), ("itemId", "m1"), ("delta", "hi")));

        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-107")] // CodexSession.swift:538
    public void HandleNotification_with_no_threadId_or_a_matching_threadId_routes_to_the_lead()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("route-lead");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/agentMessage/delta",
            JSONValue.FromDictionaryLiteral(("itemId", "m1"), ("delta", "hi"))); // no threadId
        InvokePrivate(session, "HandleNotification", "item/agentMessage/delta",
            JSONValue.FromDictionaryLiteral(("threadId", "t1"), ("itemId", "m2"), ("delta", "there"))); // matches the lead

        Assert.Equal(2, events.OfType<ProviderEvent.MessageDelta>().Count());
        Assert.Empty(events.OfType<ProviderEvent.AgentEvent>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-108")] // CodexSession.swift:557-568, 573-580, 592-593, 655-658
    public void HandleNotification_emits_the_directly_mapped_delta_and_diff_and_notice_events()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("switch-directly-mapped");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/reasoning/textDelta", JSONValue.FromDictionaryLiteral(("itemId", "r1"), ("delta", "think")));
        InvokePrivate(session, "HandleNotification", "item/reasoning/summaryTextDelta", JSONValue.FromDictionaryLiteral(("itemId", "r1"), ("delta", "more")));
        InvokePrivate(session, "HandleNotification", "item/commandExecution/outputDelta", JSONValue.FromDictionaryLiteral(("itemId", "c1"), ("delta", "out")));
        InvokePrivate(session, "HandleNotification", "item/plan/delta", JSONValue.FromDictionaryLiteral(("itemId", "p1"), ("delta", "step")));
        InvokePrivate(session, "HandleNotification", "turn/diff/updated", JSONValue.FromDictionaryLiteral(("diff", "+x")));
        InvokePrivate(session, "HandleNotification", "thread/compacted", JSONValue.FromDictionaryLiteral(("x", true)));
        InvokePrivate(session, "HandleNotification", "unknownMethod", JSONValue.FromDictionaryLiteral(("x", true)));

        Assert.Equal(2, events.OfType<ProviderEvent.ReasoningDelta>().Count());
        Assert.Single(events.OfType<ProviderEvent.ToolOutput>());
        Assert.Single(events.OfType<ProviderEvent.PlanDelta>());
        var diff = Assert.Single(events.OfType<ProviderEvent.Diff>());
        Assert.Equal("+x", diff.Value);
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel);
        Assert.Equal("Context compacted.", notice.Value.Message);
        Assert.Equal(6, events.Count); // the unknown method produced nothing
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-108")] // CodexSession.swift:569-572
    public void HandleNotification_summaryPartAdded_emits_two_newlines_only_when_the_index_is_positive()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("summary-part-added");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/reasoning/summaryPartAdded", JSONValue.FromDictionaryLiteral(("itemId", "r1"), ("summaryIndex", 0L)));
        Assert.Empty(events);
        InvokePrivate(session, "HandleNotification", "item/reasoning/summaryPartAdded", JSONValue.FromDictionaryLiteral(("itemId", "r1"), ("summaryIndex", 1L)));
        var delta = Assert.Single(events.OfType<ProviderEvent.ReasoningDelta>());
        Assert.Equal("\n\n", delta.Text);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-107")] // CodexSession.swift:548-556
    public void HandleNotification_turn_started_records_the_turn_id_on_the_lead_or_the_head()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("turn-started");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");

        InvokePrivate(session, "HandleNotification", "turn/started", JSONValue.FromDictionaryLiteral(("turn", JSONValue.FromDictionaryLiteral(("id", "turn-lead")))));
        Assert.Equal("turn-lead", PrivateField<string?>(session, "turnID"));

        InvokePrivate(session, "HandleNotification", "turn/started",
            JSONValue.FromDictionaryLiteral(("threadId", "head-1"), ("turn", JSONValue.FromDictionaryLiteral(("id", "turn-head")))));
        Assert.Equal("turn-head", PrivateField<Dictionary<string, string>>(session, "headTurns")["head-1"]);
    }

    // ---- PR-PROVCLI-109 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-109")] // CodexSession.swift:582-591
    public void HandleNotification_turn_plan_updated_maps_the_three_status_strings_and_skips_a_stepless_entry()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("plan-updated");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "turn/plan/updated", JSONValue.FromDictionaryLiteral(("plan", new JSONValue.Array(new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("step", "a"), ("status", "completed")),
            JSONValue.FromDictionaryLiteral(("step", "b"), ("status", "inProgress")),
            JSONValue.FromDictionaryLiteral(("step", "c"), ("status", "queued")), // unrecognized -> pending
            JSONValue.FromDictionaryLiteral(("status", "completed")), // no "step": skipped
        ])))));

        var todos = Assert.Single(events.OfType<ProviderEvent.Todos>());
        Assert.Equal(3, todos.Value.Count);
        Assert.Equal(TodoStep.Status.Done, todos.Value[0].TodoStepStatus);
        Assert.Equal(TodoStep.Status.Active, todos.Value[1].TodoStepStatus);
        Assert.Equal(TodoStep.Status.Pending, todos.Value[2].TodoStepStatus);
    }

    // ---- PR-PROVCLI-110, 111 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:598-612
    public void HandleNotification_tokenUsage_on_the_lead_emits_usage_and_records_the_delta_from_the_growing_total()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("token-usage-lead");
        var session = StartedSession(runtime, script); // threadID "t1"
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 100L))),
                ("modelContextWindow", 8000L),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 500L))))))));

        var usage = Assert.Single(events.OfType<ProviderEvent.Usage>());
        Assert.Equal(100, usage.Value.UsedTokens);
        Assert.Equal(8000, usage.Value.WindowTokens);
        // First update: seeded to max(0, total - used) = 400, so spend = 500 - 400 = 100.
        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal(ProviderKind.Codex, spend.Value.Provider);
        Assert.Equal(100, spend.Value.TotalTokens);
        Assert.Null(spend.Value.AgentID);

        // A second update with the total grown by 40 spends exactly the delta, not the whole total.
        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 40L))),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 540L))))))));
        Assert.Equal(40, events.OfType<ProviderEvent.TokenSpend>().Last().Value.TotalTokens);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:610-611
    public void HandleNotification_tokenUsage_without_a_positive_total_spends_the_per_update_used_value()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("token-usage-no-total");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(("last", JSONValue.FromDictionaryLiteral(("totalTokens", 25L))))))));

        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal(25, spend.Value.TotalTokens);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-111")] // CodexSession.swift:613-625
    public void HandleNotification_tokenUsage_on_a_head_tracks_its_own_spend_and_reports_agentProgress()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("token-usage-head");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        PrivateField<Dictionary<string, string>>(session, "headModels")["head-1"] = "gpt-head";
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("threadId", "head-1"),
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 30L))),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 30L))))))));

        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal(30, spend.Value.TotalTokens);
        Assert.Equal("gpt-head", spend.Value.Model);
        Assert.Equal("head-1", spend.Value.AgentID);
        var progress = Assert.Single(events.OfType<ProviderEvent.AgentProgress>());
        Assert.Equal("head-1", progress.AgentID);
        Assert.Equal(30, progress.Tokens);
        Assert.Empty(events.OfType<ProviderEvent.Usage>()); // no context meter for a head
    }

    // ---- PR-PROVCLI-112 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-112")] // CodexSession.swift:627-631
    public void HandleNotification_error_notifies_only_for_the_lead_with_willRetry_true_appending_Retrying()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("error-notification");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "error", JSONValue.FromDictionaryLiteral(
            ("willRetry", true), ("error", JSONValue.FromDictionaryLiteral(("message", "rate limited")))));
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.Equal("rate limited Retrying.", notice.Value.Message);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "error", JSONValue.FromDictionaryLiteral(
            ("willRetry", false), ("error", JSONValue.FromDictionaryLiteral(("message", "fatal")))));
        Assert.Empty(events); // willRetry false: silent here

        events.Clear();
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        InvokePrivate(session, "HandleNotification", "error", JSONValue.FromDictionaryLiteral(
            ("threadId", "head-1"), ("willRetry", true), ("error", JSONValue.FromDictionaryLiteral(("message", "head error")))));
        Assert.Empty(events); // a head's error notification produces nothing here
    }

    // ---- ErrorMessage (used by PR-PROVCLI-112 and turn/completed) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-112")] // CodexSession.swift:910-918
    public void ErrorMessage_unwraps_a_nested_json_encoded_message_and_falls_back_to_the_raw_text()
    {
        Assert.Equal("Codex reported an error.", CodexSession.ErrorMessage(null));
        Assert.Equal("plain text", CodexSession.ErrorMessage(new JSONValue.Object(new ValueMap<string, JSONValue>(
            new Dictionary<string, JSONValue> { ["message"] = "plain text" }))));
        var nested = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["message"] = "{\"error\":{\"message\":\"inner\"}}",
        }));
        Assert.Equal("inner", CodexSession.ErrorMessage(nested));
    }
}
