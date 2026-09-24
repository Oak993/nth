using Marrow.App.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.App.Tests;

// Tests for App/AppUpdater.swift (B2 1fbf81e), part 1: UpdateProgressMilestones :8-20 and UpdateInstallProgress
// :27-226. Every expected value is transcribed from the Swift: the literals of :10-19, the direct assignments
// of :92, :164 and :184, the mapping of :110-112, the creep step of :212-217 (`fraction + remaining * creepRate`
// with `remaining = creepCeiling - fraction`, evaluated here in the same order, so the Doubles are bit-equal),
// the 125 ms creep sleep of :210 and the 600 ms failure window of :145.
// Not asserted, because nothing on Windows can observe it: the Haptics.perform calls of :93, :143 and :199-201
// (port_impacts.md X6 and G16: one no-op shim at the same call sites; Windows has no desktop haptic channel).
// AppUpdater's install orchestration :237-443, UpdateInstallError :445-452 and the Downloader :459-528
// (PR-UPDATE-041 to PR-UPDATE-055) are covered in AppUpdaterInstallTests and WindowsUpdateInstallBoundaryTests,
// over the ED-188 boundary (IUpdateInstallBoundary / FakeUpdateInstallBoundary). PR-UPDATE-057/058 (the alert
// and the install.sh/install.ps1 script content) are part 2's own records, not this part's.
public sealed class AppUpdaterTests
{
    private const double PreparingCeiling = 0.05; // :10
    private const double DownloadEnd = 0.55;      // :11
    private const double VerifyEnd = 0.70;        // :12
    private const double StagingCeiling = 0.82;   // :14
    private const double HandoffEnd = 0.86;       // :16
    private const double InstallDot = 0.96;       // :18

