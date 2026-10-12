using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (step 4), 5 ("Leftovers") and 10 ("Uninstall"), unit U9 (ED-NEW-inst-u9-1).
// The sweep that the setup runs at its start and the uninstaller at its end: stale staging folders, old runtime builds and loose
// files under %LOCALAPPDATA%\nth Setup go; a folder with a file that is open or loaded is kept whole (so a run in progress never
// loses its options file) and reported; nothing outside the two folders is touched; read-only files do not stop it; a second
// sweep finds nothing. Every file lives in a temporary folder standing in for %LOCALAPPDATA% and %TEMP%.
public sealed class SetupSweeperTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stale_staging_folders_old_runtime_builds_and_loose_files_all_go_and_the_empty_setup_folder_with_them()
    {
        area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        area.PutInSetup("1.0.0", "install-options.json");
        area.PutInSetup("0.9.0", "nth Setup Engine.exe");
        area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        area.PutInSetup("runtime", "81be07", "libHarfBuzzSharp.dll");
        area.PutInSetup("setup.log");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(5, result.Removed); // two staging folders, two builds, one loose file
        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_staging_folder_with_a_file_that_is_open_is_kept_whole_and_every_other_stale_folder_goes()
    {
        var engine = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        var options = area.Put(Path.Combine(area.SetupRoot, "1.0.0", "install-options.json"), "{}");
        area.PutInSetup("0.9.0", "nth Setup Engine.exe");
        using var running = area.Hold(engine);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        var kept = Assert.Single(result.Kept);
        Assert.Equal(Path.Combine(area.SetupRoot, "1.0.0"), kept.Path);
        Assert.Equal(SweepKeptReason.InUse, kept.Reason);
        Assert.True(File.Exists(options), "the options file of a run in progress must survive");
        Assert.Equal("{}", File.ReadAllText(options));
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "0.9.0")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Once_nothing_holds_the_file_the_next_sweep_takes_the_folder_and_the_setup_folder()
    {
        var engine = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        var running = area.Hold(engine);
        Assert.False(SetupSweeper.Sweep(area.LocalAppData, area.Temp).Clean);

        area.Release(running);
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_library_in_use_keeps_its_build_folder_and_the_runtime_folder_while_other_builds_go()
    {
        var library = area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        area.PutInSetup("runtime", "4f9c2a", "libHarfBuzzSharp.dll");
        area.PutInSetup("runtime", "81be07", "libSkiaSharp.dll");
        using var loaded = area.Hold(library);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.Equal(Path.Combine(area.SetupRoot, "runtime", "4f9c2a"), Assert.Single(result.Kept).Path);
        Assert.True(File.Exists(Path.Combine(area.SetupRoot, "runtime", "4f9c2a", "libHarfBuzzSharp.dll")));
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "runtime", "81be07")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_folder_a_running_setup_has_claimed_is_kept_whole()
    {
        var staging = Path.Combine(area.SetupRoot, "1.0.0");
        area.PutInSetup("1.0.0", "install-options.json");
        using var claim = FolderClaim.Take(staging);
        area.PutInSetup("0.9.0", "install-options.json");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(staging, Assert.Single(result.Kept).Path);
        Assert.True(File.Exists(Path.Combine(staging, "install-options.json")));
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "0.9.0")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_claim_left_by_a_run_that_died_is_just_a_file_and_the_folder_goes()
    {
        area.PutInSetup("1.0.0", SetupFolders.ClaimFileName);
        area.PutInSetup("1.0.0", "nth Setup Engine.exe");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Read_only_files_and_folders_do_not_stop_the_sweep()
    {
        var locked = area.PutInSetup("1.0.0", "install-result.json");
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        var loose = area.PutInSetup("old.log");
        File.SetAttributes(loose, FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(area.SetupRoot, "1.0.0"), FileAttributes.ReadOnly | FileAttributes.Directory);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nested_folders_in_a_staging_folder_go_with_it()
    {
        area.PutInSetup("1.0.0", "logs", "2026", "install.log");
        area.PutInSetup("1.0.0", "logs", "install.log");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_outside_the_two_folders_is_touched_not_the_data_folder_the_install_root_or_a_look_alike()
    {
        var chats = area.Put(Path.Combine(area.LocalAppData, "nth", "chats.db"), "the user's chats");
        var settings = area.Put(Path.Combine(area.LocalAppData, "nth", "settings.json"));
        var updater = area.Put(Path.Combine(area.LocalAppData, "NthApp", "Update.exe"));
        var program = area.Put(Path.Combine(area.LocalAppData, "Programs", "nth", "nth.exe"));
        var lookAlike = area.Put(Path.Combine(area.LocalAppData, "nth Setup2", "x.txt"));
        var backup = area.Put(Path.Combine(area.LocalAppData, "nth Setup backup", "x.txt"));
        var otherTemp = area.Put(Path.Combine(area.Temp, "other", "x.txt"));
        var tempLookAlike = area.Put(Path.Combine(area.Temp, "nth Uninstall2", "x.txt"));
        var setupFile = area.PutInSetup("1.0.0", "install-options.json");
        var tempCopy = area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(result.Clean);
        Assert.False(File.Exists(setupFile));
        Assert.False(File.Exists(tempCopy));
        foreach (var kept in new[] { chats, settings, updater, program, lookAlike, backup, otherTemp, tempLookAlike })
            Assert.True(File.Exists(kept), $"{kept} is not ours to remove");
        Assert.Equal("the user's chats", File.ReadAllText(chats));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_sweep_finds_nothing_and_a_missing_setup_folder_is_not_made()
    {
        area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));
        Assert.Equal(2, SetupSweeper.Sweep(area.LocalAppData, area.Temp).Removed);

        var again = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(0, again.Removed);
        Assert.True(again.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
        Assert.False(Directory.Exists(area.UninstallTemp));
        Assert.Empty(again.Kept);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData(@"..\up")]
    public void A_folder_that_is_not_a_full_path_is_refused_before_anything_is_looked_at(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => SetupSweeper.Sweep(path, area.Temp));
        Assert.ThrowsAny<ArgumentException>(() => SetupSweeper.Sweep(area.LocalAppData, path));
    }
}
