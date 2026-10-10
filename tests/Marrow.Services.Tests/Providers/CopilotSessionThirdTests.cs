using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Services.Tests.Fixtures.CopilotSessionHarness;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e). Part 3: PR-PROVCLI-183
// through 202 and 371 to 374 (session errors, compaction and mode events, finishing a turn, permission prompts and
// their automatic answers, server requests, close, the tool-row dispatch, patch paths, the to-do checklist, the
// running state, the absent session delete, and the MCP, hidden, shell and shell-session rows). Wire replies are
// proven against the offline stand-in tests/fixtures/copilot-cli; the handlers are driven directly otherwise.
public sealed class CopilotSessionThirdTests
{
    private const string PermissionMethod = "session.permissions.handlePendingPermissionRequest";

    private static JSONValue Permission(string requestID, string request, string? extra = null) =>
        Json("{\"requestId\":\"" + requestID + "\",\"permissionRequest\":" + request + (extra is null ? "" : "," + extra) + "}");

    private static ValueList<ApprovalRequest.Option> Options(bool always)
    {
        var options = new List<ApprovalRequest.Option> { new("allow", "Approve", ApprovalRequest.Option.Role.Approve) };
        if (always) options.Add(new("always", "Approve for session", ApprovalRequest.Option.Role.ApproveAlways));
        options.Add(new("deny", "Decline", ApprovalRequest.Option.Role.Decline));
        return new ValueList<ApprovalRequest.Option>(options);
    }

    private static global::Marrow.Core.Models.ToolCall? Row(CopilotSession session, string name, JSONValue arguments, string? server = null, string? mcpTool = null) =>
        (global::Marrow.Core.Models.ToolCall?)Invoke(session, "ToolCall", name, arguments, server, mcpTool);

