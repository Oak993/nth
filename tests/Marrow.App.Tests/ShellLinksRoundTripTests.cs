using System.Runtime.ExceptionServices;
using Marrow.App.Port;
using Marrow.Core.Support;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a and 10, unit U5 (ED-NEW-inst-u5-1). The real ShellLinks (IShellLinkW,
// IPersistFile, IPropertyStore, IShellLinkDataList through ComWrappers) on real .lnk files, ONLY in %TEMP%\nth-tests\<guid>\:
// every field written and read back (the ToastActivatorCLSID and nth's marker included), the AppUserModelID re-stamped
// alone with every other property surviving, Velopack's own rewrite cycle, a renamed link found again by its marker, the
// %LOCALAPPDATA% raw form read back, move, delete, the writer lock, and DesktopShortcut end to end. Every test body runs on
// a fresh MTA thread, so each one also proves the calls move to ShellLinks' own STA thread. The known folders point into
// the temporary folder; the last test checks that the real Desktop and Start menu nth links are untouched.
public sealed class ShellLinksRoundTripTests : IDisposable
{
    private static readonly Guid ToastClsid = new("5D2C3F33-8E5A-4A8B-9C4F-7B1E2A3C4D5E");

    private readonly string temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;

    public ShellLinksRoundTripTests()
    {
        Directory.CreateDirectory(Desktop);
        Directory.CreateDirectory(Programs);
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(Path.Combine(Root, "current"));
        File.WriteAllBytes(CurrentExe, []);
    }

    public void Dispose()
    {
        try { Directory.Delete(temp, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // a temporary folder; Windows cleans %TEMP%
    }

    private string Desktop => Path.Combine(temp, "Desktop");

    private string Programs => Path.Combine(temp, "Programs");

    private string Profile => Path.Combine(temp, "Profile");

    private string Root => Path.Combine(temp, "Programs-nth");

    private string CurrentExe => Path.Combine(Root, "current", "nth.exe");

    // Everything but %LOCALAPPDATA% (read only, so the stored %LOCALAPPDATA% form expands for real) points into the temporary folder.
    private ShellLinks NewShell() => new(new Dictionary<ShellKnownFolder, string>
    {
        [ShellKnownFolder.Desktop] = Desktop,
        [ShellKnownFolder.Programs] = Programs,
        [ShellKnownFolder.Profile] = Profile,
        [ShellKnownFolder.RoamingAppData] = Path.Combine(temp, "Roaming"),
    });

    private ShellLink Full() => new(
        Target: CurrentExe,
        Arguments: "--profile \"work one\"",
        WorkingDirectory: Path.Combine(Root, "current"),
        IconPath: CurrentExe,
        IconIndex: 2,
        Description: "nth",
        ShowCommand: ShellLinkKeys.ShowNormal,
        AppUserModelId: ShellLinkKeys.NthAppUserModelId,
        ToastActivatorClsid: ToastClsid,
        CreatedBy: ShellLinkKeys.CreatedByValue);

    // Each test body runs on a new MTA thread (hook processes and pool threads are MTA); ShellLinks must move to its STA.
    private static void OnFreshMtaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
                body();
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the test body did not finish");
        failure?.Throw();
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_field_round_trips_through_a_real_link_on_ShellLinks_own_STA_thread()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            Assert.Equal(ApartmentState.STA, shell.CallApartment);
            var path = Path.Combine(Desktop, "nth.lnk");

            shell.WriteLink(path, Full());

            Assert.True(File.Exists(path));
            Assert.Equal(Full(), shell.ReadLink(path));
            Assert.True(shell.ReadLink(path).IsMadeByNth);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_link_without_the_optional_properties_reads_them_back_as_absent()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var path = Path.Combine(Desktop, "plain.lnk");
            var plain = new ShellLink(CurrentExe, "", Path.Combine(Root, "current"));

            shell.WriteLink(path, plain);

            var read = shell.ReadLink(path);
            Assert.Equal(plain, read);
            Assert.Null(read.AppUserModelId);
            Assert.Null(read.ToastActivatorClsid);
            Assert.Null(read.CreatedBy);
            Assert.False(read.IsMadeByNth);
        });
    }

    [Fact]
    [Trait("Decision", "ED-220")]
    public void Restamping_only_the_AppUserModelID_keeps_the_ToastActivatorCLSID_the_marker_and_every_other_field()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var path = Path.Combine(Programs, "nth.lnk");
            shell.WriteLink(path, Full() with { AppUserModelId = "velopack.NthApp" });

            shell.UpdateLink(path, new ShellLinkChange(AppUserModelId: ShellLinkKeys.NthAppUserModelId));

            Assert.Equal(Full(), shell.ReadLink(path));
        });
    }

    [Fact]
    [Trait("Decision", "ED-220")]
    public void DesktopShortcut_restamps_a_Velopack_Start_link_through_the_real_shell_and_the_CLSID_survives()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var start = Path.Combine(Programs, "nth.lnk");
            var velopack = new ShellLink(CurrentExe, "", Path.Combine(Root, "current"), CurrentExe, 0, AppUserModelId: "velopack.NthApp", ToastActivatorClsid: ToastClsid);
            shell.WriteLink(start, velopack);

            var result = new DesktopShortcut(shell, Root).RestampStart();

            Assert.Equal(new StartLinkResult(Found: true, Restamped: true), result);
            Assert.Equal(velopack with { AppUserModelId = ShellLinkKeys.NthAppUserModelId }, shell.ReadLink(start));
            Assert.Equal(new StartLinkResult(Found: true), new DesktopShortcut(shell, Root).RestampStart());
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Velopacks_rewrite_of_icon_and_AppUserModelID_keeps_nths_marker_and_the_self_heal_undoes_it()
    {
        // VELOPACK_CHECK.md check 1: after an update Velopack loads every link into the root, sets an absolute icon and its
        // own AppUserModelID, commits and saves. The same load-set-commit-save cycle, then nth's repair.
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var shortcut = new DesktopShortcut(shell, Root);
            Assert.Equal(DesktopShortcutOutcome.Created, shortcut.Create().DesktopShortcut);
            var path = Path.Combine(Desktop, "nth.lnk");
            var made = shell.ReadLink(path);

            shell.UpdateLink(path, new ShellLinkChange(IconPath: CurrentExe, IconIndex: 0, AppUserModelId: "velopack.NthApp"));

            var rewritten = shell.ReadLink(path);
            Assert.Equal(ShellLinkKeys.CreatedByValue, rewritten.CreatedBy);
            Assert.Equal("velopack.NthApp", rewritten.AppUserModelId);
            Assert.Equal(CurrentExe, rewritten.IconPath);
            Assert.Equal(made.Target, rewritten.Target);

            Assert.Equal(1, shortcut.Repair(ShortcutScan.Full).Changed);
            Assert.Equal(made, shell.ReadLink(path));
            Assert.Equal(0, shortcut.Repair(ShortcutScan.Full).Changed);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_renamed_link_is_found_again_by_its_marker_and_removed()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var shortcut = new DesktopShortcut(shell, Root);
            shortcut.Create();
            var renamed = Path.Combine(Desktop, "My nth.lnk");
            File.Move(Path.Combine(Desktop, "nth.lnk"), renamed);
            shell.WriteLink(Path.Combine(Desktop, "someone else.lnk"), new ShellLink(CurrentExe));

            var found = shortcut.FindMade(ShortcutScan.Full);

            Assert.False(found.Capped);
            Assert.Equal(new[] { renamed }, found.Links.Select(link => link.Path));
            Assert.True(found.Links[0].Link.IsMadeByNth);
            Assert.Equal(1, shortcut.RemoveMade().Changed);
            Assert.False(File.Exists(renamed));
            Assert.True(File.Exists(Path.Combine(Desktop, "someone else.lnk")));
            Assert.Equal(1, shortcut.RemoveIntoRoot().Changed);
            Assert.Empty(Directory.EnumerateFiles(Desktop));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_LOCALAPPDATA_path_is_stored_raw_and_read_back_raw()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var path = Path.Combine(Desktop, "nth.lnk");
            var raw = new ShellLink(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", "", @"%LOCALAPPDATA%\Programs\nth\current",
                @"%LOCALAPPDATA%\Programs\nth\current\nth.exe", 0, "nth", AppUserModelId: ShellLinkKeys.NthAppUserModelId, CreatedBy: ShellLinkKeys.CreatedByValue);

            shell.WriteLink(path, raw);

            var read = shell.ReadLink(path);
            Assert.Equal(raw, read);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(Path.Combine(local, @"Programs\nth\current\nth.exe"), shell.ExpandEnvironment(read.Target), ignoreCase: true);

            // an absolute target set later replaces the raw form (the shell drops its EXP_SZ block); the raw icon stays
            shell.UpdateLink(path, new ShellLinkChange(Target: CurrentExe, WorkingDirectory: Path.Combine(Root, "current")));
            Assert.Equal(raw with { Target = CurrentExe, WorkingDirectory = Path.Combine(Root, "current") }, shell.ReadLink(path));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void DesktopShortcut_stores_its_own_target_in_the_LOCALAPPDATA_form_when_the_root_is_under_it()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var shortcut = new DesktopShortcut(shell, Root);

            Assert.Equal(DesktopShortcutOutcome.Created, shortcut.Create().DesktopShortcut);

            var link = shell.ReadLink(Path.Combine(Desktop, "nth.lnk"));
            Assert.Equal(shortcut.EnvironmentForm(CurrentExe), link.Target);
            Assert.Equal(CurrentExe, shell.ExpandEnvironment(link.Target), ignoreCase: true);
            Assert.Equal(Path.Combine(Root, "current"), shell.ExpandEnvironment(link.WorkingDirectory), ignoreCase: true);
            Assert.Equal(shortcut.Expected(shortcut.CurrentLaunchTarget()), link);
            Assert.Equal(DesktopShortcutOutcome.Kept, shortcut.Create().DesktopShortcut);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Links_are_listed_moved_without_replacing_and_deleted_and_an_empty_folder_removed()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var old = Directory.CreateDirectory(Path.Combine(Programs, "nth")).FullName;
            var from = Path.Combine(old, "nth.lnk");
            var to = Path.Combine(Programs, "nth.lnk");
            shell.WriteLink(from, Full());
            File.WriteAllText(Path.Combine(Programs, "notes.txt"), "not a link");

            Assert.Equal(new[] { from }, shell.EnumerateLinks(old, "nth*.lnk").Select(link => link.Path));
            Assert.Empty(shell.EnumerateLinks(Programs));
            Assert.Empty(shell.EnumerateLinks(Path.Combine(temp, "missing")));
            Assert.False(shell.DeleteEmptyFolder(old));

            shell.MoveLink(from, to);
            Assert.Equal(Full(), shell.ReadLink(to));
            shell.WriteLink(from, Full() with { Arguments = "--other" });
            Assert.ThrowsAny<IOException>(() => shell.MoveLink(from, to));
            Assert.Equal(Full(), shell.ReadLink(to));

            shell.DeleteLink(from);
            shell.DeleteLink(from);
            Assert.False(shell.FileExists(from));
            Assert.True(shell.DeleteEmptyFolder(old));
            Assert.False(Directory.Exists(old));
            Assert.False(shell.DeleteEmptyFolder(old));
            Assert.NotNull(shell.FolderWriteTimeUtc(Programs));
            Assert.Null(shell.FolderWriteTimeUtc(old));
            shell.Notify(ShellChange.Updated, to);
            _ = shell.DesktopIconsHidden();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void One_writer_at_a_time_holds_the_named_lock()
    {
        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            using (var held = shell.TryEnterWriter(TimeSpan.FromSeconds(2)))
            {
                Assert.NotNull(held);
                var otherGotIt = true;
                var thread = new Thread(() =>
                {
                    using var other = shell.TryEnterWriter(TimeSpan.FromMilliseconds(200));
                    otherGotIt = other is not null;
                });
                thread.Start();
                thread.Join();
                Assert.False(otherGotIt);
            }
            using var after = shell.TryEnterWriter(TimeSpan.FromSeconds(2));
            Assert.NotNull(after);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_disposed_shell_refuses_calls_and_a_missing_link_fails_with_a_shell_error()
    {
        OnFreshMtaThread(() =>
        {
            var shell = NewShell();
            var error = Assert.ThrowsAny<Exception>(() => shell.ReadLink(Path.Combine(Desktop, "missing.lnk")));
            Assert.True(DesktopShortcut.IsShellFailure(error), error.GetType().Name);
            shell.Dispose();
            shell.Dispose();
            Assert.Throws<ObjectDisposedException>(() => shell.ReadLink(Path.Combine(Desktop, "missing.lnk")));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_full_create_repair_and_remove_cycle_leaves_the_real_Desktop_and_Start_menu_untouched()
    {
        var realDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var realPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var before = NthLinks(realDesktop, realPrograms);

        OnFreshMtaThread(() =>
        {
            using var shell = NewShell();
            var shortcut = new DesktopShortcut(shell, Root);
            Assert.Equal(DesktopShortcutOutcome.Created, shortcut.Create().DesktopShortcut);
            shortcut.Repair();
            shortcut.RestampStart();
            shortcut.MoveOldStartLink();
            Assert.Equal(1, shortcut.RemoveMade().Changed);
        });

        Assert.Equal(before, NthLinks(realDesktop, realPrograms));
    }

    // A read-only listing (names and write times) of nth's links in the real folders; nothing is opened.
    private static List<string> NthLinks(params string[] folders) =>
    [
        .. folders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "nth*.lnk", SearchOption.TopDirectoryOnly)
                .Concat(Directory.Exists(Path.Combine(folder, "nth")) ? Directory.EnumerateFiles(Path.Combine(folder, "nth"), "*.lnk") : []))
            .Select(file => file + "|" + File.GetLastWriteTimeUtc(file).Ticks)
            .Order(StringComparer.OrdinalIgnoreCase),
    ];
}
