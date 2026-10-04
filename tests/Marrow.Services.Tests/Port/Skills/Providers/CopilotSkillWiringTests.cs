using System.Reflection;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;
public sealed class CopilotSkillWiringTests
{
    [Theory, InlineData(false), InlineData(true), Trait("Feature", "SK-11c"), Trait("Rule", "WU-8")]
    public void Create_and_resume_send_the_verified_generation_root_without_loading_instruction_text(bool resume)
    {
        using var runtime = new TestRuntime();
        using var fixture = new GenerationFixture();
        var item = fixture.Add("O3", "documents");
        var generations = fixture.Open();
        using var generation = generations.Start("copilot", "c1", [item], fixture.Switches((item, true)), fixture.Budget);
        var session = Make(fixture, generation, resume);
        var calls = new List<(string Method, JSONValue Params)>();
        _ = runtime.Loop.Run(() => Open(session, "s-1", (method, parameters) => { calls.Add((method, parameters)); return Task.FromResult(JSONValue.Parse("{}")!); }), TimeSpan.FromSeconds(10));
        var call = Assert.Single(calls);
        Assert.Equal(resume ? "session.resume" : "session.create", call.Method);
        Assert.Equal(new[] { generation.Folder }, call.Params["skillDirectories"]!.JSONValueArray!.Select(p => p.JSONValueString!));
        Assert.DoesNotContain("BODY SENTINEL", call.Params.CompactString, StringComparison.Ordinal);
        Assert.DoesNotContain("disabledSkills", call.Params.JSONValueObject!.Keys);
    }

    [Theory, InlineData(false, -32602L, "Invalid params"), InlineData(true, -32602L, "Invalid params"), InlineData(false, -32000L, "Unsupported skillDirectories"), InlineData(true, -32000L, "Unsupported skillDirectories"), Trait("Feature", "SK-11c"), Trait("Rule", "WU-8")]
    public void Rejected_key_retries_once_and_is_omitted_on_the_next_session_for_the_same_executable(bool resume, long code, string message)
    {
        using var runtime = new TestRuntime();
        using var fixture = new GenerationFixture();
        var item = fixture.Add("O3", "documents");
        var generations = fixture.Open();
        using var generation = generations.Start("copilot", "c1", [item], fixture.Switches((item, true)), fixture.Budget);
        var session = Make(fixture, generation, resume);
        var calls = new List<(string Method, JSONValue Params)>();
        Task<JSONValue> Request(string method, JSONValue parameters)
        {
            calls.Add((method, parameters));
            return calls.Count == 1 ? Task.FromException<JSONValue>(new RPCError(code, message)) : Task.FromResult(JSONValue.Parse("{}")!);
        }
        _ = runtime.Loop.Run(() => Open(session, "s-1", Request), TimeSpan.FromSeconds(10));
        Assert.Equal(2, calls.Count);
        Assert.Equal(calls[0].Method, calls[1].Method);
        var first = calls[0].Params.JSONValueObject!.Where(p => p.Key != "skillDirectories");
        Assert.Equal(new JSONValue.Object(new ValueMap<string, JSONValue>(first, SwiftStringComparer.Instance)).Data(), calls[1].Params.Data());
        calls.Clear();
        var next = Make(fixture, generation, !resume);
        _ = runtime.Loop.Run(() => Open(next, "s-2", (method, parameters) => { calls.Add((method, parameters)); return Task.FromResult(JSONValue.Parse("{}")!); }), TimeSpan.FromSeconds(10));
        Assert.Null(Assert.Single(calls).Params["skillDirectories"]);
        var path = Path.Combine(fixture.Paths.Root, "fake copilot.exe");
        File.AppendAllText(path, "changed-size");
        calls.Clear();
        _ = runtime.Loop.Run(() => Open(Make(fixture, generation, resume), "s-3", (method, parameters) => { calls.Add((method, parameters)); return Task.FromResult(JSONValue.Parse("{}")!); }), TimeSpan.FromSeconds(10));
        Assert.NotNull(Assert.Single(calls).Params["skillDirectories"]);
    }

    [Theory, InlineData(-32000L, "authentication failed"), InlineData(-32603L, "internal error"), Trait("Feature", "SK-11c"), Trait("Rule", "WU-8")]
    public void Unrelated_errors_are_never_retried(long code, string message)
    {
        using var runtime = new TestRuntime();
        using var fixture = new GenerationFixture();
        var item = fixture.Add("O3", "documents");
        var generations = fixture.Open();
        using var generation = generations.Start("copilot", "c1", [item], fixture.Switches((item, true)), fixture.Budget);
        var count = 0;
        var error = new RPCError(code, message);
        Assert.Same(error, Assert.Throws<RPCError>(() => runtime.Loop.Run(() => Open(Make(fixture, generation, false), "s-1", (_, _) => { count++; return Task.FromException<JSONValue>(error); }), TimeSpan.FromSeconds(10))));
        Assert.Equal(1, count);
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "WU-8")]
    public void An_invalid_params_error_without_any_installed_skills_is_not_retried()
    {
        using var runtime = new TestRuntime();
        var count = 0;
        Assert.Throws<RPCError>(() => runtime.Loop.Run(() => Open(new CopilotSession(CopilotSessionHarness.Config()), "s-1", (_, _) => { count++; return Task.FromException<JSONValue>(new RPCError(-32602, "Invalid params")); }), TimeSpan.FromSeconds(10)));
        Assert.Equal(1, count);
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "WU-8")]
    public void A_damaged_generation_is_not_sent_to_the_provider()
    {
        using var runtime = new TestRuntime();
        using var fixture = new GenerationFixture();
        var item = fixture.Add("O3", "documents");
        var generations = fixture.Open();
        using var generation = generations.Start("copilot", "c1", [item], fixture.Switches((item, true)), fixture.Budget);
        File.WriteAllText(Path.Combine(generation.Folder, "extra.txt"), "tamper");
        JSONValue? sent = null;
        _ = runtime.Loop.Run(() => Open(Make(fixture, generation, false), "s-1", (_, parameters) => { sent = parameters; return Task.FromResult(JSONValue.Parse("{}")!); }), TimeSpan.FromSeconds(10));
        Assert.Null(sent!["skillDirectories"]);
    }

    private static CopilotSession Make(GenerationFixture fixture, Generation generation, bool resume)
    {
        var path = Path.Combine(fixture.Paths.Root, "fake copilot.exe");
        if (!File.Exists(path)) File.WriteAllText(path, "fixture executable identity only; never launched");
        var config = CopilotSessionHarness.Config(resumeID: resume ? "s-1" : null) with { Executable = CopilotSessionHarness.FileUrl(path) };
        var constructor = typeof(CopilotSession).GetConstructor([typeof(SessionConfiguration), typeof(Generation)]);
        Assert.NotNull(constructor);
        return (CopilotSession)constructor.Invoke([config, generation]);
    }
    private static Task<JSONValue> Open(CopilotSession session, string id, Func<string, JSONValue, Task<JSONValue>> request)
    {
        var method = typeof(CopilotSession).GetMethod("OpenSkillsSession", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<JSONValue>)method.Invoke(session, [id, request])!;
    }
}
