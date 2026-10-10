using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/PiCLI.swift (B2 1fbf81e) and the PR-PROVCLI-301 to 312 and
// UI-PROVCLI-020 records. The gate extension (:136-216) is TypeScript that Pi runs; no test launches Node or Pi
// (R-TEST-10), so records 302 to 307 are proven by the exact bytes the port writes, compared with the Swift literal
// (R-STR-9). The catalog probe runs against the offline stand-in tests/fixtures/pi-cli.
public sealed class PiCLITests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static string Scratch(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "PiCLITests", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static ValueMap<string, string> Env(params (string Key, string Value)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)));

    private static SwiftURL AnyExecutable => SwiftURL.Create("marrow-native-path:/nonexistent/pi")!;

    private static ProviderStatus.Auth Auth(TestRuntime runtime, ValueMap<string, string> environment) =>
        runtime.Loop.Run(() => PiCLI.AuthStatus(AnyExecutable, environment), Bound);

    private static JSONValue Json(string text) => JSONValue.Parse(text)!;

    // ---- PR-PROVCLI-308 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-308")] // Services/Providers/PiCLI.swift:20-27
    public void The_config_directory_is_PI_CODING_AGENT_DIR_verbatim_or_home_dot_pi_agent()
    {
        Assert.Equal("/custom/pi dir", PiCLI.ConfigDirectoryPath(Env(("PI_CODING_AGENT_DIR", "/custom/pi dir"), ("HOME", "/home/me"))));
        Assert.Equal(SwiftPath.AppendingPathComponent("/home/me", ".pi/agent"), PiCLI.ConfigDirectoryPath(Env(("HOME", "/home/me"))));
        Assert.Equal(SwiftPath.AppendingPathComponent("/home/me", ".pi/agent"),
            PiCLI.ConfigDirectoryPath(Env(("PI_CODING_AGENT_DIR", ""), ("HOME", "/home/me"))));
        Assert.Equal(SwiftPath.AppendingPathComponent(LoginEnvironment.HomeDirectory, ".pi/agent"), PiCLI.ConfigDirectoryPath(Env(("HOME", ""))));
        Assert.Equal(SwiftPath.AppendingPathComponent(LoginEnvironment.HomeDirectory, ".pi/agent"), PiCLI.ConfigDirectoryPath(Env()));
        // The public URL form is a file URL of that directory.
        var url = PiCLI.ConfigDirectory(Env(("HOME", "/home/me")));
        Assert.Equal("file", url.Scheme);
        Assert.EndsWith("/.pi/agent", url.Path, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-309, UI-PROVCLI-027 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-309")] // Services/Providers/PiCLI.swift:31-37
    public void Auth_json_with_keys_signs_in_with_the_sorted_provider_names()
    {
        using var runtime = new TestRuntime();
        var config = Scratch("auth-keys");
        File.WriteAllText(Path.Combine(config, "auth.json"), "{\"openai\":{\"type\":\"api_key\"},\"anthropic\":{},\"Zed\":1,\"google\":null}", new UTF8Encoding(false));
        // Swift's String < sorts by Unicode scalar: "Z" (U+005A) before "a" (U+0061).
        Assert.Equal(new ProviderStatus.Auth.SignedIn("Zed, anthropic, google, openai"),
            Auth(runtime, Env(("PI_CODING_AGENT_DIR", config), ("OPENAI_API_KEY", "ignored"))));

        // Without PI_CODING_AGENT_DIR the file is <HOME>/.pi/agent/auth.json.
        var home = Scratch("auth-home");
        Directory.CreateDirectory(Path.Combine(home, ".pi", "agent"));
        File.WriteAllText(Path.Combine(home, ".pi", "agent", "auth.json"), "{\"deepseek\":{}}", new UTF8Encoding(false));
        Assert.Equal(new ProviderStatus.Auth.SignedIn("deepseek"), Auth(runtime, Env(("HOME", home))));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-309")] // Services/Providers/PiCLI.swift:32-43
    public void Without_a_usable_auth_json_the_first_non_empty_provider_key_signs_in_with_no_name()
    {
        using var runtime = new TestRuntime();
        var config = Scratch("auth-env");
        var keys = new[] { "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", "OPENROUTER_API_KEY", "GROQ_API_KEY", "MISTRAL_API_KEY", "DEEPSEEK_API_KEY", "XAI_API_KEY" };
        foreach (var key in keys)
        {
            Assert.Equal(new ProviderStatus.Auth.SignedIn(null), Auth(runtime, Env(("PI_CODING_AGENT_DIR", config), (key, "k"))));
        }
        // An empty object, a non-object, malformed JSON and no file at all each fall through to the environment.
        foreach (var content in new[] { "{}", "[\"anthropic\"]", "{not json", "\"x\"" })
        {
            File.WriteAllText(Path.Combine(config, "auth.json"), content, new UTF8Encoding(false));
            Assert.Equal(new ProviderStatus.Auth.SignedIn(null), Auth(runtime, Env(("PI_CODING_AGENT_DIR", config), ("XAI_API_KEY", "k"))));
            Assert.Equal(new ProviderStatus.Auth.SignedOut(), Auth(runtime, Env(("PI_CODING_AGENT_DIR", config), ("ANTHROPIC_API_KEY", ""))));
        }
        // Other keys, and differently cased ones, do not count.
        Assert.Equal(new ProviderStatus.Auth.SignedOut(),
            Auth(runtime, Env(("PI_CODING_AGENT_DIR", Scratch("auth-none")), ("AZURE_API_KEY", "k"), ("anthropic_api_key", "k"))));
    }

    // ---- PR-PROVCLI-301 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-301")] // Services/Providers/PiCLI.swift:13-18; porting/spec/PROVCLI.json Pi start (deviation OD-0008 C7)
    public void The_support_directory_is_Nth_pi_under_local_app_data()
    {
        var localAppData = Scratch("support");
        var url = PiCLI.SupportDirectoryUnder(localAppData);
        Assert.Equal("file", url.Scheme);
        Assert.EndsWith("/Nth/pi", url.Path, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-301")] // Services/Providers/PiCLI.swift:117-126
    public void InstallExtension_creates_the_directory_and_writes_droppy_gate_ts_with_the_extension_bytes()
    {
        var localAppData = Scratch("install");
        var url = PiCLI.InstallExtension(localAppData);
        var file = Path.Combine(localAppData, "Nth", "pi", "droppy-gate.ts");
        Assert.Equal(Encoding.UTF8.GetBytes(PiCLI.ExtensionSource), File.ReadAllBytes(file));
        Assert.Equal("file", url.Scheme);
        Assert.EndsWith("/Nth/pi/droppy-gate.ts", url.Path, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-301")] // Services/Providers/PiCLI.swift:121-124
    public void InstallExtension_rewrites_the_file_only_when_its_bytes_differ()
    {
        var localAppData = Scratch("install-twice");
        PiCLI.InstallExtension(localAppData);
        var file = Path.Combine(localAppData, "Nth", "pi", "droppy-gate.ts");
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);
        PiCLI.InstallExtension(localAppData);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));

        File.WriteAllText(file, "// an older build's extension", new UTF8Encoding(false));
        PiCLI.InstallExtension(localAppData);
        Assert.Equal(Encoding.UTF8.GetBytes(PiCLI.ExtensionSource), File.ReadAllBytes(file));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-301")] // Services/Providers/PiCLI.swift:119
    public void InstallExtension_throws_when_the_directory_cannot_be_made()
    {
        var localAppData = Scratch("install-blocked");
        File.WriteAllText(Path.Combine(localAppData, "Nth"), "a file where the folder should be");
        Assert.ThrowsAny<IOException>(() => PiCLI.InstallExtension(localAppData));
    }

    // ---- PR-PROVCLI-302 .. 307: the extension text ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-301")] // Services/Providers/PiCLI.swift:136-216
    [Trait("Req", "PR-PROVCLI-302")] // Services/Providers/PiCLI.swift:167-186
    [Trait("Req", "PR-PROVCLI-303")] // Services/Providers/PiCLI.swift:148-155,187-195
    [Trait("Req", "PR-PROVCLI-304")] // Services/Providers/PiCLI.swift:140,197-214
    [Trait("Req", "PR-PROVCLI-305")] // Services/Providers/PiCLI.swift:142-146,157-165
    [Trait("Req", "PR-PROVCLI-306")] // Services/Providers/PiCLI.swift:206-209
    [Trait("Req", "PR-PROVCLI-307")] // Services/Providers/PiCLI.swift:210-213
    public void The_extension_source_is_the_Swift_literal_byte_for_byte()
    {
        // The UTF-8 bytes of the #"""...""" literal at :136-216 with its four-space indentation removed and no final
        // newline, as Swift builds it: 3,731 bytes (one U+00B7 MIDDLE DOT), SHA-256 below.
        var bytes = Encoding.UTF8.GetBytes(PiCLI.ExtensionSource);
        Assert.Equal(3731, bytes.Length);
        Assert.Equal("3754ed39aad060edd150f21f7c82ad2179c131f8e3076bb72490b4132f0cd5f8", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.StartsWith("import fs from \"node:fs\";\nimport { Type } from \"typebox\";\n\n", PiCLI.ExtensionSource, StringComparison.Ordinal);
        Assert.EndsWith("\"tool call\") };\n  });\n}", PiCLI.ExtensionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", PiCLI.ExtensionSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-302")] // Services/Providers/PiCLI.swift:167-186
    public void The_extension_awaits_the_bridge_and_registers_its_tools_with_a_middle_dot_label()
    {
        var source = PiCLI.ExtensionSource;
        Assert.Contains("const bridge = process.env.DROPPY_CODE_MCP_BRIDGE ? await import(process.env.DROPPY_CODE_MCP_BRIDGE) : null;", source, StringComparison.Ordinal);
        Assert.Contains("name: \"mcp__\" + tool.server + \"__\" + tool.name,", source, StringComparison.Ordinal);
        Assert.Contains("label: tool.server + \" \U000000B7 \" + tool.name,", source, StringComparison.Ordinal); // U+00B7 MIDDLE DOT
        Assert.Contains("parameters: Type.Unsafe(tool.inputSchema),", source, StringComparison.Ordinal);
        Assert.Contains("return { content: [{ type: \"text\", text }], details: {} };", source, StringComparison.Ordinal);
        Assert.Contains("console.error(\"droppy mcp: \" + (error instanceof Error ? error.message : String(error)));", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-303")] // Services/Providers/PiCLI.swift:148-155,187-195
    public void The_extension_appends_the_final_report_plan_and_system_prompt_segments_in_order()
    {
        var source = PiCLI.ExtensionSource;
        Assert.Contains("const file = process.env.DROPPY_CODE_PI_GATE || \"\";\n    return JSON.parse(fs.readFileSync(file, \"utf8\"));\n  } catch {\n    return {};", source, StringComparison.Ordinal);
        var report = source.IndexOf("if (gate.finalReport) extra.push(\"Reply with your final report now: no tools, one message.\");", StringComparison.Ordinal);
        var plan = source.IndexOf("if (gate.interactionMode === \"plan\") extra.push(\"You are in plan mode: read-only. Do not edit files or run commands that change anything; investigate, then end with the plan as markdown.\");", StringComparison.Ordinal);
        var prompt = source.IndexOf("if (typeof gate.systemPrompt === \"string\" && gate.systemPrompt.length > 0) extra.push(gate.systemPrompt);", StringComparison.Ordinal);
        Assert.True(report > 0 && plan > report && prompt > plan);
        Assert.Contains("if (extra.length === 0) return;\n    return { systemPrompt: event.systemPrompt + \"\\n\\n\" + extra.join(\"\\n\\n\") };", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-304")] // Services/Providers/PiCLI.swift:140,197-214
    public void The_tool_call_hook_runs_its_ladder_in_the_source_order()
    {
        var source = PiCLI.ExtensionSource;
        Assert.Contains("const readOnly = new Set([\"read\", \"grep\", \"find\", \"ls\"]);", source, StringComparison.Ordinal);
        string[] ladder =
        [
            "const gate = readGate();\n    if (gate.finalReport) return { block: true, reason: \"Final report: no tools\" };",
            "if (readOnly.has(event.toolName)) return;",
            "if (gate.interactionMode === \"plan\") return { block: true, reason: \"Plan mode is read-only\" };",
            "if (gate.runtimeMode === \"fullAccess\") return;",
            "const kind = event.toolName === \"bash\" ? \"command\" : (event.toolName === \"edit\" || event.toolName === \"write\") ? \"fileChange\" : \"tool\";",
            "if (kind === \"fileChange\" && (gate.runtimeMode === \"autoAcceptEdits\" || gate.runtimeMode === \"auto\")) return;",
            "if (kind === \"command\" && gate.runtimeMode === \"auto\" && isReadOnlyCommand(event.input && event.input.command)) return;",
            "const answer = await ctx.ui.select(",
        ];
        var hook = source.IndexOf("pi.on(\"tool_call\"", StringComparison.Ordinal);
        var at = hook;
        foreach (var step in ladder)
        {
            var next = source.IndexOf(step, StringComparison.Ordinal);
            Assert.True(next > at, step);
            at = next;
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-305")] // Services/Providers/PiCLI.swift:142-146,157-165
    public void The_read_only_command_test_splits_on_four_separators_and_checks_every_segment()
    {
        var source = PiCLI.ExtensionSource;
        Assert.Contains("const readOnlyBinaries = new Set([\n  \"ls\", \"cat\", \"head\", \"tail\", \"grep\", \"rg\", \"find\", \"wc\", \"pwd\", \"echo\",\n  \"which\", \"stat\", \"file\", \"du\", \"tree\", \"sort\", \"uniq\", \"diff\",\n]);", source, StringComparison.Ordinal);
        Assert.Contains("const readOnlyGit = new Set([\"status\", \"diff\", \"log\", \"show\", \"branch\", \"blame\"]);", source, StringComparison.Ordinal);
        Assert.Contains("const segments = String(command ?? \"\").split(/&&|\\|\\||;|\\|/);", source, StringComparison.Ordinal);
        Assert.Contains("const words = segment.trim().split(/\\s+/).filter(Boolean);\n    if (words.length === 0) return true;\n    if (words[0] === \"git\") return words.length > 1 && readOnlyGit.has(words[1]);\n    return readOnlyBinaries.has(words[0]);", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-306")] // Services/Providers/PiCLI.swift:206-209
    [Trait("Req", "PR-PROVCLI-307")] // Services/Providers/PiCLI.swift:210-213
    public void The_ask_truncates_top_level_strings_and_selects_over_a_droppy_gate_title()
    {
        var source = PiCLI.ExtensionSource;
        Assert.Contains("for (const [key, value] of Object.entries(event.input || {})) {\n      input[key] = typeof value === \"string\" && value.length > 20000 ? value.slice(0, 20000) : value;\n    }", source, StringComparison.Ordinal);
        Assert.Contains("const payload = { kind, toolName: event.toolName, toolCallId: event.toolCallId, input };", source, StringComparison.Ordinal);
        Assert.Contains("await ctx.ui.select(\"droppy-gate \" + JSON.stringify(payload), [\"Allow\", \"Allow for session\", \"Deny\"]);", source, StringComparison.Ordinal);
        Assert.Contains("if (answer === \"Allow\" || answer === \"Allow for session\") return undefined;", source, StringComparison.Ordinal);
        Assert.Contains("return { block: true, reason: \"The user declined this \" + (kind === \"command\" ? \"command\" : kind === \"fileChange\" ? \"file change\" : \"tool call\") };", source, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-312, UI-PROVCLI-020 ----

    private const string Models = "{\"id\":\"models\",\"type\":\"response\",\"success\":true,\"data\":{\"models\":[" +
        "{\"provider\":\"openai\",\"id\":\"gpt-5\",\"name\":\"GPT-5\",\"reasoning\":true}," +
        "{\"provider\":\"anthropic\",\"id\":\"claude-x\",\"name\":\"Claude X\",\"reasoning\":false}," +
        "{\"provider\":\"anthropic\",\"id\":\"a-model\"}," +
        "{\"provider\":3,\"id\":\"bad-provider\"}," +
        "{\"id\":\"no-provider\"}," +
        "{\"provider\":\"google\"}," +
        "{\"provider\":\"google\",\"id\":\"gemini\",\"reasoning\":\"true\"}" +
        "]}}";

    [Fact]
    [Trait("Req", "PR-PROVCLI-312")] // Services/Providers/PiCLI.swift:85-112
    [Trait("Req", "UI-PROVCLI-020")] // Services/Providers/PiCLI.swift:99-111,11
    public void The_catalog_maps_provider_slash_id_rows_sorted_by_provider_then_name()
    {
        var state = Json("{\"id\":\"state\",\"type\":\"response\",\"data\":{\"model\":{\"provider\":\"anthropic\",\"id\":\"claude-x\"}}}");
        var options = PiCLI.Catalog(state, Json(Models));
        var levels = new ValueList<string>(["off", "minimal", "low", "medium", "high", "xhigh", "max"]);
        Assert.Equal(
            [
                new ModelOption("anthropic/claude-x", "Claude X", detail: "anthropic", isDefault: true),
                new ModelOption("anthropic/a-model", "a-model", detail: "anthropic"),
                new ModelOption("google/gemini", "gemini", detail: "google"),
                new ModelOption("openai/gpt-5", "GPT-5", detail: "openai", efforts: levels, defaultEffort: "medium"),
            ],
            options);
        Assert.Equal(levels, PiCLI.ThinkingLevels);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-312")] // Services/Providers/PiCLI.swift:85-104
    public void The_current_model_is_matched_by_full_id_when_it_has_a_slash_and_by_parts_otherwise()
    {
        IReadOnlyList<string> Defaults(string state, string? models = null) =>
            PiCLI.Catalog(Json(state), Json(models ?? Models)).Where(o => o.IsDefault).Select(o => o.Id).ToList();
        Assert.Equal(["openai/gpt-5"], Defaults("{\"data\":{\"model\":{\"provider\":\"anthropic\",\"id\":\"openai/gpt-5\"}}}"));
        Assert.Empty(Defaults("{\"data\":{\"model\":{\"provider\":\"openai\",\"id\":\"anthropic/gpt-5\"}}}"));
        // Without "data" the response itself is the state.
        Assert.Equal(["google/gemini"], Defaults("{\"model\":{\"provider\":\"google\",\"id\":\"gemini\"}}"));
        // A JSON-null "data" is the state, so the top-level model is never read.
        Assert.Empty(Defaults("{\"data\":null,\"model\":{\"provider\":\"google\",\"id\":\"gemini\"}}"));
        // Both a provider and an id are needed.
        Assert.Empty(Defaults("{\"data\":{\"model\":{\"id\":\"gemini\"}}}"));
        Assert.Empty(Defaults("{\"data\":{\"model\":{\"provider\":\"google\"}}}"));
        Assert.DoesNotContain(PiCLI.Catalog(null, Json(Models)), o => o.IsDefault);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-312")] // Services/Providers/PiCLI.swift:91,107-111
    public void The_items_fall_back_to_a_top_level_models_array_and_an_empty_catalog_fails()
    {
        var fallback = PiCLI.Catalog(null, Json("{\"data\":{\"models\":\"not an array\"},\"models\":[{\"provider\":\"xai\",\"id\":\"grok\"}]}"));
        Assert.Equal(["xai/grok"], fallback.Select(o => o.Id).ToList());
        foreach (var models in new[] { "{\"data\":{\"models\":[]},\"models\":[{\"provider\":\"xai\",\"id\":\"grok\"}]}", "{}", "{\"models\":[{\"id\":\"x\"}]}" })
        {
            var error = Assert.Throws<ProviderError>(() => PiCLI.Catalog(null, Json(models)));
            Assert.Equal(new ProviderError.Case.Failed("Pi listed no models."), error.ErrorCase);
        }
        var none = Assert.Throws<ProviderError>(() => PiCLI.Catalog(null, null));
        Assert.Equal("Pi listed no models.", ((ProviderError.Case.Failed)none.ErrorCase).Message);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-312")] // Services/Providers/PiCLI.swift:111
    public void Rows_sort_by_provider_then_name_in_Swift_string_order()
    {
        var options = PiCLI.Catalog(null, Json("{\"models\":[" +
            "{\"provider\":\"b\",\"id\":\"1\",\"name\":\"zeta\"}," +
            "{\"provider\":\"a\",\"id\":\"2\",\"name\":\"beta\"}," +
            "{\"provider\":\"b\",\"id\":\"3\",\"name\":\"Alpha\"}," +
            "{\"provider\":\"B\",\"id\":\"4\",\"name\":\"any\"}," +
            "{\"provider\":\"a\",\"id\":\"5\",\"name\":\"beta\"}]}"));
        // "B" (U+0042) sorts before "a" and "b"; "Alpha" before "zeta"; equal pairs keep their order.
        Assert.Equal(["B/4", "a/2", "a/5", "b/3", "b/1"], options.Select(o => o.Id).ToList());
    }

    // ---- PR-PROVCLI-310, 311 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-310")] // Services/Providers/PiCLI.swift:46-64
    [Trait("Req", "PR-PROVCLI-311")] // Services/Providers/PiCLI.swift:65-83
    public void The_probe_launches_eight_tokens_in_home_asks_state_and_models_and_stops_once_both_arrive()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("probe");
        var home = Scratch("probe-home");
        script.Reply(0, "{\"type\":\"event\",\"id\":\"other\"}", "{\"type\":\"response\",\"command\":\"get_state\",\"id\":\"state\",\"data\":{\"model\":{\"provider\":\"openai\",\"id\":\"gpt-5\"}}}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"models\",\"data\":{\"models\":[{\"provider\":\"openai\",\"id\":\"gpt-5\",\"reasoning\":true}]}}", "{\"id\":\"state\",\"data\":{}}");
        var environment = Env((script.EnvironmentEntry.Key, script.EnvironmentEntry.Value), ("HOME", home), ("PI_SKIP_VERSION_CHECK", "0"));
        var options = runtime.Loop.Run(() => PiCLI.ListModels(SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!, environment), Bound);
        Assert.Equal(["openai/gpt-5"], options.Select(o => o.Id).ToList());
        Assert.True(options[0].IsDefault);

        Assert.Equal(["--mode", "rpc", "--no-session", "--no-extensions", "--no-skills", "--no-prompt-templates", "--no-themes", "--no-context-files"], script.Arguments());
        Assert.Equal(Path.GetFullPath(home).TrimEnd('\\', '/'), Path.GetFullPath(script.WorkingDirectory()!).TrimEnd('\\', '/'));
        Assert.Equal("1", script.Environment("PI_SKIP_VERSION_CHECK"));
        Assert.Null(script.Environment("DROPPY_CODE_PI_GATE"));
        Assert.Null(script.Environment("DROPPY_CODE_MCP_BRIDGE"));
        // Swift's compact JSON leaves key order unspecified (R-TEST-4): the lines are compared as JSON.
        var lines = script.StdinLines();
        Assert.Equal(2, lines.Count);
        Assert.Equal(Json("{\"type\":\"get_state\",\"id\":\"state\"}"), Json(lines[0]));
        Assert.Equal(Json("{\"type\":\"get_available_models\",\"id\":\"models\"}"), Json(lines[1]));
        // The process is terminated once both replies are in.
        runtime.Loop.RunUntil(() => script.SawEndOfInput, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-311")] // Services/Providers/PiCLI.swift:76-79
    public void A_probe_that_never_answers_is_terminated_after_thirty_seconds_and_lists_no_models()
    {
        using var runtime = new TestRuntime();
        var script = new PiScript("probe-silent");
        script.Reply(0, "{\"id\":\"state\",\"data\":{}}");
        Task<ValueList<ModelOption>>? pending = null;
        runtime.OnMain(() => pending = PiCLI.ListModels(SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!,
            Env((script.EnvironmentEntry.Key, script.EnvironmentEntry.Value), ("HOME", Scratch("silent-home")))));
        // Stdin can be delivered before the detached timeout body ever gets a pool thread.
        runtime.Loop.RunUntil(() => script.StdinLines().Count == 2 &&
            runtime.Time.TimerDeadlines.Contains(TimeSpan.FromSeconds(30)), Bound);
        runtime.Advance(TimeSpan.FromMilliseconds(29_999));
        Assert.False(pending!.IsCompleted);
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        runtime.Loop.RunUntil(() => pending.IsCompleted, Bound);
        var error = Assert.IsType<ProviderError>(pending.Exception!.InnerException);
        Assert.Equal(new ProviderError.Case.Failed("Pi listed no models."), error.ErrorCase);
        runtime.Loop.RunUntil(() => script.SawEndOfInput, Bound);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-310")] // Services/Providers/PiCLI.swift:57-61
    public void A_probe_that_cannot_start_fails_with_the_Pi_could_not_start_prefix()
    {
        using var runtime = new TestRuntime();
        var missing = Path.Combine(Scratch("missing"), "pi.exe");
        var error = Assert.Throws<ProviderError>(() =>
            runtime.Loop.Run(() => PiCLI.ListModels(SwiftURL.Create("marrow-native-path:" + missing.Replace('\\', '/'))!, Env(("HOME", Scratch("missing-home")))), Bound));
        var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.StartsWith("Pi could not start: ", failed.Message, StringComparison.Ordinal);
    }
}
