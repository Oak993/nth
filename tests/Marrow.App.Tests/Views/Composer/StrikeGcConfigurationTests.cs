using System.Text.Json;

namespace Marrow.App.Tests.Views.Composer;

// ST-0: the test host must read its own GC setting, not Marrow.App's (RISKS R-03).
public sealed class StrikeGcConfigurationTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Both_the_app_and_test_host_configure_background_server_gc()
    {
        foreach (var name in new[] { "Marrow.App", "Marrow.App.Tests" })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name + ".runtimeconfig.json")));
            var properties = json.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
            Assert.True(properties.TryGetProperty("System.GC.Server", out var server), name);
            Assert.True(server.GetBoolean(), name);
            Assert.True(properties.GetProperty("System.GC.Concurrent").GetBoolean(), name);
        }
    }
}
