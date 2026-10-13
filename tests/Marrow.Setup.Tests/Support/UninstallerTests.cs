using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 ("Uninstall flow") and 10 ("Uninstall"), unit U6 (ED-NEW-inst-u6-1).
// With a fake root and a fake Update.exe in a temporary folder: confirm with data kept, with data removed, every desktop link
// into the root and the sign-in value cleaned up (foreign ones left), nth Setup's folder removed, Velopack's own failures and
// a missing Update.exe, a running nth asked first, the install hold, the root's delayed removal, and the leftover check.
public sealed class UninstallerTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Confirm_with_data_kept_removes_nth_and_everything_it_left_but_the_data()
    {
        using var area = new UninstallArea();
        var (marked, renamed, foreign) = area.SeedLinks();
        area.Credentials.Targets.Add("nth:anthropic:0123456789abcdef");

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal(0, report.VelopackExitCode);
        var command = Assert.Single(area.Runner.Started);
        Assert.Equal(VelopackCommandKind.Uninstall, command.Kind);
        Assert.Equal(Path.Combine(area.Root, "Update.exe"), command.Executable);
        Assert.Equal(["--uninstall", "--silent"], command.Arguments);
        Assert.Equal(area.Root, command.WorkingDirectory);
        Assert.False(Directory.Exists(area.Root));
        Assert.True(report.RootGone);
        // the data and the secrets stay: the switch was off
        Assert.True(File.Exists(Path.Combine(area.DataFolder, "threads", "a.json")));
        Assert.Null(report.Data);
        Assert.Null(report.Secrets);
        Assert.Contains("nth:anthropic:0123456789abcdef", area.Credentials.Targets);
        Assert.Empty(area.Credentials.Filters);
        // every link into the root goes, marked or not, renamed included; a foreign one stays
        Assert.False(area.Shell.FileExists(marked));
        Assert.False(area.Shell.FileExists(renamed));
        Assert.True(area.Shell.FileExists(foreign));
        Assert.Equal(2, report.Shortcuts!.Changed);
        // the sign-in value into the root goes; another program's stays
        Assert.True(report.SignInRemoved);
        Assert.Null(area.Registry.GetValue(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName));
        Assert.NotNull(area.Registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
        // nth Setup's folder is swept
        Assert.False(Directory.Exists(Path.Combine(area.LocalAppData, InstallOptions.SetupFolderName)));
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Confirm_with_data_removed_deletes_the_data_folder_and_only_nths_own_secrets()
    {
        using var area = new UninstallArea();
        area.Credentials.Targets.UnionWith(["nth:anthropic:0123456789abcdef", "nth:anthropic:0123456789abcdef#carried", "nth:mcp/github:token:fedcba9876543210", "nth [dev-1]:anthropic:0123456789abcdef", "Marrow:anthropic:0123456789abcdef", "nthing:else"]);

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.False(Directory.Exists(area.DataFolder));
        Assert.True(report.Data!.Gone);
        Assert.True(report.Data.Removed >= 6); // three files, two folders and the data folder itself
        Assert.Equal((3, 0), report.Secrets);
        Assert.Equal(["Marrow:anthropic:0123456789abcdef", "nth [dev-1]:anthropic:0123456789abcdef", "nthing:else"], area.Credentials.Targets.Order(StringComparer.Ordinal));
        Assert.Equal([NthSecrets.TargetPrefix], area.Credentials.Filters);
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Removing_the_data_never_follows_a_link_out_of_the_data_folder()
    {
        using var area = new UninstallArea();
        var outside = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "projects", "app")).FullName;
        var keep = UninstallArea.Put(Path.Combine(outside, "main.c"), "int main;");
        SweepArea.MakeJunction(Path.Combine(area.DataFolder, "worktree"), outside);

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);

        Assert.False(Directory.Exists(area.DataFolder));
        Assert.True(File.Exists(keep));
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_data_file_in_use_is_reported_and_everything_else_still_goes()
    {
        using var area = new UninstallArea();
        var open = Path.Combine(area.DataFolder, "threads", "a.json");
        area.Sweep.Hold(open);

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal([open], report.Data!.Kept);
        Assert.False(File.Exists(Path.Combine(area.DataFolder, "library.json")));
        Assert.False(Directory.Exists(Path.Combine(area.DataFolder, "Logs")));
        Assert.Contains(open, report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Velopack_failing_removes_nothing_of_nths_own_and_says_files_may_be_left()
    {
        using var area = new UninstallArea();
        var (marked, _, _) = area.SeedLinks();
        area.Runner.ExitCode = 1;

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);

        Assert.Equal(UninstallOutcome.VelopackFailed, report.Outcome);
        Assert.Equal(1, report.VelopackExitCode);
        Assert.False(report.NothingChanged);
        Assert.True(Directory.Exists(area.DataFolder)); // never wiped when nth itself is still there
        Assert.True(area.Shell.FileExists(marked));
        Assert.NotNull(area.Registry.GetValue(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName));
        Assert.Contains(area.Root, report.Leftovers!);
        Assert.Contains(@"HKCU\" + UninstallEntry.KeyPath, report.Leftovers!);
        Assert.Contains("Update.exe exit code: 1", report.Details(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task No_Update_exe_and_an_Update_exe_that_cannot_start_change_nothing()
    {
        using var area = new UninstallArea();
        File.Delete(Path.Combine(area.Root, "Update.exe"));
        var missing = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);
        Assert.Equal(UninstallOutcome.NoUninstaller, missing.Outcome);
        Assert.True(missing.NothingChanged);
        Assert.Empty(area.Runner.Started);

        UninstallArea.Put(Path.Combine(area.Root, "Update.exe"));
        area.Runner.Throws = true;
        var failed = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: true);
        Assert.Equal(UninstallOutcome.DidNotStart, failed.Outcome);
        Assert.True(failed.NothingChanged);
        Assert.True(Directory.Exists(area.DataFolder));
        Assert.True(Directory.Exists(area.Root));
        Assert.True(Directory.Exists(Path.Combine(area.LocalAppData, InstallOptions.SetupFolderName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Another_setup_holding_the_root_stands_the_uninstall_aside_before_anything_is_asked()
    {
        using var area = new UninstallArea();
        var nth = new ScriptedRunningNth(RunningNthState.Busy);

        var report = await new Uninstaller(area.Ports(_ => nth, _ => null)).RunAsync(area.Root, wipeData: true);

        Assert.Equal(UninstallOutcome.InProgress, report.Outcome);
        Assert.True(report.NothingChanged);
        Assert.Empty(nth.Calls);
        Assert.Empty(area.Runner.Started);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_busy_nth_stops_its_work_cleanly_before_Velopack_runs_and_the_hold_is_let_go_after()
    {
        using var area = new UninstallArea();
        var nth = new ScriptedRunningNth(RunningNthState.Busy);
        var hold = new FakeHold();
        var heldWhileVelopackRan = false;
        area.Runner.Before = _ => heldWhileVelopackRan = hold.Disposals == 0 && nth.Calls.SequenceEqual(["ask", "stop"]);

        var report = await new Uninstaller(area.Ports(_ => nth, _ => hold)).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal(["ask", "stop"], nth.Calls);
        Assert.Equal(RunningNthState.Closed, report.Nth!.State);
        Assert.True(heldWhileVelopackRan);
        Assert.True(hold.Disposals >= 1);
    }

    [Theory]
    [InlineData(RunningNthState.NotResponding)]
    [InlineData(RunningNthState.Refused)]
    [Trait("Decision", "ED-247")]
    public async Task A_window_uninstall_goes_on_when_nth_will_not_quit_because_Velopack_stops_it(RunningNthState answer)
    {
        using var area = new UninstallArea();
        var nth = new ScriptedRunningNth(answer);

        var report = await new Uninstaller(area.Ports(_ => nth)).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal(["ask"], nth.Calls); // nothing is ended by nth Setup itself
        Assert.Single(area.Runner.Started);
    }

    [Theory]
    [InlineData(RunningNthState.Busy)]
    [InlineData(RunningNthState.NotResponding)]
    [InlineData(RunningNthState.Refused)]
    [Trait("Decision", "ED-247")]
    public async Task A_silent_uninstall_never_stops_anyones_work_and_changes_nothing(RunningNthState answer)
    {
        using var area = new UninstallArea();
        var nth = new ScriptedRunningNth(answer);

        var report = await new Uninstaller(area.Ports(_ => nth)).RunAsync(area.Root, wipeData: false, silent: true);

        Assert.Equal(UninstallOutcome.NthNotClosed, report.Outcome);
        Assert.Equal(["ask"], nth.Calls);
        Assert.Empty(area.Runner.Started);
        Assert.True(Directory.Exists(area.Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_root_Velopack_removes_a_moment_later_is_waited_for()
    {
        using var area = new UninstallArea();
        area.Runner.Removal = RootRemoval.Later;

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: false);

        Assert.True(report.RootGone);
        Assert.False(Directory.Exists(area.Root));
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_root_left_with_only_Update_exe_is_removed_and_one_with_a_file_in_use_is_reported()
    {
        using (var area = new UninstallArea())
        {
            area.Runner.Removal = RootRemoval.Never;
            var report = await new Uninstaller(area.Ports(rootPatience: TimeSpan.FromMilliseconds(300))).RunAsync(area.Root, wipeData: false);
            Assert.True(report.RootGone);
            Assert.False(Directory.Exists(area.Root));
        }
        using (var area = new UninstallArea())
        {
            area.Runner.Removal = RootRemoval.FileInUse;
            var report = await new Uninstaller(area.Ports(rootPatience: TimeSpan.FromMilliseconds(300))).RunAsync(area.Root, wipeData: false);
            Assert.Equal(UninstallOutcome.Removed, report.Outcome);
            Assert.False(report.RootGone);
            Assert.True(File.Exists(Path.Combine(area.Root, "Update.exe"))); // nothing of the root is removed past Velopack's own rule
            Assert.Contains(area.Root, report.Leftovers!);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_leftover_check_names_a_key_Velopack_did_not_remove_and_a_link_it_could_not()
    {
        using var area = new UninstallArea();
        var (_, renamed, _) = area.SeedLinks();
        area.Runner.RemovesKey = false;
        area.Shell.Fail(FakeShellLinks.Call.DeleteLink, unchecked((int)0x80070020), path: renamed); // in use

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: false);

        Assert.Equal(UninstallOutcome.Removed, report.Outcome);
        Assert.Equal(1, report.Shortcuts!.Failed);
        Assert.Equal([@"HKCU\" + UninstallEntry.KeyPath, renamed], report.Leftovers!);
        Assert.Contains("Left: " + renamed, report.Details(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Links_Velopack_already_removed_are_not_counted_twice_and_a_second_run_finds_nothing()
    {
        using var area = new UninstallArea();
        area.SeedLinks();
        area.Runner.RemovesShortcuts = true;

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: false);

        Assert.Equal(0, report.Shortcuts!.Changed);
        Assert.Single(area.Shell.LinksIn(FakeShellLinks.OneDriveDesktop)); // the foreign one
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_uninstallers_own_running_copy_is_expected_and_not_a_leftover()
    {
        using var area = new UninstallArea();
        var copy = area.Sweep.Put(Path.Combine(area.Sweep.UninstallTemp, "nth Uninstall.exe"), "copy");
        area.Sweep.Hold(copy); // to a sweep it is a running program

        var report = await new Uninstaller(area.Ports()).RunAsync(area.Root, wipeData: false);

        Assert.True(File.Exists(copy));
        Assert.Contains(report.Sweep!.Kept, kept => InstallLayout.IsInside(area.Sweep.UninstallTemp, kept.Path));
        Assert.Empty(report.Leftovers!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Steps_run_in_the_plans_order()
    {
        using var area = new UninstallArea();
        var uninstaller = new Uninstaller(area.Ports(_ => new ScriptedRunningNth(RunningNthState.Clear)));
        var steps = new List<string>();
        uninstaller.StepStarted += steps.Add;

        await uninstaller.RunAsync(area.Root, wipeData: true);

        Assert.Equal(["hold", "nth", "velopack", "data", "secrets", "desktop", "sign-in", "root", "sweep"], steps);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Paths_must_be_full_and_the_data_folder_is_nths()
    {
        using var area = new UninstallArea();
        Assert.Throws<ArgumentException>(() => new Uninstaller(area.Ports() with { LocalAppData = "relative" }));
        Assert.Throws<ArgumentException>(() => new Uninstaller(area.Ports() with { TempPath = "" }));
        Assert.Equal(Path.Combine(area.LocalAppData, "nth"), new Uninstaller(area.Ports()).DataFolder);
        // the data folder's name is the app's own (src/Marrow.Core/Support/Text.cs, OD-0135)
        var text = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Core", "Support", "Text.cs"));
        Assert.Contains("public const string DataFolderName = \"" + Uninstaller.DataFolderName + "\";", text, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent!;
        return directory.FullName;
    }
}
