using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (steps 4 and 8) and 5 ("Leftovers"), unit U9 (ED-NEW-inst-u9-1). A run's staging
// folder is made and claimed on first use, held while the run uses it, and deleted whole by Release (the options file, the result
// and the engine included); a folder an earlier run left is emptied first, so a stale result is never read as this run's; one in
// use by a program that is still running is left for the next sweep; the sweep at the setup's start keeps the run's own folder.
// Temporary folders only; the process-wide exit handlers are not hooked here (the exit-path tests call them directly).
public sealed class StagingScopeTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    private StagingScope Scope(string version = "1.0.0") => StagingScope.Create(area.LocalAppData, area.Temp, version, lastChance: false);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_folder_is_the_staging_folder_the_options_file_and_the_hook_use()
    {
        using var scope = Scope("1.0.0");

        Assert.Equal(InstallOptions.StagingFolder(area.LocalAppData, "1.0.0"), scope.Folder);
        Assert.Equal(Path.Combine(area.SetupRoot, "1.0.0"), scope.Folder);
        Assert.False(Directory.Exists(scope.Folder), "nothing is made until something is staged");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Ensure_makes_and_claims_the_folder_and_staging_goes_into_it()
    {
        using var scope = Scope();

        var folder = scope.Ensure();
        InstallOptions.ForInstall(desktopShortcut: true).Write(InstallOptions.DefaultOptionsPath(area.LocalAppData, "1.0.0"));

        Assert.Equal(scope.Folder, folder);
        Assert.True(scope.IsHeld);
        Assert.True(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
        Assert.True(File.Exists(Path.Combine(folder, InstallOptions.OptionsFileName)));
        Assert.Same(folder, scope.Ensure()); // asking again changes nothing
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void While_the_run_holds_its_folder_a_sweep_by_anything_else_keeps_it_whole()
    {
        using var scope = Scope();
        var folder = scope.Ensure();
        var options = area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        var atNthStart = SetupSweeper.SweepUnlessSetupRuns(area.LocalAppData, area.Temp, new Fakes.FakeSetupProbe(running: false));

        Assert.Equal(folder, Assert.Single(result.Kept).Path);
        Assert.Equal(folder, Assert.Single(atNthStart.Kept).Path);
        Assert.Equal("{}", File.ReadAllText(options));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Release_deletes_the_folder_with_the_options_the_result_and_the_engine_and_then_the_setup_folder()
    {
        using var scope = Scope();
        var folder = scope.Ensure();
        area.Put(Path.Combine(folder, InstallOptions.OptionsFileName));
        area.Put(Path.Combine(folder, InstallOptions.ResultFileName));
        area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        area.Put(Path.Combine(folder, "setup.log"));

        var result = scope.Release();

        Assert.True(result.Clean);
        Assert.Equal(1, result.Removed);
        Assert.False(scope.IsHeld);
        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Release_leaves_the_setup_folder_while_another_runs_build_is_in_it()
    {
        var library = area.PutInSetup("runtime", "4f9c2a", "libSkiaSharp.dll");
        using var loaded = area.Hold(library);
        using var scope = Scope();
        _ = scope.Ensure();

        _ = scope.Release();

        Assert.False(Directory.Exists(scope.Folder));
        Assert.True(File.Exists(library));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Release_can_be_called_again_and_before_anything_was_staged()
    {
        using var scope = Scope();
        Assert.Equal(0, scope.Release().Removed);

        _ = scope.Ensure();
        Assert.Equal(1, scope.Release().Removed);
        Assert.Equal(0, scope.Release().Removed);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_Release_a_Retry_makes_the_folder_again()
    {
        using var scope = Scope();
        _ = scope.Ensure();
        _ = scope.Release();

        var again = scope.Ensure();

        Assert.True(scope.IsHeld);
        Assert.True(File.Exists(Path.Combine(again, SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_folder_an_earlier_run_left_is_emptied_first_so_a_stale_result_is_never_read()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.MarkOwnedSetup("1.0.0");
        var stale = area.Put(Path.Combine(folder, InstallOptions.ResultFileName), """{"version":1,"desktopShortcut":"created"}""");
        area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        using var scope = Scope();

        _ = scope.Ensure();

        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(Path.Combine(folder, SetupFolders.EngineFileName)));
        Assert.True(File.Exists(Path.Combine(folder, SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_setup_of_the_same_version_cannot_take_a_folder_that_is_held()
    {
        using var first = Scope();
        var folder = first.Ensure();
        var options = area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");
        using var second = Scope();

        Assert.Throws<IOException>(() => second.Ensure());

        Assert.True(File.Exists(options), "the first run keeps its folder");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_program_still_running_from_the_folder_keeps_it_whole_and_the_next_sweep_takes_it()
    {
        using var scope = Scope();
        var folder = scope.Ensure();
        var engine = area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        var options = area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");
        var running = area.Hold(engine);

        var result = scope.Release();

        Assert.Equal(SweepKeptReason.InUse, Assert.Single(result.Kept).Reason);
        Assert.Equal("{}", File.ReadAllText(options)); // the install hook still needs it
        area.Release(running);
        Assert.True(SetupSweeper.Sweep(area.LocalAppData, area.Temp).Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_sweep_at_start_removes_stale_folders_and_keeps_the_runs_own()
    {
        area.PutInSetup("0.9.0", "nth Setup Engine.exe");
        area.PutInSetup("runtime", "81be07", "libSkiaSharp.dll");
        area.PutInUninstall("nth Uninstall.exe");
        using var scope = Scope();
        var folder = scope.Ensure();

        var result = await scope.SweepStaleAsync();

        Assert.Equal(3, result.Removed);
        Assert.Equal(folder, Assert.Single(result.Kept).Path);
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "0.9.0")));
        Assert.False(Directory.Exists(area.UninstallTemp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_sweep_at_start_may_run_before_the_first_use_without_harming_it()
    {
        area.PutInSetup("0.9.0", "nth Setup Engine.exe");
        using var scope = Scope();

        var swept = await scope.SweepStaleAsync();
        var folder = scope.Ensure();

        Assert.Equal(1, swept.Removed);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_sweep_and_the_first_use_in_any_order_never_cost_the_run_its_files()
    {
        for (var round = 0; round < 30; round++)
        {
            area.PutInSetup("1.0.0", "install-result.json"); // an earlier run's folder, the same version
            area.PutInSetup("0.9.0", "nth Setup Engine.exe");
            using var scope = Scope();

            var sweep = scope.SweepStaleAsync();
            var folder = scope.Ensure();
            var options = area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");
            _ = await sweep;

            Assert.True(File.Exists(options), $"round {round}: the sweep took the run's options file");
            Assert.False(File.Exists(Path.Combine(folder, InstallOptions.ResultFileName)), $"round {round}: a stale result was left");
            Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "0.9.0")));
            _ = scope.Release();
        }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_build_without_a_version_has_nowhere_to_stage_and_nothing_to_release()
    {
        using var scope = Scope("");

        Assert.Null(scope.Folder);
        Assert.Throws<InvalidOperationException>(() => scope.Ensure());
        Assert.Equal(0, scope.Release().Removed);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("relative")]
    public void The_scope_needs_full_paths(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => StagingScope.Create(path, area.Temp, "1.0.0", lastChance: false));
        Assert.ThrowsAny<ArgumentException>(() => StagingScope.Create(area.LocalAppData, path, "1.0.0", lastChance: false));
    }
}
