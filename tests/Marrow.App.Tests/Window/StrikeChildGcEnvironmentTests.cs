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
            WindowManagerNativeChildProtocol.FactName(nameof(WindowManagerNativeTests.Reopening_Settings_activates_the_native_main_window_its_panel_is_in_and_leaves_that_window_where_it_is)));
        var values = WindowManagerNativeChildProtocol.ChildEnvironment(request, WindowManagerProcessProtocol.ResolveDotnetHost());
        Assert.True(values.TryGetValue("DOTNET_gcServer", out var server));
        Assert.Equal("1", server);
        Assert.Equal("1", values["DOTNET_gcConcurrent"]);
    }
}
