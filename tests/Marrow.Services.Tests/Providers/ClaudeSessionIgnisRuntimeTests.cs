using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: Ignis runtime (OD-0134, ED-NEW-ignis-runtime). The cache-lifetime variables go on the CLI process's
// environment once per launch (so they cover helpers at every depth), every helper request's cache usage reaches Ignis
// once (two-level nesting fixture), and the overage hint of a rate_limit_event. The process is the offline recording
// transport: no CLI is spawned and no provider is reached.
public sealed class ClaudeSessionIgnisRuntimeTests
{
    private static readonly Guid Chat = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static SessionConfiguration Config(Dictionary<string, string>? environment = null) => new()
    {
        Provider = ProviderKind.Claude,
        Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
        WorkingDirectory = SwiftURL.Create("/tmp")!,
        Environment = new ValueMap<string, string>(environment ?? new Dictionary<string, string> { ["PATH"] = "user-bin" }),
        Model = "claude-opus-5-5",
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        IgnisThreadID = Chat,
    };

    private static IDisposable Help() => ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--agents"), [])));

    private static IgnisLifetimeChoice Choice(bool main, bool helpers) => new(
        new IgnisLifetimeVerdict(IgnisCacheBucket.Main, main ? IgnisLifetimeReason.Longer : IgnisLifetimeReason.AlreadyLong),
        new IgnisLifetimeVerdict(IgnisCacheBucket.Helpers, helpers ? IgnisLifetimeReason.Longer : IgnisLifetimeReason.TooFewGaps));

    private static JSONValue Usage(long input, long read, long written, long fiveMinutes, long oneHour) => JSONValue.FromDictionaryLiteral(
        ("input_tokens", input), ("cache_read_input_tokens", read), ("cache_creation_input_tokens", written), ("output_tokens", 7L),
        ("cache_creation", JSONValue.FromDictionaryLiteral(("ephemeral_5m_input_tokens", fiveMinutes), ("ephemeral_1h_input_tokens", oneHour))));

