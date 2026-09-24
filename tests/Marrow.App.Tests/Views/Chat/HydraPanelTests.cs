using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

public sealed class HydraPanelTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Req", "PR-HYDRA-170")] // App/Views/Chat/HydraPanel.swift:33-37
    public void Strip_geometry_uses_the_source_height_and_corner_radius()
    {
        Assert.Equal(50, HydraPanel.StripHeight);
        Assert.Equal(22, HydraPanel.CornerRadius);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:41
    public void Finish_hold_is_two_seconds()
    {
        Assert.Equal(2.0, HydraPanel.FinishHold);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-179")] // App/Views/Chat/HydraPanel.swift:482-488
    public void Shown_returns_the_stored_record_when_the_head_has_no_runtime()
    {
        using var runtime = new TestRuntime();
        var stored = Info(HydraHeadInfo.Status.Running) with { Activity = "stored activity", ToolCalls = 3 };

        Assert.Same(stored, HydraLiveInfo.Shown(stored, null));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:493-500
    public void Short_status_is_the_exact_lowercase_word_for_each_status()
    {
        using var runtime = new TestRuntime();

        Assert.Equal("working", HydraStatusText.Short(Info(HydraHeadInfo.Status.Running)));
        Assert.Equal("done", HydraStatusText.Short(Info(HydraHeadInfo.Status.Completed)));
        Assert.Equal("failed", HydraStatusText.Short(Info(HydraHeadInfo.Status.Failed)));
        Assert.Equal("stopped", HydraStatusText.Short(Info(HydraHeadInfo.Status.Stopped)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:502-513
    public void Long_status_uses_the_middle_dot_and_running_fallback()
    {
        using var runtime = new TestRuntime();

        Assert.Equal("· working", HydraStatusText.Long(Info(HydraHeadInfo.Status.Running)));
        Assert.Equal("· working", HydraStatusText.Long(Info(HydraHeadInfo.Status.Running) with { Activity = null }));
        Assert.Equal("· failed", HydraStatusText.Long(Info(HydraHeadInfo.Status.Failed)));
        Assert.Equal("· stopped", HydraStatusText.Long(Info(HydraHeadInfo.Status.Stopped)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:504-505
    public void Long_running_status_truncates_activity_to_forty_characters()
    {
        using var runtime = new TestRuntime();
        var activity = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNO";

        Assert.Equal("· " + activity[..40], HydraStatusText.Long(
            Info(HydraHeadInfo.Status.Running) with { Activity = activity }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:507, 542-552
    public void Completed_long_status_includes_elapsed_steps_and_landing_in_source_order()
    {
        using var runtime = new TestRuntime();
        var info = Info(HydraHeadInfo.Status.Completed) with
        {
            FinishedAt = StartedAt.AddSeconds(61),
            ToolCalls = 1,
            Landing = new HydraLanding()
        };

        Assert.Equal("· done in 1m 1s · 1 step · changed nothing", HydraStatusText.Long(info));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:547-552
    public void Elapsed_clause_is_omitted_without_a_whole_positive_second()
    {
        using var runtime = new TestRuntime();
        var completed = Info(HydraHeadInfo.Status.Completed);

        Assert.Equal("· done", HydraStatusText.Long(completed with { FinishedAt = StartedAt }));
        Assert.Equal("· done", HydraStatusText.Long(completed with { FinishedAt = StartedAt.AddMilliseconds(999) }));
        Assert.Equal("· done in 1s", HydraStatusText.Long(completed with { FinishedAt = StartedAt.AddMilliseconds(1999) }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:515-526
    public void Footer_uses_exact_running_fallback_and_finished_wording()
    {
        using var runtime = new TestRuntime();

        Assert.Equal("Working", HydraStatusText.Footer(Info(HydraHeadInfo.Status.Running) with { Activity = null }));
        Assert.Equal("Done", HydraStatusText.Footer(Info(HydraHeadInfo.Status.Completed)));
        Assert.Equal("Failed", HydraStatusText.Footer(Info(HydraHeadInfo.Status.Failed)));
        Assert.Equal("Stopped", HydraStatusText.Footer(Info(HydraHeadInfo.Status.Stopped)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:517-518
    public void Footer_running_status_truncates_activity_to_eighty_characters()
    {
        using var runtime = new TestRuntime();
        var activity = new string('a', 81);

        Assert.Equal(new string('a', 80), HydraStatusText.Footer(
            Info(HydraHeadInfo.Status.Running) with { Activity = activity }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:520-522, 542-545
    public void Footer_appends_summary_and_steps_only_when_present()
    {
        using var runtime = new TestRuntime();
        var completed = Info(HydraHeadInfo.Status.Completed) with
        {
            ToolCalls = 2,
            Summary = "Summary"
        };
        var failed = Info(HydraHeadInfo.Status.Failed) with { Summary = "Summary" };

        Assert.Equal("Done · 2 steps · Summary", HydraStatusText.Footer(completed));
        Assert.Equal("Failed · Summary", HydraStatusText.Footer(failed));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-180")] // App/Views/Chat/HydraPanel.swift:520-522
    public void Footer_truncates_finished_summary_to_eighty_characters()
    {
        using var runtime = new TestRuntime();
        var summary = new string('s', 81);

        Assert.Equal("Done · " + new string('s', 80), HydraStatusText.Footer(
            Info(HydraHeadInfo.Status.Completed) with { Summary = summary }));
        Assert.Equal("Failed · " + new string('s', 80), HydraStatusText.Footer(
            Info(HydraHeadInfo.Status.Failed) with { Summary = summary }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // App/Views/Chat/HydraPanel.swift:530-539
    public void Landed_is_nil_without_a_landing_and_changed_nothing_for_an_empty_landing()
    {
        using var runtime = new TestRuntime();

        Assert.Null(HydraStatusText.Landed(Info(HydraHeadInfo.Status.Completed)));
        Assert.Equal("changed nothing", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = new HydraLanding() }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // App/Views/Chat/HydraPanel.swift:532-539
    public void Landed_reports_patch_before_error()
    {
        using var runtime = new TestRuntime();
        var landing = new HydraLanding { PatchPath = "changes.patch", Error = "merge failed" };

        Assert.Equal("kept as a patch", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = landing }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // App/Views/Chat/HydraPanel.swift:533-539
    public void Landed_reports_error_before_conflicts_and_uses_all_files_in_conflict_count()
    {
        using var runtime = new TestRuntime();
        var files = new ValueList<HydraLanding.File>(
        [
            new HydraLanding.File { Path = "one.txt", Additions = 1, Deletions = 0 },
            new HydraLanding.File { Path = "two.txt", Additions = 2, Deletions = 0 },
        ]);
        var conflicts = new ValueList<string>(["one.txt"]);
        var conflictLanding = new HydraLanding { Files = files, Conflicts = conflicts };
        var errorLanding = conflictLanding with { Error = "merge failed" };

        Assert.Equal("did not land", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = errorLanding }));
        Assert.Equal("landed 2 files, 1 conflict", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = conflictLanding }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-181")] // App/Views/Chat/HydraPanel.swift:533-539
    public void Landed_uses_singular_and_plural_file_wording()
    {
        using var runtime = new TestRuntime();
        var oneFile = new HydraLanding
        {
            Files = new ValueList<HydraLanding.File>(
            [
                new HydraLanding.File { Path = "one.txt", Additions = 1, Deletions = 0 },
            ])
        };
        var twoFiles = oneFile with
        {
            Files = new ValueList<HydraLanding.File>(
            [
                new HydraLanding.File { Path = "one.txt", Additions = 1, Deletions = 0 },
                new HydraLanding.File { Path = "two.txt", Additions = 2, Deletions = 0 },
            ])
        };

        Assert.Equal("landed 1 file", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = oneFile }));
        Assert.Equal("landed 2 files", HydraStatusText.Landed(
            Info(HydraHeadInfo.Status.Completed) with { Landing = twoFiles }));
    }

    private static HydraHeadInfo Info(HydraHeadInfo.Status status) =>
        new(0, "", default, default)
        {
            HydraHeadInfoStatus = status,
            StartedAt = StartedAt
        };
}
