using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;

namespace Marrow.App.Tests.Window;

public sealed class StrikeChildGcEnvironmentTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Measurement_children_explicitly_enable_server_gc_before_the_runtime_starts()
    {
        var request = WindowManagerNativeChildProtocol.CreateRequest(
            WindowManagerNativeChildProtocol.FactName(nameof(WindowManagerNativeTests.Settings_reopening_uses_the_actual_main_monitor_workarea_and_native_frame_DPI)));
        var values = WindowManagerNativeChildProtocol.ChildEnvironment(request, WindowManagerProcessProtocol.ResolveDotnetHost());
        Assert.True(values.TryGetValue("DOTNET_gcServer", out var server));
        Assert.Equal("1", server);
        Assert.Equal("1", values["DOTNET_gcConcurrent"]);
    }
}
