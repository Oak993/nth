using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 and 8a, VELOPACK_CHECK.md checks 1, 8, 9, 14 and 15, unit U5
// (ED-NEW-inst-u5-1). The three Velopack hooks and nth's self-heal on FakeShellLinks and FakeUserRegistry: what each one
// does in which order, the options file (inherited variable first, fixed path second, none means no shortcut), the result
// file written before the uninstaller copy, the 1.0.0 upgrade, the uninstall key Velopack rewrites, the budgets and a hook
// that never throws. The options and result files live in a temporary folder; nothing touches the real Desktop, Start
// menu, HKCU or %LOCALAPPDATA%.
public sealed class InstallHooksTests : IDisposable
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Current = Root + @"\current";
    private const string CurrentExe = Current + @"\nth.exe";
    private const string LegacyExe = Root + @"\Marrow.App.exe";
    private const string EnvCurrent = @"%LOCALAPPDATA%\Programs\nth\current";
    private const string EnvCurrentExe = EnvCurrent + @"\nth.exe";
    private const string Version = "1.0.1";
    private static readonly string OldStartLink = FakeShellLinks.Programs + @"\nth\nth.lnk";

    private readonly string temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
    private readonly List<string> copies = [];

    public void Dispose()
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (IOException) { } // a temporary folder; Windows cleans %TEMP%
    }

    private static FakeShellLinks Machine(string? startAumid = ShellLinkKeys.NthAppUserModelId, bool start = true)
    {
        var shell = new FakeShellLinks();
        shell.AddFile(CurrentExe);
        shell.AddFile(Root + @"\nth.exe");
        shell.AddFile(Root + @"\Update.exe");
        if (start) shell.Seed(FakeShellLinks.StartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0, AppUserModelId: startAumid));
        return shell;
    }

    private static ShellLink Made(string target = EnvCurrentExe) =>
        new(target, "", EnvCurrent, EnvCurrentExe, 0, ShellLinkKeys.Description, AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

    private InstallHooks Hooks(FakeShellLinks shell, FakeUserRegistry registry, TimeProvider? time = null, bool uninstallerPresent = true, Func<string, long?>? measure = null) =>
        new(shell, registry, time,
            copyUninstaller: root =>
            {
                copies.Add(root);
                return new UninstallerCopyResult(UninstallerCopyOutcome.Copied);
            },
            measureFootprintKb: measure ?? (_ => 430_080),
            fileExists: path => uninstallerPresent && path == Root + @"\nth Uninstall.exe");

    private InstallHookContext Context(string? variable = null) => new(Root, temp, variable, Version);

    private string WriteOptions(InstallOptions options)
    {
        var path = InstallOptions.DefaultOptionsPath(temp, Version);
        options.Write(path);
        return path;
    }

    private static List<string> Names(InstallHookReport report) => [.. report.Steps.Select(step => step[..step.IndexOf(':', StringComparison.Ordinal)])];

    // ---- after install ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_install_with_the_switch_on_creates_writes_the_result_then_copies_the_uninstaller()
    {
        var shell = Machine();
        var options = WriteOptions(InstallOptions.ForInstall(desktopShortcut: true));

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context());

        Assert.Equal(new[] { "start-move", "start-restamp", "desktop", "uninstaller-copy" }, Names(report));
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), report.Result);
        Assert.Equal(report.Result, InstallResult.Read(InstallOptions.ResultPathFor(options)));
        Assert.True(shell.Peek(FakeShellLinks.DesktopLink)!.IsMadeByNth);
        Assert.Equal(new[] { Root }, copies);
        Assert.Equal("desktop: created", report.Steps[2]);
        Assert.Equal("uninstaller-copy: copied", report.Steps[3]);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_install_reads_the_inherited_variable_first_and_writes_the_result_beside_it()
    {
        var shell = Machine();
        var elsewhere = Path.Combine(temp, "elsewhere", InstallOptions.OptionsFileName);
        InstallOptions.ForInstall(desktopShortcut: false).Write(elsewhere);
        WriteOptions(InstallOptions.ForInstall(desktopShortcut: true)); // the fixed path says otherwise

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context(variable: elsewhere));

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Off), report.Result);
        Assert.Equal(report.Result, InstallResult.Read(InstallOptions.ResultPathFor(elsewhere)));
        Assert.Null(InstallResult.Read(Path.Combine(InstallOptions.StagingFolder(temp, Version), InstallOptions.ResultFileName)));
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Setup_exe_run_by_hand_makes_no_desktop_shortcut_and_no_result_but_still_fixes_Start_and_copies()
    {
        var shell = Machine(startAumid: "velopack.NthApp");

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context());

        Assert.Null(report.Result);
        Assert.Equal("desktop: no options file", report.Steps[2]);
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
        Assert.False(File.Exists(Path.Combine(InstallOptions.StagingFolder(temp, Version), InstallOptions.ResultFileName)));
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId);
        Assert.Equal(new[] { Root }, copies);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_upgrade_through_nth_Setup_reaches_the_install_hook_with_kind_update_and_only_repairs()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.OneDriveDesktop + @"\My nth.lnk", Made(LegacyExe));
        WriteOptions(InstallOptions.ForUpdate);

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context());

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), report.Result);
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.OneDriveDesktop + @"\My nth.lnk"));
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.WriteLink));
    }

    [Fact]
    [Trait("Decision", "ED-220")]
    public void After_install_restamps_Start_and_records_it_in_the_result()
    {
        var shell = Machine(startAumid: "velopack.NthApp");
        WriteOptions(InstallOptions.ForInstall(desktopShortcut: true));

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context());

        Assert.Equal("start-restamp: restamped", report.Steps[1]);
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created, StartAppIdRestamped: true), report.Result);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.DesktopLink)!.AppUserModelId);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_Setup_exe_upgrade_from_the_1_0_0_layout_ends_with_exactly_one_Start_link_to_current_nth_exe()
    {
        // VELOPACK_CHECK.md check 14: 1.0.0 was packed with --shortcuts StartMenu; Velopack's pass re-targeted its link to
        // current\nth.exe and, because a Start link into the root exists, made no Programs\nth.lnk. Then our hook runs.
        var shell = Machine(start: false);
        shell.Seed(OldStartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0, AppUserModelId: "velopack.NthApp"));
        WriteOptions(InstallOptions.ForUpdate);

        var report = Hooks(shell, new FakeUserRegistry()).AfterInstall(Context());

        Assert.Equal("start-move: moved, folder removed", report.Steps[0]);
        Assert.Equal(new[] { FakeShellLinks.StartLink }, shell.LinksIn(FakeShellLinks.Programs));
        Assert.False(shell.FolderExists(FakeShellLinks.Programs + @"\nth"));
        var start = shell.Peek(FakeShellLinks.StartLink)!;
        Assert.Equal(CurrentExe, start.Target);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, start.AppUserModelId);
        Assert.True(report.Result!.StartAppIdRestamped);
    }

    // ---- after update ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_update_repairs_copies_and_repoints_the_key_Velopack_just_rewrote_and_never_creates()
    {
        var shell = Machine(startAumid: "velopack.NthApp");
        shell.Seed(OldStartLink, new ShellLink(LegacyExe));
        shell.Seed(FakeShellLinks.ProfileDesktop + @"\nth.lnk", Made(LegacyExe));
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(Root);

        var report = Hooks(shell, registry).AfterUpdate(Context());

        Assert.Equal(new[] { "start-move", "start-restamp", "desktop-repair", "uninstaller-copy", "uninstall-key" }, Names(report));
        Assert.Null(report.Result);
        Assert.Equal("start-move: deletedold, folder removed", report.Steps[0]); // Start existed: the old one goes
        Assert.Equal("start-restamp: restamped", report.Steps[1]);
        Assert.Equal("desktop-repair: 1 changed of 1", report.Steps[2]);
        Assert.Null(shell.Peek(OldStartLink));
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.ProfileDesktop + @"\nth.lnk"));
        Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(new[] { Root }, copies);
        var key = registry.Values(UninstallEntry.KeyPath)!;
        Assert.Equal("\"" + Root + "\\nth Uninstall.exe\" --uninstall", key["UninstallString"]);
        Assert.Equal("\"" + Root + "\\nth Uninstall.exe\" --uninstall --silent", key["QuietUninstallString"]);
        Assert.Equal(430_080, key["EstimatedSize"]);
        Assert.Equal("uninstall-key: 3 written", report.Steps[4]);
    }

    // ---- before uninstall ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Before_uninstall_removes_every_link_into_the_root_and_nths_sign_in_entry_only()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.DesktopLink, Made());
        shell.Seed(FakeShellLinks.OneDriveDesktop + @"\work.lnk", new ShellLink(CurrentExe, "--profile work"));
        shell.Seed(FakeShellLinks.OneDriveDesktop + @"\other.lnk", new ShellLink(@"C:\Apps\other.exe"));
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", "\"" + Root + "\\nth.exe\" --at-sign-in");
        registry.Seed(UninstallEntry.RunKeyPath, "Other", "\"C:\\Apps\\other.exe\"");

        var report = Hooks(shell, registry).BeforeUninstall(Context());

        Assert.Equal(new[] { "desktop-remove: 2 changed of 2", "sign-in: removed" }, report.Steps);
        Assert.Equal(new[] { FakeShellLinks.OneDriveDesktop + @"\other.lnk" }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal("\"C:\\Apps\\other.exe\"", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
        Assert.NotNull(shell.Peek(FakeShellLinks.StartLink)); // Velopack's own uninstaller removes Start (check 1)
    }

    // ---- self-heal ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_self_heal_restores_Start_the_desktop_icon_form_and_the_uninstall_key_after_Velopacks_update_pass()
    {
        var shell = Machine(startAumid: "velopack.NthApp");
        // Velopack's pass after an in-app update (check 1): absolute icon, its own AppUserModelID on every link into the root
        shell.Seed(FakeShellLinks.DesktopLink, Made() with { IconPath = CurrentExe, AppUserModelId = "velopack.NthApp" });
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(Root);

        var report = Hooks(shell, registry).SelfHeal(Root);

        Assert.Equal(new[] { "start-heal: restamped", "desktop-repair: 1 changed of 1", "uninstall-key: 3 written" }, report.Steps);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId);
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal("\"" + Root + "\\nth Uninstall.exe\" --uninstall", registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));

        shell.ResetCounters();
        var again = Hooks(shell, registry).SelfHeal(Root);
        Assert.Equal(new[] { "start-heal: unchanged", "desktop-repair: 0 changed of 1", "uninstall-key: 0 written" }, again.Steps);
        Assert.Equal(0, shell.Changes);
    }

    // ---- budgets and failures ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_near_its_budget_skips_the_steps_that_no_longer_fit_and_leaves_no_false_result()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(wait: by => clock.Advance(by));
        shell.AddFile(CurrentExe);
        shell.Seed(OldStartLink, new ShellLink(CurrentExe));
        shell.Delay(FakeShellLinks.Call.MoveLink, TimeSpan.FromSeconds(26));
        WriteOptions(InstallOptions.ForInstall(desktopShortcut: true));

        var report = Hooks(shell, new FakeUserRegistry(), clock).AfterInstall(Context());

        Assert.Equal("start-move: moved, folder removed", report.Steps[0]);
        Assert.StartsWith("start-restamp: ", report.Steps[1], StringComparison.Ordinal);
        Assert.Equal("desktop: skipped, out of time", report.Steps[2]);
        Assert.Equal("uninstaller-copy: skipped, out of time", report.Steps[3]);
        Assert.Null(report.Result);
        Assert.Equal(InstallResult.Missing, InstallResult.ReadForReady(InstallOptions.ResultPathFor(InstallOptions.DefaultOptionsPath(temp, Version))));
        Assert.Empty(copies);
        Assert.True(clock.Elapsed < InstallHooks.InstallBudget);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_failing_step_is_recorded_and_the_next_steps_still_run()
    {
        var shell = Machine();
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(Root);

        var report = Hooks(shell, registry, measure: _ => throw new IOException("disk gone", unchecked((int)0x80070015))).AfterUpdate(Context());

        Assert.Equal("uninstall-key: failed 0x80070015", report.Steps[4]);
        Assert.Equal(new[] { Root }, copies);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shell_that_refuses_everything_never_makes_a_hook_throw()
    {
        var shell = Machine();
        foreach (var call in Enum.GetValues<FakeShellLinks.Call>()) shell.Fail(call, unchecked((int)0x80070005));
        WriteOptions(InstallOptions.ForInstall(desktopShortcut: true));
        var hooks = Hooks(shell, new FakeUserRegistry());

        var install = hooks.AfterInstall(Context());
        var update = hooks.AfterUpdate(Context());
        var uninstall = hooks.BeforeUninstall(Context());
        var heal = hooks.SelfHeal(Root);

        Assert.Equal(InstallResult.FailedWith(unchecked((int)0x80070005)), install.Result);
        Assert.Equal(install.Result, InstallResult.Read(InstallOptions.ResultPathFor(InstallOptions.DefaultOptionsPath(temp, Version))));
        Assert.Contains(update.Steps, step => step.Contains("failed 0x80070005", StringComparison.Ordinal));
        Assert.Contains(uninstall.Steps, step => step.Contains("failed 0x80070005", StringComparison.Ordinal));
        Assert.Contains(heal.Steps, step => step.Contains("0x80070005", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_budgets_are_Velopacks_and_ours_for_uninstall_is_stricter()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), InstallHooks.InstallBudget);
        Assert.Equal(TimeSpan.FromSeconds(15), InstallHooks.UpdateBudget);
        Assert.Equal(TimeSpan.FromSeconds(30), InstallHooks.UninstallBudget);
        // the longest step (writer wait + scan cap + margin) fits twice in the shortest budget
        Assert.True(InstallHooks.DesktopStepNeed * 2 <= InstallHooks.UpdateBudget);
    }
}
