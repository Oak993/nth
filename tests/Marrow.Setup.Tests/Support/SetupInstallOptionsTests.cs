using Marrow.Core.Support;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8, 8a and 10, unit U0 (ED-NEW-inst-u0-1). The setup's side of the two
// install files, through the copy of InstallOptions.cs compiled into nth Setup: the options file is right for an install
// with the shortcut on, with it off, an update, --silent and --no-desktop-shortcut; nothing that runs no Setup.exe has
// options; the path the setup passes in NTH_SETUP_OPTIONS is the one the hook finds; and the setup reads the hook's
// result back, a missing one as failed. Files live in a temporary folder standing in for %LOCALAPPDATA%.
public sealed class SetupInstallOptionsTests : IDisposable
{
    private readonly string localAppData = Path.Combine(Path.GetTempPath(), "nth-tests", "setup-options-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(localAppData)) Directory.Delete(localAppData, recursive: true);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("Install", "", false, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    [InlineData("Install", "", true, """{"version":1,"kind":"install","desktopShortcut":false}""")]
    [InlineData("Upgrade", "", false, """{"version":1,"kind":"update"}""")]
    [InlineData("Upgrade", "", true, """{"version":1,"kind":"update"}""")]
    [InlineData("Install", "--silent", false, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    [InlineData("Install", "-s", false, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    [InlineData("Install", "--silent --no-desktop-shortcut", false, """{"version":1,"kind":"install","desktopShortcut":false}""")]
    [InlineData("Install", "--no-desktop-shortcut --silent --installto D:\\nth", false, """{"version":1,"kind":"install","desktopShortcut":false}""")]
    [InlineData("Upgrade", "--silent --no-desktop-shortcut", false, """{"version":1,"kind":"update"}""")]
    [InlineData("Install", "--silent -- --no-desktop-shortcut", false, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    [InlineData("Install", "--silent --No-Desktop-Shortcut", false, """{"version":1,"kind":"install","desktopShortcut":true}""")]
    public void The_options_file_is_right_for_every_way_the_setup_is_run(string action, string arguments, bool userTurnedItOff, string expected)
    {
        var choice = InstallOptions.DesktopShortcutChoice(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (userTurnedItOff) choice = false; // the Welcome link or the Options switch
        var path = InstallOptions.DefaultOptionsPath(localAppData, "1.0.0");

        InstallOptions.ForAction(Enum.Parse<InstallerAction>(action), choice).Write(path);

        Assert.Equal(Path.Combine(localAppData, "nth Setup", "1.0.0", "install-options.json"), path);
        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(InstallerAction.AlreadyUpToDate)]
    [InlineData(InstallerAction.RefuseNewerInstalled)]
    public void An_outcome_that_runs_nothing_has_no_options(InstallerAction action) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallOptions.ForAction(action, desktopShortcut: true));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_path_the_setup_passes_is_the_one_the_hook_reads_and_the_result_comes_back_beside_it()
    {
        var staged = InstallOptions.DefaultOptionsPath(localAppData, "1.0.0");
        InstallOptions.ForInstall(desktopShortcut: true).Write(staged);

        // the hook, given the variable or not, finds the same file
        var hookPath = InstallOptions.OptionsPathFor(staged, localAppData, "1.0.0");
        Assert.Equal(staged, hookPath);
        Assert.Equal(staged, InstallOptions.OptionsPathFor(null, localAppData, "1.0.0"));
        Assert.Equal(InstallOptions.ForInstall(desktopShortcut: true), InstallOptions.Read(hookPath));

        // what the hook writes, the setup reads for Ready
        var result = new InstallResult(DesktopShortcutOutcome.Created, IconsHidden: true, StartAppIdRestamped: true);
        result.Write(InstallOptions.ResultPathFor(hookPath));
        Assert.Equal(result, InstallResult.ReadForReady(InstallOptions.ResultPathFor(staged)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_that_left_no_result_reads_as_failed_on_the_setup_side()
    {
        var ready = InstallResult.ReadForReady(InstallOptions.ResultPathFor(InstallOptions.DefaultOptionsPath(localAppData, "1.0.0")));

        Assert.Equal(DesktopShortcutOutcome.Failed, ready.DesktopShortcut);
        Assert.Null(ready.HResult);
    }
}
