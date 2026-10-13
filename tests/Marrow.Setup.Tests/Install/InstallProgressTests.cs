using Marrow.Setup.Install;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4's table and "Rules", and the canvas's INSTALL and UPDATE lists; unit U3
// (ED-NEW-inst-u3-1). The steps and their places on the bar, which signal makes each step work, the byte count, the creep
// after 2 s that never reaches the next step, the stall line after 20 s, waiting without either, and 100 only once the
// setup's own work after exit 0 is done.
public sealed class InstallProgressTests
{
    private static readonly InstallSizes Sizes = new(118 * InstallSizes.Megabyte, 302 * InstallSizes.Megabyte, 420 * InstallSizes.Megabyte, 0, Measured: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_fresh_install_has_four_steps_and_an_update_three_on_the_canvas_s_ranges()
    {
        Assert.Equal([(InstallStep.Checking, 0d, 4d), (InstallStep.Copying, 4d, 86d), (InstallStep.Adding, 86d, 94d), (InstallStep.Finishing, 94d, 100d)],
            InstallProgress.RangesFor(update: false).Select(r => (r.Step, r.From, r.To)));
        Assert.Equal([(InstallStep.Checking, 0d, 6d), (InstallStep.Copying, 6d, 90d), (InstallStep.Finishing, 90d, 100d)],
            InstallProgress.RangesFor(update: true).Select(r => (r.Step, r.From, r.To)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_rows_say_what_happens_in_the_string_table_s_words()
    {
        var install = new InstallProgress(update: false, Sizes, desktopShortcut: true).Advance(new ProgressEvidence(), TimeSpan.Zero);
        Assert.Equal(["Checking your PC", "Copying nth", "Adding nth to Start and your desktop", "Finishing up"], install.Steps.Select(s => s.Label));
        var off = new InstallProgress(update: false, Sizes, desktopShortcut: false).Advance(new ProgressEvidence { Staging = true }, TimeSpan.Zero);
        Assert.Equal(["Preparing files", "Copying nth", "Adding nth to Start", "Finishing up"], off.Steps.Select(s => s.Label));
        var update = new InstallProgress(update: true, Sizes, desktopShortcut: true).Advance(new ProgressEvidence { FirstStepLabel = SetupStrings.StepClosing }, TimeSpan.Zero);
        Assert.Equal(["Saving and closing nth", "Copying the new version", "Finishing up"], update.Steps.Select(s => s.Label));
        Assert.Equal(SetupStrings.InstallingSubtitle, install.Subtitle);
    }

    [Theory]
    [InlineData(false, nameof(ProgressEvidence.SetupStarted), 1)]
    [InlineData(false, nameof(ProgressEvidence.CreatingShortcuts), 2)]
    [InlineData(false, nameof(ProgressEvidence.HookStarted), 2)]
    [InlineData(false, nameof(ProgressEvidence.ResultWritten), 2)]
    [InlineData(false, nameof(ProgressEvidence.HookEnded), 3)]
    [InlineData(false, nameof(ProgressEvidence.Completed), 3)]
    [InlineData(false, nameof(ProgressEvidence.ExitedZero), 3)]
    [InlineData(true, nameof(ProgressEvidence.SetupStarted), 1)]
    [InlineData(true, nameof(ProgressEvidence.CreatingShortcuts), 2)]
    [InlineData(true, nameof(ProgressEvidence.HookStarted), 2)]
    [InlineData(true, nameof(ProgressEvidence.HookEnded), 2)]
    [InlineData(true, nameof(ProgressEvidence.ExitedZero), 2)]
    [Trait("Decision", "ED-247")]
    public void Each_signal_makes_its_own_step_work_and_every_step_before_it_done(bool update, string signal, int step)
    {
        var evidence = new ProgressEvidence { SetupStarted = true };
        typeof(ProgressEvidence).GetProperty(signal)!.SetValue(evidence, true);
        var frame = new InstallProgress(update, Sizes, desktopShortcut: true).Advance(evidence, TimeSpan.Zero);
        Assert.Equal(step, frame.WorkingIndex);
        for (var i = 0; i < frame.Steps.Count; i++)
            Assert.Equal(i < step ? StepState.Done : i == step ? StepState.Working : StepState.Waiting, frame.Steps[i].State);
        var range = InstallProgress.RangesFor(update)[step];
        Assert.InRange(frame.Target, range.From, range.To - 1e-9);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Copying_is_the_bytes_in_packages_and_current_over_T_pkg_plus_T_app_and_its_count_is_in_T_disk_megabytes()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { SetupStarted = true };
        var first = progress.Advance(evidence, TimeSpan.Zero);
        Assert.Equal(4, first.Target, 6);
        Assert.Equal("0 of 420 MB", first.Steps[1].Detail);
        Assert.Equal("420 of 420 MB", first.Steps[1].DetailLongest);
        Assert.True(first.Steps[1].DetailShown);
        Assert.Null(first.Steps[0].Detail);
        evidence.CopiedBytes = Sizes.CopyBytes / 2;
        var half = progress.Advance(evidence, TimeSpan.FromSeconds(1));
        Assert.Equal(4 + 41, half.Target, 6);
        Assert.Equal("210 of 420 MB", half.Steps[1].Detail);
        evidence.CopiedBytes = Sizes.CopyBytes * 2; // more than measured: never past the next step's start
        Assert.Equal(86, progress.Advance(evidence, TimeSpan.FromSeconds(2)).Target, 6);
        Assert.False(new InstallProgress(false, Sizes, true).Advance(new ProgressEvidence(), TimeSpan.Zero).Steps[1].DetailShown, "the count waits with its step");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_moving_for_2_s_creeps_toward_the_next_step_slowing_and_never_reaching_it()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { SetupStarted = true, CopiedBytes = Sizes.CopyBytes / 4 };
        var start = progress.Advance(evidence, TimeSpan.Zero).Target;
        Assert.Equal(start, progress.Advance(evidence, TimeSpan.FromSeconds(2)).Target);
        var previous = start;
        var gain = double.MaxValue;
        for (var s = 3; s <= 3600; s += s < 60 ? 1 : 60)
        {
            var target = progress.Advance(evidence, TimeSpan.FromSeconds(s)).Target;
            Assert.True(target > previous || s > 300, $"no creep at {s} s");
            Assert.True(target < 86, $"{target} reached the next step at {s} s");
            if (s < 60)
            {
                Assert.True(target - previous <= gain + 1e-9, $"the creep sped up at {s} s");
                gain = target - previous;
            }
            previous = target;
        }
        Assert.True(previous - start > 10, "an hour's stall still shows life");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_changing_for_20_s_is_slow_until_the_next_change()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { SetupStarted = true, CopiedBytes = 1000 };
        progress.Advance(evidence, TimeSpan.Zero);
        Assert.False(progress.Advance(evidence, TimeSpan.FromSeconds(19.9)).Slow);
        var slow = progress.Advance(evidence, TimeSpan.FromSeconds(20));
        Assert.True(slow.Slow);
        Assert.Equal(SetupStrings.InstallingSlow, slow.Subtitle);
        evidence.CopiedBytes = 2000;
        Assert.False(progress.Advance(evidence, TimeSpan.FromSeconds(20.1)).Slow);
        Assert.False(progress.Advance(evidence, TimeSpan.FromSeconds(40)).Slow);
        Assert.True(progress.Advance(evidence, TimeSpan.FromSeconds(40.1)).Slow);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Waiting_on_nth_neither_creeps_nor_stalls()
    {
        var progress = new InstallProgress(update: true, Sizes, desktopShortcut: false);
        var evidence = new ProgressEvidence { Waiting = true, FirstStepLabel = SetupStrings.StepWaiting(2) };
        var first = progress.Advance(evidence, TimeSpan.Zero);
        var later = progress.Advance(evidence, TimeSpan.FromMinutes(10));
        Assert.Equal(first.Target, later.Target);
        Assert.False(later.Slow);
        Assert.Equal("Waiting for 2 threads to finish", later.Steps[0].Label);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Only_the_setup_s_own_work_after_exit_0_lets_the_target_reach_100_and_it_never_goes_back()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { SetupStarted = true, CopiedBytes = Sizes.CopyBytes, CreatingShortcuts = true, HookStarted = true, ResultWritten = true, HookEnded = true, Completed = true, ExitedZero = true };
        var exited = progress.Advance(evidence, TimeSpan.Zero);
        Assert.True(exited.Target < 100);
        Assert.Equal(InstallPhase.Running, exited.Phase);
        Assert.True(progress.Advance(evidence, TimeSpan.FromHours(1)).Target < 100, "creep never makes 100");
        evidence.Finished = true;
        var done = progress.Advance(evidence, TimeSpan.FromHours(1));
        Assert.Equal(100, done.Target);
        Assert.Equal(InstallPhase.Succeeded, done.Phase);
        Assert.All(done.Steps, s => Assert.Equal(StepState.Done, s.State));
        Assert.Equal("420 of 420 MB", done.Steps[1].Detail);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_failure_holds_the_bar_where_it_is()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { SetupStarted = true, CopiedBytes = Sizes.CopyBytes / 2 };
        var before = progress.Advance(evidence, TimeSpan.Zero).Target;
        evidence.Failed = true;
        var failed = progress.Advance(evidence, TimeSpan.FromMinutes(5));
        Assert.Equal(InstallPhase.Failed, failed.Phase);
        Assert.Equal(before, failed.Target);
        Assert.False(failed.Slow);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Before_Setup_exe_starts_the_first_step_shows_its_checks_and_the_staging()
    {
        var progress = new InstallProgress(update: false, Sizes, desktopShortcut: true);
        var evidence = new ProgressEvidence { CheckingFraction = 0.5, Staging = true };
        var frame = progress.Advance(evidence, TimeSpan.Zero);
        Assert.Equal(InstallPhase.Checking, frame.Phase);
        Assert.Equal(2, frame.Target, 6);
        Assert.Equal(SetupStrings.StepPreparing, frame.Steps[0].Label);
        Assert.Equal(StepState.Working, frame.Steps[0].State);
    }
}
