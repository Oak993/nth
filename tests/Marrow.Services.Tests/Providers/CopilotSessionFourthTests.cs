using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using static Marrow.Services.Tests.Fixtures.CopilotSessionHarness;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CopilotSession.swift (B2 1fbf81e). Part 4: PR-PROVCLI-375
// through 393 (the tool rows for view, create, edit, str_replace_editor, apply_patch, grep, glob, web, task, agent
// management, skill, sql, pull requests and the default, and lineRange) and the Copilot parts of UI-PROVCLI-016,
// 018, 023, 027 and 032. Rows are built by the private toolCall(name:arguments:server:mcpTool:) on a session with
// no process; the catalog, quota and auth texts are proven against the offline stand-in tests/fixtures/copilot-cli.
public sealed class CopilotSessionFourthTests
{
    private const string Dot = " \U000000B7 "; // U+00B7 MIDDLE DOT
    private const string EnDash = "\U00002013"; // U+2013 EN DASH

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "copilot-wd");

    private static global::Marrow.Core.Models.ToolCall? Row(string name, string arguments) =>
        Row(name, Json(arguments));

    private static global::Marrow.Core.Models.ToolCall? Row(string name, JSONValue arguments)
    {
        var (session, _) = Offline(Config(workingDirectory: Root));
        return (global::Marrow.Core.Models.ToolCall?)Invoke(session, "ToolCall", name, arguments, null, null);
    }

    private static object Shape(global::Marrow.Core.Models.ToolCall? call) =>
        call is null ? "nil" : new { Kind = call.ToolCallKind, call.Title, call.Detail, Edits = call.Edits };

    private static object Expect(ToolCall.Kind kind, string title, string? detail = null, params FileEdit[] edits) =>
        new { Kind = kind, Title = title, Detail = detail, Edits = new ValueList<FileEdit>(edits) };

    private static string Rel(string path) => ToolTitles.RelativePath(path, Root);

    private static string? LineRange(string? json) => (string?)InvokeStatic("LineRange", json is null ? null : Json(json));

    // ---- PR-PROVCLI-375, 378 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-375")] // Services/Providers/CopilotSession.swift:960-961
    [Trait("Req", "PR-PROVCLI-378")] // Services/Providers/CopilotSession.swift:974-977
    public void View_is_a_read_row_falling_back_to_Read_file_and_str_replace_view_is_the_same()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Read, "Read file", "lines 3" + EnDash + "9"), Shape(Row("view", "{\"view_range\":[3,9]}")));
        Assert.Equal(Expect(ToolCall.Kind.Read, "Read file"), Shape(Row("view", "{\"path\":7,\"view_range\":[3]}")));
        Assert.Equal(Expect(ToolCall.Kind.Read, "Read file", "from line 5"), Shape(Row("str_replace_editor", "{\"command\":\"view\",\"view_range\":[5,-1]}")));
        Assert.Equal(Expect(ToolCall.Kind.Read, "Read file"), Shape(Row("str_replace_editor", "{\"command\":\"view\"}")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-375")] // Services/Providers/CopilotSession.swift:950-952,960-961
    [Trait("Req", "PR-PROVCLI-378")] // Services/Providers/CopilotSession.swift:976-977
    public void View_titles_the_row_with_the_path_relative_to_the_working_directory()
    {
        using var runtime = new TestRuntime();
        var file = Path.Combine(Root, "Sources", "App.swift");
        var arguments = JSONValue.FromDictionaryLiteral(("path", file), ("view_range", JSONValue.FromArrayLiteral(1, 20)));
        Assert.Equal(Expect(ToolCall.Kind.Read, Rel(file), "lines 1" + EnDash + "20"), Shape(Row("view", arguments)));
        var nested = JSONValue.FromDictionaryLiteral(("command", "view"), ("path", file), ("view_range", JSONValue.FromArrayLiteral(1, 20)));
        Assert.Equal(Shape(Row("view", arguments)), Shape(Row("str_replace_editor", nested)));
    }

    // ---- PR-PROVCLI-376, 377, 379, 380, 381 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-376")] // Services/Providers/CopilotSession.swift:962-967
    [Trait("Req", "PR-PROVCLI-377")] // Services/Providers/CopilotSession.swift:968-973
    [Trait("Req", "PR-PROVCLI-379")] // Services/Providers/CopilotSession.swift:978-983
    [Trait("Req", "PR-PROVCLI-380")] // Services/Providers/CopilotSession.swift:984-989
    [Trait("Req", "PR-PROVCLI-381")] // Services/Providers/CopilotSession.swift:990-995
    public void Without_a_path_the_edit_rows_keep_their_fallback_titles_and_carry_no_edits()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Create file"), Shape(Row("create", "{\"file_text\":\"x\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit file"), Shape(Row("edit", "{\"old_str\":\"a\",\"new_str\":\"b\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Create file"), Shape(Row("str_replace_editor", "{\"command\":\"create\",\"file_text\":\"x\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit file"), Shape(Row("str_replace_editor", "{\"command\":\"insert\",\"new_str\":\"x\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit file"), Shape(Row("str_replace_editor", "{\"command\":\"str_replace\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit file"), Shape(Row("str_replace_editor", "{}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit file"), Shape(Row("str_replace_editor", new JSONValue.Null())));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-376")] // Services/Providers/CopilotSession.swift:962-967
    [Trait("Req", "PR-PROVCLI-377")] // Services/Providers/CopilotSession.swift:968-973
    [Trait("Req", "PR-PROVCLI-379")] // Services/Providers/CopilotSession.swift:978-983
    [Trait("Req", "PR-PROVCLI-380")] // Services/Providers/CopilotSession.swift:984-989
    [Trait("Req", "PR-PROVCLI-381")] // Services/Providers/CopilotSession.swift:990-995
    public void With_a_path_each_edit_row_carries_one_edit_with_its_own_old_and_new_sides()
    {
        using var runtime = new TestRuntime();
        var file = Path.Combine(Root, "a.txt");
        var path = Rel(file);
        JSONValue Args(params (string, JSONValue)[] members) => JSONValue.FromDictionaryLiteral([("path", file), .. members]);

        Assert.Equal(Expect(ToolCall.Kind.Edit, path, null, new FileEdit(path, "", "hello\n")),
            Shape(Row("create", Args(("file_text", "hello\n"), ("old_str", "ignored")))));
        Assert.Equal(Expect(ToolCall.Kind.Edit, path, null, new FileEdit(path, "", "")), Shape(Row("create", Args())));
        Assert.Equal(Expect(ToolCall.Kind.Edit, path, null, new FileEdit(path, "a", "b")),
            Shape(Row("edit", Args(("old_str", "a"), ("new_str", "b"), ("old_string", "no"), ("new_string", "no")))));
        Assert.Equal(Expect(ToolCall.Kind.Edit, path, null, new FileEdit(path, "", "")), Shape(Row("edit", Args(("old_string", "no")))));
        Assert.Equal(Shape(Row("create", Args(("file_text", "x")))), Shape(Row("str_replace_editor", Args(("command", "create"), ("file_text", "x")))));
        // insert: an empty old side under the "Edit file" title family, even with an old_str present.
        Assert.Equal(Expect(ToolCall.Kind.Edit, path, null, new FileEdit(path, "", "added")),
            Shape(Row("str_replace_editor", Args(("command", "insert"), ("old_str", "kept?"), ("new_str", "added")))));
        Assert.Equal(Shape(Row("edit", Args(("old_str", "a"), ("new_str", "b")))),
            Shape(Row("str_replace_editor", Args(("command", "str_replace"), ("old_str", "a"), ("new_str", "b")))));
        Assert.Equal(Shape(Row("edit", Args(("old_str", "a"), ("new_str", "b")))),
            Shape(Row("str_replace_editor", Args(("command", 3), ("old_str", "a"), ("new_str", "b")))));
    }

    // ---- PR-PROVCLI-382 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-382")] // Services/Providers/CopilotSession.swift:997-1002
    public void Apply_patch_without_headers_is_titled_Edit_0_files()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit 0 files"), Shape(Row("apply_patch", "\"no headers\"")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit 0 files"), Shape(Row("apply_patch", "{}")));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit 0 files"), Shape(Row("apply_patch", new JSONValue.Null())));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-382")] // Services/Providers/CopilotSession.swift:997-1002
    public void Apply_patch_reads_the_bare_string_then_input_then_patch_and_lists_paths_without_content()
    {
        using var runtime = new TestRuntime();
        var a = Path.Combine(Root, "a.swift");
        var b = Path.Combine(Root, "b.swift");
        string Patch(params string[] files) => string.Join("\n", files.Select(f => "*** Update File: " + f)) + "\n";
        Assert.Equal(Expect(ToolCall.Kind.Edit, Rel(a), null, new FileEdit(Rel(a))), Shape(Row("apply_patch", (JSONValue)Patch(a))));
        Assert.Equal(Expect(ToolCall.Kind.Edit, "Edit 2 files", null, new FileEdit(Rel(a)), new FileEdit(Rel(b))),
            Shape(Row("apply_patch", JSONValue.FromDictionaryLiteral(("input", Patch(a, b)), ("patch", Patch(b))))));
        Assert.Equal(Expect(ToolCall.Kind.Edit, Rel(b), null, new FileEdit(Rel(b))),
            Shape(Row("apply_patch", JSONValue.FromDictionaryLiteral(("input", 5), ("patch", Patch(b))))));
        Assert.All(Row("apply_patch", (JSONValue)Patch(a, b))!.Edits, edit => Assert.Equal((null, 0L, 0L), (edit.Diff, edit.Additions, edit.Deletions)));
    }

    // ---- PR-PROVCLI-383, 384 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-383")] // Services/Providers/CopilotSession.swift:1003-1005
    [Trait("Req", "PR-PROVCLI-384")] // Services/Providers/CopilotSession.swift:1006-1008
    public void Grep_and_glob_are_search_rows_with_their_own_title_fallbacks()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Search, "TODO"), Shape(Row("grep", "{\"pattern\":\"TODO\",\"paths\":[]}")));
        Assert.Equal(Expect(ToolCall.Kind.Search, "Search"), Shape(Row("grep", "{\"pattern\":3,\"paths\":[1,false]}")));
        Assert.Equal(Expect(ToolCall.Kind.Search, "**/*.cs"), Shape(Row("glob", "{\"pattern\":\"**/*.cs\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Search, "Find files"), Shape(Row("glob", "{\"paths\":\"not an array\"}")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-383")] // Services/Providers/CopilotSession.swift:1004-1005
    [Trait("Req", "PR-PROVCLI-384")] // Services/Providers/CopilotSession.swift:1007-1008
    public void Grep_and_glob_join_their_relative_string_paths_with_a_comma()
    {
        using var runtime = new TestRuntime();
        var src = Path.Combine(Root, "src");
        var tests = Path.Combine(Root, "tests");
        var paths = JSONValue.FromArrayLiteral(src, 4, tests);
        Assert.Equal(Expect(ToolCall.Kind.Search, "x", Rel(src) + ", " + Rel(tests)),
            Shape(Row("grep", JSONValue.FromDictionaryLiteral(("pattern", "x"), ("paths", paths)))));
        Assert.Equal(Expect(ToolCall.Kind.Search, "Find files", Rel(src) + ", " + Rel(tests)),
            Shape(Row("glob", JSONValue.FromDictionaryLiteral(("paths", paths)))));
    }

    // ---- PR-PROVCLI-385, 386 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-385")] // Services/Providers/CopilotSession.swift:1009-1010
    [Trait("Req", "PR-PROVCLI-386")] // Services/Providers/CopilotSession.swift:1011-1012
    public void Web_fetch_and_web_search_are_web_rows_without_detail()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Web, "https://docs.github.com"), Shape(Row("web_fetch", "{\"url\":\"https://docs.github.com\",\"query\":\"q\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Web, "Fetch page"), Shape(Row("web_fetch", "{}")));
        Assert.Equal(Expect(ToolCall.Kind.Web, "swift regex"), Shape(Row("web_search", "{\"query\":\"swift regex\",\"url\":\"u\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Web, "Search the web"), Shape(Row("web_search", "{\"query\":null}")));
    }

    // ---- PR-PROVCLI-387, 388 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-387")] // Services/Providers/CopilotSession.swift:1013-1015
    public void Task_is_an_agent_row_whose_detail_joins_name_and_agent_type()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Audit the API", "auditor" + Dot + "explore"),
            Shape(Row("task", "{\"description\":\"Audit the API\",\"name\":\"auditor\",\"agent_type\":\"explore\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Subagent", "explore"), Shape(Row("task", "{\"agent_type\":\"explore\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Subagent", "auditor"), Shape(Row("task", "{\"name\":\"auditor\",\"agent_type\":1}")));
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Subagent"), Shape(Row("task", "{}")));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-388")] // Services/Providers/CopilotSession.swift:1016-1017,1068-1071
    public void The_agent_management_tools_are_humanized_agent_rows_subtitled_with_an_id_or_a_message()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Read agent", "agent-1"), Shape(Row("read_agent", "{\"agent_id\":\"agent-1\",\"message\":\"m\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Agent, "Write agent", "Keep going"), Shape(Row("write_agent", "{\"agent_id\":2,\"message\":\"Keep going\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Agent, "List agents"), Shape(Row("list_agents", "{}")));
    }

    // ---- PR-PROVCLI-389, 390, 391 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-389")] // Services/Providers/CopilotSession.swift:1018-1019
    [Trait("Req", "PR-PROVCLI-390")] // Services/Providers/CopilotSession.swift:1020-1021
    [Trait("Req", "PR-PROVCLI-391")] // Services/Providers/CopilotSession.swift:1022-1023
    public void Skill_sql_and_create_pull_request_are_other_rows_with_their_exact_titles()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Other, "Skill" + Dot + "pdf"), Shape(Row("skill", "{\"skill\":\"pdf\",\"command\":\"no\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Other, "Skill" + Dot), Shape(Row("skill", "{\"command\":\"no\"}")));
        Assert.Equal("Skill \U000000B7 ", Row("skill", "{}")!.Title); // U+00B7 MIDDLE DOT, trailing space kept
        var query = "SELECT *\nFROM todos\nWHERE status = 'pending' " + new string('x', 300);
        Assert.Equal(Expect(ToolCall.Kind.Other, "Open to-dos", query),
            Shape(Row("sql", JSONValue.FromDictionaryLiteral(("description", "Open to-dos"), ("query", query)))));
        Assert.Equal(Expect(ToolCall.Kind.Other, "SQL query"), Shape(Row("sql", "{}")));
        Assert.Equal(Expect(ToolCall.Kind.Other, "Fix the build"), Shape(Row("create_pull_request", "{\"title\":\"Fix the build\",\"body\":\"b\"}")));
        Assert.Equal(Expect(ToolCall.Kind.Other, "Create pull request"), Shape(Row("create_pull_request", "{\"body\":\"b\"}")));
    }

    // ---- PR-PROVCLI-392 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-392")] // Services/Providers/CopilotSession.swift:1024-1025,1068-1071; Core/Support/Text.swift:26-31
    public void Any_other_tool_is_a_humanized_other_row_subtitled_with_its_arguments_on_one_line()
    {
        using var runtime = new TestRuntime();
        Assert.Equal(Expect(ToolCall.Kind.Other, "Fetch issue details", null), Shape(Row("fetch-issue_details", new JSONValue.Null())));
        Assert.Equal(Expect(ToolCall.Kind.Other, "Report progress", "{\"done\":2}"), Shape(Row("report_progress", "{\"done\":2}")));
        Assert.Equal(Expect(ToolCall.Kind.Other, "", "[]"), Shape(Row("", "[]")));
        Assert.Equal(Expect(ToolCall.Kind.Other, "  x", "\"a\""), Shape(Row("_-x", "\"a\"")));
        // Longer than 120 Characters: the first 119 and U+2026 HORIZONTAL ELLIPSIS.
        var long_ = JSONValue.FromDictionaryLiteral(("text", new string('y', 200)));
        Assert.Equal(Expect(ToolCall.Kind.Other, "Long", SwiftString.Prefix(long_.CompactString, 119) + "\U00002026"), Shape(Row("long", long_))); // U+2026
    }

    // ---- PR-PROVCLI-393, UI-PROVCLI-032 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-393")] // Services/Providers/CopilotSession.swift:1029-1032; Core/Support/JSONValue.swift:83-90
    [Trait("Req", "UI-PROVCLI-032")] // Services/Providers/CopilotSession.swift:1029-1032
    public void LineRange_renders_two_integers_with_an_en_dash_or_an_open_range()
    {
        Assert.Equal("lines 10" + EnDash + "20", LineRange("[10,20]"));
        Assert.Equal("lines 10\U00002013" + "20", LineRange("[10,20]")); // U+2013 EN DASH, no spaces
        Assert.Equal("from line 10", LineRange("[10,-1]"));
        Assert.Equal("from line 10", LineRange("[10,-7]"));
        Assert.Equal("lines -3" + EnDash + "0", LineRange("[-3,0]"));
        // JSONValue.int keeps a numeric string and truncates a finite double; other entries are dropped first.
        Assert.Equal("lines 4" + EnDash + "8", LineRange("[\"4\",8.9]"));
        Assert.Equal("lines 1" + EnDash + "2", LineRange("[1,null,\"x\",2]"));
        Assert.Null(LineRange("[1]"));
        Assert.Null(LineRange("[1,2,3]"));
        Assert.Null(LineRange("[]"));
        Assert.Null(LineRange("\"1-2\""));
        Assert.Null(LineRange("{\"start\":1,\"end\":2}"));
        Assert.Null(LineRange(null));
    }

    // ---- UI-PROVCLI-016 ----

    [Fact]
    [Trait("Req", "UI-PROVCLI-016")] // Services/Providers/CopilotSession.swift:826-871
    public void The_approval_card_titles_fall_back_to_their_literals()
    {
        using var runtime = new TestRuntime();
        string Title(string request)
        {
            var (session, events) = Offline();
            Notify(runtime, session, "permission.requested", Json("{\"requestId\":\"r\",\"permissionRequest\":" + request + "}"));
            return Assert.Single(events.OfType<ProviderEvent.Approval>()).Value.Title;
        }
        Assert.Equal("Command", Title("{\"kind\":\"shell\"}"));
        Assert.Equal("Fetch a URL", Title("{\"kind\":\"url\"}"));
        Assert.Equal("MCP" + Dot + "tool", Title("{\"kind\":\"mcp\"}"));
        Assert.Equal("Remember: something about this project", Title("{\"kind\":\"memory\"}"));
        Assert.Equal("Tool", Title("{\"kind\":\"custom-tool\"}"));
        Assert.Equal("Tool", Title("{\"kind\":\"hook\"}"));
        Assert.Equal("Manage an extension", Title("{\"kind\":\"extension-management\"}"));
        Assert.Equal("Some new kind", Title("{\"kind\":\"some_new-kind\"}"));
        Assert.Equal("Action", Title("{}"));
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-016")] // Services/Providers/CopilotSession.swift:839-844
    public void The_approval_card_titles_for_file_kinds_fall_back_to_file()
    {
        using var runtime = new TestRuntime();
        string Title(string request)
        {
            var (session, events) = Offline(Config(workingDirectory: Root));
            Notify(runtime, session, "permission.requested", Json("{\"requestId\":\"r\",\"permissionRequest\":" + request + "}"));
            return Assert.Single(events.OfType<ProviderEvent.Approval>()).Value.Title;
        }
        Assert.Equal(Rel("file"), Title("{\"kind\":\"write\"}"));
        Assert.Equal("Read " + Rel("file"), Title("{\"kind\":\"read\"}"));
    }

    // ---- UI-PROVCLI-018, 023, 027 ----

    [Fact]
    [Trait("Req", "UI-PROVCLI-018")] // Services/Providers/CopilotSession.swift:409-419
    public void A_model_row_detail_shows_the_multiplier_and_the_unconfigured_hint_joined_by_a_middle_dot()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("model-details");
        script.Reply("models.list", "{\"models\":[" +
            "{\"id\":\"a\",\"billing\":{\"multiplier\":2},\"policy\":{\"state\":\"unconfigured\"}}," +
            "{\"id\":\"b\",\"billing\":{\"multiplier\":1.5}}," +
            "{\"id\":\"c\",\"billing\":{\"multiplier\":0}}," +
            "{\"id\":\"d\",\"policy\":{\"state\":\"Disabled\"}}," +
            "{\"id\":\"e\"}]}");
        var models = runtime.Loop.Run(() => CopilotSession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        Assert.Equal(
            [
                "2\U000000D7 premium requests" + Dot + "Enable it in your GitHub Copilot settings first", // U+00D7 MULTIPLICATION SIGN
                "1.5\U000000D7 premium requests", // U+00D7 MULTIPLICATION SIGN
                "0\U000000D7 premium requests", // U+00D7 MULTIPLICATION SIGN
                null,
                null,
            ],
            models.Skip(1).Select(m => m.Detail).ToList());
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-023")] // Services/Providers/CopilotSession.swift:448,451-464
    public void The_usage_rows_keep_their_fixed_order_and_titles_and_can_read_negative()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        var script = new CopilotScript("usage-rows");
        script.Reply("account.getQuota", "{\"quotaSnapshots\":{" +
            "\"completions\":{\"entitlementRequests\":100,\"usedRequests\":-20}," +
            "\"premium_interactions\":{\"entitlementRequests\":50,\"usedRequests\":500}," +
            "\"extra\":{\"entitlementRequests\":10,\"usedRequests\":1}}}");
        var limits = runtime.Loop.Run(() => CopilotSession.ReadPlanLimits(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound)!;
        Assert.Equal(
            [("premium_interactions", "Premium requests", 100.0), ("completions", "Code completions", -20.0)],
            limits.Windows.Select(w => (w.Id, w.Title, w.Percent)).ToList());
        // UPSTREAM DEFECT 3, reproduced: the used branch has no lower bound.
        Assert.Null(limits.PlanName);
    }

    [Fact]
    [Trait("Req", "UI-PROVCLI-027")] // Services/Providers/CopilotSession.swift:428-441
    public void The_sign_in_text_is_the_login_or_GitHub_with_the_no_access_suffix()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // Connect reads the MCP overrides (CopilotSession.swift:481-483)
        ProviderStatus.Auth Status(string reply)
        {
            var script = new CopilotScript("sign-in-text");
            script.Reply("account.getCurrentAuth", reply);
            return runtime.Loop.Run(() => CopilotSession.AuthStatus(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);
        }
        Assert.Equal(new ProviderStatus.Auth.SignedIn("octocat"), Status("{\"authInfo\":{\"login\":\"octocat\",\"copilotUser\":{\"access_type_sku\":\"free\"}}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedIn("octocat \U000000B7 no Copilot access"), // U+00B7 MIDDLE DOT
            Status("{\"authInfo\":{\"login\":\"octocat\",\"copilotUser\":{\"access_type_sku\":\"no_access\"}}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedIn("GitHub \U000000B7 no Copilot access"), // U+00B7 MIDDLE DOT
            Status("{\"authInfo\":{\"copilotUser\":{\"access_type_sku\":\"no_access\"}}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), Status("{\"authInfo\":{}}"));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status("{\"authInfo\":null}"));
        Assert.Equal(new ProviderStatus.Auth.SignedOut(), Status("{}"));
    }
}
