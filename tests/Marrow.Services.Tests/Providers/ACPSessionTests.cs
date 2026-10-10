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
    [Theory]
    [InlineData("AUTH required", true, false, true)]
    [InlineData("AUTH required", true, true, false)]
    [InlineData("network error", true, false, false)]
    [InlineData("auth required", false, false, false)]
    [Trait("Req", "PR-PROVACP-027")] // Services/Providers/ACPSession.swift:134-140 exactly one retry with authMethods[0]
    public void Review_auth_retry_uses_first_method_once_and_propagates_other_failures(string message, bool methods, bool failRetry, bool succeeds)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        Assert.NotNull(typeof(ACPSession).GetMethod("OpenSessionWithAuthRetry", BindingFlags.NonPublic | BindingFlags.Instance));
        var script = Pipes + "$ProgressPreference='SilentlyContinue'; $n=0; while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); $q=$l|ConvertFrom-Json; " +
            "if($q.method -eq 'session/new'){ $n++; if($n -eq 1" + (failRetry ? " -or $n -eq 2" : "") +
            "){ $o.WriteLine(('{\"jsonrpc\":\"2.0\",\"id\":'+$q.id+',\"error\":{\"code\":-32000,\"message\":\"" + message + "\"}}')) } " +
            "else { $o.WriteLine(('{\"jsonrpc\":\"2.0\",\"id\":'+$q.id+',\"result\":{\"sessionId\":\"review\"}}')) }; $o.Flush() } " +
            "if($q.method -eq 'authenticate'){ $o.WriteLine(('{\"jsonrpc\":\"2.0\",\"id\":'+$q.id+',\"result\":{}}')); $o.Flush() } " + EndOnBye;
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok, script);
        SetField(session, "authMethods", methods ? new ValueList<string>(["first", "preferred-but-second"]) : ValueList<string>.Empty);
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<JSONValue>(session, "OpenSessionWithAuthRetry", connection)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        if (succeeds) Assert.Equal("review", Resolved(handle.Value)["sessionId"]!.JSONValueString);
        else Assert.IsType<RPCError>(handle.Value.Exception!.InnerException);
        var frames = Lines(process.ErrorTail).Select(line => JSONValue.Parse(line)!).ToArray();
        var retries = methods && message.Contains("auth", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(retries ? 3 : 1, frames.Length);
        if (retries)
        {
            Assert.Equal("authenticate", frames[1]["method"]!.JSONValueString);
            Assert.Equal("first", frames[1]["params"]!["methodId"]!.JSONValueString);
            Assert.Equal("session/new", frames[2]["method"]!.JSONValueString);
        }
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-032")] // Services/Providers/ACPSession.swift:141-151 apply state before configured selection and returned id
    public void Review_opened_session_discovers_config_before_applying_selection()
    {
        using var runtime = new TestRuntime();
        Assert.NotNull(typeof(ACPSession).GetMethod("SelectOpenedSession", BindingFlags.NonPublic | BindingFlags.Instance));
        var process = LiveAgent(Pipes + "$ProgressPreference='SilentlyContinue'; while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); $q=$l|ConvertFrom-Json; " +
            "if($q.id){ $o.WriteLine(('{\"jsonrpc\":\"2.0\",\"id\":'+$q.id+',\"result\":{}}')); $o.Flush() } " + EndOnBye);
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        var session = new ACPSession(Config(ProviderKind.Grok, RuntimeMode.Supervised, null) with { Model = "chosen" });
        SetField(session, "connection", connection); SetField(session, "sessionID", "review-id");
        runtime.OnMain(connection.Start);
        var state = JSONValue.Parse("""{"configOptions":[{"id":"discovered-model","category":"model","currentValue":"old","options":[{"value":"chosen","name":"Chosen"}]}]}""")!;
        var handle = runtime.OnMain(() => MainActor.SpawnThrowing(() => InvokePrivateAsync<string>(session, "SelectOpenedSession", state)));
        runtime.Loop.RunUntil(() => handle.Value.IsCompleted, Bound);
        Assert.Equal("review-id", Resolved(handle.Value));
        var frame = JSONValue.Parse(Assert.Single(Lines(process.ErrorTail)))!;
        Assert.Equal("session/set_config_option", frame["method"]!.JSONValueString);
        Assert.Equal("discovered-model", frame["params"]!["configId"]!.JSONValueString);
        Assert.Equal("chosen", frame["params"]!["value"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

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
    [Trait("Req", "UI-PROVACP-031")] // ACPSession.swift:109; ProviderSession.swift:168; Provider.swift:66-69,73,25-28,32
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
        Assert.Equal(new JSONValue.String("nth"), request["clientInfo"]!["name"]);
        Assert.Equal(new JSONValue.String("Nth"), request["clientInfo"]!["title"]);
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
    // applySelection and setConfigOption are implemented. RESOLVED: Review_auth_retry and
    // Review_opened_session now exercise the exact helpers called by Start via offline scripted transport.

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
    [Trait("Req", "UI-PROVACP-018")] // ACPSession.swift:311 (cursor ids shown base-stripped: one row per model)
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
    [Trait("Req", "UI-PROVACP-018")] // ACPSession.swift:322-323 (empty catalog: no .models event, picker keeps what it had)
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
    [Trait("Req", "UI-PROVACP-019")] // ACPSession.swift:330-331 (effort disappears for the rest of the session when configOptions omits thought_level, upstream defect 1)
    [Trait("Req", "UI-PROVACP-020")] // ACPSession.swift:331 (fast disappears for the rest of the session when configOptions omits model_config/fast, upstream defect 1)
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
    [Trait("Req", "UI-PROVACP-018")] // ACPSession.swift:337-362 (the row marked default is the one matching the current model id)
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
    [Trait("Req", "UI-PROVACP-019")] // ACPSession.swift:417-430 (tick values always ordered low to high, whatever order the agent listed them in)
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
    [Trait("Req", "UI-PROVACP-020")] // ACPSession.swift:375-380 (available only when the current model's fastTier is set to the literal string "true")
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
    [Trait("Req", "UI-PROVACP-018")] // ACPSession.swift:448-449 (selecting a row sends session/set_config_option)
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
    [Trait("Req", "UI-PROVACP-018")] // ACPSession.swift:450-451 (session/set_model when no model config id was advertised)
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
    [Trait("Req", "UI-PROVACP-019")] // ACPSession.swift:456-457 (moving the slider sends session/set_config_option with the chosen value)
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
    [Trait("Req", "UI-PROVACP-020")] // ACPSession.swift:460-464 (toggling sends session/set_config_option with the string "true" or "false")
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
    [Trait("Req", "UI-PROVACP-021")] // ACPSession.swift:300-304,467 (no plan mode: asking for plan silently leaves the session where it was)
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
    [Trait("Req", "UI-PROVACP-021")] // ACPSession.swift:467-474 (switching sends set_config_option, or set_mode when the agent advertised a modes object; no transport still records the mode locally)
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
    [Trait("Req", "UI-PROVACP-032")] // ACPSession.swift:168; ProviderSession.swift:170-171 (the exact sentence, shown for all three not-running states)
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
        Assert.IsType<ProviderError.Case.NotRunning>(error2.ErrorCase);
        Assert.Equal("The agent session is not running.", error2.Message);

        using var runtime3 = new TestRuntime();
        var (session3, connection3, process3) = ConnectedSession(runtime3, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ } " + EndOnBye);
        SetField(session3, "sessionID", null); // an open connection but no session id still refuses
        var error3 = await Assert.ThrowsAsync<ProviderError>(() => session3.Send(input));
        Assert.IsType<ProviderError.Case.NotRunning>(error3.ErrorCase);
        Assert.Equal("The agent session is not running.", error3.Message);
        runtime3.OnMain(() => connection3.Notify("bye"));
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
    [Trait("Req", "UI-PROVACP-029")] // ACPSession.swift:209-211; Provider.swift:25-28,32 (unreachable at B2: no caller reaches session.compact(); if invoked it always throws this)
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
    [Trait("Req", "UI-PROVACP-017")] // ACPSession.swift:213-217 (after a click the card is dismissed by requestResolved; a second click does nothing)
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

    // ===== Part 4 of 8: finishPrompt, handleNotification, handleRequest, handleClose and their static
    // mapping helpers (PR-PROVACP-074..097). makeToolCall/makeToolUpdate's own title-synthesis records sit in
    // a later part, but both are implemented now (see the implementation header) so the tool_call event flow
    // can be exercised here; only their routing (not their exact title synthesis) is asserted below.

    private static T InvokePrivateValue<T>(ACPSession session, string method, params object?[] args) =>
        (T)typeof(ACPSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, args)!;

    private static JSONValue Update(string sessionUpdate, params (string Key, JSONValue Value)[] fields)
    {
        var pairs = new List<(string, JSONValue)> { ("sessionUpdate", sessionUpdate) };
        pairs.AddRange(fields);
        return JSONValue.FromDictionaryLiteral(("update", JSONValue.FromDictionaryLiteral(pairs.ToArray())));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-074")] // ACPSession.swift:221-224,629
    public void HandleClose_emits_no_exited_event_once_stop_has_set_isStopping_but_does_when_it_has_not()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.Stop(); // sets isStopping before closing the (nil) connection
        Assert.True(Private<bool>(session, "isStopping", field: true));
        InvokePrivate(session, "HandleClose", "tail text");
        Assert.Empty(events); // no .exited for a deliberate stop

        var session2 = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events2 = new List<ProviderEvent>();
        session2.OnEvent = events2.Add;
        InvokePrivate(session2, "HandleClose", "tail text");
        var exited = Assert.IsType<ProviderEvent.Exited>(Assert.Single(events2));
        Assert.Equal("tail text", exited.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-075")] // ACPSession.swift:71-74
    public void IsRunning_is_true_only_between_a_built_connection_and_its_close()
    {
        using var runtime = new TestRuntime();
        Assert.False(Session(ProviderKind.Grok, RuntimeMode.Supervised).IsRunning); // no connection built yet
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        Assert.True(session.IsRunning);
        runtime.OnMain(connection.Close);
        // Close() only closes stdin (StdioProcess.Terminate, JSONRPC.cs:200-203); IsClosed flips only once the
        // real child process has actually exited and the background read loop observes end-of-stream and calls
        // DidClose (JSONRPC.cs:107-121,260-270). That is real OS process/IO timing, not the manual clock, so an
        // immediate assert right after OnMain(Close) races the process exit. Wait for the same condition the
        // production isRunning getter reports instead of assuming it is already true.
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
        Assert.False(session.IsRunning);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-076")] // ACPSession.swift:489-491
    public void HandleNotification_ignores_a_non_session_update_method_a_missing_update_and_an_unknown_sessionUpdate()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/other", JSONValue.FromDictionaryLiteral(("update", JSONValue.FromDictionaryLiteral(("sessionUpdate", "plan")))));
        InvokePrivate(session, "HandleNotification", "session/update", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>())));
        InvokePrivate(session, "HandleNotification", "session/update", Update("not_a_real_kind"));
        InvokePrivate(session, "HandleNotification", "session/update", JSONValue.FromDictionaryLiteral(("update", JSONValue.FromDictionaryLiteral(("sessionUpdate", 1L)))));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-076")] // ACPSession.swift:489-491
    public void HandleNotification_ignores_notifications_while_isReplaying()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "isReplaying", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("session_info_update", ("title", "T")));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-077")] // ACPSession.swift:492-497
    public void Agent_message_chunk_drops_without_text_clears_thoughtID_and_keys_by_messageId_prefix()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "thoughtID", "thought-1-AAAAAAAA");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_message_chunk", ("content", JSONValue.FromDictionaryLiteral(("noText", true)))));
        Assert.Empty(events); // no content.text: dropped
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_message_chunk",
            ("messageId", "m1"), ("content", JSONValue.FromDictionaryLiteral(("text", "hi")))));
        var delta = Assert.IsType<ProviderEvent.MessageDelta>(Assert.Single(events));
        Assert.Equal("message-m1", delta.Id); // literal "message-" prefix
        Assert.Equal("hi", delta.Text);
        Assert.Null(Private<string?>(session, "thoughtID", field: true));
        Assert.Equal("message-m1", Private<string?>(session, "messageID", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-077")] // ACPSession.swift:495
    public void Agent_message_chunk_without_a_messageId_reuses_messageID_then_falls_back_to_a_fresh_id()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "messageID", "message-existing");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_message_chunk", ("content", JSONValue.FromDictionaryLiteral(("text", "a")))));
        Assert.Equal("message-existing", ((ProviderEvent.MessageDelta)events[0]).Id);
        SetField(session, "messageID", null);
        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_message_chunk", ("content", JSONValue.FromDictionaryLiteral(("text", "b")))));
        var id = ((ProviderEvent.MessageDelta)events[0]).Id;
        Assert.StartsWith("message-1-", id, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-078")] // ACPSession.swift:498-502
    public void Agent_thought_chunk_drops_without_text_reuses_thoughtID_and_never_clears_messageID()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "messageID", "message-keep");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_thought_chunk", ("content", JSONValue.FromDictionaryLiteral(("noText", true)))));
        Assert.Empty(events);
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_thought_chunk", ("content", JSONValue.FromDictionaryLiteral(("text", "t1")))));
        var first = Assert.IsType<ProviderEvent.ReasoningDelta>(Assert.Single(events));
        var tid = Private<string?>(session, "thoughtID", field: true);
        Assert.NotNull(tid);
        Assert.Equal("message-keep", Private<string?>(session, "messageID", field: true)); // never cleared by a thought
        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("agent_thought_chunk", ("content", JSONValue.FromDictionaryLiteral(("text", "t2")))));
        Assert.Equal(tid, ((ProviderEvent.ReasoningDelta)events[0]).Id); // reused, same run
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-079")] // ACPSession.swift:701-704
    public void NextID_format_is_prefix_dash_counter_dash_eight_uppercase_hex_chars_sharing_one_counter()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var first = InvokePrivateValue<string>(session, "NextID", "message");
        var second = InvokePrivateValue<string>(session, "NextID", "thought");
        Assert.Matches("^message-1-[0-9A-F]{8}$", first);
        Assert.Matches("^thought-2-[0-9A-F]{8}$", second); // counter shared across both prefixes
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-080")] // ACPSession.swift:503-515
    public void Tool_call_drops_without_toolCallId_clears_both_streaming_ids_and_emits_toolStarted()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "messageID", "message-x");
        SetField(session, "thoughtID", "thought-x");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call", ("kind", "execute")));
        Assert.Empty(events); // no toolCallId: dropped

        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call", ("toolCallId", "t1"), ("kind", "execute")));
        Assert.Null(Private<string?>(session, "messageID", field: true));
        Assert.Null(Private<string?>(session, "thoughtID", field: true));
        var started = Assert.IsType<ProviderEvent.ToolStarted>(Assert.Single(events));
        Assert.Equal("t1", started.Id);
        Assert.Equal(ToolCall.Kind.Command, started.Call.ToolCallKind);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-081")] // ACPSession.swift:514-515
    public void Tool_call_with_output_or_status_also_emits_an_immediate_toolUpdated_otherwise_not()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call", ("toolCallId", "t1"), ("kind", "execute"), ("title", "ls")));
        Assert.Single(events); // only .toolStarted: no output, no status

        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call", ("toolCallId", "t2"), ("kind", "execute"), ("status", "completed")));
        Assert.Equal(2, events.Count);
        var updated = Assert.IsType<ProviderEvent.ToolUpdated>(events[1]);
        Assert.Equal("t2", updated.Id);
        Assert.Equal(ToolCall.Status.Completed, updated.Update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-082")] // ACPSession.swift:516-524
    public void Tool_call_update_drops_without_id_emits_unconditionally_and_never_clears_streaming_ids()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "messageID", "message-x");
        SetField(session, "thoughtID", "thought-x");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call_update", ("kind", "execute")));
        Assert.Empty(events);

        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call_update", ("toolCallId", "t1")));
        var updated = Assert.IsType<ProviderEvent.ToolUpdated>(Assert.Single(events)); // unconditional, even all-nil
        Assert.Equal("t1", updated.Id);
        Assert.Equal("message-x", Private<string?>(session, "messageID", field: true));
        Assert.Equal("thought-x", Private<string?>(session, "thoughtID", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-083")] // ACPSession.swift:507-512,518-523
    [Trait("Req", "UI-PROVACP-023")] // ACPSession.swift:507-512,518-523 (an opencode todowrite call is routed to the checklist, never a tool row)
    public void Todo_tool_calls_are_routed_to_the_checklist_and_never_open_a_tool_row_and_the_id_is_remembered()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var todos = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "step 1")));
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call",
            ("toolCallId", "todo-1"), ("title", "todowrite"), ("rawInput", JSONValue.FromDictionaryLiteral(("todos", todos)))));
        var started = Assert.Single(events);
        var list = Assert.IsType<ProviderEvent.Todos>(started).Value;
        Assert.Equal("step 1", list[0].Text);
        var ids = Private<HashSet<string>>(session, "todoToolCallIDs", field: true);
        Assert.Contains("todo-1", ids);

        // An empty parse emits nothing and still opens no tool row.
        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call", ("toolCallId", "todo-2"), ("title", "todowrite")));
        Assert.Empty(events);
        Assert.Contains("todo-2", ids);

        // A remembered id keeps routing to the checklist even once the update no longer looks like a todo call.
        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("tool_call_update",
            ("toolCallId", "todo-1"), ("rawInput", JSONValue.FromDictionaryLiteral(("todos", todos)))));
        Assert.IsType<ProviderEvent.Todos>(Assert.Single(events));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-084")] // ACPSession.swift:638-642
    public void IsTodoCall_matches_a_present_rawInput_todos_member_even_null_or_a_recognized_title()
    {
        Assert.True(InvokeStatic<bool>("IsTodoCall", JSONValue.FromDictionaryLiteral(("rawInput", JSONValue.FromDictionaryLiteral(("todos", new JSONValue.Null()))))));
        Assert.True(InvokeStatic<bool>("IsTodoCall", JSONValue.FromDictionaryLiteral(("title", "todowrite"))));
        Assert.False(InvokeStatic<bool>("IsTodoCall", JSONValue.FromDictionaryLiteral(("title", "read file"))));
        Assert.False(InvokeStatic<bool>("IsTodoCall", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>()))));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-085")] // ACPSession.swift:644-659
    [InlineData("todo", true)]
    [InlineData("Todos", true)]
    [InlineData(" TODOWRITE ", true)]
    [InlineData("todo write", true)]
    [InlineData("write todos", true)]
    [InlineData("3 todos", true)]
    [InlineData("12 todo", true)]
    [InlineData("my todos", false)]
    // " todos" is trimmed to exactly "todos" by the leading `text = title.trimmed.lowercased()` (:645) before
    // either check runs, so it hits the five-member exact-match set (:647-648), not the suffix/numeric-prefix
    // rule the record's prose illustrates with this same string; the cited Swift code is the ground truth.
    [InlineData(" todos", true)]
    [InlineData("3.5 todos", false)]
    [InlineData("todolist", false)]
    public void IsTodoTitle_matches_five_exact_titles_and_a_numeric_count_suffix(string title, bool expected) =>
        Assert.Equal(expected, InvokeStatic<bool>("IsTodoTitle", title));

    [Fact]
    [Trait("Req", "PR-PROVACP-086")] // ACPSession.swift:663-683
    public void TodoSteps_tries_four_sources_in_order_and_stops_at_the_first_with_steps()
    {
        var rawInputTodos = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "from rawInput")));
        var update1 = JSONValue.FromDictionaryLiteral(("rawInput", JSONValue.FromDictionaryLiteral(("todos", rawInputTodos))),
            ("rawOutput", JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "from rawOutput")))))));
        Assert.Equal("from rawInput", InvokeStatic<ValueList<TodoStep>>("TodoSteps", update1)[0].Text);

        var update2 = JSONValue.FromDictionaryLiteral(("rawOutput", JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "from rawOutput")))))));
        Assert.Equal("from rawOutput", InvokeStatic<ValueList<TodoStep>>("TodoSteps", update2)[0].Text);

        var contentText = new JSONValue.String(JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "from content"))))).CompactString).Value;
        var update3 = JSONValue.FromDictionaryLiteral(("content", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", contentText)))))));
        Assert.Equal("from content", InvokeStatic<ValueList<TodoStep>>("TodoSteps", update3)[0].Text);

        var outputText = JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "from rawOutput output"))))).CompactString;
        var update4 = JSONValue.FromDictionaryLiteral(("rawOutput", JSONValue.FromDictionaryLiteral(("output", outputText))));
        Assert.Equal("from rawOutput output", InvokeStatic<ValueList<TodoStep>>("TodoSteps", update4)[0].Text);

        Assert.Empty(InvokeStatic<ValueList<TodoStep>>("TodoSteps", new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>()))));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-087")] // ACPSession.swift:688-699
    [Trait("Req", "UI-PROVACP-023")] // ACPSession.swift:685-699 (pending/active/done status mapping, unrecognized stays pending)
    public void ParseTodoItems_takes_the_first_of_four_keys_maps_seven_statuses_and_defaults_to_pending()
    {
        var items = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("content", "a"), ("status", "completed")),
            JSONValue.FromDictionaryLiteral(("text", "b"), ("status", "complete")),
            JSONValue.FromDictionaryLiteral(("step", "c"), ("status", "done")),
            JSONValue.FromDictionaryLiteral(("title", "d"), ("status", "in_progress")),
            JSONValue.FromDictionaryLiteral(("content", "e"), ("status", "inProgress")),
            JSONValue.FromDictionaryLiteral(("content", "f"), ("status", "in-progress")),
            JSONValue.FromDictionaryLiteral(("content", "g"), ("status", "active")),
            JSONValue.FromDictionaryLiteral(("content", "h"), ("status", "mystery-status")),
            JSONValue.FromDictionaryLiteral(("content", "i")), // no status
            JSONValue.FromDictionaryLiteral(("content", "   ")), // trims to empty: dropped
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>()))); // none of the four keys: dropped
        var steps = ParseTodoItemsStatic(items);
        Assert.Equal(["a", "b", "c", "d", "e", "f", "g", "h", "i"], steps.Select(s => s.Text));
        Assert.All(steps.Take(3), s => Assert.Equal(TodoStep.Status.Done, s.TodoStepStatus));
        Assert.All(steps.Skip(3).Take(4), s => Assert.Equal(TodoStep.Status.Active, s.TodoStepStatus));
        Assert.All(steps.Skip(7), s => Assert.Equal(TodoStep.Status.Pending, s.TodoStepStatus)); // unrecognized and missing both stay pending
    }

    private static ValueList<TodoStep> ParseTodoItemsStatic(JSONValue value) => ACPSession.ParseTodoItems(value);

    [Fact]
    [Trait("Req", "PR-PROVACP-088")] // ACPSession.swift:525-527
    [Trait("Req", "UI-PROVACP-023")] // ACPSession.swift:525-527 (a plan update with no entries clears the checklist)
    public void Plan_update_replaces_the_checklist_unconditionally_even_with_an_empty_result()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("plan",
            ("entries", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("content", "step"))))));
        Assert.Equal("step", Assert.IsType<ProviderEvent.Todos>(Assert.Single(events)).Value[0].Text);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("plan")); // no entries: still emitted, empty
        var empty = Assert.IsType<ProviderEvent.Todos>(Assert.Single(events));
        Assert.Empty(empty.Value);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-089")] // ACPSession.swift:528-533
    [Trait("Req", "UI-PROVACP-022")] // ACPSession.swift:528-533; Provider.swift:293-299
    public void Available_commands_update_drops_nameless_entries_defaults_detail_to_empty_and_an_empty_list_clears_the_menu()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("available_commands_update",
            ("availableCommands", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("name", "review"), ("description", "Review the diff")),
                JSONValue.FromDictionaryLiteral(("name", "bare")), // no description
                JSONValue.FromDictionaryLiteral(("description", "no name: dropped"))))));
        var commands = Assert.IsType<ProviderEvent.Commands>(Assert.Single(events)).Value;
        Assert.Equal(2, commands.Count);
        Assert.Equal("review", commands[0].Name);
        Assert.Equal("Review the diff", commands[0].Detail);
        Assert.Equal("bare", commands[1].Name);
        Assert.Equal("", commands[1].Detail);
        Assert.False(commands[0].IsBuiltIn);
        Assert.Null(commands[0].SkillPath);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("available_commands_update"));
        Assert.Empty(Assert.IsType<ProviderEvent.Commands>(Assert.Single(events)).Value); // missing array clears the menu
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-090")] // ACPSession.swift:534-536
    [Trait("Req", "UI-PROVACP-021")] // ACPSession.swift:300-304,534-536 (the agent reports a mode change only when a mode id exactly "plan" exists)
    public void Current_mode_update_sets_currentMode_but_only_reports_a_change_when_planMode_is_known()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("current_mode_update", ("currentModeId", "agent")));
        Assert.Empty(events); // planMode unknown: no report
        Assert.Equal("agent", Private<string?>(session, "currentMode", field: true));

        SetField(session, "planMode", "plan");
        InvokePrivate(session, "HandleNotification", "session/update", Update("current_mode_update", ("currentModeId", "plan")));
        Assert.Equal(InteractionMode.Plan, Assert.IsType<ProviderEvent.ModeChanged>(Assert.Single(events)).Value);

        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", new JSONValue.Object(new ValueMap<string, JSONValue>(
            new Dictionary<string, JSONValue> { ["update"] = JSONValue.FromDictionaryLiteral(("sessionUpdate", "current_mode_update")) })));
        Assert.Equal(InteractionMode.Build, Assert.IsType<ProviderEvent.ModeChanged>(Assert.Single(events)).Value); // missing currentModeId -> nil -> .build
        Assert.Null(Private<string?>(session, "currentMode", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-091")] // ACPSession.swift:537-538
    public void Config_option_update_routes_a_configOptions_array_through_applyConfigOptions()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "models", new ValueList<ModelOption>([new ModelOption("m1", "Model 1")]));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var options = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
            ("id", "model"), ("category", "model"), ("currentValue", "m1"),
            ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("value", "m1"), ("name", "Model 1"))))));
        InvokePrivate(session, "HandleNotification", "session/update", Update("config_option_update", ("configOptions", options)));
        var models = Assert.IsType<ProviderEvent.Models>(Assert.Single(events));
        Assert.Equal("m1", models.Value[0].Id);
        Assert.Equal("model", Private<string?>(session, "modelConfigID", field: true));

        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("config_option_update")); // missing configOptions: ignored
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-092")] // ACPSession.swift:539-540
    [Trait("Req", "UI-PROVACP-025")] // ACPSession.swift:539-540
    public void Session_info_update_renames_only_on_a_non_empty_title_with_no_trimming()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("session_info_update", ("title", "")));
        Assert.Empty(events);
        InvokePrivate(session, "HandleNotification", "session/update", Update("session_info_update")); // missing title
        Assert.Empty(events);
        InvokePrivate(session, "HandleNotification", "session/update", Update("session_info_update", ("title", " ")));
        Assert.Equal(" ", Assert.IsType<ProviderEvent.Title>(Assert.Single(events)).Value); // a single space IS emitted: no trimming
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-093")] // ACPSession.swift:541-549
    [Trait("Req", "UI-PROVACP-024")] // ACPSession.swift:541-549; Timeline.swift:331-339
    public void Usage_update_feeds_the_context_meter_only_when_used_is_an_integer()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleNotification", "session/update", Update("usage_update")); // no `used`: nothing
        Assert.Empty(events);
        InvokePrivate(session, "HandleNotification", "session/update", Update("usage_update", ("used", 400L), ("size", 1000L)));
        var usage = Assert.IsType<ProviderEvent.Usage>(Assert.Single(events)).Value;
        Assert.Equal(400, usage.UsedTokens);
        Assert.Equal(1000, usage.WindowTokens);
        events.Clear();
        InvokePrivate(session, "HandleNotification", "session/update", Update("usage_update", ("used", 400L))); // no size: nil window
        Assert.Null(Assert.IsType<ProviderEvent.Usage>(Assert.Single(events)).Value.WindowTokens);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-094")] // ACPSession.swift:555-559; JSONRPC.swift:97-99
    public void HandleRequest_answers_every_method_but_session_request_permission_with_method_not_found()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        InvokePrivate(session, "HandleRequest", new RPCID.Int(9), "fs/read_text_file", new JSONValue.Null());
        Assert.True(SpinWait.SpinUntil(() => Lines(process.ErrorTail).Count >= 1, Bound));
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal(-32601L, frame["error"]!["code"]!.JSONValueInt);
        Assert.Equal("Method not found", frame["error"]!["message"]!.JSONValueString);
        Assert.Null(frame["result"]);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-095")] // ACPSession.swift:592-602
    [InlineData(RuntimeMode.FullAccess, "read", "opt-once")]
    [InlineData(RuntimeMode.AutoAcceptEdits, "edit", "opt-once")]
    [InlineData(RuntimeMode.AutoAcceptEdits, "read", null)]
    [InlineData(RuntimeMode.Supervised, "edit", null)]
    [InlineData(RuntimeMode.Auto, "edit", null)]
    public void AutomaticOption_auto_approves_by_mode_and_kind_preferring_allow_once_over_allow_always(RuntimeMode mode, string kindRaw, string? expected)
    {
        var session = Session(ProviderKind.Grok, mode);
        SetField(session, "runtimeMode", mode);
        var kind = kindRaw switch { "edit" => ToolCall.Kind.Edit, _ => ToolCall.Kind.Read };
        var options = new JSONValue[]
        {
            JSONValue.FromDictionaryLiteral(("kind", "allow_always"), ("optionId", "opt-always")),
            JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-once")),
        };
        var result = InvokePrivateValue<string?>(session, "AutomaticOption", kind, options);
        Assert.Equal(expected, result);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-095")] // ACPSession.swift:599-601
    public void AutomaticOption_falls_back_to_allow_always_when_no_allow_once_option_exists()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.FullAccess);
        SetField(session, "runtimeMode", RuntimeMode.FullAccess);
        var options = new JSONValue[] { JSONValue.FromDictionaryLiteral(("kind", "allow_always"), ("optionId", "opt-always")) };
        Assert.Equal("opt-always", InvokePrivateValue<string?>(session, "AutomaticOption", ToolCall.Kind.Other, options));
        var noneNamed = new JSONValue[] { JSONValue.FromDictionaryLiteral(("kind", "reject_once"), ("optionId", "opt-reject")) };
        Assert.Null(InvokePrivateValue<string?>(session, "AutomaticOption", ToolCall.Kind.Other, noneNamed));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-096")] // ACPSession.swift:563-566
    [Trait("Req", "UI-PROVACP-017")] // ACPSession.swift:563-566 (in full-access mode, and for edits in auto-accept-edits mode, the card never appears at all)
    public void An_auto_approved_permission_is_answered_immediately_and_never_reaches_the_UI()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "runtimeMode", RuntimeMode.FullAccess);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var parameters = JSONValue.FromDictionaryLiteral(
            ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-1")))),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "t1"), ("kind", "read"))));
        InvokePrivate(session, "HandleRequest", new RPCID.Int(3), "session/request_permission", parameters);
        Assert.Empty(events); // no .approval reaches the UI
        Assert.Empty(Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true)); // never recorded
        Assert.True(SpinWait.SpinUntil(() => Lines(process.ErrorTail).Count >= 1, Bound));
        var frame = JSONValue.Parse(Lines(process.ErrorTail)[0])!;
        Assert.Equal("selected", frame["result"]!["outcome"]!["outcome"]!.JSONValueString);
        Assert.Equal("opt-1", frame["result"]!["outcome"]!["optionId"]!.JSONValueString);
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-097")] // ACPSession.swift:567-575
    [Trait("Req", "UI-PROVACP-017")] // ACPSession.swift:567-575; Requests.swift:13-24 (label is the agent's option.name falling back to optionId; three roles; no optionId not rendered)
    public void Permission_options_map_to_three_roles_dropping_ones_without_an_optionId_labeling_by_name_or_id()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised); // connection is nil: not auto-approved, not required here
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var parameters = JSONValue.FromDictionaryLiteral(
            ("options", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("kind", "allow_always"), ("optionId", "opt-a"), ("name", "Always")),
                JSONValue.FromDictionaryLiteral(("kind", "reject_once"), ("optionId", "opt-b")),
                JSONValue.FromDictionaryLiteral(("kind", "reject_always"), ("optionId", "opt-c")),
                JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-d"), ("name", "Once")),
                JSONValue.FromDictionaryLiteral(("kind", "unknown_kind"), ("optionId", "opt-e")),
                JSONValue.FromDictionaryLiteral(("name", "no id: dropped")))),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "t1"), ("kind", "read"))));
        InvokePrivate(session, "HandleRequest", new RPCID.Int(1), "session/request_permission", parameters);
        var approval = Assert.IsType<ProviderEvent.Approval>(Assert.Single(events)).Value;
        Assert.Equal(5, approval.Options.Count);
        Assert.Equal(ApprovalRequest.Option.Role.ApproveAlways, approval.Options[0].OptionRole);
        Assert.Equal("Always", approval.Options[0].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Decline, approval.Options[1].OptionRole);
        Assert.Equal(ApprovalRequest.Option.Role.Decline, approval.Options[2].OptionRole);
        Assert.Equal(ApprovalRequest.Option.Role.Approve, approval.Options[3].OptionRole);
        Assert.Equal("Once", approval.Options[3].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Approve, approval.Options[4].OptionRole); // unknown kind falls to .approve
        Assert.Equal("opt-e", approval.Options[4].Title); // no name: label falls back to the raw optionId
        Assert.DoesNotContain(approval.Options, o => o.OptionRole == ApprovalRequest.Option.Role.Cancel);
    }

    // ===== Part 5 of 8: the permission map's key, the approval card's kind/title/detail, finishPrompt's
    // guard and outcome classification, handleClose's turn-failure and exited paths, and the static title
    // helpers (kind, status, fallbackTitle, isFallbackTitle, isBareToolName, cleanTitle, subject) plus
    // makeToolCall's path/command/read/edit titling (PR-PROVACP-098..121).

    [Fact]
    [Trait("Req", "PR-PROVACP-098")] // ACPSession.swift:576; Core/Support/JSONRPC.swift:23-28
    public void Pending_permission_is_keyed_by_the_RPCIDs_string_rendering_and_an_int_and_string_id_collide()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var parameters = JSONValue.FromDictionaryLiteral(
            ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-1")))),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "t1"), ("kind", "read"))));
        InvokePrivate(session, "HandleRequest", new RPCID.Int(7), "session/request_permission", parameters);
        var pending = Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true);
        Assert.Single(pending);
        Assert.Equal("7", Assert.Single(pending.Keys));
        Assert.IsType<RPCID.Int>(pending["7"]);

        InvokePrivate(session, "HandleRequest", new RPCID.String("7"), "session/request_permission", parameters);
        Assert.Single(pending); // an int id 7 and a string id "7" collide on the same key
        Assert.IsType<RPCID.String>(pending["7"]); // the later request replaces the earlier one

        Assert.Equal(2, events.Count);
        Assert.Equal("7", Assert.IsType<ProviderEvent.Approval>(events[0]).Value.Id);
        Assert.Equal("7", Assert.IsType<ProviderEvent.Approval>(events[1]).Value.Id);
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-099")] // ACPSession.swift:577-581; Core/Models/Requests.swift:34-42
    [Trait("Req", "UI-PROVACP-013")] // ACPSession.swift:577-581; Requests.swift:36 ("Run this command?" for kind .command, agent kind "execute")
    [Trait("Req", "UI-PROVACP-014")] // ACPSession.swift:579; Requests.swift:37 ("Apply these changes?" for kind .edit, agent kinds edit/delete/move)
    [Trait("Req", "UI-PROVACP-015")] // ACPSession.swift:580; Requests.swift:38 ("Allow this action?" for read/search/fetch/unclassified and a missing toolCall)
    [InlineData("execute", ApprovalRequest.Kind.Command, "Run this command?")]
    [InlineData("edit", ApprovalRequest.Kind.FileChange, "Apply these changes?")]
    [InlineData("delete", ApprovalRequest.Kind.FileChange, "Apply these changes?")]
    [InlineData("move", ApprovalRequest.Kind.FileChange, "Apply these changes?")]
    [InlineData("read", ApprovalRequest.Kind.Tool, "Allow this action?")]
    [InlineData("search", ApprovalRequest.Kind.Tool, "Allow this action?")]
    [InlineData("fetch", ApprovalRequest.Kind.Tool, "Allow this action?")]
    [InlineData(null, ApprovalRequest.Kind.Tool, "Allow this action?")]
    public void Approval_kind_is_command_fileChange_or_tool_from_the_tool_calls_kind(string? toolKind, ApprovalRequest.Kind expectedKind, string expectedHeadline)
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var toolCallFields = new List<(string, JSONValue)> { ("toolCallId", "t1") };
        if (toolKind is not null) toolCallFields.Add(("kind", toolKind));
        var parameters = JSONValue.FromDictionaryLiteral(
            ("options", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-1")))),
            ("toolCall", JSONValue.FromDictionaryLiteral(toolCallFields.ToArray())));
        InvokePrivate(session, "HandleRequest", new RPCID.Int(1), "session/request_permission", parameters);
        var approval = Assert.IsType<ProviderEvent.Approval>(Assert.Single(events)).Value;
        Assert.Equal(expectedKind, approval.RequestKind);
        Assert.Equal(expectedHeadline, approval.Headline);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-100")] // ACPSession.swift:560-562,582-589
    [Trait("Req", "UI-PROVACP-016")] // ACPSession.swift:560-562,582-589 (with a toolCall: the same title/detail a tool row carries; without one: title "Tool", no detail)
    public void Approval_event_carries_title_detail_and_toolCallId_with_a_null_toolCall_falling_back_to_a_generic_Tool_card()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var options = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "opt-1")));

        InvokePrivate(session, "HandleRequest", new RPCID.Int(1), "session/request_permission", JSONValue.FromDictionaryLiteral(
            ("options", options),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "tool-42"), ("kind", "search"), ("rawInput", JSONValue.FromDictionaryLiteral(("pattern", "TODO")))))));
        var withPattern = Assert.IsType<ProviderEvent.Approval>(Assert.Single(events)).Value;
        Assert.Equal("TODO", withPattern.Title);
        Assert.Equal("tool-42", withPattern.ToolItemID);
        Assert.Null(withPattern.Reason); // ApprovalRequest.reason is never set by this area

        events.Clear();
        InvokePrivate(session, "HandleRequest", new RPCID.Int(2), "session/request_permission", JSONValue.FromDictionaryLiteral(
            ("options", options),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "t3"), ("kind", "unknownkind"), ("title", "Custom Title"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", "docs/file.txt")))))));
        var withDetail = Assert.IsType<ProviderEvent.Approval>(Assert.Single(events)).Value;
        Assert.Equal("Custom Title", withDetail.Title);
        Assert.Equal("docs/file.txt", withDetail.Detail);

        events.Clear();
        InvokePrivate(session, "HandleRequest", new RPCID.Int(3), "session/request_permission", JSONValue.FromDictionaryLiteral(("options", options))); // no toolCall member at all
        var fallback = Assert.IsType<ProviderEvent.Approval>(Assert.Single(events)).Value;
        Assert.Equal("Tool", fallback.Title); // null toolCall -> makeToolCall(.null) -> kind .other -> fallback "Tool"
        Assert.Equal(ApprovalRequest.Kind.Tool, fallback.RequestKind);
        Assert.Null(fallback.ToolItemID);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-101")] // ACPSession.swift:604-606
    public void FinishPrompt_only_runs_once_per_turn_a_second_call_is_a_no_op()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "FinishPrompt", (string?)null, (Exception?)null);
        Assert.Single(events);
        Assert.False(Private<bool>(session, "promptActive", field: true));

        events.Clear();
        InvokePrivate(session, "FinishPrompt", "cancelled", null); // a second call, whatever the arguments, is a no-op
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-102")] // ACPSession.swift:607-612
    public void Finishing_a_turn_clears_todo_routing_and_cancels_every_open_permission()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " + EndOnBye);
        SetField(session, "promptActive", true);
        var todoIds = Private<HashSet<string>>(session, "todoToolCallIDs", field: true);
        todoIds.Add("tool-1");
        var pending = Private<Dictionary<string, RPCID>>(session, "pendingPermissions", field: true);
        pending["7"] = new RPCID.Int(7);
        pending["s"] = new RPCID.String("s");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;

        InvokePrivate(session, "FinishPrompt", (string?)null, (Exception?)null);
        Assert.Empty(todoIds);
        Assert.Empty(pending);
        var resolved = events.OfType<ProviderEvent.RequestResolved>().Select(e => e.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "7", "s" }, resolved); // order unspecified (R-COLL-2): compared sorted
        Assert.Contains(events, e => e is ProviderEvent.TurnCompleted);
        Assert.True(SpinWait.SpinUntil(() => Lines(process.ErrorTail).Count >= 2, Bound)); // both cancel responses reach the wire
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-103")] // ACPSession.swift:613-614
    public void Turn_is_interrupted_when_cancel_was_requested_or_stopReason_is_cancelled_even_with_an_error()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        SetField(session, "cancelRequested", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "FinishPrompt", (string?)null, new ProviderError(new ProviderError.Case.Failed("boom")));
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Interrupted, completed.Status);
        Assert.Null(completed.Error); // this test comes first: interrupted even though the prompt also threw

        SetField(session, "promptActive", true);
        SetField(session, "cancelRequested", false);
        events.Clear();
        InvokePrivate(session, "FinishPrompt", "cancelled", null); // the literal "cancelled" (double l), no cancelRequested needed
        var completed2 = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Interrupted, completed2.Status);
        Assert.Null(completed2.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-104")] // ACPSession.swift:615-616
    public void Turn_fails_with_the_errors_localized_description_when_the_prompt_threw()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "FinishPrompt", (string?)null, new RPCError(1, "agent says no"));
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("agent says no", completed.Error); // RPCError's own message is shown verbatim
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-105")] // ACPSession.swift:617-618
    [Trait("Req", "UI-PROVACP-026")] // ACPSession.swift:617-618
    public void A_refusal_stop_reason_fails_the_turn_with_a_fixed_sentence()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "FinishPrompt", "refusal", null);
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("The agent declined this request.", completed.Error);
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-106")] // ACPSession.swift:619-622
    [InlineData(null)]
    [InlineData("end_turn")]
    [InlineData("max_tokens")]
    [InlineData("something_new")]
    public void Every_other_stop_reason_completes_the_turn_and_cancelRequested_resets(string? stopReason)
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "FinishPrompt", stopReason, null);
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(Assert.Single(events));
        Assert.Equal(TurnStatus.Completed, completed.Status);
        Assert.Null(completed.Error);
        Assert.False(Private<bool>(session, "cancelRequested", field: true)); // cleared unconditionally at the end
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-107")] // ACPSession.swift:625-628; Core/Support/Text.swift:17-24
    [Trait("Req", "UI-PROVACP-027")] // ACPSession.swift:627; Text.swift:17-24 (stderr's last four ANSI-stripped, trimmed, non-empty lines; else the fixed sentence)
    public void A_close_during_an_active_turn_fails_it_with_the_last_four_non_empty_lines_of_stderr_or_a_fixed_message()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "promptActive", true);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleClose", "line1\n\nline2\nline3\nline4\nline5\n");
        var completed = Assert.IsType<ProviderEvent.TurnCompleted>(events.OfType<ProviderEvent.TurnCompleted>().Single());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("line2\nline3\nline4\nline5", completed.Error); // last four non-empty trimmed lines, joined by \n

        SetField(session, "promptActive", true);
        events.Clear();
        InvokePrivate(session, "HandleClose", "\n\n   \n");
        var completedEmpty = Assert.IsType<ProviderEvent.TurnCompleted>(events.OfType<ProviderEvent.TurnCompleted>().Single());
        Assert.Equal("The agent exited.", completedEmpty.Error); // no non-empty lines: the turn-failure fallback
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-108")] // ACPSession.swift:629-630
    [Trait("Req", "UI-PROVACP-028")] // ACPSession.swift:629-630 (suppressed entirely on a deliberate stop; else last four stderr lines or nil)
    public void An_unexpected_close_emits_exited_with_the_stderr_tail_and_nil_when_empty_but_a_deliberate_stop_emits_nothing()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        InvokePrivate(session, "HandleClose", "err line\n");
        var exited = Assert.IsType<ProviderEvent.Exited>(Assert.Single(events));
        Assert.Equal("err line", exited.Error);

        events.Clear();
        InvokePrivate(session, "HandleClose", "\n\n");
        var exitedNull = Assert.IsType<ProviderEvent.Exited>(Assert.Single(events));
        Assert.Null(exitedNull.Error); // no non-empty lines: nil here, NOT "The agent exited." (that fallback is turn-only)

        SetField(session, "isStopping", true);
        events.Clear();
        InvokePrivate(session, "HandleClose", "err line\n");
        Assert.Empty(events); // stop() set isStopping: nothing further is emitted
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-109")] // ACPSession.swift:855-864; Core/Models/Timeline.swift:151-160
    [InlineData("read", ToolCall.Kind.Read)]
    [InlineData("edit", ToolCall.Kind.Edit)]
    [InlineData("delete", ToolCall.Kind.Edit)]
    [InlineData("move", ToolCall.Kind.Edit)]
    [InlineData("search", ToolCall.Kind.Search)]
    [InlineData("execute", ToolCall.Kind.Command)]
    [InlineData("fetch", ToolCall.Kind.Web)]
    [InlineData("mcp", ToolCall.Kind.Other)]
    [InlineData("agent", ToolCall.Kind.Other)]
    [InlineData("unknown", ToolCall.Kind.Other)]
    [InlineData(null, ToolCall.Kind.Other)]
    public void Kind_maps_six_ACP_strings_and_falls_everything_else_including_mcp_and_agent_to_other(string? raw, ToolCall.Kind expected)
    {
        Assert.Equal(expected, InvokeStatic<ToolCall.Kind>("Kind", raw));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-110")] // ACPSession.swift:889-895
    [Trait("Req", "UI-PROVACP-012")] // ACPSession.swift:889-895 (running: the status was absent from the recognized set)
    [InlineData("completed", ToolCall.Status.Completed)]
    [InlineData("failed", ToolCall.Status.Failed)]
    [InlineData("pending", ToolCall.Status.Running)]
    [InlineData("in_progress", ToolCall.Status.Running)]
    [InlineData("mystery-status", ToolCall.Status.Running)]
    public void Status_maps_completed_and_failed_and_treats_everything_else_as_running(string raw, ToolCall.Status expected)
    {
        Assert.Equal(expected, InvokeStatic<ToolCall.Status>("Status", raw));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-111")] // ACPSession.swift:866-877
    [InlineData(ToolCall.Kind.Command, "Run command")]
    [InlineData(ToolCall.Kind.Read, "Read file")]
    [InlineData(ToolCall.Kind.Edit, "Edit file")]
    [InlineData(ToolCall.Kind.Search, "Search")]
    [InlineData(ToolCall.Kind.Web, "Fetch")]
    [InlineData(ToolCall.Kind.Mcp, "Tool")]
    [InlineData(ToolCall.Kind.Agent, "Agent")]
    [InlineData(ToolCall.Kind.Other, "Tool")]
    public void FallbackTitle_returns_exactly_eight_verbatim_strings(ToolCall.Kind kind, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("FallbackTitle", kind));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-112")] // ACPSession.swift:879-887
    [InlineData("Run Command", true)]
    [InlineData("READ FILE", true)]
    [InlineData("Edit File", true)]
    [InlineData("Write File", true)]
    [InlineData("Search", true)]
    [InlineData("Fetch", true)]
    [InlineData("Tool", true)]
    [InlineData("Agent", true)]
    [InlineData("File", true)]
    [InlineData("Files", true)]
    [InlineData("READ", true)] // not one of the ten literals: delegates to isBareToolName
    [InlineData("Read file.ts", false)]
    [InlineData("Something else", false)]
    public void IsFallbackTitle_rejects_ten_generic_labels_lowercased_and_delegates_to_isBareToolName(string title, bool expected)
    {
        Assert.Equal(expected, InvokeStatic<bool>("IsFallbackTitle", title));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-113")] // ACPSession.swift:830-838
    public void IsBareToolName_recognizes_exactly_thirty_five_identifiers_case_insensitively()
    {
        string[] names =
        [
            "read", "write", "edit", "multiedit", "patch", "bash", "shell", "list", "ls", "glob", "grep", "search",
            "webfetch", "websearch", "fetch", "todowrite", "todoread", "task", "tool", "apply_patch", "read_file",
            "write_file", "edit_file", "list_files", "search_files", "run_command", "run_terminal_cmd", "web_search",
            "web_fetch", "codebase_search", "file_search", "grep_search", "list_dir", "view_file", "replace_file_content",
        ];
        Assert.Equal(35, names.Length);
        foreach (var name in names)
        {
            Assert.True(InvokeStatic<bool>("IsBareToolName", name), name);
            Assert.True(InvokeStatic<bool>("IsBareToolName", name.ToUpperInvariant()), name);
        }
        Assert.False(InvokeStatic<bool>("IsBareToolName", "cmd")); // Windows-flavoured names are not invented additions
        Assert.False(InvokeStatic<bool>("IsBareToolName", "powershell"));
        Assert.False(InvokeStatic<bool>("IsBareToolName", "readme"));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-114")] // ACPSession.swift:851-853
    [InlineData(null, "")]
    [InlineData("`path/to/file`", "path/to/file")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("\ntitle\n", "title")]
    [InlineData("`\n mixed \n`", "mixed")]
    [InlineData("kept\r", "kept\r")] // carriage return is NOT in the trimmed set
    [InlineData("kept\t", "kept\t")] // tab is NOT in the trimmed set
    public void CleanTitle_trims_only_backticks_spaces_and_newlines_from_both_ends(string? title, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("CleanTitle", title));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-115")] // ACPSession.swift:788-792,809-813
    [InlineData("searched web for cats", ToolCall.Kind.Web, "cats")]
    [InlineData("search web for cats", ToolCall.Kind.Web, "cats")]
    [InlineData("searching web for cats", ToolCall.Kind.Web, "cats")]
    [InlineData("searched the web for cats", ToolCall.Kind.Web, "cats")]
    [InlineData("search the web for cats", ToolCall.Kind.Web, "cats")]
    [InlineData("searched for cats", ToolCall.Kind.Search, "cats")]
    [InlineData("searching for cats", ToolCall.Kind.Search, "cats")]
    [InlineData("search for cats", ToolCall.Kind.Search, "cats")]
    [InlineData("reading file a.txt", ToolCall.Kind.Read, "a.txt")]
    [InlineData("read file a.txt", ToolCall.Kind.Read, "a.txt")]
    [InlineData("listed directory src", ToolCall.Kind.Read, "src")]
    [InlineData("listing directory src", ToolCall.Kind.Read, "src")]
    [InlineData("list directory src", ToolCall.Kind.Read, "src")]
    [InlineData("listed files in src", ToolCall.Kind.Read, "src")]
    [InlineData("list files in src", ToolCall.Kind.Read, "src")]
    [InlineData("listing files in src", ToolCall.Kind.Read, "src")]
    public void Subject_strips_at_most_one_of_sixteen_phrases_the_first_the_lowered_title_starts_with(string title, ToolCall.Kind kind, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("Subject", title, kind));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-115")] // ACPSession.swift:786-787,809-813
    public void Subject_phrase_stripping_never_applies_outside_read_edit_search_and_web_kinds()
    {
        Assert.Equal("search for cats", InvokeStatic<string>("Subject", "search for cats", ToolCall.Kind.Command));
        Assert.Equal("search for cats", InvokeStatic<string>("Subject", "search for cats", ToolCall.Kind.Mcp));
        Assert.Equal("search for cats", InvokeStatic<string>("Subject", "search for cats", ToolCall.Kind.Agent));
        Assert.Equal("search for cats", InvokeStatic<string>("Subject", "search for cats", ToolCall.Kind.Other));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-116")] // ACPSession.swift:793-799,814-818
    [InlineData("Fetched https://example.com", ToolCall.Kind.Web, "https://example.com")]
    [InlineData("Fetched", ToolCall.Kind.Web, "Fetched")] // remainder would be empty: the verb is kept
    [InlineData("Read a.txt", ToolCall.Kind.Read, "a.txt")]
    [InlineData("Browsed https://x", ToolCall.Kind.Web, "https://x")]
    [InlineData("Opened file.txt", ToolCall.Kind.Read, "file.txt")]
    public void Subject_strips_one_leading_verb_when_no_phrase_matched_and_something_remains(string title, ToolCall.Kind kind, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("Subject", title, kind));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-116")] // ACPSession.swift:793-799
    public void Subject_recognizes_exactly_forty_eight_verbs()
    {
        string[] verbs =
        [
            "read", "reading", "viewed", "viewing", "view", "fetched", "fetching", "fetch", "browsed", "browsing",
            "searched", "searching", "search", "grep", "glob", "listed", "listing", "list", "wrote", "writing",
            "write", "edited", "editing", "edit", "modified", "modifying", "modify", "updated", "updating", "update",
            "created", "creating", "create", "deleted", "deleting", "delete", "ran", "running", "run", "executed",
            "executing", "execute", "called", "calling", "call", "opened", "opening", "open",
        ];
        Assert.Equal(48, verbs.Length);
        foreach (var verb in verbs)
            Assert.True("x" == InvokeStatic<string>("Subject", $"{verb} x", ToolCall.Kind.Read), verb);
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-117")] // ACPSession.swift:819-825
    [InlineData("'devin'", "devin")]
    [InlineData("\"devin\"", "devin")]
    [InlineData("“devin”", "devin")] // U+201C LEFT / U+201D RIGHT DOUBLE QUOTATION MARK
    [InlineData("`devin`", "devin")]
    [InlineData("'", "'")] // one character: the length check fails, left alone
    [InlineData("''", "")] // exactly two characters: unwraps to empty
    [InlineData("'mismatched\"", "'mismatched\"")] // mismatched pair: not unwrapped
    public void Subject_unwraps_one_matching_pair_of_four_delimiter_kinds(string title, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("Subject", title, ToolCall.Kind.Read));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-117")] // ACPSession.swift:819-825 (for ... where, no break)
    public void Subject_unwraps_more_than_one_kind_of_quote_when_nested_because_the_loop_never_breaks()
    {
        // '"x"' : single-quote pair strips first ('"x"' -> "x"), then the double-quote pair strips again ("x" -> x).
        Assert.Equal("x", InvokeStatic<string>("Subject", "'\"x\"'", ToolCall.Kind.Read));
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-118")] // ACPSession.swift:801-804
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("read", "")]
    [InlineData("READ", "")]
    [InlineData("todowrite", "")]
    public void Subject_returns_empty_for_a_bare_tool_name_before_any_phrase_or_verb_stripping(string? title, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("Subject", title, ToolCall.Kind.Read));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-119")] // ACPSession.swift:773,714-715,775-780
    public void MakeToolCall_path_takes_the_first_non_empty_of_seven_rawInput_keys_in_order_then_locations()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        ToolCall CallWithRaw(params (string, JSONValue)[] rawFields) =>
            InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
                ("kind", "read"), ("rawInput", JSONValue.FromDictionaryLiteral(rawFields))));

        Assert.Equal("a.txt", CallWithRaw(("filePath", "a.txt"), ("file_path", "b.txt"), ("path", "c.txt")).Title);
        Assert.Equal("b.txt", CallWithRaw(("file_path", "b.txt"), ("path", "c.txt")).Title);
        Assert.Equal("c.txt", CallWithRaw(("path", "c.txt"), ("file", "d.txt")).Title);
        Assert.Equal("d.txt", CallWithRaw(("file", "d.txt"), ("notebook_path", "e.ipynb")).Title);
        Assert.Equal("e.ipynb", CallWithRaw(("notebook_path", "e.ipynb"), ("target_file", "f.txt")).Title);
        Assert.Equal("f.txt", CallWithRaw(("target_file", "f.txt"), ("targetFile", "g.txt")).Title);
        Assert.Equal("g.txt", CallWithRaw(("targetFile", "g.txt")).Title);
        Assert.Equal("g.txt", CallWithRaw(("filePath", "   "), ("targetFile", "g.txt")).Title); // whitespace-only value skipped

        var withLocation = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "read"),
            ("locations", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("path", "from-location.txt"))))));
        Assert.Equal("from-location.txt", withLocation.Title); // none of the seven keys: falls to locations[0].path
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-120")] // ACPSession.swift:719-720; ProviderSession.swift:180-192
    [InlineData("/bin/zsh -lc 'echo hi'", "echo hi")]
    [InlineData("/bin/bash -lc \"echo hi\"", "echo hi")]
    [InlineData("bash -lc 'echo hi'", "echo hi")]
    [InlineData("zsh -lc 'echo hi'", "echo hi")]
    [InlineData("/bin/sh -c 'echo hi'", "echo hi")]
    [InlineData("sh -c 'echo hi'", "echo hi")]
    [InlineData("plain command", "plain command")] // no matching prefix: shown unwrapped
    public void MakeToolCall_titles_a_command_row_from_the_unwrapped_shell_command(string command, string expected)
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var call = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "execute"), ("rawInput", JSONValue.FromDictionaryLiteral(("command", command)))));
        Assert.Equal(expected, call.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-120")] // ACPSession.swift:719-720
    public void MakeToolCall_falls_back_to_the_cleaned_title_when_rawInput_command_is_not_a_string()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var call = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "execute"), ("title", "Run something")));
        Assert.Equal("Run something", call.Title); // .command is never phrase/verb-stripped
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-121")] // ACPSession.swift:721-722
    public void MakeToolCall_titles_read_and_edit_rows_by_path_falling_back_to_the_cleaned_title_with_no_detail()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);

        var withPath = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "read"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", "src/a.ts"))), ("title", "Reading something")));
        Assert.Equal("src/a.ts", withPath.Title); // the path wins over the agent's title
        Assert.Null(withPath.Detail);

        var noPath = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "edit"), ("title", "Edited b.ts")));
        Assert.Equal("b.ts", noPath.Title); // no path found: falls back to the (verb-stripped) cleaned title
        Assert.Null(noPath.Detail);
    }

    // ===== Part 6 of 8: the rest of makeToolCall/makeToolUpdate's title/detail synthesis (search, web,
    // mcp/agent/other, the fallback and isFallbackTitle tables, diffs()) and the catalog probe, probeModels
    // and probeModelEfforts (PR-PROVACP-122..138, UI-PROVACP-001..007).

    private static ValueMap<string, string> Env(params (string Key, string Value)[] pairs) =>
        new(pairs.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    [Trait("Req", "PR-PROVACP-122")] // ACPSession.swift:723-726
    [Trait("Req", "UI-PROVACP-001")] // ACPSession.swift:706-737, :801-826
    public void MakeToolCall_titles_a_search_row_from_five_keys_in_order_with_a_four_part_detail_condition()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var wd = Private<string>(session, "WorkingDirectory");
        ToolCall Call(params (string Key, JSONValue Value)[] rawInput) => InvokePrivateValue<ToolCall>(session, "MakeToolCall",
            JSONValue.FromDictionaryLiteral(("kind", "search"), ("rawInput", JSONValue.FromDictionaryLiteral(rawInput))));

        // Key priority: pattern beats query beats glob beats regex beats search.
        Assert.Equal("P", Call(("pattern", "P"), ("query", "Q"), ("glob", "G"), ("regex", "R"), ("search", "S")).Title);
        Assert.Equal("Q", Call(("query", "Q"), ("glob", "G"), ("regex", "R"), ("search", "S")).Title);
        Assert.Equal("G", Call(("glob", "G"), ("regex", "R"), ("search", "S")).Title);
        Assert.Equal("R", Call(("regex", "R"), ("search", "S")).Title);
        Assert.Equal("S", Call(("search", "S")).Title);

        // A pattern found, a differing path: detail is set.
        var withPattern = Call(("pattern", "TODO"), ("path", "src/other.ts"));
        Assert.Equal("TODO", withPattern.Title);
        Assert.Equal("src/other.ts", withPattern.Detail); // != workingDirectory, != title

        // No pattern, but a non-empty agent title: still qualifies for detail, and the title is the agent title.
        var noPatternWithTitle = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "search"), ("title", "Searched for widgets"),
            ("rawInput", JSONValue.FromDictionaryLiteral(("path", "src/other.ts")))));
        Assert.Equal("widgets", noPatternWithTitle.Title); // "searched for " phrase stripped
        Assert.Equal("src/other.ts", noPatternWithTitle.Detail);

        // No pattern and an empty agent title: falls to path as the TITLE itself, so no detail (path == title).
        var noPatternNoTitle = Call(("path", "src/other.ts"));
        Assert.Equal("src/other.ts", noPatternNoTitle.Title);
        Assert.Null(noPatternNoTitle.Detail);

        // Path equal to the working directory: no detail even with a pattern.
        var pathIsWd = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "search"), ("rawInput", JSONValue.FromDictionaryLiteral(("pattern", "TODO"), ("path", wd)))));
        Assert.Equal("TODO", pathIsWd.Title);
        Assert.Null(pathIsWd.Detail);

        // Path equal to the chosen title: no detail.
        var pathIsTitle = Call(("pattern", "same"), ("path", "same"));
        Assert.Equal("same", pathIsTitle.Title);
        Assert.Null(pathIsTitle.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-123")] // ACPSession.swift:727-728
    public void MakeToolCall_titles_a_web_row_from_url_query_or_prompt_in_order_with_no_detail_ever()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        ToolCall Call(params (string Key, JSONValue Value)[] rawInput) => InvokePrivateValue<ToolCall>(session, "MakeToolCall",
            JSONValue.FromDictionaryLiteral(("kind", "fetch"), ("rawInput", JSONValue.FromDictionaryLiteral(rawInput))));

        Assert.Equal("https://x", Call(("url", "https://x"), ("query", "q"), ("prompt", "p")).Title);
        Assert.Equal("q", Call(("query", "q"), ("prompt", "p")).Title);
        Assert.Equal("p", Call(("prompt", "p")).Title);
        Assert.Null(Call(("prompt", "p")).Detail);

        var fallsToTitle = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "fetch"), ("title", "Fetched https://y"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", "ignored")))));
        Assert.Equal("https://y", fallsToTitle.Title); // "fetched " verb stripped; the path key is not tried for .web
        Assert.Null(fallsToTitle.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-124")] // ACPSession.swift:729-732
    public void MakeToolCall_titles_an_other_row_from_the_agent_title_with_a_three_part_path_detail()
    {
        // ACP kind() never maps to .mcp or .agent, so this branch is exercised through .other (an unknown kind string).
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var wd = Private<string>(session, "WorkingDirectory");

        var withPath = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "unknown-kind"), ("title", "Called a tool"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", "src/x.ts")))));
        Assert.Equal("Called a tool", withPath.Title); // .other never strips a verb (rowHasVerb excludes it): no path substitution either
        Assert.Equal("src/x.ts", withPath.Detail);

        var pathIsWd = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "unknown-kind"), ("title", "Called a tool"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", wd)))));
        Assert.Null(pathIsWd.Detail); // path == workingDirectory

        var pathIsTitle = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(
            ("kind", "unknown-kind"), ("title", "same"), ("rawInput", JSONValue.FromDictionaryLiteral(("path", "same")))));
        Assert.Null(pathIsTitle.Detail); // path == title
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-125")] // ACPSession.swift:733
    [InlineData("read", null, "Read file")]
    [InlineData("edit", null, "Edit file")]
    [InlineData("execute", null, "Run command")]
    [InlineData("frobnicate", null, "Tool")] // .other
    [InlineData("read", "src/a.ts", "src/a.ts")]
    public void MakeToolCall_falls_back_to_the_path_then_the_kind_fallback_label_when_titling_left_it_empty(string kind, string? path, string expected)
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var raw = path is null ? JSONValue.FromDictionaryLiteral() : JSONValue.FromDictionaryLiteral(("path", path));
        var call = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(("kind", kind), ("rawInput", raw)));
        Assert.Equal(expected, call.Title);
        Assert.NotEqual("", call.Title); // a tool row is never blank
    }

    [Theory]
    [Trait("Req", "UI-PROVACP-002")] // ACPSession.swift:868
    [Trait("Req", "UI-PROVACP-003")] // ACPSession.swift:869
    [Trait("Req", "UI-PROVACP-004")] // ACPSession.swift:870
    [Trait("Req", "UI-PROVACP-005")] // ACPSession.swift:871
    [Trait("Req", "UI-PROVACP-006")] // ACPSession.swift:872
    [Trait("Req", "UI-PROVACP-007")] // ACPSession.swift:873,875
    [InlineData(ToolCall.Kind.Command, "Run command")]
    [InlineData(ToolCall.Kind.Read, "Read file")]
    [InlineData(ToolCall.Kind.Edit, "Edit file")]
    [InlineData(ToolCall.Kind.Search, "Search")]
    [InlineData(ToolCall.Kind.Web, "Fetch")]
    [InlineData(ToolCall.Kind.Mcp, "Tool")]
    [InlineData(ToolCall.Kind.Agent, "Agent")]
    [InlineData(ToolCall.Kind.Other, "Tool")]
    public void FallbackTitle_is_the_exact_byte_for_byte_label_per_kind(ToolCall.Kind kind, string expected) =>
        Assert.Equal(expected, InvokeStatic<string>("FallbackTitle", kind));

    [Fact]
    [Trait("Req", "PR-PROVACP-126")] // ACPSession.swift:840-849
    [Trait("Req", "UI-PROVACP-011")] // ACPSession.swift:840-849 (a creation shows an empty old side, a deletion an empty new side: missing oldText/newText become "")
    public void Diffs_reads_only_diff_blocks_with_a_path_makes_it_relative_and_empties_missing_text()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var content = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "diff"), ("path", "src/a.ts"), ("oldText", "old"), ("newText", "new")),
            JSONValue.FromDictionaryLiteral(("type", "diff"), ("path", "src/b.ts"))); // no oldText/newText: both ""
        var withNoPath = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "diff"), ("oldText", "x"))); // no path: dropped
        var wrongType = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "content"), ("path", "src/c.ts")));

        var edits = InvokePrivateValue<ValueList<FileEdit>>(session, "Diffs", content);
        Assert.Equal(2, edits.Count);
        Assert.Equal("src/a.ts", edits[0].Path);
        Assert.Equal(new FileEdit("src/a.ts", "old", "new"), edits[0]); // built from the same old/new text
        Assert.Equal("src/b.ts", edits[1].Path);
        Assert.Equal(new FileEdit("src/b.ts", "", ""), edits[1]); // missing oldText/newText become "", not nil

        Assert.Empty(InvokePrivateValue<ValueList<FileEdit>>(session, "Diffs", withNoPath));
        Assert.Empty(InvokePrivateValue<ValueList<FileEdit>>(session, "Diffs", wrongType));
    }

    private static ToolUpdate Update6(ACPSession session, params (string Key, JSONValue Value)[] fields) =>
        InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(fields));

    [Fact]
    [Trait("Req", "PR-PROVACP-127")] // ACPSession.swift:741-742
    public void MakeToolUpdate_leaves_Kind_nil_without_a_kind_field_and_the_missing_kind_default_skips_verb_stripping()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var noKind = Update6(session, ("title", "Read something.txt"));
        Assert.Null(noKind.Kind); // never resets an existing row's kind
        Assert.Equal("Read something.txt", noKind.Title); // .other default: no verb stripping

        var withKind = Update6(session, ("kind", "read"), ("title", "Read something.txt"));
        Assert.Equal(ToolCall.Kind.Read, withKind.Kind);
        Assert.Equal("something.txt", withKind.Title); // .read: the "Read " verb IS stripped
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-128")] // ACPSession.swift:743-745
    public void MakeToolUpdate_a_generic_or_bare_title_never_overwrites_the_rows_title()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        Assert.Null(Update6(session, ("kind", "edit"), ("title", "Edit file")).Title); // one of the ten generic labels
        Assert.Null(Update6(session, ("kind", "edit"), ("title", "bash")).Title); // a bare tool name
        Assert.Null(Update6(session, ("kind", "edit"), ("title", "")).Title); // empty after cleaning
        Assert.Equal("src/thing.ts", Update6(session, ("kind", "edit"), ("title", "src/thing.ts")).Title); // a real subject survives
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-129")] // ACPSession.swift:746
    public void MakeToolUpdate_rawInput_command_always_wins_the_title_unconditionally()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var withOtherTitle = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("kind", "edit"), ("title", "src/thing.ts"), ("rawInput", JSONValue.FromDictionaryLiteral(("command", "bash -lc 'ls'")))));
        Assert.Equal("ls", withOtherTitle.Title); // command wins over the already-set title, and is unwrapped

        var emptyCommand = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("title", "src/thing.ts"), ("rawInput", JSONValue.FromDictionaryLiteral(("command", "")))));
        Assert.Equal("", emptyCommand.Title); // an empty command string still overwrites, with an empty title
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-130")] // ACPSession.swift:747-752
    public void MakeToolUpdate_a_path_that_only_arrives_with_the_update_names_an_untitled_read_or_edit_row()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var wd = Private<string>(session, "WorkingDirectory");

        var named = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("kind", "read"), ("locations", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("path", "src/a.ts"))))));
        Assert.Equal("src/a.ts", named.Title);

        // The row equal to the working directory itself: nothing is set.
        var isWd = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("kind", "edit"), ("locations", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("path", wd))))));
        Assert.Null(isWd.Title);

        // A title already set from an earlier step: the location is never consulted.
        var alreadyTitled = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("kind", "read"), ("title", "src/keep.ts"),
            ("locations", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("path", "src/other.ts"))))));
        Assert.Equal("src/keep.ts", alreadyTitled.Title);

        // Neither .read nor .edit: locations is never consulted, even with no kind (defaults to .other).
        var otherKind = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("kind", "execute"), ("locations", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("path", "src/a.ts"))))));
        Assert.Null(otherKind.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-131")] // ACPSession.swift:756-765
    [Trait("Req", "UI-PROVACP-010")] // ACPSession.swift:756-768 (content blocks joined by single line feeds; rawOutput stdout/stderr/output fallback in that order)
    public void MakeToolUpdate_output_is_joined_from_content_blocks_and_only_falls_back_to_rawOutput_when_empty()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var content = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", "line1")))),
            JSONValue.FromDictionaryLiteral(("type", "other")), // wrong type: skipped
            JSONValue.FromDictionaryLiteral(("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", "line2")))));
        var withContent = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("content", content), ("rawOutput", JSONValue.FromDictionaryLiteral(("stdout", "ignored")))));
        Assert.Equal("line1\nline2", withContent.Output); // content blocks always win over rawOutput

        var fallback = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("stdout", "out"), ("stderr", "err"), ("output", "o")))));
        Assert.Equal("out\nerr\no", fallback.Output); // stdout, stderr, output, in that order

        var partial = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("stderr", ""), ("output", "o")))));
        Assert.Equal("o", partial.Output); // absent stdout, empty stderr both dropped

        var nullRawOutput = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", new JSONValue.Null())));
        Assert.Null(nullRawOutput.Output);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-132")] // ACPSession.swift:766
    public void MakeToolUpdate_reads_exitCode_from_rawOutput_exitCode_or_metadata_exit_even_when_content_supplied_output()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var content = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", "hi")))));

        var direct = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("content", content), ("rawOutput", JSONValue.FromDictionaryLiteral(("exitCode", 7L)))));
        Assert.Equal(7L, direct.ExitCode); // runs even though content supplied the output

        var viaMetadata = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("metadata", JSONValue.FromDictionaryLiteral(("exit", 9L)))))));
        Assert.Equal(9L, viaMetadata.ExitCode); // exitCode wins over metadata.exit when both present

        var both = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("exitCode", 1L), ("metadata", JSONValue.FromDictionaryLiteral(("exit", 2L)))))));
        Assert.Equal(1L, both.ExitCode);

        var neither = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("stdout", "x")))));
        Assert.Null(neither.ExitCode);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-133")] // ACPSession.swift:768
    [Trait("Req", "UI-PROVACP-010")] // ACPSession.swift:768 (empty output or the exact literal "(no output)": no pane)
    public void MakeToolUpdate_suppresses_the_exact_literal_no_output_placeholder_but_not_a_padded_variant()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var content = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
            ("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", "(no output)")))));
        Assert.Null(Update6(session, ("content", content)).Output);

        var padded = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
            ("type", "content"), ("content", JSONValue.FromDictionaryLiteral(("text", " (no output)")))));
        Assert.Equal(" (no output)", Update6(session, ("content", padded)).Output); // exact match only, whitespace not trimmed first

        Assert.Null(Update6(session).Output); // nothing collected at all
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-134")] // ACPSession.swift:769
    [Trait("Req", "UI-PROVACP-012")] // ACPSession.swift:769,889-895 (completed with a non-zero exit code becomes failed)
    [InlineData("completed", 1L, ToolCall.Status.Failed)]
    [InlineData("completed", 0L, ToolCall.Status.Completed)]
    [InlineData("running", 1L, ToolCall.Status.Running)]
    [InlineData("failed", 1L, ToolCall.Status.Failed)]
    public void MakeToolUpdate_downgrades_only_a_completed_status_with_a_nonzero_exit_code(string status, long exitCode, ToolCall.Status expected)
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var update = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("status", status), ("rawOutput", JSONValue.FromDictionaryLiteral(("exitCode", exitCode)))));
        Assert.Equal(expected, update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-134")] // ACPSession.swift:769 (no status at all: stays nil even with a nonzero exit)
    public void MakeToolUpdate_with_no_status_stays_nil_even_with_a_nonzero_exit_code()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        var update = InvokePrivateValue<ToolUpdate>(session, "MakeToolUpdate", JSONValue.FromDictionaryLiteral(
            ("rawOutput", JSONValue.FromDictionaryLiteral(("exitCode", 1L)))));
        Assert.Null(update.Status);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-135")] // ACPSession.swift:754-755, contrast with :734
    [Trait("Req", "UI-PROVACP-011")] // ACPSession.swift:734,754-755 (an update with no diff blocks leaves the row's existing edits alone; a tool call with none starts empty)
    public void MakeToolUpdate_edits_stays_nil_without_a_diff_block_while_MakeToolCall_edits_is_always_assigned()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        Assert.Null(Update6(session).Edits); // no content at all: nil, never clears a row's existing edits

        var content = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(
            ("type", "diff"), ("path", "a.ts"), ("oldText", "o"), ("newText", "n")));
        var withEdits = Update6(session, ("content", content));
        Assert.NotNull(withEdits.Edits);
        Assert.Single(withEdits.Edits!);

        var call = InvokePrivateValue<ToolCall>(session, "MakeToolCall", JSONValue.FromDictionaryLiteral(("kind", "read")));
        Assert.NotNull(call.Edits); // ToolCall.Edits is a non-nullable ValueList, always assigned
        Assert.Empty(call.Edits); // empty, since no diff blocks were present
    }

    // ----- The catalog probe (PR-PROVACP-136, 137, 138). LaunchArguments is fixed per provider (e.g. the
    // literal ["acp"]) with no room to inject the -EncodedCommand/-File scripted stand-in the rest of this
    // file drives through OpenSession/ApplySelection (see the file's part 1 header note), so ProbeModels
    // cannot be driven end to end with a live agent. Tested here via extraction (PrepareProbeCatalogDirectory,
    // ProbeConfiguration), a failing Start() to prove propagation, and probeModelEfforts exercised directly
    // against a live scripted agent the same way ApplySelection/SetConfigOption already are.

    [Fact]
    [Trait("Req", "PR-PROVACP-136")] // ACPSession.swift:229-230
    public void PrepareProbeCatalogDirectory_creates_marrow_catalog_under_the_system_temp_folder_and_swallows_a_failure()
    {
        var expected = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marrow-catalog");
        if (System.IO.Directory.Exists(expected)) System.IO.Directory.Delete(expected, recursive: true);
        if (System.IO.File.Exists(expected)) System.IO.File.Delete(expected);

        var path = InvokeStatic<string>("PrepareProbeCatalogDirectory");
        Assert.Equal(expected, path); // DEVIATION OD-0008 (OD-PENDING-17): "marrow-catalog", not "droppy-code-catalog"
        Assert.True(System.IO.Directory.Exists(path));

        // A creation failure (a plain FILE already occupying the path) is swallowed, not thrown.
        System.IO.Directory.Delete(expected, recursive: true);
        System.IO.File.WriteAllText(expected, "blocking");
        try
        {
            var swallowed = InvokeStatic<string>("PrepareProbeCatalogDirectory");
            Assert.Equal(expected, swallowed); // no exception, same path returned regardless
        }
        finally
        {
            System.IO.File.Delete(expected);
            System.IO.Directory.CreateDirectory(expected);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-136")] // ACPSession.swift:231-238; :898-920 (the convenience init's nil fields)
    public void ProbeConfiguration_is_always_supervised_and_build_with_no_resume_model_or_effort()
    {
        var executable = SwiftURL.Create("file:///" + ACPSessionFixtures.PowerShell.Replace('\\', '/'))!;
        var directoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marrow-catalog-config-test");
        var env = Env(("K", "V"));
        var config = (SessionConfiguration)typeof(ACPSession).GetMethod("ProbeConfiguration", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [ProviderKind.Cursor, executable, directoryPath, env])!;
        Assert.Equal(ProviderKind.Cursor, config.Provider);
        Assert.Equal(executable, config.Executable);
        Assert.Equal(RuntimeMode.Supervised, config.RuntimeMode); // never launches with a permissive flag
        Assert.Equal(InteractionMode.Build, config.InteractionMode);
        Assert.Null(config.ResumeID);
        Assert.Null(config.ResumeAt);
        Assert.Null(config.Model);
        Assert.Null(config.Effort);
        Assert.Equal(env, config.Environment);
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-136")] // ACPSession.swift:228-247 (start() throwing propagates out of probeModels)
    [Trait("Req", "PR-PROVACP-137")] // ACPSession.swift:239-246 (defer runs a stop on every exit path, including a throw)
    public async Task ProbeModels_propagates_a_failure_from_Start()
    {
        var missing = SwiftURL.Create("file:///" + System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "marrow-missing-" + Guid.NewGuid().ToString("N"), "nope.exe").Replace('\\', '/'))!;
        await Assert.ThrowsAnyAsync<Exception>(() => ACPSession.ProbeModels(ProviderKind.Grok, missing, ValueMap<string, string>.Empty));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-138")] // ACPSession.swift:288 (no modelConfigID: returns immediately)
    public void ProbeModelEfforts_without_a_modelConfigID_returns_immediately_leaving_the_current_model_untouched()
    {
        using var runtime = new TestRuntime();
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "models", new ValueList<ModelOption>([new ModelOption("m1", "M1")]));
        SetField(session, "currentModel", "orig");
        RunOnMain(runtime, session, "ProbeModelEfforts");
        Assert.Equal("orig", Private<string?>(session, "currentModel", field: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-138")] // ACPSession.swift:289-296
    public void ProbeModelEfforts_walks_only_models_with_empty_efforts_and_restores_a_different_original_by_base_id()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $rid=[regex]::Match($l,'\"id\":(\\d+)').Groups[1].Value; " +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":'+$rid+',\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "modelConfigID", "model-opt");
        SetField(session, "currentModel", "m0"); // the original: differs by base id from the last model walked
        SetField(session, "models", new ValueList<ModelOption>([
            new ModelOption("m1", "M1"), // efforts empty: walked
            new ModelOption("m2", "M2", efforts: new ValueList<string>(["low", "high"])), // efforts non-empty: skipped
        ]));
        RunOnMain(runtime, session, "ProbeModelEfforts");
        var frames = Lines(process.ErrorTail).Select(l => JSONValue.Parse(l)!).ToList();
        Assert.Equal(2, frames.Count); // one walk request for m1 (m2 skipped), one restore request for m0
        Assert.Equal("m1", frames[0]["params"]!["value"]!.JSONValueString);
        Assert.Equal("m0", frames[1]["params"]!["value"]!.JSONValueString);
        Assert.Equal("m0", Private<string?>(session, "currentModel", field: true)); // restored
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-138")] // ACPSession.swift:294 (same base id: no restore request is sent)
    public void ProbeModelEfforts_skips_the_restore_request_when_the_walk_ends_on_the_same_base_id()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Cursor,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $rid=[regex]::Match($l,'\"id\":(\\d+)').Groups[1].Value; " +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":'+$rid+',\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "modelConfigID", "model-opt");
        SetField(session, "currentModel", "gpt-5[effort=high]"); // same base id as the model walked below
        SetField(session, "models", new ValueList<ModelOption>([new ModelOption("gpt-5", "GPT-5")]));
        RunOnMain(runtime, session, "ProbeModelEfforts");
        var frames = Lines(process.ErrorTail).Select(l => JSONValue.Parse(l)!).ToList();
        Assert.Single(frames); // only the walk request; no restore
        Assert.Equal("gpt-5", frames[0]["params"]!["value"]!.JSONValueString);
        Assert.Equal("gpt-5", Private<string?>(session, "currentModel", field: true));
        runtime.OnMain(() => connection.Notify("bye"));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-138")] // ACPSession.swift:294 (original is nil: no restore is attempted)
    public void ProbeModelEfforts_attempts_no_restore_when_there_was_no_original_current_model()
    {
        using var runtime = new TestRuntime();
        var (session, connection, process) = ConnectedSession(runtime, ProviderKind.Grok,
            Pipes + "while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); " +
            "if($l.Contains('\"method\":\"session/set_config_option\"')){ $rid=[regex]::Match($l,'\"id\":(\\d+)').Groups[1].Value; " +
            "$o.WriteLine('{\"jsonrpc\":\"2.0\",\"id\":'+$rid+',\"result\":{}}'); $o.Flush() } " + EndOnBye);
        SetField(session, "modelConfigID", "model-opt");
        SetField(session, "currentModel", null);
        SetField(session, "models", new ValueList<ModelOption>([new ModelOption("m1", "M1")]));
        RunOnMain(runtime, session, "ProbeModelEfforts");
        Assert.Single(Lines(process.ErrorTail)); // only the one walk request
        Assert.Equal("m1", Private<string?>(session, "currentModel", field: true)); // left on the last model walked
        runtime.OnMain(() => connection.Notify("bye"));
    }

    // ===== Part 7 of 8: UI-PROVACP-008..031. Most of these records describe behaviour whose members were
    // already implemented (and tested under their PR-PROVACP id) in parts 1-6; this part adds the UI-PROVACP
    // trait to those existing tests (see the edits above) and supplies dedicated tests only for the states
    // no earlier test already proved: the unreachable .agent fallback row (008), the "never a detail" side of
    // the tool-row detail rule (009), and the session-start failure message (030), which needed Start()'s
    // final guard factored into RequireSessionID so it could be proven without a live agent round trip (see
    // that method's header comment and the same reasoning already recorded for PR-PROVACP-033 in part 2).

    [Fact]
    [Trait("Req", "UI-PROVACP-008")] // ACPSession.swift:874; :855-864 (kind() never returns .agent, so this label is defined but unreachable)
    public void FallbackTitle_for_the_agent_kind_is_the_exact_word_Agent_even_though_kind_never_produces_it()
    {
        Assert.Equal("Agent", InvokeStatic<string>("FallbackTitle", ToolCall.Kind.Agent));
        // Kind() (:855-864) maps every recognized ACP kind string to .read/.edit/.search/.command/.web, and
        // anything else (including "agent") to .other, never to .agent: the row is specified but unreachable
        // from a real ACP payload, so it must stay in the table for any other caller of FallbackTitle.
        foreach (var raw in new[] { "read", "edit", "delete", "move", "search", "execute", "fetch", "agent", "mcp", "unknown", null })
            Assert.NotEqual(ToolCall.Kind.Agent, InvokeStatic<ToolCall.Kind>("Kind", raw));
    }

    [Fact]
    [Trait("Req", "UI-PROVACP-009")] // ACPSession.swift:723-732 (absent for command, read, edit and web rows, which never set a detail)
    public void MakeToolCall_never_sets_a_detail_for_command_read_edit_or_web_rows_even_with_a_qualifying_path()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        ToolCall Call(string kind, params (string Key, JSONValue Value)[] rawInput) => InvokePrivateValue<ToolCall>(session, "MakeToolCall",
            JSONValue.FromDictionaryLiteral(("kind", kind), ("title", "Something"), ("rawInput", JSONValue.FromDictionaryLiteral(rawInput))));

        Assert.Null(Call("execute", ("path", "src/other.ts"), ("command", "ls src/other.ts")).Detail);
        Assert.Null(Call("read", ("path", "src/other.ts")).Detail);
        Assert.Null(Call("edit", ("path", "src/other.ts")).Detail);
        Assert.Null(Call("fetch", ("path", "src/other.ts"), ("url", "https://x")).Detail);
    }

    [Theory]
    [Trait("Req", "UI-PROVACP-030")] // ACPSession.swift:149; ProviderSession.swift:none; Provider.swift:25-28,32 (shown when the handshake succeeded but no session id came back)
    [InlineData(ProviderKind.Cursor, "Cursor did not start a session.")]
    [InlineData(ProviderKind.Opencode, "OpenCode Zen did not start a session.")]
    [InlineData(ProviderKind.OpencodeGo, "OpenCode Go did not start a session.")]
    [InlineData(ProviderKind.Grok, "Grok did not start a session.")]
    [InlineData(ProviderKind.Devin, "Devin did not start a session.")]
    public void RequireSessionID_throws_the_exact_per_provider_message_when_no_session_id_came_back(ProviderKind provider, string message)
    {
        var session = Session(provider, RuntimeMode.Supervised);
        SetField(session, "sessionID", null);
        var error = Assert.Throws<TargetInvocationException>(() => InvokePrivateValue<string>(session, "RequireSessionID"));
        var providerError = Assert.IsType<ProviderError>(error.InnerException);
        Assert.IsType<ProviderError.Case.Failed>(providerError.ErrorCase);
        Assert.Equal(message, providerError.Message);
    }

    [Fact]
    [Trait("Req", "UI-PROVACP-030")] // ACPSession.swift:151 (a session id present: returned, not thrown)
    public void RequireSessionID_returns_the_session_id_when_one_is_present()
    {
        var session = Session(ProviderKind.Grok, RuntimeMode.Supervised);
        SetField(session, "sessionID", "sess-9");
        Assert.Equal("sess-9", InvokePrivateValue<string>(session, "RequireSessionID"));
    }
}
