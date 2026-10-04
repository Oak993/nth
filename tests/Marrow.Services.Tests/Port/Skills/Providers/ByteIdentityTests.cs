using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
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

    private static JSONValue Object(Dictionary<string, JSONValue> value) => new JSONValue.Object(new ValueMap<string, JSONValue>(value, SwiftStringComparer.Instance));
    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
