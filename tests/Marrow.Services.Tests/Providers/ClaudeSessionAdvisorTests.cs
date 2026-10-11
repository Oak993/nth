using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: ED-NEW-ignisadvisor-1. The Claude advisor picker entry launches on its main model with `--advisor opus` only when the
// installed CLI can take it; every other model launches as before and starts no version probe. The process is the offline
// recording transport and the version probe a stand-in: no CLI is spawned and no provider is reached.
public sealed class ClaudeSessionAdvisorTests
{
    private const string Entry = "claude-sonnet-5-5+advisor-opus";

    private static SessionConfiguration Config(string? model, Dictionary<string, string>? environment = null) => new()
    {
        Provider = ProviderKind.Claude,
        Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
        WorkingDirectory = SwiftURL.Create("/tmp")!,
        Environment = new ValueMap<string, string>(environment ?? new Dictionary<string, string> { ["PATH"] = "user-bin" }),
        Model = model,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    // Launches one session and returns its arguments, environment and how often the version was probed.
    private static (string[] Arguments, Dictionary<string, string> Environment, int Probes) Launch(string? model, string? version, Dictionary<string, string>? environment = null, bool probeFails = false)
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--agents"), [])));
        var probes = 0;
        using var probe = ClaudeProviderRuntime.Version.Override((_, _) =>
        {
            probes++;
            if (probeFails) throw new IOException("no CLI");
            return Task.FromResult(version is null ? new ShellResult(1, [], Encoding.UTF8.GetBytes("unknown")) : new ShellResult(0, Encoding.UTF8.GetBytes(version + " (Claude Code)\n"), []));
        });
        var processes = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); processes.Add(p); return p; });
        var session = new ClaudeSession(Config(model, environment), () => null);
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        session.Stop();
        var process = Assert.Single(processes);
        return (process.Arguments, process.Environment, probes);
    }

    private static string? After(string[] arguments, string flag) => Array.IndexOf(arguments, flag) is var at and >= 0 && at + 1 < arguments.Length ? arguments[at + 1] : null;

    // The arguments without the random session id, for comparing two launches.
    private static string[] Stable(string[] arguments)
    {
        var at = Array.IndexOf(arguments, "--session-id");
        return at < 0 ? arguments : [.. arguments[..at], .. arguments[(at + 2)..]];
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignisadvisor-1")]
    public void The_advisor_entry_launches_on_sonnet_with_the_opus_advisor_when_the_cli_is_new_enough()
    {
        var (arguments, environment, probes) = Launch(Entry, "2.1.296");
        Assert.Equal("claude-sonnet-5-5", After(arguments, "--model"));
        Assert.Equal("opus", After(arguments, "--advisor"));
        Assert.Single(arguments, argument => argument == "--advisor");
        Assert.DoesNotContain(arguments, argument => argument.Contains("+advisor", StringComparison.Ordinal)); // the picker id never reaches the CLI
        Assert.False(environment.ContainsKey(ClaudeAdvisorModels.DisableVariable));
        Assert.Equal(1, probes);

        // At exactly the minimum version it still applies.
        Assert.Equal("opus", After(Launch(Entry, "2.1.284").Arguments, "--advisor"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignisadvisor-1")]
    public void A_cli_too_old_or_of_unknown_version_runs_the_entry_on_sonnet_without_the_flag()
    {
        foreach (var (version, fails) in new (string?, bool)[] { ("2.1.283", false), ("1.0.0", false), (null, false), ("Claude Code", false), (null, true) })
        {
            var (arguments, _, probes) = Launch(Entry, version, probeFails: fails);
            Assert.Equal("claude-sonnet-5-5", After(arguments, "--model"));
            Assert.DoesNotContain("--advisor", arguments);
            Assert.Equal(1, probes);
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignisadvisor-1")]
    public void The_users_own_off_switch_and_a_cloud_provider_keep_the_flag_off()
    {
        foreach (var variable in new[] { "CLAUDE_CODE_DISABLE_ADVISOR_TOOL", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY" })
        {
            var (arguments, environment, _) = Launch(Entry, "2.1.296", new Dictionary<string, string> { ["PATH"] = "user-bin", [variable] = "1" });
            Assert.Equal("claude-sonnet-5-5", After(arguments, "--model"));
            Assert.DoesNotContain("--advisor", arguments);
            Assert.Equal("1", environment[variable]); // the user's variable is passed on untouched
        }
        // "0" and "false" do not count as set.
        Assert.Equal("opus", After(Launch(Entry, "2.1.296", new Dictionary<string, string> { ["PATH"] = "user-bin", ["CLAUDE_CODE_DISABLE_ADVISOR_TOOL"] = "0" }).Arguments, "--advisor"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignisadvisor-1")]
    public void Every_other_model_launches_exactly_as_before_and_starts_no_version_probe()
    {
        foreach (var model in new string?[] { "claude-sonnet-5-5", "claude-opus-5-5", "opus", "default", null })
        {
            var (arguments, environment, probes) = Launch(model, "2.1.296");
            Assert.Equal(0, probes);
            Assert.DoesNotContain("--advisor", arguments);
            Assert.Equal(model is null or "default" ? null : model, After(arguments, "--model"));
            Assert.DoesNotContain(environment.Keys, key => key.Contains("ADVISOR", StringComparison.OrdinalIgnoreCase));
        }
        // The entry's launch is the plain Sonnet launch plus the flag and nothing else.
        var plain = Launch("claude-sonnet-5-5", "2.1.296").Arguments;
        var advised = Launch(Entry, "2.1.296").Arguments.ToList();
        var flag = advised.IndexOf("--advisor");
        advised.RemoveRange(flag, 2);
        Assert.Equal(Stable(plain), Stable([.. advised]));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignisadvisor-1")]
    public void CacheUsageOf_keeps_advisor_iterations_out_of_the_chat_and_hands_each_on_with_its_model()
    {
        JSONValue Iteration(string type, long input, long read, long output, string? model = null) => model is null
            ? JSONValue.FromDictionaryLiteral(("type", type), ("input_tokens", input), ("cache_read_input_tokens", read), ("cache_creation_input_tokens", 0L), ("output_tokens", output))
            : JSONValue.FromDictionaryLiteral(("type", type), ("model", model), ("input_tokens", input), ("cache_read_input_tokens", read), ("cache_creation_input_tokens", 0L), ("output_tokens", output));
        // The platform documentation's example: two executor requests around one advisor request.
        var result = JSONValue.FromDictionaryLiteral(("type", "result"), ("usage", JSONValue.FromDictionaryLiteral(
            ("input_tokens", 1760L), ("cache_read_input_tokens", 412L), ("cache_creation_input_tokens", 0L), ("output_tokens", 531L),
            ("iterations", JSONValue.FromArrayLiteral(
                Iteration("message", 412, 0, 89),
                Iteration("advisor_message", 823, 0, 1612, "claude-opus-5-5"),
                Iteration("message", 1348, 412, 442))))));

        var usage = ClaudeSession.CacheUsageOf(result, "claude-sonnet-5-5")!;
        Assert.Equal((1760L, 412L, 531L), (usage.UncachedInput, usage.CacheRead, usage.Output)); // the executor's own, as the top level says
        var call = Assert.Single(usage.AdvisorCalls);
        Assert.Equal(("claude-opus-5-5", 823L, 0L, 1612L), (call.Model, call.UncachedInput, call.CacheRead, call.Output));
        Assert.Equal(1348 + 412 + 442, ClaudeSession.ContextUsageOf(result)!.UsedTokens); // the chat's size is the last executor request

        // Without advisor entries nothing changes: every iteration is the chat's.
        var plain = JSONValue.FromDictionaryLiteral(("type", "result"), ("usage", JSONValue.FromDictionaryLiteral(
            ("input_tokens", 10L), ("output_tokens", 2L), ("iterations", JSONValue.FromArrayLiteral(Iteration("message", 4, 1, 1), Iteration("message", 6, 2, 1))))));
        var none = ClaudeSession.CacheUsageOf(plain, "claude-sonnet-5-5")!;
        Assert.Equal((10L, 3L, 2L), (none.UncachedInput, none.CacheRead, none.Output));
        Assert.Empty(none.AdvisorCalls);
    }
}
