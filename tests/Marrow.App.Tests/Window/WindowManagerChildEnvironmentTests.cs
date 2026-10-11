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
            WindowManagerNativeChildProtocol.FactName(nameof(WindowManagerNativeTests.Reopening_Settings_activates_the_native_main_window_its_panel_is_in_and_leaves_that_window_where_it_is)));
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

    [Fact]
    [Trait("Rule", "R-TEST-9")]
    [Trait("Rule", "R-TEST-10")]
    public void The_native_childs_profile_is_inside_its_run_directory_and_has_the_shell_known_folders_a_dialog_opens_at()
    {
        var request = WindowManagerNativeChildProtocol.CreateRequest(
            WindowManagerNativeChildProtocol.FactName(nameof(WindowManagerNativeTests.Reopening_Settings_activates_the_native_main_window_its_panel_is_in_and_leaves_that_window_where_it_is)));
        var native = WindowManagerNativeChildProtocol.ChildEnvironment(request, WindowManagerProcessProtocol.ResolveDotnetHost());
        var home = Path.GetFullPath(native["USERPROFILE"]);
        Assert.Equal(home, Path.GetFullPath(native["HOME"]));
        Assert.StartsWith(Path.TrimEndingDirectorySeparator(request.RunDirectory) + Path.DirectorySeparatorChar, home, StringComparison.OrdinalIgnoreCase); // never the real profile
        // A shell dialog (the folder chooser, a file picker) opens at the profile's Desktop; with no such folder the shell puts a
        // "Location is not available" box on top of it.
        foreach (var name in new[] { "Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos" })
            Assert.True(Directory.Exists(Path.Combine(home, name)), name + " is missing from the child's profile");
    }
}
