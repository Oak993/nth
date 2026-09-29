using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;

namespace Marrow.App.Tests.Window;

// Only self-owned child environment dictionaries are inspected; the outer suite needs no switches.
public sealed class WindowManagerChildEnvironmentTests
{
    [Fact]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-10")]
    public void Disposable_process_and_native_children_disable_SDK_certificate_and_global_PATH_setup()
    {
        var nonce = Guid.NewGuid().ToString("N");
        var run = WindowManagerProcessProtocol.CreateRun(nonce);
        var process = WindowManagerProcessProtocol.ChildEnvironment(run, nonce);
        var request = WindowManagerNativeChildProtocol.CreateRequest(
            WindowManagerNativeChildProtocol.FactName(nameof(WindowManagerNativeTests.Settings_reopening_uses_the_actual_main_monitor_workarea_and_native_frame_DPI)));
        var native = WindowManagerNativeChildProtocol.ChildEnvironment(request, WindowManagerProcessProtocol.ResolveDotnetHost());
        foreach (var environment in new[] { process, native })
        {
            Assert.Equal("false", environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"]);
            Assert.Equal("false", environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]);
            Assert.Equal("1", environment["DOTNET_CLI_TELEMETRY_OPTOUT"]);
            Assert.DoesNotContain("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", environment.Keys);
            Assert.DoesNotContain(environment.Keys, key => key.Contains("API_KEY", StringComparison.OrdinalIgnoreCase));
        }
    }
}
