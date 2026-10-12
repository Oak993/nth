using Marrow.Core.Support;
using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 and 10 ("Progress", "Fake Velopack runner"), unit U3 (ED-NEW-inst-u3-1).
// Every scenario of the fake runner is played through the real InstallRun on a hand-driven clock (100 ms per look, the
// bar's spring at 60 Hz between looks). For every scenario: the bar never goes back, reaches 100 only after the uninstall
// key is re-pointed, never passes a step the fake has not made visible, and the steps go waiting, working, done in order.
// Then each scenario's own ending: the stall line, the reasons and second lines, the hook timeout, the upgrade's old files,
// the extract-and-rename, a window killed after exit 0, and the free-space check before anything runs.
public sealed class InstallScenarioTests
{
    [Theory]
    [MemberData(nameof(VelopackScenarios.Names), MemberType = typeof(VelopackScenarios))]
    [Trait("Decision", "ED-247")]
    public void The_bar_never_goes_back_never_overshoots_and_never_passes_a_step_it_cannot_see(string name)
    {
        using var scenario = Play(name);
        var ranges = scenario.Run.Progress.Ranges;
        double target = 0, bar = 0;
        foreach (var played in scenario.Frames)
        {
            var frame = played.Frame;
            Assert.True(frame.Target >= target - 1e-9, $"{name} at {played.Seconds:0.0} s: the target went back from {target} to {frame.Target}");
            Assert.True(played.Bar >= bar - 1e-9, $"{name} at {played.Seconds:0.0} s: the bar went back from {bar} to {played.Bar}");
            Assert.True(played.Bar <= frame.Target + 1e-6, $"{name} at {played.Seconds:0.0} s: the bar {played.Bar} is past its target {frame.Target}");
            Assert.True(frame.WorkingIndex <= played.Visible, $"{name} at {played.Seconds:0.0} s: step {frame.WorkingIndex} works but only {played.Visible} is visible");
            if (frame.Phase != InstallPhase.Succeeded)
                Assert.True(frame.Target <= ranges[played.Visible].To && frame.Target < 100, $"{name} at {played.Seconds:0.0} s: {frame.Target} passes step {played.Visible}'s end");
            target = frame.Target;
            bar = played.Bar;
        }
    }

    [Theory]
    [MemberData(nameof(VelopackScenarios.Names), MemberType = typeof(VelopackScenarios))]
    [Trait("Decision", "ED-247")]
    public void The_bar_reaches_100_only_after_the_uninstall_key_is_re_pointed(string name)
    {
        using var scenario = Play(name);
        foreach (var played in scenario.Frames)
            if (played.Frame.Target >= 100 || played.Bar >= 100) Assert.True(played.Repointed, $"{name} at {played.Seconds:0.0} s: 100 before the entry was re-pointed");
        var last = scenario.Frames[^1];
        if (Succeeds(name))
        {
            Assert.Equal(InstallPhase.Succeeded, last.Frame.Phase);
            Assert.Equal(100, last.Frame.Target);
            Assert.Equal(100, scenario.Bar.Value);
            Assert.Equal(100, scenario.Bar.Percent);
            Assert.Equal(1, scenario.After.Calls);
        }
        else
        {
            Assert.True(last.Frame.Target < 100);
            Assert.True(scenario.Bar.Value < 100);
            Assert.Equal(0, scenario.After.Calls);
        }
    }

