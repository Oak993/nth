using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a's test list and VELOPACK_CHECK.md checks 1, 2, 3 and 14, unit U5
// (ED-NEW-inst-u5-1). Every rule of the desktop and Start shortcuts, on FakeShellLinks: the file, the launch target, the
// Start re-stamp, repeat runs, ownership, cloud placeholders, repair, the hook budget, one writer at a time, inputs and
// errors, 1.0.0's Start folder and the self-heal of Start. Nothing here touches the real Desktop or Start menu.
public sealed class DesktopShortcutTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Current = Root + @"\current";
    private const string CurrentExe = Current + @"\nth.exe";
    private const string Stub = Root + @"\nth.exe";
    private const string LegacyExe = Root + @"\Marrow.App.exe";
    private const string EnvCurrent = @"%LOCALAPPDATA%\Programs\nth\current";
    private const string EnvCurrentExe = EnvCurrent + @"\nth.exe";
    private const string Foreign = @"C:\Old\nth\nth.exe";
    private const int AccessDenied = unchecked((int)0x80070005);
    private static readonly Guid ToastClsid = new("5D2C3F33-8E5A-4A8B-9C4F-7B1E2A3C4D5E");
    private static readonly string OldStartFolder = FakeShellLinks.Programs + @"\nth";
    private static readonly string OldStartLink = OldStartFolder + @"\nth.lnk";

    private static FakeShellLinks Machine(bool start = true, string? aumid = ShellLinkKeys.NthAppUserModelId)
    {
        var shell = new FakeShellLinks();
        shell.AddFile(CurrentExe);
        shell.AddFile(Stub);
        shell.AddFile(Root + @"\Update.exe");
        if (start) shell.Seed(FakeShellLinks.StartLink, VelopackStart(aumid));
        return shell;
    }

    // What Velopack 1.2.161 writes on Start (VELOPACK_CHECK.md check 2): absolute current\nth.exe, its folder, its icon.
    private static ShellLink VelopackStart(string? aumid = ShellLinkKeys.NthAppUserModelId, string arguments = "") =>
        new(CurrentExe, arguments, Current, CurrentExe, 0, AppUserModelId: aumid);

    private static ShellLink Made(string target = EnvCurrentExe) =>
        new(target, "", EnvCurrent, EnvCurrentExe, 0, ShellLinkKeys.Description, AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

    private static string OnDesktop(string name) => FakeShellLinks.OneDriveDesktop + @"\" + name;

    // ---- the file ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_fresh_install_writes_nth_lnk_with_Starts_fields_stored_with_LOCALAPPDATA_nths_AppUserModelID_and_the_marker()
    {
        var shell = Machine();

        var result = new DesktopShortcut(shell, Root).Apply(InstallOptions.ForInstall(desktopShortcut: true));

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), result);
        Assert.Equal(new[] { FakeShellLinks.DesktopLink }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Equal("nth.lnk", Path.GetFileName(FakeShellLinks.DesktopLink));
        var link = shell.Peek(FakeShellLinks.DesktopLink)!;
        Assert.Equal(EnvCurrentExe, link.Target);
        Assert.Equal("", link.Arguments);
        Assert.Equal(EnvCurrent, link.WorkingDirectory);
        Assert.Equal(EnvCurrentExe, link.IconPath);
        Assert.Equal(0, link.IconIndex);
        Assert.Equal("nth", link.Description);
        Assert.Equal(ShellLinkKeys.ShowNormal, link.ShowCommand);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, link.AppUserModelId);
        Assert.Equal(shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId, link.AppUserModelId);
        Assert.Equal(ShellLinkKeys.CreatedByValue, link.CreatedBy);
        Assert.True(link.IsMadeByNth);
        Assert.Contains((ShellChange.Created, FakeShellLinks.DesktopLink), shell.Notifications);
        // the stored form expands to exactly what Start points at
        Assert.Equal(CurrentExe, shell.ExpandEnvironment(link.Target));
        Assert.Equal(Current, shell.ExpandEnvironment(link.WorkingDirectory));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_desktop_shortcut_copies_Starts_arguments_and_icon()
    {
        var shell = Machine(start: false);
        shell.Seed(FakeShellLinks.StartLink, new ShellLink(CurrentExe, "--from-start", Current, Root + @"\icons\nth.ico", 2, AppUserModelId: ShellLinkKeys.NthAppUserModelId));
        shell.AddFile(Root + @"\icons\nth.ico");

        Assert.Equal(DesktopShortcutOutcome.Created, new DesktopShortcut(shell, Root).Create().DesktopShortcut);

        var link = shell.Peek(FakeShellLinks.DesktopLink)!;
        Assert.Equal("--from-start", link.Arguments);
        Assert.Equal(@"%LOCALAPPDATA%\Programs\nth\icons\nth.ico", link.IconPath);
        Assert.Equal(2, link.IconIndex);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_root_outside_LOCALAPPDATA_is_stored_as_a_plain_path()
    {
        const string other = @"D:\Apps\nth";
        var shell = new FakeShellLinks();
        shell.AddFile(other + @"\current\nth.exe");

        new DesktopShortcut(shell, other).Create();

        var link = shell.Peek(FakeShellLinks.DesktopLink)!;
        Assert.Equal(other + @"\current\nth.exe", link.Target);
        Assert.Equal(other + @"\current", link.WorkingDirectory);
    }

    // ---- the launch target (VELOPACK_CHECK.md check 2: never the stub) ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_launch_target_is_copied_from_Start_when_Start_points_at_a_file_in_the_root()
    {
        var shell = Machine(start: false);
        var start = VelopackStart(arguments: "--x");

        var launch = DesktopShortcut.LaunchTarget(Root, start, shell.FileExists, shell.ExpandEnvironment);

        Assert.Equal(new LaunchTarget(CurrentExe, "--x", Current, CurrentExe, 0, FromStart: true), launch);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Start_missing_with_the_stub_present_gives_current_nth_exe_never_the_stub()
    {
        var shell = Machine(start: false);
        Assert.True(shell.FileExists(Stub));

        var launch = DesktopShortcut.LaunchTarget(Root, null, shell.FileExists, shell.ExpandEnvironment);

        Assert.Equal(new LaunchTarget(CurrentExe, "", Current, CurrentExe, 0, FromStart: false), launch);
        Assert.NotEqual(Stub, launch.Target);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Start_missing_without_the_stub_gives_current_nth_exe()
    {
        var shell = Machine(start: false);
        shell.RemoveFile(Stub);

        Assert.Equal(CurrentExe, DesktopShortcut.LaunchTarget(Root, null, shell.FileExists, shell.ExpandEnvironment).Target);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_Start_link_outside_the_root_or_with_a_gone_target_is_not_copied()
    {
        var shell = Machine(start: false);
        shell.AddFile(Foreign);

        Assert.Equal(CurrentExe, DesktopShortcut.LaunchTarget(Root, new ShellLink(Foreign), shell.FileExists, shell.ExpandEnvironment).Target);
        Assert.Equal(CurrentExe, DesktopShortcut.LaunchTarget(Root, new ShellLink(LegacyExe), shell.FileExists, shell.ExpandEnvironment).Target);
        Assert.Equal(CurrentExe, DesktopShortcut.LaunchTarget(Root, new ShellLink(Stub + ".missing"), shell.FileExists, shell.ExpandEnvironment).Target);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_Start_the_desktop_shortcut_points_at_current_nth_exe()
    {
        var shell = Machine(start: false);

        new DesktopShortcut(shell, Root).Create();

        Assert.Equal(EnvCurrentExe, shell.Peek(FakeShellLinks.DesktopLink)!.Target);
    }

    // ---- Start's AppUserModelID ----

    [Fact]
    [Trait("Decision", "ED-220")]
    public void A_different_Start_ID_is_restamped_by_changing_only_the_AppUserModelID_and_the_toast_CLSID_survives()
    {
        var shell = Machine(start: false);
        var start = VelopackStart(aumid: "velopack.NthApp") with { ToastActivatorClsid = ToastClsid, Description = "nth" };
        shell.Seed(FakeShellLinks.StartLink, start);

        var result = new DesktopShortcut(shell, Root).RestampStart();

        Assert.Equal(new StartLinkResult(Found: true, Restamped: true), result);
        Assert.Equal(start with { AppUserModelId = ShellLinkKeys.NthAppUserModelId }, shell.Peek(FakeShellLinks.StartLink));
        Assert.Equal(ToastClsid, shell.Peek(FakeShellLinks.StartLink)!.ToastActivatorClsid);
        Assert.Equal(1, shell.CallCount(FakeShellLinks.Call.UpdateLink));
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.WriteLink));
    }

    [Fact]
    [Trait("Decision", "ED-220")]
    public void A_right_missing_or_foreign_Start_is_not_restamped()
    {
        var right = Machine();
        Assert.Equal(new StartLinkResult(Found: true), new DesktopShortcut(right, Root).RestampStart());
        Assert.Equal(0, right.Changes);

        var missing = Machine(start: false);
        Assert.Equal(new StartLinkResult(Found: false), new DesktopShortcut(missing, Root).RestampStart());

        var foreign = Machine(start: false);
        foreign.Seed(FakeShellLinks.StartLink, new ShellLink(Foreign, AppUserModelId: "someone.else"));
        Assert.Equal(new StartLinkResult(Found: true), new DesktopShortcut(foreign, Root).RestampStart());
        Assert.Equal("someone.else", foreign.Peek(FakeShellLinks.StartLink)!.AppUserModelId);
        Assert.Equal(0, foreign.Changes);
    }

    // ---- repeat runs ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_Apply_is_kept_with_zero_writes()
    {
        var shell = Machine();
        var shortcut = new DesktopShortcut(shell, Root);
        Assert.Equal(DesktopShortcutOutcome.Created, shortcut.Apply(InstallOptions.ForInstall(true))!.DesktopShortcut);
        shell.ResetCounters();

        var again = shortcut.Apply(InstallOptions.ForInstall(true));

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), again);
        Assert.Equal(0, shell.Changes);
        Assert.Empty(shell.Notifications);
    }

    // ---- ownership ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_foreign_nth_lnk_is_untouched_and_gives_name_taken_with_no_second_shortcut()
    {
        var shell = Machine();
        var foreign = new ShellLink(Foreign, "--dev", @"C:\Old\nth", Foreign, 0, "nth dev");
        shell.Seed(FakeShellLinks.DesktopLink, foreign);

        var result = new DesktopShortcut(shell, Root).Create();

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.NameTaken), result);
        Assert.Equal(foreign, shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(new[] { FakeShellLinks.DesktopLink }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Equal(0, shell.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unmarked_nth_lnk_into_the_root_with_custom_arguments_and_icon_is_kept_and_never_rewritten()
    {
        var shell = Machine();
        var own = new ShellLink(CurrentExe, "--profile work", @"D:\Work", @"D:\Icons\mine.ico", 3, "my nth");
        shell.Seed(FakeShellLinks.DesktopLink, own);
        var renamed = OnDesktop("Work nth.lnk");
        shell.Seed(renamed, own);
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), shortcut.Create());
        Assert.Equal(0, shortcut.Repair().Changed);

        Assert.Equal(own, shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(own, shell.Peek(renamed));
        Assert.Equal(0, shell.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unmarked_link_into_the_root_whose_target_is_gone_gets_only_its_target_and_working_directory_repointed()
    {
        var shell = Machine();
        var own = new ShellLink(LegacyExe, "--profile work", Root, @"D:\Icons\mine.ico", 3, "my nth", AppUserModelId: "MarrowCode.Marrow");
        shell.Seed(FakeShellLinks.DesktopLink, own);

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), new DesktopShortcut(shell, Root).Create());

        Assert.Equal(own with { Target = EnvCurrentExe, WorkingDirectory = EnvCurrent }, shell.Peek(FakeShellLinks.DesktopLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void RemoveMade_removes_only_the_links_nth_made_renamed_ones_included()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.DesktopLink, Made());
        shell.Seed(OnDesktop("My nth.lnk"), Made());
        shell.Seed(OnDesktop("nth work.lnk"), new ShellLink(CurrentExe, "--profile work"));
        shell.Seed(OnDesktop("Old nth.lnk"), new ShellLink(Foreign));

        var result = new DesktopShortcut(shell, Root).RemoveMade();

        Assert.Equal(new ShortcutChangeResult(2, 2, Capped: false, Busy: false, Failed: 0), result);
        Assert.Equal(new[] { OnDesktop("nth work.lnk"), OnDesktop("Old nth.lnk") }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Contains((ShellChange.Deleted, FakeShellLinks.DesktopLink), shell.Notifications);
        Assert.Contains((ShellChange.Deleted, OnDesktop("My nth.lnk")), shell.Notifications);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void RemoveIntoRoot_removes_marked_and_unmarked_links_into_the_root_on_both_desktops_and_tells_Explorer()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.DesktopLink, Made());
        shell.Seed(OnDesktop("Renamed.lnk"), Made());
        shell.Seed(OnDesktop("nth work.lnk"), new ShellLink(CurrentExe, "--profile work"));
        shell.Seed(OnDesktop("legacy.lnk"), new ShellLink(LegacyExe));
        shell.Seed(OnDesktop("Old nth.lnk"), new ShellLink(Foreign));
        var profileLink = FakeShellLinks.ProfileDesktop + @"\nth.lnk";
        shell.Seed(profileLink, new ShellLink(@"%LOCALAPPDATA%\Programs\nth\nth.exe"));

        var result = new DesktopShortcut(shell, Root).RemoveIntoRoot();

        Assert.Equal(5, result.Changed);
        Assert.Equal(new[] { OnDesktop("Old nth.lnk") }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Empty(shell.LinksIn(FakeShellLinks.ProfileDesktop));
        Assert.Equal(5, shell.Notifications.Count(n => n.Change == ShellChange.Deleted));
        Assert.Contains((ShellChange.Deleted, profileLink), shell.Notifications);
    }

    // ---- cloud placeholders ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Cloud_placeholders_are_never_read_counted_or_removed_by_a_scan()
    {
        var shell = Machine();
        var placeholder = OnDesktop("nth from my laptop.lnk");
        shell.SeedPlaceholder(placeholder, Made());
        shell.SeedPlaceholder(OnDesktop("other.lnk"), new ShellLink(CurrentExe));
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Empty(shortcut.FindMade().Links);
        Assert.Empty(shortcut.FindIntoRoot().Links);
        Assert.Equal(0, shortcut.RemoveIntoRoot().Changed);
        Assert.Equal(0, shortcut.RemoveMade().Found);
        Assert.Equal(0, shortcut.Repair().Found);

        Assert.Empty(shell.ReadPaths);
        Assert.Empty(shell.Hydrated);
        Assert.NotNull(shell.Peek(placeholder));
        Assert.True(new ShellLinkFile(placeholder, shell.AttributesOf(placeholder)!.Value, default).IsCloudPlaceholder);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_nth_lnk_placeholder_is_read_once_when_creating()
    {
        var shell = Machine();
        shell.SeedPlaceholder(FakeShellLinks.DesktopLink, Made());

        var result = new DesktopShortcut(shell, Root).Create();

        Assert.Equal(DesktopShortcutOutcome.Kept, result.DesktopShortcut);
        Assert.Equal(1, shell.ReadPaths.Count(p => p == FakeShellLinks.DesktopLink));
        Assert.Equal(new[] { FakeShellLinks.DesktopLink }, shell.Hydrated);
        Assert.Equal(0, shell.Changes);
    }

    // ---- repair ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_repoints_marked_links_to_the_old_stub_and_to_an_absolute_current_target_and_keeps_the_file_name()
    {
        var shell = Machine();
        var renamed = OnDesktop("My nth.lnk");
        shell.Seed(renamed, Made(LegacyExe) with { ToastActivatorClsid = ToastClsid });
        // what Velopack's own pass leaves after an update (check 1): absolute target and icon, its own AppUserModelID
        shell.Seed(FakeShellLinks.DesktopLink, Made(CurrentExe) with { IconPath = CurrentExe, AppUserModelId = "velopack.NthApp" });

        var result = new DesktopShortcut(shell, Root).Repair();

        Assert.Equal(new ShortcutChangeResult(2, 2, Capped: false, Busy: false, Failed: 0), result);
        Assert.Equal(Made() with { ToastActivatorClsid = ToastClsid }, shell.Peek(renamed));
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(new[] { renamed, FakeShellLinks.DesktopLink }, shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Equal(0, shell.CallCount(FakeShellLinks.Call.WriteLink));
        Assert.Contains((ShellChange.Updated, renamed), shell.Notifications);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_never_creates_and_an_update_never_creates()
    {
        var shell = Machine();
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Equal(new ShortcutChangeResult(0, 0, Capped: false, Busy: false, Failed: 0), shortcut.Repair());
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Off), shortcut.Apply(InstallOptions.ForUpdate));

        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Equal(0, shell.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_update_repairs_the_shortcut_nth_made_and_reports_it_kept()
    {
        var shell = Machine();
        shell.Seed(FakeShellLinks.DesktopLink, Made(LegacyExe));

        var result = new DesktopShortcut(shell, Root).Apply(InstallOptions.ForUpdate);

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), result);
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.DesktopLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_user_deleted_shortcut_stays_deleted_through_repair_and_updates()
    {
        var shell = Machine();
        var shortcut = new DesktopShortcut(shell, Root);
        shortcut.Create();
        shell.DeleteLink(FakeShellLinks.DesktopLink);

        shortcut.Repair();
        shortcut.Apply(InstallOptions.ForUpdate);
        shortcut.HealStart();

        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
    }

    // ---- the hook budget ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_scan_of_ten_thousand_slow_links_stops_inside_3_s_and_records_scanCapped()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(wait: by => clock.Advance(by));
        shell.AddFile(CurrentExe);
        shell.SeedMany(FakeShellLinks.OneDriveDesktop, 10_000);
        shell.Seed(OnDesktop("nth (mine).lnk"), Made());
        shell.Delay(FakeShellLinks.Call.ReadLink, TimeSpan.FromMilliseconds(10));

        var result = new DesktopShortcut(shell, Root, clock).Create(ShortcutScan.Hook);

        Assert.Equal(DesktopShortcutOutcome.Kept, result.DesktopShortcut); // nth*.lnk is looked at first
        Assert.True(result.ScanCapped);
        Assert.True(clock.Elapsed <= ShellLinkKeys.ScanCapTime + TimeSpan.FromMilliseconds(10), clock.Elapsed.ToString());
        Assert.True(shell.Reads < 400, shell.Reads.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_scan_reads_at_most_2000_links_and_a_full_scan_reads_them_all()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(wait: by => clock.Advance(by));
        shell.AddFile(CurrentExe);
        shell.SeedMany(FakeShellLinks.OneDriveDesktop, 10_000);
        var shortcut = new DesktopShortcut(shell, Root, clock);

        var hook = shortcut.FindMade(ShortcutScan.Hook);
        Assert.True(hook.Capped);
        Assert.Equal(ShellLinkKeys.ScanCapLinks, shell.Reads);

        shell.ResetCounters();
        var full = shortcut.FindMade(ShortcutScan.Full);
        Assert.False(full.Capped);
        Assert.Equal(10_000, shell.Reads);

        shell.ResetCounters();
        var created = shortcut.Create(ShortcutScan.Hook);
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created, ScanCapped: true), created);
    }

    // ---- one writer at a time ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_Settings_remove_during_an_in_flight_create_waits_for_it_and_the_end_state_is_consistent()
    {
        var shell = Machine();
        shell.Delay(FakeShellLinks.Call.WriteLink, TimeSpan.FromMilliseconds(400));
        var hook = new DesktopShortcut(shell, Root);
        var settings = new DesktopShortcut(shell, Root);

        var create = Task.Run(() => hook.Create());
        SpinWait.SpinUntil(() => shell.CallCount(FakeShellLinks.Call.WriteLink) == 1, TimeSpan.FromSeconds(5));
        var removed = settings.RemoveMade();

        Assert.Equal(DesktopShortcutOutcome.Created, (await create).DesktopShortcut);
        Assert.False(removed.Busy);
        Assert.Equal(1, removed.Changed); // it waited, then saw the whole new link
        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        Assert.Equal(0, shell.WriterTimeouts);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_writer_kept_out_for_2_s_skips_its_step_and_leaves_one_whole_shortcut()
    {
        var shell = Machine();
        shell.Delay(FakeShellLinks.Call.WriteLink, TimeSpan.FromMilliseconds(2600));
        var hook = new DesktopShortcut(shell, Root);

        var create = Task.Run(() => hook.Create());
        SpinWait.SpinUntil(() => shell.CallCount(FakeShellLinks.Call.WriteLink) == 1, TimeSpan.FromSeconds(5));
        var removing = Task.Run(() => new DesktopShortcut(shell, Root).RemoveMade());
        var creating = Task.Run(() => new DesktopShortcut(shell, Root).Create());
        var removed = await removing;
        var busyCreate = await creating;

        Assert.True(removed.Busy);
        Assert.Equal(DesktopShortcutOutcome.Created, (await create).DesktopShortcut);
        Assert.Equal(InstallResult.FailedWith(DesktopShortcut.WriterBusyHResult), busyCreate);
        Assert.Equal(Made(), shell.Peek(FakeShellLinks.DesktopLink));
        Assert.Equal(2, shell.WriterTimeouts);
    }

    // ---- inputs and errors ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_options_file_means_nothing_happens_and_off_writes_nothing()
    {
        var shell = Machine();
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Null(shortcut.Apply(null));
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Off), shortcut.Apply(InstallOptions.ForInstall(desktopShortcut: false)));

        Assert.Equal(0, shell.Changes);
        Assert.Equal(0, shell.Reads);
        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_refused_write_gives_failed_with_its_HRESULT_and_never_throws()
    {
        var shell = Machine();
        shell.Fail(FakeShellLinks.Call.WriteLink, AccessDenied);

        var result = new DesktopShortcut(shell, Root).Apply(InstallOptions.ForInstall(true));

        Assert.Equal(InstallResult.FailedWith(AccessDenied), result);
        Assert.Equal("0x80070005", InstallResult.FormatHResult(result!.HResult!.Value));
        Assert.Empty(shell.LinksIn(FakeShellLinks.OneDriveDesktop));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Any_failing_shell_call_while_creating_gives_failed_not_an_exception()
    {
        foreach (var call in new[] { FakeShellLinks.Call.KnownFolder, FakeShellLinks.Call.FileExists, FakeShellLinks.Call.EnumerateLinks, FakeShellLinks.Call.TryEnterWriter, FakeShellLinks.Call.ExpandEnvironment })
        {
            var shell = Machine();
            shell.Fail(call, AccessDenied);
            var result = new DesktopShortcut(shell, Root).Create();
            Assert.Equal(DesktopShortcutOutcome.Failed, result.DesktopShortcut);
            Assert.Equal(AccessDenied, result.HResult);
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_Explorer_notification_that_fails_does_not_fail_a_shortcut_that_was_written()
    {
        var shell = Machine();
        shell.Fail(FakeShellLinks.Call.Notify, AccessDenied);

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), new DesktopShortcut(shell, Root).Create());
        Assert.NotNull(shell.Peek(FakeShellLinks.DesktopLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Hidden_desktop_icons_are_recorded_and_still_create_the_file()
    {
        var shell = Machine();
        shell.IconsHidden = true;

        var result = new DesktopShortcut(shell, Root).Apply(InstallOptions.ForInstall(true));

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created, IconsHidden: true), result);
        Assert.NotNull(shell.Peek(FakeShellLinks.DesktopLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shell_that_cannot_tell_whether_icons_are_hidden_counts_them_shown()
    {
        var shell = Machine();
        shell.Fail(FakeShellLinks.Call.DesktopIconsHidden, AccessDenied);

        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), new DesktopShortcut(shell, Root).Create());
    }

    // ---- 1.0.0's Start folder (VELOPACK_CHECK.md check 14) ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_old_Start_link_is_moved_to_Programs_nth_lnk_with_every_property_and_the_empty_folder_goes()
    {
        var shell = Machine(start: false);
        // Velopack's pass already re-targeted 1.0.0's link to current\nth.exe (shortcuts:127-139)
        var old = VelopackStart() with { ToastActivatorClsid = ToastClsid, Description = "nth" };
        shell.Seed(OldStartLink, old);

        var result = new DesktopShortcut(shell, Root).MoveOldStartLink();

        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.Moved, FolderRemoved: true), result);
        Assert.Equal(old, shell.Peek(FakeShellLinks.StartLink));
        Assert.Null(shell.Peek(OldStartLink));
        Assert.False(shell.FolderExists(OldStartFolder));
        Assert.Equal(new[] { FakeShellLinks.StartLink }, shell.LinksIn(FakeShellLinks.Programs));
        Assert.Contains((ShellChange.Created, FakeShellLinks.StartLink), shell.Notifications);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_old_Start_link_still_pointing_at_Marrow_App_exe_is_moved_too()
    {
        var shell = Machine(start: false);
        shell.Seed(OldStartLink, new ShellLink(LegacyExe, WorkingDirectory: Current, AppUserModelId: "velopack.NthApp"));

        Assert.Equal(OldStartLinkOutcome.Moved, new DesktopShortcut(shell, Root).MoveOldStartLink().Outcome);
        Assert.Equal(LegacyExe, shell.Peek(FakeShellLinks.StartLink)!.Target);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_old_Start_link_is_deleted_only_when_Programs_nth_lnk_into_the_root_exists()
    {
        var shell = Machine();
        shell.Seed(OldStartLink, VelopackStart());

        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.DeletedOld, FolderRemoved: true), new DesktopShortcut(shell, Root).MoveOldStartLink());
        Assert.Equal(new[] { FakeShellLinks.StartLink }, shell.LinksIn(FakeShellLinks.Programs));

        var foreignStart = Machine(start: false);
        foreignStart.Seed(FakeShellLinks.StartLink, new ShellLink(Foreign));
        foreignStart.Seed(OldStartLink, VelopackStart());
        Assert.Equal(OldStartLinkOutcome.LeftForeign, new DesktopShortcut(foreignStart, Root).MoveOldStartLink().Outcome);
        Assert.NotNull(foreignStart.Peek(OldStartLink));
        Assert.Equal(0, foreignStart.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_old_Start_link_outside_the_root_and_a_folder_with_other_files_are_left()
    {
        var foreign = Machine(start: false);
        foreign.Seed(OldStartLink, new ShellLink(Foreign));
        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.LeftForeign), new DesktopShortcut(foreign, Root).MoveOldStartLink());
        Assert.NotNull(foreign.Peek(OldStartLink));

        var shared = Machine(start: false);
        shared.Seed(OldStartLink, VelopackStart());
        shared.Seed(OldStartFolder + @"\nth help.lnk", new ShellLink(@"C:\Windows\notepad.exe"));
        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.Moved, FolderRemoved: false), new DesktopShortcut(shared, Root).MoveOldStartLink());
        Assert.True(shared.FolderExists(OldStartFolder));

        var none = Machine();
        Assert.Equal(new OldStartLinkResult(OldStartLinkOutcome.None), new DesktopShortcut(none, Root).MoveOldStartLink());
    }

    // ---- the self-heal of Start ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void HealStart_repoints_a_gone_target_and_icon_restamps_and_keeps_the_toast_CLSID()
    {
        var shell = Machine(start: false);
        var start = new ShellLink(LegacyExe, "", Root, LegacyExe, 0, AppUserModelId: "velopack.NthApp", ToastActivatorClsid: ToastClsid);
        shell.Seed(FakeShellLinks.StartLink, start);

        var result = new DesktopShortcut(shell, Root).HealStart();

        Assert.Equal(new StartLinkResult(Found: true, Restamped: true, Retargeted: true, IconRepaired: true), result);
        Assert.Equal(start with { Target = CurrentExe, WorkingDirectory = Current, IconPath = CurrentExe, AppUserModelId = ShellLinkKeys.NthAppUserModelId }, shell.Peek(FakeShellLinks.StartLink));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void HealStart_leaves_a_right_Start_alone()
    {
        var shell = Machine();

        Assert.Equal(new StartLinkResult(Found: true), new DesktopShortcut(shell, Root).HealStart());
        Assert.Equal(0, shell.Changes);
    }

    // ---- folders ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Scans_cover_the_OneDrive_desktop_and_the_profile_desktop_and_create_on_the_OneDrive_one()
    {
        var shell = Machine();
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Equal(new[] { FakeShellLinks.OneDriveDesktop, FakeShellLinks.ProfileDesktop }, shortcut.DesktopFolders());
        Assert.Equal(FakeShellLinks.DesktopLink, shortcut.DesktopLinkPath);

        shell.Desktop = FakeShellLinks.ProfileDesktop;
        Assert.Equal(new[] { FakeShellLinks.ProfileDesktop }, shortcut.DesktopFolders());
    }
}
