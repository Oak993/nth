using Marrow.Services.Port.Updates;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Updates;

[Trait("Feature", "UE-3")]
[Trait("Rule", "R-TEST-4")]
public sealed class DevToolProcessBoundaryTests
{
    [Theory]
    [InlineData("07-git-upgrade")]
    [InlineData("19-node-upgrade")]
    [InlineData("28-python314-upgrade")]
    [InlineData("32-python313-upgrade")]
    [InlineData("37-office-upgrade")]
    public void Production_binding_accepts_the_recorded_argv_without_starting_a_process(string fixture) => Assert.NotEmpty(WindowsDevToolProcess.Validate(TrialHFixtures.Command(fixture)));

    [Theory]
    [InlineData("--all")]
    [InlineData("--uninstall-previous")]
    [InlineData("--force")]
    [InlineData("--allow-reboot")]
    public void Broader_winget_operations_are_rejected_before_execution(string flag) => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(new("winget", ["upgrade", flag])));

    [Fact]
    public void Arbitrary_executables_are_refused_without_process_creation() => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(new("powershell.exe", ["-Command", "anything"])));

    [Fact]
    public void A_python_selector_that_could_trigger_the_install_manager_is_refused() => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(new("py", ["-3.15", "--version"])));

    [Fact]
    public void A_Store_alias_is_never_used_as_a_python_detection_executable() => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(new("C:\\Users\\Public\\AppData\\Local\\Microsoft\\WindowsApps\\python.exe", ["--version"])));

    [Fact]
    public void A_Store_alias_using_forward_slashes_is_also_refused() => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(new("C:/Users/Public/AppData/Local/Microsoft/WindowsApps/python.exe", ["--version"])));

    [Fact]
    public void Untrialled_GitHub_CLI_upgrade_is_refused_by_the_process_binding_too() => Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(WingetUpdater.UpgradeCommand("GitHub.cli")));

    [Fact]
    public void Office_force_shutdown_or_version_pinning_is_refused()
    {
        var command = TrialHFixtures.Command("37-office-upgrade");
        Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(command with { Arguments = command.Arguments.Append("forceappshutdown=true").ToArray() }));
        Assert.Throws<InvalidOperationException>(() => WindowsDevToolProcess.Validate(command with { Arguments = command.Arguments.Append("updatetoversion=16.0.1.0").ToArray() }));
    }
}
