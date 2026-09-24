using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Services.Tests.Fixtures.CopilotSessionHarness;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e). Part 2: PR-PROVCLI-159
// through 182 (approvals and answers, stop, the model catalog, auth and quota reads, and the session.event
// routing for messages, reasoning, usage, tools and sub-agents). Wire replies are proven against the offline
// stand-in tests/fixtures/copilot-cli; the handlers are driven directly on a session with no process otherwise.
public sealed class CopilotSessionSecondTests
{
    // ---- PR-PROVCLI-159 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-159")] // Services/Providers/CopilotSession.swift:270-287,302
    public void ResolveApproval_of_an_unknown_id_does_nothing()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        runtime.OnMain(() => session.ResolveApproval("nope", "allow"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-159")] // Services/Providers/CopilotSession.swift:280-287,302
    public void A_declined_permission_marks_its_tool_call_declined_before_resolving()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        AddPending(session, "req-1", Pending("Permission", Json("{\"kind\":\"shell\"}"), "call-7"));
        AddPending(session, "req-2", Pending("Permission", Json("{\"kind\":\"shell\"}"), null));
        runtime.OnMain(() => session.ResolveApproval("req-1", "deny"));
        Assert.Equal(2, events.Count);
        var updated = Assert.IsType<ProviderEvent.ToolUpdated>(events[0]);
        Assert.Equal("call-7", updated.Id);
        Assert.Equal(new ToolUpdate { Status = ToolCall.Status.Declined }, updated.Update);
        Assert.Equal(new ProviderEvent.RequestResolved("req-1"), events[1]);
        Assert.Contains("call-7", Field<HashSet<string>>(session, "declinedTools"));

        events.Clear();
        runtime.OnMain(() => session.ResolveApproval("req-2", "whatever"));
        Assert.Equal([new ProviderEvent.RequestResolved("req-2")], events);
        Assert.Single(Field<HashSet<string>>(session, "declinedTools"));
        Assert.Empty(PendingRequests(session));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-159")] // Services/Providers/CopilotSession.swift:270-287,904-913
    public void Allow_always_and_deny_send_three_different_decisions()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("approvals");
        var (session, events, id) = Started(runtime, Config(script));
        var shell = Json("{\"kind\":\"shell\",\"canOfferSessionApproval\":true,\"commands\":[{\"identifier\":\"git status\"}]}");
        AddPending(session, "r-allow", Pending("Permission", shell, "c1"));
        AddPending(session, "r-always", Pending("Permission", shell, "c2"));
        AddPending(session, "r-always-hook", Pending("Permission", Json("{\"kind\":\"hook\"}"), "c3"));
        AddPending(session, "r-deny", Pending("Permission", shell, "c4"));
        runtime.OnMain(() =>
        {
            session.ResolveApproval("r-allow", "allow");
            session.ResolveApproval("r-always", "always");
            session.ResolveApproval("r-always-hook", "always");
            session.ResolveApproval("r-deny", "deny");
        });
        const string method = "session.permissions.handlePendingPermissionRequest";
        runtime.Loop.RunUntil(() => script.Requests(method).Count == 4, Bound);
        var sent = script.Requests(method).Select(Params).ToDictionary(p => p["requestId"]!.JSONValueString!, p => p);
        Assert.All(sent.Values, p => Assert.Equal(["requestId", "result", "sessionId"], Keys(p)));
        Assert.All(sent.Values, p => Assert.Equal(id, p["sessionId"]!.JSONValueString));
        var approveOnce = Json("{\"kind\":\"approve-once\",\"approvedInteractively\":true}");
        Assert.Equal(approveOnce, sent["r-allow"]["result"]);
        Assert.Equal(Json("{\"kind\":\"approve-for-session\",\"approval\":{\"kind\":\"commands\",\"commandIdentifiers\":[\"git status\"]}}"), sent["r-always"]["result"]);
        Assert.Equal(approveOnce, sent["r-always-hook"]["result"]);
        Assert.Equal(Json("{\"kind\":\"reject\",\"feedback\":\"The user declined this action.\"}"), sent["r-deny"]["result"]);
        Assert.Equal(4, events.OfType<ProviderEvent.RequestResolved>().Count());
        Assert.Equal("c4", Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Id);
    }

    // ---- PR-PROVCLI-160 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-160")] // Services/Providers/CopilotSession.swift:873-902
    public void SessionApproval_builds_the_per_kind_decision_or_nil()
    {
        JSONValue? Approval(string request) => CopilotSession.SessionApproval(Json(request));
        JSONValue Wrapped(string inner) => Json("{\"kind\":\"approve-for-session\",\"approval\":" + inner + "}");

        // shell: an explicit true offer and at least one string identifier.
        Assert.Equal(Wrapped("{\"kind\":\"commands\",\"commandIdentifiers\":[\"git\",\"ls\"]}"),
            Approval("{\"kind\":\"shell\",\"canOfferSessionApproval\":true,\"commands\":[{\"identifier\":\"git\"},{\"x\":1},{\"identifier\":3},{\"identifier\":\"ls\"}]}"));
        Assert.Null(Approval("{\"kind\":\"shell\",\"commands\":[{\"identifier\":\"git\"}]}"));
        Assert.Null(Approval("{\"kind\":\"shell\",\"canOfferSessionApproval\":\"true\",\"commands\":[{\"identifier\":\"git\"}]}"));
        Assert.Null(Approval("{\"kind\":\"shell\",\"canOfferSessionApproval\":true,\"commands\":[]}"));
        Assert.Null(Approval("{\"kind\":\"shell\",\"canOfferSessionApproval\":true}"));
        // write: an explicit true offer.
        Assert.Equal(Wrapped("{\"kind\":\"write\"}"), Approval("{\"kind\":\"write\",\"canOfferSessionApproval\":true}"));
        Assert.Null(Approval("{\"kind\":\"write\",\"canOfferSessionApproval\":false}"));
        Assert.Null(Approval("{\"kind\":\"write\"}"));
        // read: always, with no offer check.
        Assert.Equal(Wrapped("{\"kind\":\"read\"}"), Approval("{\"kind\":\"read\",\"canOfferSessionApproval\":false}"));
        // url: the host, returned directly rather than wrapped.
        Assert.Equal(Json("{\"kind\":\"approve-for-session\",\"domain\":\"docs.github.com\"}"), Approval("{\"kind\":\"url\",\"url\":\"https://docs.github.com/en/copilot\"}"));
        Assert.Null(Approval("{\"kind\":\"url\",\"url\":\"relative/path\"}"));
        Assert.Null(Approval("{\"kind\":\"url\"}"));
        // mcp: a string server name; the tool name may be JSON null.
        Assert.Equal(Wrapped("{\"kind\":\"mcp\",\"serverName\":\"github\",\"toolName\":\"search\"}"), Approval("{\"kind\":\"mcp\",\"serverName\":\"github\",\"toolName\":\"search\"}"));
        Assert.Equal(Wrapped("{\"kind\":\"mcp\",\"serverName\":\"github\",\"toolName\":null}"), Approval("{\"kind\":\"mcp\",\"serverName\":\"github\"}"));
        Assert.Null(Approval("{\"kind\":\"mcp\",\"toolName\":\"search\"}"));
        // custom-tool: a string tool name.
        Assert.Equal(Wrapped("{\"kind\":\"custom-tool\",\"toolName\":\"deploy\"}"), Approval("{\"kind\":\"custom-tool\",\"toolName\":\"deploy\"}"));
        Assert.Null(Approval("{\"kind\":\"custom-tool\"}"));
        // memory: always.
        Assert.Equal(Wrapped("{\"kind\":\"memory\"}"), Approval("{\"kind\":\"memory\"}"));
        // Anything else: nil.
        Assert.Null(Approval("{\"kind\":\"hook\"}"));
        Assert.Null(Approval("{\"kind\":\"extension-management\"}"));
        Assert.Null(Approval("{}"));
    }

    // ---- PR-PROVCLI-161, 162 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-161")] // Services/Providers/CopilotSession.swift:288-289,302
    [Trait("Req", "PR-PROVCLI-162")] // Services/Providers/CopilotSession.swift:290-302
    public void Questions_and_plan_exits_resolved_as_approvals_emit_their_events_in_order()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        AddPending(session, "question-1", Pending("Question", new RPCID.Int(1), new ValueList<string>(["a"])));
        AddPending(session, "plan-2", Pending("PlanExit", new RPCID.Int(2)));
        AddPending(session, "plan-3", Pending("PlanExit", new RPCID.Int(3)));
        runtime.OnMain(() => session.ResolveApproval("question-1", "deny"));
        Assert.Equal([new ProviderEvent.RequestResolved("question-1")], events);
        events.Clear();
        runtime.OnMain(() => session.ResolveApproval("plan-2", "allow"));
        Assert.Equal([new ProviderEvent.ModeChanged(InteractionMode.Build), new ProviderEvent.RequestResolved("plan-2")], events);
        events.Clear();
        runtime.OnMain(() => session.ResolveApproval("plan-3", "always"));
        Assert.Equal([new ProviderEvent.RequestResolved("plan-3")], events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-161")] // Services/Providers/CopilotSession.swift:288-289
    [Trait("Req", "PR-PROVCLI-162")] // Services/Providers/CopilotSession.swift:290-300
    public void A_dismissed_question_gets_an_empty_free_form_answer_and_a_plan_gets_approved_or_keep_planning()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("question-plan-replies");
        var (session, _, _) = Started(runtime, Config(script));
        AddPending(session, "question-11", Pending("Question", new RPCID.Int(11), new ValueList<string>(["a"])));
        AddPending(session, "plan-12", Pending("PlanExit", new RPCID.Int(12)));
        AddPending(session, "plan-13", Pending("PlanExit", new RPCID.String("thirteen")));
        runtime.OnMain(() =>
        {
            session.ResolveApproval("question-11", "allow");
            session.ResolveApproval("plan-12", "allow");
            session.ResolveApproval("plan-13", "deny");
        });
        runtime.Loop.RunUntil(() => script.Responses().Count == 3, Bound);
        var responses = script.Responses();
        Assert.Equal(Json("{\"answer\":\"\",\"wasFreeform\":true}"), responses.Single(r => r["id"]!.JSONValueInt == 11)["result"]);
        Assert.Equal(Json("{\"approved\":true,\"selectedAction\":\"interactive\"}"), responses.Single(r => r["id"]!.JSONValueInt == 12)["result"]);
        Assert.Equal(Json("{\"approved\":false,\"feedback\":\"The user wants to keep planning. Wait for their feedback before changing anything.\"}"),
            responses.Single(r => r["id"]!.JSONValueString == "thirteen")["result"]);
        Assert.All(responses, r => Assert.Null(r["error"]));
    }

    // ---- PR-PROVCLI-163 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-163")] // Services/Providers/CopilotSession.swift:305-306
    public void AnswerQuestion_consumes_a_non_question_entry_without_replying_and_ignores_an_unknown_id()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        AddPending(session, "plan-1", Pending("PlanExit", new RPCID.Int(1)));
        var answers = new ValueMap<string, ValueList<string>>([new("answer", new ValueList<string>(["x"]))]);
        runtime.OnMain(() => session.AnswerQuestion("plan-1", answers));
        runtime.OnMain(() => session.AnswerQuestion("unknown", answers));
        Assert.Empty(events);
        Assert.Empty(PendingRequests(session));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-163")] // Services/Providers/CopilotSession.swift:305-310
    public void An_answer_joins_the_first_values_with_a_comma_and_is_free_form_unless_it_is_an_offered_choice()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("answers");
        var (session, events, _) = Started(runtime, Config(script));
        var choices = new ValueList<string>(["Red", "Blue", "Red, Blue"]);
        AddPending(session, "question-21", Pending("Question", new RPCID.Int(21), choices));
        AddPending(session, "question-22", Pending("Question", new RPCID.Int(22), choices));
        AddPending(session, "question-23", Pending("Question", new RPCID.Int(23), choices));
        AddPending(session, "question-24", Pending("Question", new RPCID.Int(24), choices));
        ValueMap<string, ValueList<string>> One(params string[] values) => new([new("answer", new ValueList<string>(values))]);
        runtime.OnMain(() =>
        {
            session.AnswerQuestion("question-21", One("Blue"));
            session.AnswerQuestion("question-22", One("Green"));
            session.AnswerQuestion("question-23", One("Red", "Blue"));
            session.AnswerQuestion("question-24", new ValueMap<string, ValueList<string>>([]));
        });
        runtime.Loop.RunUntil(() => script.Responses().Count == 4, Bound);
        JSONValue Result(long id) => script.Responses().Single(r => r["id"]!.JSONValueInt == id)["result"]!;
        Assert.Equal(Json("{\"answer\":\"Blue\",\"wasFreeform\":false}"), Result(21));
        Assert.Equal(Json("{\"answer\":\"Green\",\"wasFreeform\":true}"), Result(22));
        Assert.Equal(Json("{\"answer\":\"Red, Blue\",\"wasFreeform\":false}"), Result(23));
        Assert.Equal(Json("{\"answer\":\"\",\"wasFreeform\":true}"), Result(24));
        Assert.Equal(["question-21", "question-22", "question-23", "question-24"], events.OfType<ProviderEvent.RequestResolved>().Select(e => e.Id).ToList());
    }

