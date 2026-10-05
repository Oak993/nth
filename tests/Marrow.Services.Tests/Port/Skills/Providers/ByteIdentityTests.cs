using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using System.Text;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class ByteIdentityTests
{
    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-11")]
    public void Empty_Copilot_session_parameters_are_byte_identical_to_the_existing_provider_code()
    {
        using var runtime = new TestRuntime();
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        foreach (var hydra in new HydraLaunch?[] { null, CopilotSessionHarness.Hydra(), CopilotSessionHarness.Hydra(false) })
        foreach (var antiSlop in new[] { false, true })
        {
            var config = CopilotSessionHarness.Config(model: "fixture model", effort: "high", runtimeMode: mode, hydra: hydra, antiSlopEnabled: antiSlop);
            var before = new BaselineWire(config).CopilotParameters();
            var after = (Dictionary<string, JSONValue>)CopilotSessionHarness.Invoke(new CopilotSession(config), "SessionParameters")!;
            Assert.Equal(Object(before).Data(), Object(after).Data());
            Assert.DoesNotContain("skillDirectories", after.Keys);
        }
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-11")]
    public void Empty_ACP_argv_and_initialize_are_byte_identical_to_the_existing_provider_code()
    {
        using var runtime = new TestRuntime();
        foreach (var provider in new[] { ProviderKind.Cursor, ProviderKind.Grok, ProviderKind.Devin, ProviderKind.Opencode, ProviderKind.OpencodeGo })
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            var config = CopilotSessionHarness.Config(runtimeMode: mode) with { Provider = provider };
            var before = new BaselineWire(config);
            var after = new ACPSession(config);
            Assert.Equal(before.LaunchArguments, Property<ValueList<string>>(after, "LaunchArguments"));
            Assert.Equal(before.InitializeParams.Data(), Property<JSONValue>(after, "InitializeParams").Data());
        }
    }

    [Fact, Trait("Feature", "SK-11c"), Trait("Rule", "DL-11")]
    public void Copilot_argv_is_byte_identical_and_PATH_is_the_only_child_environment_change()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var argv = (IReadOnlyList<string>)typeof(CopilotSession).GetMethod("LaunchArguments", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join("\0", BaselineWire.CopilotArguments())), Encoding.UTF8.GetBytes(string.Join("\0", argv)));
        foreach (var hasAlias in new[] { false, true })
        {
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = hasAlias ? string.Join(Path.PathSeparator, "user-python", "/profile/Microsoft/WindowsApps", "tail") : "user-python",
                ["fixture"] = "quotes \" Unicode \u03b1 \nline", ["PYTHONUTF8"] = "caller-value", ["NODE_PATH"] = "caller-node"
            };
            var method = typeof(CopilotSession).GetMethod("ChildEnvironment", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var child = (IReadOnlyDictionary<string, string>)method.Invoke(null, [environment])!;
            var bin = Path.Combine(storage.Root, "addons", "bin");
            Assert.Equal(hasAlias ? string.Join(Path.PathSeparator, "user-python", bin, "/profile/Microsoft/WindowsApps", "tail") : string.Join(Path.PathSeparator, "user-python", bin), child["PATH"]);
            Assert.Equal(EnvironmentBytes(environment, true), EnvironmentBytes(child, true));
            Assert.True(Directory.Exists(bin));
            foreach (var provider in new[] { ProviderKind.Cursor, ProviderKind.Grok, ProviderKind.Devin, ProviderKind.Opencode, ProviderKind.OpencodeGo })
            {
                var config = CopilotSessionHarness.Config() with { Provider = provider, Environment = new ValueMap<string, string>(environment) };
                var acpChild = Property<IReadOnlyDictionary<string, string>>(new ACPSession(config), "ChildEnvironment");
                Assert.Equal(EnvironmentBytes(child, false), EnvironmentBytes(acpChild, false));
            }
            Assert.Equal(hasAlias ? string.Join(Path.PathSeparator, "user-python", "/profile/Microsoft/WindowsApps", "tail") : "user-python", environment["PATH"]);
        }
    }

    [Theory, InlineData(false), InlineData(true), Trait("Feature", "SK-11c"), Trait("Rule", "DL-11")]
    public void Empty_Copilot_create_and_resume_RPC_bytes_equal_the_existing_code(bool resume)
    {
        using var runtime = new TestRuntime();
        var config = CopilotSessionHarness.Config(resumeID: resume ? "s-1" : null);
        var before = new List<byte[]>();
        var after = new List<byte[]>();
        _ = runtime.Loop.Run(() => new BaselineWire(config).OpenCopilot("s-1", (method, parameters) => Capture(before, method, parameters)), TimeSpan.FromSeconds(10));
        var open = typeof(CopilotSession).GetMethod("OpenSkillsSession", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = runtime.Loop.Run(() => (Task<JSONValue>)open.Invoke(new CopilotSession(config), ["s-1", (Func<string, JSONValue, Task<JSONValue>>)((method, parameters) => Capture(after, method, parameters))])!, TimeSpan.FromSeconds(10));
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);
    }

    [Theory, InlineData(false, false), InlineData(true, false), InlineData(true, true), Trait("Feature", "SK-11c"), Trait("Rule", "DL-11")]
    public void Empty_ACP_new_load_and_failed_load_RPC_bytes_equal_the_existing_code(bool resume, bool failedLoad)
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        foreach (var http in new[] { false, true })
        {
            var config = CopilotSessionHarness.Config(resumeID: resume ? "s-1" : null) with { Provider = ProviderKind.Cursor };
            var before = new List<byte[]>();
            var after = new List<byte[]>();
            Task<JSONValue> Reply(List<byte[]> target, string method, JSONValue parameters)
            {
                _ = Capture(target, method, parameters);
                return method == "session/load" && failedLoad ? Task.FromException<JSONValue>(new RPCError(-32000, "Fixture load failed")) : Task.FromResult(JSONValue.Parse("{\"sessionId\":\"s-1\"}")!);
            }
            var baseline = new BaselineWire(config) { canLoadSessions = true, supportsHTTPMCP = http };
            _ = runtime.Loop.Run(() => baseline.OpenACP((method, parameters) => Reply(before, method, parameters)), TimeSpan.FromSeconds(10));
            var session = new ACPSession(config);
            typeof(ACPSession).GetField("canLoadSessions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, true);
            typeof(ACPSession).GetField("supportsHTTPMCP", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, http);
            var open = typeof(ACPSession).GetMethod("OpenSessionCore", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(open);
            _ = runtime.Loop.Run(() => (Task<JSONValue>)open.Invoke(session, [(Func<string, JSONValue, Task<JSONValue>>)((method, parameters) => Reply(after, method, parameters))])!, TimeSpan.FromSeconds(10));
            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);
        }
    }

    private static Task<JSONValue> Capture(List<byte[]> target, string method, JSONValue parameters)
    {
        // JSONRPCConnection.Write's existing sendsVersion=true envelope, deterministic request IDs.
        target.Add(JSONValue.FromDictionaryLiteral(("id", (long)target.Count + 1), ("method", method), ("params", parameters), ("jsonrpc", "2.0")).Data());
        return Task.FromResult(JSONValue.Parse("{}")!);
    }
    private static byte[] EnvironmentBytes(IReadOnlyDictionary<string, string> environment, bool omitPath) =>
        Encoding.UTF8.GetBytes(string.Join("\0", environment.Where(p => !omitPath || p.Key != "PATH").OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));

    private static JSONValue Object(Dictionary<string, JSONValue> value) => new JSONValue.Object(new ValueMap<string, JSONValue>(value, SwiftStringComparer.Instance));
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
