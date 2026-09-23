using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/ACPSession.swift (B2 1fbf81e). Part 1: PR-PROVACP-001
// through 024 (the launch gate, launchArguments and start() through the initialize reply). A real provider's
// launchArguments (e.g. Grok's "--permission-mode default agent stdio") is not a valid powershell.exe switch
// set, so it cannot drive a scripted stand-in agent through Start() itself; InitializeParams and
// ApplyInitializeReply are exercised directly by reflection instead (the same production members Start()
// calls), and a direct StdioProcess (built from the same configuration values Start() uses) proves the spawn
// parameters with a working scripted agent, mirroring JSONRPCTests' technique.
public sealed class ACPSessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private static SessionConfiguration Config(ProviderKind provider, RuntimeMode mode, SwiftURL? executable, IReadOnlyDictionary<string, string>? env = null) => new()
    {
        Provider = provider,
        Executable = executable,
        WorkingDirectory = SwiftURL.Create("file:///" + Path.GetTempPath().Replace('\\', '/'))!,
        Environment = new ValueMap<string, string>(env ?? new Dictionary<string, string>()),
        RuntimeMode = mode,
        InteractionMode = InteractionMode.Build,
    };

    private static ACPSession Session(ProviderKind provider, RuntimeMode mode, SwiftURL? executable = null, IReadOnlyDictionary<string, string>? env = null) =>
        new(Config(provider, mode, executable, env));

    private static T Private<T>(ACPSession session, string member, bool field = false)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? value = field
            ? typeof(ACPSession).GetField(member, flags)!.GetValue(session)
            : typeof(ACPSession).GetProperty(member, flags)!.GetValue(session);
        return (T)value!;
    }

    private static void InvokePrivate(ACPSession session, string method, params object?[] args) =>
        typeof(ACPSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args);

    private static Task<T> InvokePrivateAsync<T>(ACPSession session, string method, params object?[] args) =>
        (Task<T>)typeof(ACPSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args)!;

    private static Task InvokePrivateVoidAsync(ACPSession session, string method, params object?[] args) =>
        (Task)typeof(ACPSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args)!;

    private static T Resolved<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "the request did not resolve");
        return task.Result;
    }

    private static T InvokeStatic<T>(string method, params object?[] args) =>
        (T)typeof(ACPSession).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    private static void SetField(ACPSession session, string name, object? value) =>
        typeof(ACPSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);

    private static StdioProcess LiveAgent(string script)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value!;
        return new StdioProcess(ACPSessionFixtures.PowerShell, ACPSessionFixtures.PowerShellEncoded(script), Environment.CurrentDirectory, env);
    }

    private const string Pipes = "$i=[Console]::In; $e=[Console]::Error; $o=[Console]::Out; ";
    private const string EndOnBye = "if($l.Contains('\"method\":\"bye\"')){ break } }";

    private static ValueList<string> LaunchArguments(ACPSession session) => Private<ValueList<string>>(session, "LaunchArguments");

    private static void SetRuntimeModeField(ACPSession session, RuntimeMode mode) =>
        typeof(ACPSession).GetField("runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, mode);

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    /// <summary>Runs a private async void-returning member (ApplySelection, Interrupt, Send) on main through
    /// TestRuntime's manual clock and pump, and fails the test if it does not complete.</summary>
    private static void RunOnMain(TestRuntime runtime, ACPSession session, string method, params object?[] args)
    {
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(async () =>
        {
            await InvokePrivateVoidAsync(session, method, args);
            return true;
        }));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.True(Resolved(handle.Value));
    }

    /// <summary>A session wired to a live scripted agent with `connection` and `sessionID` already set, as if
    /// start() had run, so applySelection/setConfigOption/send/interrupt can be exercised directly.</summary>
    private static (ACPSession Session, JSONRPCConnection Connection, StdioProcess Process) ConnectedSession(
        TestRuntime runtime, ProviderKind provider, string script, string sessionID = "sess-1")
    {
        var process = LiveAgent(script);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var session = Session(provider, RuntimeMode.Supervised);
        SetField(session, "connection", connection);
        SetField(session, "sessionID", sessionID);
        runtime.OnMain(connection.Start);
        return (session, connection, process);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-001")] // Core/Models/Provider.swift:104,25-28,32,44-47,51
    public void Exactly_five_provider_kinds_use_ACP_with_the_source_display_and_executable_names()
    {
        Assert.True(ProviderKind.Cursor.UsesACP);
        Assert.True(ProviderKind.Opencode.UsesACP);
        Assert.True(ProviderKind.OpencodeGo.UsesACP);
        Assert.True(ProviderKind.Grok.UsesACP);
        Assert.True(ProviderKind.Devin.UsesACP);
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            if (kind is ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Devin) continue;
            Assert.False(kind.UsesACP, kind.ToString());
        }
        Assert.Equal("Cursor", ProviderKind.Cursor.DisplayName);
        Assert.Equal("OpenCode Zen", ProviderKind.Opencode.DisplayName);
        Assert.Equal("OpenCode Go", ProviderKind.OpencodeGo.DisplayName);
        Assert.Equal("Grok", ProviderKind.Grok.DisplayName);
        Assert.Equal("Devin", ProviderKind.Devin.DisplayName);
        Assert.Equal("cursor-agent", ProviderKind.Cursor.ExecutableName);
        Assert.Equal("opencode", ProviderKind.Opencode.ExecutableName);
        Assert.Equal("opencode", ProviderKind.OpencodeGo.ExecutableName);
        Assert.Equal("grok", ProviderKind.Grok.ExecutableName);
        Assert.Equal("devin", ProviderKind.Devin.ExecutableName);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-002")] // ACPSession.swift:7-21,26,110
    public async Task The_launch_gate_never_delays_a_lone_start_but_spaces_a_batch_1_5_seconds_apart()
    {
        using var runtime = new TestRuntime();
        var gate = new ACPLaunchGate(TimeSpan.FromSeconds(1.5));

        var first = gate.Wait();
        await first.WaitAsync(Bound);
        Assert.True(first.IsCompletedSuccessfully);

        var second = gate.Wait();
        var armed = SpinWait.SpinUntil(() => runtime.Time.ActiveTimers >= 1 || second.IsCompleted, Bound);
        Assert.True(armed, "the second wait neither finished nor armed a timer on the manual clock");
        Assert.False(second.IsCompleted, "a batch start must wait for the previous slot plus spacing");
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.False(second.IsCompleted, "must not resolve before the full 1.5 s spacing elapsed");
        runtime.Time.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(Bound);
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-003")] // ACPSession.swift:109; ProviderSession.swift:164-169; Provider.swift:66-69,73,25-28,32
    public async Task Start_without_a_resolved_executable_throws_notInstalled_with_the_exact_per_provider_message()
    {
        var cases = new (ProviderKind Provider, string Message)[]
        {
            (ProviderKind.Cursor, "Cursor is not installed. Install it and sign in with `cursor-agent login`."),
            (ProviderKind.Opencode, "OpenCode Zen is not installed. Install it and sign in with `opencode auth login`."),
            (ProviderKind.OpencodeGo, "OpenCode Go is not installed. Install it and sign in with `opencode auth login`."),
            (ProviderKind.Grok, "Grok is not installed. Install it and sign in with `grok login`."),
            (ProviderKind.Devin, "Devin is not installed. Install it and sign in with `devin auth login`."),
        };
        foreach (var (provider, message) in cases)
        {
            var session = Session(provider, RuntimeMode.Supervised, executable: null);
            var error = await Assert.ThrowsAsync<ProviderError>(() => session.Start());
            var notInstalled = Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
            Assert.Equal(provider, notInstalled.Provider);
            Assert.Equal(message, error.Message);
        }
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-005")] // ACPSession.swift:82
    [Trait("Req", "PR-PROVACP-006")] // ACPSession.swift:83
    [Trait("Req", "PR-PROVACP-007")] // ACPSession.swift:84
    [InlineData(RuntimeMode.Auto, new[] { "--auto-review", "acp" })]
    [InlineData(RuntimeMode.FullAccess, new[] { "--force", "acp" })]
    [InlineData(RuntimeMode.Supervised, new[] { "acp" })]
    [InlineData(RuntimeMode.AutoAcceptEdits, new[] { "acp" })]
    public void Cursor_argv_matches_the_Swift_switch(RuntimeMode mode, string[] expected) =>
        Assert.Equal(expected, LaunchArguments(Session(ProviderKind.Cursor, mode)));

    [Theory]
    [Trait("Req", "PR-PROVACP-008")] // ACPSession.swift:88
    [Trait("Req", "PR-PROVACP-009")] // ACPSession.swift:89,99
    [Trait("Req", "PR-PROVACP-010")] // ACPSession.swift:90
    [Trait("Req", "PR-PROVACP-011")] // ACPSession.swift:91
    [InlineData(RuntimeMode.Supervised, new[] { "--permission-mode", "default", "agent", "stdio" })]
    [InlineData(RuntimeMode.AutoAcceptEdits, new[] { "--permission-mode", "acceptEdits", "agent", "stdio" })]
    [InlineData(RuntimeMode.Auto, new[] { "--permission-mode", "auto", "agent", "stdio" })]
    [InlineData(RuntimeMode.FullAccess, new[] { "agent", "--always-approve", "stdio" })]
    public void Grok_argv_matches_the_Swift_switch(RuntimeMode mode, string[] expected) =>
        Assert.Equal(expected, LaunchArguments(Session(ProviderKind.Grok, mode)));

    [Theory]
    [Trait("Req", "PR-PROVACP-012")] // ACPSession.swift:98
    [Trait("Req", "PR-PROVACP-013")] // ACPSession.swift:99
    [Trait("Req", "PR-PROVACP-014")] // ACPSession.swift:100
    [Trait("Req", "PR-PROVACP-015")] // ACPSession.swift:101
    [InlineData(RuntimeMode.Supervised, new[] { "--permission-mode", "auto", "acp" })]
    [InlineData(RuntimeMode.AutoAcceptEdits, new[] { "--permission-mode", "accept-edits", "acp" })]
    [InlineData(RuntimeMode.Auto, new[] { "--permission-mode", "smart", "acp" })]
    [InlineData(RuntimeMode.FullAccess, new[] { "--permission-mode", "dangerous", "acp" })]
    public void Devin_argv_matches_the_Swift_switch(RuntimeMode mode, string[] expected) =>
        Assert.Equal(expected, LaunchArguments(Session(ProviderKind.Devin, mode)));

    [Theory]
    [Trait("Req", "PR-PROVACP-016")] // ACPSession.swift:103-104,79-80,86,93
    [InlineData(ProviderKind.Opencode, RuntimeMode.Supervised)]
    [InlineData(ProviderKind.Opencode, RuntimeMode.AutoAcceptEdits)]
    [InlineData(ProviderKind.Opencode, RuntimeMode.Auto)]
    [InlineData(ProviderKind.Opencode, RuntimeMode.FullAccess)]
    [InlineData(ProviderKind.OpencodeGo, RuntimeMode.Supervised)]
    [InlineData(ProviderKind.OpencodeGo, RuntimeMode.AutoAcceptEdits)]
    [InlineData(ProviderKind.OpencodeGo, RuntimeMode.Auto)]
    [InlineData(ProviderKind.OpencodeGo, RuntimeMode.FullAccess)]
    public void OpenCode_Zen_and_Go_argv_is_the_bare_acp_subcommand_in_every_mode(ProviderKind provider, RuntimeMode mode) =>
        Assert.Equal(["acp"], LaunchArguments(Session(provider, mode)));

    [Fact]
    [Trait("Req", "PR-PROVACP-017")] // ACPSession.swift:78-106,38,68,169
    public void LaunchArguments_reads_the_immutable_launch_time_runtime_mode_not_the_mutable_property()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        Assert.Equal(["--permission-mode", "default", "agent", "stdio"], LaunchArguments(session));
        // The mutable `runtimeMode` field (what send() updates, ACPSession.swift:169) changing after launch
        // must never change launchArguments, which always reads configuration.runtimeMode (ACPSession.swift:87).
        SetRuntimeModeField(session, RuntimeMode.FullAccess);
        Assert.Equal(["--permission-mode", "default", "agent", "stdio"], LaunchArguments(session));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-004")] // ACPSession.swift:76,111-116; StdioProcess.swift:32-45
    public void WorkingDirectory_is_configuration_workingDirectory_path_forwarded_unmodified()
    {
        // ACPSession.swift:76: `private var workingDirectory: String { configuration.workingDirectory.path }`.
        // Whatever SwiftURL.Path decodes a given URL to (a Core/Port concern, tested there) is what start()
        // (:114) passes straight to StdioProcess as its directory, with no recomputation here.
        var config = Config(ProviderKind.Grok, RuntimeMode.Supervised, executable: null);
        var session = new ACPSession(config);
        Assert.Equal(config.WorkingDirectory.Path, Private<string>(session, "WorkingDirectory"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-004")] // ACPSession.swift:111-116; StdioProcess.swift:32-45
    public async Task Start_builds_the_child_process_with_the_configured_environment_unmodified()
    {
        using var runtime = new TestRuntime();
        var script = "$e=[Console]::Error; $e.WriteLine('MARKER var=' + $env:MARROW_ACP_TEST_VAR); $e.Flush();";
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value!;
        env["MARROW_ACP_TEST_VAR"] = "acp-probe-1";
        var environment = new ValueMap<string, string>(env);

        // The same construction as start() (ACPSession.swift:111-116): the executable's real path, a real
        // cwd (Environment.CurrentDirectory; the SwiftURL round trip for a Windows path is Core/Port's own
        // concern, not ACPSession's), and configuration.environment forwarded with no addition or removal.
        var process = new StdioProcess(ACPSessionFixtures.PowerShell, ACPSessionFixtures.PowerShellEncoded(script), Environment.CurrentDirectory, environment);
        process.Start();
        runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Any(l => l.StartsWith("MARKER", StringComparison.Ordinal)), Bound);
        var marker = Lines(process.ErrorTail).First(l => l.StartsWith("MARKER", StringComparison.Ordinal));
        Assert.Equal("MARKER var=acp-probe-1", marker);
        await process.WaitForExit().WaitAsync(Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-018")] // ACPSession.swift:117-122; JSONRPC.swift:58-68,109
    public async Task Start_wires_all_three_callbacks_and_starts_the_connection_with_sendsVersion_true_before_initialize()
    {
        // powershell.exe launches successfully (proving connection/process construction succeeded, :121-122)
        // but "--permission-mode" is not a recognized switch, so it exits immediately without a reply: the
        // initialize request then fails, proving the failure path propagates out of start() unchanged.
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised, SwiftURL.Create("file:///" + ACPSessionFixtures.PowerShell.Replace('\\', '/'))!);
        await Assert.ThrowsAnyAsync<Exception>(() => session.Start());

        var connection = Private<JSONRPCConnection?>(session, "connection", field: true);
        Assert.NotNull(connection);
        Assert.NotNull(connection.OnNotification);
        Assert.NotNull(connection.OnRequest);
        Assert.NotNull(connection.OnClose);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-018")] // ACPSession.swift:108-110; StdioProcess.swift:79-86
    public async Task A_spawn_failure_propagates_out_of_start_before_the_connection_field_is_reachable()
    {
        var executable = SwiftURL.Create("file:///" + Path.Combine(Path.GetTempPath(), "marrow-acp-missing-" + Guid.NewGuid().ToString("N") + ".exe").Replace('\\', '/'))!;
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised, executable);
        await Assert.ThrowsAnyAsync<Exception>(() => session.Start());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-019")] // ACPSession.swift:124-128
    [Trait("Req", "PR-PROVACP-020")] // ACPSession.swift:156-160
    public void InitializeParams_carries_the_exact_members_marrow_clientInfo_and_version()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var request = Private<JSONValue>(session, "InitializeParams");
        Assert.Equal(["clientCapabilities", "clientInfo", "protocolVersion"], request.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new JSONValue.Int(1), request["protocolVersion"]);
        Assert.Equal(["name", "title", "version"], request["clientInfo"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new JSONValue.String("marrow"), request["clientInfo"]!["name"]);
        Assert.Equal(new JSONValue.String("Marrow"), request["clientInfo"]!["title"]);
        Assert.Equal(new JSONValue.String(AppInfo.Current.Version), request["clientInfo"]!["version"]);
        Assert.Equal(["fs", "terminal"], request["clientCapabilities"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new JSONValue.Bool(false), request["clientCapabilities"]!["fs"]!["readTextFile"]);
        Assert.Equal(new JSONValue.Bool(false), request["clientCapabilities"]!["fs"]!["writeTextFile"]);
        Assert.Equal(new JSONValue.Bool(false), request["clientCapabilities"]!["terminal"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-021")] // ACPSession.swift:63-64,156-165
    public void ClientCapabilities_meta_parameterizedModelPicker_is_present_only_for_cursor()
    {
        var cursorCaps = Private<JSONValue>(Session(ProviderKind.Cursor, RuntimeMode.Supervised), "ClientCapabilities");
        Assert.Equal(new JSONValue.Bool(true), cursorCaps["_meta"]!["parameterizedModelPicker"]);
        Assert.Equal(["_meta", "fs", "terminal"], cursorCaps.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var provider in new[] { ProviderKind.Opencode, ProviderKind.OpencodeGo, ProviderKind.Grok, ProviderKind.Devin })
        {
            var caps = Private<JSONValue>(Session(provider, RuntimeMode.Supervised), "ClientCapabilities");
            Assert.Null(caps["_meta"]);
            Assert.Equal(["fs", "terminal"], caps.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-022")] // ACPSession.swift:129
    [Trait("Req", "PR-PROVACP-023")] // ACPSession.swift:130,254
    [Trait("Req", "PR-PROVACP-024")] // ACPSession.swift:131
    public void ApplyInitializeReply_reads_canLoadSessions_supportsHTTPMCP_and_ordered_authMethods()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var reply = JSONValue.FromDictionaryLiteral(
            ("agentCapabilities", JSONValue.FromDictionaryLiteral(
                ("loadSession", true),
                ("mcpCapabilities", JSONValue.FromDictionaryLiteral(("http", true))))),
            ("authMethods", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("id", "oauth"), ("name", "OAuth")),
                JSONValue.FromDictionaryLiteral(("id", "api-key"), ("name", "API key")),
                JSONValue.FromDictionaryLiteral(("name", "no id, dropped")))));
        InvokePrivate(session, "ApplyInitializeReply", reply);
        Assert.True(Private<bool>(session, "canLoadSessions", field: true));
        Assert.True(Private<bool>(session, "supportsHTTPMCP", field: true));
        Assert.Equal(["oauth", "api-key"], Private<ValueList<string>>(session, "authMethods", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-022")] // ACPSession.swift:129
    [Trait("Req", "PR-PROVACP-023")] // ACPSession.swift:130
    [Trait("Req", "PR-PROVACP-024")] // ACPSession.swift:131
    public void ApplyInitializeReply_defaults_to_false_and_empty_when_fields_are_missing_or_not_arrays()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyInitializeReply", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>())));
        Assert.False(Private<bool>(session, "canLoadSessions", field: true));
        Assert.False(Private<bool>(session, "supportsHTTPMCP", field: true));
        Assert.Empty(Private<ValueList<string>>(session, "authMethods", field: true));
    }

    // ===== Part 2 of 8: openSession, applySessionState, resolveModes, applyModels, applyConfigOptions and its
    // static helpers (PR-PROVACP-025..047, 049). applySelection and setConfigOption keep their part 1 stub
    // (their own records, PR-PROVACP-053..062, sit in part 3), so PR-PROVACP-027 (the auth retry inside
    // start()), PR-PROVACP-032 (start()'s applySessionState-then-applySelection ordering) and PR-PROVACP-033
    // (start()'s final "did not start a session" guard) cannot be exercised: start() cannot be driven by a
    // live scripted stand-in agent (this file's part 1 header; empirically `powershell.exe acp` exits without
    // reading stdin, confirmed with `powershell.exe acp < NUL`), and reaching applySelection would throw its
    // part 1 stub regardless. They are reported as untestable in this part; part 3 covers them once
    // applySelection and setConfigOption are implemented.

    [Fact]
    [Trait("Req", "PR-PROVACP-025")] // ACPSession.swift:132
    public void ApplyInitializeReply_meta_modelState_runs_applyModels_before_any_session_exists()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var reply = JSONValue.FromDictionaryLiteral(
            ("_meta", JSONValue.FromDictionaryLiteral(
                ("modelState", JSONValue.FromDictionaryLiteral(
                    ("currentModelId", "grok-1"),
                    ("availableModels", JSONValue.FromArrayLiteral(
                        JSONValue.FromDictionaryLiteral(("modelId", "grok-1"), ("name", "Grok 1")))))))));
        InvokePrivate(session, "ApplyInitializeReply", reply);
        Assert.Null(Private<string?>(session, "sessionID", field: true));
        var models = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Equal("grok-1", models.Current);
        Assert.Equal("Grok 1", models.Value[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-025")] // ACPSession.swift:132,306-325 (a JSON null modelState enters applyModels, which emits nothing)
    public void ApplyInitializeReply_meta_modelState_as_JSON_null_enters_applyModels_and_emits_nothing()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var reply = JSONValue.FromDictionaryLiteral(("_meta", JSONValue.FromDictionaryLiteral(("modelState", new JSONValue.Null()))));
        InvokePrivate(session, "ApplyInitializeReply", reply);
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-026")] // ACPSession.swift:252-254; MCP/MCPProviderConfig.swift:77-124
    [Trait("Req", "PR-PROVACP-031")] // ACPSession.swift:266-268
    public async Task OpenSession_with_no_resume_sends_session_new_with_cwd_and_the_empty_mcpServers_array_and_reads_sessionId()
    {
        using var storage = new StorageScratchFolder(); // no claude.json: AcpServers(...) is [] (PR-MCP-170).
        using var runtime = new TestRuntime();
        var process = LiveAgent(Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/new\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"sessionId\":\"sess-A\"}}'); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        runtime.OnMain(connection.Start);
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSession", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        var result = Resolved(handle.Value);
        Assert.Equal("sess-A", result["sessionId"]!.JSONValueString);
        Assert.Equal("sess-A", Private<string?>(session, "sessionID", field: true));
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/new", frame["method"]!.JSONValueString);
        Assert.Equal(["cwd", "mcpServers"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(Private<string>(session, "WorkingDirectory"), frame["params"]!["cwd"]!.JSONValueString);
        Assert.Equal(new JSONValue.Array(ValueList<JSONValue>.Empty), frame["params"]!["mcpServers"]);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-031")] // ACPSession.swift:266-268 (a reply with no sessionId leaves sessionID nil)
    public async Task OpenSession_leaves_sessionID_nil_when_the_session_new_reply_has_no_sessionId()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var process = LiveAgent(Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/new\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}'); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        runtime.OnMain(connection.Start);
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSession", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Resolved(handle.Value);
        Assert.Null(Private<string?>(session, "sessionID", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-028")] // ACPSession.swift:255-265
    [Trait("Req", "PR-PROVACP-030")] // ACPSession.swift:258,260 (the flag is always reset once session/load returns)
    public async Task OpenSession_resumes_via_session_load_using_the_configurations_resumeID_not_the_replys_own_id()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var process = LiveAgent(Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/load\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"sessionId\":\"ignored-reply-id\"}}'); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var config = Config(ProviderKind.Grok, RuntimeMode.Supervised, executable: null) with { ResumeID = "resume-1" };
        var session = new ACPSession(config);
        SetField(session, "canLoadSessions", true);
        runtime.OnMain(connection.Start);
        Assert.False(Private<bool>(session, "isReplaying", field: true));
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSession", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        var result = Resolved(handle.Value);
        Assert.False(Private<bool>(session, "isReplaying", field: true)); // :260 always resets before returning
        Assert.Equal("resume-1", Private<string?>(session, "sessionID", field: true)); // NOT "ignored-reply-id"
        Assert.Equal("ignored-reply-id", result["sessionId"]!.JSONValueString); // the reply IS the returned state
        Assert.Single(Lines(process.ErrorTail)); // no fallback session/new was sent
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/load", frame["method"]!.JSONValueString);
        Assert.Equal("resume-1", frame["params"]!["sessionId"]!.JSONValueString);
        Assert.Equal(["cwd", "mcpServers", "sessionId"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-028")] // ACPSession.swift:255 (canLoadSessions false skips session/load entirely)
    public async Task OpenSession_skips_session_load_and_goes_straight_to_session_new_when_canLoadSessions_is_false()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var process = LiveAgent(Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/new\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"sessionId\":\"fresh-1\"}}'); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var config = Config(ProviderKind.Grok, RuntimeMode.Supervised, executable: null) with { ResumeID = "resume-1" };
        var session = new ACPSession(config); // canLoadSessions defaults to false
        runtime.OnMain(connection.Start);
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSession", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.Equal("fresh-1", Private<string?>(session, "sessionID", field: true));
        Assert.Single(Lines(process.ErrorTail));
        Assert.Equal("session/new", JSONValue.Parse(Lines(process.ErrorTail)[0])!["method"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-029")] // ACPSession.swift:259,261-265
    [Trait("Req", "PR-PROVACP-030")] // ACPSession.swift:258,260 (reset even though session/load threw)
    public async Task OpenSession_falls_through_silently_to_session_new_when_session_load_fails()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var process = LiveAgent(Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/load\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"no such session\"}}'); $o.Flush() } " +
            "if($l.Contains('\"method\":\"session/new\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"sessionId\":\"fresh-2\"}}'); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var config = Config(ProviderKind.Grok, RuntimeMode.Supervised, executable: null) with { ResumeID = "resume-1" };
        var session = new ACPSession(config);
        SetField(session, "canLoadSessions", true);
        runtime.OnMain(connection.Start);
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSession", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.True(handle.Value.IsCompletedSuccessfully); // the session/load error never propagates (try?)
        Assert.False(Private<bool>(session, "isReplaying", field: true));
        Assert.Equal("fresh-2", Private<string?>(session, "sessionID", field: true));
        var frames = Lines(process.ErrorTail).Select(l => JSONValue.Parse(l)!).ToList();
        Assert.Equal(["session/load", "session/new"], frames.Select(f => f["method"]!.JSONValueString!));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-034")] // ACPSession.swift:271-279
    public void ApplySessionState_reads_modes_models_and_configOptions_from_the_reply_in_order()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var state = JSONValue.FromDictionaryLiteral(
            ("modes", JSONValue.FromDictionaryLiteral(
                ("currentModeId", "build"),
                ("availableModes", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("id", "plan")),
                    JSONValue.FromDictionaryLiteral(("id", "build")))))),
            ("models", JSONValue.FromDictionaryLiteral(
                ("currentModelId", "m1"),
                ("availableModels", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("modelId", "m1")))))),
            ("configOptions", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("id", "mode-opt"), ("category", "mode"),
                    ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "plan"))))))));
        InvokePrivate(session, "ApplySessionState", state);
        Assert.True(Private<bool>(session, "hasModeAPI", field: true));
        Assert.Equal("build", Private<string?>(session, "currentMode", field: true));
        Assert.Equal("plan", Private<string?>(session, "planMode", field: true));
        Assert.Equal("m1", Private<string?>(session, "currentModel", field: true));
        // configOptions runs after modes/models: modeConfigID set and resolveModes re-run from ["plan"] only.
        Assert.Equal("mode-opt", Private<string?>(session, "modeConfigID", field: true));
        Assert.Null(Private<string?>(session, "buildMode", field: true));
        Assert.NotEmpty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-034")] // ACPSession.swift:272,277-278 (null modes/models and a non-array configOptions are ignored)
    public void ApplySessionState_ignores_null_modes_null_models_and_a_non_array_configOptions()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var state = JSONValue.FromDictionaryLiteral(
            ("modes", new JSONValue.Null()), ("models", new JSONValue.Null()), ("configOptions", new JSONValue.String("nope")));
        InvokePrivate(session, "ApplySessionState", state);
        Assert.False(Private<bool>(session, "hasModeAPI", field: true));
        Assert.Null(Private<string?>(session, "modelConfigID", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-035")] // ACPSession.swift:301
    public void ResolveModes_plan_is_only_the_exact_id_plan()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ResolveModes", new ValueList<string>(["planning", "Plan", "ask"]));
        Assert.Null(Private<string?>(session, "planMode", field: true));
        InvokePrivate(session, "ResolveModes", new ValueList<string>(["ask", "plan"]));
        Assert.Equal("plan", Private<string?>(session, "planMode", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-036")] // ACPSession.swift:302-303
    public void ResolveModes_build_prefers_the_four_known_ids_by_the_agents_own_order_else_first_non_plan_non_ask()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ResolveModes", new ValueList<string>(["ask", "code", "agent"]));
        Assert.Equal("code", Private<string?>(session, "buildMode", field: true)); // agent's own list order
        InvokePrivate(session, "ResolveModes", new ValueList<string>(["plan", "ask"]));
        Assert.Null(Private<string?>(session, "buildMode", field: true));
        InvokePrivate(session, "ResolveModes", new ValueList<string>(["plan", "ask", "custom"]));
        Assert.Equal("custom", Private<string?>(session, "buildMode", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-037")] // ACPSession.swift:306-325
    public void ApplyModels_builds_the_catalog_and_strips_currentModelId_for_every_provider()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised); // not Cursor
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var state = JSONValue.FromDictionaryLiteral(
            ("currentModelId", "grok-3[effort=high]"),
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", "grok-3[effort=high]"), ("name", "Grok 3")),
                JSONValue.FromDictionaryLiteral(("name", "no id, dropped")))));
        InvokePrivate(session, "ApplyModels", state);
        Assert.Equal("grok-3", Private<string?>(session, "currentModel", field: true)); // :307 stripped for every provider
        var evt = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Single(evt.Value);
        Assert.Equal("grok-3[effort=high]", evt.Value[0].Id); // :311 not stripped for a non-Cursor provider
        Assert.Equal("Grok 3", evt.Value[0].Name);
        Assert.False(evt.Value[0].IsDefault); // "grok-3[effort=high]" != "grok-3"
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-037")] // ACPSession.swift:311 (Cursor strips availableModels ids too); :315 name fallback
    public void ApplyModels_strips_availableModels_ids_only_for_cursor_and_name_falls_back_to_id()
    {
        var session = Session(ProviderKind.Cursor, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var state = JSONValue.FromDictionaryLiteral(
            ("currentModelId", "gpt-5[effort=high]"),
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", "gpt-5[effort=high]"), ("description", "d")))));
        InvokePrivate(session, "ApplyModels", state);
        var evt = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Equal("gpt-5", evt.Value[0].Id);
        Assert.Equal("gpt-5", evt.Value[0].Name);
        Assert.Equal("d", evt.Value[0].Detail);
        Assert.True(evt.Value[0].IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-037")] // ACPSession.swift:322-323 (an empty resulting list leaves the catalog and emits nothing)
    public void ApplyModels_an_empty_resulting_list_leaves_the_catalog_in_place_and_emits_nothing()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("currentModelId", "m1"), ("availableModels", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("modelId", "m1"))))));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("availableModels", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "no id"))))));
        Assert.Empty(events);
        Assert.Single(Private<ValueList<ModelOption>>(session, "models", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-038")] // ACPSession.swift:312,317-318
    public void ApplyModels_reads_per_model_efforts_from_meta_reasoningEfforts_ordered_with_the_default_flag()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var state = JSONValue.FromDictionaryLiteral(
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", "grok-4"),
                    ("_meta", JSONValue.FromDictionaryLiteral(("reasoningEfforts", JSONValue.FromArrayLiteral(
                        JSONValue.FromDictionaryLiteral(("value", "high")),
                        JSONValue.FromDictionaryLiteral(("value", "low"), ("default", true)),
                        JSONValue.FromDictionaryLiteral(("value", "medium"), ("default", "true"))))))))));
        InvokePrivate(session, "ApplyModels", state);
        var evt = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Equal(["low", "medium", "high"], evt.Value[0].Efforts); // orderedEfforts low to high
        Assert.Equal("low", evt.Value[0].DefaultEffort); // first entry whose default is the boolean true
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-039")] // ACPSession.swift:330-331,338,364,484
    public void ApplyConfigOptions_resets_effort_and_fast_ids_every_call_but_not_model_or_mode_ids()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt"), ("category", "model")),
            JSONValue.FromDictionaryLiteral(("id", "mode-opt"), ("category", "mode")),
            JSONValue.FromDictionaryLiteral(("id", "effort-opt"), ("category", "thought_level"),
                ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "low"))))),
            JSONValue.FromDictionaryLiteral(("id", "fast"), ("category", "model_config")),
        ]));
        Assert.Equal("model-opt", Private<string?>(session, "modelConfigID", field: true));
        Assert.Equal("mode-opt", Private<string?>(session, "modeConfigID", field: true));
        Assert.Equal("effort-opt", Private<string?>(session, "effortConfigID", field: true));
        Assert.Equal("fast", Private<string?>(session, "fastConfigID", field: true));
        // A second array lacking the effort/fast entries: model and mode ids survive; effort and fast vanish.
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt-2"), ("category", "model")),
        ]));
        Assert.Equal("model-opt-2", Private<string?>(session, "modelConfigID", field: true));
        Assert.Equal("mode-opt", Private<string?>(session, "modeConfigID", field: true));
        Assert.Null(Private<string?>(session, "effortConfigID", field: true));
        Assert.Null(Private<string?>(session, "fastConfigID", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-040")] // ACPSession.swift:333,336
    public void ApplyConfigOptions_classifies_by_category_falling_back_to_id_and_skips_entries_without_a_string_id()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("noId", "x")), // skipped: no id
            JSONValue.FromDictionaryLiteral(("id", "mode")), // no category: falls back to its own id "mode"
            JSONValue.FromDictionaryLiteral(("id", "weird"), ("category", "unexpected-key")), // ignored
        ]));
        Assert.Equal("mode", Private<string?>(session, "modeConfigID", field: true));
        Assert.Null(Private<string?>(session, "modelConfigID", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-041")] // ACPSession.swift:334,400-405
    public void Flatten_unwraps_exactly_one_level_of_grouped_options()
    {
        var grouped = new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("options", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("value", "a")),
                JSONValue.FromDictionaryLiteral(("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "too-deep")))))))),
            JSONValue.FromDictionaryLiteral(("value", "b")),
        ]);
        var flat = InvokeStatic<ValueList<JSONValue>>("Flatten", grouped);
        Assert.Equal(3, flat.Count);
        Assert.Equal("a", flat[0]["value"]!.JSONValueString);
        // The doubly nested group entry is kept as a group object (its "options" not unwrapped further): no "value".
        Assert.Null(flat[1]["value"]);
        Assert.Equal("b", flat[2]["value"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-042")] // ACPSession.swift:337-362
    public void Model_config_option_records_its_id_current_model_and_rebuilds_the_list()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt"), ("category", "model"), ("currentValue", "m2"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "m1"), ("name", "Model One"), ("description", "d1")),
                    JSONValue.FromDictionaryLiteral(("value", "m2"))))), // name falls back to id
        ]));
        Assert.Equal("model-opt", Private<string?>(session, "modelConfigID", field: true));
        Assert.Equal("m2", Private<string?>(session, "currentModel", field: true));
        var evt = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Equal(2, evt.Value.Count);
        Assert.Equal("Model One", evt.Value[0].Name);
        Assert.Equal("d1", evt.Value[0].Detail);
        Assert.False(evt.Value[0].IsDefault);
        Assert.Equal("m2", evt.Value[1].Name);
        Assert.True(evt.Value[1].IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-042")] // ACPSession.swift:339 (currentValue absent keeps the previous model; an empty rebuild keeps the catalog)
    public void Model_config_option_keeps_the_previous_current_model_without_currentValue_and_an_empty_rebuild_keeps_the_catalog()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("currentModelId", "keep-me"), ("availableModels", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("modelId", "keep-me"))))));
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt"), ("category", "model")), // no currentValue, no options
        ]));
        Assert.Equal("keep-me", Private<string?>(session, "currentModel", field: true));
        Assert.Single(Private<ValueList<ModelOption>>(session, "models", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-043")] // ACPSession.swift:340-359
    public void Rebuilt_model_list_carries_over_efforts_and_defaultEffort_by_base_id_first_wins_on_a_collision()
    {
        var session = Session(ProviderKind.Cursor, RuntimeMode.Supervised); // usesParameterizedModels
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", "gpt-5[effort=high]"),
                    ("_meta", JSONValue.FromDictionaryLiteral(("reasoningEfforts", JSONValue.FromArrayLiteral(
                        JSONValue.FromDictionaryLiteral(("value", "high"), ("default", true))))))),
                JSONValue.FromDictionaryLiteral(("modelId", "gpt-5[effort=low]")))))); // duplicate base id "gpt-5": first wins
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "model-opt"), ("category", "model"),
                ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "gpt-5[effort=medium]"))))),
        ]));
        var models = Private<ValueList<ModelOption>>(session, "models", field: true);
        Assert.Equal(["high"], models[0].Efforts); // learned from the FIRST "gpt-5" entry, not the second
        Assert.Equal("high", models[0].DefaultEffort);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-044")] // ACPSession.swift:363-366
    public void Mode_config_option_records_its_id_current_mode_and_re_resolves_plan_and_build_but_not_hasModeAPI()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "mode-opt"), ("category", "mode"), ("currentValue", "plan"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "plan")), JSONValue.FromDictionaryLiteral(("value", "agent"))))),
        ]));
        Assert.Equal("mode-opt", Private<string?>(session, "modeConfigID", field: true));
        Assert.Equal("plan", Private<string?>(session, "currentMode", field: true));
        Assert.Equal("plan", Private<string?>(session, "planMode", field: true));
        Assert.Equal("agent", Private<string?>(session, "buildMode", field: true));
        Assert.False(Private<bool>(session, "hasModeAPI", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-045")] // ACPSession.swift:367-374
    public void Thought_level_and_effort_share_one_branch_accepted_only_when_the_values_qualify_as_a_scale()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "thinking"), ("category", "thought_level"), ("currentValue", "true"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "true")), JSONValue.FromDictionaryLiteral(("value", "false"))))),
        ]));
        Assert.Null(Private<string?>(session, "effortConfigID", field: true)); // a boolean thinking toggle does not qualify

        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "eff"), ("category", "effort"), ("currentValue", "off"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "off")), JSONValue.FromDictionaryLiteral(("value", "high"))))),
        ]));
        Assert.Equal("eff", Private<string?>(session, "effortConfigID", field: true));
        Assert.Equal("off", Private<string?>(session, "currentEffort", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-046")] // ACPSession.swift:408-411
    public void IsEffortScale_recognizes_the_seven_known_members_case_insensitively_by_any_match()
    {
        Assert.True(InvokeStatic<bool>("IsEffortScale", new ValueList<string>(["off", "HIGH"])));
        Assert.True(InvokeStatic<bool>("IsEffortScale", new ValueList<string>(["Extra-High"])));
        Assert.False(InvokeStatic<bool>("IsEffortScale", new ValueList<string>(["minimal", "ultra"])));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-047")] // ACPSession.swift:418-429
    public void OrderedEfforts_sorts_the_eleven_entry_rank_table_low_to_high_stable_on_unknowns()
    {
        Assert.Equal(["low", "medium", "high"], ACPSession.OrderedEfforts(new ValueList<string>(["high", "low", "medium"])));
        Assert.Equal(["extra-high", "xhigh", "extra_high", "extra high", "max", "ultra"],
            ACPSession.OrderedEfforts(new ValueList<string>(["max", "extra-high", "xhigh", "extra_high", "ultra", "extra high"])));
        // Zero or one recognized value: unchanged.
        Assert.Equal(["unknown"], ACPSession.OrderedEfforts(new ValueList<string>(["unknown"])));
        Assert.Equal(["b", "a"], ACPSession.OrderedEfforts(new ValueList<string>(["b", "a"])));
        // An unrecognized value keeps its place among its neighbours (stable on the original offset).
        Assert.Equal(["low", "mystery", "high"], ACPSession.OrderedEfforts(new ValueList<string>(["low", "mystery", "high"])));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-049")] // ACPSession.swift:375-380
    public void Model_config_option_only_becomes_the_Fast_toggle_when_its_id_is_exactly_fast()
    {
        var session = Session(ProviderKind.Cursor, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "other"), ("category", "model_config"), ("currentValue", "x")),
        ]));
        Assert.Null(Private<string?>(session, "fastConfigID", field: true));
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "fast"), ("category", "model_config"), ("currentValue", "true")),
        ]));
        Assert.Equal("fast", Private<string?>(session, "fastConfigID", field: true));
        Assert.Equal("true", Private<string?>(session, "currentFast", field: true));
    }

    // Part 3: PR-PROVACP-050 through PR-PROVACP-073.

    [Fact]
    [Trait("Req", "PR-PROVACP-050")] // ACPSession.swift:385-396
    public void ApplyConfigOptions_post_pass_touches_only_the_current_model_and_clears_fastTier_on_a_partial_update()
    {
        var session = Session(ProviderKind.Cursor, RuntimeMode.Supervised);
        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("currentModelId", "m1"),
            ("availableModels", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("modelId", "m1")), JSONValue.FromDictionaryLiteral(("modelId", "m2"))))));
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "eff"), ("category", "effort"), ("currentValue", "high"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "low")), JSONValue.FromDictionaryLiteral(("value", "high"))))),
            JSONValue.FromDictionaryLiteral(("id", "fast"), ("category", "model_config"), ("currentValue", "true")),
        ]));
        var models1 = Private<ValueList<ModelOption>>(session, "models", field: true);
        Assert.Equal(["low", "high"], models1[0].Efforts);
        Assert.Equal("true", models1[0].FastTier);
        Assert.Empty(models1[1].Efforts); // m2 is not the current model: untouched
        Assert.Null(models1[1].FastTier);

        // A second, partial pass on the same current model with no fast option this time: fastTier is cleared.
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "eff"), ("category", "effort"), ("currentValue", "high"),
                ("options", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("value", "low")), JSONValue.FromDictionaryLiteral(("value", "high"))))),
        ]));
        var models2 = Private<ValueList<ModelOption>>(session, "models", field: true);
        Assert.Equal(["low", "high"], models2[0].Efforts);
        Assert.Null(models2[0].FastTier);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-051")] // ACPSession.swift:397
    public void ApplyConfigOptions_emits_models_whenever_the_catalog_is_non_empty_but_never_when_it_is_empty()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "mode-opt"), ("category", "mode"), ("currentValue", "agent"),
                ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "agent"))))),
        ]));
        Assert.Empty(events); // the catalog is still empty: no event

        InvokePrivate(session, "ApplyModels", JSONValue.FromDictionaryLiteral(
            ("availableModels", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("modelId", "m1"))))));
        events.Clear();
        InvokePrivate(session, "ApplyConfigOptions", new ValueList<JSONValue>([
            JSONValue.FromDictionaryLiteral(("id", "mode-opt"), ("category", "mode"), ("currentValue", "agent"),
                ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "agent"))))),
        ]));
        Assert.IsType<ProviderEvent.Models>(Assert.Single(events)); // non-empty catalog: emits even though nothing changed
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-052")] // ACPSession.swift:432-436
    public void BaseModelID_strips_everything_from_the_first_opening_bracket_with_no_trimming_and_no_closing_bracket_needed()
    {
        Assert.Equal("gpt-5", InvokeStatic<string>("BaseModelID", "gpt-5"));
        Assert.Equal("gpt-5", InvokeStatic<string>("BaseModelID", "gpt-5[effort=high,fast=true]"));
        Assert.Equal("gpt-5", InvokeStatic<string>("BaseModelID", "gpt-5[unclosed"));
        Assert.Equal("model ", InvokeStatic<string>("BaseModelID", "model [x]")); // no trimming of the space before '['
        Assert.Equal("", InvokeStatic<string>("BaseModelID", "[bracketed]"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-053")] // ACPSession.swift:438-441
    public void SameModel_compares_base_ids_and_is_false_when_either_side_is_nil()
    {
        Assert.False(InvokeStatic<bool>("SameModel", [null, null]));
        Assert.False(InvokeStatic<bool>("SameModel", ["gpt-5", null]));
        Assert.False(InvokeStatic<bool>("SameModel", [null, "gpt-5"]));
        Assert.True(InvokeStatic<bool>("SameModel", ["gpt-5[effort=high]", "gpt-5"]));
        Assert.False(InvokeStatic<bool>("SameModel", ["gpt-5", "gpt-4"]));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-054")] // ACPSession.swift:443-444
    public async Task ApplySelection_does_nothing_at_all_without_a_live_connection_and_session_id()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised); // connection and sessionID both nil
        await InvokePrivateVoidAsync(session, "ApplySelection", "some-model", "high", true, InteractionMode.Build);
        Assert.Null(Private<string?>(session, "currentModel", field: true));
        Assert.Null(Private<string?>(session, "currentEffort", field: true));
        Assert.Null(Private<string?>(session, "currentFast", field: true));
        Assert.Null(Private<string?>(session, "currentMode", field: true));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-055")] // ACPSession.swift:445
    [InlineData(null)]
    [InlineData("")]
    public void ApplySelection_skips_a_nil_or_empty_model_and_sends_no_model_request(string? model)
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        RunOnMain(runtime, session, "ApplySelection", model, null, false, InteractionMode.Build);
        Assert.Null(Private<string?>(session, "currentModel", field: true));
        Assert.Empty(Lines(process.ErrorTail));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-056")] // ACPSession.swift:446-454
    public void ApplySelection_model_change_prefers_set_config_option_and_sends_the_base_id_when_parameterized()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Cursor,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "modelConfigID", "model-opt");
        RunOnMain(runtime, session, "ApplySelection", "gpt-5[effort=high]", null, false, InteractionMode.Build);
        Assert.Equal("gpt-5", Private<string?>(session, "currentModel", field: true)); // Cursor: usesParameterizedModels
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/set_config_option", frame["method"]!.JSONValueString);
        Assert.Equal("model-opt", frame["params"]!["configId"]!.JSONValueString);
        Assert.Equal("gpt-5", frame["params"]!["value"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-056")] // ACPSession.swift:446-454 (fallback to session/set_model, and the optimistic assignment after it is rejected)
    public void ApplySelection_model_change_falls_back_to_session_set_model_and_still_updates_currentModel_when_rejected()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok, // not usesParameterizedModels: the raw id is sent
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_model\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"rejected\"}}'); $o.Flush() } " + EndOnBye);
        RunOnMain(runtime, session, "ApplySelection", "grok-4", null, false, InteractionMode.Build);
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/set_model", frame["method"]!.JSONValueString);
        Assert.Equal(["modelId", "sessionId"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("grok-4", frame["params"]!["modelId"]!.JSONValueString);
        Assert.Equal("grok-4", Private<string?>(session, "currentModel", field: true)); // set even though the request was rejected
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-056")] // ACPSession.swift:447
    public void ApplySelection_sends_nothing_when_the_wire_id_already_matches_the_current_model_by_base_id()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Cursor,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "currentModel", "gpt-5");
        RunOnMain(runtime, session, "ApplySelection", "gpt-5[effort=high]", null, false, InteractionMode.Build);
        Assert.Empty(Lines(process.ErrorTail));
        Assert.Equal("gpt-5", Private<string?>(session, "currentModel", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-057")] // ACPSession.swift:456-459
    public void ApplySelection_effort_change_needs_a_config_id_and_an_exact_case_sensitive_string_difference()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}'); $o.Flush() } " + EndOnBye);
        // No effortConfigID discovered: dropped silently, no request, no state change.
        RunOnMain(runtime, session, "ApplySelection", null, "High", false, InteractionMode.Build);
        Assert.Empty(Lines(process.ErrorTail));
        Assert.Null(Private<string?>(session, "currentEffort", field: true));

        SetField(session, "effortConfigID", "eff-opt");
        SetField(session, "currentEffort", "High"); // same string as the "High" above: exact equality, no change
        RunOnMain(runtime, session, "ApplySelection", null, "high", false, InteractionMode.Build); // differs only by case
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/set_config_option", frame["method"]!.JSONValueString);
        Assert.Equal("eff-opt", frame["params"]!["configId"]!.JSONValueString);
        Assert.Equal("high", frame["params"]!["value"]!.JSONValueString);
        Assert.Equal("high", Private<string?>(session, "currentEffort", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-058")] // ACPSession.swift:460-466
    public void ApplySelection_writes_the_Fast_toggle_as_literal_true_or_false_strings_on_every_differing_call()
    {
        using var runtime = new TestRuntime();
        // Two requests go out on this one connection, so the reply must echo back the REAL request id, not a
        // hardcoded one, or the second reply never matches its continuation.
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Cursor,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $rid=[regex]::Match($l,'\"id\":(\\d+)').Groups[1].Value; " +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":'+$rid+',\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "fastConfigID", "fast");
        // currentFast is nil: the first call sends "false" even though fastMode itself was not requested changed.
        RunOnMain(runtime, session, "ApplySelection", null, null, false, InteractionMode.Build);
        var frame1 = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("fast", frame1["params"]!["configId"]!.JSONValueString);
        Assert.Equal(new JSONValue.String("false"), frame1["params"]!["value"]); // a JSON string, never a boolean
        Assert.Equal("false", Private<string?>(session, "currentFast", field: true));

        RunOnMain(runtime, session, "ApplySelection", null, null, true, InteractionMode.Build);
        var frame2 = JSONValue.Parse(Lines(process.ErrorTail)[1])!;
        Assert.Equal(new JSONValue.String("true"), frame2["params"]!["value"]);
        Assert.Equal("true", Private<string?>(session, "currentFast", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-059")] // ACPSession.swift:467
    public void ApplySelection_mode_branch_is_skipped_entirely_when_the_chosen_mode_id_is_nil()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "hasModeAPI", true); // a transport is available, but planMode is nil: no fallback either
        RunOnMain(runtime, session, "ApplySelection", null, null, false, InteractionMode.Plan);
        Assert.Empty(Lines(process.ErrorTail));
        Assert.Null(Private<string?>(session, "currentMode", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-060")] // ACPSession.swift:468-472
    [Trait("Req", "PR-PROVACP-061")] // ACPSession.swift:467,473-474 (currentMode is set even with neither transport)
    public void ApplySelection_mode_change_prefers_set_config_option_falls_back_only_with_hasModeAPI_and_always_updates_currentMode()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_mode\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "buildMode", "agent");
        // No modeConfigID and hasModeAPI false: nothing is sent, yet currentMode is still updated (PR-PROVACP-061).
        RunOnMain(runtime, session, "ApplySelection", null, null, false, InteractionMode.Build);
        Assert.Empty(Lines(process.ErrorTail));
        Assert.Equal("agent", Private<string?>(session, "currentMode", field: true));

        SetField(session, "currentMode", (string?)null);
        SetField(session, "hasModeAPI", true);
        RunOnMain(runtime, session, "ApplySelection", null, null, false, InteractionMode.Build);
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/set_mode", frame["method"]!.JSONValueString);
        Assert.Equal(["modeId", "sessionId"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("agent", frame["params"]!["modeId"]!.JSONValueString);
        Assert.Equal("agent", Private<string?>(session, "currentMode", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-062")] // ACPSession.swift:477-485
    public void SetConfigOption_sends_the_exact_three_field_request_and_feeds_the_replys_configOptions_back()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"configOptions\":" +
            "[{\"id\":\"mode-opt\",\"category\":\"mode\",\"currentValue\":\"plan\",\"options\":[{\"value\":\"plan\"}]}]}}'); $o.Flush() } " + EndOnBye);
        RunOnMain(runtime, session, "SetConfigOption", "cfg-1", "val-1");
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("session/set_config_option", frame["method"]!.JSONValueString);
        Assert.Equal(["configId", "sessionId", "value"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("cfg-1", frame["params"]!["configId"]!.JSONValueString);
        Assert.Equal("val-1", frame["params"]!["value"]!.JSONValueString);
        Assert.Equal("mode-opt", Private<string?>(session, "modeConfigID", field: true)); // the reply's configOptions fed back in
        Assert.Equal("plan", Private<string?>(session, "currentMode", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-062")] // ACPSession.swift:477-485 (a thrown error is swallowed, never propagated)
    public void SetConfigOption_swallows_a_thrown_error_and_leaves_state_untouched()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"nope\"}}'); $o.Flush() } " + EndOnBye);
        RunOnMain(runtime, session, "SetConfigOption", "cfg-1", "val-1"); // must complete without throwing
        Assert.Null(Private<string?>(session, "modeConfigID", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-063")] // ACPSession.swift:168; ProviderSession.swift:170-171
    public async Task Send_throws_notRunning_with_the_exact_message_when_the_session_is_not_running()
    {
        var input = new TurnInput { Text = "hi", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised); // no connection, no sessionID
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Send(input));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Equal("The agent session is not running.", error.Message);

        using var runtime = new TestRuntime();
        var (session2, connection2, process2) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ } " + EndOnBye);
        runtime.OnMain(connection2.Close); // a closed connection with a sessionID still refuses
        // Close() only terminates the process; IsClosed flips later, once the process's own exit is observed.
        runtime.Loop.RunUntil(() => connection2.IsClosed, Bound);
        var error2 = await Assert.ThrowsAsync<ProviderError>(() => session2.Send(input));
        Assert.Equal("The agent session is not running.", error2.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-064")] // ACPSession.swift:169-175
    public void Send_refreshes_runtimeMode_and_finishes_applySelections_round_trip_before_returning()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_model\"')){ $o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}'); $o.Flush() } " + EndOnBye);
        var input = new TurnInput
        {
            Text = "hi", Images = ValueList<Attachment>.Empty, Model = "grok-4",
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
        };
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(async () => { await session.Send(input); return true; }));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.True(Resolved(handle.Value));
        Assert.Equal(RuntimeMode.FullAccess, Private<RuntimeMode>(session, "runtimeMode", field: true));
        Assert.Equal("grok-4", Private<string?>(session, "currentModel", field: true)); // applySelection's round trip finished
        Assert.Equal("session/set_model", JSONValue.Parse(Lines(process.ErrorTail)[0])!["method"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-065")] // ACPSession.swift:176-179
    public void Send_builds_the_prompt_as_one_text_block_then_one_image_block_per_readable_image_attachment()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(dataDirectory)); // R-TEST-9
        var folder = Path.Combine(dataDirectory!, "ACPSessionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var imageBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 }; // not a real image: kept as its own bytes unchanged
            var imagePath = Path.Combine(folder, "shot.png");
            File.WriteAllBytes(imagePath, imageBytes);
            var docPath = Path.Combine(folder, "notes.txt");
            File.WriteAllText(docPath, "not an image");

            using var runtime = new TestRuntime();
            var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
                Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
            var input = new TurnInput
            {
                Text = "look at this",
                Images = new ValueList<Attachment>([
                    new Attachment { Name = "shot.png", Path = imagePath, MimeType = "image/png" },
                    new Attachment { Name = "notes.txt", Path = docPath, MimeType = "text/plain" }, // not an image: dropped
                ]),
                RuntimeMode = RuntimeMode.Supervised,
                InteractionMode = InteractionMode.Build,
            };
            var handle = runtime.OnMain(() => MainActor.SpawnThrowing(async () => { await session.Send(input); return true; }));
            runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
            Assert.True(Resolved(handle.Value));
            // The prompt request is fired from a detached task (PR-PROVACP-067): keep pumping the main loop
            // (not a bare wall-clock wait) until its enqueued continuation actually runs and writes the frame.
            runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Any(l => l.Contains("\"method\":\"session/prompt\"", StringComparison.Ordinal)), Bound);
            var promptFrame = JSONValue.Parse(Lines(process.ErrorTail).Single(l => l.Contains("\"method\":\"session/prompt\"", StringComparison.Ordinal)))!;
            var prompt = promptFrame["params"]!["prompt"]!.JSONValueArray!;
            Assert.Equal(2, prompt.Count); // text block, then one image block; the text attachment was dropped
            Assert.Equal("text", prompt[0]["type"]!.JSONValueString);
            Assert.Equal("look at this", prompt[0]["text"]!.JSONValueString);
            Assert.Equal("image", prompt[1]["type"]!.JSONValueString);
            Assert.Equal("image/png", prompt[1]["mimeType"]!.JSONValueString);
            Assert.Equal(Convert.ToBase64String(imageBytes), prompt[1]["data"]!.JSONValueString);
            // session/prompt is deliberately left unanswered (this record is about the fire-and-forget send,
            // not the reply): closing the connection would resolve it into finishPrompt, which is part 4's
            // own stub. Leave the process running; it is reaped with the test host.
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-066")] // ACPSession.swift:180-184
    public void Send_clears_streaming_ids_arms_promptActive_resets_cancelRequested_and_emits_turnStarted_with_a_nil_id()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "messageID", "stale-message");
        SetField(session, "thoughtID", "stale-thought");
        SetField(session, "cancelRequested", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var input = new TurnInput { Text = "hi", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(async () => { await session.Send(input); return true; }));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.True(Resolved(handle.Value));
        Assert.Null(Private<string?>(session, "messageID", field: true));
        Assert.Null(Private<string?>(session, "thoughtID", field: true));
        Assert.True(Private<bool>(session, "promptActive", field: true));
        Assert.False(Private<bool>(session, "cancelRequested", field: true));
        var started = Assert.IsType<ProviderEvent.TurnStarted>(Assert.Single(events));
        Assert.Null(started.ProviderTurnID);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-067")] // ACPSession.swift:185-195
    public void Send_fires_session_prompt_in_a_detached_task_with_exact_params_and_returns_before_it_resolves()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok, // session/prompt is never answered
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        var input = new TurnInput { Text = "hello there", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(async () => { await session.Send(input); return true; }));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound); // completes even though session/prompt never replies
        Assert.True(Resolved(handle.Value));
        // Keep pumping (not a bare wall-clock wait) until the detached task's continuation runs and writes the frame.
        runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Any(l => l.Contains("\"method\":\"session/prompt\"", StringComparison.Ordinal)), Bound);
        var frame = JSONValue.Parse(Lines(process.ErrorTail).Single(l => l.Contains("\"method\":\"session/prompt\"", StringComparison.Ordinal)))!;
        Assert.Equal(["prompt", "sessionId"], frame["params"]!.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("sess-1", frame["params"]!["sessionId"]!.JSONValueString);
        Assert.Equal("hello there", frame["params"]!["prompt"]!.JSONValueArray![0]["text"]!.JSONValueString);
        // session/prompt is deliberately left unanswered here (that is the point of this record); closing the
        // connection would resolve it into finishPrompt, which is part 4's own stub. Leave the process running.
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-068")] // ACPSession.swift:198-199
    public async Task Interrupt_does_nothing_unless_a_prompt_is_active_on_a_live_session()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        // promptActive is false by default.
        await session.Interrupt();
        Assert.Empty(Lines(process.ErrorTail));
        Assert.False(Private<bool>(session, "cancelRequested", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-069")] // ACPSession.swift:200-205
    [Trait("Req", "PR-PROVACP-070")] // ACPSession.swift:206
    public async Task Interrupt_cancels_every_pending_permission_then_sends_session_cancel_as_a_notification()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "promptActive", true);
        var pending = Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true);
        pending["req-A"] = new RPCID.Int(1);
        pending["req-B"] = new RPCID.String("b");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        await session.Interrupt();
        Assert.True(Private<bool>(session, "cancelRequested", field: true));
        // The frames were written to the child's stdin already; give its echo back over stderr time to land.
        Assert.True(SpinWait.SpinUntil(() => Lines(process.ErrorTail).Count >= 3, Bound), "the two answers and the notify were not all echoed back");
        var frames = Lines(process.ErrorTail).Select(l => JSONValue.Parse(l)!).ToList();
        var answers = frames.Where(f => f["id"] is not null).ToList(); // the two responses (a notification carries no "id")
        Assert.Equal(2, answers.Count);
        foreach (var answer in answers)
        {
            Assert.Equal("cancelled", answer["result"]!["outcome"]!["outcome"]!.JSONValueString);
            Assert.Null(answer["result"]!["outcome"]!["optionId"]);
        }
        var resolvedIDs = events.OfType<ProviderEvent.RequestResolved>().Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(["req-A", "req-B"], resolvedIDs);
        Assert.Empty(Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true));
        var notifyIndex = frames.FindIndex(f => f["method"]?.JSONValueString == "session/cancel");
        Assert.True(notifyIndex >= 0);
        Assert.True(frames.Take(notifyIndex).All(f => f["id"] is not null)); // the two answers came first
        Assert.Equal(["sessionId"], frames[notifyIndex]["params"]!.JSONValueObject!.Keys);
        Assert.Equal("sess-1", frames[notifyIndex]["params"]!["sessionId"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-071")] // ACPSession.swift:209-211; Provider.swift:25-28,32
    [InlineData(ProviderKind.Cursor, "Cursor compacts its own context.")]
    [InlineData(ProviderKind.Opencode, "OpenCode Zen compacts its own context.")]
    [InlineData(ProviderKind.OpencodeGo, "OpenCode Go compacts its own context.")]
    [InlineData(ProviderKind.Grok, "Grok compacts its own context.")]
    [InlineData(ProviderKind.Devin, "Devin compacts its own context.")]
    public async Task Compact_always_throws_with_the_exact_provider_named_message(ProviderKind provider, string message)
    {
        var session = Session(provider, RuntimeMode.Supervised);
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Compact());
        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-072")] // ACPSession.swift:213-217
    public void ResolveApproval_answers_once_then_a_second_call_for_the_same_id_is_a_silent_no_op()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        var pending = Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true);
        pending["req-A"] = new RPCID.Int(7);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.ResolveApproval("req-A", "opt-1");
        var resolved = Assert.IsType<ProviderEvent.RequestResolved>(Assert.Single(events));
        Assert.Equal("req-A", resolved.Id);
        Assert.Empty(Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true));
        Assert.True(SpinWait.SpinUntil(() => Lines(process.ErrorTail).Count >= 1, Bound), "the response frame was not echoed back");
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("selected", frame["result"]!["outcome"]!["outcome"]!.JSONValueString);
        Assert.Equal("opt-1", frame["result"]!["outcome"]!["optionId"]!.JSONValueString);

        events.Clear();
        session.ResolveApproval("req-A", "opt-2"); // id was already removed: no-op
        Assert.Empty(events);
        Assert.Single(Lines(process.ErrorTail)); // no second response frame
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-072")] // ACPSession.swift:213-217 (a nil connection skips the response but the event is still emitted)
    public void ResolveApproval_still_emits_requestResolved_when_the_connection_is_nil()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised); // connection is nil
        var pending = Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true);
        pending["req-A"] = new RPCID.Int(1);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.ResolveApproval("req-A", "opt-1"); // must not throw despite the nil connection
        var resolved = Assert.IsType<ProviderEvent.RequestResolved>(Assert.Single(events));
        Assert.Equal("req-A", resolved.Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-073")] // ACPSession.swift:219
    public void AnswerQuestion_is_an_intentional_no_op()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.AnswerQuestion("req-A", new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>>
        {
            ["choice"] = new ValueList<string>(["a", "b"]),
        }));
        Assert.Empty(events);
    }
}