    // ---- PR-PROVCLI-164 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-164")] // Services/Providers/CopilotSession.swift:312-314
    public void Stop_without_a_connection_only_marks_the_session_stopping()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config());
        runtime.OnMain(session.Stop);
        Assert.True(Field<bool>(session, "isStopping"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-164")] // Services/Providers/CopilotSession.swift:312-319,931-937
    public void Stop_asks_the_runtime_to_shut_down_then_closes_the_connection_without_an_exit_event()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("stop");
        var (session, events, _) = Started(runtime, Config(script));
        runtime.OnMain(session.Stop);
        Assert.True(Field<bool>(session, "isStopping"));
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
        var shutdown = Assert.Single(script.Requests("runtime.shutdown"));
        Assert.Null(shutdown["params"]); // request(_:) with no params sends none (JSONRPC.swift:108)
        Assert.Empty(events.OfType<ProviderEvent.Exited>());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-164")] // Services/Providers/CopilotSession.swift:320-323
    public void An_unanswered_shutdown_is_closed_by_the_three_second_backstop()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("stop-backstop");
        script.Silent("runtime.shutdown");
        var (session, _, _) = Started(runtime, Config(script));
        runtime.OnMain(session.Stop);
        runtime.Loop.RunUntil(() => script.Requests("runtime.shutdown").Count == 1, Bound);
        runtime.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.False(TryRunUntil(runtime, () => !session.IsRunning, TimeSpan.FromSeconds(2)));
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
    }

    // ---- PR-PROVCLI-165, 166 ----

    private const string Catalog = "{\"models\":[" +
        "{\"id\":\"gpt-5\",\"name\":\"GPT-5\",\"billing\":{\"multiplier\":1.0},\"supportedReasoningEfforts\":[\"low\",\"medium\",7,\"high\"],\"defaultReasoningEffort\":\"medium\"}," +
        "{\"id\":\"claude-opus-4.1\",\"billing\":{\"multiplier\":10},\"policy\":{\"state\":\"unconfigured\"}}," +
        "{\"id\":\"o3-mini\",\"name\":\"o3-mini\",\"billing\":{\"multiplier\":0.33},\"policy\":{\"state\":\"enabled\"}}," +
        "{\"id\":\"gone\",\"name\":\"Gone\",\"policy\":{\"state\":\"disabled\"}}," +
        "{\"name\":\"No id\"}," +
        "{\"id\":7}," +
        "{\"id\":\"plain\",\"policy\":{\"state\":\"unconfigured\"},\"billing\":{\"multiplier\":\"2\"}}" +
        "]}";

    [Fact]
    [Trait("Req", "PR-PROVCLI-165")] // Services/Providers/CopilotSession.swift:402-425; Services/Providers/ProviderRegistry.swift:146
    [Trait("Req", "PR-PROVCLI-166")] // Services/Providers/CopilotSession.swift:409-419
    public void ListModels_seeds_Auto_skips_disabled_and_idless_entries_and_describes_the_rest()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("models");
        script.Reply("models.list", Catalog);
        var models = runtime.Loop.Run(() => CopilotSession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.Equal(["auto", "gpt-5", "claude-opus-4.1", "o3-mini", "plain"], models.Select(m => m.Id).ToList());
        Assert.Same(ProviderRegistry.CopilotAuto, models[0]);
        Assert.Equal("Auto", models[0].Name);
        Assert.Equal("Copilot picks the model for each request", models[0].Detail);
        Assert.True(models[0].IsDefault);

        Assert.Equal("GPT-5", models[1].Name);
        Assert.Equal("1\U000000D7 premium requests", models[1].Detail); // U+00D7 MULTIPLICATION SIGN
        Assert.Equal(["low", "medium", "high"], models[1].Efforts);
        Assert.Equal("medium", models[1].DefaultEffort);
        Assert.False(models[1].IsDefault);

        Assert.Equal("claude-opus-4.1", models[2].Name);
        // U+00D7 MULTIPLICATION SIGN; U+00B7 MIDDLE DOT between the two details.
        Assert.Equal("10\U000000D7 premium requests \U000000B7 Enable it in your GitHub Copilot settings first", models[2].Detail);
        Assert.Empty(models[2].Efforts);
        Assert.Null(models[2].DefaultEffort);

        Assert.Equal("0.33\U000000D7 premium requests", models[3].Detail); // U+00D7 MULTIPLICATION SIGN

        // A string multiplier is not a double: only the unconfigured hint remains.
        Assert.Equal("plain", models[4].Name);
        Assert.Equal("Enable it in your GitHub Copilot settings first", models[4].Detail);

        var request = Assert.Single(script.Requests("models.list"));
        Assert.Equal(Json("{}"), request["params"]);
        var expected = new List<string> { "--headless", "--stdio", "--no-auto-update", "--log-level", "error" };
        expected.AddRange(Marrow.Services.MCP.MCPSessionOverrides.CopilotArguments());
        Assert.Equal(expected, script.Calls().Single());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-165")] // Services/Providers/CopilotSession.swift:406-408,419
    public void A_catalog_of_only_disabled_models_still_yields_the_Auto_row_and_no_detail_means_nil()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("models-disabled");
        script.Reply("models.list", "{\"models\":[{\"id\":\"a\",\"policy\":{\"state\":\"disabled\"}},{\"id\":\"b\",\"policy\":{\"state\":\"enabled\"}}]}");
        var models = runtime.Loop.Run(() => CopilotSession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.Equal(2, models.Count);
        Assert.Equal("b", models[1].Name);
        Assert.Null(models[1].Detail);

        var empty = new CopilotScript("models-empty");
        empty.Reply("models.list", "{\"models\":[{\"id\":\"a\",\"policy\":{\"state\":\"disabled\"}}]}");
        var only = runtime.Loop.Run(() => CopilotSession.ListModels(FileUrl(empty.ExecutablePath), new ValueMap<string, string>([empty.EnvironmentEntry])), Bound);
        Assert.Same(ProviderRegistry.CopilotAuto, Assert.Single(only));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-165")] // Services/Providers/CopilotSession.swift:405
    public void A_failed_models_list_propagates()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("models-error");
        script.Error("models.list", -32603, "Not authorized");
        var error = Assert.Throws<RPCError>(() => runtime.Loop.Run(() => CopilotSession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound));
        Assert.Equal("Not authorized", error.Message);
    }

    // ---- PR-PROVCLI-167 ----

    private static ProviderStatus.Auth Auth(TestRuntime runtime, string label, string? reply, bool error = false)
    {
        var script = new CopilotScript(label);
        if (error) script.Error("account.getCurrentAuth", -32603, "offline");
        else if (reply is not null) script.Reply("account.getCurrentAuth", reply);
        var auth = runtime.Loop.Run(() => CopilotSession.AuthStatus(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.Equal(Json("{}"), Assert.Single(script.Requests("account.getCurrentAuth"))["params"]);
        return auth;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-167")] // Services/Providers/CopilotSession.swift:427-441
    public void AuthStatus_reads_the_login_and_flags_an_account_without_Copilot_access()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        Assert.Equal(new ProviderStatus.Auth.SignedIn("octocat"), Auth(runtime, "auth-login", "{\"authInfo\":{\"login\":\"octocat\",\"copilotUser\":{\"access_type_sku\":\"free_limited_copilot\"}}}"));
        // U+00B7 MIDDLE DOT between the login and the note.
        Assert.Equal(new ProviderStatus.Auth.SignedIn("octocat \U000000B7 no Copilot access"),
            Auth(runtime, "auth-no-access", "{\"authInfo\":{\"login\":\"octocat\",\"copilotUser\":{\"access_type_sku\":\"no_access\"}}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedIn("GitHub \U000000B7 no Copilot access"), // U+00B7 MIDDLE DOT
            Auth(runtime, "auth-no-login", "{\"authInfo\":{\"copilotUser\":{\"access_type_sku\":\"no_access\"}}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), Auth(runtime, "auth-empty", "{\"authInfo\":{}}"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-167")] // Services/Providers/CopilotSession.swift:433-434
    public void AuthStatus_is_signed_out_without_authInfo_and_unknown_when_the_request_fails()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Auth(runtime, "auth-null", "{\"authInfo\":null}"));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Auth(runtime, "auth-missing", "{}"));
        Assert.Equal(new ProviderStatus.Auth.Unknown(), Auth(runtime, "auth-error", null, error: true));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-167")] // Services/Providers/CopilotSession.swift:429-431
    public void AuthStatus_is_unknown_never_signed_out_when_the_CLI_cannot_be_reached()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var missing = FileUrl(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid().ToString("N"), "copilot.exe"));
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            runtime.Loop.Run(() => CopilotSession.AuthStatus(missing, new ValueMap<string, string>([])), Bound));

        var script = new CopilotScript("auth-old-cli");
        script.Reply("ping", "{}");
        Assert.Equal(new ProviderStatus.Auth.Unknown(),
            runtime.Loop.Run(() => CopilotSession.AuthStatus(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound));
        Assert.Empty(script.Requests("account.getCurrentAuth"));
    }

    // ---- PR-PROVCLI-168 .. 173 ----

    private static PlanLimits? Limits(TestRuntime runtime, CopilotScript script) =>
        runtime.Loop.Run(() => CopilotSession.ReadPlanLimits(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);

    [Fact]
    [Trait("Req", "PR-PROVCLI-168")] // Services/Providers/CopilotSession.swift:444-463
    [Trait("Req", "PR-PROVCLI-172")] // Services/Providers/CopilotSession.swift:465-467
    public void ReadPlanLimits_walks_the_three_snapshots_in_a_fixed_order_and_names_the_plan()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota");
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{" +
            "\"completions\":{\"entitlementRequests\":2000,\"usedRequests\":500}," +
            "\"chat\":{\"entitlementRequests\":300,\"usedRequests\":30,\"resetDate\":\"2026-10-01T00:00:00Z\"}," +
            "\"premium_interactions\":{\"entitlementRequests\":300,\"remainingPercentage\":75.5,\"resetDate\":\"2026-10-01T08:30:15.250Z\"}}}");
        script.Reply("account.getCurrentAuth", "{\"authInfo\":{\"copilotUser\":{\"copilot_plan\":\"individual\"}}}");
        var limits = Limits(runtime, script)!;
        Assert.Equal(["premium_interactions", "chat", "completions"], limits.Windows.Select(w => w.Id).ToList());
        Assert.Equal(["Premium requests", "Chat messages", "Code completions"], limits.Windows.Select(w => w.Title).ToList());
        Assert.Equal(24.5, limits.Windows[0].Percent);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 30, 15, 250, TimeSpan.Zero), limits.Windows[0].ResetsAt);
        Assert.Equal(30.0 / 300.0 * 100.0, limits.Windows[1].Percent);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), limits.Windows[1].ResetsAt);
        Assert.Equal(500.0 / 2000.0 * 100.0, limits.Windows[2].Percent);
        Assert.Null(limits.Windows[2].ResetsAt);
        Assert.All(limits.Windows, w => Assert.Null(w.Detail));
        Assert.Null(limits.Problem);
        Assert.Equal(PlanLimitsReader.PlanName("individual") is { } name ? "Copilot " + name : null, limits.PlanName);
        // Both requests on the one process, each with the empty object.
        Assert.Single(script.Calls());
        Assert.Equal(["ping", "account.getQuota", "account.getCurrentAuth"], script.Methods());
        Assert.Equal(Json("{}"), script.Requests("account.getQuota").Single()["params"]);
        Assert.Equal(Json("{}"), script.Requests("account.getCurrentAuth").Single()["params"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-169")] // Services/Providers/CopilotSession.swift:451-453
    public void A_snapshot_is_skipped_when_absent_null_explicitly_unlimited_or_without_a_positive_entitlement()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota-skips");
        // premium: explicit true unlimited; chat: a string "true" does not skip; completions: zero entitlement.
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{" +
            "\"premium_interactions\":{\"isUnlimitedEntitlement\":true,\"entitlementRequests\":300}," +
            "\"chat\":{\"isUnlimitedEntitlement\":\"true\",\"entitlementRequests\":100,\"usedRequests\":50}," +
            "\"completions\":{\"entitlementRequests\":0,\"usedRequests\":5}}}");
        var limits = Limits(runtime, script)!;
        var chat = Assert.Single(limits.Windows);
        Assert.Equal("chat", chat.Id);
        Assert.Equal(50.0 / 100.0 * 100.0, chat.Percent);

        foreach (var snapshots in new[]
        {
            "{\"quotaSnapshots\":{\"premium_interactions\":null,\"chat\":{\"entitlementRequests\":-5},\"completions\":{\"usedRequests\":5}}}",
            "{\"quotaSnapshots\":{\"premium_interactions\":{\"entitlementRequests\":\"300\"}}}",
            "{}",
        })
        {
            var skipped = new CopilotScript("quota-all-skipped");
            skipped.Reply("account.getQuota", snapshots);
            Assert.Null(Limits(runtime, skipped));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-170")] // Services/Providers/CopilotSession.swift:454-456
    public void The_remaining_branch_clamps_both_ends_and_the_used_branch_clamps_only_the_top()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota-percent");
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{" +
            "\"premium_interactions\":{\"entitlementRequests\":300,\"remainingPercentage\":120}," +
            "\"chat\":{\"entitlementRequests\":300,\"remainingPercentage\":-10,\"usedRequests\":1}," +
            "\"completions\":{\"entitlementRequests\":300,\"usedRequests\":-30}}}");
        var limits = Limits(runtime, script)!;
        Assert.Equal(0.0, limits.Windows[0].Percent);
        Assert.Equal(100.0, limits.Windows[1].Percent);
        // UPSTREAM DEFECT (upstream_defects.md entry 3), reproduced: no lower clamp on the fallback branch.
        Assert.Equal(-30.0 / 300.0 * 100.0, limits.Windows[2].Percent);
        Assert.True(limits.Windows[2].Percent < 0);

        var over = new CopilotScript("quota-over");
        over.Reply("account.getQuota", "{\"quotaSnapshots\":{\"chat\":{\"entitlementRequests\":300,\"usedRequests\":600}}}");
        Assert.Equal(100.0, Assert.Single(Limits(runtime, over)!.Windows).Percent);
        var none = new CopilotScript("quota-unused");
        none.Reply("account.getQuota", "{\"quotaSnapshots\":{\"chat\":{\"entitlementRequests\":300}}}");
        Assert.Equal(0.0, Assert.Single(Limits(runtime, none)!.Windows).Percent);
        var third = new CopilotScript("quota-third");
        third.Reply("account.getQuota", "{\"quotaSnapshots\":{\"chat\":{\"entitlementRequests\":3,\"usedRequests\":1}}}");
        Assert.Equal(1.0 / 3.0 * 100.0, Assert.Single(Limits(runtime, third)!.Windows).Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-171")] // Services/Providers/CopilotSession.swift:464
    [Trait("Req", "PR-PROVCLI-172")] // Services/Providers/CopilotSession.swift:464-465
    public void With_no_window_ReadPlanLimits_returns_nil_without_asking_for_the_plan()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota-empty");
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{}}");
        Assert.Null(Limits(runtime, script));
        Assert.Equal(["ping", "account.getQuota"], script.Methods());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-168")] // Services/Providers/CopilotSession.swift:447
    public void A_failed_quota_request_propagates()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota-error");
        script.Error("account.getQuota", -32603, "quota unavailable");
        var error = Assert.Throws<RPCError>(() => Limits(runtime, script));
        Assert.Equal("quota unavailable", error.Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-172")] // Services/Providers/CopilotSession.swift:465-467
    public void A_failed_or_planless_auth_read_leaves_the_plan_name_nil_and_the_windows_intact()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("quota-auth-error");
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{\"chat\":{\"entitlementRequests\":10,\"usedRequests\":1}}}");
        script.Error("account.getCurrentAuth", -32603, "offline");
        var limits = Limits(runtime, script)!;
        Assert.Equal(PlanLimitsReader.PlanName(null) is { } name ? "Copilot " + name : null, limits.PlanName);
        Assert.Equal(1.0 / 10.0 * 100.0, Assert.Single(limits.Windows).Percent);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-173")] // Services/Providers/CopilotSession.swift:470-472; Core/Support/ISO8601.swift:10-26
    public void ParseDate_tries_fractional_seconds_then_whole_seconds_then_a_bare_date()
    {
        DateTimeOffset? Parse(string text) => (DateTimeOffset?)InvokeStatic("ParseDate", text);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, 57, TimeSpan.Zero), Parse("2026-09-13T18:28:42.057Z"));
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 18, 28, 42, TimeSpan.Zero), Parse("2026-09-13T18:28:42Z"));
        // Date.ISO8601FormatStyle's default time zone is GMT: a bare date is its midnight in UTC.
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), Parse("2026-09-13"));
        Assert.Null(Parse("next month"));
        Assert.Null(Parse(""));
    }

    // ---- PR-PROVCLI-174 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-174")] // Services/Providers/CopilotSession.swift:504-506
    public void Only_session_event_notifications_for_this_session_are_handled()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline(sessionID: "s-1");
        var title = Json("{\"title\":\"Fix the build\"}");
        Notify(runtime, session, Event("session.title_changed", title), method: "session.other");
        Notify(runtime, session, Json("{\"sessionId\":\"s-1\"}"));
        Notify(runtime, session, Json("{\"sessionId\":\"s-1\",\"event\":{\"type\":7,\"data\":{\"title\":\"x\"}}}"));
        Notify(runtime, session, Event("session.title_changed", title, sessionID: "s-2"));
        Assert.Empty(events);
        Notify(runtime, session, Event("session.title_changed", title, sessionID: "s-1"));
        Notify(runtime, session, Event("session.title_changed", title, sessionID: null));
        Notify(runtime, session, Json("{\"sessionId\":7,\"event\":{\"type\":\"session.title_changed\",\"data\":{\"title\":\"Fix the build\"}}}"));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(new ProviderEvent.Title("Fix the build"), e));

        // Before a session id exists nothing is filtered by it.
        var (fresh, freshEvents) = Make(Config());
        Notify(runtime, fresh, Event("session.title_changed", title, sessionID: "any"));
        Assert.Single(freshEvents);
    }

    // ---- PR-PROVCLI-175 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-175")] // Services/Providers/CopilotSession.swift:507-512
    public void Without_Hydra_a_sub_agent_s_assistant_and_tool_events_are_dropped_but_not_its_usage_or_other_events()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m1\",\"deltaContent\":\"hi\"}"), agentID: "a1");
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m1\",\"content\":\"hi\"}"), agentID: "a1");
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"t1\",\"toolName\":\"bash\",\"arguments\":{\"command\":\"ls\"}}"), agentID: "a1");
        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"t1\",\"success\":true}"), agentID: "a1");
        Assert.Empty(events);
        Assert.Empty(Field<Dictionary<string, string>>(session, "toolNames"));

        Notify(runtime, session, "assistant.usage", Json("{\"inputTokens\":10,\"outputTokens\":5}"), agentID: "a1");
        var spend = Assert.IsType<ProviderEvent.TokenSpend>(Assert.Single(events)).Value;
        Assert.Equal("a1", spend.AgentID);
        Notify(runtime, session, "session.title_changed", Json("{\"title\":\"T\"}"), agentID: "a1");
        Assert.Equal(new ProviderEvent.Title("T"), events[1]);
        // Hydra off: a sub-agent's life events are ignored (PR-PROVCLI-182), not wrapped.
        Assert.Equal(2, events.Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-175")] // Services/Providers/CopilotSession.swift:507-517
    public void With_Hydra_a_head_s_events_pass_wrapped_in_agentEvent_and_the_lead_s_go_out_bare()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline(Config(hydra: Hydra()));
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m1\",\"deltaContent\":\"hi\"}"), agentID: "a1");
        Notify(runtime, session, "tool.execution_progress", Json("{\"toolCallId\":\"t1\",\"progressMessage\":\"50%\"}"), agentID: "a1");
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m2\",\"deltaContent\":\"lead\"}"));
        Assert.Equal(
            [
                new ProviderEvent.AgentEvent("a1", new ProviderEvent.MessageDelta("m1", "hi")),
                new ProviderEvent.AgentEvent("a1", new ProviderEvent.ToolUpdated("t1", new ToolUpdate { Detail = "50%" })),
                new ProviderEvent.MessageDelta("m2", "lead"),
            ],
            events);
    }

    // ---- PR-PROVCLI-176 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-176")] // Services/Providers/CopilotSession.swift:520-526
    public void Turn_start_announces_a_lead_turn_only_when_none_is_active_and_a_head_s_every_time()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline(Config(hydra: Hydra()));
        Notify(runtime, session, "assistant.turn_start", Json("{\"turnId\":\"t-1\"}"));
        Assert.True(Field<bool>(session, "turnActive"));
        Notify(runtime, session, "assistant.turn_start", Json("{\"turnId\":\"t-2\"}"));
        Notify(runtime, session, "assistant.turn_start", Json("{\"turnId\":\"h-1\"}"), agentID: "a1");
        Notify(runtime, session, "assistant.turn_start", Json("{}"), agentID: "a1");
        Assert.Equal(
            [
                new ProviderEvent.TurnStarted("t-1"),
                new ProviderEvent.AgentEvent("a1", new ProviderEvent.TurnStarted("h-1")),
                new ProviderEvent.AgentEvent("a1", new ProviderEvent.TurnStarted(null)),
            ],
            events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-176")] // Services/Providers/CopilotSession.swift:527-543,561-576
    public void Deltas_reasoning_usage_info_partial_output_and_progress_map_to_their_events()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m1\",\"deltaContent\":\"Hel\"}"));
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m1\"}"));
        Notify(runtime, session, "assistant.reasoning_delta", Json("{\"reasoningId\":\"r1\",\"deltaContent\":\"think\"}"));
        Notify(runtime, session, "assistant.reasoning", Json("{\"reasoningId\":\"r1\",\"content\":\" thought \"}"));
        Notify(runtime, session, "assistant.reasoning", Json("{\"reasoningId\":\"r2\"}"));
        Notify(runtime, session, "assistant.reasoning", Json("{\"content\":\"no id\"}"));
        Notify(runtime, session, "session.usage_info", Json("{\"currentTokens\":1200,\"tokenLimit\":128000}"));
        Notify(runtime, session, "session.usage_info", Json("{\"currentTokens\":1300}"));
        Notify(runtime, session, "session.usage_info", Json("{\"tokenLimit\":128000}"));
        Notify(runtime, session, "tool.execution_partial_result", Json("{\"toolCallId\":\"t1\",\"partialOutput\":\"line\\n\"}"));
        Notify(runtime, session, "tool.execution_progress", Json("{\"toolCallId\":\"t1\",\"progressMessage\":\"Running\"}"));
        Notify(runtime, session, "tool.execution_progress", Json("{\"toolCallId\":\"t1\"}"));
        Assert.Equal(
            [
                new ProviderEvent.MessageDelta("m1", "Hel"),
                new ProviderEvent.ReasoningDelta("r1", "think"),
                new ProviderEvent.ReasoningCompleted("r1", " thought "),
                new ProviderEvent.ReasoningCompleted("r2", ""),
                new ProviderEvent.Usage(new ContextUsage { UsedTokens = 1200, WindowTokens = 128000 }),
                new ProviderEvent.Usage(new ContextUsage { UsedTokens = 1300, WindowTokens = null }),
                new ProviderEvent.ToolOutput("t1", "line\n"),
                new ProviderEvent.ToolUpdated("t1", new ToolUpdate { Detail = "Running" }),
            ],
            events);
        Assert.Contains("m1", Field<HashSet<string>>(session, "streamedMessages"));
        // DEVIATION ED-025: the trimmed text itself is the key.
        Assert.Equal(["", "thought"], Field<HashSet<string>>(session, "shownReasoning").OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-176")] // Services/Providers/CopilotSession.swift:614-615,623-624,631-639
    public void Title_warning_idle_shutdown_and_unknown_events()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "session.title_changed", Json("{\"title\":\"\"}"));
        Notify(runtime, session, "session.title_changed", Json("{\"title\":\"Refactor\"}"));
        Notify(runtime, session, "session.warning", Json("{\"message\":\"Rate limited\"}"));
        Notify(runtime, session, "session.warning", Json("{}"));
        Notify(runtime, session, "session.shutdown", Json("{\"shutdownType\":\"error\",\"errorReason\":\"Crashed\"}"));
        Notify(runtime, session, "session.shutdown", Json("{\"shutdownType\":\"routine\",\"errorReason\":\"Bye\"}"));
        Notify(runtime, session, "session.shutdown", Json("{\"shutdownType\":\"error\"}"));
        Notify(runtime, session, "session.idle", Json("{}")); // no active turn: nothing
        Notify(runtime, session, "session.start", Json("{}"));
        Notify(runtime, session, "assistant.intent", Json("{\"intent\":\"x\"}"));
        Assert.Equal(3, events.Count);
        Assert.Equal(new ProviderEvent.Title("Refactor"), events[0]);
        Assert.Equal(new Notice { NoticeLevel = Notice.Level.Warning, Message = "Rate limited" }, Assert.IsType<ProviderEvent.Notice>(events[1]).Value);
        Assert.Equal(new Notice { NoticeLevel = Notice.Level.Error, Message = "Crashed" }, Assert.IsType<ProviderEvent.Notice>(events[2]).Value);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-176")] // Services/Providers/CopilotSession.swift:631-632,695-715
    public void Session_idle_ends_the_turn_as_interrupted_only_when_aborted_is_true()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        foreach (var (data, status) in new[]
        {
            ("{\"aborted\":true}", TurnStatus.Interrupted),
            ("{\"aborted\":\"true\"}", TurnStatus.Completed),
            ("{}", TurnStatus.Completed),
        })
        {
            events.Clear();
            SetField(session, "turnActive", true);
            Notify(runtime, session, "session.idle", Json(data));
            Assert.Equal([new ProviderEvent.TurnCompleted(status, null)], events);
            Assert.False(Field<bool>(session, "turnActive"));
        }
    }

    // ---- PR-PROVCLI-177 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-177")] // Services/Providers/CopilotSession.swift:544-560
    public void Assistant_usage_records_the_spend_and_reports_it_with_the_head_s_id()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var ledger = runtime.OnMain(() => (TokenLedger)Activator.CreateInstance(typeof(TokenLedger), nonPublic: true)!);
        using var ledgerScope = TokenLedger.Shared.Override(ledger);
        var before = ledger.DailyTotals.Values.Sum();
        var (session, events) = Offline(Config(model: "gpt-5", hydra: Hydra()));
        Notify(runtime, session, "assistant.usage", Json("{\"inputTokens\":100,\"outputTokens\":20,\"cacheReadTokens\":5,\"model\":\"claude-sonnet-4.5\"}"));
        Notify(runtime, session, "assistant.usage", Json("{\"inputTokens\":7}"), agentID: "head-1");
        Notify(runtime, session, "assistant.usage", Json("{\"inputTokens\":0,\"outputTokens\":0}"));
        Notify(runtime, session, "assistant.usage", Json("{\"model\":\"x\"}"));
        Assert.Equal(2, events.Count);
        Assert.Equal(new TokenSpend(ProviderKind.Copilot, 120, inputTokens: 100, outputTokens: 20, cachedInputTokens: 5, model: "claude-sonnet-4.5", agentID: null),
            Assert.IsType<ProviderEvent.TokenSpend>(events[0]).Value);
        // A head's report goes out bare (onEvent, not the head's sink), carrying its id; the model falls back to the session's.
        Assert.Equal(new TokenSpend(ProviderKind.Copilot, 7, inputTokens: 7, outputTokens: null, cachedInputTokens: null, model: "gpt-5", agentID: "head-1"),
            Assert.IsType<ProviderEvent.TokenSpend>(events[1]).Value);
        Assert.Equal(before + 127, ledger.DailyTotals.Values.Sum());
    }

    // ---- PR-PROVCLI-178, 179 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-178")] // Services/Providers/CopilotSession.swift:642-648,1064-1066
    [Trait("Decision", "ED-025")]
    public void A_message_s_carried_reasoning_is_shown_once_unless_a_reasoning_event_already_showed_it()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m1\",\"reasoningText\":\"  Plan the fix\\n\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m2\",\"reasoningText\":\"Plan the fix\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m3\",\"reasoningText\":\" \\n\\t \"}"));
        Notify(runtime, session, "assistant.reasoning", Json("{\"reasoningId\":\"r9\",\"content\":\"Check tests\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m4\",\"reasoningText\":\"\\nCheck tests \"}"));
        Assert.Equal(
            [
                new ProviderEvent.ReasoningCompleted("m1-reasoning", "  Plan the fix\n"),
                new ProviderEvent.ReasoningCompleted("r9", "Check tests"),
            ],
            events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-178")] // Services/Providers/CopilotSession.swift:183,703
    public void Shown_reasoning_is_forgotten_when_a_turn_begins_and_when_it_ends()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m1\",\"reasoningText\":\"Same\"}"));
        SetField(session, "turnActive", true);
        Notify(runtime, session, "session.idle", Json("{}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m2\",\"reasoningText\":\"Same\"}"));
        runtime.OnMain(() => Invoke(session, "BeginTurn"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m3\",\"reasoningText\":\"Same\"}"));
        Assert.Equal(["m1-reasoning", "m2-reasoning", "m3-reasoning"], events.OfType<ProviderEvent.ReasoningCompleted>().Select(e => e.Id).ToList());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-179")] // Services/Providers/CopilotSession.swift:643,649-652
    public void A_message_completes_when_it_has_content_or_streamed_and_needs_a_message_id()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message", Json("{\"content\":\"no id\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m1\",\"content\":\"\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m2\"}"));
        Notify(runtime, session, "assistant.message_delta", Json("{\"messageId\":\"m3\",\"deltaContent\":\"x\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m3\",\"content\":\"\"}"));
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m4\",\"content\":\"Done.\"}"));
        Assert.Equal(
            [
                new ProviderEvent.MessageDelta("m3", "x"),
                new ProviderEvent.MessageCompleted("m3", ""),
                new ProviderEvent.MessageCompleted("m4", "Done."),
            ],
            events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-179")] // Services/Providers/CopilotSession.swift:653-657
    [Trait("Req", "PR-PROVCLI-180")] // Services/Providers/CopilotSession.swift:660-662,673-674
    public void A_message_registers_the_calls_it_asks_for_once_even_when_execution_start_repeats_them()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "assistant.message", Json("{\"messageId\":\"m1\",\"content\":\"Listing\",\"toolRequests\":[" +
            "{\"toolCallId\":\"c1\",\"name\":\"bash\",\"arguments\":{\"command\":\"ls -la\",\"description\":\"List files\"}}," +
            "{\"toolCallId\":\"c2\"}," +
            "{\"name\":\"bash\"}," +
            "{\"toolCallId\":\"c3\",\"name\":\"github-search\",\"arguments\":{\"q\":\"x\"},\"mcpServerName\":\"github\",\"mcpToolName\":\"search_code\"}]}"));
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c1\",\"toolName\":\"bash\",\"arguments\":{\"command\":\"other\"}}"));
        Assert.Equal(3, events.Count);
        Assert.Equal(new ProviderEvent.MessageCompleted("m1", "Listing"), events[0]);
        var bash = Assert.IsType<ProviderEvent.ToolStarted>(events[1]);
        Assert.Equal("c1", bash.Id);
        Assert.Equal((ToolCall.Kind.Command, "ls -la", "List files"), (bash.Call.ToolCallKind, bash.Call.Title, bash.Call.Detail));
        var mcp = Assert.IsType<ProviderEvent.ToolStarted>(events[2]);
        Assert.Equal("c3", mcp.Id);
        // U+00B7 MIDDLE DOT between the server and the tool.
        Assert.Equal((ToolCall.Kind.Mcp, "github \U000000B7 search_code", "{\"q\":\"x\"}"), (mcp.Call.ToolCallKind, mcp.Call.Title, mcp.Call.Detail));
        Assert.Equal(["c1", "c3"], Field<Dictionary<string, string>>(session, "toolNames").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
    }

    // ---- PR-PROVCLI-180 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-180")] // Services/Providers/CopilotSession.swift:663-666
    public void Update_todo_publishes_the_checklist_and_draws_no_row()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c1\",\"toolName\":\"update_todo\",\"arguments\":{\"todos\":\"- [x] Read\\n- [ ] Write\"}}"));
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c2\",\"toolName\":\"update_todo\",\"arguments\":{}}"));
        Assert.Equal(2, events.Count);
        var todos = Assert.IsType<ProviderEvent.Todos>(events[0]).Value;
        Assert.Equal([("Read", TodoStep.Status.Done), ("Write", TodoStep.Status.Pending)], todos.Select(t => (t.Text, t.TodoStepStatus)).ToList());
        Assert.Empty(Assert.IsType<ProviderEvent.Todos>(events[1]).Value);
        Assert.Equal("update_todo", Field<Dictionary<string, string>>(session, "toolNames")["c1"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-180")] // Services/Providers/CopilotSession.swift:667-672
    public void A_task_call_records_its_brief_from_description_or_name_and_prompt_or_task()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c1\",\"toolName\":\"task\",\"arguments\":{\"description\":\"Audit\",\"name\":\"auditor\",\"prompt\":\"Check it\",\"task\":\"ignored\"}}"));
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c2\",\"toolName\":\"task\",\"arguments\":{\"name\":\"fixer\",\"task\":\"Fix it\"}}"));
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c3\",\"toolName\":\"task\"}"));
        var briefs = Field<Dictionary<string, (string Description, string? Prompt)>>(session, "taskBriefs");
        Assert.Equal(("Audit", "Check it"), briefs["c1"]);
        Assert.Equal(("fixer", "Fix it"), briefs["c2"]);
        Assert.Equal(("Subagent", (string?)null), briefs["c3"]);
        Assert.Equal(3, events.OfType<ProviderEvent.ToolStarted>().Count());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-180")] // Services/Providers/CopilotSession.swift:673-677
    public void A_call_with_edits_is_followed_by_an_update_carrying_just_the_edits()
    {
        using var runtime = new TestRuntime();
        var directory = Path.GetTempPath();
        var (session, events) = Offline(Config(workingDirectory: directory));
        var file = Path.Combine(directory, "notes.md");
        Notify(runtime, session, "tool.execution_start", Json(JSONValue.FromDictionaryLiteral(
            ("toolCallId", "c1"), ("toolName", "create"), ("arguments", JSONValue.FromDictionaryLiteral(("path", file), ("file_text", "hello\n")))).CompactString));
        Assert.Equal(2, events.Count);
        var started = Assert.IsType<ProviderEvent.ToolStarted>(events[0]);
        var updated = Assert.IsType<ProviderEvent.ToolUpdated>(events[1]);
        Assert.Equal("c1", updated.Id);
        Assert.Equal(new ToolUpdate { Edits = started.Call.Edits }, updated.Update);
        Assert.Single(started.Call.Edits);
    }

    // ---- PR-PROVCLI-181 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-181")] // Services/Providers/CopilotSession.swift:680-693,942-943
    public void Tool_results_hide_five_tools_default_to_success_and_read_declines_from_the_app_s_record()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        var names = Field<Dictionary<string, string>>(session, "toolNames");
        foreach (var hidden in new[] { "ask_user", "exit_plan_mode", "update_todo", "task_complete", "report_intent" }) names["h-" + hidden] = hidden;
        names["ok"] = "bash";
        names["fail"] = "bash";
        names["declined"] = "bash";
        names["plain"] = "view";
        Field<HashSet<string>>(session, "declinedTools").Add("declined");
        foreach (var hidden in new[] { "ask_user", "exit_plan_mode", "update_todo", "task_complete", "report_intent" })
            Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"h-" + hidden + "\",\"success\":true,\"result\":{\"content\":\"x\"}}"));
        Assert.Empty(events);

        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"ok\",\"result\":{\"content\":\"short\",\"detailedContent\":\"long\"}}"));
        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"fail\",\"success\":false,\"error\":{\"message\":\"exit 1\"},\"result\":{\"content\":\"partial\"}}"));
        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"declined\",\"success\":false,\"result\":{\"content\":\"denied\"}}"));
        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"plain\",\"success\":true}"));
        Notify(runtime, session, "tool.execution_complete", Json("{\"toolCallId\":\"unknown\",\"success\":true,\"error\":{\"message\":\"ignored\"},\"result\":{\"content\":\"out\"}}"));
        Notify(runtime, session, "tool.execution_complete", Json("{\"success\":true}"));
        Assert.Equal(
            [
                new ProviderEvent.ToolUpdated("ok", new ToolUpdate { Output = "long", Status = ToolCall.Status.Completed }),
                new ProviderEvent.ToolUpdated("fail", new ToolUpdate { Output = "exit 1", Status = ToolCall.Status.Failed }),
                new ProviderEvent.ToolUpdated("declined", new ToolUpdate { Output = "denied", Status = ToolCall.Status.Declined }),
                new ProviderEvent.ToolUpdated("plain", new ToolUpdate { Output = null, Status = ToolCall.Status.Completed }),
                new ProviderEvent.ToolUpdated("unknown", new ToolUpdate { Output = "out", Status = ToolCall.Status.Completed }),
            ],
            events);
        // The name is read, never removed.
        Assert.Equal("bash", names["ok"]);
    }

    // ---- PR-PROVCLI-182 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-182")] // Services/Providers/CopilotSession.swift:579-590
    public void Subagent_started_names_the_head_by_its_task_brief_and_consumes_it()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline(Config(hydra: Hydra()));
        Notify(runtime, session, "tool.execution_start", Json("{\"toolCallId\":\"c1\",\"toolName\":\"task\",\"arguments\":{\"description\":\"Audit the API\",\"prompt\":\"Read every route\"}}"));
        events.Clear();
        Notify(runtime, session, "subagent.started", Json("{\"toolCallId\":\"c1\",\"agentName\":\"hydra-reviewer\",\"agentDisplayName\":\"Reviewer\",\"model\":\"gpt-5\"}"), agentID: "a1");
        Notify(runtime, session, "subagent.started", Json("{\"toolCallId\":\"c1\",\"agentName\":\"hydra-reviewer\",\"agentDisplayName\":\"Reviewer\"}"), agentID: "a2");
        Notify(runtime, session, "subagent.started", Json("{\"agentName\":\"worker\"}"), agentID: "a3");
        Notify(runtime, session, "subagent.started", Json("{}"), agentID: "a4");
        var spawns = events.Select(e => Assert.IsType<ProviderEvent.AgentStarted>(e).Value).ToList();
        Assert.Equal(4, spawns.Count);
        Assert.Equal(new AgentSpawn { Id = "a1", TaskID = null, ToolUseID = "c1", Description = "Audit the API", Prompt = "Read every route", Model = "gpt-5", Profile = HydraPrompts.ProfileName("hydra-reviewer") }, spawns[0]);
        Assert.Equal(new AgentSpawn { Id = "a2", ToolUseID = "c1", Description = "Reviewer", Prompt = null, Model = null, Profile = HydraPrompts.ProfileName("hydra-reviewer") }, spawns[1]);
        Assert.Equal(new AgentSpawn { Id = "a3", Description = "worker", Profile = HydraPrompts.ProfileName("worker") }, spawns[2]);
        Assert.Equal(new AgentSpawn { Id = "a4", Description = "Subagent", Profile = HydraPrompts.ProfileName(null) }, spawns[3]);
        Assert.Empty(Field<Dictionary<string, (string Description, string? Prompt)>>(session, "taskBriefs"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-182")] // Services/Providers/CopilotSession.swift:591-596
    public void Subagent_completed_and_failed_finish_the_head_with_their_status()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline(Config(hydra: Hydra()));
        Notify(runtime, session, "subagent.completed", Json("{\"cancelled\":true}"), agentID: "a1");
        Notify(runtime, session, "subagent.completed", Json("{\"cancelled\":\"yes\"}"), agentID: "a2");
        Notify(runtime, session, "subagent.completed", Json("{}"), agentID: "a3");
        Notify(runtime, session, "subagent.failed", Json("{\"error\":\"Out of budget\"}"), agentID: "a4");
        Notify(runtime, session, "subagent.failed", Json("{}"), agentID: "a5");
        Assert.Equal(
            [
                new ProviderEvent.AgentFinished("a1", TurnStatus.Interrupted, null),
                new ProviderEvent.AgentFinished("a2", TurnStatus.Completed, null),
                new ProviderEvent.AgentFinished("a3", TurnStatus.Completed, null),
                new ProviderEvent.AgentFinished("a4", TurnStatus.Failed, "Out of budget"),
                new ProviderEvent.AgentFinished("a5", TurnStatus.Failed, null),
            ],
            events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-182")] // Services/Providers/CopilotSession.swift:19,580,592,595
    public void Subagent_events_are_ignored_without_Hydra_or_an_agent_id_and_a_head_cannot_be_stopped()
    {
        using var runtime = new TestRuntime();
        var (plain, plainEvents) = Offline();
        Notify(runtime, plain, "subagent.started", Json("{\"agentName\":\"x\"}"), agentID: "a1");
        Notify(runtime, plain, "subagent.completed", Json("{}"), agentID: "a1");
        Notify(runtime, plain, "subagent.failed", Json("{}"), agentID: "a1");
        Assert.Empty(plainEvents);
        var (hydra, hydraEvents) = Offline(Config(hydra: Hydra()));
        Notify(runtime, hydra, "subagent.started", Json("{\"agentName\":\"x\"}"));
        Notify(runtime, hydra, "subagent.completed", Json("{}"));
        Notify(runtime, hydra, "subagent.failed", Json("{}"));
        Assert.Empty(hydraEvents);
        Assert.False(runtime.Loop.Run(() => ((ProviderSession)hydra).StopAgent("a1"), Bound));
    }
}
