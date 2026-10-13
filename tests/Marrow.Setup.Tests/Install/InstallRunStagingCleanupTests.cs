using Marrow.Core.Support;
using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): ED-NEW-inst-merge-13. U3's finished run deleted its staging folder with a direct recursive
// delete of Request.StagingFolder, while U9 (C7b) makes that folder a claimed, owner-marked unit that only its guarded,
// bounded cleanup may remove. The run now hands the folder back to the scope that claimed it. Fake Setup.exe, temp folders.
public sealed class InstallRunStagingCleanupTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_finished_run_hands_its_staging_folder_to_the_claiming_scope_which_removes_it_and_lets_go()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false);
        Assert.True(scenario.Scope.IsHeld);

        scenario.Play();

        Assert.True(scenario.Run.Evidence.Finished);
        Assert.False(Directory.Exists(scenario.Staging), "the scope's cleanup removed the engine, the options, the result and the log");
        Assert.False(scenario.Scope.IsHeld, "the claim is let go once the run is done");
        Assert.Empty(scenario.Run.StagingCleanup.Kept);
        Assert.True(scenario.Run.StagingCleanup.Removed > 0);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Something_the_cleanup_cannot_prove_is_nths_is_kept_and_reported_not_deleted()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false);
        var stranger = Path.Combine(scenario.Staging, "notes.txt");
        File.WriteAllText(stranger, "not nth's");

        scenario.Play();

        Assert.True(scenario.Run.Evidence.Finished);
        Assert.True(File.Exists(stranger), "a direct recursive delete would have taken it");
        Assert.Contains(scenario.Run.StagingCleanup.Kept, kept => kept.Path.StartsWith(scenario.Staging, StringComparison.OrdinalIgnoreCase));
        Assert.False(scenario.Scope.IsHeld, "the claim is let go even when something is kept");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_run_built_without_the_cleanup_deletes_nothing_from_its_path_alone()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false);
        File.WriteAllText(scenario.Request.OptionsPath, "{}");
        var bare = new InstallRun(scenario.Request, scenario.Runner, scenario.Disk, scenario.After, scenario.Time);

        bare.DeleteStaging();

        Assert.True(File.Exists(scenario.Request.OptionsPath));
        Assert.Same(SweepResult.Nothing, bare.StagingCleanup);
    }
}
