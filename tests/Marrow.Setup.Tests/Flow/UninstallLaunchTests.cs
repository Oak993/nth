using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 ("Uninstall flow": "runs from a temp copy, because Velopack's uninstaller
// stops processes running from the install folder"; "The temp copy deletes itself"), unit U6 (ED-NEW-inst-u6-1). In a temporary
// folder: the root is found in the plan's order; only a run from inside the root hands off, and never the copy itself; the copy
// carries nothing but uninstall mode, the root, its mark and --silent; it is written whole under %TEMP%\nth Uninstall (a name of
// its own when the usual one is held), started in its own folder under the folder's claim; and only the copy asks Windows to
// remove it at restart (no test reaches the real MoveFileEx).
public sealed class UninstallLaunchTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_root_is_installto_then_the_programs_own_folder_then_the_entry_then_NthApp()
    {
        using var area = new UninstallArea();
        var keys = new FakeUninstallKeys();
        var elsewhere = Path.Combine(area.Sweep.Base, "Downloads", "nth Setup.exe");
        var other = Directory.CreateDirectory(Path.Combine(area.Sweep.Base, "D", "nth")).FullName;

        Assert.Equal(other, UninstallLaunch.FindRoot(["--uninstall", "--installto", other + @"\"], Path.Combine(area.Root, "nth Uninstall.exe"), keys, area.LocalAppData, area.Sweep.Base));
        Assert.Equal(area.Root, UninstallLaunch.FindRoot(["--uninstall"], Path.Combine(area.Root, "nth Uninstall.exe"), keys, area.LocalAppData, area.Sweep.Base));
        keys.Locations[InstallLayout.AppId] = "\"" + other + "\"";
        Assert.Equal(other, UninstallLaunch.FindRoot(["--uninstall"], elsewhere, keys, area.LocalAppData, area.Sweep.Base));
        keys.Locations[InstallLayout.AppId] = Path.Combine(area.Sweep.Base, "gone");
        Assert.Equal(Path.Combine(area.LocalAppData, InstallLayout.AppId), UninstallLaunch.FindRoot(["--uninstall"], elsewhere, keys, area.LocalAppData, area.Sweep.Base));
        keys.Locations[InstallLayout.AppId] = "relative";
        Assert.Equal(Path.Combine(area.LocalAppData, InstallLayout.AppId), UninstallLaunch.FindRoot(["--uninstall"], null, keys, area.LocalAppData, area.Sweep.Base));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Only_a_run_from_inside_the_root_hands_off_and_the_copy_never_does()
    {
        var root = WindowRig.Root;
        Assert.True(UninstallLaunch.NeedsCopy(["--uninstall"], root + @"\nth Uninstall.exe", root));
        Assert.True(UninstallLaunch.NeedsCopy(["--uninstall"], root + @"\current\nth Setup.exe", root));
        Assert.False(UninstallLaunch.NeedsCopy(["--uninstall", UninstallLaunch.CopyFlag], root + @"\nth Uninstall.exe", root));
        Assert.False(UninstallLaunch.NeedsCopy(["--uninstall"], @"C:\Users\Ana\Downloads\nth Setup.exe", root));
        Assert.False(UninstallLaunch.NeedsCopy(["--uninstall"], root + @"2\nth Uninstall.exe", root));
        Assert.False(UninstallLaunch.NeedsCopy(["--uninstall"], null, root));
        Assert.True(UninstallLaunch.IsCopy(["--uninstall", "--installto", root, UninstallLaunch.CopyFlag]));
        Assert.False(UninstallLaunch.IsCopy(["--uninstall", "--", UninstallLaunch.CopyFlag]));
        Assert.False(UninstallLaunch.IsCopy(["--uninstall", "--UNINSTALL-COPY"]));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_copy_carries_uninstall_mode_the_root_its_mark_and_silent_only()
    {
        Assert.Equal(["--uninstall", "--installto", WindowRig.Root, "--uninstall-copy"], UninstallLaunch.CopyArguments(WindowRig.Root, silent: false));
        Assert.Equal(["--uninstall", "--installto", WindowRig.Root, "--uninstall-copy", "--silent"], UninstallLaunch.CopyArguments(WindowRig.Root, silent: true));
        Assert.Equal("nth Uninstall.exe", UninstallLaunch.CopyFileName);
        Assert.Equal(TimeSpan.FromSeconds(10), UninstallLaunch.HandOffPatience);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_copy_is_written_whole_and_takes_a_name_of_its_own_when_the_usual_one_is_held()
    {
        using var area = new UninstallArea();
        var program = Path.Combine(area.Root, InstallLayout.RootUninstaller);
        var folder = area.Sweep.UninstallTemp;

        var first = UninstallLaunch.WriteCopy(program, area.Temp);
        Assert.Equal(Path.Combine(folder, "nth Uninstall.exe"), first);
        Assert.Equal("uninstaller", File.ReadAllText(first));
        var again = UninstallLaunch.WriteCopy(program, area.Temp); // an old copy that has exited is replaced
        Assert.Equal(first, again);

        area.Sweep.Hold(first); // a copy that still runs
        var second = UninstallLaunch.WriteCopy(program, area.Temp);
        Assert.NotEqual(first, second);
        Assert.Matches(@"^nth Uninstall [0-9a-f]{8}\.exe$", Path.GetFileName(second));
        Assert.Equal("uninstaller", File.ReadAllText(second));
        Assert.DoesNotContain(Directory.EnumerateFiles(folder), file => file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_hand_off_claims_the_folder_and_starts_the_copy_in_its_own_folder()
    {
        using var area = new UninstallArea();
        var program = Path.Combine(area.Root, InstallLayout.RootUninstaller);
        var started = new List<(string Path, IReadOnlyList<string> Arguments, string Folder, bool Claimed)>();

        var copy = UninstallLaunch.HandOff(program, area.Root, area.Temp, silent: true, (path, arguments, folder) =>
        {
            started.Add((path, arguments, folder, IsClaimed(area.Sweep.UninstallTemp)));
            return true;
        });

        var run = Assert.Single(started);
        Assert.Equal(copy, run.Path);
        Assert.Equal(Path.Combine(area.Sweep.UninstallTemp, "nth Uninstall.exe"), run.Path);
        Assert.Equal(UninstallLaunch.CopyArguments(area.Root, silent: true), run.Arguments);
        Assert.Equal(area.Sweep.UninstallTemp, run.Folder);
        Assert.True(run.Claimed); // no sweep could take the copy before it ran
        Assert.False(IsClaimed(area.Sweep.UninstallTemp)); // the claim goes as the first process exits
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_copy_that_cannot_start_or_be_written_leaves_the_first_run_to_go_on_in_place()
    {
        using var area = new UninstallArea();
        var program = Path.Combine(area.Root, InstallLayout.RootUninstaller);
        Assert.Null(UninstallLaunch.HandOff(program, area.Root, area.Temp, silent: false, (_, _, _) => false));
        Assert.Null(UninstallLaunch.HandOff(program, area.Root, area.Temp, silent: false, (_, _, _) => throw new System.ComponentModel.Win32Exception(5)));
        Assert.Null(UninstallLaunch.HandOff(Path.Combine(area.Root, "missing.exe"), area.Root, area.Temp, silent: false, (_, _, _) => true));
        using var claim = FolderClaim.Take(area.Sweep.UninstallTemp); // another uninstaller is handing off right now
        Assert.Null(UninstallLaunch.HandOff(program, area.Root, area.Temp, silent: false, (_, _, _) => true));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Only_the_copy_asks_Windows_to_remove_it_at_restart_and_its_folder_after_it()
    {
        using var area = new UninstallArea();
        var copy = Path.Combine(area.Sweep.UninstallTemp, "nth Uninstall.exe");
        var asked = new List<string>();

        Assert.True(UninstallLaunch.ScheduleCopyRemoval(copy, area.Temp, path => { asked.Add(path); return true; }));
        Assert.Equal([copy, area.Sweep.UninstallTemp], asked);

        asked.Clear();
        Assert.False(UninstallLaunch.ScheduleCopyRemoval(copy, area.Temp, path => { asked.Add(path); return false; })); // not an administrator
        Assert.Equal([copy], asked); // the folder is left for the sweeps too

        asked.Clear();
        Assert.False(UninstallLaunch.ScheduleCopyRemoval(Path.Combine(area.Root, "nth Uninstall.exe"), area.Temp, path => { asked.Add(path); return true; }));
        Assert.False(UninstallLaunch.ScheduleCopyRemoval(null, area.Temp, path => { asked.Add(path); return true; }));
        Assert.False(UninstallLaunch.ScheduleCopyRemoval(area.Sweep.UninstallTemp, area.Temp, path => { asked.Add(path); return true; }));
        Assert.Empty(asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_restart_removal_is_reached_only_from_the_program_and_never_from_a_test()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "Marrow.sln"))) repository = repository.Parent!;
        var setup = Path.Combine(repository.FullName, "src", "Marrow.Setup");
        var callers = Directory.EnumerateFiles(setup, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("Restart" + "Removal.DeleteAtRestart", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(setup, file))
            .ToList();
        Assert.Equal(["Program.cs"], callers);
        var tests = Path.Combine(repository.FullName, "tests", "Marrow.Setup.Tests");
        Assert.DoesNotContain(Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories).Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)),
            file => File.ReadAllText(file).Contains("Restart" + "Removal.", StringComparison.Ordinal));
    }

    private static bool IsClaimed(string folder)
    {
        try
        {
            using var claim = FolderClaim.Take(folder);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
