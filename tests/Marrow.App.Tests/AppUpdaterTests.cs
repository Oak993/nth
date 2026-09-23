using Marrow.App;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests;

public sealed class AppUpdaterTests
{
    [Fact]
    [Trait("Req", "PR-UPDATE-031")] // App/AppUpdater.swift:8-20
    public void The_progress_track_uses_the_literal_milestones_and_dot_order()
    {
        Assert.Equal(0.05, UpdateProgressMilestones.PreparingCeiling);
        Assert.Equal(0.55, UpdateProgressMilestones.DownloadEnd);
        Assert.Equal(0.70, UpdateProgressMilestones.VerifyEnd);
        Assert.Equal(0.82, UpdateProgressMilestones.StagingCeiling);
        Assert.Equal(0.86, UpdateProgressMilestones.HandoffEnd);
        Assert.Equal(0.96, UpdateProgressMilestones.InstallDot);
        Assert.Equal(1.0, 1.0);
        Assert.Equal(new[] { 0.55, 0.70, 0.86, 0.96 }, UpdateProgressMilestones.Dots);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-032")] // App/AppUpdater.swift:40-55,72-85
    public void Progress_visibility_and_running_predicates_follow_the_phase()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.False(progress.ShowsProgressUI);
            Assert.False(progress.IsInstallRunning);

            progress.Begin();
            Assert.Equal(UpdateInstallProgress.Phase.Preparing, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.True(progress.IsInstallRunning);

            progress.NoteDownloadProgress(1);
            Assert.Equal(UpdateInstallProgress.Phase.Downloading, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.True(progress.IsInstallRunning);

            progress.BeginVerification();
            Assert.Equal(UpdateInstallProgress.Phase.Verifying, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.True(progress.IsInstallRunning);

            progress.BeginStaging();
            Assert.Equal(UpdateInstallProgress.Phase.Staging, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.True(progress.IsInstallRunning);

            progress.NoteHandoff();
            Assert.Equal(UpdateInstallProgress.Phase.Handoff, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.True(progress.IsInstallRunning);

            progress.ArmCelebration();
            Assert.True(progress.ConsumePendingCelebration());
            Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
            Assert.True(progress.ShowsProgressUI);
            Assert.False(progress.IsInstallRunning);
            progress.CompleteCelebration();
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);

            progress.Begin();
            progress.Fail();
            Assert.Equal(UpdateInstallProgress.Phase.Failed, progress.UpdateInstallProgressPhase);
            Assert.False(progress.ShowsProgressUI);
            Assert.False(progress.IsInstallRunning);
        });

        runtime.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Equal(UpdateInstallProgress.Phase.Idle, runtime.OnMain(() => progress.UpdateInstallProgressPhase));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-033")] // App/AppUpdater.swift:89-95
    public void Begin_resets_the_raw_fraction_and_starts_the_preparing_creep()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.ArmCelebration();
            Assert.True(progress.ConsumePendingCelebration());
            progress.FinishCelebrationSweep();
            Assert.Equal(1.0, progress.Fraction);

            progress.Begin();
            Assert.Equal(UpdateInstallProgress.Phase.Preparing, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.004, progress.Fraction);
        });

        runtime.AdvanceMilliseconds(125);
        Assert.Equal(0.0063, runtime.OnMain(() => progress.Fraction), 12);
        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:99-113
    public void Download_reports_map_to_the_download_track_and_unknown_size_creeps()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.Begin();
            progress.NoteDownloadProgress(0);
            Assert.Equal(UpdateInstallProgress.Phase.Downloading, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.004, progress.Fraction);
        });

        runtime.AdvanceMilliseconds(125);
        Assert.Equal(0.004 + (0.50 - 0.004) * 0.012, runtime.OnMain(() => progress.Fraction), 12);

        runtime.OnMain(() =>
        {
            progress.NoteDownloadProgress(0.4);
            Assert.Equal(0.25, progress.Fraction);
            progress.NoteDownloadProgress(0.2);
            Assert.Equal(0.25, progress.Fraction);
            progress.NoteDownloadProgress(0.8);
            Assert.Equal(0.45, progress.Fraction);
            progress.NoteDownloadProgress(2);
            Assert.Equal(0.55, progress.Fraction);

            progress.BeginVerification();
            var fraction = progress.Fraction;
            progress.NoteDownloadProgress(1);
            Assert.Equal(fraction, progress.Fraction);
        });
        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-035")] // App/AppUpdater.swift:115-121
    public void Begin_verification_lands_on_download_end_and_ignores_calls_outside_an_install()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.BeginVerification();
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.0, progress.Fraction);

            progress.Begin();
            progress.NoteDownloadProgress(0.8);
            progress.BeginVerification();
            Assert.Equal(UpdateInstallProgress.Phase.Verifying, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.55, progress.Fraction);
        });

        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-036")] // App/AppUpdater.swift:123-129
    public void Begin_staging_lands_on_the_verification_milestone()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.Begin();
            progress.NoteDownloadProgress(1);
            progress.BeginStaging();
            Assert.Equal(UpdateInstallProgress.Phase.Staging, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.70, progress.Fraction);
        });

        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-037")] // App/AppUpdater.swift:131-136,199-201
    public void Handoff_lands_on_its_milestone_only_while_an_install_is_running()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.NoteHandoff();
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.0, progress.Fraction);

            progress.Begin();
            progress.NoteHandoff();
            Assert.Equal(UpdateInstallProgress.Phase.Handoff, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.86, progress.Fraction);
        });

        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:139-149,180-185
    public void Failure_resets_after_600_milliseconds_unless_a_new_begin_cancels_it()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.Begin();
            progress.Fail();
            Assert.Equal(UpdateInstallProgress.Phase.Failed, progress.UpdateInstallProgressPhase);
        });

        runtime.AdvanceMilliseconds(599);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, runtime.OnMain(() => progress.UpdateInstallProgressPhase));
        runtime.AdvanceMilliseconds(1);
        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.0, progress.Fraction);

            progress.Begin();
            progress.Fail();
            progress.Begin();
        });

        runtime.AdvanceMilliseconds(600);
        runtime.OnMain(() =>
        {
            Assert.Equal(UpdateInstallProgress.Phase.Preparing, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.004, progress.Fraction);
        });
        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:192-202
    public void Progress_is_monotonic_and_the_celebration_sweep_completes_without_reversing()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() =>
        {
            progress.Begin();
            progress.NoteDownloadProgress(1);
            Assert.Equal(0.55, progress.Fraction);

            progress.NoteDownloadProgress(0.5);
            Assert.Equal(0.55, progress.Fraction);
            progress.NoteDownloadProgress(2);
            Assert.Equal(0.55, progress.Fraction);

            progress.BeginStaging();
            Assert.Equal(0.70, progress.Fraction);
            progress.NoteHandoff();
            Assert.Equal(0.86, progress.Fraction);

            progress.ArmCelebration();
            Assert.True(progress.ConsumePendingCelebration());
            Assert.Equal(0.86, progress.Fraction);
            progress.FinishCelebrationSweep();
            Assert.Equal(1.0, progress.Fraction);
            progress.FinishCelebrationSweep();
            Assert.Equal(1.0, progress.Fraction);
            progress.CompleteCelebration();
            Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
            Assert.Equal(0.0, progress.Fraction);
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:204-225
    public void Creep_ticks_every_125_milliseconds_and_updates_its_existing_ceiling_and_rate()
    {
        using var runtime = new TestRuntime();
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(progress.Begin);
        runtime.AdvanceMilliseconds(125);
        var firstTick = 0.004 + (0.05 - 0.004) * 0.05;
        Assert.Equal(firstTick, runtime.OnMain(() => progress.Fraction), 12);

        runtime.OnMain(() => progress.NoteDownloadProgress(0));
        runtime.AdvanceMilliseconds(125);
        var secondTick = firstTick + (0.50 - firstTick) * 0.012;
        Assert.Equal(secondTick, runtime.OnMain(() => progress.Fraction), 12);
        Reset(progress, runtime);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-041")] // App/AppUpdater.swift:261-265
    public void Install_is_a_no_op_in_a_development_build()
    {
        using var runtime = new TestRuntime();
        using var appScope = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>(), isDevelopment: true));
        var progress = runtime.OnMain(() => UpdateInstallProgress.Shared);
        Reset(progress, runtime);

        runtime.OnMain(() => AppUpdater.Shared.Install(null!));

        Assert.Equal(UpdateInstallProgress.Phase.Idle, runtime.OnMain(() => progress.UpdateInstallProgressPhase));
        Assert.Equal(0.0, runtime.OnMain(() => progress.Fraction));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-054")] // App/AppUpdater.swift:242,254-259
    public void Relaunch_marker_is_consumed_once_and_a_stale_version_is_rejected()
    {
        using var runtime = new TestRuntime();
        using var appScope = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>(), isDevelopment: false));
        var updater = runtime.OnMain(() => AppUpdater.Shared);
        var version = AppInfo.Current.Version;
        const string key = "appUpdater_pendingRelaunchVersion";

        runtime.Defaults.Set(key, version);
        Assert.True(runtime.OnMain(updater.ConsumeRelaunchMarker));
        Assert.False(runtime.OnMain(updater.ConsumeRelaunchMarker));
        Assert.Null(runtime.Defaults.Object(key));

        runtime.Defaults.Set(key, "not-the-running-version");
        Assert.False(runtime.OnMain(updater.ConsumeRelaunchMarker));
        Assert.Null(runtime.Defaults.Object(key));
    }

    private static void Reset(UpdateInstallProgress progress, TestRuntime runtime)
    {
        runtime.OnMain(() =>
        {
            if (progress.HasPendingCelebration)
            {
                progress.ConsumePendingCelebration();
                progress.CompleteCelebration();
            }

            progress.Begin();
            progress.Fail();
        });
        runtime.AdvanceMilliseconds(600);
    }
}
