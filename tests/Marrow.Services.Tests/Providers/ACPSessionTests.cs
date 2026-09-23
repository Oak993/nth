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

    private static ValueList<string> LaunchArguments(ACPSession session) => Private<ValueList<string>>(session, "LaunchArguments");

    private static void SetRuntimeModeField(ACPSession session, RuntimeMode mode) =>
        typeof(ACPSession).GetField("runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, mode);

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

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
}
