using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Screens;

[Collection("SetupHeadless")]
public sealed class C7U3PostExitFlowTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_post_exit_failure_shows_Problem_with_its_plain_message_and_Retry_can_finish()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast")) { RetryTimeline = VelopackScenarios.Build("fast") };
            rig.Scenario.After.Throws = true;
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);
            rig.Window.Run(2);

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            Assert.Equal("the shell refused", rig.Session.ProblemReason);
            Assert.False(rig.Paused);
            Assert.False(rig.Flow.Busy);
            Assert.True(rig.Flow.Run!.Frame.Target < 100);
            Assert.False(rig.Flow.Run!.Evidence.Finished);
            Assert.Null(rig.Session.Result);
            Assert.True(Directory.Exists(rig.Scenario.Staging), "keep the engine and diagnostics for Retry");

            rig.Scenario.After.Throws = false;
            rig.Session.Retry();
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);
            rig.Window.Run(2);
            Assert.Equal(SetupScreen.Ready, rig.Session.Screen);
            Assert.Equal(100, rig.Flow.Run!.Frame.Target);
        });
    }
}
