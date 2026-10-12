using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (steps 4 to 8) and 4; unit U3 (ED-NEW-inst-u3-1). Install goes to
// Installing in the same window at once; Close works while it checks and is paused (with sign-out and shutdown held) from
// the moment Setup.exe starts; after the exit and the setup's own step Close works again, the bar fills and Ready shows
// 0.65 s later (0.25 s in Calm). Not enough space goes to Problem and Setup.exe never starts; a failure goes to Problem
// with its reason and second line; Retry runs the same install again; the update draws on Update itself. A fake Setup.exe
// acts out each timeline in a temp folder; nothing real is ever started.
[Collection("SetupHeadless")]
public sealed class InstallFlowTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Install_goes_to_Installing_at_once_and_Close_still_works_while_it_checks()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"));
            rig.Session.ChooseInstall();

            Assert.Equal(SetupScreen.Installing, rig.Session.Screen);
            Assert.True(rig.Flow.Busy);
            Assert.False(rig.Paused);
            Assert.Equal(4, rig.Panel.Rows.Count);
            Assert.Equal(StepState.Working, rig.Panel.Rows[0].Step!.State);
            Assert.All(rig.Panel.Rows.Skip(1), row => Assert.Equal(StepState.Waiting, row.Step!.State));
            Assert.Equal(0, rig.Runner.Starts);

            rig.Window.Window.Close();
            Assert.False(rig.Window.Window.IsVisible);
            rig.Staged();
            rig.Step();
            Assert.False(rig.Poll.Running);
            Assert.Equal(0, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void From_Setup_s_start_Close_is_paused_then_the_bar_fills_after_the_setup_s_own_step_and_Ready_follows()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"));
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Step();

            Assert.Equal(1, rig.Runner.Starts);
            Assert.True(rig.Paused);
            Assert.Equal(SetupStrings.ShutdownInstalling, rig.Window.Blocker.Reason);
            rig.Window.Window.Close();
            Assert.True(rig.Window.Window.IsVisible, "Close is paused while Setup.exe runs");
            Assert.Equal(1, rig.Window.Window.Guard.Refusals);

            var fullAt = -1.0;
            var readyAt = -1.0;
            var clock = 0.0;
            rig.Until(() => !rig.Poll.Running, each: () =>
            {
                Assert.True(rig.Panel.Bar.Value < 100 || rig.Scenario.Hive.IsRepointed(rig.Scenario.Root), "100 before the uninstall key points at nth");
            });
            Assert.Equal(1, rig.Scenario.After.Calls);
            Assert.False(rig.Paused);
            Assert.Null(rig.Window.Blocker.Reason);
            Assert.NotNull(rig.Session.Result);
            Assert.False(Directory.Exists(rig.Scenario.Staging), "the staging folder is deleted after the install");
            for (var frame = 0; frame < 300 && readyAt < 0; frame++)
            {
                rig.Window.Frames.Frame();
                clock += 1.0 / 60;
                if (fullAt < 0 && rig.Panel.Bar.Value >= 100) fullAt = clock;
                if (rig.Session.Screen == SetupScreen.Ready) readyAt = clock;
            }

            Assert.True(fullAt >= 0, "the bar reached 100");
            Assert.InRange(readyAt - fullAt, InstallFlow.ReadyAfter - 0.02, InstallFlow.ReadyAfter + 0.05);
            Assert.Equal("100%", rig.Panel.PercentText);
            Assert.All(rig.Panel.Rows, row => Assert.Equal(StepState.Done, row.Step!.State));
            Assert.False(rig.Flow.Busy);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void In_Calm_Ready_follows_the_full_bar_a_quarter_second_later()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"), look: WindowRig.Calm);
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);
            Assert.Equal(100, rig.Panel.Bar.Value);
            var frames = 0;
            while (rig.Session.Screen != SetupScreen.Ready && frames < 120)
            {
                rig.Window.Frames.Frame();
                frames++;
            }
            Assert.InRange(frames / 60.0, InstallFlow.ReadyAfterCalm - 0.02, InstallFlow.ReadyAfterCalm + 0.05);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_enough_space_it_goes_to_Problem_and_Setup_never_starts()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"), free: 1_000_000);
            rig.Session.ChooseInstall();

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            Assert.Equal(FailureReason.NoSpace, rig.Session.Failure!.Reason);
            Assert.StartsWith("There isn’t enough free space on drive ", rig.Session.ProblemReason, StringComparison.Ordinal);
            Assert.Equal(SetupStrings.ProblemSafe, rig.Session.ProblemSecondLine);
            Assert.Equal(0, rig.Poll.Starts);
            Assert.Equal(0, rig.Runner.Starts);
            Assert.False(rig.Flow.Busy);
            Assert.False(rig.Paused);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_failure_goes_to_Problem_with_its_reason_and_second_line_and_Close_works_again()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("accessDenied"));
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            var failure = rig.Session.Failure!;
            Assert.Equal(FailureReason.Blocked, failure.Reason);
            Assert.Equal(SetupStrings.ProblemBlocked, rig.Session.ProblemReason);
            Assert.Equal(failure.SecondLine, rig.Session.ProblemSecondLine);
            Assert.False(rig.Paused);
            Assert.Null(rig.Window.Blocker.Reason);
            Assert.False(rig.Flow.Busy);
            Assert.Equal(0, rig.Scenario.After.Calls);
            Assert.True(Directory.Exists(rig.Scenario.Staging), "the engine stays for Retry");
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Retry_runs_the_same_install_again_from_Problem()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("accessDenied")) { RetryTimeline = VelopackScenarios.Build("fast") };
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);
            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);

            rig.Session.Retry();
            Assert.Equal(SetupScreen.Installing, rig.Session.Screen);
            Assert.Equal(2, rig.Runs.Count);
            Assert.Equal(StepState.Working, rig.Panel.Rows[0].Step!.State);
            Assert.True(rig.Panel.Bar.Value < 5, "a fresh run starts its bar again");
            rig.Staged();
            rig.Until(() => !rig.Poll.Running);
            rig.Window.Run(2);

            Assert.Equal(1, rig.Runner.Starts);
            Assert.Equal(SetupScreen.Ready, rig.Session.Screen);
            Assert.Equal(1, rig.Scenario.After.Calls);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_payload_that_cannot_be_staged_goes_to_Problem_before_Setup_starts()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"), payload: false);
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Step();

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            Assert.Equal(FailureReason.Other, rig.Session.Failure!.Reason);
            Assert.Equal(SetupStrings.ProblemSafe, rig.Session.ProblemSecondLine);
            Assert.Equal(0, rig.Runner.Starts);
            Assert.False(rig.Paused);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Setup_waits_for_the_checks_before_it_and_a_no_goes_back_to_the_route_s_screen()
    {
        headless.OnUiThread(() =>
        {
            var answer = new TaskCompletionSource<bool>();
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"), beforeEngine: _ => answer.Task);
            rig.Session.ChooseInstall();
            rig.Staged();
            for (var i = 0; i < 10; i++) rig.Step();
            Assert.Equal(0, rig.Runner.Starts);
            Assert.False(rig.Paused);

            answer.SetResult(false);
            rig.Step();
            rig.Window.Run(1);
            Assert.Equal(SetupScreen.Welcome, rig.Session.Screen);
            Assert.False(rig.Flow.Busy);
            Assert.Equal(0, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_yes_from_the_checks_starts_Setup_on_the_next_turn()
    {
        headless.OnUiThread(() =>
        {
            var answer = new TaskCompletionSource<bool>();
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"), beforeEngine: _ => answer.Task);
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Step();
            Assert.Equal(0, rig.Runner.Starts);
            answer.SetResult(true);
            rig.Step();
            Assert.Equal(1, rig.Runner.Starts);
            Assert.True(rig.Paused);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Install_asked_twice_starts_one_run()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"));
            rig.Session.ChooseInstall();
            rig.Flow.Begin();
            rig.Session.ChooseInstall();
            Assert.Single(rig.Runs);
            Assert.Equal(1, rig.Poll.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_update_draws_its_three_steps_on_Update_itself()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("upgrade"), update: true);
            Assert.False(rig.Panel.IsVisible);
            rig.Session.ChooseInstall();

            Assert.Equal(SetupScreen.Update, rig.Session.Screen);
            Assert.True(rig.Panel.IsVisible);
            Assert.Equal([SetupStrings.StepChecking, SetupStrings.StepCopyingNew, SetupStrings.StepFinishing], rig.Panel.Rows.Select(r => r.Label));
            rig.Staged();
            rig.Step();
            Assert.True(rig.Paused);
            rig.Until(() => !rig.Poll.Running);
            rig.Window.Run(2);

            Assert.Equal(SetupScreen.Ready, rig.Session.Screen);
            Assert.True(rig.Scenario.Hive.IsRepointed(rig.Scenario.Root));
        });
    }
}
