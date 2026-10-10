using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e). Part 1: PR-PROVCLI-134
// through 158 (start, the session parameters, modes and selection, send, slash commands, interrupt, compact and
// rewind). Wire behaviour is proven against the compiled offline stand-in tests/fixtures/copilot-cli (R-TEST-8,
// R-TEST-10), which records every Content-Length frame it reads; pure members and the private state the Swift
// keeps are exercised directly (by reflection for private members), without a process.
public sealed class CopilotSessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    // A non-"file" scheme keeps the Windows path unchanged through SwiftURL.Path (the ClaudeSessionTests note).
    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/'))!;

    private static SessionConfiguration Config(
        CopilotScript? script,
        string? resumeID = null,
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        HydraLaunch? hydra = null,
        bool antiSlopEnabled = true) => new()
        {
            Provider = ProviderKind.Copilot,
            Executable = script is null ? null : FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(script is null ? Path.GetTempPath() : Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = script is null ? new ValueMap<string, string>([]) : new ValueMap<string, string>([script.EnvironmentEntry]),
            ResumeID = resumeID,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            Hydra = hydra,
            AntiSlopEnabled = antiSlopEnabled,
        };

    private static (CopilotSession Session, List<ProviderEvent> Events) Make(SessionConfiguration configuration)
    {
        var session = new CopilotSession(configuration);
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        return (session, events);
    }

    private static (CopilotSession Session, List<ProviderEvent> Events, string ID) Started(TestRuntime runtime, SessionConfiguration configuration)
    {
        var (session, events) = Make(configuration);
        var id = runtime.Loop.Run(() => session.Start(), Bound);
        return (session, events, id);
    }

    private static TurnInput Turn(string text, RuntimeMode runtimeMode = RuntimeMode.Supervised, InteractionMode interactionMode = InteractionMode.Build,
        string? model = null, string? effort = null, ValueList<Attachment>? images = null) => new()
        {
            Text = text,
            Images = images ?? ValueList<Attachment>.Empty,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
        };

    private static T Field<T>(CopilotSession session, string name) => (T)typeof(CopilotSession).GetField(name, Instance)!.GetValue(session)!;

    private static void SetField(CopilotSession session, string name, object? value) =>
        typeof(CopilotSession).GetField(name, Instance)!.SetValue(session, value);

    private static object? Invoke(CopilotSession session, string method, params object?[] args) =>
        typeof(CopilotSession).GetMethod(method, Instance)!.Invoke(session, args);

    private static IReadOnlyDictionary<string, JSONValue> Parameters(CopilotSession session) =>
        (IReadOnlyDictionary<string, JSONValue>)Invoke(session, "SessionParameters")!;

    /// <summary>A pending request of the private PendingRequest union (CopilotSession.swift:31-38).</summary>
    private static object Pending(string caseName, params object?[] args)
    {
        var union = typeof(CopilotSession).GetNestedType("PendingRequest", BindingFlags.NonPublic)!;
        var type = union.GetNestedType(caseName, BindingFlags.Public | BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, args)!;
    }

    private static void AddPending(CopilotSession session, string key, object pending) =>
        ((IDictionary)typeof(CopilotSession).GetField("pendingRequests", Instance)!.GetValue(session)!)[key] = pending;

    private static JSONValue Params(JSONValue message) => message["params"]!;

    private static List<string> Keys(JSONValue value) => value.JSONValueObject!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    // ---- PR-PROVCLI-134 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-134")] // Services/Providers/CopilotSession.swift:92; Core/Models/Provider.swift:34,53,75
    public void Start_throws_notInstalled_copilot_when_no_executable_is_resolved()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));
        var notInstalled = Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(ProviderKind.Copilot, notInstalled.Provider);
        Assert.Equal("Copilot is not installed. Install it and sign in with `copilot login`.", error.ErrorDescription);
        Assert.False(session.IsRunning);
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-135, 137 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-135")] // Services/Providers/CopilotSession.swift:480-483
    public void A_session_process_is_launched_with_the_five_base_tokens_then_the_MCP_session_overrides()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("argv");
        Started(runtime, Config(script));
        var expected = new List<string> { "--headless", "--stdio", "--no-auto-update", "--log-level", "error" };
        expected.AddRange(MCPSessionOverrides.CopilotArguments());
        Assert.Equal(expected, script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-137")] // Services/Providers/CopilotSession.swift:6,484-491
    public void Every_frame_is_Content_Length_framed_and_carries_jsonrpc_2_0()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("framing");
        Started(runtime, Config(script));
        runtime.Loop.RunUntil(() => script.Methods().Contains("session.commands.list"), Bound);
        // The stand-in only records frames it parsed from a Content-Length header.
        var messages = script.Messages();
        Assert.Equal(["ping", "session.create", "session.commands.list"], messages.Select(m => m["method"]!.JSONValueString!).ToList());
        Assert.All(messages, m => Assert.Equal("2.0", m["jsonrpc"]!.JSONValueString));
    }

    // ---- PR-PROVCLI-138 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-138")] // Services/Providers/CopilotSession.swift:494-499
    public void The_handshake_is_one_ping_with_the_message_marrow()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("ping");
        Started(runtime, Config(script));
        var ping = script.Messages()[0];
        Assert.Equal("ping", ping["method"]!.JSONValueString);
        Assert.Equal(["message"], Keys(Params(ping)));
        Assert.Equal("nth", Params(ping)["message"]!.JSONValueString);
        Assert.Single(script.Requests("ping"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-138")] // Services/Providers/CopilotSession.swift:495; Core/Support/JSONValue.swift:83-90
    public void Any_protocolVersion_that_reads_as_an_Int_is_accepted_and_its_value_is_never_compared()
    {
        foreach (var version in new[] { "99", "0", "\"3\"" })
        {
            using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
            var script = new CopilotScript("version");
            script.Reply("ping", "{\"protocolVersion\":" + version + "}");
            var (session, _, _) = Started(runtime, Config(script));
            Assert.True(session.IsRunning, version);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-138")] // Services/Providers/CopilotSession.swift:495-498
    public void A_ping_reply_without_an_integer_protocolVersion_closes_the_connection_and_fails_with_the_update_hint()
    {
        foreach (var reply in new[] { "{}", "{\"protocolVersion\":\"three\"}", "{\"protocolVersion\":true}", "{\"protocolVersion\":null}" })
        {
            using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
            var script = new CopilotScript("no-version");
            script.Reply("ping", reply);
            var (session, _) = Make(Config(script));
            var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));
            Assert.Equal("This `copilot` does not speak the SDK protocol. Update it with `copilot update`.", error.ErrorDescription);
            Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
            Assert.False(session.IsRunning, reply);
            Assert.Equal(["ping"], script.Methods());
        }
    }

    // ---- PR-PROVCLI-139 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-139")] // Services/Providers/CopilotSession.swift:104-109,114
    public void Without_a_resume_id_start_creates_a_session_under_a_fresh_lowercased_UUID_and_returns_it()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("create");
        var (session, _, id) = Started(runtime, Config(script));
        Assert.Matches(new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"), id);
        var create = Assert.Single(script.Requests("session.create"));
        Assert.Equal(id, Params(create)["sessionId"]!.JSONValueString);
        Assert.Empty(script.Requests("session.resume"));
        Assert.Equal(id, Field<string>(session, "sessionID"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-139")] // Services/Providers/CopilotSession.swift:104-109,114
    public void With_a_resume_id_start_resumes_that_id_with_the_same_parameters()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var fresh = new CopilotScript("create-shape");
        Started(runtime, Config(fresh));
        var script = new CopilotScript("resume");
        var (_, _, id) = Started(runtime, Config(script, resumeID: "5c0ffee0-aaaa-bbbb-cccc-000000000001"));
        Assert.Equal("5c0ffee0-aaaa-bbbb-cccc-000000000001", id);
        var resume = Assert.Single(script.Requests("session.resume"));
        Assert.Empty(script.Requests("session.create"));
        Assert.Equal("5c0ffee0-aaaa-bbbb-cccc-000000000001", Params(resume)["sessionId"]!.JSONValueString);
        Assert.Equal(Keys(Params(fresh.Requests("session.create").Single())), Keys(Params(resume)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-139")] // Services/Providers/CopilotSession.swift:107-108
    public void A_resume_the_CLI_refuses_throws_out_of_start()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("resume-refused");
        script.Error("session.resume", -32000, "Session not found");
        var (session, _) = Make(Config(script, resumeID: "gone"));
        var error = Assert.Throws<RPCError>(() => runtime.Loop.Run(() => session.Start(), Bound));
        Assert.Equal("Session not found", error.Message);
        Assert.Null(typeof(CopilotSession).GetField("sessionID", Instance)!.GetValue(session));
    }

    // ---- PR-PROVCLI-140, 141, 142 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-140")] // Services/Providers/CopilotSession.swift:121-131
    public void The_session_parameters_carry_the_eight_base_fields()
    {
        using var runtime = new TestRuntime();
        var configuration = Config(null);
        var (session, _) = Make(configuration);
        var parameters = Parameters(session);
        Assert.Equal("nth", parameters["clientName"].JSONValueString);
        Assert.Equal(configuration.WorkingDirectory.Path, parameters["workingDirectory"].JSONValueString);
        Assert.Equal(true, parameters["streaming"].JSONValueBool);
        Assert.Equal(true, parameters["requestPermission"].JSONValueBool);
        Assert.Equal(true, parameters["requestUserInput"].JSONValueBool);
        Assert.Equal(true, parameters["requestExitPlanMode"].JSONValueBool);
        Assert.Equal(false, parameters["includeSubAgentStreamingEvents"].JSONValueBool);
        Assert.Equal(false, parameters["isExperimentalMode"].JSONValueBool);
        // The eight, plus the system message that is always there (:149); no model, effort or agents.
        Assert.Equal(
            ["clientName", "includeSubAgentStreamingEvents", "isExperimentalMode", "requestExitPlanMode", "requestPermission", "requestUserInput", "streaming", "systemMessage", "workingDirectory"],
            parameters.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-140")] // Services/Providers/CopilotSession.swift:105-108,121-131
    public void The_create_request_sends_the_parameters_plus_sessionId()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("create-params");
        var configuration = Config(script);
        var (_, _, id) = Started(runtime, configuration);
        var sent = Params(script.Requests("session.create").Single());
        Assert.Equal("nth", sent["clientName"]!.JSONValueString);
        Assert.Equal(configuration.WorkingDirectory.Path, sent["workingDirectory"]!.JSONValueString);
        Assert.Equal(true, sent["streaming"]!.JSONValueBool);
        Assert.Equal(true, sent["requestPermission"]!.JSONValueBool);
        Assert.Equal(true, sent["requestUserInput"]!.JSONValueBool);
        Assert.Equal(true, sent["requestExitPlanMode"]!.JSONValueBool);
        Assert.Equal(false, sent["includeSubAgentStreamingEvents"]!.JSONValueBool);
        Assert.Equal(false, sent["isExperimentalMode"]!.JSONValueBool);
        Assert.Equal(id, sent["sessionId"]!.JSONValueString);
        Assert.Equal(
            ["clientName", "includeSubAgentStreamingEvents", "isExperimentalMode", "requestExitPlanMode", "requestPermission", "requestUserInput", "sessionId", "streaming", "systemMessage", "workingDirectory"],
            Keys(sent));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-140")] // Services/Providers/CopilotSession.swift:129-130
    [Trait("Req", "PR-PROVCLI-141")] // Services/Providers/CopilotSession.swift:117-120,130
    public void Experimental_mode_is_on_only_for_a_session_configured_in_Auto()
    {
        using var runtime = new TestRuntime();
        foreach (var mode in new[] { RuntimeMode.Supervised, RuntimeMode.AutoAcceptEdits, RuntimeMode.Auto, RuntimeMode.FullAccess })
        {
            var (session, _) = Make(Config(null, runtimeMode: mode));
            Assert.Equal(mode == RuntimeMode.Auto, Parameters(session)["isExperimentalMode"].JSONValueBool);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-141")] // Services/Providers/CopilotSession.swift:117-120,130,328-341
    public void Experimental_mode_follows_the_configuration_never_the_live_runtime_mode()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Make(Config(null, runtimeMode: RuntimeMode.Supervised));
        SetField(session, "runtimeMode", RuntimeMode.Auto);
        Assert.Equal(false, Parameters(session)["isExperimentalMode"].JSONValueBool);
        var (auto, _) = Make(Config(null, runtimeMode: RuntimeMode.Auto));
        SetField(auto, "runtimeMode", RuntimeMode.Supervised);
        Assert.Equal(true, Parameters(auto)["isExperimentalMode"].JSONValueBool);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-141")] // Services/Providers/CopilotSession.swift:117-120,160-162,328-341
    public void Switching_a_live_session_into_Auto_changes_only_the_permission_mode_over_the_wire()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("into-auto");
        script.Reply("session.permissions.setMode", "{\"mode\":\"assisted\"}");
        var (session, _, _) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("hi", runtimeMode: RuntimeMode.Auto)), Bound);
        var setMode = Assert.Single(script.Requests("session.permissions.setMode"));
        Assert.Equal("assisted", Params(setMode)["mode"]!.JSONValueString);
        // isExperimentalMode is fixed at session.create (:130) from the configuration and is not part of applyModes
        // (:328-341, PR-PROVCLI-141): it legitimately carries the pre-switch value (false) on session.create itself,
        // and must not reappear on any later wire message such as setMode.
        Assert.All(script.Messages().Where(m => m["method"]?.JSONValueString != "session.create"),
            m => Assert.Null(m["params"]?["isExperimentalMode"]));
        Assert.Single(script.Requests("session.create"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-142")] // Services/Providers/CopilotSession.swift:78-79,132-133
    public void Model_and_reasoningEffort_are_sent_at_creation_only_when_non_empty()
    {
        using var runtime = new TestRuntime();
        var (both, _) = Make(Config(null, model: "gpt-5", effort: "high"));
        Assert.Equal("gpt-5", Parameters(both)["model"].JSONValueString);
        Assert.Equal("high", Parameters(both)["reasoningEffort"].JSONValueString);
        var (empty, _) = Make(Config(null, model: "", effort: ""));
        Assert.False(Parameters(empty).ContainsKey("model"));
        Assert.False(Parameters(empty).ContainsKey("reasoningEffort"));
        var (none, _) = Make(Config(null));
        Assert.False(Parameters(none).ContainsKey("model"));
        Assert.False(Parameters(none).ContainsKey("reasoningEffort"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-142")] // Services/Providers/CopilotSession.swift:47-48,132-133,357-358,366
    public void The_parameters_read_the_stored_model_and_effort_that_applySelection_keeps()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Make(Config(null, model: "gpt-5", effort: "high"));
        SetField(session, "model", "claude-sonnet-4.5");
        SetField(session, "effort", "low");
        Assert.Equal("claude-sonnet-4.5", Parameters(session)["model"].JSONValueString);
        Assert.Equal("low", Parameters(session)["reasoningEffort"].JSONValueString);
    }

    // ---- PR-PROVCLI-143, 144 ----

    private static HydraLaunch Hydra(bool native) => new()
    {
        HeadsProvider = native ? ProviderKind.Copilot : ProviderKind.Codex,
        RunsNatively = native,
        WorkerModel = "gpt-5",
        WorkerEffort = "medium",
        MaxHeads = 3,
    };

    [Fact]
    [Trait("Req", "PR-PROVCLI-143")] // Services/Providers/CopilotSession.swift:129,134-141,149-156
    public void Native_Hydra_defines_the_custom_agents_streams_sub_agents_and_appends_the_Copilot_policy()
    {
        using var runtime = new TestRuntime();
        var hydra = Hydra(native: true);
        var (session, _) = Make(Config(null, hydra: hydra, antiSlopEnabled: true));
        var parameters = Parameters(session);
        Assert.Equal(new JSONValue.Array(HydraPrompts.CopilotAgents(hydra)), parameters["customAgents"]);
        Assert.Equal(true, parameters["includeSubAgentStreamingEvents"].JSONValueBool);
        Assert.Equal(HydraPrompts.Policy(ProviderKind.Copilot, hydra) + "\n\n" + AntiSlopPolicy.Instructions(true),
            parameters["systemMessage"]["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-143")] // Services/Providers/CopilotSession.swift:129,142-145,149-156
    public void A_cross_provider_Hydra_pair_defines_no_agents_and_appends_the_fallback_policy()
    {
        using var runtime = new TestRuntime();
        var hydra = Hydra(native: false);
        var (session, _) = Make(Config(null, hydra: hydra, antiSlopEnabled: false));
        var parameters = Parameters(session);
        Assert.False(parameters.ContainsKey("customAgents"));
        Assert.Equal(false, parameters["includeSubAgentStreamingEvents"].JSONValueBool);
        Assert.Equal(HydraPrompts.FallbackPolicy(hydra) + "\n\n" + AntiSlopPolicy.Instructions(false),
            parameters["systemMessage"]["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-144")] // Services/Providers/CopilotSession.swift:147-157; Core/Models/AntiSlopPolicy.swift:16-18
    public void Every_session_appends_exactly_one_system_message_holding_the_anti_slop_notice_when_Hydra_is_off()
    {
        using var runtime = new TestRuntime();
        foreach (var enabled in new[] { true, false })
        {
            var (session, _) = Make(Config(null, antiSlopEnabled: enabled));
            var message = Parameters(session)["systemMessage"];
            Assert.Equal(["content", "mode"], Keys(message));
            Assert.Equal("append", message["mode"]!.JSONValueString);
            // With Hydra off the only piece is AntiSlopPolicy.instructions(enabled:), which is never nil: the off
            // notice goes out too (:147-148), so a resumed thread cannot keep earlier guidance alive.
            Assert.Equal(enabled ? AntiSlopPolicy.EnabledInstructions : AntiSlopPolicy.DisabledInstructions, message["content"]!.JSONValueString);
        }
    }

    // ---- PR-PROVCLI-145, 146, 147, 148 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-145")] // Services/Providers/CopilotSession.swift:108-114,328-348,382-385
    public void Start_applies_the_thread_modes_after_create_and_then_loads_the_commands()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("modes-after-create");
        script.Reply("session.permissions.setMode", "{\"mode\":\"allow-all\"}");
        Started(runtime, Config(script, runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Plan));
        runtime.Loop.RunUntil(() => script.Methods().Contains("session.commands.list"), Bound);
        Assert.Equal(["ping", "session.create", "session.permissions.setMode", "session.mode.set", "session.commands.list"], script.Methods());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-145")] // Services/Providers/CopilotSession.swift:108-114,328-348,382-385
    public void Start_applies_the_thread_modes_after_a_resume_too()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("modes-after-resume");
        script.Reply("session.permissions.setMode", "{\"mode\":\"allow-all\"}");
        var (_, _, id) = Started(runtime, Config(script, resumeID: "r-1", runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Plan));
        Assert.Equal("r-1", id);
        runtime.Loop.RunUntil(() => script.Methods().Contains("session.commands.list"), Bound);
        Assert.Equal(["ping", "session.resume", "session.permissions.setMode", "session.mode.set", "session.commands.list"], script.Methods());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-146")] // Services/Providers/CopilotSession.swift:46,330-341
    public void The_permission_mode_is_set_with_source_rpc_only_when_it_differs_and_the_reply_decides_what_holds()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("set-mode");
        // Refused: the reply says "manual" still holds.
        script.Reply("session.permissions.setMode", "{\"mode\":\"manual\"}", occurrence: 1);
        script.Reply("session.permissions.setMode", "{\"mode\":\"assisted\"}", occurrence: 2);
        var (session, _, id) = Started(runtime, Config(script));
        // Supervised wants "manual", which the session already holds: no call at start.
        Assert.Empty(script.Requests("session.permissions.setMode"));

        runtime.Loop.Run(() => session.Send(Turn("one", runtimeMode: RuntimeMode.Auto)), Bound);
        var first = Assert.Single(script.Requests("session.permissions.setMode"));
        Assert.Equal(["mode", "sessionId", "source"], Keys(Params(first)));
        Assert.Equal(id, Params(first)["sessionId"]!.JSONValueString);
        Assert.Equal("assisted", Params(first)["mode"]!.JSONValueString);
        Assert.Equal("rpc", Params(first)["source"]!.JSONValueString);
        Assert.Equal("manual", Field<string>(session, "permissionMode"));
        Assert.Equal(RuntimeMode.Auto, Field<RuntimeMode>(session, "runtimeMode"));

        // Still "manual", so the next turn asks again, and this time the reply confirms the change.
        runtime.Loop.Run(() => session.Send(Turn("two", runtimeMode: RuntimeMode.Auto)), Bound);
        Assert.Equal(2, script.Requests("session.permissions.setMode").Count);
        Assert.Equal("assisted", Field<string>(session, "permissionMode"));

        runtime.Loop.Run(() => session.Send(Turn("three", runtimeMode: RuntimeMode.Auto)), Bound);
        Assert.Equal(2, script.Requests("session.permissions.setMode").Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-146")] // Services/Providers/CopilotSession.swift:335-340
    public void A_failed_or_empty_setMode_reply_keeps_the_previous_permission_mode()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var failing = new CopilotScript("set-mode-error");
        failing.Error("session.permissions.setMode", -32602, "no");
        var (session, _, _) = Started(runtime, Config(failing));
        runtime.Loop.Run(() => session.Send(Turn("one", runtimeMode: RuntimeMode.FullAccess)), Bound);
        Assert.Equal("manual", Field<string>(session, "permissionMode"));

        var empty = new CopilotScript("set-mode-empty");
        var (other, _, _) = Started(runtime, Config(empty));
        runtime.Loop.Run(() => other.Send(Turn("one", runtimeMode: RuntimeMode.FullAccess)), Bound);
        Assert.Equal("manual", Field<string>(other, "permissionMode"));
        Assert.Equal("allow-all", Params(empty.Requests("session.permissions.setMode").Single())["mode"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-147")] // Services/Providers/CopilotSession.swift:371-380
    public void Permission_mode_names_map_the_four_runtime_modes_to_three_strings()
    {
        Assert.Equal("manual", CopilotSession.PermissionModeName(RuntimeMode.Supervised));
        Assert.Equal("manual", CopilotSession.PermissionModeName(RuntimeMode.AutoAcceptEdits));
        Assert.Equal("assisted", CopilotSession.PermissionModeName(RuntimeMode.Auto));
        Assert.Equal("allow-all", CopilotSession.PermissionModeName(RuntimeMode.FullAccess));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-148")] // Services/Providers/CopilotSession.swift:44,342-348
    public void The_interaction_mode_is_set_to_plan_or_interactive_and_advances_even_when_the_call_fails()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("mode-set");
        script.Error("session.mode.set", -32602, "no");
        var (session, _, id) = Started(runtime, Config(script));
        Assert.Equal(InteractionMode.Build, Field<InteractionMode>(session, "interactionMode"));
        Assert.Empty(script.Requests("session.mode.set"));

        runtime.Loop.Run(() => session.Send(Turn("plan it", interactionMode: InteractionMode.Plan)), Bound);
        var toPlan = Assert.Single(script.Requests("session.mode.set"));
        Assert.Equal(["mode", "sessionId"], Keys(Params(toPlan)));
        Assert.Equal(id, Params(toPlan)["sessionId"]!.JSONValueString);
        Assert.Equal("plan", Params(toPlan)["mode"]!.JSONValueString);
        Assert.Equal(InteractionMode.Plan, Field<InteractionMode>(session, "interactionMode"));

        runtime.Loop.Run(() => session.Send(Turn("again", interactionMode: InteractionMode.Plan)), Bound);
        Assert.Single(script.Requests("session.mode.set"));

        runtime.Loop.Run(() => session.Send(Turn("build it", interactionMode: InteractionMode.Build)), Bound);
        Assert.Equal("interactive", Params(script.Requests("session.mode.set")[1])["mode"]!.JSONValueString);
        Assert.Equal(InteractionMode.Build, Field<InteractionMode>(session, "interactionMode"));
    }

    // ---- PR-PROVCLI-149 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-149")] // Services/Providers/CopilotSession.swift:351-360,361-368
    public void A_model_switch_carries_the_effort_and_makes_the_separate_effort_call_unnecessary()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("switch-model");
        var (session, _, id) = Started(runtime, Config(script, model: "gpt-5", effort: "low"));
        runtime.Loop.Run(() => session.Send(Turn("hi", model: "claude-sonnet-4.5", effort: "high")), Bound);
        var switched = Assert.Single(script.Requests("session.model.switchTo"));
        Assert.Equal(["modelId", "reasoningEffort", "sessionId"], Keys(Params(switched)));
        Assert.Equal(id, Params(switched)["sessionId"]!.JSONValueString);
        Assert.Equal("claude-sonnet-4.5", Params(switched)["modelId"]!.JSONValueString);
        Assert.Equal("high", Params(switched)["reasoningEffort"]!.JSONValueString);
        Assert.Empty(script.Requests("session.model.setReasoningEffort"));
        Assert.Equal("claude-sonnet-4.5", Field<string>(session, "model"));
        Assert.Equal("high", Field<string>(session, "effort"));

        // The same selection again changes nothing.
        runtime.Loop.Run(() => session.Send(Turn("again", model: "claude-sonnet-4.5", effort: "high")), Bound);
        Assert.Single(script.Requests("session.model.switchTo"));
        Assert.Empty(script.Requests("session.model.setReasoningEffort"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-149")] // Services/Providers/CopilotSession.swift:353-356,361-367
    public void An_effort_change_alone_is_its_own_call_and_an_empty_model_or_effort_is_never_sent()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("set-effort");
        var (session, _, id) = Started(runtime, Config(script, model: "gpt-5", effort: "low"));
        runtime.Loop.Run(() => session.Send(Turn("hi", model: "gpt-5", effort: "high")), Bound);
        Assert.Empty(script.Requests("session.model.switchTo"));
        var effort = Assert.Single(script.Requests("session.model.setReasoningEffort"));
        Assert.Equal(["reasoningEffort", "sessionId"], Keys(Params(effort)));
        Assert.Equal(id, Params(effort)["sessionId"]!.JSONValueString);
        Assert.Equal("high", Params(effort)["reasoningEffort"]!.JSONValueString);
        Assert.Equal("high", Field<string>(session, "effort"));

        runtime.Loop.Run(() => session.Send(Turn("empty", model: "", effort: "")), Bound);
        runtime.Loop.Run(() => session.Send(Turn("nil")), Bound);
        Assert.Empty(script.Requests("session.model.switchTo"));
        Assert.Single(script.Requests("session.model.setReasoningEffort"));

        // A model switch with no effort sends no reasoningEffort key.
        runtime.Loop.Run(() => session.Send(Turn("model only", model: "o3")), Bound);
        Assert.Equal(["modelId", "sessionId"], Keys(Params(script.Requests("session.model.switchTo").Single())));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-149")] // Services/Providers/CopilotSession.swift:356-358,362-367
    public void A_failed_switch_leaves_the_stored_values_so_the_next_turn_tries_again()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("switch-fails");
        script.Error("session.model.switchTo", -32602, "unknown model");
        script.Error("session.model.setReasoningEffort", -32602, "unknown effort");
        var (session, _, _) = Started(runtime, Config(script, model: "gpt-5", effort: "low"));
        runtime.Loop.Run(() => session.Send(Turn("hi", model: "nope", effort: "high")), Bound);
        Assert.Single(script.Requests("session.model.switchTo"));
        // The switch failed, so the effort still differs and gets its own (also failing) call.
        Assert.Single(script.Requests("session.model.setReasoningEffort"));
        Assert.Equal("gpt-5", Field<string>(session, "model"));
        Assert.Equal("low", Field<string>(session, "effort"));
        runtime.Loop.Run(() => session.Send(Turn("again", model: "nope", effort: "high")), Bound);
        Assert.Equal(2, script.Requests("session.model.switchTo").Count);
        Assert.Equal(2, script.Requests("session.model.setReasoningEffort").Count);
    }

    // ---- PR-PROVCLI-150 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-150")] // Services/Providers/CopilotSession.swift:229-236
    public void SlashCommand_reads_a_leading_command_name_and_the_trimmed_rest()
    {
        Assert.Equal(("review", "this file"), CopilotSession.SlashCommand("/review this file"));
        Assert.Equal(("review", ""), CopilotSession.SlashCommand("  /review  \n"));
        Assert.Equal(("add-dir", "a\n  b"), CopilotSession.SlashCommand("/add-dir\n  a\n  b  "));
        Assert.Equal(("x_1-y", ""), CopilotSession.SlashCommand("/x_1-y"));
        Assert.Null(CopilotSession.SlashCommand("review this"));
        Assert.Null(CopilotSession.SlashCommand("/1review"));
        Assert.Null(CopilotSession.SlashCommand("/-review"));
        Assert.Null(CopilotSession.SlashCommand("/review/this"));
        Assert.Null(CopilotSession.SlashCommand("/"));
        Assert.Null(CopilotSession.SlashCommand(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-150")] // Services/Providers/CopilotSession.swift:161
    public void Send_without_a_live_connection_throws_notRunning_before_anything_happens()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Send(Turn("hi")), Bound));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Equal("The agent session is not running.", error.ErrorDescription);
        Assert.Empty(events);
        Assert.False(Field<bool>(session, "turnActive"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-150")] // Services/Providers/CopilotSession.swift:160-172,382-395
    public void A_loaded_slash_command_is_invoked_and_any_other_text_is_sent_whole_as_a_prompt()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("route-command");
        script.Reply("session.commands.list", "{\"commands\":[{\"name\":\"review\",\"description\":\"Review changes\"}]}");
        script.Reply("session.commands.invoke", "{\"kind\":\"completed\"}");
        var (session, events, id) = Started(runtime, Config(script));
        runtime.Loop.RunUntil(() => events.OfType<ProviderEvent.Commands>().Any(), Bound);

        runtime.Loop.Run(() => session.Send(Turn("  /review the diff ")), Bound);
        var invoke = Assert.Single(script.Requests("session.commands.invoke"));
        Assert.Equal(["input", "name", "sessionId"], Keys(Params(invoke)));
        Assert.Equal(id, Params(invoke)["sessionId"]!.JSONValueString);
        Assert.Equal("review", Params(invoke)["name"]!.JSONValueString);
        Assert.Equal("the diff", Params(invoke)["input"]!.JSONValueString);
        Assert.Empty(script.Requests("session.send"));

        runtime.Loop.Run(() => session.Send(Turn(" /unknown x ")), Bound);
        var send = Assert.Single(script.Requests("session.send"));
        Assert.Equal(" /unknown x ", Params(send)["prompt"]!.JSONValueString);
        Assert.Single(script.Requests("session.commands.invoke"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-150")] // Services/Providers/CopilotSession.swift:162-164,173-176
    public void Modes_and_selection_go_out_before_the_prompt_and_a_failed_send_clears_the_turn_and_throws()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("send-fails");
        script.Error("session.send", -32603, "boom");
        var (session, events, _) = Started(runtime, Config(script));
        var error = Assert.Throws<RPCError>(() => runtime.Loop.Run(() => session.Send(Turn("hi", RuntimeMode.FullAccess, InteractionMode.Plan, model: "o3")), Bound));
        Assert.Equal("boom", error.Message);
        Assert.False(Field<bool>(session, "turnActive"));
        Assert.Single(events.OfType<ProviderEvent.TurnStarted>());
        var methods = script.Methods().SkipWhile(m => m != "session.permissions.setMode").Where(m => m != "session.commands.list").ToList();
        Assert.Equal(["session.permissions.setMode", "session.mode.set", "session.model.switchTo", "session.send"], methods);
    }

    // ---- PR-PROVCLI-151 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-151")] // Services/Providers/CopilotSession.swift:179-186
    public void BeginTurn_clears_the_per_turn_state_and_announces_a_turn_with_no_provider_id()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(null));
        SetField(session, "turnError", "earlier");
        SetField(session, "interruptRequested", true);
        ((HashSet<string>)typeof(CopilotSession).GetField("shownReasoning", Instance)!.GetValue(session)!).Add("seen");
        ((HashSet<string>)typeof(CopilotSession).GetField("streamedMessages", Instance)!.GetValue(session)!).Add("m1");
        runtime.OnMain(() => Invoke(session, "BeginTurn"));
        Assert.True(Field<bool>(session, "turnActive"));
        Assert.Null(typeof(CopilotSession).GetField("turnError", Instance)!.GetValue(session));
        Assert.False(Field<bool>(session, "interruptRequested"));
        Assert.Empty(Field<HashSet<string>>(session, "shownReasoning"));
        Assert.Empty(Field<HashSet<string>>(session, "streamedMessages"));
        var started = Assert.IsType<ProviderEvent.TurnStarted>(Assert.Single(events));
        Assert.Null(started.ProviderTurnID);
    }

    // ---- PR-PROVCLI-152 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-152")] // Services/Providers/CopilotSession.swift:188-200
    public void A_prompt_carries_only_image_attachments_as_file_references()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("attachments");
        var (session, _, id) = Started(runtime, Config(script));
        var images = new ValueList<Attachment>([
            new Attachment { Name = "shot.png", Path = @"C:\Users\me\shot.png", MimeType = "image/png" },
            new Attachment { Name = "notes.txt", Path = @"C:\Users\me\notes.txt", MimeType = "text/plain" },
            new Attachment { Name = "photo.jpg", Path = @"C:\Users\me\photo.jpg", MimeType = "image/jpeg" },
        ]);
        runtime.Loop.Run(() => session.Send(Turn("look", images: images)), Bound);
        var sent = Params(script.Requests("session.send").Single());
        Assert.Equal(["attachments", "prompt", "sessionId"], Keys(sent));
        Assert.Equal(id, sent["sessionId"]!.JSONValueString);
        Assert.Equal("look", sent["prompt"]!.JSONValueString);
        var attachments = sent["attachments"]!.JSONValueArray!;
        Assert.Equal(2, attachments.Count);
        Assert.Equal(["displayName", "mimeType", "path", "type"], Keys(attachments[0]));
        Assert.Equal("file", attachments[0]["type"]!.JSONValueString);
        Assert.Equal(@"C:\Users\me\shot.png", attachments[0]["path"]!.JSONValueString);
        Assert.Equal("shot.png", attachments[0]["displayName"]!.JSONValueString);
        Assert.Equal("image/png", attachments[0]["mimeType"]!.JSONValueString);
        Assert.Equal(@"C:\Users\me\photo.jpg", attachments[1]["path"]!.JSONValueString);
        Assert.Equal("image/jpeg", attachments[1]["mimeType"]!.JSONValueString);

        var textOnly = new ValueList<Attachment>([new Attachment { Name = "a.txt", Path = @"C:\a.txt", MimeType = "text/plain" }]);
        runtime.Loop.Run(() => session.Send(Turn("plain", images: textOnly)), Bound);
        Assert.Equal(["prompt", "sessionId"], Keys(Params(script.Requests("session.send")[1])));
    }

    // ---- PR-PROVCLI-153 ----

    private static (CopilotSession Session, List<ProviderEvent> Events, CopilotScript Script) CommandSession(TestRuntime runtime, string label, string invokeReply)
    {
        var script = new CopilotScript(label);
        script.Reply("session.commands.list", "{\"commands\":[{\"name\":\"review\"}]}");
        script.Reply("session.commands.invoke", invokeReply);
        var (session, events, _) = Started(runtime, Config(script));
        runtime.Loop.RunUntil(() => events.OfType<ProviderEvent.Commands>().Any(), Bound);
        events.Clear();
        return (session, events, script);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-153")] // Services/Providers/CopilotSession.swift:209-222
    public void A_text_reply_strips_ANSI_fences_the_text_follows_the_reported_mode_and_finishes_the_turn()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, events, _) = CommandSession(runtime, "command-text",
            "{\"kind\":\"text\",\"mode\":\"plan\",\"text\":\"\\u001b[1mUsage\\u001b[0m: 42\"}");
        runtime.Loop.Run(() => session.Send(Turn("/review")), Bound);
        Assert.IsType<ProviderEvent.TurnStarted>(events[0]);
        Assert.Equal(new ProviderEvent.ModeChanged(InteractionMode.Plan), events[1]);
        var message = Assert.IsType<ProviderEvent.MessageCompleted>(events[2]);
        Assert.Matches(new Regex("^command-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$"), message.Id);
        Assert.Equal("```\nUsage: 42\n```", message.Text);
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), events[3]);
        Assert.Equal(4, events.Count);
        Assert.Equal(InteractionMode.Plan, Field<InteractionMode>(session, "interactionMode"));
        Assert.False(Field<bool>(session, "turnActive"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-153")] // Services/Providers/CopilotSession.swift:209-222
    public void A_completed_markdown_reply_shows_the_message_raw_and_any_other_mode_is_build()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, events, _) = CommandSession(runtime, "command-markdown",
            "{\"kind\":\"completed\",\"mode\":\"autopilot\",\"markdown\":true,\"message\":\"**Done**\"}");
        runtime.Loop.Run(() => session.Send(Turn("/review", interactionMode: InteractionMode.Build)), Bound);
        Assert.Equal(new ProviderEvent.ModeChanged(InteractionMode.Build), events[1]);
        Assert.Equal("**Done**", Assert.IsType<ProviderEvent.MessageCompleted>(events[2]).Text);
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), events[3]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-153")] // Services/Providers/CopilotSession.swift:216-222
    public void A_blank_text_reply_shows_no_message_but_still_finishes_the_turn()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, events, _) = CommandSession(runtime, "command-blank", "{\"kind\":\"text\",\"text\":\" \\n \"}");
        runtime.Loop.Run(() => session.Send(Turn("/review")), Bound);
        Assert.Equal(2, events.Count);
        Assert.IsType<ProviderEvent.TurnStarted>(events[0]);
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), events[1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-153")] // Services/Providers/CopilotSession.swift:214-215
    public void An_agent_prompt_reply_sends_its_prompt_or_else_the_command_input_with_no_images()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, _, script) = CommandSession(runtime, "command-prompt", "{\"kind\":\"agent-prompt\",\"prompt\":\"Review the staged diff\"}");
        var images = new ValueList<Attachment>([new Attachment { Name = "a.png", Path = @"C:\a.png", MimeType = "image/png" }]);
        runtime.Loop.Run(() => session.Send(Turn("/review now", images: images)), Bound);
        var sent = Params(script.Requests("session.send").Single());
        Assert.Equal("Review the staged diff", sent["prompt"]!.JSONValueString);
        Assert.Null(sent["attachments"]);
        Assert.True(Field<bool>(session, "turnActive"));

        var (other, _, otherScript) = CommandSession(runtime, "command-prompt-input", "{\"kind\":\"agent-prompt\"}");
        runtime.Loop.Run(() => other.Send(Turn("/review the tests")), Bound);
        Assert.Equal("the tests", Params(otherScript.Requests("session.send").Single())["prompt"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-153")] // Services/Providers/CopilotSession.swift:223-225
    public void Any_other_reply_kind_warns_that_only_Copilot_can_show_the_dialog_and_finishes_the_turn()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, events, _) = CommandSession(runtime, "command-dialog", "{\"kind\":\"select\"}");
        runtime.Loop.Run(() => session.Send(Turn("/review")), Bound);
        var notice = Assert.IsType<ProviderEvent.Notice>(events[1]).Value;
        Assert.Equal(Notice.Level.Warning, notice.NoticeLevel);
        Assert.Equal("/review opens a dialog that only Copilot's own terminal UI can show.", notice.Message);
        Assert.Equal(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null), events[2]);
        Assert.Equal(3, events.Count);
    }

    // ---- PR-PROVCLI-154 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-154")] // Services/Providers/CopilotSession.swift:71-73,382-397
    public void The_command_list_skips_the_ten_own_commands_and_recognizes_names_and_aliases()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("commands");
        var own = new[] { "allow-all", "yolo", "permissions", "model", "models", "plan", "session", "sessions", "cwd", "cd" };
        var entries = own.Select(n => $"{{\"name\":\"{n}\",\"aliases\":[\"{n}-alias\"]}}").ToList();
        entries.Insert(3, "{\"name\":\"review\",\"description\":\"Review changes\",\"aliases\":[\"r\",7,\"rv\"]}");
        entries.Add("{\"name\":\"help\"}");
        entries.Add("{\"description\":\"no name\",\"aliases\":[\"ghost\"]}");
        script.Reply("session.commands.list", "{\"commands\":[" + string.Join(",", entries) + "]}");
        var (session, events, id) = Started(runtime, Config(script));
        runtime.Loop.RunUntil(() => events.OfType<ProviderEvent.Commands>().Any(), Bound);
        var list = Assert.Single(script.Requests("session.commands.list"));
        Assert.Equal(["sessionId"], Keys(Params(list)));
        Assert.Equal(id, Params(list)["sessionId"]!.JSONValueString);
        var commands = Assert.Single(events.OfType<ProviderEvent.Commands>()).Value;
        Assert.Equal(2, commands.Count);
        Assert.Equal(("review", "Review changes", false, (string?)null), (commands[0].Name, commands[0].Detail, commands[0].IsBuiltIn, commands[0].SkillPath));
        Assert.Equal(("help", "", false, (string?)null), (commands[1].Name, commands[1].Detail, commands[1].IsBuiltIn, commands[1].SkillPath));
        Assert.Equal(["help", "r", "review", "rv"], Field<HashSet<string>>(session, "commandNames").OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-154")] // Services/Providers/CopilotSession.swift:385,394-395
    public void A_failed_or_empty_command_list_publishes_nothing_and_slash_text_goes_out_as_a_prompt()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var failing = new CopilotScript("commands-error");
        failing.Error("session.commands.list", -32601, "no");
        var (session, events, _) = Started(runtime, Config(failing));
        runtime.Loop.RunUntil(() => failing.Methods().Contains("session.commands.list"), Bound);
        runtime.RunUntilIdle();
        runtime.Loop.Run(() => session.Send(Turn("/review")), Bound);
        Assert.Empty(events.OfType<ProviderEvent.Commands>());
        Assert.Empty(Field<HashSet<string>>(session, "commandNames"));
        Assert.Equal("/review", Params(failing.Requests("session.send").Single())["prompt"]!.JSONValueString);

        var owned = new CopilotScript("commands-own-only");
        owned.Reply("session.commands.list", "{\"commands\":[{\"name\":\"model\"}]}");
        var (ownSession, ownEvents, _) = Started(runtime, Config(owned));
        runtime.Loop.RunUntil(() => owned.Methods().Contains("session.commands.list"), Bound);
        // Replies come back in order: once a later request is answered, the command list has been handled.
        runtime.Loop.Run(() => ownSession.Compact(), Bound);
        Assert.Empty(ownEvents.OfType<ProviderEvent.Commands>());
        Assert.Empty(Field<HashSet<string>>(ownSession, "commandNames"));
    }

    // ---- PR-PROVCLI-155, 156 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-155")] // Services/Providers/CopilotSession.swift:238-239
    public void Interrupt_does_nothing_without_an_active_turn_or_a_connection()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, events) = Make(Config(null));
        SetField(session, "turnActive", true);
        AddPending(session, "p1", Pending("Permission", JSONValue.FromDictionaryLiteral(("kind", "shell")), null));
        runtime.Loop.Run(() => session.Interrupt(), Bound);
        Assert.False(Field<bool>(session, "interruptRequested"));
        Assert.Single((IDictionary)typeof(CopilotSession).GetField("pendingRequests", Instance)!.GetValue(session)!);
        Assert.Empty(events);

        var script = new CopilotScript("interrupt-idle");
        var (idle, _, _) = Started(runtime, Config(script));
        runtime.Loop.Run(() => idle.Interrupt(), Bound);
        Assert.False(Field<bool>(idle, "interruptRequested"));
        Assert.Empty(script.Requests("session.abort"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-155")] // Services/Providers/CopilotSession.swift:238-243
    public void Interrupt_marks_the_request_and_aborts_the_session_as_user_initiated_ignoring_a_failure()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("interrupt");
        script.Error("session.abort", -32603, "already idle");
        var (session, _, id) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("work")), Bound);
        runtime.Loop.Run(() => session.Interrupt(), Bound);
        Assert.True(Field<bool>(session, "interruptRequested"));
        var abort = Assert.Single(script.Requests("session.abort"));
        Assert.Equal(["reason", "sessionId"], Keys(Params(abort)));
        Assert.Equal(id, Params(abort)["sessionId"]!.JSONValueString);
        Assert.Equal("user_initiated", Params(abort)["reason"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-155")] // Services/Providers/CopilotSession.swift:240-242
    [Trait("Req", "PR-PROVCLI-156")] // Services/Providers/CopilotSession.swift:904-929
    public void Interrupt_cancels_each_pending_kind_with_its_own_reply_before_the_abort()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("cancel-pending");
        var (session, events, id) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("work")), Bound);
        events.Clear();
        AddPending(session, "perm-1", Pending("Permission", JSONValue.FromDictionaryLiteral(("kind", "shell")), "call-1"));
        AddPending(session, "question-41", Pending("Question", new RPCID.Int(41), new ValueList<string>(["a", "b"])));
        AddPending(session, "plan-p-9", Pending("PlanExit", new RPCID.String("p-9")));
        runtime.Loop.Run(() => session.Interrupt(), Bound);
        runtime.Loop.RunUntil(() => script.Requests("session.permissions.handlePendingPermissionRequest").Count == 1 && script.Responses().Count == 2 && script.Requests("session.abort").Count == 1, Bound);

        Assert.Empty((IDictionary)typeof(CopilotSession).GetField("pendingRequests", Instance)!.GetValue(session)!);
        Assert.Equal(["perm-1", "plan-p-9", "question-41"],
            events.OfType<ProviderEvent.RequestResolved>().Select(e => e.Id).OrderBy(k => k, StringComparer.Ordinal).ToList());
        Assert.Equal(3, events.Count);

        var permission = Params(script.Requests("session.permissions.handlePendingPermissionRequest").Single());
        Assert.Equal(["requestId", "result", "sessionId"], Keys(permission));
        Assert.Equal(id, permission["sessionId"]!.JSONValueString);
        Assert.Equal("perm-1", permission["requestId"]!.JSONValueString);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("kind", "cancelled"), ("reason", "The user stopped the turn.")), permission["result"]);

        var question = script.Responses().Single(r => r["id"]!.JSONValueInt == 41);
        Assert.Null(question["result"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("code", -32800L), ("message", "The user stopped the turn.")), question["error"]);

        var plan = script.Responses().Single(r => r["id"]!.JSONValueString == "p-9");
        Assert.Null(plan["error"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("approved", false), ("feedback", "The user stopped the turn.")), plan["result"]);

        // The replies go out before the abort (:241-242); the permission reply rides a detached task (:906).
        var frames = script.Messages();
        var abortIndex = frames.ToList().FindIndex(m => m["method"]?.JSONValueString == "session.abort");
        Assert.True(frames.Take(abortIndex).Count(m => m["method"] is null) == 2);
    }

    // ---- PR-PROVCLI-157 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-157")] // Services/Providers/CopilotSession.swift:245-246
    public void Compact_without_a_session_throws_notRunning()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Make(Config(null));
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Compact(), Bound));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-157")] // Services/Providers/CopilotSession.swift:245-248
    public void Compact_requests_a_manual_history_compaction_and_its_error_propagates()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("compact");
        script.Error("session.history.compact", -32603, "Nothing to compact");
        var (session, events, id) = Started(runtime, Config(script));
        var error = Assert.Throws<RPCError>(() => runtime.Loop.Run(() => session.Compact(), Bound));
        Assert.Equal("Nothing to compact", error.Message);
        var compact = Assert.Single(script.Requests("session.history.compact"));
        Assert.Equal(["sessionId", "trigger"], Keys(Params(compact)));
        Assert.Equal(id, Params(compact)["sessionId"]!.JSONValueString);
        Assert.Equal("manual", Params(compact)["trigger"]!.JSONValueString);
        Assert.Empty(events.OfType<ProviderEvent.Notice>());
    }

    // ---- PR-PROVCLI-158 ----

    private const string ThreePoints = "{\"points\":[{\"eventId\":\"e1\"},{\"eventId\":\"e2\"},{\"eventId\":\"e3\"}]}";

    [Fact]
    [Trait("Req", "PR-PROVCLI-158")] // Services/Providers/CopilotSession.swift:253
    public void Rollback_returns_silently_without_a_session_or_a_positive_turn_count()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var (session, _) = Make(Config(null));
        runtime.Loop.Run(() => session.Rollback(1), Bound);
        var script = new CopilotScript("rollback-zero");
        var (live, _, _) = Started(runtime, Config(script));
        runtime.Loop.Run(() => live.Rollback(0), Bound);
        runtime.Loop.Run(() => live.Rollback(-2), Bound);
        Assert.Empty(script.Requests("session.history.listRewindPoints"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-158")] // Services/Providers/CopilotSession.swift:250-267
    public void Rollback_rewinds_the_conversation_to_the_point_of_the_nth_last_user_message()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("rollback");
        script.Reply("session.history.listRewindPoints", ThreePoints);
        script.Reply("session.history.rewind", "{\"outcome\":\"success\"}");
        var (session, _, id) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Rollback(2), Bound);
        var list = Assert.Single(script.Requests("session.history.listRewindPoints"));
        Assert.Equal(["sessionId"], Keys(Params(list)));
        Assert.Equal(id, Params(list)["sessionId"]!.JSONValueString);
        var rewind = Assert.Single(script.Requests("session.history.rewind"));
        Assert.Equal(["eventId", "mode", "sessionId"], Keys(Params(rewind)));
        Assert.Equal(id, Params(rewind)["sessionId"]!.JSONValueString);
        Assert.Equal("e2", Params(rewind)["eventId"]!.JSONValueString);
        Assert.Equal("conversation", Params(rewind)["mode"]!.JSONValueString);

        runtime.Loop.Run(() => session.Rollback(3), Bound);
        Assert.Equal("e1", Params(script.Requests("session.history.rewind")[1])["eventId"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-158")] // Services/Providers/CopilotSession.swift:255-259
    public void Rollback_without_a_point_for_that_turn_fails_with_the_exact_message()
    {
        foreach (var points in new[] { ThreePoints, "{\"points\":[{\"eventId\":\"e1\"},{\"id\":\"e2\"}]}", "{}", "{\"points\":[{\"eventId\":7}]}" })
        {
            using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
            var script = new CopilotScript("rollback-missing");
            script.Reply("session.history.listRewindPoints", points);
            var (session, _, _) = Started(runtime, Config(script));
            var turns = points == ThreePoints ? 4 : 1;
            var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Rollback(turns), Bound));
            Assert.Equal(new ProviderError.Case.Failed("Copilot has no rewind point for that turn."), error.ErrorCase);
            Assert.Empty(script.Requests("session.history.rewind"));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-158")] // Services/Providers/CopilotSession.swift:265-267
    public void A_rewind_whose_outcome_is_not_success_fails_with_its_error_or_the_default_message()
    {
        foreach (var (reply, message) in new[]
        {
            ("{\"outcome\":\"failure\",\"error\":\"Event not found\"}", "Event not found"),
            ("{\"outcome\":\"Success\"}", "Copilot could not rewind the conversation."),
            ("{}", "Copilot could not rewind the conversation."),
        })
        {
            using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
            var script = new CopilotScript("rewind-fails");
            script.Reply("session.history.listRewindPoints", ThreePoints);
            script.Reply("session.history.rewind", reply);
            var (session, _, _) = Started(runtime, Config(script));
            var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Rollback(1), Bound));
            Assert.Equal(new ProviderError.Case.Failed(message), error.ErrorCase);
        }
    }
}
