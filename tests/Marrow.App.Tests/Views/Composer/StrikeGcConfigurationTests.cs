using System.Text.Json;

namespace Marrow.App.Tests.Views.Composer;

// ST-0: the test host must read its own GC setting, not Marrow.App's (RISKS R-03).
public sealed class StrikeGcConfigurationTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Shipping_app_uses_background_workstation_gc_and_test_host_keeps_its_own_policy()
    {
        foreach (var name in new[] { "Marrow.App", "Marrow.App.Tests" })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json")));
            var properties = json.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
            Assert.True(properties.TryGetProperty("System.GC.Server", out var server), name);
            // T44 explicitly supersedes ST-0's desktop server-GC choice. The host stays independent.
            Assert.Equal(name == "Marrow.App.Tests", server.GetBoolean());
            Assert.True(properties.GetProperty("System.GC.Concurrent").GetBoolean(), name);
        }
    }
}
