using Marrow.Core.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers") and 8 (uninstall flow), unit U9 (ED-NEW-inst-u9-1). The three
// places that sweep: nth's own start and its before-uninstall hook (only when no setup runs: SweepUnlessSetupRuns), the
// uninstaller after Velopack has run (its own temporary copy is running, so that folder is kept and reported), and a run's own
// folder (RemoveFolder, which also removes the folders it leaves empty). Temporary folders only.
public sealed class SetupSweeperEntryPointsTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nth_at_its_start_removes_the_leftovers_when_no_setup_runs()
    {
        area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));
        var chats = area.Put(Path.Combine(area.LocalAppData, "nth", "chats.db"));
        var probe = new FakeSetupProbe(running: false);

        var result = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, probe);

        Assert.Equal(3, result.Removed);
        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
        Assert.False(Directory.Exists(area.UninstallTemp));
        Assert.True(File.Exists(chats));
        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nth_at_its_start_leaves_everything_while_a_setup_or_the_uninstaller_is_open()
    {
        var staged = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        var library = area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        var copy = area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));
        var probe = new FakeSetupProbe(running: true);

        var result = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, probe);

        Assert.Equal(0, result.Removed);
        Assert.Empty(result.Kept);
        Assert.All(new[] { staged, library, copy }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nth_at_its_start_does_not_even_look_for_a_setup_when_there_is_nothing_to_sweep()
    {
        var probe = new FakeSetupProbe(running: false);

        var result = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, probe);

        Assert.Same(SweepResult.Nothing, result);
        Assert.Equal(0, probe.Asked);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sweep_at_nth_start_still_keeps_a_folder_whose_files_are_in_use()
    {
        var engine = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        using var running = area.Hold(engine);

        var result = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, new FakeSetupProbe(running: false));

        Assert.Equal(SweepKeptReason.InUse, Assert.Single(result.Kept).Reason);
        Assert.True(File.Exists(engine));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_probe_is_required()
    {
        area.PutInSetup("1.0.0", "x");
        Assert.Throws<ArgumentNullException>(() => SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, null!));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstaller_removes_the_setup_folder_and_its_temporary_folder_when_it_is_not_itself_running_there()
    {
        area.PutInSetup("1.0.0", "install-options.json");
        area.PutInSetup("1.0.0", "install-result.json");
        area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
        Assert.False(Directory.Exists(area.UninstallTemp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstaller_running_from_its_temporary_copy_finds_that_folder_in_use_and_reports_it_for_the_caller()
    {
        var copy = area.Put(Path.Combine(area.UninstallTemp, "nth Uninstall.exe"));
        area.Put(Path.Combine(area.UninstallTemp, "readme.txt"));
        area.PutInSetup("1.0.0", "install-options.json");
        using var running = area.Hold(copy);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        var kept = Assert.Single(result.Kept);
        Assert.Equal(area.UninstallTemp, kept.Path);
        Assert.Equal(SweepKeptReason.InUse, kept.Reason);
        Assert.True(File.Exists(Path.Combine(area.UninstallTemp, "readme.txt")), "a folder in use is kept whole");
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_uninstaller_own_native_libraries_in_the_setup_folder_are_reported_so_the_caller_can_deal_with_them()
    {
        var library = area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        area.PutInSetup("1.0.0", "install-options.json");
        using var loaded = area.Hold(library);

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(Path.Combine(area.SetupRoot, "runtime", "4f9c2a"), Assert.Single(result.Kept).Path);
        Assert.False(result.Clean);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_run_removes_its_own_folder_and_the_setup_folder_when_that_is_all_there_was()
    {
        var staging = Path.GetDirectoryName(area.PutInSetup("1.0.0", "install-options.json"))!;
        area.PutInSetup("1.0.0", "install-result.json");

        var result = SetupSweeper.RemoveFolder(area.LocalAppData, staging);

        Assert.Equal(1, result.Removed);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_run_removing_its_own_folder_leaves_the_setup_folder_while_something_else_is_there()
    {
        var staging = Path.GetDirectoryName(area.PutInSetup("1.0.0", "install-options.json"))!;
        var other = area.PutInSetup("0.9.0", "install-options.json");

        _ = SetupSweeper.RemoveFolder(area.LocalAppData, staging);

        Assert.False(Directory.Exists(staging));
        Assert.True(File.Exists(other));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_run_removing_a_runtime_build_also_removes_the_runtime_folder_when_it_is_empty()
    {
        var build = Path.GetDirectoryName(area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll"))!;

        _ = SetupSweeper.RemoveFolder(area.LocalAppData, build);

        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_folder_in_use_is_not_removed_by_the_run_that_asks_and_neither_are_its_parents()
    {
        var engine = area.PutInSetup("1.0.0", "nth Setup Engine.exe");
        using var running = area.Hold(engine);

        var result = SetupSweeper.RemoveFolder(area.LocalAppData, Path.GetDirectoryName(engine)!);

        Assert.Equal(SweepKeptReason.InUse, Assert.Single(result.Kept).Reason);
        Assert.True(File.Exists(engine));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("nth")]
    [InlineData(@"nth Setup")]
    [InlineData(@"nth Setup\..\nth")]
    [InlineData(@"..\temp")]
    public void A_run_can_only_remove_a_folder_inside_the_setup_folder(string relative)
    {
        var target = Path.GetFullPath(Path.Combine(area.LocalAppData, relative));
        if (relative.Length == 0) target = relative;
        Directory.CreateDirectory(area.SetupRoot);
        Directory.CreateDirectory(Path.Combine(area.LocalAppData, "nth"));

        Assert.ThrowsAny<ArgumentException>(() => SetupSweeper.RemoveFolder(area.LocalAppData, target));

        Assert.True(Directory.Exists(Path.Combine(area.LocalAppData, "nth")));
        Assert.True(Directory.Exists(area.SetupRoot));
    }
}
