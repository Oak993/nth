using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ClaudeSession.swift (B2 1fbf81e), :1-693.
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
    [Trait("Req", "PR-PROVCLI-004")] // ClaudeSession.swift:844-852
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
    [Trait("Req", "PR-PROVCLI-019")] // ClaudeSession.swift:301-310
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
    [Trait("Req", "PR-PROVCLI-031")] // ClaudeSession.swift:312-322
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
    [Trait("Req", "PR-PROVCLI-034")] // ClaudeSession.swift:324-329 - every tool answer, denial included, wraps as a success control_response
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
    [Trait("Req", "PR-PROVCLI-035")] // ClaudeSession.swift:254-262, :838-842
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
    [Trait("Req", "PR-PROVCLI-039")] // ClaudeSession.swift:289-292, :696
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
    [Trait("Req", "PR-PROVCLI-046")] // ClaudeSession.swift:427-447 - registers the head this test then stops
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
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:346-349 (control_cancel_request case of the dispatch switch :331-386)
    [Trait("Req", "PR-PROVCLI-043")] // ClaudeSession.swift:346-349
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
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:383-384 - an unrecognized type is silently ignored
    public void Handle_ignores_an_unrecognized_message_type()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.Handle(JSONValue.FromDictionaryLiteral(("type", "something_else")));
        session.Handle(new JSONValue.Object(ValueMap<string, JSONValue>.Empty)); // no "type" at all
        Assert.Empty(events);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-041")] // ClaudeSession.swift:375-382 - the rate_limit_event case of the same dispatch switch
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
    [Trait("Req", "PR-PROVCLI-042")] // ClaudeSession.swift:335-343
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
    [Trait("Req", "PR-PROVCLI-042")] // ClaudeSession.swift:340-341 - the fallback message when no error text is given
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

    // ---- PR-PROVCLI-044 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-044")] // ClaudeSession.swift:390-394
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
    [Trait("Req", "PR-PROVCLI-045")] // ClaudeSession.swift:395-396
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
    [Trait("Req", "PR-PROVCLI-046")] // ClaudeSession.swift:427-447
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
    [Trait("Req", "PR-PROVCLI-047")] // ClaudeSession.swift:399-412
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
        Assert.Empty(events.OfType<ProviderEvent.TokenSpend>()); // never added to the ledger, :402-405

        events.Clear();
        // Unknown task id: nothing.
        session.HandleSystem(JSONValue.FromDictionaryLiteral(("subtype", "task_progress"), ("task_id", "missing")));
        Assert.Empty(events);
    }

    // ---- PR-PROVCLI-048 ----

    [Fact]
    [Trait("Req", "PR-PROVCLI-048")] // ClaudeSession.swift:413-421
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
}
