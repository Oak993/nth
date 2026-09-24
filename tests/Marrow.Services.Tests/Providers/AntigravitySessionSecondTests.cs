using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/AntigravitySession.swift (B2 1fbf81e). Part 2: PR-PROVCLI-343,
// 344 and 413 to 434 (the tool-name table, the row built for each kind, the parameter-key helpers, and the catalog
// steps of listModels). The private tool mapping is exercised directly by reflection; the catalog runs against the
// compiled offline stand-in tests/fixtures/antigravity-cli (R-TEST-8, R-TEST-10).
public sealed class AntigravitySessionSecondTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private const string MiddleDot = "\U000000B7"; // U+00B7 MIDDLE DOT
    private const string EnDash = "\U00002013"; // U+2013 EN DASH
    private const string Ellipsis = "\U00002026"; // U+2026 HORIZONTAL ELLIPSIS

    // A non-"file" scheme keeps the Windows path unchanged through SwiftURL.Path (the ClaudeSessionTests note).
    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;

    private static string Scratch()
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "AntigravitySessionSecondTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static AntigravitySession Session(string workingDirectory) => new(new SessionConfiguration
    {
        Provider = ProviderKind.Antigravity,
        WorkingDirectory = FileUrl(workingDirectory),
        Environment = new ValueMap<string, string>([]),
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    });

    private static object? Call(object? target, string method, params object?[] args)
    {
        try
        {
            return typeof(AntigravitySession).GetMethod(method, target is null ? Static : Instance)!.Invoke(target, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private static JSONValue Json(string text) => JSONValue.Parse(text) ?? throw new ArgumentException("Not JSON: " + text);

    /// <summary>makeToolCall(name:parameters:) (AntigravitySession.swift:636-676) on a session rooted at <paramref name="root"/>.</summary>
    private static ToolCall Row(string name, string parameters, string? root = null) =>
        (ToolCall)Call(Session(root ?? Scratch()), "MakeToolCall", name, Json(parameters))!;

    private static ToolCall.Kind Kind(string name) => (ToolCall.Kind)Call(null, "Kind", name)!;

    private static string? PathIn(string parameters) => (string?)Call(null, "Path", Json(parameters));

    private static string? StringIn(string parameters, params string[] keys) => (string?)Call(null, "String", Json(parameters), (IReadOnlyList<string>)keys);

    private static string? LineRange(string parameters) => (string?)Call(null, "LineRange", Json(parameters));

    private static string Humanize(string name) => (string)Call(null, "Humanize", name)!;

    private static string Quote(string text) => new JSONValue.String(text).CompactString;

    private static ValueList<ModelOption> ListModels(TestRuntime runtime, AntigravityScript script) =>
        runtime.Loop.Run(() => AntigravitySession.ListModels(FileUrl(script.ExecutablePath), new ValueMap<string, string>([script.EnvironmentEntry])), Bound);

    private static AntigravityScript Catalog(string label, string models, string? current = null)
    {
        var script = new AntigravityScript(label);
        script.Set("models.stdout", models);
        if (current is not null) script.Set("model.stdout", "{\"command\":{\"data\":{\"id\":\"" + current + "\"}}}");
        return script;
    }

    // ---- PR-PROVCLI-343 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-343")] // Services/Providers/AntigravitySession.swift:678-702
    public void Fifty_tool_names_map_to_seven_kinds_and_everything_else_is_other()
    {
        var table = new Dictionary<ToolCall.Kind, string[]>
        {
            [ToolCall.Kind.Command] = ["run_command", "send_command_input", "command_status", "wait", "wait_5_seconds", "schedule"],
            [ToolCall.Kind.Read] = ["view_file", "view_file_outline", "view_code_item", "view_content_chunk", "read_resource", "read_browser_page",
                "capture_browser_screenshot", "capture_browser_console_logs"],
            [ToolCall.Kind.Edit] = ["write_to_file", "replace_file_content", "multi_replace_file_content", "sed_file", "notebook_edit", "notebook_execution"],
            [ToolCall.Kind.Search] = ["list_dir", "grep_search", "find_by_name", "list_resources", "list_browser_pages"],
            [ToolCall.Kind.Web] = ["search_web", "read_url_content", "open_browser_url", "browser_click_element", "browser_drag_pixel_to_pixel",
                "browser_get_dom", "browser_get_network_request", "browser_input", "browser_list_network_requests", "browser_mouse_down",
                "browser_mouse_up", "browser_move_mouse", "browser_press_key", "browser_refresh_page", "browser_resize_window", "browser_scroll",
                "browser_scroll_dom", "browser_select_option", "click_browser_pixel", "execute_browser_javascript"],
            [ToolCall.Kind.Agent] = ["invoke_subagent", "browser_subagent", "define_subagent", "manage_subagents"],
            [ToolCall.Kind.Mcp] = ["call_mcp_tool"],
        };
        Assert.Equal(50, table.Values.Sum(names => names.Length));
        foreach (var (kind, names) in table)
            foreach (var name in names) Assert.Equal(kind, Kind(name));
        foreach (var name in new[] { "", "Run_command", "run_command ", "view_files", "browser_close", "mcp__server__tool", "unknown_tool", "wait_10_seconds" })
            Assert.Equal(ToolCall.Kind.Other, Kind(name));
    }

    // ---- PR-PROVCLI-344 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-344")] // Services/Providers/AntigravitySession.swift:636-639
    public void Every_tool_gets_a_row_built_by_its_kind_not_its_name()
    {
        // A command-kind name ignores a path parameter; an edit-kind name uses it; the name picks the branch.
        var root = Scratch();
        var file = Path.Combine(root, "x.txt");
        var command = Row("run_command", "{\"AbsolutePath\":" + Quote(file) + ",\"CommandLine\":\"make\"}", root);
        Assert.Equal(ToolCall.Kind.Command, command.ToolCallKind);
        Assert.Equal("make", command.Title);
        Assert.Empty(command.Edits);
        var edit = Row("sed_file", "{\"AbsolutePath\":" + Quote(file) + ",\"CommandLine\":\"make\"}", root);
        Assert.Equal(ToolCall.Kind.Edit, edit.ToolCallKind);
        Assert.Equal("x.txt", edit.Title);
        Assert.Equal(ToolCall.Status.Running, edit.ToolCallStatus);
        Assert.Equal("", edit.Output);
    }

    // ---- PR-PROVCLI-413 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-413")] // Services/Providers/AntigravitySession.swift:640-642
    public void A_command_row_is_titled_with_the_first_command_key_unwrapped_and_never_has_a_detail()
    {
        Assert.Equal("ls -la", Row("run_command", "{\"command\":\"no\",\"CommandLine\":\"bash -lc 'ls -la'\",\"Description\":\"List\"}").Title);
        Assert.Equal("pwd", Row("send_command_input", "{\"CommandLine\":\"\",\"command\":\"pwd\",\"Command\":\"no\"}").Title);
        Assert.Equal("echo hi", Row("command_status", "{\"Command\":\"  /bin/zsh -lc \\\"echo hi\\\"  \"}").Title);
        var row = Row("wait_5_seconds", "{\"CommandLine\":\"sleep 5\",\"description\":\"wait\"}");
        Assert.Equal(ToolCall.Kind.Command, row.ToolCallKind);
        Assert.Null(row.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-413")] // Services/Providers/AntigravitySession.swift:642,725-729
    public void A_command_row_without_a_command_is_titled_with_the_humanized_name()
    {
        Assert.Equal("Wait 5 seconds", Row("wait_5_seconds", "{}").Title);
        Assert.Equal("Schedule", Row("schedule", "null").Title);
    }

    // ---- PR-PROVCLI-414 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-414")] // Services/Providers/AntigravitySession.swift:643-646
    public void A_read_row_is_titled_by_path_then_url_then_name_and_shows_a_line_range_only_for_a_path()
    {
        var root = Scratch();
        var file = Path.Combine(root, "src.cs");
        var byPath = Row("view_file", "{\"AbsolutePath\":" + Quote(file) + ",\"Url\":\"https://a\",\"StartLine\":12,\"EndLine\":40}", root);
        Assert.Equal(ToolCall.Kind.Read, byPath.ToolCallKind);
        Assert.Equal("src.cs", byPath.Title);
        Assert.Equal("L12" + EnDash + "40", byPath.Detail);
        var byUrl = Row("read_browser_page", "{\"URL\":\"https://example.test/p\",\"StartLine\":12,\"EndLine\":40}", root);
        Assert.Equal("https://example.test/p", byUrl.Title);
        Assert.Null(byUrl.Detail);
        Assert.Equal("https://u", Row("read_resource", "{\"Url\":\"\",\"url\":\"https://u\"}", root).Title);
        Assert.Null(Row("view_file", "{\"AbsolutePath\":" + Quote(file) + "}", root).Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-414")] // Services/Providers/AntigravitySession.swift:644,725-729
    public void A_read_row_with_neither_path_nor_url_is_titled_with_the_humanized_name()
    {
        Assert.Equal("Capture browser screenshot", Row("capture_browser_screenshot", "{\"StartLine\":3}").Title);
        Assert.Null(Row("capture_browser_screenshot", "{\"StartLine\":3}").Detail);
    }

    // ---- PR-PROVCLI-415 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-415")] // Services/Providers/AntigravitySession.swift:647-650
    public void An_edit_row_lists_its_file_as_one_path_only_edit_and_carries_no_content()
    {
        var root = Scratch();
        var file = Path.Combine(root, "notes.md");
        var row = Row("replace_file_content", "{\"TargetFile\":" + Quote(file) + ",\"ReplacementContent\":\"new\",\"TargetContent\":\"old\"}", root);
        Assert.Equal(ToolCall.Kind.Edit, row.ToolCallKind);
        Assert.Equal("notes.md", row.Title);
        Assert.Equal([new FileEdit("notes.md")], row.Edits);
        Assert.Null(row.Edits[0].Diff);
        Assert.Equal(0, row.Edits[0].Additions);
        Assert.Equal(0, row.Edits[0].Deletions);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-415")] // Services/Providers/AntigravitySession.swift:648-649
    public void An_edit_row_without_a_path_has_no_edits_and_the_humanized_name()
    {
        var row = Row("multi_replace_file_content", "{\"ReplacementChunks\":[]}");
        Assert.Equal("Multi replace file content", row.Title);
        Assert.Empty(row.Edits);
    }

    // ---- PR-PROVCLI-416 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-416")] // Services/Providers/AntigravitySession.swift:651-658
    public void A_search_row_is_titled_with_the_pattern_and_subtitled_with_the_folder_or_titled_with_the_folder_alone()
    {
        var root = Scratch();
        var folder = Path.Combine(root, "src");
        var withPattern = Row("grep_search", "{\"Query\":\"TODO\",\"query\":\"no\",\"SearchPath\":" + Quote(folder) + ",\"AbsolutePath\":\"ignored\"}", root);
        Assert.Equal(ToolCall.Kind.Search, withPattern.ToolCallKind);
        Assert.Equal("TODO", withPattern.Title);
        Assert.Equal("src", withPattern.Detail);
        var folderOnly = Row("list_dir", "{\"DirectoryPath\":" + Quote(folder) + "}", root);
        Assert.Equal("src", folderOnly.Title);
        Assert.Null(folderOnly.Detail);
        var patternOnly = Row("find_by_name", "{\"pattern\":\"*.cs\",\"dir\":" + Quote(folder) + "}", root);
        Assert.Equal("*.cs", patternOnly.Title);
        Assert.Null(patternOnly.Detail); // "dir" is a path(in:) key, not a folder key
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-416")] // Services/Providers/AntigravitySession.swift:652-653,658
    public void The_search_pattern_and_folder_keys_are_read_in_their_own_orders()
    {
        Assert.Equal("q2", Row("grep_search", "{\"Query\":\"\",\"query\":\"q2\",\"Pattern\":\"p\"}").Title);
        Assert.Equal("p3", Row("grep_search", "{\"Pattern\":\"p3\",\"pattern\":\"p4\"}").Title);
        Assert.Equal("f2", Row("grep_search", "{\"pattern\":\"x\",\"SearchDirectory\":\"f2\",\"DirectoryPath\":\"f3\",\"Path\":\"f4\",\"path\":\"f5\"}").Detail);
        Assert.Equal("f5", Row("grep_search", "{\"pattern\":\"x\",\"path\":\"f5\"}").Detail);
        Assert.Equal("List resources", Row("list_resources", "{}").Title);
    }

    // ---- PR-PROVCLI-417 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-417")] // Services/Providers/AntigravitySession.swift:659-661
    public void A_web_row_is_titled_with_the_first_of_Url_URL_url_query_Query_and_has_no_detail()
    {
        Assert.Equal("https://a", Row("search_web", "{\"query\":\"q\",\"Url\":\"https://a\"}").Title);
        Assert.Equal("https://b", Row("open_browser_url", "{\"URL\":\"https://b\",\"url\":\"https://c\"}").Title);
        Assert.Equal("lower", Row("search_web", "{\"Query\":\"upper\",\"query\":\"lower\"}").Title);
        Assert.Equal("upper", Row("search_web", "{\"Query\":\"upper\",\"query\":\"\"}").Title);
        var row = Row("browser_click_element", "{\"url\":\"https://d\",\"Description\":\"Click\"}");
        Assert.Equal(ToolCall.Kind.Web, row.ToolCallKind);
        Assert.Null(row.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-417")] // Services/Providers/AntigravitySession.swift:661,725-729
    public void A_web_row_without_a_subject_is_titled_with_the_humanized_name()
    {
        Assert.Equal("Execute browser javascript", Row("execute_browser_javascript", "{\"Script\":\"1\"}").Title);
    }

    // ---- PR-PROVCLI-418 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-418")] // Services/Providers/AntigravitySession.swift:662-664
    public void An_agent_row_is_titled_with_the_first_task_key_as_a_single_line_of_at_most_96_characters()
    {
        Assert.Equal("Fix the build", Row("invoke_subagent", "{\"Prompt\":\"no\",\"Task\":\"\\n  Fix the build  \\nthen test\"}").Title);
        Assert.Equal("d", Row("browser_subagent", "{\"Task\":\"\",\"task\":\"\",\"Prompt\":\"\",\"prompt\":\"\",\"Description\":\"\",\"description\":\"d\"}").Title);
        var longTask = new string('a', 97);
        Assert.Equal(new string('a', 95) + Ellipsis, Row("define_subagent", "{\"task\":\"" + longTask + "\"}").Title);
        Assert.Equal(new string('b', 96), Row("define_subagent", "{\"task\":\"" + new string('b', 96) + "\"}").Title);
        var row = Row("manage_subagents", "{\"prompt\":\"p\",\"SubagentType\":\"worker\"}");
        Assert.Equal(ToolCall.Kind.Agent, row.ToolCallKind);
        Assert.Null(row.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-418")] // Services/Providers/AntigravitySession.swift:664,725-729
    public void An_agent_row_without_a_task_is_titled_with_the_humanized_name()
    {
        Assert.Equal("Manage subagents", Row("manage_subagents", "{}").Title);
    }

    // ---- PR-PROVCLI-419 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-419")] // Services/Providers/AntigravitySession.swift:665-669
    public void An_mcp_row_is_titled_server_middle_dot_tool_with_the_arguments_as_its_detail()
    {
        var row = Row("call_mcp_tool", "{\"ServerName\":\"github\",\"server\":\"no\",\"tool_name\":\"search\",\"Arguments\":{\"q\":\"x\"},\"arguments\":{\"no\":1}}");
        Assert.Equal(ToolCall.Kind.Mcp, row.ToolCallKind);
        Assert.Equal("github " + MiddleDot + " search", row.Title);
        Assert.Equal("{\"q\":\"x\"}", row.Detail);
        Assert.Equal("{\"k\":[1,2]}", Row("call_mcp_tool", "{\"server_name\":\"s\",\"Tool\":\"t\",\"arguments\":{\"k\":[1,2]}}").Detail);
        Assert.Equal("\"raw\"", Row("call_mcp_tool", "{\"Input\":\"raw\"}").Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-419")] // Services/Providers/AntigravitySession.swift:666-669
    public void An_mcp_row_falls_back_to_MCP_and_lower_case_tool_and_reads_no_lower_case_input()
    {
        var row = Row("call_mcp_tool", "{\"input\":{\"a\":1},\"Server\":\"\"}");
        Assert.Equal("MCP " + MiddleDot + " tool", row.Title);
        Assert.Null(row.Detail);
        Assert.Null(Row("call_mcp_tool", "{\"Arguments\":null,\"arguments\":{\"b\":2}}").Detail);
        Assert.Equal("srv " + MiddleDot + " t4", Row("call_mcp_tool", "{\"server\":\"srv\",\"tool\":\"t4\"}").Title);
    }

    // ---- PR-PROVCLI-420 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-420")] // Services/Providers/AntigravitySession.swift:670-674
    public void An_other_row_is_titled_with_the_humanized_name_and_keeps_a_short_non_empty_parameter_detail()
    {
        var row = Row("todo_write", "{\"items\":[1]}");
        Assert.Equal(ToolCall.Kind.Other, row.ToolCallKind);
        Assert.Equal("Todo write", row.Title);
        Assert.Equal("{\"items\":[1]}", row.Detail);
        Assert.Null(Row("todo_write", "null").Detail);
        Assert.Null(Row("todo_write", "{}").Detail);
        Assert.Equal("\"\"", Row("todo_write", "\"\"").Detail);
        // 300 characters stay; 301 are dropped, not truncated.
        var exact = "\"" + new string('x', 298) + "\"";
        Assert.Equal(exact, Row("todo_write", exact).Detail);
        Assert.Null(Row("todo_write", "\"" + new string('x', 299) + "\"").Detail);
    }

    // ---- PR-PROVCLI-421 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-421")] // Services/Providers/AntigravitySession.swift:704-709
    public void The_path_is_the_first_non_empty_of_eleven_keys_in_order()
    {
        string[] keys = ["AbsolutePath", "TargetFile", "NotebookPath", "FilePath", "File", "file", "path", "Path", "DirectoryPath", "directory", "dir"];
        for (var index = 0; index < keys.Length; index++)
        {
            var members = keys.Skip(index).Select(key => Quote(key) + ":" + Quote("v-" + key));
            Assert.Equal("v-" + keys[index], PathIn("{" + string.Join(",", members) + "}"));
        }
        Assert.Null(PathIn("{\"SearchPath\":\"s\",\"SearchDirectory\":\"d\",\"Url\":\"u\"}"));
        Assert.Null(PathIn("null"));
    }

    // ---- PR-PROVCLI-422 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-422")] // Services/Providers/AntigravitySession.swift:711-716
    public void Key_lookup_skips_missing_keys_empty_strings_and_non_strings()
    {
        Assert.Equal("c", StringIn("{\"a\":\"\",\"b\":7,\"c\":\"c\",\"d\":\"d\"}", "a", "b", "c", "d"));
        Assert.Null(StringIn("{\"a\":\"\",\"b\":{\"x\":1},\"c\":null,\"d\":[\"d\"]}", "a", "b", "c", "d"));
        Assert.Null(StringIn("[\"a\"]", "a"));
        Assert.Equal(" ", StringIn("{\"a\":\" \"}", "a"));
    }

    // ---- PR-PROVCLI-423 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-423")] // Services/Providers/AntigravitySession.swift:718-723
    public void A_line_range_is_L_start_en_dash_end_only_when_the_end_is_greater()
    {
        Assert.Equal("L12" + EnDash + "40", LineRange("{\"StartLine\":12,\"EndLine\":40}"));
        Assert.Equal("L12", LineRange("{\"StartLine\":12,\"EndLine\":12}"));
        Assert.Equal("L12", LineRange("{\"StartLine\":12,\"EndLine\":3}"));
        Assert.Equal("L12", LineRange("{\"StartLine\":12}"));
        Assert.Equal("L12", LineRange("{\"StartLine\":12,\"EndLine\":\"x\"}"));
        Assert.Null(LineRange("{\"EndLine\":40}"));
        Assert.Null(LineRange("{\"StartLine\":null}"));
        // JSONValue.int reads a numeric string and truncates a finite double (JSONValue.swift:83-90).
        Assert.Equal("L7" + EnDash + "9", LineRange("{\"StartLine\":\"7\",\"EndLine\":9.8}"));
    }

    // ---- PR-PROVCLI-424 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-424")] // Services/Providers/AntigravitySession.swift:725-729
    public void Humanize_capitalizes_only_the_first_underscore_separated_word_lowercasing_its_rest()
    {
        Assert.Equal("Run command", Humanize("run_command"));
        Assert.Equal("Browser get dom", Humanize("browser_get_dom"));
        Assert.Equal("Xmlread file", Humanize("XMLRead_file"));
        Assert.Equal("Wait 5 seconds", Humanize("wait_5_seconds"));
        Assert.Equal("Keep CASE here", Humanize("keep_CASE_here"));
        // split(separator:) drops empty pieces.
        Assert.Equal("Double gap", Humanize("__double__gap_"));
        Assert.Equal("", Humanize(""));
        Assert.Equal("___", Humanize("___"));
    }

    // ---- PR-PROVCLI-425 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-425")] // Services/Providers/AntigravitySession.swift:453-456
    public void The_catalog_runs_agy_models_and_the_default_model_probe_concurrently()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("concurrent", "gemini-3-flash\tGemini 3 Flash\n", "gemini-3-flash");
        script.Set("rendezvous", "models\nmodel\n");
        Assert.Single(ListModels(runtime, script));
        var calls = script.Calls();
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, call => call.SequenceEqual(["models"]));
        Assert.Contains(calls, call => call.SequenceEqual(["-p", "/model", "--output-format", "json"]));
        Assert.Equal("ok", script.Read("rendezvous-models.txt"));
        Assert.Equal("ok", script.Read("rendezvous-model.txt"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-425")] // Services/Providers/AntigravitySession.swift:456
    public void A_failed_default_model_probe_never_fails_the_catalog()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("probe-fails", "gemini-3-flash\tGemini 3 Flash\n");
        script.Set("model.stdout", "Error: not json");
        script.Set("model.exit", "2");
        var models = ListModels(runtime, script);
        Assert.Equal("gemini-3-flash", Assert.Single(models).Id);
    }

    // ---- PR-PROVCLI-426 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-426")] // Services/Providers/AntigravitySession.swift:457-460,510-515
    public void The_current_model_id_is_read_only_from_command_data_id()
    {
        using var runtime = new TestRuntime();
        const string rows = "aaa\tA\nbbb\tB\n";
        var exact = Catalog("current", rows, "bbb");
        Assert.Equal([false, true], ListModels(runtime, exact).Select(option => option.IsDefault));

        // Any other shape leaves the id nil, so the first row becomes the default.
        foreach (var shape in new[] { "{\"data\":{\"id\":\"bbb\"}}", "{\"command\":{\"id\":\"bbb\"}}", "{\"command\":{\"data\":{\"id\":5}}}", "bbb" })
        {
            var script = Catalog("current-shape", rows);
            script.Set("model.stdout", shape);
            Assert.Equal([true, false], ListModels(runtime, script).Select(option => option.IsDefault));
        }
    }

    // ---- PR-PROVCLI-427 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-427")] // Services/Providers/AntigravitySession.swift:461-463
    public void A_models_call_that_cannot_run_or_prints_invalid_UTF8_throws_that_agy_models_produced_no_output()
    {
        using var runtime = new TestRuntime();
        var missing = FileUrl(Path.Combine(Scratch(), "no-such-agy.exe"));
        var error = Assert.Throws<ShellError>(() => runtime.Loop.Run(() => AntigravitySession.ListModels(missing, new ValueMap<string, string>([])), Bound));
        Assert.Equal("`agy models` produced no output.", error.ErrorDescription);

        var script = new AntigravityScript("bad-utf8");
        script.SetBytes("models.stdout", [0x67, 0x65, 0xFF, 0xFE, 0x0A]);
        var invalid = Assert.Throws<ShellError>(() => ListModels(runtime, script));
        Assert.Equal("`agy models` produced no output.", invalid.ErrorDescription);
    }

    // ---- PR-PROVCLI-428 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-428")] // Services/Providers/AntigravitySession.swift:468-471
    public void Each_catalog_line_is_ANSI_stripped_trimmed_and_split_on_its_first_tab_only()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("lines",
            "\U0000001B[36m  gemini-3-flash \U0000001B[0m\t  Gemini\tFlash  \r\n" + // U+001B ESCAPE
            "\n   \n" +
            "\U0000001B[1mgpt-oss-120b\U0000001B[22m\tGPT OSS 120B\n", "none"); // U+001B ESCAPE
        var models = ListModels(runtime, script);
        Assert.Equal(["gemini-3-flash", "gpt-oss-120b"], models.Select(option => option.Id));
        Assert.Equal(["Gemini\tFlash", "GPT OSS 120B"], models.Select(option => option.Name));
    }

    // ---- PR-PROVCLI-429 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-429")] // Services/Providers/AntigravitySession.swift:472,519-523
    public void Only_a_first_field_made_of_letters_digits_hyphen_underscore_and_period_starting_with_a_letter_or_digit_is_a_model()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("slugs",
            "MODEL NAME\tDISPLAY\n" +
            "Fetching models...\n" +
            "-leading\tNo\n" +
            "_leading\tNo\n" +
            ".leading\tNo\n" +
            "vendor/model\tNo\n" +
            "model:tag\tNo\n" +
            "model+x\tNo\n" +
            "1st_model.v2-x\tFirst\n" +
            "\U000000E9t\U000000E9-model\tAccented\n", "none"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        Assert.Equal(["1st_model.v2-x", "\U000000E9t\U000000E9-model"], ListModels(runtime, script).Select(option => option.Id)); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
    }

    // ---- PR-PROVCLI-430 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-430")] // Services/Providers/AntigravitySession.swift:473-474
    public void A_row_without_a_name_is_named_after_its_slug()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("names", "bare-model\n" + "tabbed-model\t\n" + "named-model\tNamed\n", "none");
        Assert.Equal(["bare-model", "tabbed-model", "Named"], ListModels(runtime, script).Select(option => option.Name));
    }

    // ---- PR-PROVCLI-431 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-431")] // Services/Providers/AntigravitySession.swift:476
    public void A_row_is_current_by_raw_slug_by_base_slug_or_by_an_unanchored_prefix_of_the_current_id()
    {
        using var runtime = new TestRuntime();
        const string rows = "gemini-3.1-pro-low\tPro (Low)\ngemini-2\tTwo\nother\tOther\n";
        Assert.Equal([true, false, false], ListModels(runtime, Catalog("raw", rows, "gemini-3.1-pro-low")).Select(option => option.IsDefault));
        Assert.Equal([true, false, false], ListModels(runtime, Catalog("base", rows, "gemini-3.1-pro")).Select(option => option.IsDefault));
        Assert.Equal([true, false, false], ListModels(runtime, Catalog("suffixed", rows, "gemini-3.1-pro-high")).Select(option => option.IsDefault));
        // "gemini-3" is an unanchored prefix of "gemini-3.1-pro-high" too, so both rows are current.
        Assert.Equal([true, true, false], ListModels(runtime, Catalog("prefix", "gemini-3.1-pro-high\tPro\ngemini-3\tThree\nother\tOther\n", "gemini-3.1-pro-high"))
            .Select(option => option.IsDefault));
    }

    // ---- PR-PROVCLI-432 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-432")] // Services/Providers/AntigravitySession.swift:465-466,478-484,496
    public void The_first_row_of_a_base_slug_fixes_its_base_name_seeds_its_efforts_and_its_place()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("first-row",
            "zeta-high\tZeta (High)\n" +
            "alpha\tAlpha\n" +
            "zeta-low\tZeta Renamed (Low)\n", "none");
        var models = ListModels(runtime, script);
        Assert.Equal(["zeta", "alpha"], models.Select(option => option.Id));
        Assert.Equal("Zeta", models[0].Name);
        Assert.Equal(["low", "high"], models[0].Efforts);
        Assert.Empty(models[1].Efforts);
    }

    // ---- PR-PROVCLI-433 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-433")] // Services/Providers/AntigravitySession.swift:485-492
    public void A_later_row_adds_only_an_unseen_effort_and_can_only_turn_the_default_on()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("later-rows",
            "m-medium\tM (Medium)\n" +
            "m-medium\tAgain (Medium)\n" +
            "m\tPlain\n" +
            "n-low\tN (Low)\n" +
            "m-high\tM (High)\n", "m-medium");
        var models = ListModels(runtime, script);
        Assert.Equal("M", models[0].Name);
        Assert.Equal(["medium", "high"], models[0].Efforts);
        Assert.True(models[0].IsDefault);
        Assert.False(models[1].IsDefault);
    }

    // ---- PR-PROVCLI-434 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-434")] // Services/Providers/AntigravitySession.swift:496-498
    public void Efforts_come_out_low_medium_high_whatever_order_they_were_seen_in()
    {
        using var runtime = new TestRuntime();
        var script = Catalog("effort-order", "x-high\tX (High)\nx-low\tX (Low)\nx-medium\tX (Medium)\ny-medium\tY (Medium)\ny-low\tY (Low)\n", "none");
        var models = ListModels(runtime, script);
        Assert.Equal(["low", "medium", "high"], models[0].Efforts);
        Assert.Equal(["low", "medium"], models[1].Efforts);
    }
}
