using System.Reflection;
using System.Text.Json;
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

// Source: DroppyCode/Services/Providers/CodexSession.swift (B3 52bc792), :1-1059.
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
        Assert.Equal("nth", clientInfo["name"]!.JSONValueString); // DEVIATION OD-0008 B11
        Assert.Equal("nth", clientInfo["title"]!.JSONValueString); // DEVIATION OD-0008 B11
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
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:338-343 (B3)
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
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:338-339 (B3)
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

    [Fact]
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:338-339 (B3)
    public void StopAgent_returns_false_for_the_leads_own_thread_id_even_if_it_is_in_headThreads()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("stop-agent-lead-id");
        var session = StartedSession(runtime, script); // threadID "t1"
        // The lead's own thread id, oddly also recorded as a head with a running turn.
        PrivateField<HashSet<string>>(session, "headThreads").Add("t1");
        PrivateField<Dictionary<string, string>>(session, "headTurns")["t1"] = "turn-1";
        var before = script.MessageCount(0);

        Assert.False(Resolved(session.StopAgent("t1")));
        Assert.Equal(before, script.MessageCount(0)); // nothing sent: the lead's own thread is never a stoppable head
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-095")] // CodexSession.swift:338 (B3)
    public void StopAgent_returns_false_and_sends_nothing_once_the_lead_thread_id_is_gone()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("stop-agent-no-thread-id");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        PrivateField<Dictionary<string, string>>(session, "headTurns")["head-1"] = "turn-1";
        // As after ForkHistory's no-previousID archive path (:269-273): the connection stays open, the head
        // and its turn are still recorded, but there is no lead thread id any more.
        typeof(CodexSession).GetField("threadID", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, null);
        var before = script.MessageCount(0);

        Assert.False(Resolved(session.StopAgent("head-1")));
        Assert.Equal(before, script.MessageCount(0)); // guard let threadID fails (:338), so turn/interrupt is never sent
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
    [Trait("Req", "UI-PROVCLI-026")] // CodexSession.swift:414-436: limitName suffix only when >1 snapshot, id "<limitId>-<primary|secondary>"
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
    [Trait("Req", "UI-PROVCLI-026")] // CodexSession.swift:414-436: single snapshot, id falls back to "codex-primary"
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
    [Trait("Req", "UI-PROVCLI-026")] // CodexSession.swift:429-434: Codex is the only provider that surfaces a problem string in the usage panel
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
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:601-615 (B3)
    public void HandleNotification_tokenUsage_on_the_lead_emits_usage_and_records_the_delta_from_the_growing_total()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var script = Script("token-usage-lead");
        var session = StartedSession(runtime, script); // threadID "t1", activeModel "gpt-5.1-codex"
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
        Assert.Equal("gpt-5.1-codex", spend.Value.Model); // the thread/start model, on the fresh-thread path
        Assert.Null(spend.Value.AgentID);

        // Swift :673-674: recordCodexSpend writes the ledger under the (lowercased) thread id.
        runtime.OnMain(() => ledger.FlushLiveSpend());
        var livePath = Path.Combine(storage.Root, "token-activity-live-v2.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(livePath));
        Assert.Equal(100, document.RootElement.GetProperty("liveCodex").GetProperty("t1").GetInt64());

        // A second update with the total grown by 40 spends exactly the delta, not the whole total.
        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 40L))),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 540L))))))));
        Assert.Equal(40, events.OfType<ProviderEvent.TokenSpend>().Last().Value.TotalTokens);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:672-674 (B3)
    public void HandleNotification_tokenUsage_on_the_lead_with_no_thread_id_emits_the_event_but_writes_nothing_to_the_ledger()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var script = Script("token-usage-no-thread-id");
        var session = StartedSession(runtime, script);
        typeof(CodexSession).GetField("threadID", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, null);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var before = ledger.DailyTotals.Values.Sum();

        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 15L))),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 15L))))))));

        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal(15, spend.Value.TotalTokens);
        Assert.Null(spend.Value.AgentID);
        // Swift :673: `if let rolloutID, !rolloutID.isEmpty` is false with no thread id, so the ledger is never touched.
        Assert.Equal(before, ledger.DailyTotals.Values.Sum());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:613-614 (B3)
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
    [Trait("Req", "PR-PROVCLI-110")] // CodexSession.swift:147-150 (B3)
    public void Send_switching_the_turns_model_updates_activeModel_so_a_later_lead_spend_names_the_new_model()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("send-switch-model");
        var session = StartedSession(runtime, script); // activeModel becomes "gpt-5.1-codex" from thread/start's reply
        script.SetResponse("turn/start", "{}");

        // A turn that names a different model switches activeModel to that turn's model.
        var input = new TurnInput
        {
            Text = "x",
            Images = ValueList<Attachment>.Empty,
            Model = "gpt-6-codex",
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        Run(runtime, session.Send(input));
        Assert.Equal("gpt-6-codex", PrivateField<string?>(session, "activeModel"));

        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        runtime.OnMain(() => InvokePrivate(session, "HandleNotification", "thread/tokenUsage/updated", JSONValue.FromDictionaryLiteral(
            ("tokenUsage", JSONValue.FromDictionaryLiteral(
                ("last", JSONValue.FromDictionaryLiteral(("totalTokens", 10L))),
                ("total", JSONValue.FromDictionaryLiteral(("totalTokens", 10L))))))));

        // Lead spend and TokenLedger records name the model of the turn that just ran, not the thread's original model.
        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal("gpt-6-codex", spend.Value.Model);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-111")] // CodexSession.swift:616-628 (B3)
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

    // ============ part 3 (PR-PROVCLI-113 to 133, 440, 441, UI-PROVCLI-006) ============

    [Fact]
    [Trait("Req", "PR-PROVCLI-113")] // CodexSession.swift:910-918
    public void ErrorMessage_returns_unparseable_raw_text_unchanged_and_prefers_the_inner_error_message()
    {
        Assert.Equal("not JSON: \u2026", CodexSession.ErrorMessage(new JSONValue.Object(new ValueMap<string, JSONValue>(
            new Dictionary<string, JSONValue> { ["message"] = "not JSON: \u2026" }))));
        // "error.message" over a top-level "message" when the nested payload carries both.
        var nested = new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["message"] = "{\"error\":{\"message\":\"inner error\"},\"message\":\"outer\"}",
        }));
        Assert.Equal("inner error", CodexSession.ErrorMessage(nested));
    }

    // ---- PR-PROVCLI-114 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-114")]
    public void HandleNotification_turn_completed_maps_three_statuses_and_clears_the_lead_turn_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("turn-completed-lead");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "turn/started", JSONValue.FromDictionaryLiteral(("turn", JSONValue.FromDictionaryLiteral(("id", "turn-1")))));

        InvokePrivate(session, "HandleNotification", "turn/completed", JSONValue.FromDictionaryLiteral(("turn", JSONValue.FromDictionaryLiteral(("status", "interrupted")))));
        var completed = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "turn/completed", JSONValue.FromDictionaryLiteral(("turn", JSONValue.FromDictionaryLiteral(("status", "failed")))));
        Assert.Equal(TurnStatus.Failed, Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()).Status);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "turn/completed", JSONValue.FromDictionaryLiteral(("turn", JSONValue.FromDictionaryLiteral(("status", "somethingElse")))));
        Assert.Equal(TurnStatus.Completed, Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()).Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-114")]
    public void HandleNotification_turn_completed_emits_usageLimit_before_turnCompleted_on_a_matching_failure()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("turn-completed-usage-limit");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "turn/completed", JSONValue.FromDictionaryLiteral(
            ("turn", JSONValue.FromDictionaryLiteral(("status", "failed"), ("error", JSONValue.FromDictionaryLiteral(("message", "you hit your limit for today")))))));

        Assert.Equal(2, events.Count);
        Assert.IsType<ProviderEvent.UsageLimit>(events[0]);
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(events[1]);
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("you hit your limit for today", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-114")]
    public void HandleNotification_turn_completed_for_a_head_clears_its_turn_and_emits_agentFinished_with_the_error_as_summary()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("turn-completed-head");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        PrivateField<Dictionary<string, string>>(session, "headTurns")["head-1"] = "head-turn";
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "turn/completed", JSONValue.FromDictionaryLiteral(
            ("threadId", "head-1"), ("turn", JSONValue.FromDictionaryLiteral(("status", "failed"), ("error", JSONValue.FromDictionaryLiteral(("message", "boom")))))));

        var finished = Assert.Single(events.OfType<ProviderEvent.AgentFinished>());
        Assert.Equal("head-1", finished.AgentID);
        Assert.Equal(TurnStatus.Failed, finished.Status);
        Assert.Equal("boom", finished.Summary);
        Assert.False(PrivateField<Dictionary<string, string>>(session, "headTurns").ContainsKey("head-1"));
        Assert.Empty(events.OfType<ProviderEvent.TurnCompleted>());
    }

    // ---- PR-PROVCLI-115 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-115")]
    public void HandleNotification_serverRequest_resolved_removes_the_pending_entry_and_emits_requestResolved()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("server-request-resolved");
        var session = StartedSession(runtime, script);
        SeedQuestion(session, "7", new RPCID.Int(7));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "serverRequest/resolved", JSONValue.FromDictionaryLiteral(("requestId", 7L)));

        var resolved = Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
        Assert.Equal("7", resolved.Id);
        Assert.False(HasPending(session, "7"));

        events.Clear();
        InvokePrivate(session, "HandleNotification", "serverRequest/resolved", JSONValue.FromDictionaryLiteral(("requestId", "unknown")));
        Assert.Empty(events); // unknown id: nothing
    }

    // ---- PR-PROVCLI-116, PR-PROVCLI-440 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-116")] // CodexSession.swift:681-682,698-704 (B3)
    [Trait("Req", "PR-PROVCLI-440")]
    public void HandleNotification_thread_started_registers_a_head_whose_parent_is_the_lead_and_records_its_model()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("thread-started-parented");
        var session = StartedSession(runtime, script); // threadID = "t1"
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "thread/started", JSONValue.FromDictionaryLiteral(
            ("thread", JSONValue.FromDictionaryLiteral(("id", "head-1"), ("parentThreadId", "t1"), ("model", "gpt-head"), ("preview", "  do the thing  ")))));

        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("head-1", started.Value.Id);
        Assert.Equal("do the thing", started.Value.Description);
        Assert.Equal("do the thing", started.Value.Prompt);
        Assert.Equal("gpt-head", started.Value.Model);
        Assert.Contains("head-1", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.Equal("gpt-head", PrivateField<Dictionary<string, string>>(session, "headModels")["head-1"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-116")] // CodexSession.swift:681-682,684-695 (B3)
    [Trait("Req", "PR-PROVCLI-440")]
    public void HandleNotification_thread_started_adopts_an_unrelated_thread_by_nickname_or_role_and_logs_it()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("thread-started-adopted");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var logged = new List<string>();
        using (Logger.Sink.Override((message, _) => logged.Add(message)))
        {
            InvokePrivate(session, "HandleNotification", "thread/started", JSONValue.FromDictionaryLiteral(
                ("thread", JSONValue.FromDictionaryLiteral(("id", "head-2"), ("parentThreadId", "not-t1"), ("agentNickname", "scout"), ("preview", "")))));
        }

        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("head-2", started.Value.Id);
        Assert.Equal("scout", started.Value.Description); // nickname alone, no role
        Assert.Null(started.Value.Prompt); // empty preview
        Assert.Contains("head-2", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.Contains(logged, m => m.Contains("head-2", StringComparison.Ordinal) && m.Contains("not-t1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-116")] // CodexSession.swift:684-685 (B3)
    public void HandleNotification_thread_started_ignores_an_unrelated_thread_with_no_nickname_or_role()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("thread-started-dropped");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "thread/started", JSONValue.FromDictionaryLiteral(
            ("thread", JSONValue.FromDictionaryLiteral(("id", "head-3"), ("parentThreadId", "not-t1")))));

        Assert.Empty(events);
        Assert.DoesNotContain("head-3", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-116")] // CodexSession.swift:681-682 (B3)
    public void HandleNotification_thread_started_naming_the_leads_own_thread_id_is_ignored_even_with_a_nickname()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("thread-started-lead-id");
        var session = StartedSession(runtime, script); // threadID "t1"
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        // Carries an agentNickname, which would otherwise be enough to adopt it: exclusion runs first.
        InvokePrivate(session, "HandleNotification", "thread/started", JSONValue.FromDictionaryLiteral(
            ("thread", JSONValue.FromDictionaryLiteral(("id", "t1"), ("parentThreadId", "not-t1"), ("agentNickname", "scout")))));

        Assert.Empty(events);
        Assert.DoesNotContain("t1", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    // ---- PR-PROVCLI-117 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-117")]
    public void HandleNotification_item_started_maps_message_reasoning_plan_and_ignores_userMessage_for_the_lead()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("item-started-lead");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "m1"), ("type", "agentMessage"), ("text", "hi")))));
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "r1"), ("type", "reasoning")))));
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "p1"), ("type", "plan"), ("text", "step 1")))));
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "u1"), ("type", "userMessage"), ("content", new JSONValue.Array(ValueList<JSONValue>.Empty))))));
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "h1"), ("type", "hookPrompt")))));
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("type", "agentMessage"))))); // no id: dropped

        var msg = Assert.Single(events.OfType<ProviderEvent.MessageDelta>());
        Assert.Equal("m1", msg.Id);
        Assert.Equal("hi", msg.Text);
        var reasoning = Assert.Single(events.OfType<ProviderEvent.ReasoningDelta>());
        Assert.Equal("", reasoning.Text);
        var plan = Assert.Single(events.OfType<ProviderEvent.PlanDelta>());
        Assert.Equal("step 1", plan.Text);
        Assert.Empty(events.OfType<ProviderEvent.AgentStarted>()); // userMessage on the lead: nothing
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-117")]
    [Trait("Req", "PR-PROVCLI-133")]
    public void HandleNotification_item_started_userMessage_on_a_head_announces_its_brief()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("item-started-head-brief");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-1");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(
            ("threadId", "head-1"),
            ("item", JSONValue.FromDictionaryLiteral(("id", "u1"), ("type", "userMessage"),
                ("content", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("text", "  Build the feature  "))])))))));

        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("head-1", started.Value.Id);
        Assert.Equal("Build the feature", started.Value.Description);
        Assert.Equal("Build the feature", started.Value.Prompt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-133")]
    public void UserText_reads_the_type_value_shape_and_returns_nil_when_empty()
    {
        var withValueShape = JSONValue.FromDictionaryLiteral(("id", "u1"), ("type", "userMessage"),
            ("content", new JSONValue.Array(new ValueList<JSONValue>(
            [
                JSONValue.FromDictionaryLiteral(("type", "text"), ("value", "line one")),
                JSONValue.FromDictionaryLiteral(("type", "other"), ("value", "ignored")),
            ]))));
        Assert.Equal("line one", InvokeStatic("UserText", withValueShape));

        var empty = JSONValue.FromDictionaryLiteral(("content", new JSONValue.Array(ValueList<JSONValue>.Empty)));
        Assert.Null(InvokeStatic("UserText", empty));
    }

    // ---- PR-PROVCLI-118 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-118")]
    public void HandleNotification_item_completed_maps_message_reasoning_summary_and_plan()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("item-completed-lead");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "m1"), ("type", "agentMessage"), ("text", "done")))));
        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "p1"), ("type", "plan"), ("text", "done plan")))));
        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(("id", "u1"), ("type", "userMessage")))));

        var msg = Assert.Single(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.Equal("done", msg.Text);
        var plan = Assert.Single(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Equal("done plan", plan.Markdown);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-118")]
    public void HandleNotification_item_completed_reasoning_prefers_the_summary_over_the_content()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("item-completed-reasoning");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(
            ("item", JSONValue.FromDictionaryLiteral(("id", "r1"), ("type", "reasoning"),
                ("summary", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("a"), new JSONValue.String("b")]))),
                ("content", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("c")])))))));
        Assert.Equal("a\n\nb", Assert.Single(events.OfType<ProviderEvent.ReasoningCompleted>()).Text);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(
            ("item", JSONValue.FromDictionaryLiteral(("id", "r2"), ("type", "reasoning"),
                ("summary", new JSONValue.Array(ValueList<JSONValue>.Empty)),
                ("content", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("c"), new JSONValue.String("d")])))))));
        Assert.Equal("c\n\nd", Assert.Single(events.OfType<ProviderEvent.ReasoningCompleted>()).Text);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-118")]
    public void HandleNotification_item_completed_builds_a_tool_row_and_updates_it_for_an_unannounced_call()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("item-completed-tool");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(
            ("item", JSONValue.FromDictionaryLiteral(("id", "c1"), ("type", "webSearch"), ("query", "cats")))));

        var started = Assert.Single(events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal("c1", started.Id);
        Assert.Equal(ToolCall.Kind.Web, started.Call.ToolCallKind);
        var updated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal("c1", updated.Id);
        Assert.Equal(ToolCall.Status.Completed, updated.Update.Status);
    }

    // ---- PR-PROVCLI-119 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:760-761,776,778-779 (B3)
    public void HandleNotification_collabAgentToolCall_spawnAgent_registers_each_receiver_as_a_head()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("spawn-agent");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-1"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "  go build it  "),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-a"), new JSONValue.String("head-b")])))))));

        var starts = events.OfType<ProviderEvent.AgentStarted>().ToList();
        Assert.Equal(2, starts.Count);
        Assert.Equal("head-a", starts[0].Value.Id);
        Assert.Equal("spawn-1", starts[0].Value.ToolUseID);
        Assert.Equal("go build it", starts[0].Value.Description);
        Assert.Equal("go build it", starts[0].Value.Prompt);
        Assert.Null(starts[0].Value.Profile); // no Hydra configured
        Assert.Contains("head-a", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.Contains("head-b", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.False(PrivateField<Dictionary<string, string>>(session, "headModels").ContainsKey("head-a")); // PR-PROVCLI-440: no entry from this path
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:765 (B3)
    public void HandleNotification_collabAgentToolCall_ignores_a_call_that_is_not_spawnAgent()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("not-spawn-agent");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "wait-1"), ("type", "collabAgentToolCall"), ("tool", "wait"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-c")])))))));

        Assert.DoesNotContain(events.OfType<ProviderEvent.AgentStarted>(), e => e.Value.Id == "head-c");
        Assert.DoesNotContain("head-c", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:777 (B3)
    public void HandleNotification_collabAgentToolCall_spawnAgent_skips_a_receiver_equal_to_the_leads_own_thread_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("spawn-agent-lead-receiver");
        var session = StartedSession(runtime, script); // threadID "t1"
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-2"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "go build it"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("t1"), new JSONValue.String("head-d")])))))));

        var starts = events.OfType<ProviderEvent.AgentStarted>().ToList();
        var start = Assert.Single(starts); // only head-d, not the lead's own thread id
        Assert.Equal("head-d", start.Value.Id);
        Assert.DoesNotContain("t1", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.Contains("head-d", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:771-774 (B3)
    public void HandleNotification_collabAgentToolCall_spawnAgent_routes_the_head_to_the_one_profile_whose_model_and_effort_match()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var hydra = new HydraLaunch
        {
            HeadsProvider = ProviderKind.Codex,
            RunsNatively = true,
            HeadProfiles = new ValueList<HydraHeadProfile>([
                new HydraHeadProfile("reviewer") { Model = "gpt-5.1-codex", Effort = "high" },
                new HydraHeadProfile("scout") { Model = "gpt-5.1-codex", Effort = "low" },
            ]),
        };
        var script = Script("spawn-agent-routed-one-match");
        var session = StartedSession(runtime, script, hydra);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-r1"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "go review it"),
            ("model", "gpt-5.1-codex"), ("reasoningEffort", "high"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-r1")])))))));

        var start = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("reviewer", start.Value.Profile); // exactly one match: model+effort both match "reviewer" only
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:771-774 (B3)
    public void HandleNotification_collabAgentToolCall_spawnAgent_leaves_the_head_unrouted_when_no_profile_matches()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var hydra = new HydraLaunch
        {
            HeadsProvider = ProviderKind.Codex,
            RunsNatively = true,
            HeadProfiles = new ValueList<HydraHeadProfile>([
                new HydraHeadProfile("reviewer") { Model = "gpt-5.1-codex", Effort = "high" },
            ]),
        };
        var script = Script("spawn-agent-routed-no-match");
        var session = StartedSession(runtime, script, hydra);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        // The item names no model/effort at all, so it matches neither the profile's model nor its effort.
        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-r2"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "go build it"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-r2")])))))));

        var start = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Null(start.Value.Profile); // zero matches: unrouted
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:771-774 (B3)
    public void HandleNotification_collabAgentToolCall_spawnAgent_leaves_the_head_unrouted_when_several_profiles_match()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var hydra = new HydraLaunch
        {
            HeadsProvider = ProviderKind.Codex,
            RunsNatively = true,
            HeadProfiles = new ValueList<HydraHeadProfile>([
                new HydraHeadProfile("reviewer-a") { Model = "gpt-5.1-codex", Effort = "high" },
                new HydraHeadProfile("reviewer-b") { Model = "gpt-5.1-codex", Effort = "high" },
            ]),
        };
        var script = Script("spawn-agent-routed-several-matches");
        var session = StartedSession(runtime, script, hydra);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-r3"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "go review it"),
            ("model", "gpt-5.1-codex"), ("reasoningEffort", "high"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-r3")])))))));

        var start = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Null(start.Value.Profile); // two matches: unrouted, neither is authoritative
    }

    // ---- PR-PROVCLI-120 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-120")] // CodexSession.swift:781-784 (B3)
    public void HandleNotification_subAgentActivity_adopts_an_unknown_head_and_logs_it_then_ends_it_on_completed()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("sub-agent-activity-completed");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var logged = new List<string>();
        using (Logger.Sink.Override((message, _) => logged.Add(message)))
        {
            InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
                ("id", "note-1"), ("type", "subAgentActivity"), ("agentThreadId", "head-x"), ("agentPath", "root/worker_one"), ("kind", "completed")))));
        }

        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("head-x", started.Value.Id);
        Assert.Equal("worker one", started.Value.Description);
        Assert.Null(started.Value.Prompt);
        Assert.Contains("head head-x registered from a subAgentActivity note (worker_one); no thread/started or spawn receiver id preceded it", logged);
        var finished = Assert.Single(events.OfType<ProviderEvent.AgentFinished>());
        Assert.Equal("head-x", finished.AgentID);
        Assert.Equal(TurnStatus.Completed, finished.Status);
        Assert.Null(finished.Summary);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-120")] // CodexSession.swift:784,799-801 (B3)
    public void HandleNotification_subAgentActivity_does_not_readopt_an_already_known_head_and_ends_it_on_interrupted()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("sub-agent-activity-known");
        var session = StartedSession(runtime, script);
        PrivateField<HashSet<string>>(session, "headThreads").Add("head-y");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "note-2"), ("type", "subAgentActivity"), ("agentThreadId", "head-y"), ("kind", "interrupted")))));

        Assert.Empty(events.OfType<ProviderEvent.AgentStarted>());
        var finished = Assert.Single(events.OfType<ProviderEvent.AgentFinished>());
        Assert.Equal(TurnStatus.Interrupted, finished.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-120")] // CodexSession.swift:782-783 (B3)
    public void HandleNotification_subAgentActivity_naming_the_leads_own_thread_does_nothing()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("sub-agent-activity-lead-id");
        var session = StartedSession(runtime, script); // threadID "t1"
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var logged = new List<string>();

        using (Logger.Sink.Override((message, _) => logged.Add(message)))
        {
            InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
                ("id", "note-3"), ("type", "subAgentActivity"), ("agentThreadId", "t1"), ("agentPath", "root/worker_two"), ("kind", "completed")))));
        }

        Assert.Empty(events);
        Assert.Empty(logged);
        Assert.DoesNotContain("t1", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-119")] // CodexSession.swift:760 (B3)
    public void HandleNotification_collabAgentToolCall_and_subAgentActivity_register_no_head_once_the_lead_thread_id_is_gone()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("describe-heads-no-thread-id");
        var session = StartedSession(runtime, script); // threadID "t1"
        // The connection stays open (as after ForkHistory's no-previousID archive path, :269-273) but the lead
        // thread id is gone: describeHeads's guard (:760) must stop both branches cold.
        typeof(CodexSession).GetField("threadID", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, null);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleNotification", "item/started", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "spawn-null"), ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "go build it"),
            ("receiverThreadIds", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("head-z")])))))));
        InvokePrivate(session, "HandleNotification", "item/completed", JSONValue.FromDictionaryLiteral(("item", JSONValue.FromDictionaryLiteral(
            ("id", "note-null"), ("type", "subAgentActivity"), ("agentThreadId", "head-w"), ("agentPath", "root/worker_two"), ("kind", "completed")))));

        Assert.Empty(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Empty(events.OfType<ProviderEvent.AgentFinished>());
        Assert.DoesNotContain("head-z", PrivateField<HashSet<string>>(session, "headThreads"));
        Assert.DoesNotContain("head-w", PrivateField<HashSet<string>>(session, "headThreads"));
    }

    // ---- PR-PROVCLI-121 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-121")]
    public void ToolCallFrom_commandExecution_picks_read_search_or_command_by_its_action_kinds()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-call-command-kinds");
        var session = StartedSession(runtime, script);

        var read = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("command", "/bin/zsh -lc 'cat file.txt'"),
            ("commandActions", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("type", "read"), ("path", "file.txt"))])))));
        var readCall = Assert.IsType<ToolCall>(read);
        Assert.Equal(ToolCall.Kind.Read, readCall.ToolCallKind);
        Assert.Equal("file.txt", readCall.Title);
        Assert.Equal("cat file.txt", readCall.Detail);

        var search = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("command", "grep -r foo"),
            ("commandActions", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("type", "search"))])))));
        Assert.Equal(ToolCall.Kind.Search, Assert.IsType<ToolCall>(search).ToolCallKind);

        var command = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("command", "npm test"),
            ("commandActions", new JSONValue.Array(ValueList<JSONValue>.Empty))));
        Assert.Equal(ToolCall.Kind.Command, Assert.IsType<ToolCall>(command).ToolCallKind);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-121")]
    public void ToolCallFrom_fileChange_titles_a_single_path_or_a_count()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-call-file-change");
        var session = StartedSession(runtime, script);

        var one = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "fileChange"), ("changes", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("path", "a.txt"))])))));
        var oneCall = Assert.IsType<ToolCall>(one);
        Assert.Equal(ToolCall.Kind.Edit, oneCall.ToolCallKind);
        Assert.Equal("a.txt", oneCall.Title);

        var two = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "fileChange"), ("changes", new JSONValue.Array(new ValueList<JSONValue>(
            [JSONValue.FromDictionaryLiteral(("path", "a.txt")), JSONValue.FromDictionaryLiteral(("path", "b.txt"))])))));
        Assert.Equal("Edit 2 files", Assert.IsType<ToolCall>(two).Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-121")]
    public void ToolCallFrom_mcpToolCall_titles_server_dot_tool_and_omits_null_arguments()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-call-mcp");
        var session = StartedSession(runtime, script);

        var withArgs = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "mcpToolCall"), ("server", "fs"), ("tool", "read"), ("arguments", JSONValue.FromDictionaryLiteral(("path", "x")))));
        var withArgsCall = Assert.IsType<ToolCall>(withArgs);
        Assert.Equal("fs \u00b7 read", withArgsCall.Title);
        Assert.NotNull(withArgsCall.Detail);

        var noArgs = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "mcpToolCall")));
        var noArgsCall = Assert.IsType<ToolCall>(noArgs);
        Assert.Equal("MCP \u00b7 tool", noArgsCall.Title);
        Assert.Null(noArgsCall.Detail); // arguments absent -> compact "null" -> omitted
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-121")]
    public void ToolCallFrom_collabAgentToolCall_picks_a_verb_from_the_table_and_prefers_the_prompt_as_title()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-call-collab");
        var session = StartedSession(runtime, script);

        var withPrompt = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(
            ("type", "collabAgentToolCall"), ("tool", "spawnAgent"), ("prompt", "build it")));
        var withPromptCall = Assert.IsType<ToolCall>(withPrompt);
        Assert.Equal(ToolCall.Kind.Agent, withPromptCall.ToolCallKind);
        Assert.Equal("build it", withPromptCall.Title);
        Assert.Equal("Send out a head", withPromptCall.Detail);

        var noPrompt = InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "collabAgentToolCall"), ("tool", "wait")));
        var noPromptCall = Assert.IsType<ToolCall>(noPrompt);
        Assert.Equal("Wait for heads", noPromptCall.Title);
        Assert.Null(noPromptCall.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-121")]
    public void ToolCallFrom_maps_the_remaining_item_types_and_drops_subAgentActivity_and_unknowns()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-call-remaining");
        var session = StartedSession(runtime, script);

        Assert.Equal(ToolCall.Kind.Other, Assert.IsType<ToolCall>(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "dynamicToolCall"), ("tool", "custom")))).ToolCallKind);
        Assert.Equal(ToolCall.Kind.Web, Assert.IsType<ToolCall>(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "webSearch"), ("query", "cats")))).ToolCallKind);
        Assert.Equal("Image", Assert.IsType<ToolCall>(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "imageView")))).Title);
        Assert.Equal("Generate image", Assert.IsType<ToolCall>(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "imageGeneration")))).Title);
        var review = Assert.IsType<ToolCall>(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "enteredReviewMode"), ("review", "r"))));
        Assert.Equal("Review", review.Title);
        Assert.Equal("r", review.Detail);
        Assert.Null(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "subAgentActivity"))));
        Assert.Null(InvokePrivate(session, "ToolCallFrom", JSONValue.FromDictionaryLiteral(("type", "somethingUnmapped"))));
    }

    // ---- PR-PROVCLI-122, PR-PROVCLI-123 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-122")]
    [Trait("Req", "PR-PROVCLI-123")]
    public void ToolUpdateFrom_commandExecution_carries_output_exitCode_and_status_from_the_exit_code()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-update-command");
        var session = StartedSession(runtime, script);

        var ok = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("aggregatedOutput", "out"), ("exitCode", 0L))));
        Assert.Equal("out", ok.Output);
        Assert.Equal(0, ok.ExitCode);
        Assert.Equal(ToolCall.Status.Completed, ok.Status);

        var failed = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("exitCode", 2L))));
        Assert.Equal(ToolCall.Status.Failed, failed.Status);

        var interrupted = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "commandExecution"), ("status", "interrupted"))));
        Assert.Equal(ToolCall.Status.Failed, interrupted.Status); // PR-PROVCLI-123: interrupted reads as failed

        var declined = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(("type", "commandExecution"), ("status", "declined"))));
        Assert.Equal(ToolCall.Status.Declined, declined.Status);

        var running = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(("type", "commandExecution"), ("status", "inProgress"))));
        Assert.Equal(ToolCall.Status.Running, running.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-122")]
    public void ToolUpdateFrom_fileChange_builds_one_edit_per_change_with_diff_stats()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-update-file-change");
        var session = StartedSession(runtime, script);

        var diff = "@@ -1,2 +1,2 @@\n-old\n+new\n+added\n";
        var update = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "fileChange"),
            ("changes", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("path", "a.txt"), ("diff", diff))]))))));
        var edit = Assert.Single(update.Edits!);
        Assert.Equal("a.txt", edit.Path);
        Assert.Equal(2, edit.Additions);
        Assert.Equal(1, edit.Deletions);
        Assert.Equal(ToolCall.Status.Completed, update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-122")]
    public void ToolUpdateFrom_mcpToolCall_prefers_the_error_message_over_the_result_content()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-update-mcp");
        var session = StartedSession(runtime, script);

        var withError = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "mcpToolCall"), ("error", JSONValue.FromDictionaryLiteral(("message", "bad"))),
            ("result", JSONValue.FromDictionaryLiteral(("content", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("text", "ignored"))]))))))));
        Assert.Equal("bad", withError.Output);

        var withResult = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(
            ("type", "mcpToolCall"),
            ("result", JSONValue.FromDictionaryLiteral(("content", new JSONValue.Array(new ValueList<JSONValue>(
            [JSONValue.FromDictionaryLiteral(("text", "line1")), JSONValue.FromDictionaryLiteral(("text", "line2"))]))))))));
        Assert.Equal("line1\nline2", withResult.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-122")]
    public void ToolUpdateFrom_webSearch_and_similar_types_are_always_completed_with_no_output()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("tool-update-always-completed");
        var session = StartedSession(runtime, script);

        foreach (var type in new[] { "webSearch", "imageView", "enteredReviewMode", "exitedReviewMode" })
        {
            var update = Assert.IsType<ToolUpdate>(InvokePrivate(session, "ToolUpdateFrom", JSONValue.FromDictionaryLiteral(("type", type), ("status", "failed"))));
            Assert.Equal(ToolCall.Status.Completed, update.Status); // status ignored for these types
            Assert.Null(update.Output);
        }
    }

    // ---- PR-PROVCLI-124 to 129 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-124")]
    [Trait("Req", "PR-PROVCLI-130")]
    [Trait("Req", "UI-PROVCLI-006")]
    public void HandleRequest_commandExecution_requestApproval_builds_a_command_approval_with_the_default_four_options_sorted()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-command");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(1), "item/commandExecution/requestApproval", JSONValue.FromDictionaryLiteral(
            ("command", "/bin/zsh -lc 'rm -rf x'"), ("cwd", WorkingDir(session)), ("reason", "cleanup"), ("itemId", "c1")));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("1", approval.Id);
        Assert.Equal(ApprovalRequest.Kind.Command, approval.RequestKind);
        Assert.Equal("rm -rf x", approval.Title);
        Assert.Equal("cleanup", approval.Reason);
        Assert.Equal("c1", approval.ToolItemID);
        Assert.Equal(4, approval.Options.Count);
        Assert.Equal(new[] { "accept", "acceptForSession", "decline", "cancel" }, approval.Options.Select(o => o.Id));
        var stopTurn = approval.Options.Last();
        Assert.Equal("Stop turn", stopTurn.Title); // UI-PROVCLI-006
        Assert.Equal(ApprovalRequest.Option.Role.Cancel, stopTurn.OptionRole);
        Assert.True(HasPending(session, "1"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-124")]
    public void HandleRequest_commandExecution_requestApproval_falls_back_to_the_literal_Command_title()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-command-fallback");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(2), "item/commandExecution/requestApproval", JSONValue.FromDictionaryLiteral());

        Assert.Equal("Command", Assert.Single(events.OfType<ProviderEvent.Approval>()).Value.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-130")]
    [Trait("Req", "UI-PROVCLI-007")] // CodexSession.swift:1008-1010: "Always allow" only for acceptWithExecpolicyAmendment on a command approval
    public void HandleRequest_commandExecution_requestApproval_adds_the_amend_option_and_drops_unknown_decisions()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-amend");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(3), "item/commandExecution/requestApproval", JSONValue.FromDictionaryLiteral(
            ("command", "ls"),
            ("availableDecisions", new JSONValue.Array(new ValueList<JSONValue>(
            [
                new JSONValue.String("accept"),
                new JSONValue.String("somethingUnrecognized"),
                JSONValue.FromDictionaryLiteral(("acceptWithExecpolicyAmendment", JSONValue.FromDictionaryLiteral(("x", true)))),
            ])))));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(new[] { "accept", "amend" }, approval.Options.Select(o => o.Id));
        Assert.Equal("Always allow", approval.Options[1].Title);
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-007")] // CodexSession.swift:1013-1015: two .approveAlways options (acceptForSession, amend) sort stably, server order preserved
    public void HandleRequest_commandExecution_requestApproval_keeps_the_server_order_of_two_approveAlways_options()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-amend-and-session");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(7), "item/commandExecution/requestApproval", JSONValue.FromDictionaryLiteral(
            ("command", "ls"),
            ("availableDecisions", new JSONValue.Array(new ValueList<JSONValue>(
            [
                JSONValue.FromDictionaryLiteral(("acceptWithExecpolicyAmendment", JSONValue.FromDictionaryLiteral(("x", true)))),
                new JSONValue.String("acceptForSession"),
            ])))));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        // Both are .approveAlways; a stable sort keeps them in the server's own order: amend first, then acceptForSession.
        Assert.Equal(new[] { "amend", "acceptForSession" }, approval.Options.Select(o => o.Id));
        Assert.Equal("Always allow", approval.Options[0].Title);
        Assert.Equal("Approve for session", approval.Options[1].Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-125")]
    [Trait("Req", "UI-PROVCLI-013")] // CodexSession.swift:942: fixed title "Apply file changes", never carries the file name
    public void HandleRequest_fileChange_requestApproval_uses_the_default_four_options_with_no_amend_and_a_verbatim_detail()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-file-change");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(4), "item/fileChange/requestApproval", JSONValue.FromDictionaryLiteral(
            ("grantRoot", @"C:\Users\me\project"), ("reason", "writes"), ("itemId", "f1")));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.FileChange, approval.RequestKind);
        Assert.Equal("Apply file changes", approval.Title);
        Assert.Equal(@"C:\Users\me\project", approval.Detail); // not made relative
        Assert.Equal(4, approval.Options.Count);
        Assert.DoesNotContain(approval.Options, o => o.Id == "amend"); // command: false
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-126")]
    [Trait("Req", "UI-PROVCLI-008")] // CodexSession.swift:959: "Allow once", option id "turn", role .approve
    [Trait("Req", "UI-PROVCLI-009")] // CodexSession.swift:960: "Allow for session", option id "session", role .approveAlways, always present
    [Trait("Req", "UI-PROVCLI-015")] // CodexSession.swift:1018-1030: "Write to <paths>" + " \u00b7 " + "Network access"
    public void HandleRequest_permissions_requestApproval_builds_the_three_fixed_options_and_a_described_title()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-permissions");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(5), "item/permissions/requestApproval", JSONValue.FromDictionaryLiteral(
            ("permissions", JSONValue.FromDictionaryLiteral(
                ("fileSystem", JSONValue.FromDictionaryLiteral(("write", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("/a")]))))),
                ("network", JSONValue.FromDictionaryLiteral(("enabled", true))))),
            ("itemId", "p1")));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.Permissions, approval.RequestKind);
        Assert.Equal("Write to /a \u00b7 Network access", approval.Title);
        Assert.Null(approval.Detail);
        Assert.Equal(new[] { "turn", "session", "decline" }, approval.Options.Select(o => o.Id));
        Assert.Equal(new[] { ApprovalRequest.Option.Role.Approve, ApprovalRequest.Option.Role.ApproveAlways, ApprovalRequest.Option.Role.Decline }, approval.Options.Select(o => o.OptionRole));
        Assert.Equal("Allow once", approval.Options[0].Title);
        Assert.Equal("Allow for session", approval.Options[1].Title);
        Assert.Equal("Decline", approval.Options[2].Title);
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-015")] // CodexSession.swift:1023-1025: "Read <paths>" part, and all three parts joined by " · "
    public void HandleRequest_permissions_requestApproval_title_includes_the_read_part_joined_with_the_others()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-permissions-read");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(12), "item/permissions/requestApproval", JSONValue.FromDictionaryLiteral(
            ("permissions", JSONValue.FromDictionaryLiteral(
                ("fileSystem", JSONValue.FromDictionaryLiteral(
                    ("write", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("/a"), new JSONValue.String("/b")]))),
                    ("read", new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String("/c")]))))),
                ("network", JSONValue.FromDictionaryLiteral(("enabled", true))))),
            ("itemId", "p2")));

        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("Write to /a, /b · Read /c · Network access", approval.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-126")]
    [Trait("Req", "UI-PROVCLI-015")] // CodexSession.swift:1018-1030: literal "Additional permissions" appears only when no part applied
    public void HandleRequest_permissions_requestApproval_with_no_parts_shows_additional_permissions()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-approval-permissions-empty");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(6), "item/permissions/requestApproval", JSONValue.FromDictionaryLiteral());

        Assert.Equal("Additional permissions", Assert.Single(events.OfType<ProviderEvent.Approval>()).Value.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-127")]
    public void HandleRequest_tool_requestUserInput_builds_questions_and_skips_entries_with_no_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("request-user-input");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(8), "item/tool/requestUserInput", JSONValue.FromDictionaryLiteral(
            ("questions", new JSONValue.Array(new ValueList<JSONValue>(
            [
                JSONValue.FromDictionaryLiteral(("id", "q1"), ("header", "Pick"), ("question", "Which?"), ("isSecret", true),
                    ("options", new JSONValue.Array(new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("label", "A"), ("description", "first"))])))),
                JSONValue.FromDictionaryLiteral(("header", "no id")),
            ])))));

        var question = Assert.Single(events.OfType<ProviderEvent.Question>()).Value;
        Assert.Equal("8", question.Id);
        var q = Assert.Single(question.Questions);
        Assert.Equal("q1", q.Id);
        Assert.Equal("Pick", q.Header);
        Assert.Equal("Which?", q.Prompt);
        Assert.False(q.AllowsMultiple);
        Assert.False(q.AllowsOther);
        Assert.True(q.IsSecret);
        var choice = Assert.Single(q.Choices);
        Assert.Equal("A", choice.Label);
        Assert.Equal("first", choice.Detail);
        Assert.True(HasPending(session, "8"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-128")]
    public void HandleRequest_mcpServer_elicitation_request_always_declines_and_warns()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("mcp-elicitation");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        var before = script.MessageCount(0);
        InvokePrivate(session, "HandleRequest", new RPCID.Int(9), "mcpServer/elicitation/request", JSONValue.FromDictionaryLiteral(("serverName", "files-mcp")));

        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>()).Value;
        Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
        Assert.Equal("files-mcp asked for input, which nth cannot show yet.", notice.Message);
        Assert.False(HasPending(session, "9"));
        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, before)))!;
        Assert.Equal("decline", response["result"]!["action"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-128")]
    public void HandleRequest_mcpServer_elicitation_request_falls_back_to_An_MCP_server()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("mcp-elicitation-fallback");
        var session = StartedSession(runtime, script);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleRequest", new RPCID.Int(10), "mcpServer/elicitation/request", JSONValue.FromDictionaryLiteral());

        Assert.Equal("An MCP server asked for input, which nth cannot show yet.", Assert.Single(events.OfType<ProviderEvent.Notice>()).Value.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-129")]
    public void HandleRequest_refuses_an_unknown_method_with_JSONRPC_error_32601()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("unknown-method");
        var session = StartedSession(runtime, script);

        var before = script.MessageCount(0);
        InvokePrivate(session, "HandleRequest", new RPCID.Int(11), "some/unknownMethod", JSONValue.FromDictionaryLiteral());

        var response = JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, before)))!;
        Assert.Equal(-32601, response["error"]!["code"]!.JSONValueInt);
        Assert.Equal("nth does not support some/unknownMethod.", response["error"]!["message"]!.JSONValueString);
    }

    // ---- PR-PROVCLI-131 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-131")]
    public void HandleClose_resolves_every_pending_request_and_reports_exited_only_when_not_stopping()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = Script("handle-close");
        var session = StartedSession(runtime, script);
        SeedQuestion(session, "q1", new RPCID.String("q1"));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "HandleClose", "some\ntail");

        var resolved = Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
        Assert.Equal("q1", resolved.Id);
        Assert.False(HasPending(session, "q1"));
        Assert.Single(events.OfType<ProviderEvent.Exited>());

        events.Clear();
        typeof(CodexSession).GetField("isStopping", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, true);
        InvokePrivate(session, "HandleClose", "tail");
        Assert.Empty(events.OfType<ProviderEvent.Exited>()); // stopping: no exit notice
    }

    // ---- PR-PROVCLI-441 ----

    private static JsonElement LiveState(StorageScratchFolder storage, TokenLedger ledger, TestRuntime runtime)
    {
        runtime.OnMain(() => ledger.FlushLiveSpend());
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(storage.Root, "token-activity-live-v2.json")));
        return document.RootElement.Clone();
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-441")] // CodexSession.swift:662-674 (guard spend > 0: zero or negative does nothing at all)
    public void RecordCodexSpend_of_zero_or_less_writes_no_ledger_row_and_emits_no_event()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var session = new CodexSession(Config(Script("spend-nothing")));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 0L, "ABC-123", "gpt-5", null));
        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", -7L, "ABC-123", "gpt-5", "head-1"));

        Assert.Empty(events);
        Assert.Empty(ledger.DailyTotals);
        Assert.False(File.Exists(Path.Combine(storage.Root, "token-activity-live-v2.json"))); // no save was ever scheduled
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-441")] // CodexSession.swift:673-676; TokenLedger.swift:122-126,128-145 (rollout id lowercased; daily total and liveCodex both grow; one event)
    public void RecordCodexSpend_with_a_rollout_id_adds_to_daily_and_liveCodex_under_the_lowercased_id_and_emits_one_event()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var session = new CodexSession(Config(Script("spend-ledger")));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var before = ledger.DailyTotals.Values.Sum();

        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 120L, "ABC-123", "gpt-5", null));

        Assert.Equal(before + 120, ledger.DailyTotals.Values.Sum());
        var live = LiveState(storage, ledger, runtime);
        Assert.Equal(120, live.GetProperty("liveCodex").GetProperty("abc-123").GetInt64());
        Assert.False(live.GetProperty("liveCodex").TryGetProperty("ABC-123", out _));
        Assert.Equal(120, live.GetProperty("daily").EnumerateObject().Sum(day => day.Value.GetInt64()));
        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>()).Value;
        Assert.Equal(ProviderKind.Codex, spend.Provider);
        Assert.Equal(120, spend.TotalTokens);
        Assert.Equal("gpt-5", spend.Model);
        Assert.Null(spend.AgentID);
        Assert.Null(spend.InputTokens);
        Assert.Null(spend.OutputTokens);
        Assert.Null(spend.CachedInputTokens);
        Assert.Single(events);

        // A second spend under the same id in other casing accumulates on the same lowercased key.
        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 30L, "abc-123", "gpt-5", null));
        Assert.Equal(150, LiveState(storage, ledger, runtime).GetProperty("liveCodex").GetProperty("abc-123").GetInt64());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-441")] // CodexSession.swift:673-676 (the head's agent id and model ride on the event; nil or empty rollout id writes no ledger row)
    public void RecordCodexSpend_without_a_rollout_id_emits_the_event_only_and_carries_the_agent_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var session = new CodexSession(Config(Script("spend-no-rollout")));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 50L, "", "gpt-5", "head-9"));
        runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 5L, null, null, null));

        Assert.Empty(ledger.DailyTotals);
        Assert.False(File.Exists(Path.Combine(storage.Root, "token-activity-live-v2.json")));
        var spends = events.OfType<ProviderEvent.TokenSpend>().Select(e => e.Value).ToList();
        Assert.Equal(2, spends.Count);
        Assert.Equal(2, events.Count);
        Assert.Equal(50, spends[0].TotalTokens);
        Assert.Equal("gpt-5", spends[0].Model);
        Assert.Equal("head-9", spends[0].AgentID);
        Assert.Equal(ProviderKind.Codex, spends[0].Provider);
        Assert.Equal(5, spends[1].TotalTokens);
        Assert.Null(spends[1].Model);
        Assert.Null(spends[1].AgentID);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-441")] // windows_path of PR-PROVCLI-441: the rollout id is lowercased culture-invariantly (TokenLedger.swift:125)
    public void RecordCodexSpend_lowercases_the_rollout_id_culture_invariantly()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var session = new CodexSession(Config(Script("spend-turkish")));
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            runtime.OnMain(() => InvokePrivate(session, "RecordCodexSpend", 10L, "ID-I1", "gpt-5", null));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(10, LiveState(storage, ledger, runtime).GetProperty("liveCodex").GetProperty("id-i1").GetInt64());
    }

    // ---- PR-PROVCLI-132 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-132")]
    public void IsRunning_is_false_with_no_connection_and_true_only_while_the_connection_is_open()
    {
        var session = new CodexSession(Config(Script("is-running")));
        Assert.False(session.IsRunning); // no connection yet

        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var started = StartedSession(runtime, Script("is-running-2"));
        Assert.True(started.IsRunning);
        started.Stop();
        runtime.Loop.RunUntil(() => !started.IsRunning, TimeSpan.FromSeconds(5));
        Assert.False(started.IsRunning);
    }

    // ---- helpers ----

    private static string WorkingDir(CodexSession session) => typeof(CodexSession)
        .GetProperty("WorkingDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session) as string ?? "";

    private static object? InvokeStatic(string method, params object?[] args) =>
        typeof(CodexSession).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
}
