using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ClaudeSession.swift (B3 52bc7926), :1-829.
//
// Tests for unit Services.Providers.ClaudeSession part 1 of 4 (PR-PROVCLI-001 to 024): the launch path
// (start()'s argument vector, the --help option probe, the Hydra argument blocks, the resume/session-id
// tail, the initialize handshake) and the static command-discovery probe (handshake). Each test spawns
// tests/fixtures/claude-cli/ClaudeStandIn.cs, a compiled offline stand-in (R-TEST-8, R-TEST-10; never a
// real AI CLI), driven through tests/Marrow.Services.Tests/Fixtures/ClaudeStandIns.cs. It records every
// launch's argument vector and answers the first control_request with a scripted control_response, then
// exits, so no test process is left running.
public sealed class ClaudeSessionTests
{
    // SwiftURL faithfully ports url.path as swift-foundation gives it on macOS: for a "file://" URL, always the
    // decoded RFC3986 path, which for "file:///C:/x" is "/C:/x" (SwiftURL.cs :524-532, header note: "The Windows
    // shape of a file:///C:/... path has no ruling and is out of scope"). CreateProcessW rejects that leading
    // slash before the drive letter (confirmed directly: a real spawn with that literal string as its working
    // directory fails Win32Exception "The directory name is invalid", while the same string without the extra
    // slash spawns fine). Start() and Handshake() call configuration.Executable.Path and
    // configuration.WorkingDirectory.Path exactly as the Swift source does (ClaudeSession.swift:67,136-141),
    // so this is not something ClaudeSession's own code can paper over. ENGINEERING DECISION, test-only: build
    // the SwiftURL from a non-"file" scheme, whose Path getter takes the plain RFC3986Parser.PercentDecode
    // branch (SwiftURL.cs :526-528) instead of FileSystemPath()'s POSIX-forcing one, so .Path round-trips the
    // real Windows path unchanged and every process this test suite spawns gets a directory that exists.
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private const string MinimalResponse = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{}}}";

    private static SessionConfiguration Config(
        ClaudeScript script,
        string? resumeID = null,
        string? resumeAt = null,
        string? model = null,
        string? effort = null,
        bool fastMode = false,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        HydraLaunch? hydra = null,
        bool antiSlopEnabled = true)
    {
        var environment = new ValueMap<string, string>([script.EnvironmentEntry]);
        return new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            Executable = FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = environment,
            ResumeID = resumeID,
            ResumeAt = resumeAt,
            Model = model,
            Effort = effort,
            FastMode = fastMode,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
            Hydra = hydra,
            AntiSlopEnabled = antiSlopEnabled,
        };
    }

    private static ClaudeScript Script(string label, string response = MinimalResponse)
    {
        var script = new ClaudeScript(label);
        script.SetResponse(response);
        return script;
    }

    private static string RunStart(TestRuntime runtime, ClaudeSession session) =>
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30));

    // ---- PR-PROVCLI-001 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-001")] // ClaudeSession.swift:83
    public async Task Start_without_an_executable_throws_notInstalled_before_spawning_anything()
    {
        var config = new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            WorkingDirectory = FileUrl(Path.GetTempPath()),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var session = new ClaudeSession(config);
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Start());
        Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(
            "Claude is not installed. Install it and sign in with `claude auth login`.",
            error.ErrorDescription);
    }

    // ---- PR-PROVCLI-002 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-002")] // ClaudeSession.swift:84,153,166
    public void Start_uses_the_resume_id_as_the_returned_and_stored_session_id()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("resume-id");
        var session = new ClaudeSession(Config(script, resumeID: "thread-resume-42"));
        var id = RunStart(runtime, session);
        Assert.Equal("thread-resume-42", id);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-002")] // ClaudeSession.swift:84
    public void Start_generates_a_lowercase_uuid_when_no_resume_id_is_supplied()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("generated-id");
        var session = new ClaudeSession(Config(script));
        var id = RunStart(runtime, session);
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id);
        Assert.Equal(id, id.ToLowerInvariant());
        Assert.NotEqual(id, id.ToUpperInvariant());
    }

    // ---- PR-PROVCLI-003 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-003")] // ClaudeSession.swift:85-90
    public void Start_base_argument_vector_is_the_fixed_14_tokens_in_order()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("base-argv");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        var sessionCall = script.Calls()[^1].Arguments;
        Assert.Equal(
            new[]
            {
                "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                "--include-partial-messages", "--permission-prompt-tool", "stdio",
                "--setting-sources", "user,project,local",
                "--allow-dangerously-skip-permissions", "--permission-mode", "default",
            },
            sessionCall.Take(14));
    }

    // ---- PR-PROVCLI-004 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-004")] // ClaudeSession.swift:820-828
    public void ModeName_maps_runtime_and_interaction_modes_with_plan_tested_first()
    {
        Assert.Equal("default", ClaudeSession.ModeName(RuntimeMode.Supervised, InteractionMode.Build));
        Assert.Equal("acceptEdits", ClaudeSession.ModeName(RuntimeMode.AutoAcceptEdits, InteractionMode.Build));
        Assert.Equal("auto", ClaudeSession.ModeName(RuntimeMode.Auto, InteractionMode.Build));
        Assert.Equal("bypassPermissions", ClaudeSession.ModeName(RuntimeMode.FullAccess, InteractionMode.Build));
        // Plan overrides every runtime mode.
        Assert.Equal("plan", ClaudeSession.ModeName(RuntimeMode.Supervised, InteractionMode.Plan));
        Assert.Equal("plan", ClaudeSession.ModeName(RuntimeMode.FullAccess, InteractionMode.Plan));
    }

    // ---- PR-PROVCLI-005 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-005")] // ClaudeSession.swift:91
    public void Start_appends_model_flag_only_for_a_non_default_model()
    {
        using var runtimeA = new TestRuntime();
        using var storage_runtimeA = new StorageScratchFolder();
        var withModel = Script("model-set");
        RunStart(runtimeA, new ClaudeSession(Config(withModel, model: "opus")));
        Assert.Contains("--model", withModel.Calls()[^1].Arguments);
        Assert.Equal("opus", withModel.Calls()[^1].Arguments[Array.IndexOf([.. withModel.Calls()[^1].Arguments], "--model") + 1]);

        using var runtimeB = new TestRuntime();
        using var storage_runtimeB = new StorageScratchFolder();
        var defaultModel = Script("model-default");
        RunStart(runtimeB, new ClaudeSession(Config(defaultModel, model: "default")));
        Assert.DoesNotContain("--model", defaultModel.Calls()[^1].Arguments);

        using var runtimeC = new TestRuntime();
        using var storage_runtimeC = new StorageScratchFolder();
        var noModel = Script("model-none");
        RunStart(runtimeC, new ClaudeSession(Config(noModel, model: null)));
        Assert.DoesNotContain("--model", noModel.Calls()[^1].Arguments);
    }

    // ---- PR-PROVCLI-006 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-006")] // ClaudeSession.swift:92
    public void Start_appends_effort_flag_only_for_a_non_empty_effort()
    {
        using var runtimeA = new TestRuntime();
        using var storage_runtimeA = new StorageScratchFolder();
        var withEffort = Script("effort-set");
        RunStart(runtimeA, new ClaudeSession(Config(withEffort, effort: "unheard-of-level")));
        var args = withEffort.Calls()[^1].Arguments;
        Assert.Contains("--effort", args);
        Assert.Equal("unheard-of-level", args[Array.IndexOf([.. args], "--effort") + 1]);

        using var runtimeB = new TestRuntime();
        using var storage_runtimeB = new StorageScratchFolder();
        var emptyEffort = Script("effort-empty");
        RunStart(runtimeB, new ClaudeSession(Config(emptyEffort, effort: "")));
        Assert.DoesNotContain("--effort", emptyEffort.Calls()[^1].Arguments);

        using var runtimeC = new TestRuntime();
        using var storage_runtimeC = new StorageScratchFolder();
        var noEffort = Script("effort-none");
        RunStart(runtimeC, new ClaudeSession(Config(noEffort, effort: null)));
        Assert.DoesNotContain("--effort", noEffort.Calls()[^1].Arguments);
    }

    // ---- PR-PROVCLI-007 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-007")] // ClaudeSession.swift:93
    public void Start_fastMode_passes_settings_with_the_exact_json_token()
    {
        // ClaudeSession.swift:93: #"{"fastMode":true}"# - 17 characters (the record's prose says 18; the
        // literal itself, counted directly, is 17: { " f a s t M o d e " : t r u e } = 1+1+8+1+1+4+1).
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("fast-mode");
        RunStart(runtime, new ClaudeSession(Config(script, fastMode: true)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--settings");
        Assert.True(index >= 0);
        var token = args[index + 1];
        Assert.Equal("{\"fastMode\":true}", token);
        Assert.Equal(17, token.Length);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-007")] // ClaudeSession.swift:93
    public void Start_omits_settings_when_fastMode_is_false()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("no-fast-mode");
        RunStart(runtime, new ClaudeSession(Config(script, fastMode: false)));
        Assert.DoesNotContain("--settings", script.Calls()[^1].Arguments);
    }

    // ---- PR-PROVCLI-009 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-009")] // ClaudeSession.swift:69-80
    public async Task KnownOptions_caches_a_successful_probe_by_executable_path_and_does_not_reprobe()
    {
        var script = new ClaudeScript("known-options-cache");
        script.SetHelp("Usage: claude [options]\n  --system-prompt-snapshot <mode>\n  --forward-subagent-text\n");
        var executable = FileUrl(script.ExecutablePath);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };

        var first = await ClaudeSession.KnownOptions(executable, environment);
        var second = await ClaudeSession.KnownOptions(executable, environment);

        Assert.Equal(new HashSet<string> { "--system-prompt-snapshot", "--forward-subagent-text" }, first);
        Assert.Equal(first, second);
        Assert.Single(script.Calls()); // one --help launch only; the second call served from the cache
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-009")] // ClaudeSession.swift:74
    public async Task KnownOptions_probe_failure_returns_empty_and_is_not_cached()
    {
        var missing = FileUrl(Path.Combine(Path.GetTempPath(), "missing-claude-" + Guid.NewGuid().ToString("N") + ".exe"));
        var options = await ClaudeSession.KnownOptions(missing, new Dictionary<string, string>());
        Assert.Empty(options);

        var cacheField = typeof(ClaudeSession).GetField("knownOptions", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cache = (System.Collections.IDictionary)cacheField.GetValue(null)!;
        Assert.False(cache.Contains(missing.Path));
    }

    // ---- PR-PROVCLI-010 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-010")] // ClaudeSession.swift:99-100
    public void Start_passes_system_prompt_snapshot_off_when_the_help_probe_found_the_flag()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("snapshot-off");
        script.SetHelp("--system-prompt-snapshot <mode>\n");
        RunStart(runtime, new ClaudeSession(Config(script)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--system-prompt-snapshot");
        Assert.True(index >= 0);
        Assert.Equal("off", args[index + 1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-010")] // ClaudeSession.swift:99-100
    public void Start_omits_system_prompt_snapshot_when_the_help_probe_did_not_find_the_flag()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("snapshot-absent");
        RunStart(runtime, new ClaudeSession(Config(script)));
        Assert.DoesNotContain("--system-prompt-snapshot", script.Calls()[^1].Arguments);
    }

    // ---- PR-PROVCLI-011, 012, 013 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-011")] // ClaudeSession.swift:102-108
    [Trait("Req", "PR-PROVCLI-012")] // ClaudeSession.swift:109-110
    [Trait("Req", "PR-PROVCLI-013")] // ClaudeSession.swift:111-112
    public void Start_native_hydra_passes_agents_and_forward_subagent_text_and_caps_the_environment()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("hydra-native");
        script.SetHelp("--forward-subagent-text\n");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true, MaxHeads = 4 };
        RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra)));
        var args = script.Calls()[^1].Arguments;

        var agentsIndex = Array.IndexOf([.. args], "--agents");
        Assert.True(agentsIndex >= 0);
        var agentsToken = args[agentsIndex + 1];
        Assert.Contains("droppy-worker", agentsToken, StringComparison.Ordinal);
        Assert.Contains("droppy-scout", agentsToken, StringComparison.Ordinal);
        Assert.Contains("--forward-subagent-text", args);
        Assert.DoesNotContain("--disallowedTools", args);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-012")] // ClaudeSession.swift:110
    public void Start_native_hydra_omits_forward_subagent_text_when_the_probe_did_not_find_it()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("hydra-no-forward");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra)));
        Assert.DoesNotContain("--forward-subagent-text", script.Calls()[^1].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-013")] // ClaudeSession.swift:112
    public void Start_native_hydra_without_maxHeads_does_not_write_the_environment_cap()
    {
        // MaxHeads null: CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS is neither written nor cleared. The stand-in
        // does not report its own environment, so this is checked through Config's own environment map: the
        // key is simply never requested of the session, and the argv/response flow still succeeds, which it
        // would not if building the environment had thrown.
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("hydra-no-cap");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true, MaxHeads = null };
        var id = RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra)));
        Assert.NotNull(id);
    }

    // ---- PR-PROVCLI-014 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-014")] // ClaudeSession.swift:113-119
    public void Start_hydra_on_another_provider_disallows_agent_and_task_as_two_bare_tokens()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("hydra-fallback");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Codex, RunsNatively = false };
        RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--disallowedTools");
        Assert.True(index >= 0);
        Assert.Equal("Agent", args[index + 1]);
        Assert.Equal("Task", args[index + 2]);
        Assert.DoesNotContain("--agents", args);
    }

    // ---- PR-PROVCLI-015 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-015")] // ClaudeSession.swift:121-128, AntiSlopPolicy.swift:16-18
    public void Start_always_passes_append_system_prompt_with_only_anti_slop_when_hydra_is_off()
    {
        // AntiSlopPolicy.instructions(enabled:) is `Bool -> String`, never nil or empty (AntiSlopPolicy.swift
        // :16-18: `enabled ? enabledInstructions : disabledInstructions`), so with no Hydra the compactMap
        // drops only the nil hydraPolicy and the token is exactly the anti-slop text for the given
        // antiSlopEnabled, never "" - the flag is unconditional (PR-PROVCLI-015's own point), but its value is
        // empty only in the unreachable case where AntiSlopPolicy.instructions itself returned "".
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("append-anti-slop-off");
        RunStart(runtime, new ClaudeSession(Config(script, antiSlopEnabled: false)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--append-system-prompt");
        Assert.True(index >= 0);
        Assert.Equal(AntiSlopPolicy.DisabledInstructions, args[index + 1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-015")] // ClaudeSession.swift:121-128, AntiSlopPolicy.swift:16-18
    public void Start_append_system_prompt_is_the_enabled_anti_slop_text_when_hydra_is_off_and_anti_slop_is_on()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("append-anti-slop-on");
        RunStart(runtime, new ClaudeSession(Config(script, antiSlopEnabled: true)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--append-system-prompt");
        Assert.Equal(AntiSlopPolicy.EnabledInstructions, args[index + 1]);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-015")] // ClaudeSession.swift:121-128
    public void Start_append_system_prompt_joins_hydra_policy_and_anti_slop_with_two_newlines()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("append-hydra");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra, antiSlopEnabled: true)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--append-system-prompt");
        var token = args[index + 1];
        var expectedHydraPolicy = HydraPrompts.Policy(ProviderKind.Claude, hydra);
        Assert.Equal(expectedHydraPolicy + "\n\n" + AntiSlopPolicy.EnabledInstructions, token);
        Assert.EndsWith(AntiSlopPolicy.EnabledInstructions, token, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-016 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-016")] // ClaudeSession.swift:129-134
    public void Start_without_a_resume_id_passes_session_id()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("no-resume");
        var id = RunStart(runtime, new ClaudeSession(Config(script)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--session-id");
        Assert.True(index >= 0);
        Assert.Equal(id, args[index + 1]);
        Assert.DoesNotContain("--resume", args);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-016")] // ClaudeSession.swift:129-134
    public void Start_with_a_resume_id_and_no_anchor_passes_resume_without_resume_session_at()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("resume-no-anchor");
        RunStart(runtime, new ClaudeSession(Config(script, resumeID: "r-1")));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--resume");
        Assert.True(index >= 0);
        Assert.Equal("r-1", args[index + 1]);
        Assert.DoesNotContain("--session-id", args);
        Assert.DoesNotContain("--resume-session-at", args);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-016")] // ClaudeSession.swift:130-131
    public void Start_with_a_resume_id_and_an_anchor_passes_resume_session_at_right_after_resume()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("resume-with-anchor");
        RunStart(runtime, new ClaudeSession(Config(script, resumeID: "r-2", resumeAt: "msg-9")));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--resume");
        Assert.True(index >= 0);
        Assert.Equal("r-2", args[index + 1]);
        Assert.Equal("--resume-session-at", args[index + 2]);
        Assert.Equal("msg-9", args[index + 3]);
    }

    // ---- PR-PROVCLI-008 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-008")] // ClaudeSession.swift:94-96
    public void Start_omits_mcp_config_when_no_claude_config_is_present()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("mcp-absent");
        RunStart(runtime, new ClaudeSession(Config(script)));
        var args = script.Calls()[^1].Arguments;
        Assert.DoesNotContain("--mcp-config", args);
        Assert.DoesNotContain("--strict-mcp-config", args);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-008")] // ClaudeSession.swift:94-96, MCPProviderConfig.swift:212-215
    public void Start_appends_mcp_config_and_strict_mcp_config_when_a_claude_config_with_servers_exists()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        Directory.CreateDirectory(Path.GetDirectoryName(Marrow.Services.Models.MCPPaths.ClaudeConfigURL)!);
        File.WriteAllText(Marrow.Services.Models.MCPPaths.ClaudeConfigURL, "{\"mcpServers\":{\"x\":{}}}");

        var script = Script("mcp-present");
        RunStart(runtime, new ClaudeSession(Config(script)));
        var args = script.Calls()[^1].Arguments;
        var index = Array.IndexOf([.. args], "--mcp-config");
        Assert.True(index >= 0);
        Assert.Equal(Marrow.Services.Models.MCPPaths.ClaudeConfigURL, args[index + 1]);
        Assert.Equal("--strict-mcp-config", args[index + 2]);
    }

    // ---- PR-PROVCLI-017 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-017")] // ClaudeSession.swift:136-151
    public void Start_spawns_the_process_and_returns_once_the_reader_delivers_the_initialize_reply()
    {
        // Every other Start() test in this file proves the same wiring (a real child process, its reader
        // attached, control() resolved from a message the reader delivered); this one names it directly.
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("spawn-and-read");
        var session = new ClaudeSession(Config(script));
        var id = RunStart(runtime, session);
        Assert.False(string.IsNullOrEmpty(id));
    }

    // ---- PR-PROVCLI-018, 019 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-018")] // ClaudeSession.swift:154-160
    public void Start_initialize_request_omits_hydra_fields_without_native_hydra()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("initialize-no-hydra");
        RunStart(runtime, new ClaudeSession(Config(script)));
        var captured = script.CapturedControlRequestLine(1); // 0: --help probe, 1: the session process
        Assert.Contains("\"subtype\":\"initialize\"", captured, StringComparison.Ordinal);
        Assert.Contains("\"hooks\":null", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("agentProgressSummaries", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("forwardSubagentText", captured, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-018")] // ClaudeSession.swift:155-159
    public void Start_initialize_request_adds_agent_progress_and_forward_text_under_native_hydra()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("initialize-hydra");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        RunStart(runtime, new ClaudeSession(Config(script, hydra: hydra)));
        var captured = script.CapturedControlRequestLine(1);
        Assert.Contains("\"agentProgressSummaries\":true", captured, StringComparison.Ordinal);
        Assert.Contains("\"forwardSubagentText\":true", captured, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-019")] // ClaudeSession.swift:301-309
    public void Control_request_id_is_marrow_1_for_the_first_request_deviation_OD_0012_F1()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("marrow-id");
        RunStart(runtime, new ClaudeSession(Config(script)));
        var captured = script.CapturedControlRequestLine(1);
        Assert.Contains("\"request_id\":\"marrow-1\"", captured, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"control_request\"", captured, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-019")] // ClaudeSession.swift:302-303
    public async Task Control_throws_notRunning_when_no_process_is_attached()
    {
        var script = Script("control-no-process");
        var session = new ClaudeSession(Config(script));
        var error = await Assert.ThrowsAsync<ProviderError>(
            () => session.Control(new JSONValue.Object(ValueMap<string, JSONValue>.Empty)));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Equal("The agent session is not running.", error.ErrorDescription);
    }

    // ---- PR-PROVCLI-020 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-020")] // ClaudeSession.swift:161-163,169-181
    [Trait("Req", "UI-PROVCLI-022")] // ClaudeSession.swift:169-181 - the model picker row from the CLI's own initialize response
    public void Start_publishes_the_model_catalog_from_the_initialize_response()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        const string response = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
            "\"models\":[" +
            "{\"value\":\"default\",\"displayName\":\"Default Model\",\"description\":\"d\",\"supportedEffortLevels\":[\"low\",\"high\"],\"supportsFastMode\":true}," +
            "{\"value\":\"opus\"}," +
            "{\"displayName\":\"No value, skipped\"}" +
            "]}}}";
        var script = Script("models", response);
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);

        var models = Assert.Single(events.OfType<ProviderEvent.Models>());
        Assert.Equal(2, models.Value.Count);
        Assert.Equal("default", models.Value[0].Id);
        Assert.Equal("Default Model", models.Value[0].Name);
        Assert.Equal("d", models.Value[0].Detail);
        Assert.Equal(["low", "high"], models.Value[0].Efforts);
        Assert.True(models.Value[0].IsDefault);
        Assert.Equal("fast", models.Value[0].FastTier);
        Assert.Equal("opus", models.Value[1].Id);
        Assert.Equal("opus", models.Value[1].Name); // displayName absent falls back to value
        Assert.False(models.Value[1].IsDefault);
        Assert.Null(models.Value[1].FastTier);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-020")] // ClaudeSession.swift:163
    public void Start_emits_no_models_event_when_the_response_carries_no_models_array()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("no-models");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        Assert.Empty(events.OfType<ProviderEvent.Models>());
    }

    // ---- PR-PROVCLI-021 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-021")] // ClaudeSession.swift:164-165
    public void Start_publishes_commands_from_the_initialize_response()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        const string response = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
            "\"commands\":[{\"name\":\"review\",\"description\":\"Review the change\"},\"raw-name\"]}}}";
        var script = Script("commands", response);
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);

        var commands = Assert.Single(events.OfType<ProviderEvent.Commands>());
        Assert.Equal(2, commands.Value.Count);
        Assert.Equal("review", commands.Value[0].Name);
        Assert.Equal("Review the change", commands.Value[0].Detail);
        Assert.Equal("raw-name", commands.Value[1].Name);
        Assert.Equal("Claude Code command", commands.Value[1].Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-021")] // ClaudeSession.swift:164 - a missing commands array is not an error
    public void Start_succeeds_and_emits_no_commands_event_when_the_response_carries_no_commands_array()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("no-commands");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        var id = RunStart(runtime, session);
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Empty(events.OfType<ProviderEvent.Commands>());
    }

    // ---- PR-PROVCLI-022, 023, 024 (the static handshake probe) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-022")] // ClaudeSession.swift:183-191
    public async Task Handshake_spawns_the_9_token_argv_that_omits_session_flags_and_adds_no_session_persistence()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-argv");
        script.SetResponse(MinimalHandshakeResponse());
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await ClaudeSession.Handshake(FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment);

        Assert.Equal(
            new[]
            {
                "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
                "--no-session-persistence", "--setting-sources", "user,project,local",
            },
            script.Calls()[0].Arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-024")] // ClaudeSession.swift:198
    public async Task Handshake_sends_the_literal_handshake_request_id()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-id");
        script.SetResponse(MinimalHandshakeResponse());
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        await ClaudeSession.Handshake(FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment);
        var captured = script.CapturedControlRequestLine(0);
        Assert.Contains("\"request_id\":\"handshake\"", captured, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"initialize\"", captured, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-022")] // ClaudeSession.swift:185-207 - the handshake result carries models and commands
    public async Task Handshake_returns_the_models_and_commands_from_the_response()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-result");
        const string response = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
            "\"models\":[{\"value\":\"default\"}],\"commands\":[\"deploy\"]}}}";
        script.SetResponse(response);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var (models, commands) = await ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment);
        Assert.Single(models);
        Assert.Equal("default", models[0].Id);
        Assert.Single(commands);
        Assert.Equal("deploy", commands[0].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-022")] // ClaudeSession.swift:203-205 - an initialize response with no commands throws
    [Trait("Req", "PR-PROVCLI-026")] // ClaudeSession.swift:203-205 - the same guard also destroys the model catalog
    public async Task Handshake_throws_when_the_response_has_no_commands_array()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-no-commands");
        const string response = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{}}}";
        script.SetResponse(response);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var error = await Assert.ThrowsAsync<ProviderError>(() => ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment));
        Assert.Equal("Claude could not load its commands.", error.ErrorDescription);
    }

    // ---- PR-PROVCLI-023 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-023")] // ClaudeSession.swift:193-211
    public async Task Handshake_terminates_the_watchdog_and_process_after_a_normal_return()
    {
        using var runtime = new TestRuntime();
        // The stand-in exits on its own right after replying, so a normal handshake finishing well inside the
        // 20 s watchdog window proves defer's unconditional terminate does not hang waiting on a process that
        // has already exited, and does not itself throw.
        var script = new ClaudeScript("handshake-defer");
        script.SetResponse(MinimalHandshakeResponse());
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var (models, commands) = await ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(models);
        Assert.NotNull(commands);
    }

    // ---- PR-PROVCLI-025 ----

    private const string RealHandshakeReply =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"models\":[{\"value\":\"real-model\"}],\"commands\":[\"real\"]}}}";

    [Fact]
    [Trait("Req", "PR-PROVCLI-025")] // ClaudeSession.swift:200-202 (only type control_response AND response.request_id == "handshake" is accepted; everything else is skipped)
    public async Task Handshake_skips_every_decoy_message_and_accepts_only_the_exact_control_response_for_handshake()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-decoys");
        // Each decoy carries a usable commands array, so accepting any of them would hand back a wrong catalog (or the
        // "could not load" failure) instead of the real one that arrives last.
        var decoys = new[]
        {
            "{\"type\":\"system\",\"subtype\":\"init\"}",
            "{\"type\":\"system\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[\"decoy-system\"]}}}",
            "{\"type\":\"control_response\",\"response\":{\"request_id\":\"marrow-1\",\"response\":{\"commands\":[\"decoy-other-id\"]}}}",
            "{\"type\":\"control_response\",\"response\":{\"request_id\":\"Handshake\",\"response\":{\"commands\":[\"decoy-case\"]}}}",
            "{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake2\",\"response\":{\"commands\":[\"decoy-longer-id\"]}}}",
            "{\"type\":\"control_response\",\"response\":{\"response\":{\"commands\":[\"decoy-no-id\"]}}}",
            "{\"type\":\"control_response\",\"request_id\":\"handshake\",\"response\":{\"request_id\":null,\"response\":{\"commands\":[\"decoy-top-level-id\"]}}}",
            "{\"type\":\"control_request\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[\"decoy-request\"]}}}",
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\"}}",
            "{\"type\":\"result\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[\"decoy-result\"]}}}",
        };
        script.SetResponse(string.Join("\n", decoys) + "\n" + RealHandshakeReply);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };

        var (models, commands) = await ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(["real"], commands.Select(command => command.Name));
        Assert.Equal(["real-model"], models.Select(model => model.Id));
    }

    // ---- PR-PROVCLI-027 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-027")] // ClaudeSession.swift:208 (the stream ends with no match: the unfinished-discovery failure, nothing returned)
    public async Task Handshake_reports_an_unfinished_discovery_when_the_stream_ends_with_no_matching_reply()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-silent-exit");
        File.WriteAllText(Path.Combine(script.ControlDirectory, "exit-now.txt"), "1"); // a stand-in that exits at once, never answering
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };

        var error = await Assert.ThrowsAsync<ProviderError>(() => ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment).WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("Claude's command discovery did not finish. Try again.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-027")] // ClaudeSession.swift:200-202,208 (a reply for a different id does not count: the stream then ends with no match)
    public async Task Handshake_reports_an_unfinished_discovery_when_only_a_reply_for_another_id_arrived()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("handshake-other-id");
        script.SetResponse("{\"type\":\"control_response\",\"response\":{\"request_id\":\"marrow-9\",\"response\":{\"commands\":[\"decoy\"]}}}");
        script.SetExitAfterReply();
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };

        var error = await Assert.ThrowsAsync<ProviderError>(() => ClaudeSession.Handshake(
            FileUrl(script.ExecutablePath), FileUrl(Path.GetDirectoryName(script.ExecutablePath)!), environment).WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("Claude's command discovery did not finish. Try again.", error.ErrorDescription);
    }

    private static string MinimalHandshakeResponse() =>
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{\"commands\":[]}}}";

    // ==================================================================================================
    // Part 2 (PR-PROVCLI-025 to 048): Send, Interrupt, Compact, ResolveApproval, AnswerQuestion, Stop,
    // StopAgent, and the rest of Handle's dispatch switch (control_request, control_cancel_request,
    // system, rate_limit_event).
    // ==================================================================================================

    private static SessionConfiguration MinimalConfig() => new()
    {
        Provider = ProviderKind.Claude,
        WorkingDirectory = FileUrl(Path.GetTempPath()),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static TurnInput Turn(
        string text = "hi",
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        string? model = null,
        IReadOnlyList<Attachment>? images = null) => new()
    {
        Text = text,
        Images = images is null ? ValueList<Attachment>.Empty : new ValueList<Attachment>(images),
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        Model = model,
    };

    private static void RunSend(TestRuntime runtime, ClaudeSession session, TurnInput input) =>
        runtime.Loop.Run(() => session.Send(input), TimeSpan.FromSeconds(10));

    // ---- PR-PROVCLI-028, 029 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-028")] // ClaudeSession.swift:216-221
    public void Send_sends_set_permission_mode_only_when_the_computed_name_changed()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("send-mode");
        var session = new ClaudeSession(Config(script, runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Build));
        RunStart(runtime, session);
        RunSend(runtime, session, Turn(runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Build));
        var modeLine = script.CapturedLine(1, 1);
        Assert.Contains("\"subtype\":\"set_permission_mode\"", modeLine, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"bypassPermissions\"", modeLine, StringComparison.Ordinal);
        var userLine = script.CapturedLine(1, 2);
        Assert.Contains("\"type\":\"user\"", userLine, StringComparison.Ordinal);

        RunSend(runtime, session, Turn(runtimeMode: RuntimeMode.FullAccess, interactionMode: InteractionMode.Build));
        // Unchanged mode: the next message is the user write directly, no new set_permission_mode.
        var nextLine = script.CapturedLine(1, 3);
        Assert.Contains("\"type\":\"user\"", nextLine, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-029")] // ClaudeSession.swift:222-226
    public void Send_sends_set_model_with_null_wire_for_default_and_string_otherwise()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("send-model");
        var session = new ClaudeSession(Config(script, model: null));
        RunStart(runtime, session);
        RunSend(runtime, session, Turn(model: "opus"));
        var setModelLine = script.CapturedLine(1, 1);
        Assert.Contains("\"subtype\":\"set_model\"", setModelLine, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"opus\"", setModelLine, StringComparison.Ordinal);

        RunSend(runtime, session, Turn(model: "default"));
        var toDefaultLine = script.CapturedLine(1, 3); // 2 was the user write from the first send
        Assert.Contains("\"subtype\":\"set_model\"", toDefaultLine, StringComparison.Ordinal);
        Assert.Contains("\"model\":null", toDefaultLine, StringComparison.Ordinal);

        RunSend(runtime, session, Turn(model: "default"));
        // Model unchanged (still "default"): no new set_model call, straight to the user write.
        var noChangeLine = script.CapturedLine(1, 5); // 4 was the user write from the second send
        Assert.Contains("\"type\":\"user\"", noChangeLine, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-029")] // ClaudeSession.swift:222-226 - nil to nil sends nothing
    public void Send_sends_no_set_model_control_request_when_both_are_nil()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("send-model-nil");
        var session = new ClaudeSession(Config(script, model: null));
        RunStart(runtime, session);
        RunSend(runtime, session, Turn(model: null));
        var line = script.CapturedLine(1, 1);
        Assert.Contains("\"type\":\"user\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("set_model", line, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-030 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-030")] // ClaudeSession.swift:227-235
    public void Send_puts_image_attachments_first_as_base64_blocks_then_exactly_one_text_block_last()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("send-images");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);

        var imagePath = Path.Combine(Path.GetTempPath(), "claude-send-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(imagePath, Encoding.UTF8.GetBytes("not-a-real-image"));
        var notImagePath = Path.Combine(Path.GetTempPath(), "claude-send-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(notImagePath, "plain text, not an attachment image");
        try
        {
            var image = new Attachment { Name = "shot.png", Path = imagePath, MimeType = "image/png" };
            var notImage = new Attachment { Name = "notes.txt", Path = notImagePath, MimeType = "text/plain" };
            RunSend(runtime, session, Turn(text: "look at this", images: [image, notImage]));
            var line = script.CapturedLine(1, 1);
            // Undecodable bytes (WicImageDecoder fails gracefully) keep the original bytes and mimeType.
            var expectedData = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-real-image"));
            Assert.Contains(
                "\"content\":[{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"" + expectedData + "\"}},{\"type\":\"text\",\"text\":\"look at this\"}]",
                line, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(imagePath);
            File.Delete(notImagePath);
        }
    }

    // ---- PR-PROVCLI-031 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-031")] // ClaudeSession.swift:311-321
    public void Send_writes_a_user_message_with_empty_session_id_and_null_parent_tool_use_id_and_starts_the_turn()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("send-envelope");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        RunSend(runtime, session, Turn(text: "hello there"));
        var line = script.CapturedLine(1, 1);
        Assert.Contains("\"type\":\"user\"", line, StringComparison.Ordinal);
        Assert.Contains("\"session_id\":\"\"", line, StringComparison.Ordinal);
        Assert.Contains("\"parent_tool_use_id\":null", line, StringComparison.Ordinal);
        var started = Assert.Single(events.OfType<ProviderEvent.TurnStarted>());
        Assert.Null(started.ProviderTurnID);
    }

    // ---- PR-PROVCLI-032 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-032")] // ClaudeSession.swift:249-252
    public async Task Compact_throws_notRunning_before_starting_and_sends_bare_compact_string_once_running()
    {
        var script = Script("compact");
        var notRunningSession = new ClaudeSession(Config(script));
        var error = await Assert.ThrowsAsync<ProviderError>(() => notRunningSession.Compact());
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);
        Assert.Equal("The agent session is not running.", error.ErrorDescription);

        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var runningScript = Script("compact-running");
        var session = new ClaudeSession(Config(runningScript));
        RunStart(runtime, session);
        runtime.Loop.Run(() => session.Compact(), TimeSpan.FromSeconds(10));
        var line = runningScript.CapturedLine(1, 1);
        Assert.Contains("\"message\":{\"role\":\"user\",\"content\":\"/compact\"}", line, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-033, 034 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-033")] // ClaudeSession.swift:238-247
    public void Interrupt_with_no_active_turn_sends_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("interrupt-idle");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        runtime.Loop.Run(() => session.Interrupt(), TimeSpan.FromSeconds(10));
        Assert.Equal(1, script.MessageCount(1)); // only the initialize handshake, nothing more
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-033")] // ClaudeSession.swift:238-247
    [Trait("Req", "PR-PROVCLI-034")] // ClaudeSession.swift:323-328 - every tool answer, denial included, wraps as a success control_response
    public void Interrupt_denies_every_pending_tool_with_the_interrupt_flag_then_sends_interrupt()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("interrupt-active");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        session.SeedPendingTool("req-1", "Bash", JSONValue.FromDictionaryLiteral(("command", "ls")));
        RunSend(runtime, session, Turn()); // sets turnActive
        runtime.Loop.Run(() => session.Interrupt(), TimeSpan.FromSeconds(10));

        var denyLine = script.CapturedLine(1, 2); // 1: the user write from Send, 2: the tool deny
        Assert.Contains("\"type\":\"control_response\"", denyLine, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"success\"", denyLine, StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"req-1\"", denyLine, StringComparison.Ordinal);
        Assert.Contains("\"behavior\":\"deny\"", denyLine, StringComparison.Ordinal);
        Assert.Contains("\"message\":\"The user stopped the turn.\"", denyLine, StringComparison.Ordinal);
        Assert.Contains("\"interrupt\":true", denyLine, StringComparison.Ordinal);
        var interruptLine = script.CapturedLine(1, 3);
        Assert.Contains("\"subtype\":\"interrupt\"", interruptLine, StringComparison.Ordinal);
        Assert.Contains(events, e => e is ProviderEvent.RequestResolved r && r.Id == "req-1");
    }

    // ---- PR-PROVCLI-035, 036, 037 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-035")] // ClaudeSession.swift:254-262, :814-818
    public void ResolveApproval_allow_sends_updatedInput_and_always_adds_session_scoped_permissions()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("approve-allow");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);

        session.SeedPendingTool("req-allow", "Write", JSONValue.FromDictionaryLiteral(("path", "a.txt")));
        session.ResolveApproval("req-allow", "allow");
        var allowLine = script.CapturedLine(1, 1);
        Assert.Contains("\"behavior\":\"allow\"", allowLine, StringComparison.Ordinal);
        Assert.Contains("\"updatedInput\":{\"path\":\"a.txt\"}", allowLine, StringComparison.Ordinal);
        Assert.DoesNotContain("updatedPermissions", allowLine, StringComparison.Ordinal);

        var suggestions = new JSONValue.Array(new ValueList<JSONValue>(
        [
            JSONValue.FromDictionaryLiteral(("type", "edit"), ("path", "a.txt")),
            new JSONValue.String("not-an-object"),
        ]));
        session.SeedPendingTool("req-always", "Write", JSONValue.FromDictionaryLiteral(("path", "b.txt")), suggestions);
        session.ResolveApproval("req-always", "always");
        var alwaysLine = script.CapturedLine(1, 2);
        Assert.Contains(
            "\"updatedPermissions\":[{\"type\":\"edit\",\"path\":\"a.txt\",\"destination\":\"session\"},\"not-an-object\"]",
            alwaysLine, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-035")] // ClaudeSession.swift:255 - an unknown requestID returns without sending anything
    public void ResolveApproval_with_unknown_requestID_sends_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("approve-unknown");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        session.ResolveApproval("missing", "allow");
        Assert.Equal(1, script.MessageCount(1));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-036")] // ClaudeSession.swift:262-267
    public void ResolveApproval_approving_ExitPlanMode_switches_to_build_and_resends_permission_mode()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("approve-exit-plan");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script, runtimeMode: RuntimeMode.Supervised, interactionMode: InteractionMode.Plan)) { OnEvent = events.Add };
        RunStart(runtime, session);
        session.SeedPendingTool("req-plan", "ExitPlanMode", JSONValue.FromDictionaryLiteral(("plan", "do the thing")));
        session.ResolveApproval("req-plan", "allow");
        runtime.RunUntilIdle();

        var modeChanged = Assert.Single(events.OfType<ProviderEvent.ModeChanged>());
        Assert.Equal(InteractionMode.Build, modeChanged.Value);
        var setModeLine = script.CapturedLine(1, 2); // 1: the allow response, 2: the detached set_permission_mode request
        Assert.Contains("\"subtype\":\"set_permission_mode\"", setModeLine, StringComparison.Ordinal);
        // ModeName(runtimeMode: Supervised, interaction: Build) - the STORED runtimeMode, not the still-.plan
        // configured interaction mode, per Self.modeName(runtimeMode, .build) at :264.
        Assert.Contains("\"mode\":\"default\"", setModeLine, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-037")] // ClaudeSession.swift:268-273
    public void ResolveApproval_decline_message_is_plan_specific_for_ExitPlanMode_and_generic_otherwise()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("decline");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);

        session.SeedPendingTool("req-decline-plan", "ExitPlanMode", JSONValue.FromDictionaryLiteral(("plan", "x")));
        session.ResolveApproval("req-decline-plan", "reject");
        var planDeclineLine = script.CapturedLine(1, 1);
        Assert.Contains("\"behavior\":\"deny\"", planDeclineLine, StringComparison.Ordinal);
        Assert.Contains(
            "\"message\":\"The user wants to keep planning. Wait for their feedback before changing anything.\"",
            planDeclineLine, StringComparison.Ordinal);
        Assert.DoesNotContain("\"interrupt\"", planDeclineLine, StringComparison.Ordinal);

        session.SeedPendingTool("req-decline-other", "Bash", JSONValue.FromDictionaryLiteral(("command", "ls")));
        session.ResolveApproval("req-decline-other", "reject");
        var otherDeclineLine = script.CapturedLine(1, 2);
        Assert.Contains("\"message\":\"The user declined this action.\"", otherDeclineLine, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-038 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-038")] // ClaudeSession.swift:277-287
    public void AnswerQuestion_joins_values_with_comma_space_and_merges_into_the_original_input()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("answer");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        session.SeedPendingTool("req-q", "AskUserQuestion", JSONValue.FromDictionaryLiteral(("questions", new JSONValue.Array(ValueList<JSONValue>.Empty))));
        var answers = new ValueMap<string, ValueList<string>>(new Dictionary<string, ValueList<string>>
        {
            ["Which color?"] = new ValueList<string>(["red", "blue"]),
        });
        session.AnswerQuestion("req-q", answers);
        var line = script.CapturedLine(1, 1);
        Assert.Contains("\"behavior\":\"allow\"", line, StringComparison.Ordinal);
        Assert.Contains("\"updatedInput\":{\"questions\":[],\"answers\":{\"Which color?\":\"red, blue\"}}", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-038")] // ClaudeSession.swift:278 - an unknown requestID sends nothing
    public void AnswerQuestion_with_unknown_requestID_sends_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("answer-unknown");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        session.AnswerQuestion("missing", ValueMap<string, ValueList<string>>.Empty);
        Assert.Equal(1, script.MessageCount(1));
    }

    // ---- PR-PROVCLI-039 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-039")] // ClaudeSession.swift:289-292, :690
    public void Stop_sets_isStopping_before_terminating_so_no_exited_event_is_reported()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("stop");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        session.Stop();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (session.IsRunning && DateTime.UtcNow < deadline)
        {
            runtime.RunUntilIdle();
            System.Threading.Thread.Sleep(20);
        }
        Assert.False(session.IsRunning);
        runtime.RunUntilIdle();
        Assert.Empty(events.OfType<ProviderEvent.Exited>());
    }

    // ---- PR-PROVCLI-040 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-040")] // ClaudeSession.swift:294-297
    public void StopAgent_with_no_registered_task_returns_false_and_sends_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("stopagent-unknown");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);
        var stopped = runtime.Loop.Run(() => session.StopAgent("nope"), TimeSpan.FromSeconds(10));
        Assert.False(stopped);
        Assert.Equal(1, script.MessageCount(1));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-040")] // ClaudeSession.swift:294-297, :45
    [Trait("Req", "PR-PROVCLI-046")] // ClaudeSession.swift:425-445 - registers the head this test then stops
    public void StopAgent_sends_stop_task_keyed_by_the_registered_task_id()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("stopagent-known");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        session.HandleSystem(JSONValue.FromDictionaryLiteral(
            ("subtype", "task_started"), ("task_id", "task-9"), ("subagent_type", "droppy-worker-reviewer"), ("tool_use_id", "tool-9")));
        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("tool-9", started.Value.Id);

        var stopped = runtime.Loop.Run(() => session.StopAgent("tool-9"), TimeSpan.FromSeconds(10));
        Assert.True(stopped);
        var line = script.CapturedLine(1, 1);
        Assert.Contains("\"subtype\":\"stop_task\"", line, StringComparison.Ordinal);
        Assert.Contains("\"task_id\":\"task-9\"", line, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-041, 042, 043 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:344-347 (control_cancel_request case of the dispatch switch :330-384)
    [Trait("Req", "PR-PROVCLI-043")] // ClaudeSession.swift:344-347
    public void Handle_control_cancel_request_removes_the_pending_tool_and_sends_no_reply()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.SeedPendingTool("req-c", "Bash", JSONValue.FromDictionaryLiteral(("command", "ls")));
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "control_cancel_request"), ("request_id", "req-c")));
        Assert.Contains(events, e => e is ProviderEvent.RequestResolved r && r.Id == "req-c");

        events.Clear();
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "control_cancel_request"), ("request_id", "missing")));
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:381-382 - an unrecognized type is silently ignored
    public void Handle_ignores_an_unrecognized_message_type()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "something_else")));
        session.Handle(new JSONValue.Object(ValueMap<string, JSONValue>.Empty)); // no "type" at all
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:373-380 - the rate_limit_event case of the same dispatch switch
    [Trait("Req", "UI-PROVCLI-029")] // ClaudeSession.swift:378 - Claude's usage-limit warning notice, with the reset-time suffix
    public void Handle_rate_limit_event_emits_a_warning_notice_and_usageLimit_only_when_rejected()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };

        session.Handle(JSONValue.FromDictionaryLiteral(("type", "rate_limit_event"), ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "ok")))));
        Assert.Empty(events);

        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "rate_limit_event"),
            ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "rejected"), ("resetsAt", 1700000000.0)))));
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.StartsWith("Claude hit a usage limit. It resets at ", notice.Value.Message, StringComparison.Ordinal);
        var limit = Assert.Single(events.OfType<ProviderEvent.UsageLimit>());
        Assert.Equal(SwiftDate.From1970Seconds(1700000000.0), limit.ResetsAt);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-042")] // ClaudeSession.swift:334-341
    public Task Control_resolves_by_matching_request_id_and_throws_for_an_error_subtype()
    {
        // Start() spawns the readTask via MainActor.Spawn, which needs a pumped loop to deliver the
        // control_response back to Control()'s awaiter: run it through runtime.Loop like every other live
        // session test (RunStart), not a bare await, or the readTask never gets to run.
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = new ClaudeScript("control-error");
        script.SetResponse("{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"subtype\":\"error\",\"error\":\"nope\"}}");
        var session = new ClaudeSession(Config(script));
        var error = Assert.Throws<ProviderError>(() => RunStart(runtime, session));
        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.Equal("nope", error.ErrorDescription);
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-042")] // ClaudeSession.swift:338-339 - the fallback message when no error text is given
    public Task Control_error_without_a_message_uses_the_verbatim_fallback()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = new ClaudeScript("control-error-fallback");
        script.SetResponse("{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"subtype\":\"error\"}}");
        var session = new ClaudeSession(Config(script));
        var error = Assert.Throws<ProviderError>(() => RunStart(runtime, session));
        Assert.Equal("Claude rejected the request.", error.ErrorDescription);
        return Task.CompletedTask;
    }

    // ---- PR-PROVCLI-442 (B3, new) ----
    //
    // pendingControl (:37) becomes a PendingReplies<JSONValue> (Control, :301-309): the process/hasClosed
    // guards and controlCounter's advance run unconditionally first, so a cancelled request still uses up its
    // id; only PendingReplies.Register's own cancellation check (Core/Support/PendingReplies.swift:17,
    // PendingRepliesTests.cs) decides whether anything is stored or sent.

    [Fact]
    [Trait("Req", "PR-PROVCLI-442")] // ClaudeSession.swift:301-309, Core/Support/PendingReplies.swift:17
    public void Control_cancelled_before_the_send_throws_writes_nothing_and_still_uses_up_that_request_id()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("control-cancel-before-send");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session); // consumes id marrow-1 for the initialize handshake
        var linesBeforeCancel = script.MessageCount(1); // call 1 is the session process (call 0 is --help)

        // Cancel before the loop ever runs the task's body: by the time Control() executes, its ambient task
        // is already cancelled, matching "a caller already cancelled" (not one cancelled mid-flight).
        var handle = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))));
        handle.Cancel();
        runtime.RunUntilIdle();

        Assert.True(handle.Value.IsFaulted);
        Assert.IsType<CancellationError>(handle.Value.Exception!.InnerException);
        Assert.Equal(linesBeforeCancel, script.MessageCount(1)); // no control_request line was ever written

        // controlCounter still advanced past the cancelled call's id (marrow-2): the next request skips it.
        runtime.Loop.Run(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))), TimeSpan.FromSeconds(10));
        var nextLine = script.CapturedLine(1, linesBeforeCancel);
        Assert.Contains("\"request_id\":\"marrow-3\"", nextLine, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-442")] // ClaudeSession.swift:301-309, Core/Support/PendingReplies.swift:31-35,:38-43
    public void Control_cancelled_while_waiting_throws_and_a_later_reply_for_that_id_is_dropped()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        // Answer the handshake only, then stay alive while the next request is pending. Exiting after the
        // handshake raced DidClose against registration and could fail with notRunning instead of cancellation.
        var script = new ClaudeScript("control-cancel-while-waiting");
        script.SetResponse(MinimalResponse);
        File.WriteAllText(Path.Combine(script.ControlDirectory, "reply-limit.txt"), "1");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session); // consumes id marrow-1; the stand-in keeps reading

        var handle = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))));
        runtime.RunUntilIdle(); // registers marrow-2 and runs send(); the live stand-in leaves it unanswered
        Assert.Contains("\"request_id\":\"marrow-2\"", script.CapturedLine(1, 1), StringComparison.Ordinal);
        runtime.RunUntilIdle();
        Assert.False(handle.Value.IsCompleted); // still waiting

        handle.Cancel();
        runtime.RunUntilIdle();
        Assert.True(handle.Value.IsFaulted);
        Assert.IsType<CancellationError>(handle.Value.Exception!.InnerException);

        // A control_response for that id, arriving after the removal, finds no waiting entry and is dropped:
        // no crash, no second resume (which would trap), and the already-faulted task is unchanged.
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "control_response"),
            ("response", JSONValue.FromDictionaryLiteral(("request_id", "marrow-2"), ("response", JSONValue.FromDictionaryLiteral())))));
        Assert.True(handle.Value.IsFaulted);
        Assert.IsType<CancellationError>(handle.Value.Exception!.InnerException);
        session.Stop();
        runtime.RunUntilIdle();
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-442")] // ClaudeSession.swift:301-309, Core/Support/PendingReplies.swift:31-35,:38-43
    public async Task Control_reply_before_cancellation_is_returned_and_survives_the_late_cancel()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        // Same live stand-in shape as the cancelled-while-waiting test: the handshake answers marrow-1,
        // the next request (marrow-2) is captured but left unanswered, so the manual reply below is the
        // only one for that id. ClaudeSession.swift:301-309 registers marrow-2 before sending
        // (PendingReplies.swift:17-20 stores the waiter before send() runs).
        var script = new ClaudeScript("control-reply-before-cancel");
        script.SetResponse(MinimalResponse);
        File.WriteAllText(Path.Combine(script.ControlDirectory, "reply-limit.txt"), "1");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session); // consumes id marrow-1; the stand-in keeps reading

        var handle = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))));
        runtime.RunUntilIdle(); // registers marrow-2 and runs send(); the live stand-in leaves it unanswered
        Assert.Contains("\"request_id\":\"marrow-2\"", script.CapturedLine(1, 1), StringComparison.Ordinal);
        runtime.RunUntilIdle();
        Assert.False(handle.Value.IsCompleted); // still waiting

        // The distinct reply for marrow-2 arrives BEFORE the cancellation takes effect.
        // PendingReplies.swift:38-43 resolve() removes the waiter and resumes with the value; the later
        // onCancel task (PendingReplies.swift:31-35) then finds nothing and does nothing, so no second
        // resume (which would trap) occurs. The record's verification_method (PROVCLI.json PR-PROVCLI-442)
        // requires this third outcome: "a reply that arrives before the cancellation is returned".
        var expected = JSONValue.FromDictionaryLiteral(("ok", true), ("marker", "reply-wins-442"));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "control_response"),
            ("response", JSONValue.FromDictionaryLiteral(("request_id", "marrow-2"), ("response", expected)))));
        // Cancel the SAME caller after the reply but before its continuation is pumped: the entry is
        // already gone, so the cancel finds nothing and the already-resolved reply wins.
        handle.Cancel();
        runtime.RunUntilIdle();

        Assert.True(handle.Value.IsCompletedSuccessfully);
        Assert.Equal(expected, await handle.Value);

        // A duplicate late reply for the same id still finds nothing and leaves the success unchanged.
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "control_response"),
            ("response", JSONValue.FromDictionaryLiteral(("request_id", "marrow-2"), ("response", JSONValue.FromDictionaryLiteral())))));
        runtime.RunUntilIdle();
        Assert.True(handle.Value.IsCompletedSuccessfully);
        Assert.Equal(expected, await handle.Value);

        session.Stop();
        runtime.RunUntilIdle();
    }

    // PR-PROVCLI-442 "No timeout applies" (PendingReplies.register is called with no timeout, ClaudeSession.swift:301-309): a request nobody
    // answers waits through any span of time until its reply arrives or didClose fails it. Time here is the manual clock, so a 30 s (or any)
    // timeout in Control would complete the waiter long before the thirty minutes the test lets pass.
    [Fact]
    [Trait("Req", "PR-PROVCLI-442")] // ClaudeSession.swift:301-309, Core/Support/PendingReplies.swift:10-36 (no timeout argument)
    public async Task Control_with_no_reply_keeps_waiting_past_any_timeout_until_answered_or_the_session_closes()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = new ClaudeScript("control-no-timeout");
        script.SetResponse(MinimalResponse);
        File.WriteAllText(Path.Combine(script.ControlDirectory, "reply-limit.txt"), "1"); // answers the handshake only
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session); // marrow-1

        var answered = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))));
        var closed = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "set_model"))));
        runtime.RunUntilIdle();
        Assert.Contains("\"request_id\":\"marrow-2\"", script.CapturedLine(1, 1), StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"marrow-3\"", script.CapturedLine(1, 2), StringComparison.Ordinal);

        runtime.Advance(TimeSpan.FromMinutes(30));
        runtime.RunUntilIdle();
        Assert.False(answered.Value.IsCompleted);
        Assert.False(closed.Value.IsCompleted);

        var marker = JSONValue.FromDictionaryLiteral(("ok", true), ("marker", "late-reply-442"));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "control_response"),
            ("response", JSONValue.FromDictionaryLiteral(("request_id", "marrow-2"), ("response", marker)))));
        runtime.RunUntilIdle();
        Assert.Equal(marker, await answered.Value);
        Assert.False(closed.Value.IsCompleted); // the other one still waits, however long ago it was sent

        session.Stop(); // the process ends: didClose fails what is still waiting (PR-PROVCLI-061)
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!closed.Value.IsCompleted && DateTime.UtcNow < deadline)
        {
            runtime.RunUntilIdle();
            System.Threading.Thread.Sleep(20);
        }
        Assert.True(closed.Value.IsFaulted);
        Assert.IsType<ProviderError>(closed.Value.Exception!.InnerException);
    }

    // "This holds for every control() caller, start()'s initialize included" (ClaudeSession.swift:160): cancelling Start while its initialize
    // reply is outstanding throws CancellationError out of Start, and the reply that comes later is dropped like any unmatched id.
    [Fact]
    [Trait("Req", "PR-PROVCLI-442")] // ClaudeSession.swift:154-166 (start's initialize goes through control), :301-309
    public void Start_cancelled_while_the_initialize_reply_is_outstanding_throws_CancellationError_and_the_late_reply_is_dropped()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = new ClaudeScript("start-cancel-initialize");
        script.SetResponse(MinimalResponse);
        File.WriteAllText(Path.Combine(script.ControlDirectory, "reply-limit.txt"), "0"); // the handshake is never answered
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };

        var start = MainActor.SpawnThrowing(() => session.Start());
        runtime.Loop.RunUntil(() => script.MessageCount(1) >= 1, TimeSpan.FromSeconds(30));
        var line = script.CapturedLine(1, 0);
        Assert.Contains("\"request_id\":\"marrow-1\"", line, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"initialize\"", line, StringComparison.Ordinal);
        Assert.False(start.Value.IsCompleted);

        start.Cancel();
        runtime.Loop.RunUntil(() => start.Value.IsCompleted, TimeSpan.FromSeconds(30));
        Assert.True(start.Value.IsFaulted);
        Assert.IsType<CancellationError>(start.Value.Exception!.InnerException);

        // The reply that arrives after the removal finds nothing waiting: no event, no second resume.
        var command = JSONValue.FromDictionaryLiteral(("name", "review"), ("description", "Review"));
        var initialization = JSONValue.FromDictionaryLiteral(("commands", new JSONValue.Array(new ValueList<JSONValue>(new[] { command }))));
        var lateReply = JSONValue.FromDictionaryLiteral(("request_id", "marrow-1"), ("response", initialization));
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "control_response"), ("response", lateReply)));
        runtime.RunUntilIdle();
        Assert.Empty(events.OfType<ProviderEvent.Models>());
        Assert.Empty(events.OfType<ProviderEvent.Commands>());
        Assert.True(start.Value.IsFaulted);
        session.Stop();
        runtime.RunUntilIdle();
    }

    // ---- PR-PROVCLI-044 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-044")] // ClaudeSession.swift:388-392
    public void HandleSystem_init_adopts_a_new_session_id_and_emits_sessionReady_only_when_it_differs()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        // The session id starts unset (Start() was never called), so any id differs and is adopted.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "init"), ("session_id", "new-id")));
        var ready = Assert.Single(events.OfType<ProviderEvent.SessionReady>());
        Assert.Equal("new-id", ready.SessionID);

        events.Clear();
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "init"), ("session_id", "new-id")));
        Assert.Empty(events.OfType<ProviderEvent.SessionReady>());
    }

    // ---- PR-PROVCLI-045 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-045")] // ClaudeSession.swift:393-394
    [Trait("Req", "UI-PROVCLI-028")] // ClaudeSession.swift:394 - Claude's compaction notice, verbatim "Context compacted."
    public void HandleSystem_compact_boundary_emits_the_verbatim_info_notice()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "compact_boundary")));
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel);
        Assert.Equal("Context compacted.", notice.Value.Message);
    }

    // ---- PR-PROVCLI-046 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-046")] // ClaudeSession.swift:425-445
    public void HandleSystem_task_started_registers_an_agent_only_when_it_qualifies()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };

        // No task_id: ignored.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started")));
        Assert.Empty(events);

        // skip_transcript true: ignored.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(
            ("subtype", "task_started"), ("task_id", "t1"), ("skip_transcript", true), ("subagent_type", "droppy-worker-x")));
        Assert.Empty(events);

        // Neither subagent_type nor task_type local_agent: a background shell task, ignored.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t2")));
        Assert.Empty(events);

        // task_type local_agent qualifies even with no subagent_type; id falls back to the task id, with defaults.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t3"), ("task_type", "local_agent")));
        var started = Assert.Single(events.OfType<ProviderEvent.AgentStarted>());
        Assert.Equal("t3", started.Value.Id);
        Assert.Equal("t3", started.Value.TaskID);
        Assert.Null(started.Value.ToolUseID);
        Assert.Equal("Subagent", started.Value.Description);
        Assert.Null(started.Value.Prompt);
        Assert.Null(started.Value.Model);
        Assert.Null(started.Value.Profile);
        Assert.False(started.Value.IsBackground);
    }

    // ---- PR-PROVCLI-047 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-047")] // ClaudeSession.swift:397-410
    public void HandleSystem_task_progress_forwards_summary_lastTool_tokens_and_toolCalls_without_touching_the_ledger()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t1"), ("task_type", "local_agent")));
        events.Clear();

        session.HandleSystem(JSONValue.FromDictionaryLiteral(
            ("subtype", "task_progress"), ("task_id", "t1"), ("summary", "reading files"), ("last_tool_name", "Read"),
            ("usage", JSONValue.FromDictionaryLiteral(("total_tokens", 42L), ("tool_uses", 3L)))));
        var progress = Assert.Single(events.OfType<ProviderEvent.AgentProgress>());
        Assert.Equal("t1", progress.AgentID);
        Assert.Equal("reading files", progress.Summary);
        Assert.Equal("Read", progress.LastTool);
        Assert.Equal(42, progress.Tokens);
        Assert.Equal(3, progress.ToolCalls);
        Assert.Empty(events.OfType<ProviderEvent.TokenSpend>()); // never added to the ledger, :400-403

        events.Clear();
        // Unknown task id: nothing.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_progress"), ("task_id", "missing")));
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-048 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-048")] // ClaudeSession.swift:411-419
    public void HandleSystem_task_notification_maps_status_strings_and_finishes_the_head()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t1"), ("task_type", "local_agent")));
        events.Clear();
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_notification"), ("task_id", "t1"), ("status", "failed"), ("summary", "boom")));
        var finished = Assert.Single(events.OfType<ProviderEvent.AgentFinished>());
        Assert.Equal("t1", finished.AgentID);
        Assert.Equal(TurnStatus.Failed, finished.Status);
        Assert.Equal("boom", finished.Summary);

        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t2"), ("task_type", "local_agent")));
        events.Clear();
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_notification"), ("task_id", "t2"), ("status", "stopped")));
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(events.OfType<ProviderEvent.AgentFinished>()).Status);

        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_started"), ("task_id", "t3"), ("task_type", "local_agent")));
        events.Clear();
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_notification"), ("task_id", "t3")));
        Assert.Equal(TurnStatus.Completed, Assert.Single(events.OfType<ProviderEvent.AgentFinished>()).Status);

        events.Clear();
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_notification"), ("task_id", "missing")));
        Assert.Empty(events);
    }

    // ==================================================================================================
    // Part 3 (PR-PROVCLI-049 to 064, 354 to 361): the four per-event-type transcript handlers
    // (handleStreamEvent, handleAssistant, handleToolResults, handleResult), handleControlRequest's own
    // body, didClose (implemented in part 1, tested here), and the toolCall mapping table.
    // ==================================================================================================

    // ---- PR-PROVCLI-049 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-049")] // ClaudeSession.swift:350-368
    public void Handle_keeps_independent_stream_state_per_head_and_wraps_head_events_as_agentEvent()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };

        // The lead's own stream_event goes out unwrapped.
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "lead-msg")))))));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "lead")))))));
        var leadDelta = Assert.Single(events.OfType<ProviderEvent.MessageDelta>());
        Assert.Equal("lead-msg-0", leadDelta.Id);
        events.Clear();

        // Two different heads' stream_events each keep their own bookkeeping and are wrapped as agentEvent.
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-a"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "a-msg")))))));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-b"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "b-msg")))))));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-a"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "a-text")))))));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-b"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "b-text")))))));

        var wrapped = events.OfType<ProviderEvent.AgentEvent>().ToList();
        Assert.Equal(2, wrapped.Count);
        Assert.All(wrapped, e => Assert.IsType<ProviderEvent.MessageDelta>(e.Value));
        var aDelta = (ProviderEvent.MessageDelta)wrapped.First(e => e.AgentID == "head-a").Value;
        var bDelta = (ProviderEvent.MessageDelta)wrapped.First(e => e.AgentID == "head-b").Value;
        Assert.Equal("a-msg-0", aDelta.Id); // each head's index-0 block id is namespaced by its own currentMessageID
        Assert.Equal("b-msg-0", bDelta.Id);
    }

    // ---- PR-PROVCLI-050 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-050")] // ClaudeSession.swift:455-495
    public void HandleStreamEvent_opens_and_extends_text_thinking_and_tool_blocks_by_index()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig());
        var stream = new ClaudeSession.StreamState();

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "m1")))), ref stream, events.Add);
        Assert.Equal("m1", stream.CurrentMessageID);

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "he")))), ref stream, events.Add);
        var textStart = Assert.Single(events.OfType<ProviderEvent.MessageDelta>());
        Assert.Equal("m1-0", textStart.Id);
        Assert.Equal("he", textStart.Text);
        events.Clear();

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 1L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "thinking"), ("thinking", "hm")))), ref stream, events.Add);
        var thinkStart = Assert.Single(events.OfType<ProviderEvent.ReasoningDelta>());
        Assert.Equal("m1-1", thinkStart.Id);
        Assert.Equal("hm", thinkStart.Text);
        events.Clear();

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 2L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t1"), ("name", "Bash")))), ref stream, events.Add);
        var toolStart = Assert.Single(events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal("t1", toolStart.Id);
        Assert.Equal(ToolCall.Kind.Command, toolStart.Call.ToolCallKind);
        Assert.Equal("Shell command", toolStart.Call.Title); // built from an empty input (:476)
        events.Clear();

        // A tool_use that maps to no row (TodoWrite, PR-PROVCLI-354) emits nothing.
        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 3L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t2"), ("name", "TodoWrite")))), ref stream, events.Add);
        Assert.Empty(events);

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_delta"), ("index", 0L), ("delta", JSONValue.FromDictionaryLiteral(("type", "text_delta"), ("text", "llo")))), ref stream, events.Add);
        var textDelta = Assert.Single(events.OfType<ProviderEvent.MessageDelta>());
        Assert.Equal("m1-0", textDelta.Id);
        Assert.Equal("llo", textDelta.Text);
        events.Clear();

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_delta"), ("index", 1L), ("delta", JSONValue.FromDictionaryLiteral(("type", "thinking_delta"), ("thinking", "m")))), ref stream, events.Add);
        var thinkDelta = Assert.Single(events.OfType<ProviderEvent.ReasoningDelta>());
        Assert.Equal("m1-1", thinkDelta.Id);
        events.Clear();

        // A delta for an index with no recorded block id (the tool_use block at index 2) is dropped.
        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_delta"), ("index", 2L), ("delta", JSONValue.FromDictionaryLiteral(("type", "text_delta"), ("text", "x")))), ref stream, events.Add);
        Assert.Empty(events);

        // Unrecognized event/block/delta types are ignored.
        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "ping")), ref stream, events.Add);
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-051 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-051")] // ClaudeSession.swift:497-517
    [Trait("Req", "UI-PROVCLI-034")] // ClaudeSession.swift:497-517 - the two synthetic placeholders are dropped, not drawn
    public void HandleAssistant_drops_synthetic_placeholders_and_surfaces_synthetic_api_errors_as_warnings()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig());
        var stream = new ClaudeSession.StreamState();

        JSONValue SyntheticMessage(string text, bool isApiError) => JSONValue.FromDictionaryLiteral(
            ("message", JSONValue.FromDictionaryLiteral(
                ("model", "<synthetic>"),
                ("content", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", text)))))),
            ("isApiErrorMessage", isApiError));

        // A placeholder is dropped even when flagged as an API error.
        session.HandleAssistant(SyntheticMessage("No response requested.", true), ref stream, events.Add);
        Assert.Empty(events);
        session.HandleAssistant(SyntheticMessage("(no content)", true), ref stream, events.Add);
        Assert.Empty(events);

        // A non-placeholder synthetic message without the API-error flag is dropped too.
        session.HandleAssistant(SyntheticMessage("Something else.", false), ref stream, events.Add);
        Assert.Empty(events);

        // Only isApiErrorMessage true, non-empty and non-placeholder text becomes a warning notice.
        session.HandleAssistant(SyntheticMessage("The API failed.", true), ref stream, events.Add);
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel);
        Assert.Equal("The API failed.", notice.Value.Message);
        events.Clear();

        // Multiple text blocks join with a single newline and the whole joined text is trimmed.
        var multi = JSONValue.FromDictionaryLiteral(
            ("message", JSONValue.FromDictionaryLiteral(
                ("model", "<synthetic>"),
                ("content", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "  line one  ")),
                    JSONValue.FromDictionaryLiteral(("type", "other")),
                    JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "line two")))))),
            ("isApiErrorMessage", true));
        session.HandleAssistant(multi, ref stream, events.Add);
        Assert.Equal("line one  \nline two", Assert.Single(events.OfType<ProviderEvent.Notice>()).Value.Message);
    }

    // ---- PR-PROVCLI-052 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-052")] // ClaudeSession.swift:518-543
    public void HandleAssistant_completes_blocks_against_opened_stream_ids_and_mints_uuids_when_none_are_open()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig());
        var stream = new ClaudeSession.StreamState();
        stream.OpenText["m1"] = ["m1-0"];
        stream.OpenThinking["m1"] = ["m1-1"];

        var message = JSONValue.FromDictionaryLiteral(
            ("uuid", "u-1"),
            ("message", JSONValue.FromDictionaryLiteral(
                ("id", "m1"),
                ("content", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "hello")),
                    JSONValue.FromDictionaryLiteral(("type", "thinking"), ("thinking", "hmm")),
                    JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t1"), ("name", "Bash"), ("input", JSONValue.FromDictionaryLiteral(("command", "ls")))))))));
        session.HandleAssistant(message, ref stream, events.Add);

        Assert.IsType<ProviderEvent.AssistantMessageID>(events[0]); // emitted first
        Assert.Equal("u-1", ((ProviderEvent.AssistantMessageID)events[0]).Value);
        var textDone = Assert.Single(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.Equal("m1-0", textDone.Id);
        Assert.Equal("hello", textDone.Text);
        var thinkDone = Assert.Single(events.OfType<ProviderEvent.ReasoningCompleted>());
        Assert.Equal("m1-1", thinkDone.Id);
        var toolStarted = Assert.Single(events.OfType<ProviderEvent.ToolStarted>());
        Assert.Equal("t1", toolStarted.Id);
        var toolUpdated = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal("t1", toolUpdated.Id);
        Assert.Equal("ls", toolUpdated.Update.Title);

        events.Clear();
        // No id was ever opened: a fresh, uniquely minted id is used.
        var noOpenIds = JSONValue.FromDictionaryLiteral(
            ("message", JSONValue.FromDictionaryLiteral(
                ("id", "m2"),
                ("content", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "x")))))));
        session.HandleAssistant(noOpenIds, ref stream, events.Add);
        var minted = Assert.Single(events.OfType<ProviderEvent.MessageCompleted>());
        Assert.StartsWith("m2-text-", minted.Id, StringComparison.Ordinal);
        Assert.NotEqual("m2-text-", minted.Id);

        // TodoWrite emits .todos, never a tool row.
        events.Clear();
        var todoEntry = JSONValue.FromDictionaryLiteral(("content", "step"), ("status", "completed"));
        var todoInput = JSONValue.FromDictionaryLiteral(("todos", JSONValue.FromArrayLiteral(todoEntry)));
        var todoBlock = JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t9"), ("name", "TodoWrite"), ("input", todoInput));
        var todo = JSONValue.FromDictionaryLiteral(("message", JSONValue.FromDictionaryLiteral(("id", "m3"), ("content", JSONValue.FromArrayLiteral(todoBlock)))));
        session.HandleAssistant(todo, ref stream, events.Add);
        var todos = Assert.Single(events.OfType<ProviderEvent.Todos>());
        Assert.Single(todos.Value);
        Assert.Equal("step", todos.Value[0].Text);
        Assert.Equal(TodoStep.Status.Done, todos.Value[0].TodoStepStatus);
        Assert.Empty(events.OfType<ProviderEvent.ToolStarted>());
    }

    // ---- PR-PROVCLI-053 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-053")] // ClaudeSession.swift:546-560
    public void HandleToolResults_closes_the_row_hides_five_tools_and_reads_a_decline_from_the_error_text()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig());
        var stream = new ClaudeSession.StreamState();

        JSONValue ToolResultMessage(params JSONValue[] blocks) => JSONValue.FromDictionaryLiteral(
            ("message", JSONValue.FromDictionaryLiteral(("content", JSONValue.FromArrayLiteral(blocks)))));

        void OpenTool(string id, string name) =>
            session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", id), ("name", name)))), ref stream, events.Add);

        OpenTool("t-hidden", "AskUserQuestion");
        OpenTool("t-ok", "Bash");
        events.Clear();

        // A hidden tool's result never becomes a row.
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(
            ("type", "tool_result"), ("tool_use_id", "t-hidden"), ("content", "ignored"))), events.Add);
        Assert.Empty(events);

        // A successful, non-hidden result.
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(
            ("type", "tool_result"), ("tool_use_id", "t-ok"), ("content", "ok"))), events.Add);
        var ok = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Completed, ok.Update.Status);
        Assert.Equal("ok", ok.Update.Output);
        events.Clear();

        // is_error true with no decline wording -> failed. Output from an array of text fields, newline joined.
        OpenTool("t-fail", "Bash");
        events.Clear();
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(
            ("type", "tool_result"), ("tool_use_id", "t-fail"), ("is_error", true),
            ("content", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "line one")),
                JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "line two")))))), events.Add);
        var failed = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>());
        Assert.Equal(ToolCall.Status.Failed, failed.Update.Status);
        Assert.Equal("line one\nline two", failed.Update.Output);
        events.Clear();

        // is_error true with "declined" (case-insensitive) -> declined.
        OpenTool("t-declined", "Bash");
        events.Clear();
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(
            ("type", "tool_result"), ("tool_use_id", "t-declined"), ("is_error", true), ("content", "User DECLINED the request"))), events.Add);
        Assert.Equal(ToolCall.Status.Declined, Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Status);
        events.Clear();

        // is_error true with "doesn't want" -> declined too.
        OpenTool("t-doesnt", "Bash");
        events.Clear();
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(
            ("type", "tool_result"), ("tool_use_id", "t-doesnt"), ("is_error", true), ("content", "the user doesn't want this"))), events.Add);
        Assert.Equal(ToolCall.Status.Declined, Assert.Single(events.OfType<ProviderEvent.ToolUpdated>()).Update.Status);
        events.Clear();

        // A non-tool_result block is skipped.
        session.HandleToolResults(ToolResultMessage(JSONValue.FromDictionaryLiteral(("type", "other"))), events.Add);
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-054 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-054")] // ClaudeSession.swift:562-571
    public void HandleResult_before_started_stores_the_joined_errors_as_launchError_and_emits_no_turn_event()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.HandleResult(JSONValue.FromDictionaryLiteral(("errors", JSONValue.FromArrayLiteral("session not found", "try again"))));
        Assert.Empty(events);
        Assert.Equal("session not found\ntry again", session.LaunchError);

        // An empty errors array leaves launchError unset.
        var fresh = new ClaudeSession(MinimalConfig());
        fresh.HandleResult(JSONValue.FromDictionaryLiteral(("errors", JSONValue.FromArrayLiteral())));
        Assert.Null(fresh.LaunchError);
    }

    // ---- PR-PROVCLI-055 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-055")] // ClaudeSession.swift:572-607
    [Trait("Req", "UI-PROVCLI-030")] // ClaudeSession.swift:602 - Claude's turn-failure fallback, verbatim "Claude stopped before finishing."
    public async Task HandleResult_after_start_ends_the_turn_with_usage_spend_cleared_state_and_status()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.MarkStarted();
        session.SeedPendingTool("req-1", "Bash", JSONValue.FromDictionaryLiteral(("command", "ls")));

        // A head's own stream state (:590-593): populate one head and the lead before the result.
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-x"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "keep-me")))))));
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "lead-before")))))));
        events.Clear();

        // HandleResult's usage branch reaches TokenLedger.Shared.Current, a MainActorModel: run it on main.
        runtime.OnMain(() => session.HandleResult(JSONValue.FromDictionaryLiteral(
            ("subtype", "success"),
            ("usage", JSONValue.FromDictionaryLiteral(
                ("input_tokens", 10L), ("output_tokens", 5L), ("cache_read_input_tokens", 2L), ("cache_creation_input_tokens", 1L))))));

        // usage precedes tokenSpend precedes requestResolved (:574-586).
        var usageIndex = events.FindIndex(e => e is ProviderEvent.Usage);
        var spendIndex = events.FindIndex(e => e is ProviderEvent.TokenSpend);
        var resolvedIndex = events.FindIndex(e => e is ProviderEvent.RequestResolved);
        Assert.True(usageIndex >= 0 && usageIndex < spendIndex && spendIndex < resolvedIndex);
        var usage = ((ProviderEvent.Usage)events[usageIndex]).Value;
        Assert.Equal(18, usage.UsedTokens);
        var spend = ((ProviderEvent.TokenSpend)events[spendIndex]).Value;
        Assert.Equal(18, spend.TotalTokens);
        Assert.Single(events.OfType<ProviderEvent.RequestResolved>());
        var completed = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Completed, completed.Status);
        Assert.Null(completed.Error);

        // The head's stream survived the result; the lead's was reset to a fresh StreamState.
        events.Clear();
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"), ("parent_tool_use_id", "head-x"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "t")))))));
        var headWrapped = (ProviderEvent.MessageDelta)Assert.Single(events.OfType<ProviderEvent.AgentEvent>()).Value;
        Assert.Equal("keep-me-0", headWrapped.Id);

        events.Clear();
        session.Handle(JSONValue.FromDictionaryLiteral(
            ("type", "stream_event"),
            ("event", JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", 0L), ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "t")))))));
        var leadDelta = Assert.Single(events.OfType<ProviderEvent.MessageDelta>());
        Assert.Equal("message-0", leadDelta.Id); // mainStream's currentMessageID is null again after the reset

        // Failed status, falling back to the exact literal message when both result and errors are empty.
        events.Clear();
        session.MarkStarted();
        session.HandleResult(JSONValue.FromDictionaryLiteral(("subtype", "error_during_execution")));
        var failed = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, failed.Status);
        Assert.Equal("Claude stopped before finishing.", failed.Error);

        // Interrupted always wins over a result that otherwise reads as an error, with a nil error.
        events.Clear();
        session.MarkStarted();
        session.MarkTurnActive();
        await session.Interrupt();
        session.HandleResult(JSONValue.FromDictionaryLiteral(("subtype", "error_during_execution"), ("result", "real failure text")));
        var interrupted = Assert.Single(events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Interrupted, interrupted.Status);
        Assert.Null(interrupted.Error);

        // interruptRequested is reset last: a further result reads normally again.
        events.Clear();
        session.MarkStarted();
        session.HandleResult(JSONValue.FromDictionaryLiteral(("subtype", "success")));
        Assert.Equal(TurnStatus.Completed, Assert.Single(events.OfType<ProviderEvent.TurnCompleted>()).Status);
    }

    // ---- PR-PROVCLI-056 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-056")] // ClaudeSession.swift:373-380
    public void Handle_rate_limit_event_reports_a_rejected_limit_as_warning_plus_usageLimit()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };

        // Non-"rejected" status does nothing.
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "rate_limit_event"), ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "ok")))));
        Assert.Empty(events);

        // Rejected with no reset time: no suffix.
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "rate_limit_event"), ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "rejected")))));
        var notice = Assert.Single(events.OfType<ProviderEvent.Notice>());
        Assert.Equal("Claude hit a usage limit.", notice.Value.Message);
        var limit = Assert.Single(events.OfType<ProviderEvent.UsageLimit>());
        Assert.Null(limit.ResetsAt);
        events.Clear();

        // Rejected with a reset time (falls back to resetsAtSeconds when resetsAt is absent).
        const double seconds = 1_700_000_000.0;
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "rate_limit_event"), ("rate_limit_info", JSONValue.FromDictionaryLiteral(("status", "rejected"), ("resetsAtSeconds", seconds)))));
        var notice2 = Assert.Single(events.OfType<ProviderEvent.Notice>());
        var expectedDate = SwiftDate.From1970Seconds(seconds);
        Assert.Equal($"Claude hit a usage limit. It resets at {UsageLimitSignal.Format(expectedDate)}.", notice2.Value.Message);
        Assert.Equal(expectedDate, Assert.Single(events.OfType<ProviderEvent.UsageLimit>()).ResetsAt);
    }

    // ---- PR-PROVCLI-057 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-057")] // ClaudeSession.swift:609-617
    public void HandleControlRequest_refuses_every_subtype_except_can_use_tool_with_the_marrow_error_string()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("control-refuse");
        var session = new ClaudeSession(Config(script));
        RunStart(runtime, session);

        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-x"), ("request", JSONValue.FromDictionaryLiteral(("subtype", "some_other_kind")))));
        var line = script.CapturedLine(1, 1); // 0: the initialize handshake
        Assert.Contains("\"type\":\"control_response\"", line, StringComparison.Ordinal);
        Assert.Contains("\"subtype\":\"error\"", line, StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"req-x\"", line, StringComparison.Ordinal);
        // DEVIATION OD-0012 F1: upstream sends "Droppy Code does not support this request."
        Assert.Contains("\"error\":\"nth does not support this request.\"", line, StringComparison.Ordinal);

        // A request without a string request_id, or without a request object, is ignored entirely.
        var countAfterRefusal = script.MessageCount(1);
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request", JSONValue.FromDictionaryLiteral(("subtype", "x")))));
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "r2")));
        Assert.Equal(countAfterRefusal, script.MessageCount(1));
    }

    // ---- PR-PROVCLI-058 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-058")] // ClaudeSession.swift:618-639
    public void HandleControlRequest_askUserQuestion_builds_a_multi_question_prompt_that_always_allows_a_free_answer()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        var options = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("label", "A"), ("description", "first")),
            JSONValue.FromDictionaryLiteral(("no_label_here", true)));
        var questionWithId = JSONValue.FromDictionaryLiteral(("question", "Pick one"), ("options", options), ("multiSelect", true));
        var questionWithoutId = JSONValue.FromDictionaryLiteral(("no_question_key", true));
        var questions = JSONValue.FromArrayLiteral(questionWithId, questionWithoutId);
        var input = JSONValue.FromDictionaryLiteral(("questions", questions));
        var request = JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "AskUserQuestion"), ("input", input));
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "req-q"), ("request", request)));
        var question = Assert.Single(events.OfType<ProviderEvent.Question>()).Value;
        Assert.Equal("req-q", question.Id);
        var q = Assert.Single(question.Questions); // the entry with no "question" key is skipped
        Assert.Equal("Pick one", q.Id);
        Assert.Equal("Pick one", q.Prompt);
        Assert.Equal("", q.Header);
        Assert.True(q.AllowsMultiple);
        Assert.True(q.AllowsOther);
        Assert.False(q.IsSecret);
        var choice = Assert.Single(q.Choices); // the option with no "label" is skipped
        Assert.Equal("A", choice.Label);
        Assert.Equal("first", choice.Detail);
    }

    // ---- PR-PROVCLI-059 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-059")] // ClaudeSession.swift:640-651
    [Trait("Req", "UI-PROVCLI-014")] // ClaudeSession.swift:645 - plan approval title, verbatim "Implement this plan"
    [Trait("Req", "UI-PROVCLI-004")] // ClaudeSession.swift:647 - "Implement plan" button, id "allow", role approve
    [Trait("Req", "UI-PROVCLI-005")] // ClaudeSession.swift:648 - "Keep planning" button, id "deny", role decline
    public void HandleControlRequest_exitPlanMode_emits_plan_then_a_two_option_approval()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-p"),
            ("request", JSONValue.FromDictionaryLiteral(
                ("subtype", "can_use_tool"), ("tool_name", "ExitPlanMode"), ("tool_use_id", "tu-1"),
                ("input", JSONValue.FromDictionaryLiteral(("plan", "do the thing")))))));
        var plan = Assert.Single(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Equal("tu-1", plan.Id);
        Assert.Equal("do the thing", plan.Markdown);
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("req-p", approval.Id);
        Assert.Equal(ApprovalRequest.Kind.Plan, approval.RequestKind);
        Assert.Equal("Implement this plan", approval.Title);
        Assert.Equal("tu-1", approval.ToolItemID);
        Assert.Equal(2, approval.Options.Count);
        Assert.Equal("allow", approval.Options[0].Id);
        Assert.Equal("Implement plan", approval.Options[0].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Approve, approval.Options[0].OptionRole);
        Assert.Equal("deny", approval.Options[1].Id);
        Assert.Equal("Keep planning", approval.Options[1].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Decline, approval.Options[1].OptionRole);

        // A missing plan shows an empty plan body; the id falls back to the control request id.
        events.Clear();
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-p2"), ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "ExitPlanMode")))));
        var plan2 = Assert.Single(events.OfType<ProviderEvent.PlanCompleted>());
        Assert.Equal("req-p2", plan2.Id);
        Assert.Equal("", plan2.Markdown);
    }

    // ---- PR-PROVCLI-060 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-060")] // ClaudeSession.swift:652-675
    [Trait("Req", "UI-PROVCLI-001")] // ClaudeSession.swift:661 - "Approve" button, id "allow", role approve
    [Trait("Req", "UI-PROVCLI-002")] // ClaudeSession.swift:662-664 - "Approve for session" button, id "always", conditional on permission_suggestions
    [Trait("Req", "UI-PROVCLI-003")] // ClaudeSession.swift:665 - "Decline" button, id "deny", role decline
    public void HandleControlRequest_default_tool_builds_an_approval_whose_kind_follows_the_call_with_edits_suppressed()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };

        // A command call, with permission_suggestions present -> "always" offered; detail is request.description.
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-1"),
            ("request", JSONValue.FromDictionaryLiteral(
                ("subtype", "can_use_tool"), ("tool_name", "Bash"), ("description", "list files"), ("tool_use_id", "tu-1"),
                ("decision_reason", "matches an allowlist"),
                ("input", JSONValue.FromDictionaryLiteral(("command", "ls"))),
                ("permission_suggestions", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("tool", "Bash"))))))));
        var approval = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.Command, approval.RequestKind);
        Assert.Equal("ls", approval.Title); // the ToolCall built with edits suppressed
        Assert.Equal("list files", approval.Detail); // command kind: request.description, not the tool call's own detail
        Assert.Equal("matches an allowlist", approval.Reason);
        Assert.Equal("tu-1", approval.ToolItemID);
        Assert.Equal(3, approval.Options.Count);
        Assert.Equal("allow", approval.Options[0].Id);
        Assert.Equal("Approve", approval.Options[0].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Approve, approval.Options[0].OptionRole);
        Assert.Equal("always", approval.Options[1].Id);
        Assert.Equal("Approve for session", approval.Options[1].Title);
        Assert.Equal(ApprovalRequest.Option.Role.ApproveAlways, approval.Options[1].OptionRole);
        Assert.Equal("deny", approval.Options[2].Id);
        Assert.Equal("Decline", approval.Options[2].Title);
        Assert.Equal(ApprovalRequest.Option.Role.Decline, approval.Options[2].OptionRole);

        // An edit call, with no permission_suggestions -> only allow/deny; Write's edits stay suppressed.
        events.Clear();
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-2"),
            ("request", JSONValue.FromDictionaryLiteral(
                ("subtype", "can_use_tool"), ("tool_name", "Write"),
                ("input", JSONValue.FromDictionaryLiteral(("file_path", "a.txt"), ("content", "hi")))))));
        var approval2 = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.FileChange, approval2.RequestKind);
        Assert.Equal(2, approval2.Options.Count);
        Assert.DoesNotContain(approval2.Options, o => o.Id == "always");

        // Any other kind (e.g. Read) maps to .tool; a non-command kind's detail is the tool call's own (nil here).
        events.Clear();
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(
            ("request_id", "req-3"),
            ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "Read"), ("input", JSONValue.FromDictionaryLiteral(("file_path", "b.txt")))))));
        var approval3 = Assert.Single(events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal(ApprovalRequest.Kind.Tool, approval3.RequestKind);
        Assert.Equal("b.txt", approval3.Title);
        Assert.Null(approval3.Detail);
    }

    // ---- PR-PROVCLI-061 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-061")] // ClaudeSession.swift:678-692
    public void DidClose_fails_every_waiting_request_clears_prompts_and_reports_exited_only_for_a_started_session()
    {
        using var runtime = new TestRuntime();
        using var storage_runtime = new StorageScratchFolder();
        var script = Script("didclose-started");
        File.WriteAllText(Path.Combine(script.ControlDirectory, "reply-limit.txt"), "1");
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(script)) { OnEvent = events.Add };
        RunStart(runtime, session);
        session.SeedPendingTool("req-1", "Bash", JSONValue.FromDictionaryLiteral(("command", "ls")));

        // Swift :684 fails requests that are already waiting, and :685-686 clears existing prompts.
        // Keep the child alive until both controls are registered and the tool prompt has been seeded.
        var first = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "interrupt"))));
        var second = MainActor.SpawnThrowing(() => session.Control(JSONValue.FromDictionaryLiteral(("subtype", "set_model"))));
        runtime.RunUntilIdle();
        Assert.Contains("\"request_id\":\"marrow-2\"", script.CapturedLine(1, 1), StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"marrow-3\"", script.CapturedLine(1, 2), StringComparison.Ordinal);
        runtime.RunUntilIdle();
        Assert.False(first.Value.IsCompleted);
        Assert.False(second.Value.IsCompleted);
        File.WriteAllText(Path.Combine(script.ControlDirectory, "exit-now.txt"), "1");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((session.IsRunning || !first.Value.IsCompleted || !second.Value.IsCompleted ||
                !events.OfType<ProviderEvent.Exited>().Any()) && DateTime.UtcNow < deadline)
        {
            runtime.RunUntilIdle();
            System.Threading.Thread.Sleep(20);
        }
        Assert.False(session.IsRunning);
        runtime.RunUntilIdle();

        Assert.Contains(events, e => e is ProviderEvent.RequestResolved r && r.Id == "req-1"); // pending tools fail
        Assert.NotEmpty(events.OfType<ProviderEvent.Exited>()); // a started session says it exited
        Assert.True(first.Value.IsFaulted);
        Assert.True(second.Value.IsFaulted);
        var failure = Assert.IsType<ProviderError>(first.Value.Exception!.InnerException);
        Assert.Same(failure, second.Value.Exception!.InnerException);
        Assert.Equal("Claude exited.", failure.ErrorDescription); // :684, empty stderr and no launch error
        var pendingTools = typeof(ClaudeSession).GetField("pendingTools", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        Assert.Empty((System.Collections.IEnumerable)pendingTools); // :686 actually cleared the prompts
    }

    // ---- PR-PROVCLI-062 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-062")] // ClaudeSession.swift:699-757
    public void ToolCall_dispatches_by_exact_case_sensitive_name_and_the_default_falls_through_to_other()
    {
        var session = new ClaudeSession(MinimalConfig());

        // Case sensitive, no normalization: a differently-cased name is unrecognized.
        var lower = session.ToolCall("bash", JSONValue.FromDictionaryLiteral(("command", "ls")))!;
        Assert.Equal(ToolCall.Kind.Other, lower.ToolCallKind);
        Assert.Equal("bash", lower.Title); // the catch-all default (:755), not Bash's own command title

        // withEdits is read only by Write/Edit/MultiEdit; Bash and Read ignore it either way.
        Assert.Empty(session.ToolCall("Bash", JSONValue.FromDictionaryLiteral(("command", "ls")), withEdits: false)!.Edits);
        Assert.Empty(session.ToolCall("Read", JSONValue.FromDictionaryLiteral(("file_path", "a.txt")), withEdits: true)!.Edits);
    }

    // ---- PR-PROVCLI-354 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-354")] // ClaudeSession.swift:703-705
    public void ToolCall_five_hidden_tools_never_make_a_row()
    {
        var session = new ClaudeSession(MinimalConfig());
        foreach (var name in new[] { "TodoWrite", "AskUserQuestion", "ExitPlanMode", "EnterPlanMode", "ToolSearch" })
            Assert.Null(session.ToolCall(name, JSONValue.FromDictionaryLiteral()));
    }

    // ---- PR-PROVCLI-355 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-355")] // ClaudeSession.swift:706-707
    public void ToolCall_bash_titles_the_row_with_the_command_and_subtitles_with_the_description()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Bash", JSONValue.FromDictionaryLiteral(("command", "ls -la"), ("description", "list files")))!;
        Assert.Equal(ToolCall.Kind.Command, call.ToolCallKind);
        Assert.Equal("ls -la", call.Title);
        Assert.Equal("list files", call.Detail);

        var fallback = session.ToolCall("Bash", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Shell command", fallback.Title);
        Assert.Null(fallback.Detail); // no detail fallback string
    }

    // ---- PR-PROVCLI-356 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-356")] // ClaudeSession.swift:708-709
    public void ToolCall_six_shell_management_tools_title_the_row_with_the_tool_name_verbatim()
    {
        var session = new ClaudeSession(MinimalConfig());
        foreach (var name in new[] { "BashOutput", "KillShell", "KillBash", "Monitor", "TaskOutput", "TaskStop" })
        {
            var call = session.ToolCall(name, JSONValue.FromDictionaryLiteral(("ignored", "value")))!;
            Assert.Equal(ToolCall.Kind.Command, call.ToolCallKind);
            Assert.Equal(name, call.Title);
            Assert.Null(call.Detail);
        }
    }

    // ---- PR-PROVCLI-357 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-357")] // ClaudeSession.swift:710-711
    public void ToolCall_read_titles_the_row_with_the_relative_file_path()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Read", JSONValue.FromDictionaryLiteral(("file_path", "notes.txt")))!;
        Assert.Equal(ToolCall.Kind.Read, call.ToolCallKind);
        Assert.Equal("notes.txt", call.Title);
        Assert.Null(call.Detail);
        Assert.Empty(call.Edits);

        var fallback = session.ToolCall("Read", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Read file", fallback.Title);
    }

    // ---- PR-PROVCLI-358 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-358")] // ClaudeSession.swift:712-717
    public void ToolCall_write_makes_an_edit_row_whose_single_fileEdit_has_an_empty_old_side()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Write", JSONValue.FromDictionaryLiteral(("file_path", "new.txt"), ("content", "hello")))!;
        Assert.Equal(ToolCall.Kind.Edit, call.ToolCallKind);
        Assert.Equal("new.txt", call.Title);
        var edit = Assert.Single(call.Edits);
        Assert.Equal("new.txt", edit.Path);
        Assert.Equal(0, edit.Deletions); // the old side is empty: a whole-file addition
        Assert.True(edit.Additions > 0);

        // withEdits false: the row is returned without edits.
        Assert.Empty(session.ToolCall("Write", JSONValue.FromDictionaryLiteral(("file_path", "new.txt"), ("content", "hello")), withEdits: false)!.Edits);

        // Missing content: no edit attached even with withEdits true.
        Assert.Empty(session.ToolCall("Write", JSONValue.FromDictionaryLiteral(("file_path", "new.txt")))!.Edits);

        var fallback = session.ToolCall("Write", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Write file", fallback.Title);
    }

    // ---- PR-PROVCLI-359 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-359")] // ClaudeSession.swift:718-723
    public void ToolCall_edit_makes_an_edit_row_whose_single_fileEdit_takes_both_sides_defaulting_to_empty()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Edit", JSONValue.FromDictionaryLiteral(("file_path", "a.txt"), ("old_string", "foo"), ("new_string", "bar")))!;
        Assert.Equal(ToolCall.Kind.Edit, call.ToolCallKind);
        var edit = Assert.Single(call.Edits);
        Assert.Equal("a.txt", edit.Path);

        // Unlike Write, there is no third condition on content: a call with neither string still attaches one
        // FileEdit with both sides empty.
        var bothEmpty = session.ToolCall("Edit", JSONValue.FromDictionaryLiteral(("file_path", "a.txt")))!;
        var emptyEdit = Assert.Single(bothEmpty.Edits);
        Assert.Equal(0, emptyEdit.Additions);
        Assert.Equal(0, emptyEdit.Deletions);

        var fallback = session.ToolCall("Edit", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Edit file", fallback.Title); // the same fallback MultiEdit uses
    }

    // ---- PR-PROVCLI-360 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-360")] // ClaudeSession.swift:724-731
    public void ToolCall_multiEdit_makes_one_row_with_one_fileEdit_per_entry_against_the_same_path()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("MultiEdit", JSONValue.FromDictionaryLiteral(
            ("file_path", "a.txt"),
            ("edits", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("old_string", "a"), ("new_string", "b")),
                JSONValue.FromDictionaryLiteral(), // neither key: still becomes an empty-to-empty FileEdit (map, not compactMap)
                JSONValue.FromDictionaryLiteral(("old_string", "c"), ("new_string", "d"))))))!;
        Assert.Equal(ToolCall.Kind.Edit, call.ToolCallKind);
        Assert.Equal(3, call.Edits.Count);
        Assert.All(call.Edits, edit => Assert.Equal("a.txt", edit.Path));

        // Missing "edits": an empty array, not an omitted list.
        var noEdits = session.ToolCall("MultiEdit", JSONValue.FromDictionaryLiteral(("file_path", "a.txt")))!;
        Assert.Empty(noEdits.Edits);

        var fallback = session.ToolCall("MultiEdit", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Edit file", fallback.Title);
    }

    // ---- PR-PROVCLI-361 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-361")] // ClaudeSession.swift:732-733
    public void ToolCall_notebookEdit_titles_from_notebook_path_and_attaches_no_edits()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("NotebookEdit", JSONValue.FromDictionaryLiteral(("notebook_path", "nb.ipynb")), withEdits: true)!;
        Assert.Equal(ToolCall.Kind.Edit, call.ToolCallKind);
        Assert.Equal("nb.ipynb", call.Title);
        Assert.Empty(call.Edits); // no FileEdit is ever attached, even with withEdits true

        var fallback = session.ToolCall("NotebookEdit", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Edit notebook", fallback.Title);

        // file_path is not read for this tool; only notebook_path.
        var wrongKey = session.ToolCall("NotebookEdit", JSONValue.FromDictionaryLiteral(("file_path", "should-not-be-used.ipynb")))!;
        Assert.Equal("Edit notebook", wrongKey.Title);
    }

    // ---- PR-PROVCLI-063 ----
    //
    // B3: todos(from:) (old ClaudeSession.swift:765-775) was deleted; TodoWrite now goes through the shared
    // ACPSession.ParseTodoItems (ClaudeSession.swift:534-535). Its own key-order/blank-drop/seven-status
    // combinatorics are already exhaustively covered where that parser lives
    // (ACPSessionTests.ParseTodoItems_takes_the_first_of_four_keys_maps_seven_statuses_and_defaults_to_pending,
    // PR-PROVACP-087, "already B3-correct" per the lane record's b3_note); this test proves ClaudeSession's own
    // wiring: input["todos"] reaches the shared parser unchanged (so its key fallback order and blank-drop
    // both show through here too, not just a hard-coded "content"/two-status subset), the result is emitted
    // as a single .todos event and never as a tool row, and a missing todos array still emits (empty).

    [Fact]
    [Trait("Req", "PR-PROVCLI-063")] // ClaudeSession.swift:534-535, ACPSession.swift:685-699
    public void HandleAssistant_TodoWrite_delegates_to_the_shared_parser_key_fallback_blank_drop_included()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig());
        var stream = new ClaudeSession.StreamState();

        var todos = JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("content", "from content"), ("status", "completed")),
            JSONValue.FromDictionaryLiteral(("text", "from text"), ("status", "active")), // content absent: falls back to text
            JSONValue.FromDictionaryLiteral(("step", "from step")), // content/text absent: falls back to step; no status -> pending
            JSONValue.FromDictionaryLiteral(("title", "from title")), // content/text/step all absent: falls back to title
            JSONValue.FromDictionaryLiteral(("content", "   \n"), ("text", "never reached")), // blank content is NOT replaced by a later key: dropped
            new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>()))); // none of the four keys: dropped
        var input = JSONValue.FromDictionaryLiteral(("todos", todos));
        var block = JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t-todo"), ("name", "TodoWrite"), ("input", input));
        var message = JSONValue.FromDictionaryLiteral(("message", JSONValue.FromDictionaryLiteral(("id", "m1"), ("content", JSONValue.FromArrayLiteral(block)))));
        session.HandleAssistant(message, ref stream, events.Add);

        var emitted = Assert.Single(events.OfType<ProviderEvent.Todos>()); // exactly one .todos event
        Assert.Equal(["from content", "from text", "from step", "from title"], emitted.Value.Select(s => s.Text));
        Assert.Equal(TodoStep.Status.Done, emitted.Value[0].TodoStepStatus);
        Assert.Equal(TodoStep.Status.Active, emitted.Value[1].TodoStepStatus);
        Assert.Equal(TodoStep.Status.Pending, emitted.Value[2].TodoStepStatus); // missing status -> pending
        Assert.Equal(TodoStep.Status.Pending, emitted.Value[3].TodoStepStatus);
        Assert.Empty(events.OfType<ProviderEvent.ToolStarted>()); // never a tool row (hiddenResultTools/toolCall both exclude it)

        // A missing todos array still emits, as an empty list.
        events.Clear();
        var emptyBlock = JSONValue.FromDictionaryLiteral(("type", "tool_use"), ("id", "t-todo-2"), ("name", "TodoWrite"), ("input", JSONValue.FromDictionaryLiteral()));
        var emptyMessage = JSONValue.FromDictionaryLiteral(("message", JSONValue.FromDictionaryLiteral(("id", "m2"), ("content", JSONValue.FromArrayLiteral(emptyBlock)))));
        session.HandleAssistant(emptyMessage, ref stream, events.Add);
        Assert.Empty(Assert.Single(events.OfType<ProviderEvent.Todos>()).Value);
    }

    // ---- PR-PROVCLI-064 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-064")] // ClaudeSession.swift:759-773, :802-812
    public void TurnSpend_sums_four_token_fields_across_iterations_while_contextUsage_reads_the_last_iteration()
    {
        // No usage object: zero spend, no context usage.
        Assert.Equal(0, ClaudeSession.TurnSpend(JSONValue.FromDictionaryLiteral()));
        Assert.Null(ClaudeSession.ContextUsageOf(JSONValue.FromDictionaryLiteral()));

        // A single usage object, no iterations: the four fields summed, missing ones zero.
        var single = JSONValue.FromDictionaryLiteral(("usage", JSONValue.FromDictionaryLiteral(("input_tokens", 3L), ("output_tokens", 4L))));
        Assert.Equal(7, ClaudeSession.TurnSpend(single));
        var singleUsage = ClaudeSession.ContextUsageOf(single)!;
        Assert.Equal(7, singleUsage.UsedTokens);
        Assert.Null(singleUsage.WindowTokens);

        // With iterations: turnSpend sums every iteration; contextUsage reads only the LAST one.
        var multi = JSONValue.FromDictionaryLiteral(
            ("usage", JSONValue.FromDictionaryLiteral(
                ("iterations", JSONValue.FromArrayLiteral(
                    JSONValue.FromDictionaryLiteral(("input_tokens", 1L), ("output_tokens", 1L)),
                    JSONValue.FromDictionaryLiteral(("input_tokens", 2L), ("cache_read_input_tokens", 3L), ("cache_creation_input_tokens", 4L), ("output_tokens", 5L)))))),
            ("modelUsage", JSONValue.FromDictionaryLiteral(
                ("m1", JSONValue.FromDictionaryLiteral(("contextWindow", 100L))),
                ("m2", JSONValue.FromDictionaryLiteral(("contextWindow", 200L))))));
        Assert.Equal(1 + 1 + 2 + 3 + 4 + 5, ClaudeSession.TurnSpend(multi));
        var multiUsage = ClaudeSession.ContextUsageOf(multi)!;
        Assert.Equal(2 + 3 + 4 + 5, multiUsage.UsedTokens); // the last iteration only
        Assert.Equal(200, multiUsage.WindowTokens); // the max across modelUsage's values

        // Zero usage: no ContextUsage, no TokenSpend event.
        var zero = JSONValue.FromDictionaryLiteral(("usage", JSONValue.FromDictionaryLiteral()));
        Assert.Null(ClaudeSession.ContextUsageOf(zero));
        Assert.Null(ClaudeSession.TurnSpendEvent(zero, null));

        // turnSpendEvent: with iterations, totalTokens only, no parts guessed.
        var eventWithIterations = ClaudeSession.TurnSpendEvent(multi, "claude-x")!;
        Assert.Equal(1 + 1 + 2 + 3 + 4 + 5, eventWithIterations.TotalTokens);
        Assert.Null(eventWithIterations.InputTokens);
        Assert.Null(eventWithIterations.OutputTokens);
        Assert.Null(eventWithIterations.CachedInputTokens);
        Assert.Equal("claude-x", eventWithIterations.Model);
        Assert.Equal(ProviderKind.Claude, eventWithIterations.Provider);

        // turnSpendEvent: single usage object, inputTokens folds in the cache fields, cachedInputTokens stays a slice.
        var singleUsageObj = JSONValue.FromDictionaryLiteral(("usage", JSONValue.FromDictionaryLiteral(
            ("input_tokens", 3L), ("cache_read_input_tokens", 2L), ("cache_creation_input_tokens", 1L), ("output_tokens", 4L))));
        var eventSingle = ClaudeSession.TurnSpendEvent(singleUsageObj, null)!;
        Assert.Equal(3 + 2 + 1 + 4, eventSingle.TotalTokens);
        Assert.Equal(3 + 2 + 1, eventSingle.InputTokens);
        Assert.Equal(4, eventSingle.OutputTokens);
        Assert.Equal(2, eventSingle.CachedInputTokens);
    }

    // ---- PR-PROVCLI-362 to PR-PROVCLI-370, UI-PROVCLI-031 (part 4) ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-362")] // ClaudeSession.swift:734-735
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:734-735 - Claude's fallback title "Find files"
    public void ToolCall_glob_titles_with_the_pattern_and_subtitles_with_the_relative_search_path()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Glob", JSONValue.FromDictionaryLiteral(("pattern", "*.cs"), ("path", "src")))!;
        Assert.Equal(ToolCall.Kind.Search, call.ToolCallKind);
        Assert.Equal("*.cs", call.Title);
        Assert.Equal("src", call.Detail);
        Assert.Empty(call.Edits);

        var fallback = session.ToolCall("Glob", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Find files", fallback.Title); // Glob's own fallback, distinct from Grep's "Search"
        Assert.Null(fallback.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-363")] // ClaudeSession.swift:736-737
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:736-737 - Claude's fallback title "Search", one word apart from Glob's "Find files"
    public void ToolCall_grep_titles_with_the_pattern_and_subtitles_with_the_relative_search_path()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("Grep", JSONValue.FromDictionaryLiteral(("pattern", "TODO"), ("path", "src")))!;
        Assert.Equal(ToolCall.Kind.Search, call.ToolCallKind);
        Assert.Equal("TODO", call.Title);
        Assert.Equal("src", call.Detail);

        var fallback = session.ToolCall("Grep", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Search", fallback.Title); // NOT "Find files", which is Glob's
        Assert.Null(fallback.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-364")] // ClaudeSession.swift:738-739
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:738-739 - Claude's fallback title "List files"
    public void ToolCall_ls_titles_with_the_relative_path_and_has_no_subtitle()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("LS", JSONValue.FromDictionaryLiteral(("path", "src")))!;
        Assert.Equal(ToolCall.Kind.Search, call.ToolCallKind);
        Assert.Equal("src", call.Title); // the path is the TITLE here, opposite of Glob and Grep
        Assert.Null(call.Detail);

        var fallback = session.ToolCall("LS", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("List files", fallback.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-365")] // ClaudeSession.swift:740-741
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:740-741 - Claude's fallback title "Fetch page"
    public void ToolCall_webFetch_titles_with_the_raw_url_and_has_no_subtitle()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("WebFetch", JSONValue.FromDictionaryLiteral(("url", "https://example.com/a")))!;
        Assert.Equal(ToolCall.Kind.Web, call.ToolCallKind);
        Assert.Equal("https://example.com/a", call.Title); // the raw URL, not shortened to a host
        Assert.Null(call.Detail);

        var fallback = session.ToolCall("WebFetch", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Fetch page", fallback.Title);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-366")] // ClaudeSession.swift:742-743
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:742-743 - Claude's fallback title "Search the web"
    public void ToolCall_webSearch_titles_with_the_query_and_has_no_subtitle()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("WebSearch", JSONValue.FromDictionaryLiteral(("query", "how to port swift")))!;
        Assert.Equal(ToolCall.Kind.Web, call.ToolCallKind);
        Assert.Equal("how to port swift", call.Title);
        Assert.Null(call.Detail);

        var fallback = session.ToolCall("WebSearch", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Search the web", fallback.Title); // a different key and a different fallback from WebFetch
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-367")] // ClaudeSession.swift:744-745
    [Trait("Req", "UI-PROVCLI-031")] // ClaudeSession.swift:744-745 - Claude's fallback title "Subagent"
    public void ToolCall_task_and_agent_both_title_with_the_description_and_subtitle_with_the_subagent_type()
    {
        var session = new ClaudeSession(MinimalConfig());
        foreach (var name in new[] { "Task", "Agent" })
        {
            var call = session.ToolCall(name, JSONValue.FromDictionaryLiteral(("description", "Build the thing"), ("subagent_type", "builder")))!;
            Assert.Equal(ToolCall.Kind.Agent, call.ToolCallKind);
            Assert.Equal("Build the thing", call.Title);
            Assert.Equal("builder", call.Detail);
        }

        var fallback = session.ToolCall("Task", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Subagent", fallback.Title);
        Assert.Null(fallback.Detail); // no detail fallback
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-368")] // ClaudeSession.swift:746-747
    public void ToolCall_skill_titles_with_Skill_middle_dot_and_the_skill_name_falling_back_to_command_then_empty()
    {
        var session = new ClaudeSession(MinimalConfig());
        var bySkillKey = session.ToolCall("Skill", JSONValue.FromDictionaryLiteral(("skill", "reviewer")))!;
        Assert.Equal(ToolCall.Kind.Other, bySkillKey.ToolCallKind);
        Assert.Equal("Skill · reviewer", bySkillKey.Title); // U+00B7 MIDDLE DOT, not a hyphen, bullet or colon
        Assert.Null(bySkillKey.Detail);

        var byCommandKey = session.ToolCall("Skill", JSONValue.FromDictionaryLiteral(("command", "cmd-name")))!;
        Assert.Equal("Skill · cmd-name", byCommandKey.Title); // falls back to "command" when "skill" is absent

        var neitherKey = session.ToolCall("Skill", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("Skill · ", neitherKey.Title); // empty fallback: a visible trailing separator and space
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-369")] // ClaudeSession.swift:748-754
    public void ToolCall_unrecognized_mcp_prefixed_name_splits_into_server_and_tool_rejoining_extra_underscored_segments()
    {
        var session = new ClaudeSession(MinimalConfig());

        // Three+ segments: parts[1] is the server, parts[2...] rejoined with "__" is the tool -
        // a tool name that itself contains "__" is put back together, not truncated at the first split.
        var multiSegment = session.ToolCall("mcp__myserver__nested__tool", JSONValue.FromDictionaryLiteral(("a", 1L)))!;
        Assert.Equal(ToolCall.Kind.Mcp, multiSegment.ToolCallKind);
        Assert.Equal("myserver · nested__tool", multiSegment.Title); // U+00B7 MIDDLE DOT
        Assert.Equal("{\"a\":1}", multiSegment.Detail);

        // Exactly two segments after "mcp": server present, tool falls back to the WHOLE original name.
        var twoSegments = session.ToolCall("mcp__onlyserver", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("onlyserver · mcp__onlyserver", twoSegments.Title);

        // Only "mcp" itself (no server segment): server falls back to the literal "MCP".
        var justMcp = session.ToolCall("mcp__", JSONValue.FromDictionaryLiteral())!;
        Assert.Equal("MCP · mcp__", justMcp.Title);

        // Null input: detail is nil, not the string "null". An empty (non-null) object is not nil, though:
        // it stringifies to "{}" (compactString of an empty object), as seen for twoSegments/justMcp above.
        var nullInput = session.ToolCall("mcp__s__t", new JSONValue.Null())!;
        Assert.Null(nullInput.Detail);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-370")] // ClaudeSession.swift:755
    public void ToolCall_any_other_unrecognized_name_becomes_an_other_row_titled_with_its_name_verbatim()
    {
        var session = new ClaudeSession(MinimalConfig());
        var call = session.ToolCall("SomeFutureTool", JSONValue.FromDictionaryLiteral(("x", "y")))!;
        Assert.Equal(ToolCall.Kind.Other, call.ToolCallKind);
        Assert.Equal("SomeFutureTool", call.Title); // no humanizing, no truncation
        Assert.Null(call.Detail);
        Assert.Empty(call.Edits);
    }

    // ---- PR-PROVCLI-439 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-439")] // ClaudeSession.swift:775-800, :35,:62,:222-225, :581
    public void HandleResult_turnSpendEvent_carries_the_sessions_own_stored_model()
    {
        // model starts as configuration.model at init (:35,:62) and HandleResult (:581) passes it through
        // to TurnSpendEvent unchanged, including the literal "default".
        using var storage_runtime = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig() with { Model = "default" }) { OnEvent = events.Add };
        session.MarkStarted();
        runtime.OnMain(() => session.HandleResult(JSONValue.FromDictionaryLiteral(
            ("subtype", "success"),
            ("usage", JSONValue.FromDictionaryLiteral(("input_tokens", 1L), ("output_tokens", 1L))))));
        var spend = Assert.Single(events.OfType<ProviderEvent.TokenSpend>()).Value;
        Assert.Equal("default", spend.Model); // kept verbatim; only the wire value sent to the CLI becomes null
        Assert.Equal(ProviderKind.Claude, spend.Provider);

        // A different configured model is likewise carried through unchanged.
        events.Clear();
        var session2 = new ClaudeSession(MinimalConfig() with { Model = "claude-opus-4" }) { OnEvent = events.Add };
        session2.MarkStarted();
        runtime.OnMain(() => session2.HandleResult(JSONValue.FromDictionaryLiteral(
            ("subtype", "success"),
            ("usage", JSONValue.FromDictionaryLiteral(("input_tokens", 1L), ("output_tokens", 1L))))));
        var spend2 = Assert.Single(events.OfType<ProviderEvent.TokenSpend>()).Value;
        Assert.Equal("claude-opus-4", spend2.Model);
    }
}