    // ---- PR-PROVCLI-183 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-183")] // Services/Providers/CopilotSession.swift:616-622,631-632,695-715
    public void A_session_error_during_a_turn_becomes_the_turn_s_failure_and_the_later_one_wins()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        SetField(session, "turnActive", true);
        Notify(runtime, session, "session.error", Json("{\"message\":\"first\"}"));
        Notify(runtime, session, "session.error", Json("{\"message\":\"second\"}"));
        Assert.Empty(events);
        Assert.Equal("second", Field<string>(session, "turnError"));
        Notify(runtime, session, "session.idle", Json("{}"));
        Assert.Equal([new ProviderEvent.TurnCompleted(TurnStatus.Failed, "second")], events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-183")] // Services/Providers/CopilotSession.swift:616-622
    public void A_session_error_between_turns_is_an_error_notice_at_once()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "session.error", Json("{\"message\":\"Rate limited\"}"));
        Notify(runtime, session, "session.error");
        Assert.Equal(
            [
                new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "Rate limited" }),
                new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "Copilot reported an error." }),
            ],
            events);
        Assert.Null(RawField(session, "turnError"));
    }

    // ---- PR-PROVCLI-184 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-184")] // Services/Providers/CopilotSession.swift:718-726
    public void ErrorMessage_falls_back_and_rewrites_a_wrapped_HTTP_reply()
    {
        Assert.Equal("Copilot reported an error.", CopilotSession.ErrorMessage(Json("{}")));
        Assert.Equal("Copilot reported an error.", CopilotSession.ErrorMessage(Json("{\"message\":7}")));
        Assert.Equal("plain", CopilotSession.ErrorMessage(Json("{\"message\":\"plain\"}")));
        // The remainder after the marker is the HTTP reply as JSON; its body is trimmed of whitespace and newlines.
        Assert.Equal("HTTP 429: Too many requests",
            CopilotSession.ErrorMessage(Json("{\"message\":\"Failed to list models: GenericFailure, {\\\"status\\\":429,\\\"body\\\":\\\" \\\\n Too many requests \\\\n\\\"}\"}")));
        // A remainder that does not parse, or lacks an int status or a string body, leaves the message unchanged.
        const string unparsed = "Failed: GenericFailure, not json";
        Assert.Equal(unparsed, CopilotSession.ErrorMessage(Json("{\"message\":\"" + unparsed + "\"}")));
        var noBody = "x GenericFailure, {\\\"status\\\":500}";
        Assert.Equal("x GenericFailure, {\"status\":500}", CopilotSession.ErrorMessage(Json("{\"message\":\"" + noBody + "\"}")));
        var textStatus = "x GenericFailure, {\\\"status\\\":\\\"oops\\\",\\\"body\\\":\\\"b\\\"}";
        Assert.Equal("x GenericFailure, {\"status\":\"oops\",\"body\":\"b\"}", CopilotSession.ErrorMessage(Json("{\"message\":\"" + textStatus + "\"}")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-184")] // Services/Providers/CopilotSession.swift:727-731
    public void ErrorMessage_appends_the_Copilot_access_hint_on_a_403_a_sign_in_remediation_or_the_not_authorized_text()
    {
        const string hint = " Check that your GitHub account has Copilot access, or sign in again with `copilot login`.";
        Assert.Equal("Denied" + hint, CopilotSession.ErrorMessage(Json("{\"message\":\"Denied\",\"statusCode\":403}")));
        // JSONValue.int reads a numeric string and a whole double too (JSONValue.swift:83-90).
        Assert.Equal("Denied" + hint, CopilotSession.ErrorMessage(Json("{\"message\":\"Denied\",\"statusCode\":\"403\"}")));
        Assert.Equal("Denied" + hint, CopilotSession.ErrorMessage(Json("{\"message\":\"Denied\",\"statusCode\":403.0}")));
        Assert.Equal("Denied", CopilotSession.ErrorMessage(Json("{\"message\":\"Denied\",\"statusCode\":401}")));
        Assert.Equal("Copilot reported an error." + hint, CopilotSession.ErrorMessage(Json("{\"remediation\":\"sign_in\"}")));
        Assert.Equal("x", CopilotSession.ErrorMessage(Json("{\"message\":\"x\",\"remediation\":\"Sign_in\"}")));
        Assert.Equal("You are NOT AUTHORIZED TO USE THIS COPILOT FEATURE." + hint,
            CopilotSession.ErrorMessage(Json("{\"message\":\"You are NOT AUTHORIZED TO USE THIS COPILOT FEATURE.\"}")));
        // The hint is tested after the HTTP rewrite, against the rewritten text.
        Assert.Equal("HTTP 403: not authorized to use this Copilot feature" + hint,
            CopilotSession.ErrorMessage(Json("{\"message\":\"GenericFailure, {\\\"status\\\":403,\\\"body\\\":\\\"not authorized to use this Copilot feature\\\"}\"}")));
    }

    // ---- PR-PROVCLI-185 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-185")] // Services/Providers/CopilotSession.swift:625-630
    public void Compaction_is_an_info_notice_on_success_a_warning_on_a_reported_error_and_otherwise_silent()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "session.compaction_complete", Json("{\"success\":true,\"error\":\"ignored\"}"));
        Notify(runtime, session, "session.compaction_complete", Json("{\"success\":false,\"error\":\"Nothing to compact\"}"));
        Notify(runtime, session, "session.compaction_complete", Json("{\"success\":\"true\",\"error\":\"String success\"}"));
        Notify(runtime, session, "session.compaction_complete", Json("{\"success\":false}"));
        Notify(runtime, session, "session.compaction_complete", Json("{\"error\":7}"));
        Assert.Equal(
            [
                new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "Context compacted." }),
                new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "Nothing to compact" }),
                new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Warning, Message = "String success" }),
            ],
            events);
    }

    // ---- PR-PROVCLI-186 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-186")] // Services/Providers/CopilotSession.swift:603-613
    public void Mode_changes_follow_plan_interactive_and_autopilot_and_ignore_anything_else()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"plan\"}"));
        Assert.Equal(InteractionMode.Plan, Field<InteractionMode>(session, "interactionMode"));
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"shell\"}"));
        Notify(runtime, session, "session.mode_changed", Json("{}"));
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"Plan\"}"));
        Assert.Equal(InteractionMode.Plan, Field<InteractionMode>(session, "interactionMode"));
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"autopilot\"}"));
        Assert.Equal(InteractionMode.Build, Field<InteractionMode>(session, "interactionMode"));
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"plan\"}"));
        Notify(runtime, session, "session.mode_changed", Json("{\"newMode\":\"interactive\"}"));
        Assert.Equal(InteractionMode.Build, Field<InteractionMode>(session, "interactionMode"));
        Assert.Equal(
            [
                new ProviderEvent.ModeChanged(InteractionMode.Plan),
                new ProviderEvent.ModeChanged(InteractionMode.Build),
                new ProviderEvent.ModeChanged(InteractionMode.Plan),
                new ProviderEvent.ModeChanged(InteractionMode.Build),
            ],
            events);
    }

    // ---- PR-PROVCLI-187 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-187")] // Services/Providers/CopilotSession.swift:695-715
    public void Finishing_a_turn_clears_four_caches_keeps_streamed_messages_withdraws_prompts_and_reports_last()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        SetField(session, "turnActive", true);
        SetField(session, "turnError", "boom");
        Field<Dictionary<string, string>>(session, "toolNames")["c1"] = "bash";
        Field<HashSet<string>>(session, "declinedTools").Add("c1");
        Field<HashSet<string>>(session, "streamedMessages").Add("m1");
        Field<HashSet<string>>(session, "shownReasoning").Add("thought");
        var briefs = RawField(session, "taskBriefs")!;
        briefs.GetType().GetMethod("set_Item")!.Invoke(briefs, ["t1", ("Look", (string?)null)]);
        AddPending(session, "req-1", Pending("Permission", Json("{\"kind\":\"shell\"}"), null));
        Notify(runtime, session, "session.idle", Json("{}"));
        Assert.Equal([new ProviderEvent.RequestResolved("req-1"), new ProviderEvent.TurnCompleted(TurnStatus.Failed, "boom")], events);
        Assert.False(Field<bool>(session, "turnActive"));
        Assert.Empty(Field<Dictionary<string, string>>(session, "toolNames"));
        Assert.Empty(Field<HashSet<string>>(session, "declinedTools"));
        Assert.Empty(Field<HashSet<string>>(session, "shownReasoning"));
        Assert.Equal(0, (int)briefs.GetType().GetProperty("Count")!.GetValue(briefs)!);
        Assert.Contains("m1", Field<HashSet<string>>(session, "streamedMessages"));
        Assert.Empty(PendingRequests(session));
        Assert.Null(RawField(session, "turnError"));

        // No active turn: nothing at all.
        events.Clear();
        Notify(runtime, session, "session.idle", Json("{\"aborted\":true}"));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-187")] // Services/Providers/CopilotSession.swift:707-714
    public void An_aborted_or_interrupted_turn_reports_interrupted_even_with_a_stored_error_and_resets_the_flag()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        SetField(session, "turnActive", true);
        SetField(session, "turnError", "boom");
        Notify(runtime, session, "session.idle", Json("{\"aborted\":true}"));
        SetField(session, "turnActive", true);
        SetField(session, "interruptRequested", true);
        Notify(runtime, session, "session.idle", Json("{\"aborted\":\"true\"}"));
        Assert.False(Field<bool>(session, "interruptRequested"));
        SetField(session, "turnActive", true);
        Notify(runtime, session, "session.idle", Json("{\"aborted\":false}"));
        Assert.Equal(
            [
                new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null),
                new ProviderEvent.TurnCompleted(TurnStatus.Interrupted, null),
                new ProviderEvent.TurnCompleted(TurnStatus.Completed, null),
            ],
            events);
    }

    // ---- PR-PROVCLI-188 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-188")] // Services/Providers/CopilotSession.swift:776-783,806-824
    public void The_mode_answers_what_it_allows_without_a_prompt_and_a_hook_resolution_is_ignored()
    {
        using var runtime = new TestRuntime();
        void Expect(RuntimeMode mode, string request, bool asked, string? prompt = null)
        {
            var (session, events) = Offline(Config(runtimeMode: mode));
            Notify(runtime, session, "permission.requested", Permission("p", request, prompt));
            Assert.Equal(asked, events.OfType<ProviderEvent.Approval>().Any());
            Assert.Equal(asked ? 1 : 0, PendingRequests(session).Count);
        }
        const string memory = "{\"kind\":\"memory\",\"fact\":\"f\"}";
        const string mcp = "{\"kind\":\"mcp\",\"serverName\":\"github\"}";
        Expect(RuntimeMode.FullAccess, memory, asked: false);
        Expect(RuntimeMode.FullAccess, mcp, asked: false);
        Expect(RuntimeMode.AutoAcceptEdits, "{\"kind\":\"write\"}", asked: false);
        Expect(RuntimeMode.AutoAcceptEdits, "{\"kind\":\"read\"}", asked: false);
        Expect(RuntimeMode.AutoAcceptEdits, memory, asked: true);
        Expect(RuntimeMode.Auto, "{\"kind\":\"write\"}", asked: false);
        Expect(RuntimeMode.Auto, "{\"kind\":\"read\"}", asked: false);
        Expect(RuntimeMode.Auto, memory, asked: true);
        // The judge's recommendation: promptRequest first, then the request's own.
        Expect(RuntimeMode.Auto, memory, asked: false, prompt: "\"promptRequest\":{\"assistedApproval\":{\"recommendation\":\"approve\"}}");
        Expect(RuntimeMode.Auto, "{\"kind\":\"memory\",\"assistedApproval\":{\"recommendation\":\"approve\"}}", asked: false);
        Expect(RuntimeMode.Auto, "{\"kind\":\"memory\",\"assistedApproval\":{\"recommendation\":\"approve\"}}", asked: true,
            prompt: "\"promptRequest\":{\"assistedApproval\":{\"recommendation\":\"deny\"}}");
        Expect(RuntimeMode.Auto, memory, asked: true, prompt: "\"promptRequest\":{\"assistedApproval\":{\"recommendation\":\"Approve\"}}");
        Expect(RuntimeMode.Supervised, "{\"kind\":\"memory\",\"assistedApproval\":{\"recommendation\":\"approve\"}}", asked: true);

        // resolvedByHook exactly true: ignored entirely, whatever the mode.
        foreach (var mode in new[] { RuntimeMode.Supervised, RuntimeMode.FullAccess })
        {
            var (session, events) = Offline(Config(runtimeMode: mode));
            Notify(runtime, session, "permission.requested", Permission("p", memory, "\"resolvedByHook\":true"));
            Assert.Empty(events);
            Assert.Empty(PendingRequests(session));
        }
        var (stringHook, stringHookEvents) = Offline();
        Notify(runtime, stringHook, "permission.requested", Permission("p", memory, "\"resolvedByHook\":\"true\""));
        Assert.Single(stringHookEvents.OfType<ProviderEvent.Approval>());

        // No string requestId or no permissionRequest: dropped.
        var (bare, bareEvents) = Offline();
        Notify(runtime, bare, "permission.requested", Json("{\"permissionRequest\":" + memory + "}"));
        Notify(runtime, bare, "permission.requested", Json("{\"requestId\":\"p\"}"));
        Assert.Empty(bareEvents);
        Assert.Empty(PendingRequests(bare));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-188")] // Services/Providers/CopilotSession.swift:780-783,808-814
    [Trait("Req", "PR-PROVCLI-192")] // Services/Providers/CopilotSession.swift:904-913
    public void An_automatic_approval_is_approve_once_without_the_interactive_flag_sent_at_once()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("automatic");
        var (session, events, id) = Started(runtime, Config(script, runtimeMode: RuntimeMode.FullAccess));
        events.Clear();
        Notify(runtime, session, Event("permission.requested", Permission("perm-9", "{\"kind\":\"shell\",\"fullCommandText\":\"rm -rf build\"}"), sessionID: id));
        runtime.Loop.RunUntil(() => script.Requests(PermissionMethod).Count == 1, Bound);
        var sent = Params(script.Requests(PermissionMethod).Single());
        Assert.Equal(["requestId", "result", "sessionId"], Keys(sent));
        Assert.Equal(id, sent["sessionId"]!.JSONValueString);
        Assert.Equal("perm-9", sent["requestId"]!.JSONValueString);
        Assert.Equal(Json("{\"kind\":\"approve-once\"}"), sent["result"]);
        Assert.Empty(events.OfType<ProviderEvent.Approval>());
        Assert.Empty(PendingRequests(session));
    }

    // ---- PR-PROVCLI-189 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-189")] // Services/Providers/CopilotSession.swift:783-790
    public void A_write_prompt_shows_the_CLI_s_own_patch_on_the_row_before_asking()
    {
        using var runtime = new TestRuntime();
        var root = Path.Combine(Path.GetTempPath(), "copilot-wd");
        var (session, events) = Offline(Config(workingDirectory: root));
        const string diff = "--- a/x\n+++ b/x\n@@\n-old\n+new\n+more\n";
        var file = Path.Combine(root, "src", "a.swift");
        Notify(runtime, session, "permission.requested", Permission("p1",
            JSONValue.FromDictionaryLiteral(("kind", "write"), ("toolCallId", "c1"), ("resolvedPath", file), ("fileName", "ignored"), ("diff", diff)).CompactString));
        Assert.Equal(2, events.Count);
        var updated = Assert.IsType<ProviderEvent.ToolUpdated>(events[0]);
        Assert.Equal("c1", updated.Id);
        var relative = ToolTitles.RelativePath(file, root);
        Assert.Equal(new ToolUpdate { Edits = new ValueList<FileEdit>([new FileEdit(relative, diff, 2, 1)]) }, updated.Update);
        Assert.IsType<ProviderEvent.Approval>(events[1]);
        Assert.True(PendingRequests(session).Contains("p1"));

        // fileName, then the empty string, when resolvedPath is missing.
        events.Clear();
        Notify(runtime, session, "permission.requested", Permission("p2", "{\"kind\":\"write\",\"toolCallId\":\"c2\",\"fileName\":\"b.txt\",\"diff\":\"+x\"}"));
        Notify(runtime, session, "permission.requested", Permission("p3", "{\"kind\":\"write\",\"toolCallId\":\"c3\",\"diff\":\"-y\"}"));
        var edits = events.OfType<ProviderEvent.ToolUpdated>().Select(e => e.Update.Edits!.Single()).ToList();
        Assert.Equal([new FileEdit("b.txt", "+x", 1, 0), new FileEdit("", "-y", 0, 1)], edits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-189")] // Services/Providers/CopilotSession.swift:784-790
    public void No_patch_update_without_a_write_kind_a_tool_call_id_or_a_non_empty_diff()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "permission.requested", Permission("p1", "{\"kind\":\"memory\",\"toolCallId\":\"c1\",\"diff\":\"+x\"}"));
        Notify(runtime, session, "permission.requested", Permission("p2", "{\"kind\":\"memory\",\"diff\":\"+x\"}"));
        Notify(runtime, session, "permission.requested", Permission("p3", "{\"kind\":\"custom-tool\",\"toolCallId\":\"c3\",\"diff\":\"\"}"));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.IsType<ProviderEvent.Approval>(e));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-189")] // Services/Providers/CopilotSession.swift:784-790
    public void A_write_prompt_without_a_tool_call_id_or_with_an_empty_or_non_string_diff_updates_no_row()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "permission.requested", Permission("p1", "{\"kind\":\"write\",\"fileName\":\"a\",\"diff\":\"+x\"}"));
        Notify(runtime, session, "permission.requested", Permission("p2", "{\"kind\":\"write\",\"toolCallId\":\"c2\",\"fileName\":\"a\",\"diff\":\"\"}"));
        Notify(runtime, session, "permission.requested", Permission("p3", "{\"kind\":\"write\",\"toolCallId\":\"c3\",\"fileName\":\"a\",\"diff\":7}"));
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.IsType<ProviderEvent.Approval>(e));
    }

    // ---- PR-PROVCLI-190 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-190")] // Services/Providers/CopilotSession.swift:791-803
    public void The_prompt_offers_approve_for_session_only_when_one_can_be_built_and_carries_its_ids()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        Notify(runtime, session, "permission.requested", Permission("p-mem", "{\"kind\":\"memory\",\"fact\":\"tabs\",\"toolCallId\":\"c1\"}"));
        Notify(runtime, session, "permission.requested", Permission("p-hook", "{\"kind\":\"hook\",\"toolName\":\"deploy\"}"));
        var approvals = events.OfType<ProviderEvent.Approval>().Select(a => a.Value).ToList();
        Assert.Equal(new ApprovalRequest("p-mem", ApprovalRequest.Kind.Tool, "Remember: tabs", Options(always: true), ToolItemID: "c1"), approvals[0]);
        Assert.Equal(new ApprovalRequest("p-hook", ApprovalRequest.Kind.Tool, "deploy", Options(always: false)), approvals[1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-190")] // Services/Providers/CopilotSession.swift:800-802
    public void The_judge_s_reason_is_adopted_only_when_the_description_has_none_and_it_is_not_empty()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        const string judged = "\"promptRequest\":{\"assistedApproval\":{\"reason\":\"Fetches public docs\"}}";
        Notify(runtime, session, "permission.requested", Permission("a", "{\"kind\":\"url\",\"url\":\"https://x.dev/\"}", judged));
        Notify(runtime, session, "permission.requested", Permission("b", "{\"kind\":\"url\",\"url\":\"https://x.dev/\",\"intention\":\"Read the docs\"}", judged));
        Notify(runtime, session, "permission.requested", Permission("c", "{\"kind\":\"url\",\"url\":\"https://x.dev/\"}", "\"promptRequest\":{\"assistedApproval\":{\"reason\":\"\"}}"));
        Notify(runtime, session, "permission.requested", Permission("d", "{\"kind\":\"url\",\"url\":\"https://x.dev/\",\"intention\":\"\"}", judged));
        var reasons = events.OfType<ProviderEvent.Approval>().Select(a => a.Value.Reason).ToList();
        Assert.Equal(["Fetches public docs", "Read the docs", null, "Fetches public docs"], reasons);
    }

    // ---- PR-PROVCLI-191 ----

    private static ApprovalRequest Described(TestRuntime runtime, string request, string? workingDirectory = null)
    {
        var (session, events) = Offline(Config(workingDirectory: workingDirectory));
        Notify(runtime, session, "permission.requested", Permission("r", request));
        return Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-191")] // Services/Providers/CopilotSession.swift:826-871,1068-1071
    public void Kinds_without_paths_are_described_with_their_exact_titles_details_and_reasons()
    {
        using var runtime = new TestRuntime();
        (string Title, string? Detail, string? Reason, ApprovalRequest.Kind Kind) Of(string request)
        {
            var approval = Described(runtime, request);
            return (approval.Title, approval.Detail, approval.Reason, approval.RequestKind);
        }
        const string Dot = " \U000000B7 "; // U+00B7 MIDDLE DOT
        Assert.Equal(("git status", null, "Check the tree", ApprovalRequest.Kind.Command),
            Of("{\"kind\":\"shell\",\"fullCommandText\":\"/bin/zsh -lc 'git status'\",\"intention\":\"Check the tree\"}"));
        Assert.Equal(("Command", null, null, ApprovalRequest.Kind.Command), Of("{\"kind\":\"shell\",\"intention\":\"\"}"));
        Assert.Equal(("make", "Needs the network", null, ApprovalRequest.Kind.Command),
            Of("{\"kind\":\"shell\",\"fullCommandText\":\"make\",\"requestSandboxBypass\":true,\"requestSandboxBypassReason\":\"Needs the network\"}"));
        Assert.Equal(("make", "Runs outside the sandbox.", null, ApprovalRequest.Kind.Command),
            Of("{\"kind\":\"shell\",\"fullCommandText\":\"make\",\"requestSandboxBypass\":true}"));
        Assert.Equal(("make", null, null, ApprovalRequest.Kind.Command),
            Of("{\"kind\":\"shell\",\"fullCommandText\":\"make\",\"requestSandboxBypass\":\"true\",\"requestSandboxBypassReason\":\"x\"}"));
        Assert.Equal(("Fetch https://x.dev/a", "Redirected from https://x.dev", "Docs", ApprovalRequest.Kind.Permissions),
            Of("{\"kind\":\"url\",\"url\":\"https://x.dev/a\",\"redirectedFrom\":\"https://x.dev\",\"intention\":\"Docs\"}"));
        Assert.Equal(("Fetch a URL", null, null, ApprovalRequest.Kind.Permissions), Of("{\"kind\":\"url\"}"));
        Assert.Equal(("github" + Dot + "Search issues", "{\"q\":\"bug\"}", null, ApprovalRequest.Kind.Tool),
            Of("{\"kind\":\"mcp\",\"serverName\":\"github\",\"toolTitle\":\"Search issues\",\"toolName\":\"search\",\"args\":{\"q\":\"bug\"},\"intention\":\"ignored\"}"));
        Assert.Equal(("MCP" + Dot + "search", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"mcp\",\"toolName\":\"search\",\"args\":null}"));
        Assert.Equal(("MCP" + Dot + "tool", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"mcp\"}"));
        Assert.Equal(("MCP" + Dot + "tool", "[1,2]", null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"mcp\",\"args\":[1,2]}"));
        Assert.Equal(("Remember: tabs over spaces", null, "User said so", ApprovalRequest.Kind.Tool),
            Of("{\"kind\":\"memory\",\"fact\":\"tabs over spaces\",\"reason\":\"User said so\",\"intention\":\"ignored\"}"));
        Assert.Equal(("Remember: something about this project", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"memory\"}"));
        Assert.Equal(("deploy", "Ships it", null, ApprovalRequest.Kind.Tool),
            Of("{\"kind\":\"custom-tool\",\"toolName\":\"deploy\",\"toolDescription\":\"Ships it\",\"intention\":\"ignored\"}"));
        Assert.Equal(("Tool", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"custom-tool\"}"));
        Assert.Equal(("Run the linter first", "lint", null, ApprovalRequest.Kind.Tool),
            Of("{\"kind\":\"hook\",\"toolName\":\"lint\",\"hookMessage\":\"Run the linter first\"}"));
        Assert.Equal(("Tool", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"hook\"}"));
        // A hookMessage that is present but not a string: the title falls back, and the detail is the tool name.
        Assert.Equal(("lint", "lint", null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"hook\",\"toolName\":\"lint\",\"hookMessage\":7}"));
        Assert.Equal(("Install github-mcp", null, null, ApprovalRequest.Kind.Tool),
            Of("{\"kind\":\"extension-management\",\"operation\":\"install\",\"extensionName\":\"github-mcp\"}"));
        Assert.Equal(("Manage an extension", null, null, ApprovalRequest.Kind.Tool), Of("{\"kind\":\"extension-management\"}"));
        Assert.Equal(("Future tool kind", null, "Why", ApprovalRequest.Kind.Tool), Of("{\"kind\":\"future-tool_kind\",\"intention\":\"Why\"}"));
        Assert.Equal(("Action", null, null, ApprovalRequest.Kind.Tool), Of("{}"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-191")] // Services/Providers/CopilotSession.swift:829-848
    public void Path_kinds_are_described_relative_to_the_working_directory()
    {
        using var runtime = new TestRuntime();
        var root = Path.Combine(Path.GetTempPath(), "copilot-wd");
        var other = Path.Combine(Path.GetTempPath(), "elsewhere");
        var inside = Path.Combine(root, "pkg");
        const string Dot = " \U000000B7 "; // U+00B7 MIDDLE DOT
        ApprovalRequest Of(string request) => Described(runtime, request, root);
        string Rel(string path) => ToolTitles.RelativePath(path, root);

        var shell = Of(JSONValue.FromDictionaryLiteral(("kind", "shell"), ("fullCommandText", "ls"), ("resolvedWorkingDirectory", inside),
            ("requestSandboxBypass", true)).CompactString);
        Assert.Equal("ls", shell.Title);
        Assert.Equal(Rel(inside) + Dot + "Runs outside the sandbox.", shell.Detail);
        Assert.Null(Of(JSONValue.FromDictionaryLiteral(("kind", "shell"), ("fullCommandText", "ls"), ("resolvedWorkingDirectory", root)).CompactString).Detail);
        Assert.Equal(Rel(other), Of(JSONValue.FromDictionaryLiteral(("kind", "shell"), ("resolvedWorkingDirectory", other)).CompactString).Detail);

        var file = Path.Combine(root, "a.txt");
        var write = Of(JSONValue.FromDictionaryLiteral(("kind", "write"), ("resolvedPath", file), ("fileName", "b.txt"), ("intention", "Fix")).CompactString);
        Assert.Equal((ApprovalRequest.Kind.FileChange, Rel(file), "Fix"), (write.RequestKind, write.Title, write.Reason));
        Assert.Equal(Rel("b.txt"), Of("{\"kind\":\"write\",\"fileName\":\"b.txt\"}").Title);
        Assert.Equal(Rel("file"), Of("{\"kind\":\"write\"}").Title);

        var read = Of(JSONValue.FromDictionaryLiteral(("kind", "read"), ("resolvedPath", file), ("path", "b.txt")).CompactString);
        Assert.Equal((ApprovalRequest.Kind.Permissions, "Read " + Rel(file)), (read.RequestKind, read.Title));
        Assert.Equal("Read " + Rel("b.txt"), Of("{\"kind\":\"read\",\"path\":\"b.txt\"}").Title);
        Assert.Equal("Read " + Rel("file"), Of("{\"kind\":\"read\",\"fileName\":\"ignored\"}").Title);
    }

    // ---- PR-PROVCLI-193 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-193")] // Services/Providers/CopilotSession.swift:737-752
    public void A_userInput_request_becomes_one_question_keyed_by_the_RPC_id()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        runtime.OnMain(() => Invoke(session, "HandleRequest", new RPCID.Int(5), "userInput.request",
            Json("{\"question\":\"Which color?\",\"choices\":[\"Red\",7,\"Blue\"],\"allowFreeform\":false}")));
        runtime.OnMain(() => Invoke(session, "HandleRequest", new RPCID.String("q-x"), "userInput.request", Json("{}")));
        Assert.Equal(
            [
                new ProviderEvent.Question(new QuestionRequest("question-5", new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question("answer", "", "Which color?",
                        new ValueList<QuestionRequest.Choice>([new("Red"), new("Blue")]), AllowsMultiple: false, AllowsOther: false, IsSecret: false),
                ]))),
                new ProviderEvent.Question(new QuestionRequest("question-q-x", new ValueList<QuestionRequest.Question>([
                    new QuestionRequest.Question("answer", "", "", ValueList<QuestionRequest.Choice>.Empty, AllowsMultiple: false, AllowsOther: true, IsSecret: false),
                ]))),
            ],
            events);
        Assert.Equal(Pending("Question", new RPCID.Int(5), new ValueList<string>(["Red", "Blue"])), PendingRequests(session)["question-5"]);
        Assert.Equal(Pending("Question", new RPCID.String("q-x"), ValueList<string>.Empty), PendingRequests(session)["question-q-x"]);
    }

    // ---- PR-PROVCLI-194 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-194")] // Services/Providers/CopilotSession.swift:753-767
    public void An_exitPlanMode_request_shows_the_plan_then_a_two_option_approval_on_the_same_key()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        runtime.OnMain(() => Invoke(session, "HandleRequest", new RPCID.Int(8), "exitPlanMode.request",
            Json("{\"planContent\":\"# Plan\\n1. Do it\",\"summary\":\"short\"}")));
        runtime.OnMain(() => Invoke(session, "HandleRequest", new RPCID.Int(9), "exitPlanMode.request", Json("{\"planContent\":\"\",\"summary\":\"short\"}")));
        runtime.OnMain(() => Invoke(session, "HandleRequest", new RPCID.Int(10), "exitPlanMode.request", Json("{\"planContent\":5}")));
        ApprovalRequest Approval(string key) => new(key, ApprovalRequest.Kind.Plan, "Implement this plan",
            new ValueList<ApprovalRequest.Option>([
                new("allow", "Implement plan", ApprovalRequest.Option.Role.Approve),
                new("deny", "Keep planning", ApprovalRequest.Option.Role.Decline),
            ]),
            ToolItemID: key);
        Assert.Equal(
            [
                new ProviderEvent.PlanCompleted("plan-8", "# Plan\n1. Do it"),
                new ProviderEvent.Approval(Approval("plan-8")),
                new ProviderEvent.PlanCompleted("plan-9", "short"),
                new ProviderEvent.Approval(Approval("plan-9")),
                new ProviderEvent.PlanCompleted("plan-10", ""),
                new ProviderEvent.Approval(Approval("plan-10")),
            ],
            events);
        Assert.Equal(Pending("PlanExit", new RPCID.Int(8)), PendingRequests(session)["plan-8"]);
    }

    // ---- PR-PROVCLI-195, 196 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-195")] // Services/Providers/CopilotSession.swift:768-770
    [Trait("Req", "PR-PROVCLI-196")] // Services/Providers/CopilotSession.swift:771-773
    public void An_Auto_tier_switch_is_refused_with_no_and_an_unknown_request_gets_minus_32601_naming_it()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("server-requests");
        var (session, events, _) = Started(runtime, Config(script));
        events.Clear();
        runtime.OnMain(() =>
        {
            Invoke(session, "HandleRequest", new RPCID.Int(31), "autoModeSwitch.request", Json("{\"reason\":\"rate_limit\"}"));
            Invoke(session, "HandleRequest", new RPCID.Int(32), "sampling.createMessage", Json("{}"));
        });
        runtime.Loop.RunUntil(() => script.Responses().Count == 2, Bound);
        var auto = script.Responses().Single(r => r["id"]!.JSONValueInt == 31);
        Assert.Equal(Json("{\"response\":\"no\"}"), auto["result"]);
        Assert.Null(auto["error"]);
        var unknown = script.Responses().Single(r => r["id"]!.JSONValueInt == 32);
        Assert.Null(unknown["result"]);
        Assert.Equal(-32601, unknown["error"]!["code"]!.JSONValueInt);
        // DEVIATION OD-0012 F1: upstream says "Droppy Code does not support sampling.createMessage."
        Assert.Equal("nth does not support sampling.createMessage.", unknown["error"]!["message"]!.JSONValueString);
        Assert.DoesNotContain(events, e => e is ProviderEvent.Question or ProviderEvent.Approval or ProviderEvent.PlanCompleted);
        Assert.Empty(PendingRequests(session));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-195")] // Services/Providers/CopilotSession.swift:768-773
    public void Without_a_connection_the_refusals_send_nothing_and_store_nothing()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        runtime.OnMain(() =>
        {
            Invoke(session, "HandleRequest", new RPCID.Int(1), "autoModeSwitch.request", Json("{}"));
            Invoke(session, "HandleRequest", new RPCID.Int(2), "anything.else", Json("{}"));
        });
        Assert.Empty(events);
        Assert.Empty(PendingRequests(session));
    }

    // ---- PR-PROVCLI-192 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-192")] // Services/Providers/CopilotSession.swift:904-905
    public void A_permission_reply_without_a_session_id_is_a_silent_no_op()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Make(Config(runtimeMode: RuntimeMode.FullAccess));
        Notify(runtime, session, "permission.requested", Permission("p", "{\"kind\":\"memory\"}"));
        Assert.Empty(events);
        Assert.Empty(PendingRequests(session));
    }

    // ---- PR-PROVCLI-197 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-197")] // Services/Providers/CopilotSession.swift:931-938
    public void Close_withdraws_every_prompt_without_replying_ends_the_turn_silently_and_reports_the_exit()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        SetField(session, "turnActive", true);
        AddPending(session, "p1", Pending("Permission", Json("{\"kind\":\"shell\"}"), "c1"));
        AddPending(session, "question-2", Pending("Question", new RPCID.Int(2), ValueList<string>.Empty));
        runtime.OnMain(() => Invoke(session, "HandleClose", "\u001B[31mfatal:\u001B[0m first\n\n  second  \nthird\nfourth\r\nfifth\n"));
        Assert.Equal(["p1", "question-2"], events.OfType<ProviderEvent.RequestResolved>().Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList());
        Assert.Equal(new ProviderEvent.Exited("second\nthird\nfourth\nfifth"), events[^1]);
        Assert.Equal(3, events.Count);
        Assert.Empty(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.False(Field<bool>(session, "turnActive"));
        Assert.Empty(PendingRequests(session));
        Assert.Empty(Field<HashSet<string>>(session, "declinedTools"));

        // An empty tail reports an exit with no error.
        var (quiet, quietEvents) = Offline();
        runtime.OnMain(() => Invoke(quiet, "HandleClose", " \n"));
        Assert.Equal([new ProviderEvent.Exited(null)], quietEvents);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-197")] // Services/Providers/CopilotSession.swift:936
    public void Close_after_stop_resolves_prompts_but_reports_no_exit()
    {
        using var runtime = new TestRuntime();
        var (session, events) = Offline();
        SetField(session, "isStopping", true);
        SetField(session, "turnActive", true);
        AddPending(session, "p1", Pending("PlanExit", new RPCID.Int(1)));
        runtime.OnMain(() => Invoke(session, "HandleClose", "bye"));
        Assert.Equal([new ProviderEvent.RequestResolved("p1")], events);
        Assert.False(Field<bool>(session, "turnActive"));
    }

    // ---- PR-PROVCLI-198, 371, 372 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-198")] // Services/Providers/CopilotSession.swift:945-955
    [Trait("Req", "PR-PROVCLI-371")] // Services/Providers/CopilotSession.swift:945-949
    public void A_server_makes_an_MCP_row_before_the_hidden_set_or_any_name_is_tested()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Offline();
        const string Dot = " \U000000B7 "; // U+00B7 MIDDLE DOT
        var arguments = Json("{\"q\":\"is:open\",\"n\":2}");
        Assert.Equal(new { Kind = ToolCall.Kind.Mcp, Title = "github" + Dot + "search_issues", Detail = (string?)arguments.CompactString },
            Shape(Row(session, "github-search_issues", arguments, "github", "search_issues")));
        Assert.Equal(new { Kind = ToolCall.Kind.Mcp, Title = "github" + Dot + "bash", Detail = (string?)null },
            Shape(Row(session, "bash", new JSONValue.Null(), "github")));
        Assert.Equal(new { Kind = ToolCall.Kind.Mcp, Title = "tools" + Dot + "ask_user", Detail = (string?)"{}" },
            Shape(Row(session, "ask_user", Json("{}"), "tools")));
        var mcp = Row(session, "create", Json("{\"path\":\"/x\",\"file_text\":\"y\"}"), "fs", "create")!;
        Assert.Empty(mcp.Edits);
        Assert.Equal("", mcp.Output);
        Assert.Equal(ToolCall.Status.Running, mcp.ToolCallStatus);
    }

    private static object Shape(global::Marrow.Core.Models.ToolCall? call) =>
        call is null ? "nil" : new { Kind = call.ToolCallKind, call.Title, call.Detail };

    [Fact]
    [Trait("Req", "PR-PROVCLI-372")] // Services/Providers/CopilotSession.swift:942-943,954-955
    public void Five_tools_never_get_a_row()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Offline();
        foreach (var name in new[] { "ask_user", "exit_plan_mode", "update_todo", "task_complete", "report_intent" })
        {
            Assert.Null(Row(session, name, Json("{\"command\":\"view\"}")));
        }
        Assert.NotNull(Row(session, "Ask_user", new JSONValue.Null()));
    }

    // ---- PR-PROVCLI-373, 374 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-373")] // Services/Providers/CopilotSession.swift:956-957
    public void Bash_and_powershell_are_command_rows_titled_with_the_command()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Offline();
        foreach (var name in new[] { "bash", "powershell" })
        {
            Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "npm test", Detail = (string?)"Run the tests" },
                Shape(Row(session, name, Json("{\"command\":\"npm test\",\"description\":\"Run the tests\"}"))));
            Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "Shell command", Detail = (string?)null },
                Shape(Row(session, name, Json("{\"command\":7,\"description\":false}"))));
            Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "Shell command", Detail = (string?)null },
                Shape(Row(session, name, new JSONValue.Null())));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-374")] // Services/Providers/CopilotSession.swift:958-959,1068-1071
    public void The_shell_session_tools_are_humanized_command_rows_subtitled_with_the_shell_id()
    {
        using var runtime = new TestRuntime();
        var (session, _) = Offline();
        Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "Read bash", Detail = (string?)"shell-1" },
            Shape(Row(session, "read_bash", Json("{\"shellId\":\"shell-1\",\"shell_id\":\"no\"}"))));
        Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "Write bash", Detail = (string?)null },
            Shape(Row(session, "write_bash", Json("{\"shell_id\":\"snake\"}"))));
        Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "Stop bash", Detail = (string?)null },
            Shape(Row(session, "stop_bash", new JSONValue.Null())));
        Assert.Equal(new { Kind = ToolCall.Kind.Command, Title = "List bash", Detail = (string?)"7" },
            Shape(Row(session, "list_bash", Json("{\"shellId\":\"7\"}"))));
    }

    // ---- PR-PROVCLI-199 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-199")] // Services/Providers/CopilotSession.swift:1034-1045
    public void PatchPaths_reads_the_three_headers_trimmed_in_first_occurrence_order()
    {
        const string patch = "*** Begin Patch\n*** Update File: src/a.swift  \n@@\n-x\n+y\n" +
            "*** Add File:  b.txt\r\n+new\n*** Delete File: c.md\n*** Update File: src/a.swift\n" +
            "*** Move to: d.txt\n*** Update File:    \n  *** Add File: indented.txt\n*** update file: lower.txt\n*** End Patch";
        Assert.Equal(["src/a.swift", "b.txt", "c.md"], CopilotSession.PatchPaths(patch));
        Assert.Empty(CopilotSession.PatchPaths(""));
        Assert.Empty(CopilotSession.PatchPaths("no headers\nat all"));
        // Only .whitespaces are trimmed; a vertical tab is a newline here, so it splits the line instead.
        Assert.Equal(["one", "two"], CopilotSession.PatchPaths("*** Add File: one\u000B*** Add File: two\t"));
    }

    // ---- PR-PROVCLI-200 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-200")] // Services/Providers/CopilotSession.swift:1047-1062
    public void Todos_parse_bullets_and_numbers_with_x_done_blank_pending_and_anything_else_active()
    {
        const string markdown = "# Plan\n- [x] Read the code\n  * [X] Upper-case done\n+ [ ] Write tests\n1. [] Empty brackets\n" +
            "2) [~] Tilde\n3.[-]Tight\n- [x]\n- no brackets\n- [ab] two marks\n-[ ]   spaced text\n\n* [ ] \tlast";
        var steps = CopilotSession.Todos(markdown);
        Assert.Equal(
            [
                ("Read the code", TodoStep.Status.Done),
                ("Upper-case done", TodoStep.Status.Done),
                ("Write tests", TodoStep.Status.Pending),
                ("Empty brackets", TodoStep.Status.Pending),
                ("Tilde", TodoStep.Status.Active),
                ("Tight", TodoStep.Status.Active),
                ("spaced text", TodoStep.Status.Pending),
                ("last", TodoStep.Status.Pending),
            ],
            steps.Select(s => (s.Text, s.TodoStepStatus)).ToList());
        Assert.Empty(CopilotSession.Todos(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-200")] // Services/Providers/CopilotSession.swift:1049,1054-1058
    public void The_bracket_holds_one_Character_tried_before_none()
    {
        // `(.?)` is greedy: "[]]" reads "]" as the mark; "[] ]" reads an empty mark and "]" as text.
        Assert.Equal([("x", TodoStep.Status.Active)], CopilotSession.Todos("- []] x").Select(s => (s.Text, s.TodoStepStatus)).ToList());
        Assert.Equal([("]", TodoStep.Status.Pending)], CopilotSession.Todos("- [] ]").Select(s => (s.Text, s.TodoStepStatus)).ToList());
        // One Character, even when it is two UTF-16 units: an emoji mark is in progress.
        Assert.Equal([("Ship", TodoStep.Status.Active)],
            CopilotSession.Todos("- [\U0001F504] Ship").Select(s => (s.Text, s.TodoStepStatus)).ToList()); // U+1F504 ANTICLOCKWISE ARROWS
        // Whitespace inside the text is kept; only the run after the bracket is dropped.
        Assert.Equal([("a  b", TodoStep.Status.Done)], CopilotSession.Todos("- [x]  a  b").Select(s => (s.Text, s.TodoStepStatus)).ToList());
    }

    // ---- PR-PROVCLI-201 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-201")] // Services/Providers/CopilotSession.swift:82-85
    public void The_session_runs_only_while_its_connection_is_open()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        Assert.False(Make(Config()).Session.IsRunning);
        var script = new CopilotScript("running");
        var (session, _, _) = Started(runtime, Config(script));
        Assert.True(session.IsRunning);
        runtime.OnMain(session.Stop);
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
    }

    // ---- PR-PROVCLI-202 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-202")] // Services/Providers/CopilotSession.swift:24; porting/dead_code.md entry 5
    public void No_session_delete_is_ever_sent_through_a_whole_session_life()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("no-delete");
        var (session, _, _) = Started(runtime, Config(script));
        runtime.Loop.Run(() => session.Send(Turn("hello")), Bound);
        runtime.Loop.Run(() => session.Rollback(0), Bound);
        runtime.OnMain(session.Stop);
        runtime.Loop.RunUntil(() => !session.IsRunning, Bound);
        Assert.DoesNotContain(script.Methods(), method => method.Contains("delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(CopilotSession).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static),
            method => method.Name.Contains("Delete", StringComparison.Ordinal));
    }
}
