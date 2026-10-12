using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8, item 1, and 8a's row "After Setup.exe exits 0"; VELOPACK_CHECK.md
// checks 5, 8 and 14; unit U5 (ED-NEW-inst-u5-1). What nth Setup does once Velopack's Setup.exe has exited 0, on
// FakeShellLinks and FakeUserRegistry through the setup's own linked copies: 1.0.0's Start link moved if the hook did not,
// Start re-stamped, a desktop shortcut nth made aligned (never created), the uninstall entry re-pointed with the measured
// size, and the re-stamp recorded in install-result.json (in a temporary folder). It never throws.
public sealed class AfterSetupExitTests : IDisposable
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Current = Root + @"\current";
    private const string CurrentExe = Current + @"\nth.exe";
    private const string EnvCurrent = @"%LOCALAPPDATA%\Programs\nth\current";
    private const string EnvCurrentExe = EnvCurrent + @"\nth.exe";
    private static readonly string OldStartLink = FakeShellLinks.Programs + @"\nth\nth.lnk";

    private readonly string temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (IOException) { } // a temporary folder; Windows cleans %TEMP%
    }

    private string ResultPath => Path.Combine(temp, InstallOptions.ResultFileName);

    private static FakeShellLinks Machine(string? startAumid = "velopack.NthApp", bool start = true)
    {
        var shell = new FakeShellLinks();
        shell.AddFile(CurrentExe);
        shell.AddFile(Root + @"\Update.exe");
        if (start) shell.Seed(FakeShellLinks.StartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0, AppUserModelId: startAumid));
        return shell;
    }

    private static FakeUserRegistry Installed()
    {
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(Root);
        return registry;
    }

    private static ShellLink Made(string target = EnvCurrentExe) =>
        new(target, "", EnvCurrent, EnvCurrentExe, 0, ShellLinkKeys.Description, AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

    private AfterSetupExitReport Run(FakeShellLinks shell, FakeUserRegistry registry, string? resultPath = null, bool uninstaller = true) =>
        AfterSetupExit.Run(shell, registry, Root, resultPath, _ => 430_080, path => uninstaller && path == Root + @"\nth Uninstall.exe");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_a_fresh_install_Start_is_restamped_the_key_repointed_and_the_result_records_it()
    {
        var shell = Machine();
        var registry = Installed();
        new InstallResult(DesktopShortcutOutcome.Created).Write(ResultPath); // the hook ran before Velopack's Start pass

        var report = Run(shell, registry, ResultPath);

        Assert.True(report.StartPresent);
        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.None), report.OldStart);
        Assert.Equal(new StartLinkResult(Found: true, Restamped: true), report.Start);
        Assert.Equal(new UninstallRepointResult(true, true, 3, true), report.UninstallKey);
        Assert.True(report.ResultUpdated);
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created, StartAppIdRestamped: true), InstallResult.Read(ResultPath));
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId);
        Assert.Equal("\"" + Root + "\\nth Uninstall.exe\" --uninstall", registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));
        Assert.Equal("\"" + Root + "\\nth Uninstall.exe\" --uninstall --silent", registry.GetValue(UninstallEntry.KeyPath, "QuietUninstallString"));
        Assert.Equal(430_080, registry.GetValue(UninstallEntry.KeyPath, "EstimatedSize"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_run_changes_nothing_and_leaves_the_result_file_as_it_is()
    {
        var shell = Machine();
        var registry = Installed();
        new InstallResult(DesktopShortcutOutcome.Off).Write(ResultPath);
        Run(shell, registry, ResultPath);
        var bytes = File.ReadAllBytes(ResultPath);
        shell.ResetCounters();
        var writes = registry.Writes;

        var report = Run(shell, registry, ResultPath);

        Assert.Equal(new StartLinkResult(Found: true), report.Start);
        Assert.Equal(0, report.UninstallKey!.Writes);
        Assert.False(report.ResultUpdated);
        Assert.Equal(0, shell.Changes);
        Assert.Equal(writes, registry.Writes);
        Assert.Equal(bytes, File.ReadAllBytes(ResultPath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_1_0_0_upgrade_whose_hook_was_killed_still_ends_with_one_Start_link_into_the_root()
    {
        var shell = Machine(start: false);
        shell.Seed(OldStartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0, AppUserModelId: "velopack.NthApp"));

        var report = Run(shell, Installed());

        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.Moved, FolderRemoved: true), report.OldStart);
        Assert.True(report.StartPresent);
        Assert.True(report.Start.Restamped);
        Assert.Equal(new[] { FakeShellLinks.StartLink }, shell.LinksIn(FakeShellLinks.Programs));
        Assert.False(shell.FolderExists(FakeShellLinks.Programs + @"\nth"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_Start_link_is_reported_and_nothing_is_created()
    {
        var shell = Machine(start: false);

        var report = Run(shell, Installed());

        Assert.False(report.StartPresent);
        Assert.Equal(new StartLinkResult(Found: false), report.Start);
        Assert.Empty(shell.LinksIn(FakeShellLinks.Programs));
        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_desktop_shortcut_nth_made_is_aligned_and_none_is_ever_created()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.OneDriveDesktop + @"\My nth.lnk", Made(Root + @"\Marrow.App.exe"));

        var report = Run(shell, Installed());

        Assert.Equal(new ShortcutChangeResult(1, 1, Capped: false, Busy: false, Failed: 0), report.Desktop);
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.OneDriveDesktop + @"\My nth.lnk"));
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.WriteLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_restamp_without_a_result_file_or_path_writes_no_result()
    {
        var withoutPath = Run(Machine(), Installed());
        var withoutFile = Run(Machine(), Installed(), ResultPath);

        Assert.True(withoutPath.Start.Restamped);
        Assert.False(withoutPath.ResultUpdated);
        Assert.True(withoutFile.Start.Restamped);
        Assert.False(withoutFile.ResultUpdated);
        Assert.False(File.Exists(ResultPath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_result_that_already_records_the_restamp_is_not_rewritten()
    {
        new InstallResult(DesktopShortcutOutcome.Kept, StartAppIdRestamped: true).Write(ResultPath);
        var bytes = File.ReadAllBytes(ResultPath);

        var report = Run(Machine(), Installed(), ResultPath);

        Assert.True(report.Start.Restamped);
        Assert.False(report.ResultUpdated);
        Assert.Equal(bytes, File.ReadAllBytes(ResultPath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_nths_uninstaller_Velopacks_strings_stay_and_another_installs_key_is_untouched()
    {
        var registry = Installed();
        var report = Run(Machine(), registry, uninstaller: false);

        Assert.Equal(new UninstallRepointResult(true, true, 1, false), report.UninstallKey);
        Assert.Equal("\"" + Root + "\\Update.exe\" --uninstall", registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));

        var foreign = new FakeUserRegistry();
        foreign.WriteVelopackUninstallKey(@"D:\Elsewhere\nth");
        Assert.Equal(new UninstallRepointResult(true, false, 0, true), Run(Machine(), foreign).UninstallKey);
        Assert.Equal(0, foreign.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shell_and_a_disk_that_refuse_everything_never_make_it_throw()
    {
        var shell = Machine();
        foreach (var call in Enum.GetValues<FakeShellLinks.Call>()) shell.Fail(call, unchecked((int)0x80070005));
        new InstallResult(DesktopShortcutOutcome.Created).Write(ResultPath);

        var report = AfterSetupExit.Run(shell, Installed(), Root, ResultPath, _ => throw new IOException("disk gone"), _ => true);

        Assert.False(report.StartPresent);
        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.Failed, HResult: unchecked((int)0x80070005)), report.OldStart);
        Assert.Equal(unchecked((int)0x80070005), report.Start.HResult);
        Assert.Null(report.Desktop);
        Assert.Null(report.UninstallKey);
        Assert.False(report.ResultUpdated);
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), InstallResult.Read(ResultPath));
    }
}