    // ---- PR-UPDATE-031: the milestones -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-031")] // App/AppUpdater.swift:8-18
    public void The_milestones_are_the_six_literal_fractions()
    {
        Assert.Equal(0.05, UpdateProgressMilestones.PreparingCeiling);
        Assert.Equal(0.55, UpdateProgressMilestones.DownloadEnd);
        Assert.Equal(0.70, UpdateProgressMilestones.VerifyEnd);
        Assert.Equal(0.82, UpdateProgressMilestones.StagingCeiling);
        Assert.Equal(0.86, UpdateProgressMilestones.HandoffEnd);
        Assert.Equal(0.96, UpdateProgressMilestones.InstallDot);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-031")] // App/AppUpdater.swift:19
    public void The_tick_dots_are_download_end_verify_end_handoff_end_and_install_dot_in_that_order()
    {
        Assert.Equal(new[] { 0.55, 0.70, 0.86, 0.96 }, UpdateProgressMilestones.Dots.ToArray());
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-031")] // App/AppUpdater.swift:158-170
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:193-197
    public void The_celebration_sweep_takes_the_track_from_the_handoff_dot_to_exactly_one()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.ArmCelebration);
        Assert.True(runtime.OnMain(progress.ConsumePendingCelebration));
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, progress));
        Assert.Equal(HandoffEnd, FractionOf(runtime, progress));

        runtime.OnMain(progress.FinishCelebrationSweep);
        Assert.Equal(1.0, FractionOf(runtime, progress));
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, progress));
    }

    // ---- PR-UPDATE-032: phases and predicates --------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-032")] // App/AppUpdater.swift:40-55
    public void Phase_has_the_eight_cases_in_source_order()
    {
        Assert.Equal(
            new[] { "Idle", "Preparing", "Downloading", "Verifying", "Staging", "Handoff", "Celebrating", "Failed" },
            Enum.GetNames<UpdateInstallProgress.Phase>());
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-032")] // App/AppUpdater.swift:57, :63, :70
    public void A_new_progress_is_idle_at_zero_with_no_pending_celebration()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
        Assert.False(runtime.OnMain(() => progress.HasPendingCelebration));
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-032")] // App/AppUpdater.swift:72-85
    [InlineData(UpdateInstallProgress.Phase.Idle, false, false)]
    [InlineData(UpdateInstallProgress.Phase.Preparing, true, true)]
    [InlineData(UpdateInstallProgress.Phase.Downloading, true, true)]
    [InlineData(UpdateInstallProgress.Phase.Verifying, true, true)]
    [InlineData(UpdateInstallProgress.Phase.Staging, true, true)]
    [InlineData(UpdateInstallProgress.Phase.Handoff, true, true)]
    [InlineData(UpdateInstallProgress.Phase.Celebrating, true, false)]
    [InlineData(UpdateInstallProgress.Phase.Failed, false, false)]
    public void ShowsProgressUI_and_IsInstallRunning_follow_the_phase(UpdateInstallProgress.Phase phase, bool showsProgressUI, bool isInstallRunning)
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        DriveTo(runtime, progress, phase);
        Assert.Equal(phase, PhaseOf(runtime, progress));
        Assert.Equal(showsProgressUI, runtime.OnMain(() => progress.ShowsProgressUI));
        Assert.Equal(isInstallRunning, runtime.OnMain(() => progress.IsInstallRunning));
    }

    // ---- PR-UPDATE-033: begin() ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-033")] // App/AppUpdater.swift:89-92
    public void Begin_enters_preparing_with_the_raw_target_at_exactly_0_004()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        Assert.Equal(UpdateInstallProgress.Phase.Preparing, PhaseOf(runtime, progress));
        Assert.Equal(0.004, runtime.OnMain(() => progress.UpdateInstallProgressFractionTarget.Value));
        Assert.Equal(0.004, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-033")] // App/AppUpdater.swift:94
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:208-218
    public void Begin_starts_a_creep_toward_0_05_at_rate_0_05()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        var first = Step(0.004, PreparingCeiling, 0.05);
        var second = Step(first, PreparingCeiling, 0.05);

        runtime.AdvanceMilliseconds(124);
        Assert.Equal(0.004, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(1);
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(second, FractionOf(runtime, progress));
        Assert.Equal(UpdateInstallProgress.Phase.Preparing, PhaseOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-033")] // App/AppUpdater.swift:92
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:195
    public void Begin_after_a_finished_sweep_sets_0_004_directly_although_it_is_below_the_current_fraction()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.ArmCelebration);
        runtime.OnMain(() => progress.ConsumePendingCelebration());
        runtime.OnMain(progress.FinishCelebrationSweep);
        Assert.Equal(1.0, FractionOf(runtime, progress));

        runtime.OnMain(progress.Begin);
        Assert.Equal(UpdateInstallProgress.Phase.Preparing, PhaseOf(runtime, progress));
        Assert.Equal(0.004, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-033")] // App/AppUpdater.swift:90
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:144-148, :187-190
    public void Begin_cancels_a_pending_failure_reset()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(progress.Fail);                  // t = 0: the reset would land at t = 600
        runtime.AdvanceMilliseconds(300);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));

        runtime.OnMain(progress.Begin);                 // t = 300: a new creep ticks at 425, 550, 675
        runtime.AdvanceMilliseconds(300);               // t = 600
        Assert.Equal(UpdateInstallProgress.Phase.Preparing, PhaseOf(runtime, progress));
        var twoTicks = Step(Step(0.004, PreparingCeiling, 0.05), PreparingCeiling, 0.05);
        Assert.Equal(twoTicks, FractionOf(runtime, progress));

        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Preparing, PhaseOf(runtime, progress));
    }

    // ---- PR-UPDATE-034: byte progress ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:100-112
    public void The_first_byte_report_moves_preparing_to_downloading_and_maps_linearly_onto_0_05_to_0_55()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, PhaseOf(runtime, progress));
        Assert.Equal(0.05 + (0.55 - 0.05) * 0.5, FractionOf(runtime, progress));

        runtime.OnMain(() => progress.NoteDownloadProgress(0.75));
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, PhaseOf(runtime, progress));
        Assert.Equal(0.05 + (0.55 - 0.05) * 0.75, FractionOf(runtime, progress));
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:112 min(downloadFraction, 1)
    [InlineData(1.0)]
    [InlineData(2.5)]
    public void A_complete_or_overshooting_download_lands_exactly_on_0_55(double downloadFraction)
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(downloadFraction));
        Assert.Equal(0.05 + (0.55 - 0.05) * 1.0, FractionOf(runtime, progress));
        Assert.Equal(0.55, FractionOf(runtime, progress));
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:105-108
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:205-207
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void An_unknown_size_report_retargets_the_running_creep_toward_0_50_at_rate_0_012(double downloadFraction)
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);                                   // creep toward 0.05, first tick at t = 125
        runtime.AdvanceMilliseconds(60);
        runtime.OnMain(() => progress.NoteDownloadProgress(downloadFraction));
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, PhaseOf(runtime, progress));
        Assert.Equal(0.004, FractionOf(runtime, progress));

        var first = Step(0.004, 0.50, 0.012);
        runtime.AdvanceMilliseconds(65);                                  // t = 125: the same task, new target
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(60);                                  // t = 185: no second task ticks here
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(65);                                  // t = 250
        Assert.Equal(Step(first, 0.50, 0.012), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:106, :212-216
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:213-216
    public void An_unknown_size_download_creeps_toward_0_50_and_never_reaches_the_download_end_dot()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0));
        var (expected, applies) = CreepToRest(0.004, 0.50, 0.012);

        runtime.AdvanceMilliseconds(125.0 * (applies + 1));
        Assert.Equal(expected, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(60_000);
        Assert.Equal(expected, FractionOf(runtime, progress));
        Assert.True(expected < 0.50);
        Assert.True(expected < DownloadEnd);
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, PhaseOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:109
    public void A_positive_report_stops_the_creep()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0));
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(Step(0.004, 0.50, 0.012), FractionOf(runtime, progress));

        runtime.OnMain(() => progress.NoteDownloadProgress(0.1));
        var mapped = 0.05 + (0.55 - 0.05) * 0.1;
        Assert.Equal(mapped, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(5000);
        Assert.Equal(mapped, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:100-104
    public void A_byte_report_while_idle_changes_nothing()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        runtime.OnMain(() => progress.NoteDownloadProgress(0));
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:100-104
    public void A_byte_report_after_a_failure_changes_nothing()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(progress.Fail);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));
        Assert.Equal(0.004, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:100-104
    public void A_byte_report_while_verifying_leaves_the_phase_and_the_verification_creep_alone()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        runtime.OnMain(progress.BeginVerification);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.9));
        Assert.Equal(UpdateInstallProgress.Phase.Verifying, PhaseOf(runtime, progress));
        Assert.Equal(DownloadEnd, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(Step(0.55, 0.70 - 0.02, 0.12), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-034")] // App/AppUpdater.swift:100-104
    public void A_byte_report_while_celebrating_changes_nothing()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.ArmCelebration);
        runtime.OnMain(() => progress.ConsumePendingCelebration());
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, progress));
        Assert.Equal(HandoffEnd, FractionOf(runtime, progress));
    }

    // ---- PR-UPDATE-035: beginVerification() -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-035")] // App/AppUpdater.swift:115-121
    public void BeginVerification_lands_on_0_55_and_creeps_toward_0_68_at_rate_0_12()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        runtime.OnMain(progress.BeginVerification);
        Assert.Equal(UpdateInstallProgress.Phase.Verifying, PhaseOf(runtime, progress));
        Assert.Equal(0.55, FractionOf(runtime, progress));

        var first = Step(0.55, 0.70 - 0.02, 0.12);
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(Step(first, 0.70 - 0.02, 0.12), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-035")] // App/AppUpdater.swift:117, :120
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:222-225
    public void BeginVerification_stops_the_running_creep_and_starts_its_own_on_a_fresh_125_ms_schedule()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);                          // old task's first tick due at t = 125
        runtime.AdvanceMilliseconds(60);
        runtime.OnMain(() => progress.NoteDownloadProgress(0));  // same old task, retargeted
        runtime.OnMain(progress.BeginVerification);              // t = 60: old task cancelled, new one ticks at 185
        Assert.Equal(0.55, FractionOf(runtime, progress));

        runtime.AdvanceMilliseconds(65);                         // t = 125
        Assert.Equal(0.55, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(59);                         // t = 184
        Assert.Equal(0.55, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(1);                          // t = 185
        Assert.Equal(Step(0.55, 0.70 - 0.02, 0.12), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-035")] // App/AppUpdater.swift:120
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:212-216
    public void The_verification_creep_comes_to_rest_short_of_the_verify_dot()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(1));
        runtime.OnMain(progress.BeginVerification);
        var (expected, applies) = CreepToRest(0.55, 0.70 - 0.02, 0.12);

        runtime.AdvanceMilliseconds(125.0 * (applies + 1) + 30_000);
        Assert.Equal(expected, FractionOf(runtime, progress));
        Assert.True(expected < 0.70 - 0.02);
        Assert.True(expected < VerifyEnd);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-035")] // App/AppUpdater.swift:116
    public void BeginVerification_outside_an_install_does_nothing()
    {
        using var runtime = new TestRuntime();

        var idle = NewProgress(runtime);
        runtime.OnMain(idle.BeginVerification);
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, idle));
        Assert.Equal(0.0, FractionOf(runtime, idle));

        var failed = NewProgress(runtime);
        runtime.OnMain(failed.Begin);
        runtime.OnMain(failed.Fail);
        runtime.OnMain(failed.BeginVerification);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, failed));
        Assert.Equal(0.004, FractionOf(runtime, failed));

        var celebrating = NewProgress(runtime);
        runtime.OnMain(celebrating.ArmCelebration);
        runtime.OnMain(() => celebrating.ConsumePendingCelebration());
        runtime.OnMain(celebrating.BeginVerification);
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, celebrating));
        Assert.Equal(HandoffEnd, FractionOf(runtime, celebrating));
    }

    // ---- PR-UPDATE-036: beginStaging() ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-036")] // App/AppUpdater.swift:123-129
    public void BeginStaging_lands_on_0_70_and_creeps_toward_0_82_at_rate_0_10()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(1));
        runtime.OnMain(progress.BeginVerification);
        runtime.AdvanceMilliseconds(60);                         // verification creep's first tick is at t = 125
        runtime.OnMain(progress.BeginStaging);
        Assert.Equal(UpdateInstallProgress.Phase.Staging, PhaseOf(runtime, progress));
        Assert.Equal(0.70, FractionOf(runtime, progress));

        var first = Step(0.70, StagingCeiling, 0.10);
        runtime.AdvanceMilliseconds(65);                         // t = 125: the verification creep was stopped
        Assert.Equal(0.70, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(60);                         // t = 185: the staging creep's first tick
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(125);
        Assert.Equal(Step(first, StagingCeiling, 0.10), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-036")] // App/AppUpdater.swift:128
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:212-216
    public void The_staging_creep_comes_to_rest_short_of_its_ceiling()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(1));
        runtime.OnMain(progress.BeginVerification);
        runtime.OnMain(progress.BeginStaging);
        var (expected, applies) = CreepToRest(0.70, StagingCeiling, 0.10);

        runtime.AdvanceMilliseconds(125.0 * (applies + 1) + 30_000);
        Assert.Equal(expected, FractionOf(runtime, progress));
        Assert.True(expected < StagingCeiling);
        Assert.True(StagingCeiling - expected <= 0.0005);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-036")] // App/AppUpdater.swift:124
    public void BeginStaging_outside_an_install_does_nothing()
    {
        using var runtime = new TestRuntime();

        var idle = NewProgress(runtime);
        runtime.OnMain(idle.BeginStaging);
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, idle));
        Assert.Equal(0.0, FractionOf(runtime, idle));

        var celebrating = NewProgress(runtime);
        runtime.OnMain(celebrating.ArmCelebration);
        runtime.OnMain(() => celebrating.ConsumePendingCelebration());
        runtime.OnMain(celebrating.BeginStaging);
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, celebrating));
        Assert.Equal(HandoffEnd, FractionOf(runtime, celebrating));
    }

    // ---- PR-UPDATE-037: noteHandoff() -------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-037")] // App/AppUpdater.swift:131-136
    public void NoteHandoff_lands_on_0_86_and_leaves_no_creep_running()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(1));
        runtime.OnMain(progress.BeginVerification);
        runtime.OnMain(progress.BeginStaging);
        runtime.AdvanceMilliseconds(250);
        Assert.True(FractionOf(runtime, progress) > 0.70);

        runtime.OnMain(progress.NoteHandoff);
        Assert.Equal(UpdateInstallProgress.Phase.Handoff, PhaseOf(runtime, progress));
        Assert.Equal(0.86, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(10_000);
        Assert.Equal(0.86, FractionOf(runtime, progress));
        Assert.Equal(UpdateInstallProgress.Phase.Handoff, PhaseOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-037")] // App/AppUpdater.swift:132
    public void NoteHandoff_outside_an_install_does_nothing()
    {
        using var runtime = new TestRuntime();

        var idle = NewProgress(runtime);
        runtime.OnMain(idle.NoteHandoff);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, idle));
        Assert.Equal(0.0, FractionOf(runtime, idle));

        var failed = NewProgress(runtime);
        runtime.OnMain(failed.Begin);
        runtime.OnMain(failed.Fail);
        runtime.OnMain(failed.NoteHandoff);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, failed));
        Assert.Equal(0.004, FractionOf(runtime, failed));
    }

    // ---- PR-UPDATE-038: fail() ---------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:139-148, :180-185
    public void Fail_shows_the_failed_phase_for_600_ms_then_resets_to_idle_at_zero()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.AdvanceMilliseconds(125);
        var atFailure = Step(0.004, PreparingCeiling, 0.05);
        Assert.Equal(atFailure, FractionOf(runtime, progress));

        runtime.OnMain(progress.Fail);                           // t = 125
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));
        Assert.False(runtime.OnMain(() => progress.ShowsProgressUI));
        Assert.False(runtime.OnMain(() => progress.IsInstallRunning));

        runtime.AdvanceMilliseconds(599);                        // t = 724: the creep was stopped, the head holds
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));
        Assert.Equal(atFailure, FractionOf(runtime, progress));

        runtime.AdvanceMilliseconds(1);                          // t = 725
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
        Assert.Equal(0.0, runtime.OnMain(() => progress.UpdateInstallProgressFractionTarget.Value));

        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:139-148
    [InlineData(UpdateInstallProgress.Phase.Downloading)]
    [InlineData(UpdateInstallProgress.Phase.Verifying)]
    [InlineData(UpdateInstallProgress.Phase.Staging)]
    [InlineData(UpdateInstallProgress.Phase.Handoff)]
    public void Fail_from_any_running_phase_enters_failed_and_resets_600_ms_later(UpdateInstallProgress.Phase from)
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        DriveTo(runtime, progress, from);
        runtime.OnMain(progress.Fail);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));
        runtime.AdvanceMilliseconds(599);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, PhaseOf(runtime, progress));
        runtime.AdvanceMilliseconds(1);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:140
    public void Fail_outside_an_install_does_nothing()
    {
        using var runtime = new TestRuntime();

        var idle = NewProgress(runtime);
        runtime.OnMain(idle.Fail);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, idle));

        var celebrating = NewProgress(runtime);
        runtime.OnMain(celebrating.ArmCelebration);
        runtime.OnMain(() => celebrating.ConsumePendingCelebration());
        runtime.OnMain(celebrating.Fail);
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, celebrating));
        runtime.AdvanceMilliseconds(1000);
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, celebrating));
        Assert.Equal(HandoffEnd, FractionOf(runtime, celebrating));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-038")] // App/AppUpdater.swift:146, :162
    public void A_failure_reset_is_skipped_when_a_celebration_took_over_in_the_600_ms()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(progress.Fail);                           // t = 0
        runtime.AdvanceMilliseconds(300);
        runtime.OnMain(progress.ArmCelebration);
        Assert.True(runtime.OnMain(progress.ConsumePendingCelebration));
        runtime.AdvanceMilliseconds(700);                        // t = 1000, past the 600 ms mark
        Assert.Equal(UpdateInstallProgress.Phase.Celebrating, PhaseOf(runtime, progress));
        Assert.Equal(HandoffEnd, FractionOf(runtime, progress));
    }

    // ---- PR-UPDATE-039: forward only --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:193-197
    public void A_lower_or_equal_value_never_moves_the_head()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        var high = 0.05 + (0.55 - 0.05) * 0.5;
        Assert.Equal(high, FractionOf(runtime, progress));

        runtime.OnMain(() => progress.NoteDownloadProgress(0.2));
        Assert.Equal(high, FractionOf(runtime, progress));
        runtime.OnMain(() => progress.NoteDownloadProgress(0.5));
        Assert.Equal(high, FractionOf(runtime, progress));
        runtime.OnMain(() => progress.NoteDownloadProgress(0.6));
        Assert.Equal(0.05 + (0.55 - 0.05) * 0.6, FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:193-197
    public void Every_step_of_an_install_moves_the_head_forward_onto_its_milestone()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        Assert.Equal(0.004, FractionOf(runtime, progress));
        runtime.OnMain(() => progress.NoteDownloadProgress(1));
        Assert.Equal(DownloadEnd, FractionOf(runtime, progress));
        runtime.OnMain(progress.BeginVerification);
        Assert.Equal(DownloadEnd, FractionOf(runtime, progress));  // equal value: no change
        runtime.OnMain(progress.BeginStaging);
        Assert.Equal(VerifyEnd, FractionOf(runtime, progress));
        runtime.OnMain(progress.NoteHandoff);
        Assert.Equal(HandoffEnd, FractionOf(runtime, progress));
        Assert.True(HandoffEnd < InstallDot);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-039")] // App/AppUpdater.swift:173-176, :180-185
    public void Completing_the_celebration_resets_the_raw_target_directly_to_zero()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.ArmCelebration);
        runtime.OnMain(() => progress.ConsumePendingCelebration());
        runtime.OnMain(progress.FinishCelebrationSweep);
        Assert.Equal(1.0, FractionOf(runtime, progress));

        runtime.OnMain(progress.CompleteCelebration);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, PhaseOf(runtime, progress));
        Assert.Equal(0.0, FractionOf(runtime, progress));
    }

    // ---- PR-UPDATE-040: the creep ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:204-207
    public void A_second_start_while_a_creep_runs_retargets_it_instead_of_starting_another()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);                          // t = 0: the task's ticks are at 125, 250, ...
        runtime.AdvanceMilliseconds(60);
        runtime.OnMain(progress.Begin);                          // t = 60: 0.004 again, same ceiling and rate
        Assert.Equal(0.004, FractionOf(runtime, progress));

        var first = Step(0.004, PreparingCeiling, 0.05);
        runtime.AdvanceMilliseconds(65);                         // t = 125
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(60);                         // t = 185: a second task would tick here
        Assert.Equal(first, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(65);                         // t = 250
        Assert.Equal(Step(first, PreparingCeiling, 0.05), FractionOf(runtime, progress));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-040")] // App/AppUpdater.swift:212-216
    public void A_creep_ends_and_clears_itself_once_the_gap_is_0_0005_or_less()
    {
        using var runtime = new TestRuntime();
        var progress = NewProgress(runtime);
        runtime.OnMain(progress.Begin);
        var (rest, applies) = CreepToRest(0.004, PreparingCeiling, 0.05);

        runtime.AdvanceMilliseconds(125.0 * applies);
        Assert.Equal(rest, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(125);                        // the tick that sees the small gap and ends
        Assert.Equal(rest, FractionOf(runtime, progress));
        Assert.True(PreparingCeiling - rest <= 0.0005);
        Assert.True(rest < PreparingCeiling);
        runtime.AdvanceMilliseconds(10_000);
        Assert.Equal(rest, FractionOf(runtime, progress));

        // The ended task set creepTask to nil, so the next start makes a new task that moves the head again.
        runtime.OnMain(() => progress.NoteDownloadProgress(0));
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, PhaseOf(runtime, progress));
        runtime.AdvanceMilliseconds(124);
        Assert.Equal(rest, FractionOf(runtime, progress));
        runtime.AdvanceMilliseconds(1);
        Assert.Equal(Step(rest, 0.50, 0.012), FractionOf(runtime, progress));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static UpdateInstallProgress NewProgress(TestRuntime runtime) =>
        runtime.OnMain(UpdateInstallProgressFixtures.NewProgress);

    private static UpdateInstallProgress.Phase PhaseOf(TestRuntime runtime, UpdateInstallProgress progress) =>
        runtime.OnMain(() => progress.UpdateInstallProgressPhase);

    private static double FractionOf(TestRuntime runtime, UpdateInstallProgress progress) =>
        runtime.OnMain(() => progress.Fraction);

    // App/AppUpdater.swift:212 and :217, in the Swift's evaluation order.
    private static double Step(double fraction, double ceiling, double rate)
    {
        var remaining = ceiling - fraction;
        return fraction + remaining * rate;
    }

    // App/AppUpdater.swift:209-218: where a creep from `start` comes to rest, and how many ticks applied a step.
    private static (double Final, int Applies) CreepToRest(double start, double ceiling, double rate)
    {
        var fraction = start;
        var applies = 0;
        while (ceiling - fraction > 0.0005)
        {
            fraction = Step(fraction, ceiling, rate);
            applies += 1;
        }
        return (fraction, applies);
    }

    // Reaches each phase through the calls the installer and the About page make (App/AppUpdater.swift:89-176).
    private static void DriveTo(TestRuntime runtime, UpdateInstallProgress progress, UpdateInstallProgress.Phase phase)
    {
        switch (phase)
        {
            case UpdateInstallProgress.Phase.Idle:
                return;
            case UpdateInstallProgress.Phase.Celebrating:
                runtime.OnMain(progress.ArmCelebration);
                runtime.OnMain(() => progress.ConsumePendingCelebration());
                return;
            case UpdateInstallProgress.Phase.Failed:
                runtime.OnMain(progress.Begin);
                runtime.OnMain(progress.Fail);
                return;
        }
        runtime.OnMain(progress.Begin);
        if (phase == UpdateInstallProgress.Phase.Preparing) return;
        runtime.OnMain(() => progress.NoteDownloadProgress(0.25));
        if (phase == UpdateInstallProgress.Phase.Downloading) return;
        runtime.OnMain(progress.BeginVerification);
        if (phase == UpdateInstallProgress.Phase.Verifying) return;
        runtime.OnMain(progress.BeginStaging);
        if (phase == UpdateInstallProgress.Phase.Staging) return;
        runtime.OnMain(progress.NoteHandoff);
    }
}
