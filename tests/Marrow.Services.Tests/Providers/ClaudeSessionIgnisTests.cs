using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: Ignis unit U2 (OD-0134, ED-NEW-ignis-providers; work/ignis/PLAN.md). Claude's CacheUsage event (the
// result's iterations summed, the 5-minute and 1-hour write fields kept apart), the HelperAgents seam (only the
// scout's model changes) and Compact(instructions). Process tests run tests/fixtures/claude-cli/ClaudeStandIn.cs,
// the offline stand-in (R-TEST-8, R-TEST-10): no real CLI and no provider is ever reached.
public sealed class ClaudeSessionIgnisTests
{
    // See ClaudeSessionTests.FileUrl (ENGINEERING DECISION, test-only).
    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private const string MinimalResponse = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{}}}";

    private static SessionConfiguration Config(ClaudeScript script, string? model = null, HydraLaunch? hydra = null) => new()
    {
        Provider = ProviderKind.Claude,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
        Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        Model = model,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Hydra = hydra,
    };

    private static SessionConfiguration MinimalConfig() => new()
    {
        Provider = ProviderKind.Claude,
        WorkingDirectory = FileUrl(Path.GetTempPath()),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static ClaudeScript Script(string label)
    {
        var script = new ClaudeScript(label);
        script.SetResponse(MinimalResponse);
        return script;
    }

    private static JSONValue Iteration(long input, long read, long written, long output, long? fiveMinutes = null, long? oneHour = null)
    {
        var fields = new List<(string, JSONValue)>
        {
            ("input_tokens", input), ("cache_read_input_tokens", read), ("cache_creation_input_tokens", written), ("output_tokens", output),
        };
        if (fiveMinutes is not null || oneHour is not null)
            fields.Add(("cache_creation", JSONValue.FromDictionaryLiteral(
                ("ephemeral_5m_input_tokens", fiveMinutes ?? 0), ("ephemeral_1h_input_tokens", oneHour ?? 0))));
        return JSONValue.FromDictionaryLiteral([.. fields]);
    }

    // ---- CacheUsage ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void CacheUsageOf_sums_the_iterations_and_keeps_the_five_minute_and_one_hour_writes_apart()
    {
        var result = JSONValue.FromDictionaryLiteral(
            ("subtype", "success"),
            ("usage", JSONValue.FromDictionaryLiteral(
                ("input_tokens", 999L), // the top level is ignored once iterations are present
                ("iterations", JSONValue.FromArrayLiteral(
                    Iteration(input: 12, read: 40_000, written: 3_000, output: 300, fiveMinutes: 1_000, oneHour: 2_000),
                    Iteration(input: 8, read: 43_000, written: 500, output: 120, fiveMinutes: 0, oneHour: 400))))),
            ("modelUsage", JSONValue.FromDictionaryLiteral(("claude-opus", JSONValue.FromDictionaryLiteral(("contextWindow", 200_000L))))));

        var usage = ClaudeSession.CacheUsageOf(result, "claude-opus")!;

        // The second iteration's 500 written tokens name a TTL for 400 of them; 100 stay unknown.
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Claude, "claude-opus", null,
            UncachedInput: 20, CacheRead: 83_000, CacheWrite5m: 1_000, CacheWrite1h: 2_400, CacheWriteUnknownTtl: 100, Output: 420,
            ContextUsed: 8 + 43_000 + 500 + 120, ContextWindow: 200_000, Reported: true), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void CacheUsageOf_reads_the_top_level_usage_and_puts_a_write_without_ttl_in_the_unknown_bucket()
    {
        var result = JSONValue.FromDictionaryLiteral(("usage", Iteration(input: 10, read: 2, written: 1, output: 5)));

        var usage = ClaudeSession.CacheUsageOf(result, null)!;

        Assert.Equal(new IgnisTurnUsage(ProviderKind.Claude, null, null, 10, 2, 0, 0, 1, 5, 18, null, true), usage);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void CacheUsageOf_without_cache_fields_is_not_reported_and_without_usage_is_null()
    {
        var bare = JSONValue.FromDictionaryLiteral(("usage", JSONValue.FromDictionaryLiteral(("input_tokens", 10L), ("output_tokens", 5L))));
        Assert.Equal(IgnisTurnUsage.NotReported(ProviderKind.Claude, "m", null, 15, null), ClaudeSession.CacheUsageOf(bare, "m"));
        Assert.Null(ClaudeSession.CacheUsageOf(JSONValue.FromDictionaryLiteral(("subtype", "success")), "m"));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void HandleResult_emits_cache_usage_after_usage_and_spend_and_before_the_turn_ends()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(MinimalConfig()) { OnEvent = events.Add };
        session.MarkStarted();

        runtime.OnMain(() => session.HandleResult(JSONValue.FromDictionaryLiteral(
            ("subtype", "success"),
            ("usage", Iteration(input: 10, read: 2, written: 1, output: 5, fiveMinutes: 0, oneHour: 1)))));

        var cacheIndex = events.FindIndex(e => e is ProviderEvent.CacheUsage);
        Assert.True(cacheIndex > events.FindIndex(e => e is ProviderEvent.TokenSpend));
        Assert.True(cacheIndex < events.FindIndex(e => e is ProviderEvent.TurnCompleted));
        var usage = ((ProviderEvent.CacheUsage)events[cacheIndex]).Value;
        Assert.Equal(1, usage.CacheWrite1h);
        Assert.Equal(0, usage.CacheWriteUnknownTtl);
        Assert.True(usage.Reported);
    }

    // ---- HelperAgents ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void WithIgnisHelperModel_moves_only_the_scout_to_the_helper_model_and_is_inert_when_unbound()
    {
        var agents = HydraPrompts.ClaudeAgents(new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true, WorkerModel = "claude-opus-main" });

        Assert.Equal(agents.CompactString, ClaudeSession.WithIgnisHelperModel(agents, "claude-opus-main").CompactString); // unbound

        IgnisHelperQuery? asked = null;
        using (IgnisSeams.HelperAgents.Override(query => { asked = query; return "claude-haiku-helper"; }))
        {
            var changed = ClaudeSession.WithIgnisHelperModel(agents, "claude-opus-main");
            Assert.Equal("claude-haiku-helper", changed[HydraPrompts.ScoutAgentName]?["model"]?.JSONValueString);
            Assert.Equal("claude-opus-main", changed[HydraPrompts.WorkerAgentName]?["model"]?.JSONValueString);
            // Everything else in the scout entry (prompt, tools, effort) is unchanged.
            Assert.Equal(agents[HydraPrompts.ScoutAgentName]?["tools"]?.CompactString, changed[HydraPrompts.ScoutAgentName]?["tools"]?.CompactString);
            Assert.Equal(agents[HydraPrompts.ScoutAgentName]?["prompt"]?.JSONValueString, changed[HydraPrompts.ScoutAgentName]?["prompt"]?.JSONValueString);
            Assert.Equal(agents[HydraPrompts.WorkerAgentName]?.CompactString, changed[HydraPrompts.WorkerAgentName]?.CompactString);
        }
        Assert.Equal(new IgnisHelperQuery(ProviderKind.Claude, "claude-opus-main"), asked);

        using (IgnisSeams.HelperAgents.Override(_ => null))
            Assert.Equal(agents.CompactString, ClaudeSession.WithIgnisHelperModel(agents, "claude-opus-main").CompactString);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Start_native_hydra_with_smart_helpers_passes_the_helper_model_on_the_scout_entry_only()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var helpers = IgnisSeams.HelperAgents.Override(_ => "claude-haiku-helper");
        var script = Script("ignis-helpers");
        var hydra = new HydraLaunch { HeadsProvider = ProviderKind.Claude, RunsNatively = true };
        runtime.Loop.Run(() => new ClaudeSession(Config(script, model: "claude-opus-main", hydra: hydra)).Start(), TimeSpan.FromSeconds(30));

        var args = script.Calls()[^1].Arguments;
        var agents = JSONValue.Parse(args[args.ToList().IndexOf("--agents") + 1])!;
        Assert.Equal("claude-haiku-helper", agents[HydraPrompts.ScoutAgentName]?["model"]?.JSONValueString);
        Assert.Null(agents[HydraPrompts.WorkerAgentName]?["model"]);
        Assert.Equal("claude-opus-main", args[args.ToList().IndexOf("--model") + 1]);
    }

    // ---- Compact(instructions) ----

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Compact_with_instructions_sends_them_after_the_compact_command_and_without_sends_the_bare_command()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var script = Script("ignis-compact");
        var session = new ClaudeSession(Config(script));
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(30));

        var instructions = ClaudeSession.KeepTurnsInstructions(12);
        runtime.Loop.Run(() => session.Compact(instructions), TimeSpan.FromSeconds(10));
        runtime.Loop.Run(() => session.Compact("  "), TimeSpan.FromSeconds(10));

        Assert.StartsWith("Keep the last 12 turns ", instructions, StringComparison.Ordinal);
        Assert.Contains("\"content\":" + new JSONValue.String("/compact " + instructions).CompactString + "}", script.CapturedLine(1, 1), StringComparison.Ordinal);
        Assert.Contains("\"message\":{\"role\":\"user\",\"content\":\"/compact\"}", script.CapturedLine(1, 2), StringComparison.Ordinal);
    }
}