    [Theory]
    [MemberData(nameof(VelopackScenarios.Names), MemberType = typeof(VelopackScenarios))]
    [Trait("Decision", "ED-247")]
    public void The_steps_go_waiting_working_done_in_order_and_never_back(string name)
    {
        using var scenario = Play(name);
        var best = new StepState[scenario.Run.Progress.Ranges.Count];
        foreach (var played in scenario.Frames)
        {
            var states = played.Frame.Steps.Select(s => s.State).ToArray();
            var working = Array.IndexOf(states, StepState.Working);
            if (working < 0) Assert.All(states, s => Assert.Equal(StepState.Done, s));
            else
            {
                Assert.All(states[..working], s => Assert.Equal(StepState.Done, s));
                Assert.All(states[(working + 1)..], s => Assert.Equal(StepState.Waiting, s));
            }
            for (var i = 0; i < states.Length; i++)
            {
                Assert.True(states[i] >= best[i], $"{name} at {played.Seconds:0.0} s: step {i} went back from {best[i]} to {states[i]}");
                best[i] = states[i];
            }
        }
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("default")]
    [InlineData("hookTimeout")]
    [InlineData("upgrade")]
    [InlineData("extractToTemp")]
    [Trait("Decision", "ED-247")]
    public void A_good_run_shows_every_step_working_on_the_way_and_ends_with_all_done_and_the_full_byte_count(string name)
    {
        using var scenario = Play(name);
        var steps = scenario.Run.Progress.Ranges.Count;
        for (var i = 1; i < steps; i++)
            Assert.Contains(scenario.Frames, f => f.Frame.WorkingIndex == i && f.Frame.Steps[i].State == StepState.Working);
        var last = scenario.Frames[^1].Frame;
        Assert.All(last.Steps, s => Assert.Equal(StepState.Done, s.State));
        var copying = last.Steps.Single(s => s.Step == InstallStep.Copying);
        Assert.Equal(SetupStrings.ProgressBytes(4, 4), copying.Detail);
        Assert.Equal(copying.DetailLongest, copying.Detail);
        Assert.False(last.Slow);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_25_s_stall_shows_the_slow_line_20_s_after_the_bytes_stop_and_takes_it_away_when_they_move()
    {
        using var scenario = Play("stall25");
        var frames = scenario.Frames;
        var lastMove = 0.0;
        var appeared = false;
        var leftWithBytes = false;
        for (var i = 1; i < frames.Count; i++)
        {
            var played = frames[i];
            if (played.Seconds < 0 || played.Frame.Phase == InstallPhase.Succeeded) continue;
            if (played.Copied != frames[i - 1].Copied)
            {
                if (frames[i - 1].Frame.Slow && !played.Frame.Slow) leftWithBytes = true;
                lastMove = played.Seconds;
                continue;
            }
            if (played.Frame.WorkingIndex != 1) continue;
            var idle = played.Seconds - lastMove;
            if (idle < 19.9) Assert.False(played.Frame.Slow, $"slow after only {idle:0.0} s");
            if (idle > 20.05) Assert.True(played.Frame.Slow, $"not slow after {idle:0.0} s");
            appeared |= played.Frame.Slow;
        }
        Assert.True(appeared, "the stall line never showed");
        Assert.True(leftWithBytes, "the stall line did not leave when the bytes moved");
        Assert.Equal(SetupStrings.InstallingSlow, frames.First(f => f.Frame.Slow).Frame.Subtitle);
        Assert.Equal(SetupStrings.InstallingSubtitle, frames[^1].Frame.Subtitle);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_slow_pc_with_8_s_pauses_never_shows_the_stall_line_and_the_bar_creeps_in_the_pauses_without_reaching_the_next_step()
    {
        using var scenario = Play("slow");
        Assert.DoesNotContain(scenario.Frames, f => f.Frame.Slow);
        var copying = scenario.Frames.Where(f => f.Frame.WorkingIndex == 1).ToList();
        Assert.True(copying.Max(f => f.Frame.Target) <= 86, "Copying never passes the next step's start");
        var crept = false;
        for (var i = 1; i < copying.Count; i++)
            crept |= copying[i].Frame.Target > copying[i - 1].Frame.Target && copying[i].Copied == copying[i - 1].Copied;
        Assert.True(crept, "the bar never crept while the bytes paused");
        // The creep is gentle: when the bytes come back the bar is never far ahead of them, so it soon moves again.
        var sizes = VelopackScenarios.Sizes;
        Assert.All(copying, f => Assert.True(f.Frame.Target - (4 + 82.0 * Math.Min(f.Copied, sizes.CopyBytes) / sizes.CopyBytes) <= 15, $"{f.Frame.Target} runs ahead of the bytes at {f.Seconds:0.0} s"));
    }

    [Theory]
    [InlineData("filesInUse", FailureReason.InUse, true)]
    [InlineData("accessDenied", FailureReason.Blocked, true)]
    [InlineData("crashMidCopy", FailureReason.Other, false)]
    [InlineData("restoreFailed", FailureReason.Other, false)]
    [InlineData("restoreOk", FailureReason.Other, true)]
    [InlineData("velopackNoSpace", FailureReason.NoSpace, true)]
    [Trait("Decision", "ED-247")]
    public void A_failed_run_names_its_reason_and_says_nothing_was_changed_only_when_that_is_true(string name, FailureReason reason, bool unchanged)
    {
        using var scenario = Play(name);
        var failure = Assert.IsType<InstallFailure>(scenario.Run.Failure);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(unchanged, failure.NothingChanged);
        Assert.Equal(unchanged ? SetupStrings.ProblemSafe : SetupStrings.ProblemLeft, failure.SecondLine);
        Assert.Equal(InstallPhase.Failed, scenario.Frames[^1].Frame.Phase);
        Assert.NotEmpty(failure.Log);
        if (reason == FailureReason.NoSpace) Assert.StartsWith("There isn’t enough free space on drive ", failure.Sentence, StringComparison.Ordinal);
        if (reason == FailureReason.InUse) Assert.Equal(SetupStrings.ProblemInUse, failure.Sentence);
        if (reason == FailureReason.Blocked) Assert.Equal(SetupStrings.ProblemBlocked, failure.Sentence);
        Assert.True(Directory.Exists(scenario.Staging), "Problem keeps the log for Copy details");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_that_overruns_is_waited_out_the_install_finishes_and_Ready_reads_no_result_as_failed()
    {
        using var scenario = Play("hookTimeout");
        Assert.Equal(InstallPhase.Succeeded, scenario.Frames[^1].Frame.Phase);
        Assert.Equal(InstallResult.Missing, scenario.Run.Result);
        var adding = scenario.Frames.Where(f => f.Frame.WorkingIndex == 2).ToList();
        Assert.True(adding.Count > 250, "the 30 s hook is waited for at the Adding step");
        Assert.True(adding.Max(f => f.Frame.Target) < 94);
        Assert.Contains(adding, f => f.Frame.Slow);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_upgrade_never_counts_the_old_install_s_files_and_the_rollback_folder_is_gone_at_the_end()
    {
        using var scenario = Play("upgrade");
        var beforeRename = scenario.Frames.Where(f => f.Seconds is >= 0 and < 0.35).ToList();
        Assert.NotEmpty(beforeRename);
        Assert.All(beforeRename, f => Assert.True(f.Frame.Target < 6.5, $"{f.Frame.Target} before Velopack renamed the old root"));
        Assert.Equal(InstallPhase.Succeeded, scenario.Frames[^1].Frame.Phase);
        Assert.Empty(InstallFailure.RollbackFolders(scenario.Root));
        Assert.Equal(["Checking your PC", "Copying the new version", "Finishing up"], scenario.Frames[^1].Frame.Steps.Select(s => s.Label));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Files_extracted_through_a_temporary_folder_and_renamed_are_counted_once_and_never_go_back()
    {
        using var scenario = Play("extractToTemp");
        var copied = scenario.Frames.Select(f => f.Copied).ToList();
        for (var i = 1; i < copied.Count; i++) Assert.True(copied[i] >= copied[i - 1]);
        Assert.Equal(VelopackScenarios.Sizes.CopyBytes, scenario.Run.Evidence.CopiedBytes);
        Assert.True(Directory.Exists(Path.Combine(scenario.Root, "current")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_window_killed_after_exit_0_never_shows_100_and_leaves_Velopack_s_key_for_nth_s_next_start_to_fix()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("killedAfterExit"), update: false).Play(finish: false);
        Assert.True(scenario.Run.NeedsFinish);
        Assert.True(scenario.Frames.Max(f => f.Frame.Target) < 100);
        Assert.False(scenario.Hive.IsRepointed(scenario.Root));
        Assert.Contains("Update.exe", scenario.Hive.Values["UninstallString"], StringComparison.Ordinal);
        Assert.True(Directory.Exists(scenario.Staging), "the staging folder is left for the next run's sweep");
        // nth's next start runs the same re-point (unit U5's self-heal, ED-NEW-inst-u5-1); here its stand-in.
        scenario.After.Run(scenario.Root, scenario.Request.ResultPath);
        Assert.True(scenario.Hive.IsRepointed(scenario.Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Not_enough_free_space_fails_before_anything_runs_with_nothing_changed()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false, free: 112 * InstallSizes.Megabyte, sizes: new InstallSizes(118 * InstallSizes.Megabyte, 302 * InstallSizes.Megabyte, 420 * InstallSizes.Megabyte, 0, true)).Play();
        Assert.Equal(0, scenario.Runner.Starts);
        var failure = Assert.IsType<InstallFailure>(scenario.Run.Failure);
        Assert.Equal(FailureReason.NoSpace, failure.Reason);
        Assert.True(failure.NothingChanged);
        Assert.Equal("There isn’t enough free space on drive " + FreeSpaceCheck.Drive(scenario.Root) + ". nth needs about 420 MB, and 112 MB is free.", failure.Sentence);
        Assert.False(File.Exists(scenario.Request.OptionsPath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Setup_exe_is_started_once_silently_with_the_log_the_root_and_the_options_file_the_hook_reads()
    {
        using var scenario = Play("fast");
        var command = Assert.IsType<Marrow.Setup.Support.VelopackCommand>(scenario.Runner.Command);
        Assert.Equal(1, scenario.Runner.Starts);
        Assert.Equal(scenario.Request.EnginePath, command.Executable);
        Assert.Equal(["--silent", "--log", scenario.Request.LogPath, "--installto", scenario.Root], command.Arguments);
        Assert.Equal(scenario.Request.OptionsPath, command.Environment[InstallOptions.EnvironmentVariable]);
        Assert.False(Directory.Exists(scenario.Staging), "the staging folder is deleted once the install is complete");
        Assert.Equal(DesktopShortcutOutcome.Created, scenario.Run.Result!.DesktopShortcut);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_options_file_asks_the_hook_for_the_shortcut_choice_before_Setup_exe_starts()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false, desktopShortcut: false);
        scenario.Run.CheckSpace();
        scenario.Run.Start();
        Assert.Equal(InstallOptions.ForInstall(false), InstallOptions.Read(scenario.Request.OptionsPath));
        scenario.Runner.Advance(0.5);
        Assert.Equal(SetupStrings.StepAddingStart, scenario.Run.Tick().Steps[2].Label);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_step_after_exit_that_throws_shows_its_failure_without_certifying_completion()
    {
        using var scenario = new InstallScenario(VelopackScenarios.Build("fast"), update: false);
        scenario.After.Throws = true;
        scenario.Play();
        Assert.Equal(1, scenario.After.Calls);
        Assert.Equal(InstallPhase.Failed, scenario.Frames[^1].Frame.Phase);
        Assert.Equal("the shell refused", scenario.Run.Failure!.Sentence);
        Assert.Equal(InstallStep.Finishing, scenario.Run.Failure.Step);
        Assert.Equal(0, scenario.Run.Failure.ExitCode);
        Assert.False(scenario.Run.Failure.NothingChanged);
        Assert.False(scenario.Run.Evidence.Finished);
        Assert.Null(scenario.Run.Result);
        Assert.True(scenario.Run.Frame.Target < 100);
        Assert.True(Directory.Exists(scenario.Staging));
        Assert.False(scenario.Hive.IsRepointed(scenario.Root));
        Assert.Contains("Update.exe", scenario.Hive.Values["UninstallString"], StringComparison.Ordinal);
        Assert.Contains("Message: the shell refused", scenario.Run.Failure.Details("1.1.0", "Windows 11", 1, "x64", null), StringComparison.Ordinal);
    }

    private static bool Succeeds(string name) => name is "default" or "fast" or "slow" or "stall25" or "hookTimeout" or "upgrade" or "extractToTemp";

    private static InstallScenario Play(string name)
    {
        var finish = name != "killedAfterExit";
        return new InstallScenario(VelopackScenarios.Build(name), VelopackScenarios.IsUpgrade(name)).Play(finish: finish);
    }
}