    private static JSONValue Assistant(string? parent, string id, JSONValue usage, string model = "claude-haiku-5-5")
    {
        var body = JSONValue.FromDictionaryLiteral(
            ("id", id), ("type", "message"), ("role", "assistant"), ("model", model),
            ("content", JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "working")))),
            ("usage", usage));
        return parent is null
            ? JSONValue.FromDictionaryLiteral(("type", "assistant"), ("message", body), ("parent_tool_use_id", new JSONValue.Null()))
            : JSONValue.FromDictionaryLiteral(("type", "assistant"), ("message", body), ("parent_tool_use_id", parent));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void WithIgnisCacheLifetimes_adds_only_the_chosen_variables_the_user_has_not_set()
    {
        var environment = new ValueMap<string, string>(new Dictionary<string, string> { ["PATH"] = "user-bin" });
        Assert.Same(environment, ClaudeSession.WithIgnisCacheLifetimes(environment, Config())); // unbound

        IgnisLifetimeQuery? asked = null;
        using (IgnisSeams.CacheLifetimes.Override(query => { asked = query; return Choice(main: true, helpers: true); }))
        {
            var changed = ClaudeSession.WithIgnisCacheLifetimes(environment, Config());
            Assert.Equal("1h", changed["CLAUDE_CODE_PROMPT_CACHE_TTL"]);
            Assert.Equal("1h", changed["CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL"]);
            Assert.Equal("user-bin", changed["PATH"]);
            Assert.Equal(3, changed.Count);

            // A variable already present in any letter case is the user's and stays as it is.
            var own = new ValueMap<string, string>(new Dictionary<string, string> { ["claude_code_subagent_prompt_cache_ttl"] = "5m" });
            var kept = ClaudeSession.WithIgnisCacheLifetimes(own, Config());
            Assert.Equal("5m", kept["claude_code_subagent_prompt_cache_ttl"]);
            Assert.Equal("1h", kept["CLAUDE_CODE_PROMPT_CACHE_TTL"]);
            Assert.Equal(2, kept.Count);
        }
        Assert.Equal(ProviderKind.Claude, asked!.Provider);
        Assert.Equal("claude-opus-5-5", asked.MainModel);
        Assert.Equal(Chat, asked.ThreadID);

        using (IgnisSeams.CacheLifetimes.Override(_ => Choice(main: false, helpers: false)))
            Assert.Same(environment, ClaudeSession.WithIgnisCacheLifetimes(environment, Config()));
        using (IgnisSeams.CacheLifetimes.Override(_ => null))
            Assert.Same(environment, ClaudeSession.WithIgnisCacheLifetimes(environment, Config()));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Start_sets_the_lifetimes_on_the_process_environment_once_and_helpers_never_ask_again()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = Help();
        var processes = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); processes.Add(p); return p; });
        var queries = new List<IgnisLifetimeQuery>();
        using var lifetimes = IgnisSeams.CacheLifetimes.Override(query => { queries.Add(query); return Choice(main: false, helpers: true); });
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config(), () => null) { OnEvent = events.Add };

        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        // Helpers at two depths run inside the same process: their requests carry the process's environment.
        runtime.OnMain(() =>
        {
            session.Handle(Assistant("toolu_level1", "msg_a", Usage(10, 0, 4_000, 4_000, 0)));
            session.Handle(Assistant("toolu_level2", "msg_b", Usage(10, 0, 3_000, 3_000, 0)));
        });

        var process = Assert.Single(processes);
        Assert.Equal("1h", process.Environment["CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL"]);
        Assert.False(process.Environment.ContainsKey("CLAUDE_CODE_PROMPT_CACHE_TTL"));
        Assert.StartsWith("user-bin", process.Environment["PATH"], StringComparison.Ordinal); // the rest of the environment is the launch's own
        var query = Assert.Single(queries);
        Assert.Equal(Chat, query.ThreadID);
        Assert.False(query.Environment.ContainsKey("CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL")); // asked before anything was added
        Assert.Equal(2, events.OfType<ProviderEvent.CacheUsage>().Count());
        session.Stop();
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Start_keeps_a_variable_the_user_set_and_adds_nothing_when_ignis_is_unbound()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = Help();
        var processes = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); processes.Add(p); return p; });

        var plain = new ClaudeSession(Config(), () => null);
        runtime.Loop.Run(() => plain.Start(), TimeSpan.FromSeconds(5));
        plain.Stop();

        using (IgnisSeams.CacheLifetimes.Override(_ => Choice(main: true, helpers: true)))
        {
            var own = new ClaudeSession(Config(new Dictionary<string, string> { ["PATH"] = "user-bin", ["CLAUDE_CODE_PROMPT_CACHE_TTL"] = "5m" }), () => null);
            runtime.Loop.Run(() => own.Start(), TimeSpan.FromSeconds(5));
            own.Stop();
        }

        Assert.Equal(2, processes.Count);
        Assert.DoesNotContain(processes[0].Environment.Keys, key => key.Contains("PROMPT_CACHE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("5m", processes[1].Environment["CLAUDE_CODE_PROMPT_CACHE_TTL"]);
        Assert.Equal("1h", processes[1].Environment["CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL"]);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Every_helper_request_at_every_depth_reaches_ignis_once_and_the_main_chat_only_through_its_result()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config()) { OnEvent = events.Add };
        session.MarkStarted();

        runtime.OnMain(() =>
        {
            // The lead's own assistant message: counted by the result, never here.
            session.Handle(Assistant(null, "msg_lead", Usage(5, 50_000, 1_000, 0, 1_000), model: "claude-opus-5-5"));
            // Level 1: the lead's Task call toolu_A spawned a helper; its message arrives once per content block.
            session.Handle(Assistant("toolu_A", "msg_a1", Usage(12, 0, 8_000, 8_000, 0)));
            session.Handle(Assistant("toolu_A", "msg_a1", Usage(12, 0, 8_000, 8_000, 0)));
            // Level 2: helper A's own Task call toolu_B spawned a helper of its own.
            session.Handle(Assistant("toolu_B", "msg_b1", Usage(3, 0, 6_000, 6_000, 0)));
            session.Handle(Assistant("toolu_B", "msg_b2", Usage(4, 6_000, 500, 500, 0)));
            session.Handle(Assistant("toolu_A", "msg_a2", Usage(6, 8_000, 900, 0, 900)));
            // A message without an id, or the CLI's synthetic model, is not a request.
            session.Handle(Assistant("toolu_A", "", Usage(1, 1, 1, 1, 0)));
            session.Handle(Assistant("toolu_A", "msg_synthetic", Usage(1, 1, 1, 1, 0), model: "<synthetic>"));
            session.HandleResult(JSONValue.FromDictionaryLiteral(("subtype", "success"), ("usage", Usage(5, 50_000, 1_000, 0, 1_000))));
        });

        var usages = events.OfType<ProviderEvent.CacheUsage>().Select(e => e.Value).ToList();
        Assert.Equal(["toolu_A", "toolu_B", "toolu_B", "toolu_A", null], usages.Select(u => u.AgentID));
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Claude, "claude-haiku-5-5", "toolu_A", 12, 0, 8_000, 0, 0, 7, 8_012, null, true), usages[0]);
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Claude, "claude-haiku-5-5", "toolu_B", 4, 6_000, 500, 0, 0, 7, 6_504, null, true), usages[2]);
        Assert.Equal(900, usages[3].CacheWrite1h); // the observed bucket wins over the assumed helper default (#84289)
        Assert.Null(usages[4].AgentID);
        Assert.Equal(1_000, usages[4].CacheWrite1h);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void HeadCacheUsageOf_reads_one_request_and_skips_what_is_not_one()
    {
        Assert.Equal(new IgnisTurnUsage(ProviderKind.Claude, "claude-haiku-5-5", "toolu_A", 2, 30, 0, 0, 40, 7, 72, null, true),
            ClaudeSession.HeadCacheUsageOf(Assistant("toolu_A", "m", JSONValue.FromDictionaryLiteral(
                ("input_tokens", 2L), ("cache_read_input_tokens", 30L), ("cache_creation_input_tokens", 40L), ("output_tokens", 7L))), "toolu_A"));
        // No cache fields: not reported; no usage: nothing.
        Assert.Null(ClaudeSession.HeadCacheUsageOf(Assistant("toolu_A", "m", JSONValue.FromDictionaryLiteral(("input_tokens", 2L))), "toolu_A"));
        Assert.Null(ClaudeSession.HeadCacheUsageOf(JSONValue.FromDictionaryLiteral(("type", "assistant"), ("message", JSONValue.FromDictionaryLiteral(("id", "m")))), "toolu_A"));
    }

    [Theory]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    [InlineData("{\"status\":\"allowed\",\"rateLimitType\":\"overage\"}", true)]
    [InlineData("{\"status\":\"allowed_warning\",\"rate_limit_type\":\"overage\"}", true)]
    [InlineData("{\"status\":\"allowed\",\"isUsingOverage\":true,\"rateLimitType\":\"five_hour\"}", true)]
    [InlineData("{\"status\":\"allowed\",\"isUsingOverage\":false,\"rateLimitType\":\"overage\"}", false)]
    [InlineData("{\"status\":\"allowed\",\"is_using_overage\":false}", false)]
    [InlineData("{\"status\":\"allowed\",\"rateLimitType\":\"seven_day\"}", null)]
    [InlineData("{\"status\":\"allowed\"}", null)]
    public void OverageHint_reads_the_rate_limit_fields(string info, bool? expected) =>
        Assert.Equal(expected, ClaudeSession.OverageHint(JSONValue.Parse(info)!));

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void An_allowed_rate_limit_event_on_overage_says_paid_credits_and_a_rejected_one_stays_a_usage_limit()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(Config()) { OnEvent = events.Add };
        session.MarkStarted();

        runtime.OnMain(() =>
        {
            session.Handle(JSONValue.Parse("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"rateLimitType\":\"seven_day\"}}")!);
            session.Handle(JSONValue.Parse("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed_warning\",\"rateLimitType\":\"overage\",\"isUsingOverage\":true}}")!);
            session.Handle(JSONValue.Parse("{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"rejected\",\"rateLimitType\":\"overage\"}}")!);
        });

        Assert.Equal([new ProviderEvent.CacheBilling(true)], events.OfType<ProviderEvent.CacheBilling>());
        Assert.Single(events.OfType<ProviderEvent.UsageLimit>());
    }
}
