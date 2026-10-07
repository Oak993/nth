using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// OD-0116 / OD-0120 / OD-0122, ED-439, research claude-cli-standby-research.md section 1: Codex holds the next account's context by history injection,
// with no model turn. Every test spawns the compiled offline stand-in (R-TEST-8, R-TEST-10); no real CLI, network or model.
[Trait("Rule", "R-TEST-4")]
public sealed class CodexStandbyTests
{
    private const string Initialize = "{\"userAgent\":\"marrow/0.159.3 (Windows 10.0.26300; x86_64) test\",\"platformFamily\":\"windows\"}";

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static SessionConfiguration Config(CodexScript script, RuntimeMode runtimeMode = RuntimeMode.FullAccess) => new()
    {
        Provider = ProviderKind.Codex,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
        Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        Model = "gpt-5.1-codex",
        RuntimeMode = runtimeMode,
        InteractionMode = InteractionMode.Build,
    };

    private static CodexScript Script(string label, string initialize = Initialize)
    {
        var script = new CodexScript(label);
        script.SetResponse("initialize", initialize);
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"},\"model\":\"gpt-5.1-codex\"}");
        script.SetResponse("thread/inject_items", "{}");
        script.SetResponse("thread/delete", "{}");
        script.SetResponse("turn/start", "{\"turn\":{\"id\":\"u1\"}}");
        return script;
    }

    private static void Pump(TestRuntime runtime, Task task, int steps = 200)
    {
        for (var i = 0; i < steps && !task.IsCompleted; i++)
        {
            runtime.RunUntilIdle();
            if (task.IsCompleted) break;
            SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromMilliseconds(25));
        }
    }

    private static T Run<T>(TestRuntime runtime, Task<T> task)
    {
        Pump(runtime, task);
        return task.GetAwaiter().GetResult();
    }

    private static void Run(TestRuntime runtime, Task task)
    {
        Pump(runtime, task);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(TestRuntime runtime, Func<bool> condition, int steps = 200)
    {
        for (var i = 0; i < steps && !condition(); i++)
        {
            runtime.RunUntilIdle();
            if (condition()) break;
            SpinWait.SpinUntil(condition, TimeSpan.FromMilliseconds(25));
        }
    }

    private static IReadOnlyList<JSONValue> Wire(CodexScript script) =>
        Enumerable.Range(0, script.MessageCount(0)).Select(index => JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(script.CapturedLine(0, index)))!).ToArray();

    private static IReadOnlyList<string> Methods(CodexScript script) =>
        Wire(script).Select(line => line["method"]?.JSONValueString).OfType<string>().ToArray();

    [Fact]
    public void The_standby_loads_history_with_no_model_turn_and_the_handover_is_one_turn_with_the_users_real_mode()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = Script("standby-happy");
        var session = new CodexSession(Config(script));
        Assert.Equal("t1", Run(runtime, session.StartInjectedStandby(CancellationToken.None)));
        Run(runtime, session.InjectStandbyHistory("recorded context", CancellationToken.None));
        Run(runtime, session.InjectStandbyHistory("newer turns", CancellationToken.None));

        Assert.Equal(["initialize", "initialized", "thread/start", "thread/inject_items", "thread/inject_items"], Methods(script));
        var start = Wire(script)[2]["params"]!;
        Assert.Equal("read-only", start["sandbox"]!.JSONValueString);
        Assert.Equal("untrusted", start["approvalPolicy"]!.JSONValueString);
        var inject = Wire(script)[3]["params"]!;
        Assert.Equal("t1", inject["threadId"]!.JSONValueString);
        var item = inject["items"]!.JSONValueArray![0];
        Assert.Equal(("message", "developer"), (item["type"]!.JSONValueString, item["role"]!.JSONValueString));
        Assert.Equal(("input_text", "recorded context"), (item["content"]!.JSONValueArray![0]["type"]!.JSONValueString, item["content"]!.JSONValueArray![0]["text"]!.JSONValueString));
        Assert.Null(session.StandbyViolation);

        // The standby may not run a turn at all.
        var early = Assert.Throws<ProviderError>(() => Run(runtime, session.Send(Input("too early"))));
        Assert.Contains("waiting", early.ErrorDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("turn/start", Methods(script));

        Run(runtime, session.EndStandbyAsync(CancellationToken.None));
        Run(runtime, session.Send(Input("the user's message")));
        var methods = Methods(script);
        Assert.Equal("turn/start", methods[^1]);
        Assert.Single(methods, method => method == "turn/start");
        var turn = Wire(script)[^1]["params"]!;
        Assert.Equal("never", turn["approvalPolicy"]!.JSONValueString);
        Assert.Equal("dangerFullAccess", turn["sandboxPolicy"]!["type"]!.JSONValueString);
        Assert.Equal("gpt-5.4", turn["model"]!.JSONValueString);
        Assert.Equal("high", turn["effort"]!.JSONValueString);
        Assert.Equal("the user's message", turn["input"]!.JSONValueArray![0]["text"]!.JSONValueString);
    }

    private static TurnInput Input(string text) => new()
    {
        Text = text, Images = ValueList<Attachment>.Empty, Model = "gpt-5.4", Effort = "high",
        RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build,
    };

    [Theory]
    [InlineData("turn/started", "{\"method\":\"turn/started\",\"params\":{\"threadId\":\"t1\",\"turn\":{\"id\":\"x\"}}}")]
    [InlineData("item/started", "{\"method\":\"item/started\",\"params\":{\"threadId\":\"t1\",\"item\":{\"type\":\"agentMessage\",\"id\":\"i1\"}}}")]
    [InlineData("hook/started", "{\"method\":\"hook/started\",\"params\":{\"threadId\":\"t1\"}}")]
    [InlineData("item/commandExecution/requestApproval", "{\"id\":99,\"method\":\"item/commandExecution/requestApproval\",\"params\":{\"threadId\":\"t1\",\"command\":\"ls\"}}")]
    public void Any_turn_item_hook_or_approval_on_a_waiting_thread_stops_and_discards_it(string expected, string line)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = Script("standby-violation-" + expected.Replace('/', '-'));
        script.SetEmit("thread/inject_items", line);
        var session = new CodexSession(Config(script));
        var reported = new List<string>();
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        session.OnStandbyViolation = reported.Add;
        Run(runtime, session.StartInjectedStandby(CancellationToken.None));
        Run(runtime, session.InjectStandbyHistory("recorded context", CancellationToken.None));
        PumpUntil(runtime, () => session.StandbyViolation is not null);
        Assert.Equal(expected, session.StandbyViolation);
        Assert.Equal([expected], reported);
        PumpUntil(runtime, () => Methods(script).Contains("thread/delete"));
        Assert.Contains("thread/delete", Methods(script));
        Assert.Equal("t1", Wire(script).Last(w => w["method"]?.JSONValueString == "thread/delete")["params"]!["threadId"]!.JSONValueString);
        Assert.DoesNotContain("turn/start", Methods(script));
        PumpUntil(runtime, () => !session.IsRunning);
        Assert.False(session.IsRunning);
        // Nothing the waiting thread did reached the conversation.
        Assert.DoesNotContain(events, value => value is ProviderEvent.TurnStarted or ProviderEvent.Approval or ProviderEvent.ToolStarted);
    }

    [Theory]
    [InlineData("marrow/0.158.9 (Windows)")]
    [InlineData("junk")]
    [InlineData("")]
    public void A_CLI_older_than_the_verified_version_or_with_no_version_has_no_standby_and_starts_no_thread(string userAgent)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = Script("standby-old", "{\"userAgent\":\"" + userAgent + "\"}");
        var session = new CodexSession(Config(script));
        var task = session.StartInjectedStandby(CancellationToken.None);
        Pump(runtime, task);
        Assert.IsType<StandbyUnavailableException>(task.Exception!.GetBaseException());
        Assert.DoesNotContain("thread/start", Methods(script));
        PumpUntil(runtime, () => !session.IsRunning);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void A_CLI_whose_schema_does_not_list_thread_inject_items_has_no_standby()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(false));
        var script = Script("standby-no-schema");
        var session = new CodexSession(Config(script));
        var task = session.StartInjectedStandby(CancellationToken.None);
        Pump(runtime, task);
        Assert.IsType<StandbyUnavailableException>(task.Exception!.GetBaseException());
        Assert.DoesNotContain("thread/start", Methods(script));
    }

    [Fact]
    public void An_inject_error_discards_the_thread_and_reports_the_standby_unavailable()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = Script("standby-inject-error");
        script.SetError("thread/inject_items", "unknown variant `thread/inject_items`");
        var session = new CodexSession(Config(script));
        Run(runtime, session.StartInjectedStandby(CancellationToken.None));
        var task = session.InjectStandbyHistory("recorded context", CancellationToken.None);
        Pump(runtime, task);
        Assert.IsType<StandbyUnavailableException>(task.Exception!.GetBaseException());
        PumpUntil(runtime, () => Methods(script).Contains("thread/delete"));
        Assert.Contains("thread/delete", Methods(script));
        Assert.DoesNotContain("turn/start", Methods(script));
    }

    [Fact]
    public void Cancelling_the_standby_deletes_the_waiting_thread_stops_the_process_and_never_runs_a_turn()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var script = Script("standby-cancel");
        var session = new CodexSession(Config(script));
        Run(runtime, session.StartInjectedStandby(CancellationToken.None));
        Run(runtime, session.InjectStandbyHistory("recorded context", CancellationToken.None));
        Run(runtime, session.DiscardStandby());
        Assert.Equal("thread/delete", Methods(script)[^1]);
        PumpUntil(runtime, () => !session.IsRunning);
        Assert.False(session.IsRunning);
        Assert.DoesNotContain("turn/start", Methods(script));
    }

    [Fact]
    public void The_run_time_check_reads_the_version_once_per_executable_and_caches_the_verdict()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var probes = 0;
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => { probes++; return Task.FromResult(true); });
        var script = Script("standby-support");
        Assert.True(Run(runtime, new CodexSession(Config(script)).SupportsStandbyInjection(CancellationToken.None)));
        Assert.True(Run(runtime, new CodexSession(Config(script)).SupportsStandbyInjection(CancellationToken.None)));
        Assert.Equal(1, probes);
        // One app-server was started to read the version; the second check, and the standby itself, reuse the verdict for this executable.
        Assert.Single(script.Calls());
        Assert.DoesNotContain("thread/start", Methods(script));
    }

    [Fact]
    public void The_run_time_check_is_false_for_an_old_CLI_and_for_a_CLI_that_will_not_start()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var probe = CodexHistoryInjection.SchemaProbe.Override((_, _) => Task.FromResult(true));
        var old = Script("standby-support-old", "{\"userAgent\":\"marrow/0.150.0 (Windows)\"}");
        Assert.False(Run(runtime, new CodexSession(Config(old)).SupportsStandbyInjection(CancellationToken.None)));
        var broken = new CodexScript("standby-support-broken");
        broken.SetError("initialize", "refused");
        Assert.False(Run(runtime, new CodexSession(Config(broken)).SupportsStandbyInjection(CancellationToken.None)));
    }

    [Theory]
    [InlineData("marrow/0.159.3 (Windows)", true)]
    [InlineData("codex_cli_rs/0.160.0 (Mac OS 14; arm64)", true)]
    [InlineData("marrow/1.2.0", true)]
    [InlineData("marrow/0.159.2 (Windows)", false)]
    [InlineData("marrow/0.99.99", false)]
    [InlineData("no version here", false)]
    [InlineData("", false)]
    public void The_user_agent_version_gate_requires_at_least_0_159_3(string userAgent, bool expected) =>
        Assert.Equal(expected, CodexHistoryInjection.VersionSupportsInjection(userAgent));

    [Fact]
    public void The_schema_check_requires_thread_inject_items_in_the_client_requests()
    {
        Assert.True(CodexHistoryInjection.SchemaListsInjection("{\"oneOf\":[{\"properties\":{\"method\":{\"enum\":[\"thread/inject_items\"]}}}]}"));
        Assert.False(CodexHistoryInjection.SchemaListsInjection("{\"oneOf\":[{\"properties\":{\"method\":{\"enum\":[\"thread/start\"]}}}]}"));
        Assert.False(CodexHistoryInjection.SchemaListsInjection(""));
    }
}
