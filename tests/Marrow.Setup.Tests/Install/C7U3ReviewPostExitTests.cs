using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

// C7 independent review: only fakes and disposable folders; no installer or app process.
public sealed class C7U3ReviewPostExitTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_failed_post_exit_step_cannot_certify_100_before_the_key_is_repointed()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false);
        scenario.After.Throws = true;
        scenario.Play();
        Assert.Equal(1, scenario.After.Calls);
        Assert.False(scenario.Hive.IsRepointed(scenario.Root));
        Assert.Contains("Update.exe", scenario.Hive.Values["UninstallString"], StringComparison.Ordinal);
        Assert.True(scenario.Run.Frame.Target < 100, "100 certifies the post-exit work although the key still points to Update.exe");
    }
}
